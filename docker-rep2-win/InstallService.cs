using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Security.Cryptography;
using System.Windows;
using IniParser;
using IniParser.Model;
using Microsoft.Win32;

namespace docker_rep2_win
{
    public static class InstallService
    {
        public class InstallHooks
        {
            public Action? BeforeDeploy { get; set; }
        }

        private const string SetupScriptTemplate = """
            apk update
            #apk upgrade --no-cache
            apk add --no-cache docker docker-compose tzdata socat

            cp /usr/share/zoneinfo/Asia/Tokyo /etc/localtime
            echo "Asia/Tokyo" > /etc/timezone

            cat <<EOF > /etc/profile.d/00-env.sh
            export REP2_PORT={0}
            export REP2_DATA={1}
            export MONITOR_PORT={2}
            export TZ=Asia/Tokyo
            EOF

            cat <<'EOF' > /etc/rc.entry
            #!/bin/sh
            export PATH=/usr/sbin:/usr/bin:/sbin:/bin
            for s in /etc/profile.d/*.sh; do
              if [ -r "$s" ]; then
                . "$s"
              fi
            done

            dockerd --userland-proxy=false > /var/log/dockerd.log 2>&1 &
            socat TCP-LISTEN:$MONITOR_PORT,fork /dev/null &
            EOF
            chmod +x /etc/rc.entry

            printf '[boot]\ncommand=/etc/rc.entry\n' > /etc/wsl.conf

            addgroup root docker 2>/dev/null || true
            """;

        private const string ComposeRawUrl = "https://raw.githubusercontent.com/fukumen/docker-rep2/php8/docker-compose.yml";

        public delegate void ProgressHandler(double percentage, string status);

        // AppMode.Install
        public static async Task RunInstallAsync(ProgressHandler onProgress, InstallHooks? hooks = null, CancellationToken cancellationToken = default)
        {
            var settings = ((App)Application.Current).Settings;
            try
            {
                // シークレットキーを生成 (jsonが残っていたらその値を使う)
                if (string.IsNullOrEmpty(settings.User.SecretKey))
                {
                    byte[] keyBytes = RandomNumberGenerator.GetBytes(32);
                    settings.User.SecretKey = Convert.ToHexString(keyBytes).ToLower();
                }

                onProgress(5, "システム設定を構成中...");
                PrepareInstallDirectory(settings.InstallPath);
                PrepareDataDirectory(settings.DataPath);
                await ConfigureSystemAsync(settings, cancellationToken);

                onProgress(10, $"{AppInfo.DistroName}を構築中...");
                await BuildWslBaseAsync(settings, (p, s) => onProgress(10 + p * 0.5, s), cancellationToken); // 10% -> 60%

                onProgress(60, "設定ファイルを作成中...");
                await DeployAppConfigAsync(settings, hooks, cancellationToken);

                onProgress(70, "WSLをシャットダウン中...");
                await StartApplicationAsync(settings, (p, s) => onProgress(70 + p * 0.3, s), useShutdown: true, cancellationToken); // 70% -> 100%

                onProgress(95, "アンインストーラーを登録中...");
                UninstallService.Register();

                SetStartup(settings.User.AutoStart);

                SaveFinalSettings(settings);

                // スタートメニューを強制再起動
                foreach (var p in Process.GetProcessesByName("StartMenuExperienceHost"))
                {
                    try { p.Kill(); } catch { }
                }

                onProgress(100, "インストール完了！");
            }
            catch (Exception ex)
            {
                await CleanupAsync(settings);
                await CleanupUserSetupAsync(settings);
                if (ex is OperationCanceledException) throw;
                throw;
            }
        }

        // AppMode.UserSetup
        public static async Task RunUserSetupAsync(ProgressHandler onProgress, InstallHooks? hooks = null, CancellationToken cancellationToken = default)
        {
            var settings = ((App)Application.Current).Settings;
            try
            {
                // シークレットキーを生成
                if (string.IsNullOrEmpty(settings.User.SecretKey))
                {
                    byte[] keyBytes = RandomNumberGenerator.GetBytes(32);
                    settings.User.SecretKey = Convert.ToHexString(keyBytes).ToLower();
                }

                onProgress(5, "システム設定を構成中...");
                PrepareDataDirectory(settings.DataPath);
                await ConfigureWslConfigAsync(cancellationToken);

                onProgress(10, $"{AppInfo.DistroName}を構築中...");
                await BuildWslBaseAsync(settings, (p, s) => onProgress(15 + p * 0.45, s), cancellationToken); // 15% -> 60%

                onProgress(60, "設定ファイルを作成中...");
                await DeployAppConfigAsync(settings, hooks, cancellationToken);

                onProgress(70, "WSLをシャットダウン中...");
                await StartApplicationAsync(settings, (p, s) => onProgress(70 + p * 0.3, s), useShutdown: true, cancellationToken);

                SetStartup(settings.User.AutoStart);

                SaveFinalSettings(settings);

                onProgress(100, "セットアップ完了！");
            }
            catch (Exception ex)
            {
                await CleanupUserSetupAsync(settings);
                if (ex is OperationCanceledException) throw;
                throw;
            }
        }

        // AppMode.Config
        public static async Task RunConfigAsync(ProgressHandler onProgress, InstallHooks? hooks = null, CancellationToken cancellationToken = default)
        {
            var settings = ((App)Application.Current).Settings;

            try
            {
                // ここに来る時は常にWSLの再起動や再構築を伴う変更がある
                double startAppPercent = 70;
                double startAppScale = 0.3;

                if (settings.User.VersionChanged)
                {
                    onProgress(5, $"{AppInfo.DistroName}を再構築中...");
                    await BuildWslBaseAsync(settings, (p, s) => onProgress(5 + p * 0.6, s), cancellationToken); // 5% -> 65%
                    onProgress(65, "設定ファイルを更新中...");
                }
                else
                {
                    onProgress(5, "設定ファイルを更新中...");
                    if (settings.User.MonitorPortChanged)
                    {
                        ((App)Application.Current).UpdateMonitorPort(settings.User.MonitorPort); 
                        string script = string.Format(SetupScriptTemplate, settings.Port, WslService.ConvertToWslPath(settings.DataPath), settings.User.MonitorPort);
                        await WslService.ExecuteScriptAsync(script, cancellationToken);
                    }
                    startAppPercent = 10;
                    startAppScale = 0.9;
                }

                await DeployAppConfigAsync(settings, hooks, cancellationToken);

                onProgress(startAppPercent, $"{AppInfo.DistroName}を停止中...");
                await StartApplicationAsync(settings, (p, s) => onProgress(startAppPercent + p * startAppScale, s), useShutdown: false, cancellationToken);

                SetStartup(settings.User.AutoStart);

                SaveFinalSettings(settings);

                onProgress(100, "設定の変更が完了しました！");
            }
            catch (Exception ex)
            {
                try { WslService.TerminateDistro(); } catch { }
                if (ex is OperationCanceledException) throw;
                throw;
            }
        }

        // AppMode.Update
        public static async Task RunUpdateAsync(ProgressHandler onProgress, InstallHooks? hooks = null, CancellationToken cancellationToken = default)
        {
            var settings = ((App)Application.Current).Settings;
            try
            {
                onProgress(5, "バイナリを更新中...");
                CopySelfAndCreateShortcuts(settings);

                onProgress(20, "設定ファイルを更新中...");
                await DeployAppConfigAsync(settings, hooks, cancellationToken);

                onProgress(40, $"{AppInfo.DistroName}をセットアップ中...");
                ((App)Application.Current).UpdateMonitorPort(settings.User.MonitorPort); 
                string script = string.Format(SetupScriptTemplate, settings.Port, WslService.ConvertToWslPath(settings.DataPath), settings.User.MonitorPort);
                await WslService.ExecuteScriptAsync(script, cancellationToken);

                onProgress(60, "アンインストーラー情報を更新中...");
                UninstallService.Register();

                SetStartup(settings.User.AutoStart);

                SaveFinalSettings(settings);

                onProgress(80, "WSLを再起動中...");
                await StartApplicationAsync(settings, (p, s) => onProgress(80 + p * 0.2, s), useShutdown: false, cancellationToken);

                // スタートメニューを強制再起動
                foreach (var p in Process.GetProcessesByName("StartMenuExperienceHost"))
                {
                    try { p.Kill(); } catch { }
                }

                onProgress(100, "更新完了！");
            }
            catch (Exception ex)
            {
                try { WslService.TerminateDistro(); } catch { }
                if (ex is OperationCanceledException) throw;
                throw;
            }
        }

        private static async Task ConfigureSystemAsync(AppSettings settings, CancellationToken cancellationToken)
        {
            await ConfigureWslConfigAsync(cancellationToken);
            if (settings.OpenFirewall)
            {
                FirewallService.OpenPort(settings.Port);
            }
            else
            {
                FirewallService.RemovePortRule();
            }
            CopySelfAndCreateShortcuts(settings);
        }

        private static async Task BuildWslBaseAsync(AppSettings settings, ProgressHandler onProgress, CancellationToken cancellationToken)
        {
            string tarPath = Path.Combine(Path.GetTempPath(), "alpine-rootfs.tar.gz");
            
            try
            {
                await DownloadFileAsync(settings.DownloadUrl, tarPath, (p, s) => onProgress(p * 0.3, s), cancellationToken); // 0% -> 30%

                onProgress(30, "ダウンロードしたファイルの整合性を確認中...");
                await VerifyFileHashAsync(tarPath, settings, cancellationToken);

                onProgress(35, "Alpine Linuxをインポート中...");
                await Task.Run(() => WslService.UnregisterDistro());

                await WslService.ImportDistroAsync(tarPath);
            }
            finally
            {
                if (File.Exists(tarPath))
                {
                    try { File.Delete(tarPath); } catch { }
                }
            }

            onProgress(60, $"{AppInfo.DistroName}をセットアップ中...");
            ((App)Application.Current).UpdateMonitorPort(settings.User.MonitorPort); 
            string script = string.Format(SetupScriptTemplate, settings.Port, WslService.ConvertToWslPath(settings.DataPath), settings.User.MonitorPort);
            await WslService.ExecuteScriptAsync(script, cancellationToken);
        }

        private static async Task VerifyFileHashAsync(string filePath, AppSettings settings, CancellationToken cancellationToken)
        {
            string expectedHash = settings.SelectedHash;

            bool isArchMismatch = !IsSameArchitecture(settings.DownloadUrl, settings.ManifestDownloadUrl);

            if (string.IsNullOrEmpty(expectedHash) || isArchMismatch)
            {
                using var client = new HttpClient();
                string hashUrl = $"{settings.DownloadUrl}.sha512";
                string hashContent = await client.GetStringAsync(hashUrl, cancellationToken);
                expectedHash = hashContent.Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? string.Empty;
            }

            if (string.IsNullOrEmpty(expectedHash)) return;

            using var sha512 = System.Security.Cryptography.SHA512.Create();
            using var stream = File.OpenRead(filePath);
            byte[] hashBytes = await sha512.ComputeHashAsync(stream, cancellationToken);
            string actualHash = BitConverter.ToString(hashBytes).Replace("-", "").ToLower();

            if (!string.Equals(actualHash, expectedHash.Trim().ToLower(), StringComparison.OrdinalIgnoreCase))
            {
                string msg = $"ハッシュ検証に失敗しました。\n\n" +
                             $"[デバッグ情報]\n" +
                             $"ダウンロードURL: {settings.DownloadUrl}\n" +
                             $"マニフェスト基準URL: {settings.ManifestDownloadUrl}\n" +
                             $"期待値: {expectedHash}\n" +
                             $"実際の値: {actualHash}\n\n" +
                             $"ファイルが破損しているか、アーキテクチャが一致していない可能性があります。";
                throw new Exception(msg);
            }
        }

        private static bool IsSameArchitecture(string url1, string url2)
        {
            if (string.IsNullOrEmpty(url1) || string.IsNullOrEmpty(url2)) return false;

            string arch1 = GetArchName(url1);
            string arch2 = GetArchName(url2);

            return !string.IsNullOrEmpty(arch1) && arch1 == arch2;
        }

        private static string GetArchName(string url)
        {
            if (url.Contains("aarch64")) return "aarch64";
            if (url.Contains("x86_64")) return "x86_64";
            return string.Empty;
        }

        private static async Task DeployAppConfigAsync(AppSettings settings, InstallHooks? hooks, CancellationToken cancellationToken)
        {
            hooks?.BeforeDeploy?.Invoke();

            string appDataPath = settings.WindowsDataPath;
            if (string.IsNullOrEmpty(appDataPath)) return;

            // php-local.ini の作成
            string phpIniPath = Path.Combine(appDataPath, "php-local.ini");
            string phpIniContent = $"memory_limit = {settings.User.PhpMemoryLimit}\n";
            await File.WriteAllTextAsync(phpIniPath, phpIniContent, cancellationToken);

            string yamlContent;
            string localComposeFileRoot = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "docker-compose.yml");

            if (File.Exists(localComposeFileRoot))
            {
                // ローカルファイルがあればそれを使用
                yamlContent = await File.ReadAllTextAsync(localComposeFileRoot, cancellationToken);
            }
            else
            {
                // なければダウンロード
                using (var client = new HttpClient())
                {
                    yamlContent = await client.GetStringAsync(ComposeRawUrl, cancellationToken);
                }
            }

            string filePath = Path.Combine(appDataPath, "docker-compose.yml");
            await File.WriteAllTextAsync(filePath, yamlContent, cancellationToken);

            var sb = new System.Text.StringBuilder();
            sb.AppendLine("services:");
            sb.AppendLine("  rep2php8:");
            sb.AppendLine("    volumes:");
            sb.AppendLine("      - ./php-local.ini:/usr/local/etc/php/conf.d/z-php-local.ini");

            if (settings.User.EnableCertbot)
            {
                sb.AppendLine("      - ./certbot/conf:/etc/letsencrypt:ro");
            }

            sb.AppendLine("    environment:");
            sb.AppendLine($"      SECRET_KEY: \"{settings.User.SecretKey}\"");

            if (settings.User.EnableCertbot)
            {
                sb.AppendLine("  certbot:");
                sb.AppendLine("    image: rep2-certbot");
                sb.AppendLine("    pid: \"service:rep2php8\"");
                sb.AppendLine("    depends_on:");
                sb.AppendLine("      - rep2php8");
                sb.AppendLine("    entrypoint: [\"/bin/sh\", \"-c\", \"while true; do certbot renew --deploy-hook 'kill -USR1 $$(pidof caddy)'; sleep 86400; done\"]");
                sb.AppendLine("    build:");
                sb.AppendLine("      context: ./certbot");
                sb.AppendLine("    volumes:");
                sb.AppendLine("      - ./certbot/conf:/etc/letsencrypt");
                sb.AppendLine("      - ./certbot/logs:/var/log/letsencrypt");
                sb.AppendLine("      - ./certbot/certbot.ini:/certbot.ini");
            }

            string overridePath = Path.Combine(appDataPath, "docker-compose.override.yml");
            await File.WriteAllTextAsync(overridePath, sb.ToString(), cancellationToken);
        }

        private static async Task StartApplicationAsync(AppSettings settings, ProgressHandler onProgress, bool useShutdown, CancellationToken cancellationToken)
        {
            if (useShutdown)
            {
                await Task.Run(() => { WslService.Shutdown(); });
            }
            else
            {
                await Task.Run(() => { WslService.TerminateDistro(); });
            }

            onProgress(30, $"{AppInfo.DistroName}を起動中...");
            await DockerService.StartDockerdAsync(cancellationToken);

            onProgress(60, $"{AppInfo.DockerName}を起動中...");
            await DockerService.UpAsync(cancellationToken);
        }

        private static void PrepareInstallDirectory(string path)
        {
            if (Directory.Exists(path))
            {
                string currentExe = AppInfo.CurrentExePath;
                foreach (var file in Directory.GetFiles(path))
                {
                    if (!string.IsNullOrEmpty(currentExe) && string.Equals(Path.GetFullPath(file), Path.GetFullPath(currentExe), StringComparison.OrdinalIgnoreCase)) continue;
                    try { File.Delete(file); } catch { }
                }
                foreach (var dir in Directory.GetDirectories(path))
                {
                    try { Directory.Delete(dir, true); } catch { }
                }
            }
            else
            {
                Directory.CreateDirectory(path);
            }
        }

        private static void PrepareDataDirectory(string path)
        {
            if (!Directory.Exists(path))
            {
                Directory.CreateDirectory(path);
            }

            // win サブディレクトリを作成
            string appDataPath = ((App)Application.Current).Settings.WindowsDataPath;
            if (!string.IsNullOrEmpty(appDataPath) && !Directory.Exists(appDataPath))
            {
                Directory.CreateDirectory(appDataPath);
            }
        }

        private static async Task CleanupAsync(AppSettings settings)
        {
            // 管理者権限が必要なクリーンアップ
            try { await Task.Run(() => FirewallService.RemovePortRule()); } catch { }
            try
            {
                if (Directory.Exists(settings.InstallPath))
                {
                    await Task.Run(() =>
                    {
                        string currentExe = AppInfo.CurrentExePath;
                        foreach (var file in Directory.GetFiles(settings.InstallPath))
                        {
                            if (!string.IsNullOrEmpty(currentExe) && string.Equals(Path.GetFullPath(file), Path.GetFullPath(currentExe), StringComparison.OrdinalIgnoreCase)) continue;
                            try { File.Delete(file); } catch { }
                        }
                    });
                }
            }
            catch { }
        }

        private static async Task CleanupUserSetupAsync(AppSettings settings)
        {
            // ユーザー権限で可能なクリーンアップ
            try { await Task.Run(() => WslService.UnregisterDistro()); } catch { }

            try
            {
                string appDataPath = AppInfo.LocalAppDataPath;
                if (Directory.Exists(appDataPath))
                {
                    await Task.Run(() => Directory.Delete(appDataPath, true));
                }
            }
            catch { }

            try
            {
                if (!WslService.HasOtherDistros())
                {
                    string configPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".wslconfig");
                    if (File.Exists(configPath)) File.Delete(configPath);
                }
            }
            catch { }

            try { SetStartup(false); } catch { }

            try
            {
                if (Directory.Exists(settings.DataPath))
                {
                    bool shouldDelete = false;

                    Application.Current.Dispatcher.Invoke(() =>
                    {
                        var result = MessageBox.Show(
                            $"セットアップ中にエラーが発生しました。\n故障診断のために、データフォルダ内の 'win-error.txt' を確認・コピーすることをお勧めします。\n\n作成されたデータフォルダ ({settings.DataPath}) を今すぐ削除しますか？\n(ログを残す場合は 'いいえ' を選択してください)",
                            "クリーンアップの確認",
                            MessageBoxButton.YesNo,
                            MessageBoxImage.Question);

                        if (result == MessageBoxResult.Yes) shouldDelete = true;
                    });

                    if (shouldDelete)
                    {
                        await Task.Run(() => Directory.Delete(settings.DataPath, true));
                    }
                }
            }
            catch { }
        }

        private static async Task DownloadFileAsync(string url, string tarPath, ProgressHandler onProgress, CancellationToken cancellationToken)
        {
            using (var client = new HttpClient())
            {
                using (var response = await client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellationToken))
                {
                    response.EnsureSuccessStatusCode();
                    var totalBytes = response.Content.Headers.ContentLength ?? -1L;
                    var buffer = new byte[8192];
                    var totalRead = 0L;

                    using (var source = await response.Content.ReadAsStreamAsync(cancellationToken))
                    using (var fileStream = new FileStream(tarPath, FileMode.Create, FileAccess.Write, FileShare.None, 8192, true))
                    {
                        int read;
                        while ((read = await source.ReadAsync(buffer, 0, buffer.Length, cancellationToken)) > 0)
                        {
                            await fileStream.WriteAsync(buffer, 0, read, cancellationToken);
                            totalRead += read;
                            if (totalBytes != -1)
                            {
                                double percentage = (double)totalRead / totalBytes * 100;
                                onProgress(percentage, $"ダウンロード中... {percentage:F1}%");
                            }
                        }
                    }
                }
            }
        }

        private static void CopySelfAndCreateShortcuts(AppSettings settings)
        {
            string sourceExe = AppInfo.CurrentExePath;
            if (string.IsNullOrEmpty(sourceExe)) return;

            string? sourceDir = Path.GetDirectoryName(sourceExe);
            if (string.IsNullOrEmpty(sourceDir)) return;

            var extensions = new[] { ".exe", ".dll", ".json", ".ico", ".pdb" };
            foreach (var file in Directory.GetFiles(sourceDir))
            {
                string ext = Path.GetExtension(file).ToLower();
                if (extensions.Contains(ext))
                {
                    // .exe の場合は固定の InstalledExePath に、それ以外は InstallPath 配下に元の名前でコピー
                    string destFile = (ext == ".exe") 
                        ? settings.InstalledExePath 
                        : Path.Combine(settings.InstallPath, Path.GetFileName(file));

                    if (!string.Equals(Path.GetFullPath(file), Path.GetFullPath(destFile), StringComparison.OrdinalIgnoreCase))
                    {
                        File.Copy(file, destFile, true);
                    }
                }
            }

            Application.Current.Dispatcher.Invoke(() => CreateStartMenuShortcuts(settings));
        }

        private static void CreateStartMenuShortcuts(AppSettings settings)
        {
            string commonProgramsPath = Environment.GetFolderPath(Environment.SpecialFolder.CommonPrograms);
            try
            {
                string exePath = settings.InstalledExePath;
                string shortcutPath = Path.Combine(commonProgramsPath, $"{AppInfo.AppFullName}.lnk");
                ShellLinkHelper.CreateShortcut(shortcutPath, exePath, "", $"{AppInfo.AppFullName} を起動", exePath + ",0", Path.GetDirectoryName(exePath), AppInfo.AppShortName);
            }
            catch (Exception ex)
            {
                Logger.Log(ex, "CreateStartMenuShortcuts failed");
                MessageBox.Show("スタートメニューの作成に失敗しました: " + ex.Message, "エラー", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        public static void SetStartup(bool enable)
        {
            const string approvedKeyPath = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\StartupFolder";
            string appName = $"{AppInfo.AppShortName}.lnk";

            try
            {
                string startupFolder = Environment.GetFolderPath(Environment.SpecialFolder.Startup);
                string startupPath = Path.Combine(startupFolder, appName);

                if (enable)
                {
                    string exePath = ((App)Application.Current).Settings.InstalledExePath;
                    if (!string.IsNullOrEmpty(exePath))
                    {
                        // --silent-start 引数付きでショートカット作成
                        ShellLinkHelper.CreateShortcut(startupPath, exePath, "--silent-start", $"{AppInfo.AppShortName} をバックグラウンドで起動", exePath + ",0", Path.GetDirectoryName(exePath), AppInfo.AppShortName);
                    }
                }
                else
                {
                    if (File.Exists(startupPath)) File.Delete(startupPath);
                }

                // 【重要】無効化した場合やアンインストール時は、OSが記録した「無効化履歴(StartupApproved)」も削除する
                if (!enable)
                {
                    using (var key = Registry.CurrentUser.OpenSubKey(approvedKeyPath, true))
                    {
                        key?.DeleteValue(appName, false);
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.Log(ex, "SetStartup failed");
                MessageBox.Show("スタートアップ設定の変更に失敗しました: " + ex.Message, "エラー", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private static void SaveFinalSettings(AppSettings settings)
        {
            settings.User.PendingCertbotUpdate = false;
            settings.User.Save();
        }

        private static async Task ConfigureWslConfigAsync(CancellationToken cancellationToken)
        {
            string configPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".wslconfig");
            await Task.Run(() =>
            {
                var parser = new FileIniDataParser();
                IniData data = File.Exists(configPath) ? parser.ReadFile(configPath) : new IniData();
                if (!data.Sections.Contains("wsl2")) data.Sections.Add("wsl2");
                var wsl2 = data["wsl2"];
                wsl2["networkingMode"] = "mirrored";
                wsl2["nestedVirtualization"] = "false";

                // インスタンスのアイドル停止を無効化 (WSL 2.5.4以降が必要)
                // チェックが外れている場合は、設定を変更しない
                if (((App)Application.Current).Settings.KeepWslRunning)
                {
                    if (!data.Sections.Contains("general")) data.Sections.Add("general");
                    var general = data["general"];
                    general["instanceIdleTimeout"] = "-1";
                }

                if (((App)Application.Current).Settings.HostAddressLoopback)
                {
                    if (!data.Sections.Contains("experimental")) data.Sections.Add("experimental");
                    var experimental = data["experimental"];
                    experimental["hostAddressLoopback"] = "true";
                }

                parser.WriteFile(configPath, data);
            }, cancellationToken);
        }
    }
}
