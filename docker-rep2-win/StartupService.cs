using System.Diagnostics;
using System.IO;
using System.Security.Principal;
using System.Windows;

namespace docker_rep2_win
{
    public enum AppMode
    {
        Auto,
        Install,
        Uninstall,
        Update,
        Config,
        UserSetup,
        Start,
        SilentStart,
        BackgroundStart,
        Stop,
        Version,
        Menu
    }

    public enum SystemState
    {
        NotInstalled,           // 未インストール
        Installed_Ready,        // インストール済・パス正常・セットアップ済
        Installed_NeedsSetup,   // インストール済・パス正常・セットアップ未完了
        UpdateAvailable,        // インストール済・パス不一致・セットアップ済
        UpdateBlocked,          // インストール済・パス不一致・セットアップ未完了（ガード対象）
        Manual                  // 引数指定あり
    }

    public class StartupInfo
    {
        public AppMode Mode { get; set; }
        public SystemState State { get; set; }
        public string[] RawArgs { get; set; } = Array.Empty<string>();
        public bool IsAdminRequired { get; set; }
    }

    public record IpcConfig(string Command, bool ExitOnSuccess);

    public static class StartupService
    {
        private static readonly Dictionary<AppMode, string> ModeMapping = new()
        {
            { AppMode.Install,          "--install" },
            { AppMode.Uninstall,        "--uninstall" },
            { AppMode.Update,           "--update" },
            { AppMode.Config,           "--config" },
            { AppMode.UserSetup,        "--user-setup" },
            { AppMode.Start,            "--start" },
            { AppMode.SilentStart,      "--silent-start" },
            { AppMode.BackgroundStart,  "--bg-start" },
            { AppMode.Stop,             "--stop" },
            { AppMode.Version,         "--version" },
            { AppMode.Menu,            "--menu" }
            };


        private static readonly Dictionary<AppMode, IpcConfig> IpcMapping = new()
        {
            { AppMode.Install,         new("EXIT_APP",     false) },
            { AppMode.Uninstall,       new("EXIT_APP",     false) },
            { AppMode.Update,          new("EXIT_APP",     false) },
            { AppMode.Config,          new("SHOW_CONFIG",  true)  },
            { AppMode.UserSetup,       new("EXIT_APP",     false) },
            { AppMode.Start,           new("SHOW_MAIN",    true)  },
            { AppMode.SilentStart,     new("EXIT_APP",     false) },
            { AppMode.BackgroundStart, new("EXIT_APP",     false) },
            { AppMode.Stop,            new("STOP_WSL",     true)  },
            { AppMode.Version,         new("SHOW_VERSION", true)  },
            { AppMode.Menu,            new("SHOW_MENU",    true)  }
        };

        public static StartupInfo AnalyzeStartup(string[] args)
        {
            var mode = ParseArguments(args);
            var state = SystemState.Manual;

            if (mode == AppMode.Auto)
            {
                bool isInstalled = IsInstalled();
                bool inPath = IsRunningFromInstallPath();
                bool needsSetup = NeedsUserSetup();

                // 【真理値表に基づく判定】
                state = (isInstalled, inPath, needsSetup) switch
                {
                    (false, _,     _)     => SystemState.NotInstalled,
                    (true,  true,  true)  => SystemState.Installed_NeedsSetup,
                    (true,  true,  false) => SystemState.Installed_Ready,
                    (true,  false, false) => SystemState.UpdateAvailable,
                    (true,  false, true)  => SystemState.UpdateBlocked,
                };
            }
            else if (RequiresSetupCheck(mode))
            {
                // 実行系モードの場合、セットアップが未完了なら誘導する
                if (NeedsUserSetup())
                {
                    state = SystemState.Installed_NeedsSetup;
                }
            }

            return new StartupInfo
            {
                Mode = mode,
                State = state,
                RawArgs = args,
                IsAdminRequired = RequiresAdmin(mode)
            };
        }

        private static AppMode ParseArguments(string[] args)
        {
            foreach (var entry in ModeMapping)
            {
                if (args.Contains(entry.Value)) return entry.Key;
            }
            return AppMode.Auto;
        }

        public static bool IsInstalled()
        {
            var app = (App)Application.Current;
            string path = app.Settings.InstalledExePath;
            return !string.IsNullOrEmpty(path) && File.Exists(path);
        }

        public static bool NeedsUserSetup()
        {
            var app = (App)Application.Current;
            // DataPath が未設定、またはディレクトリ自体が存在しない
            if (string.IsNullOrEmpty(app.Settings.DataPath) || !Directory.Exists(app.Settings.DataPath)) return true;

            // 管理ディレクトリ (win) が存在しない
            if (!Directory.Exists(app.Settings.WindowsDataPath)) return true;

            // WSL ディストリビューションが登録されていない
            return !WslService.IsDistroInstalled();
        }

        public static bool IsRunningFromInstallPath()
        {
            try
            {
                var app = (App)Application.Current;
                string installedPath = app.Settings.InstalledExePath;
                if (string.IsNullOrEmpty(installedPath)) return false;

                string currentPath = AppInfo.CurrentExePath;
                if (string.IsNullOrEmpty(currentPath)) return false;

                return string.Equals(Path.GetFullPath(currentPath), Path.GetFullPath(installedPath), StringComparison.OrdinalIgnoreCase);
            }
            catch { return false; }
        }

        public static bool RequiresSetupCheck(AppMode mode)
        {
            // 以下のモードはセットアップ済みであることを期待して動かしてしまう
            return mode is AppMode.Config or AppMode.Stop or AppMode.SilentStart or AppMode.BackgroundStart or AppMode.Start or AppMode.Menu;
        }

        public static bool RequiresAdmin(AppMode mode)
        {
            return mode is AppMode.Install or AppMode.Uninstall or AppMode.Update;
        }

        public static bool IsAdministrator()
        {
            using (var identity = WindowsIdentity.GetCurrent())
            {
                var principal = new WindowsPrincipal(identity);
                return principal.IsInRole(WindowsBuiltInRole.Administrator);
            }
        }
        
        public static string GetArgumentString(AppMode mode)
        {
            if (mode == AppMode.Auto) return "";
            if (ModeMapping.TryGetValue(mode, out var arg))
            {
                return arg;
            }
            throw new InvalidOperationException($"AppMode '{mode}' に対応するコマンドライン引数が定義されていません。");
        }

        public static IpcConfig GetIpcConfig(AppMode mode)
        {
            if (IpcMapping.TryGetValue(mode, out var config))
            {
                return config;
            }
            throw new InvalidOperationException($"AppMode '{mode}' に対応する IPC 設定が定義されていません。");
        }
    }
}
