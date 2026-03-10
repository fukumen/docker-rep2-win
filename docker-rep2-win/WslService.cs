using System.Diagnostics;
using System.Management;
using System.IO;
using Windows.Management.Deployment;
using IniParser;
using IniParser.Model;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System;
using System.Text;
using System.Threading;
using Microsoft.Win32;
using System.Windows;
using System.Runtime.InteropServices;
using Windows.Win32;

namespace docker_rep2_win
{
    public static class WslService
    {
        // プロセス実行結果
        public record WslResult(int ExitCode, string StdOut, string StdErr);

        // --- 共通ヘルパーメソッド ---

        /// WSL管理コマンドを実行
        private static async Task<WslResult> RunWslCommandAsync(string arguments, int timeoutSeconds = 30, CancellationToken cancellationToken = default)
        {
            var psi = new ProcessStartInfo
            {
                FileName = "wsl.exe",
                Arguments = arguments,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                StandardOutputEncoding = Encoding.Unicode,
                StandardErrorEncoding = Encoding.Unicode,
                WorkingDirectory = "C:\\" // WSLが確実に理解できるパスに固定
            };

            return await RunProcessAsync(psi, null, timeoutSeconds, cancellationToken);
        }

        /// Linux内部コマンドを実行
        private static async Task<WslResult> RunLinuxCommandAsync(string arguments, string? input = null, int timeoutSeconds = 30, string? workDir = null, CancellationToken cancellationToken = default)
        {
            var psi = new ProcessStartInfo
            {
                FileName = "wsl.exe",
                Arguments = arguments,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8,
                WorkingDirectory = string.IsNullOrEmpty(workDir) ? "C:\\" : workDir // デフォルトを C:\ に
            };

            if (input != null)
            {
                psi.RedirectStandardInput = true;
                psi.StandardInputEncoding = Encoding.UTF8;
            }

            // docker compose 用の環境変数をセット
            var app = (App)Application.Current;
            var settings = app.Settings;
            string composeFiles = "docker-compose.yml:docker-compose.override.yml";
            string localFile = Path.Combine(settings.WindowsDataPath, "docker-compose.local.yml");
            if (File.Exists(localFile))
            {
                composeFiles += ":docker-compose.local.yml";
            }
            psi.EnvironmentVariables["COMPOSE_FILE"] = composeFiles;
            psi.EnvironmentVariables["WSLENV"] = "COMPOSE_FILE/u";

            return await RunProcessAsync(psi, input, timeoutSeconds, cancellationToken);
        }

        private static async Task<WslResult> RunProcessAsync(ProcessStartInfo psi, string? input, int timeoutSeconds, CancellationToken cancellationToken = default)
        {
            using (var process = new Process { StartInfo = psi })
            {
                var stdoutBuilder = new StringBuilder();
                var stderrBuilder = new StringBuilder();

                process.OutputDataReceived += (s, e) => { if (e.Data != null) stdoutBuilder.AppendLine(e.Data); };
                process.ErrorDataReceived += (s, e) => { if (e.Data != null) stderrBuilder.AppendLine(e.Data); };

                if (!process.Start())
                {
                    throw new Exception($"Failed to start process: {psi.FileName} {psi.Arguments}");
                }

                process.BeginOutputReadLine();
                process.BeginErrorReadLine();

                // 入力がある場合は書き込む
                if (input != null)
                {
                    try
                    {
                        using (var writer = process.StandardInput)
                        {
                            await writer.WriteAsync(input);
                        }
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"Error writing to stdin: {ex.Message}");
                    }
                }

                // タイムアウト用トークンと外部からのキャンセル用トークンを結合
                using (var timeoutCts = (timeoutSeconds > 0) ? new CancellationTokenSource(TimeSpan.FromSeconds(timeoutSeconds)) : new CancellationTokenSource())
                using (var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(timeoutCts.Token, cancellationToken))
                {
                    try
                    {
                        await process.WaitForExitAsync(linkedCts.Token);
                    }
                    catch (OperationCanceledException)
                    {
                        try { process.Kill(true); } catch { }
                        
                        if (timeoutCts.Token.IsCancellationRequested)
                        {
                            throw new TimeoutException($"WSL process timed out after {timeoutSeconds}s: {psi.Arguments}");
                        }
                        throw;
                    }
                }

                return new WslResult(process.ExitCode, stdoutBuilder.ToString(), stderrBuilder.ToString());
            }
        }

        public static async Task<(bool isOsOk, bool wslInstalled, bool isWslVersionOk, bool virtEnabled, bool biosVirtEnabled)> CheckSystemRequirements()
        {
            bool isOsOk = Environment.OSVersion.Version.Build >= 22621;
            bool wsl = false;
            bool isWslVersionOk = false;
            bool virt = false;
            bool biosVirt = false;

            await Task.Run(async () =>
            {
                // WSLのインストール確認
                try
                {
                    var result = await RunWslCommandAsync("--status");
                    if (result.ExitCode == 0) wsl = true;
                }
                catch { wsl = false; }

                // WSLバージョンチェック (Mirrored Modeには 2.0.0 以上が必要)
                if (wsl)
                {
                    try
                    {
                        var packageManager = new PackageManager();
                        var packages = packageManager.FindPackagesForUser(string.Empty, "MicrosoftCorporationII.WindowsSubsystemForLinux_8wekyb3d8bbwe");

                        foreach (var package in packages)
                        {
                            var v = package.Id.Version;
                            var version = new Version(v.Major, v.Minor, v.Build, v.Revision);
                            if (version >= new Version(2, 0, 0))
                            {
                                isWslVersionOk = true;
                            }
                            break;
                        }
                    }
                    catch { isWslVersionOk = false; }
                }

                // 仮想マシンプラットフォーム機能の直接確認 (Win32_OptionalFeature)
                try {
                    using (var searcher = new ManagementObjectSearcher("SELECT InstallState FROM Win32_OptionalFeature WHERE Name = 'VirtualMachinePlatform'"))
                    using (var collection = searcher.Get())
                    {
                        foreach (var item in collection)
                        {
                            // InstallState: 1 = Enabled
                            if (item["InstallState"] != null && Convert.ToInt32(item["InstallState"]) == 1)
                            {
                                virt = true;
                            }
                            break;
                        }
                    }
                } catch { virt = false; }

                // BIOS仮想化設定 (VirtualizationFirmwareEnabled)
                try {
                    // 1. WSLが既に正常動作しているなら、UEFI仮想化は間違いなく有効
                    if (wsl)
                    {
                        biosVirt = true;
                    }
                    else
                    {
                        // 2. WSLが未インストール等の場合のみ、UEFI設定を直接確認する
                        // Windows API (IsProcessorFeaturePresent) による判定
                        if (PInvoke.IsProcessorFeaturePresent(Windows.Win32.System.Threading.PROCESSOR_FEATURE_ID.PF_VIRT_FIRMWARE_ENABLED))
                        {
                            biosVirt = true;
                        }
                        else
                        {
                            // 3. APIで判定できない場合のみ WMI による確認を行う
                            using (var searcher = new ManagementObjectSearcher("Select VirtualizationFirmwareEnabled from Win32_Processor"))
                            using (var collection = searcher.Get())
                            {
                                foreach (var item in collection)
                                {
                                    var val = item["VirtualizationFirmwareEnabled"];
                                    if (val != null && val is bool b && b)
                                    {
                                        biosVirt = true;
                                        break;
                                    }
                                }
                            }
                        }
                    }
                } catch { biosVirt = false; }
            });

            return (isOsOk, wsl, isWslVersionOk, virt, biosVirt);
        }

        public static async Task<(bool exists, bool needsUpdate)> CheckWslConfigStatus()
        {
            string configPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".wslconfig");
            if (!File.Exists(configPath)) return (false, true);

            return await Task.Run(() =>
            {
                try
                {
                    var parser = new FileIniDataParser();
                    IniData data = parser.ReadFile(configPath);

                    if (data.Sections.Contains("wsl2"))
                    {
                        var wsl2 = data["wsl2"];
                        string networkingMode = wsl2["networkingMode"] ?? string.Empty;
                        bool hasMirrored = string.Equals(networkingMode, "mirrored", StringComparison.OrdinalIgnoreCase);
                        return (true, !hasMirrored);
                    }
                    return (true, true);
                }
                catch
                {
                    return (true, true);
                }
            });
        }

        // 管理者権限が必要
        public static void EnableWslFeatures(bool isUpdate = false)
        {
            string cmd = isUpdate ? "wsl --update --web-download" : "wsl --install --no-distribution";
            Process.Start(new ProcessStartInfo
            {
                FileName = "powershell.exe",
                Arguments = $"-NoExit -Command \"{cmd}\"",
                Verb = "runas",
                UseShellExecute = true
            });
        }

        public static string? GetAppDataWslPath()
        {
            var app = (App)Application.Current;
            string path = app.Settings.WindowsDataPath;
            return string.IsNullOrEmpty(path) ? null : ConvertToWslPath(path);
        }

        public static string ConvertToWslPath(string windowsPath)
        {
            if (string.IsNullOrEmpty(windowsPath) || windowsPath.Length < 2) return windowsPath;

            string drive = windowsPath[0].ToString().ToLower();
            string pathWithoutDrive = windowsPath.Substring(3).Replace('\\', '/');
            return $"/mnt/{drive}/{pathWithoutDrive}";
        }

        public static bool IsDistroInstalled()
        {
            string vhdxPath = Path.Combine(AppInfo.LocalAppDataPath, "wsl", "ext4.vhdx");
            if (!File.Exists(vhdxPath)) return false;

            string distroName = AppInfo.DistroName;
            try
            {
                using (var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Lxss"))
                {
                    if (key != null)
                    {
                        foreach (var subkeyName in key.GetSubKeyNames())
                        {
                            using (var subkey = key.OpenSubKey(subkeyName))
                            {
                                if (subkey?.GetValue("DistributionName")?.ToString() == distroName)
                                {
                                    return true;
                                }
                            }
                        }
                    }
                }
            }
            catch
            {
                try
                {
                    var result = Task.Run(() => RunWslCommandAsync("--list --quiet", 3)).GetAwaiter().GetResult();
                    return result.StdOut.Contains(distroName);
                }
                catch { return false; }
            }

            return false;
        }

        public static bool HasOtherDistros()
        {
            try
            {
                var result = Task.Run(() => RunWslCommandAsync("--list --quiet", 3)).GetAwaiter().GetResult();
                string output = result.StdOut;
                
                var lines = output.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                                  .Select(l => l.Trim('\0', ' ', '\t'))
                                  .Where(l => !string.IsNullOrWhiteSpace(l));

                return lines.Any();
            }
            catch
            {
                return true;
            }
        }

        public static void UnregisterDistro()
        {
            string distroName = AppInfo.DistroName;
            try
            {
                Task.Run(() => RunWslCommandAsync($"--unregister {distroName}")).GetAwaiter().GetResult();
            }
            catch { }
        }

        public static void Shutdown()
        {
            try 
            {
                Task.Run(() => RunWslCommandAsync("--shutdown")).GetAwaiter().GetResult();
            }
            catch { }
        }

        public static void TerminateDistro()
        {
            string distroName = AppInfo.DistroName;
            try
            {
                Task.Run(() => RunWslCommandAsync($"--terminate {distroName}")).GetAwaiter().GetResult();
            }
            catch { }
        }

        public static async Task ImportDistroAsync(string tarPath)
        {
            string distroName = AppInfo.DistroName;

            string installPath = Path.Combine(AppInfo.LocalAppDataPath, "wsl");
            if (!Directory.Exists(installPath)) Directory.CreateDirectory(installPath);
            
            string vhdxPath = Path.Combine(installPath, "ext4.vhdx");

            using (var cts = new CancellationTokenSource())
            {
                var importTask = RunWslCommandAsync($"--import {distroName} \"{installPath}\" \"{tarPath}\" --version 2", -1, cts.Token);

                var monitorTask = Task.Run(async () =>
                {
                    long lastSize = -1;
                    int stallCount = 0;
                    const int checkIntervalMs = 2000;
                    const int stallLimit = 30;

                    while (!importTask.IsCompleted)
                    {
                        await Task.Delay(checkIntervalMs);

                        if (importTask.IsCompleted) break;

                        if (File.Exists(vhdxPath))
                        {
                            try
                            {
                                long currentSize = new FileInfo(vhdxPath).Length;
                                if (currentSize > lastSize)
                                {
                                    lastSize = currentSize;
                                    stallCount = 0;
                                }
                                else
                                {
                                    stallCount++;
                                }
                            }
                            catch { }
                        }

                        if (stallCount >= stallLimit)
                        {
                            cts.Cancel();
                            break;
                        }
                    }
                });

                try
                {
                    await importTask;
                }
                catch (OperationCanceledException)
                {
                    throw new TimeoutException("WSL import timed out (disk activity stalled).");
                }
                
                var result = await importTask;
                if (result.ExitCode != 0)
                {
                    string message = !string.IsNullOrWhiteSpace(result.StdErr) ? result.StdErr : result.StdOut;
                    throw new Exception($"WSLのインポートに失敗しました (ExitCode: {result.ExitCode}): {message}");
                }
            }
        }

        public static async Task<WslResult> RunCommandAsync(string command, int timeoutMs = -1, bool ignoreExitCode = false, CancellationToken cancellationToken = default)
        {
            string distroName = AppInfo.DistroName;
            int timeoutSec = timeoutMs > 0 ? timeoutMs / 1000 : 3600;

            var result = await RunLinuxCommandAsync($"-d {distroName} -u root sh -l -c \"{command.Replace("\"", "\\\"")}\"", null, timeoutSec, null, cancellationToken);

            if (!ignoreExitCode && result.ExitCode != 0)
            {
                throw new Exception($"WSLコマンドの実行に失敗しました (ExitCode: {result.ExitCode}): {result.StdErr}");
            }

            return result;
        }

        public static async Task ExecuteScriptAsync(string script, CancellationToken cancellationToken = default)
        {
            string distroName = AppInfo.DistroName;
            string linuxScript = script.Replace("\r\n", "\n");
            
            var result = await RunLinuxCommandAsync($"-d {distroName} -u root sh", linuxScript, 60, "C:\\", cancellationToken);

            if (result.ExitCode != 0)
            {
                throw new Exception($"セットアップスクリプトの実行に失敗しました: {result.StdErr}");
            }
        }

        public static async Task<string> GetOsVersionAsync(CancellationToken cancellationToken = default)
        {
            string distroName = AppInfo.DistroName;
            try
            {
                var result = await RunLinuxCommandAsync($"-d {distroName} -u root cat /etc/alpine-release", null, 30, null, cancellationToken);
                return result.StdOut.Trim();
            }
            catch
            {
                return string.Empty;
            }
        }

        public static async Task<string> GetDockerdLogAsync(CancellationToken cancellationToken = default)
        {
            string distroName = AppInfo.DistroName;
            try
            {
                var result = await RunLinuxCommandAsync($"-d {distroName} -u root cat /var/log/dockerd.log", null, 30, null, cancellationToken);
                return result.StdOut;
            }
            catch
            {
                return "ログを取得できませんでした。";
            }
        }

        public static async Task<WslResult> UpgradeAlpineAsync(CancellationToken cancellationToken = default)
        {
            return await RunCommandAsync("apk update && apk upgrade --no-cache", 120000, false, cancellationToken);
        }
    }
}
