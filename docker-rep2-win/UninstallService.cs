using Microsoft.Win32;
using System.Diagnostics;
using System.IO;

namespace docker_rep2_win
{
    public static class UninstallService
    {
        private const string RegistryPath = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\" + AppInfo.AppShortName;

        private class RegistryStore : IDisposable
        {
            private readonly RegistryKey? _key;

            // パスのトークン化に使用する特殊フォルダの定義
            private static readonly (Environment.SpecialFolder Folder, string Token)[] PathTokens = new[]
            {
                (Environment.SpecialFolder.MyDocuments, "{Documents}"),
                (Environment.SpecialFolder.UserProfile, "{UserProfile}")
            };

            public RegistryStore(RegistryKey? key) => _key = key;

            public string? GetString(string name)
            {
                if (_key == null) return null;
                string? s = _key.GetValue(name) as string;
                if (s != null && name == "DataPath") return ExpandPath(s);
                return s;
            }

            public void SetString(string name, string? value)
            {
                if (_key == null || value == null) return;
                if (name == "DataPath") value = TokenizePath(value);
                _key.SetValue(name, value);
            }

            public int GetInt(string name, int defaultValue = 0)
            {
                if (_key == null) return defaultValue;
                return _key.GetValue(name) is int i ? i : defaultValue;
            }

            public void SetInt(string name, int value)
            {
                _key?.SetValue(name, value, RegistryValueKind.DWord);
            }

            // パスをトークン化する（例: C:\Users\Name\Documents\rep2-data -> {Documents}\rep2-data）
            private static string TokenizePath(string path)
            {
                if (string.IsNullOrEmpty(path)) return path;

                foreach (var (folder, token) in PathTokens)
                {
                    string folderPath = Environment.GetFolderPath(folder);
                    if (!string.IsNullOrEmpty(folderPath) && path.StartsWith(folderPath, StringComparison.OrdinalIgnoreCase))
                    {
                        return token + path.Substring(folderPath.Length);
                    }
                }
                return path;
            }

            // トークンを展開する（例: {Documents}\rep2-data -> C:\Users\Name\Documents\rep2-data）
            private static string ExpandPath(string path)
            {
                if (string.IsNullOrEmpty(path)) return path;

                foreach (var (folder, token) in PathTokens)
                {
                    if (path.StartsWith(token, StringComparison.OrdinalIgnoreCase))
                    {
                        string folderPath = Environment.GetFolderPath(folder);
                        return folderPath + path.Substring(token.Length);
                    }
                }
                return path;
            }

            public void Dispose() => _key?.Dispose();
        }

        public static void Register()
        {
            try
            {
                var settings = ((App)System.Windows.Application.Current).Settings;
                string exePath = settings.InstalledExePath;
                string installDir = settings.InstallPath;

                // ディレクトリの使用量を計算 (KB単位)
                long totalBytes = GetDirectorySize(installDir);
                if (!string.IsNullOrEmpty(settings.DataPath) && Directory.Exists(settings.DataPath))
                {
                    totalBytes += GetDirectorySize(settings.DataPath);
                }
                int sizeKB = (int)(totalBytes / 1024);

                using (var store = new RegistryStore(Registry.LocalMachine.CreateSubKey(RegistryPath)))
                {
                    store.SetString("DisplayName", AppInfo.AppFullName);
                    store.SetString("UninstallString", $"\"{exePath}\" --uninstall");
                    store.SetString("ModifyPath", $"\"{exePath}\" --config");
                    store.SetString("DisplayIcon", exePath);
                    store.SetString("Publisher", AppInfo.AppPublisher);
                    store.SetString("DisplayVersion", AppInfo.AppVersion);
                    store.SetString("InstallLocation", installDir);
                    store.SetString("DataPath", settings.DataPath);
                    store.SetInt("EstimatedSize", sizeKB);
                    store.SetInt("Port", settings.Port);
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"レジストリ登録中にエラーが発生しました: {ex.Message}");
            }
        }

        public static void LoadRegistrySettings(AppSettings settings)
        {
            using (var store = new RegistryStore(Registry.LocalMachine.OpenSubKey(RegistryPath)))
            {
                settings.InstallPath = store?.GetString("InstallLocation") ?? settings.InstallPath;
                settings.DataPath = store?.GetString("DataPath") ?? settings.DataPath;
                settings.Port = store?.GetInt("Port", settings.Port) ?? settings.Port;
            }
        }

        private static long GetDirectorySize(string directoryPath)
        {
            if (!Directory.Exists(directoryPath)) return 0;
            try
            {
                // サブディレクトリを含めたすべてのファイルのサイズを合計
                return Directory.GetFiles(directoryPath, "*", SearchOption.AllDirectories)
                                .Select(f => new FileInfo(f).Length)
                                .Sum();
            }
            catch
            {
                return 0;
            }
        }

        public static async Task ExecuteUninstallAsync(bool removeData)
        {
            // レジストリを削除する前に、保存しておいたデータパスを取得する
            string? savedDataPath = null;
            try
            {
                using (var store = new RegistryStore(Registry.LocalMachine.OpenSubKey(RegistryPath)))
                {
                    savedDataPath = store.GetString("DataPath");
                }
            }
            catch { }

            await Task.Run(() => WslService.UnregisterDistro());

            // AppData 内のアプリフォルダを削除 (WSLの実体ファイルも wsl --unregister で消えているが、親フォルダを掃除する)
            try
            {
                string appDataPath = AppInfo.LocalAppDataPath;
                if (Directory.Exists(appDataPath))
                {
                    await Task.Run(() => Directory.Delete(appDataPath, true));
                }
            }
            catch { }

            // 他にディストリビューションが残っていなければ .wslconfig も消す
            try
            {
                if (!WslService.HasOtherDistros())
                {
                    string configPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".wslconfig");
                    if (File.Exists(configPath))
                    {
                        File.Delete(configPath);
                    }
                }
            }
            catch { }

            FirewallService.RemovePortRule();

            Registry.LocalMachine.DeleteSubKey(RegistryPath, false);

            // savedDataPath はアンインストーラー起動時にレジストリから取得したパスを使用
            if (removeData && !string.IsNullOrEmpty(savedDataPath) && Directory.Exists(savedDataPath))
            {
                await Task.Run(() => Directory.Delete(savedDataPath, true));
            }

            RemoveShortcuts();

            // スタートアップ登録と、OS側の無効化履歴(StartupApproved)を削除
            InstallService.SetStartup(false);

            // ファイル削除がOSに認識されるまで少し待機してから、スタートメニューのプロセスを再起動する
            await Task.Delay(2000);
            try
            {
                foreach (var p in Process.GetProcessesByName("StartMenuExperienceHost"))
                {
                    try { p.Kill(); } catch { }
                }
            }
            catch { }
        }

        private static void RemoveShortcuts()
        {
            try
            {
                // スタートメニューのショートカットを削除
                string commonProgramsPath = Environment.GetFolderPath(Environment.SpecialFolder.CommonPrograms);
                string startMenuLnk = Path.Combine(commonProgramsPath, $"{AppInfo.AppFullName}.lnk");
                if (File.Exists(startMenuLnk)) File.Delete(startMenuLnk);

                // デスクトップのショートカットを削除
                string desktopPath = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
                string path = Path.Combine(desktopPath, $"{AppInfo.AppShortName}.lnk");
                if (File.Exists(path)) File.Delete(path);
            }
            catch (Exception ex)
            {
                Logger.Log(ex, "RemoveShortcuts failed");
            }
        }
    }
}
