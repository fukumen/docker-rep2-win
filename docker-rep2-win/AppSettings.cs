using System.IO;
using System.Text.Json;
using System.Reflection;

namespace docker_rep2_win
{
    [AttributeUsage(AttributeTargets.Property)]
    public class RequiresRebootAttribute : Attribute { }

    public class UserSettingsData
    {
        // PageConfig2で変更可能、Wslの再構築か再起動が必要
        [RequiresReboot]
        public string SelectedVersion { get; set; } = string.Empty;

        [RequiresReboot]
        public string PhpMemoryLimit { get; set; } = "256M";

        [RequiresReboot]
        public int MonitorPort { get; set; } = 28080;

        [RequiresReboot]
        public bool EnableCertbot { get; set; } = false;

        // PageConfig2で変更可能、PageConfig2で処理が完了
        public bool RunInBackground { get; set; } = true;
        public bool AutoStart { get; set; } = true;
        public bool AutoLaunchBrowser { get; set; } = true;
        public bool AutoUpdateAlpine { get; set; } = true;
        public bool AutoUpdateApp { get; set; } = true;
        public bool DebugLogEnabled { get; set; } = false;

        // 内部的に永続化が必要な値
        public string SecretKey { get; set; } = string.Empty;
        public bool PendingCertbotUpdate { get; set; } = false;
        public bool IsCaddyfileMounted { get; set; } = false;

        public UserSettingsData Clone() => (UserSettingsData)this.MemberwiseClone();
    }

    public class UserSettings : UserSettingsData
    {
        private UserSettingsData _original = new UserSettingsData();

        public UserSettingsData Original => _original;

        public bool VersionChanged => IsChanged(nameof(SelectedVersion));
        public bool MemoryLimitChanged => IsChanged(nameof(PhpMemoryLimit));
        public bool MonitorPortChanged => IsChanged(nameof(MonitorPort));
        public bool EnableCertbotChanged => IsChanged(nameof(EnableCertbot));
        public bool RunInBackgroundChanged => IsChanged(nameof(RunInBackground));
        public bool AutoStartChanged => IsChanged(nameof(AutoStart));
        public bool AutoLaunchBrowserChanged => IsChanged(nameof(AutoLaunchBrowser));
        public bool AutoUpdateAlpineChanged => IsChanged(nameof(AutoUpdateAlpine));
        public bool AutoUpdateAppChanged => IsChanged(nameof(AutoUpdateApp));
        public bool DebugLogEnabledChanged => IsChanged(nameof(DebugLogEnabled));

        public bool NeedsWslReboot => HasChangedPropertyWithAttribute<RequiresRebootAttribute>();
        public bool HasAnyChanges => HasChangedProperty();

        private bool IsChanged(string propertyName)
        {
            var prop = typeof(UserSettingsData).GetProperty(propertyName);
            if (prop == null) return false;
            return IsPropertyChanged(prop);
        }

        private bool IsPropertyChanged(PropertyInfo prop)
        {
            var currentVal = prop.GetValue(this);
            var originalVal = prop.GetValue(_original);
            return !Equals(currentVal, originalVal);
        }

        private bool HasChangedProperty()
        {
            foreach (var prop in typeof(UserSettingsData).GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly))
            {
                if (IsPropertyChanged(prop)) return true;
            }
            return false;
        }

        private bool HasChangedPropertyWithAttribute<TAttribute>() where TAttribute : Attribute
        {
            foreach (var prop in typeof(UserSettingsData).GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly))
            {
                if (Attribute.IsDefined(prop, typeof(TAttribute)) && IsPropertyChanged(prop))
                {
                    return true;
                }
            }
            return false;
        }

        public void TakeSnapshot() => _original = base.Clone();

        public void Save()
        {
            var app = (App)System.Windows.Application.Current;
            string appDataPath = app.Settings.WindowsDataPath;
            if (string.IsNullOrEmpty(appDataPath)) return;

            try
            {
                if (!Directory.Exists(appDataPath)) Directory.CreateDirectory(appDataPath);

                var path = Path.Combine(appDataPath, "win-setting.json");
                var json = JsonSerializer.Serialize((UserSettingsData)this, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(path, json);
                TakeSnapshot();
            }
            catch (Exception ex)
            {
                Logger.Log(ex, "UserSettings.Save failed");
                System.Diagnostics.Debug.WriteLine($"設定の保存に失敗しました: {ex.Message}");
            }
        }

        public void Load()
        {
            var app = (App)System.Windows.Application.Current;
            string appDataPath = app.Settings.WindowsDataPath;
            if (string.IsNullOrEmpty(appDataPath)) return;
            string path = Path.Combine(appDataPath, "win-setting.json");
            if (!File.Exists(path)) return;

            try
            {
                var json = File.ReadAllText(path);
                var data = JsonSerializer.Deserialize<UserSettingsData>(json);
                if (data != null)
                {
                    foreach (var prop in typeof(UserSettingsData).GetProperties())
                    {
                        prop.SetValue(this, prop.GetValue(data));
                    }
                    TakeSnapshot();
                }
            }
            catch (Exception ex)
            {
                Logger.Log(ex, "UserSettings.Load failed");
                System.Diagnostics.Debug.WriteLine($"設定の読み込みに失敗しました: {ex.Message}");
            }
        }
    }

    public class AppSettings
    {
        internal AppSettings() { }

        // システム・インストール情報 (Registry / HKLM に保存・管理)
        public string InstallPath { get; set; } = string.Empty;
        public string DataPath { get; set; } = string.Empty;
        public int Port { get; set; } = 80;

        public string InstalledExePath => string.IsNullOrEmpty(InstallPath) ? string.Empty : Path.Combine(InstallPath, AppInfo.AppExeName);

        public string WindowsDataPath => string.IsNullOrEmpty(DataPath) ? string.Empty : Path.Combine(DataPath, AppInfo.AppDataDirName);

        // ユーザー設定 (win-setting.json に保存・管理される実体)
        public UserSettings User { get; set; } = new UserSettings();

        // 一時的な作業用変数 (永続化しない)
        public bool OpenFirewall { get; set; } = true;
        public bool KeepWslRunning { get; set; } = true;
        public bool HostAddressLoopback { get; set; } = true;
        public string DownloadUrl { get; set; } = string.Empty;
        public string ManifestDownloadUrl { get; set; } = string.Empty;
        public string SelectedHash { get; set; } = string.Empty;

        public void Load()
        {
            // レジストリから設定値をロード
            UninstallService.LoadRegistrySettings(this);

            // win-setting.jsonから設定値をロード
            User.Load();
        }
    }

    public static class AppInfo
    {
        public const string DockerName = "docker-rep2";

        public const string AppFullName = "docker-rep2 for Windows";

        public const string AppShortName = "docker-rep2-win";

        public const string AppPublisher = "fukumen";

        public const string AppGitHubUrl = "https://github.com/fukumen/docker-rep2-win";

        public const string ManifestUrl = "https://gist.githubusercontent.com/fukumen/341c40b1a0861bb72b24736a2c7ca49e/raw/versions.json";

        public const string AppExeName = "docker-rep2-win.exe";

        public const string CoreAppName = "rep2";

        public const string AppDataDirName = "win";

        public const string DistroName = "docker-rep2-distro";

        public static string AppVersion
        {
            get
            {
                var attr = Assembly.GetExecutingAssembly().GetCustomAttribute<AssemblyInformationalVersionAttribute>();
                return attr?.InformationalVersion.Split('+')[0] ?? "0.0.0";
            }
        }

        public static string CurrentExePath => System.Diagnostics.Process.GetCurrentProcess().MainModule?.FileName ?? string.Empty;

        public static string LocalAppDataPath => 
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), AppShortName);
    }
}
