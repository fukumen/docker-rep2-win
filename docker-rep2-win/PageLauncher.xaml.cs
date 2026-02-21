using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;

namespace docker_rep2_win
{
    /// <summary>
    /// PageLauncher.xaml の相互作用ロジック
    /// </summary>
    public partial class PageLauncher : Page
    {
        public PageLauncher()
        {
            InitializeComponent();
            Loaded += PageLauncher_Loaded;
        }

        private async void PageLauncher_Loaded(object sender, RoutedEventArgs e)
        {
            // 画面が表示されたらメンテナンス開始
            await RunMaintenanceAndLaunch();
        }

        private async Task RunMaintenanceAndLaunch()
        {
            try
            {
                var launcher = new LauncherService(UpdateStatus);
                await launcher.RunMaintenanceAndLaunchAsync();

                var app = (App)Application.Current;
                if (app.Settings.User.AutoLaunchBrowser)
                {
                    UpdateStatus("ブラウザを起動します...");
                    launcher.OpenBrowser();
                }

                await Task.Delay(1000); // ステータスを見せるためのウェイト（お好みで）
                
                if (Application.Current.MainWindow is MainWindow mw)
                {
                    mw.OnLaunchCompleted();
                }
            }
            catch (Exception ex)
            {
                Logger.Log(ex, "Launch maintenance failed");
                TxtStatus.Text = $"起動処理中にエラーが発生しました: {ex.Message}";
                PrgLoading.IsIndeterminate = false;
                PrgLoading.Value = 0;
                // エラー時はユーザーが閉じるまで待機するなど
            }
        }

        private void UpdateStatus(string text)
        {
            // UIスレッドで更新
            Dispatcher.Invoke(() => TxtStatus.Text = text);
        }
    }
}
