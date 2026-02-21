using System.Diagnostics;
using System.Windows;

namespace docker_rep2_win
{
    public class LauncherService
    {
        private readonly Action<string> _statusUpdater;

        public LauncherService(Action<string>? statusUpdater = null)
        {
            _statusUpdater = statusUpdater ?? (_ => { });
        }

        public async Task RunMaintenanceAndLaunchAsync()
        {
            try
            {
                await DockerService.StartDockerdAsync();

                var app = (App)Application.Current;
                var settings = app.Settings;

                if (settings.User.AutoUpdateAlpine)
                {
                    UpdateStatus("Alpine Linux を更新中...");
                    await WslService.UpgradeAlpineAsync();

                    var version = await WslService.GetOsVersionAsync();
                    if (!string.IsNullOrEmpty(version))
                    {
                        settings.User.SelectedVersion = version;
                        settings.User.Save();
                    }
                }

                if (settings.User.AutoUpdateApp)
                {
                    UpdateStatus($"{AppInfo.DockerName}の更新を確認中...");
                    await DockerService.PullAsync();
                }

                UpdateStatus($"{AppInfo.DockerName}を起動中...");
                await DockerService.UpAsync();

                UpdateStatus("不要なデータを整理しています...");
                await DockerService.PruneAsync();

                UpdateStatus("起動完了");
            }
            catch (Exception ex)
            {
                Logger.Log(ex, "RunMaintenanceAndLaunchAsync failed");
                throw new Exception($"起動処理中にエラーが発生しました: {ex.Message}", ex);
            }
        }

        public void OpenBrowser()
        {
            var app = (App)Application.Current;
            var settings = app.Settings;

            // ポート番号を取得 (MainWindow起動時にロード済み)
            int port = settings.Port;
            string url = port == 80 ? "http://localhost" : $"http://localhost:{port}";

            Process.Start(new ProcessStartInfo
            {
                FileName = url,
                UseShellExecute = true
            });
        }

        private void UpdateStatus(string text)
        {
            _statusUpdater(text);
        }
    }
}
