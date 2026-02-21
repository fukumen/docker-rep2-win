using System.Windows;
using System.Windows.Controls;
namespace docker_rep2_win
{
    /// <summary>
    /// PageStop.xaml の相互作用ロジック
    /// </summary>
    public partial class PageStop : Page
    {
        public PageStop()
        {
            InitializeComponent();
            Loaded += PageStop_Loaded;
        }

        private async void PageStop_Loaded(object sender, RoutedEventArgs e)
        {
            await StopEnvironment();
        }

        private void UpdateStatus(string text)
        {
            Dispatcher.Invoke(() => TxtStatus.Text = text);
        }

        private async Task StopEnvironment()
        {
            try
            {
                // 1. Docker Compose Down (コンテナの停止・削除)
                UpdateStatus($"{AppInfo.DockerName}を停止中...");
                
                // タイムアウトを設けるか、エラーでも次に進むように try-catch するのが安全
                try {
                    await DockerService.DownAsync();
                } catch { /* ignore */ }

                // 2. WSL Terminate (インスタンスの完全停止)
                UpdateStatus($"{AppInfo.DistroName}を停止中...");
                await Task.Run(() => WslService.TerminateDistro());

                if (Application.Current.MainWindow is MainWindow mw)
                {
                    mw.OnStopCompleted();
                }
            }
            catch (Exception ex)
            {
                Logger.Log(ex, "StopEnvironment failed");
                TxtStatus.Text = $"エラーが発生しました: {ex.Message}";
                PrgLoading.IsIndeterminate = false;
                PrgLoading.Value = 0;

                MessageBox.Show("停止中にエラーが発生しましたが、アプリを終了します。\n" + ex.Message);
                if (Application.Current.MainWindow is MainWindow mw)
                {
                    mw.OnStopCompleted();
                }
            }
        }
    }
}
