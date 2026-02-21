using System.Threading;
using System.Windows;
using System.Windows.Controls;

namespace docker_rep2_win
{
    /// <summary>
    /// PageInstall.xaml の相互作用ロジック
    /// </summary>
    public partial class PageInstall : Page
    {
        private readonly AppMode _mode;
        private readonly CancellationTokenSource _cts = new();

        public PageInstall(AppMode mode = AppMode.Install)
        {
            InitializeComponent();
            _mode = mode;

            if (_mode == AppMode.Update)
            {
                TxtTitle.Text = "プログラムを更新しています...";
                BtnCancel.Visibility = Visibility.Hidden;
            }
            else if (_mode == AppMode.Config)
            {
                TxtTitle.Text = "設定を更新しています...";
                BtnCancel.Visibility = Visibility.Hidden;
            }
            else if (_mode == AppMode.UserSetup)
            {
                TxtTitle.Text = "ユーザー環境をセットアップしています...";
                BtnCancel.Visibility = Visibility.Hidden;
            }

            StartInstallation();
        }

        private void Page_Loaded(object sender, RoutedEventArgs e)
        {
            if (BtnCancel.IsVisible)
            {
                BtnCancel.Focus();
            }
        }

        private async void StartInstallation()
        {
            try
            {
                InstallService.ProgressHandler progress = (percent, status) =>
                {
                    // UIスレッドで進捗を更新
                    Dispatcher.Invoke(() =>
                    {
                        PrgInstall.IsIndeterminate = false;
                        PrgInstall.Value = percent;
                        TxtStatus.Text = status;
                    });
                };

                if (_mode == AppMode.Update)
                {
                    await InstallService.RunUpdateAsync(progress, _cts.Token);
                }
                else if (_mode == AppMode.Config)
                {
                    await InstallService.RunConfigAsync(progress, _cts.Token);
                }
                else if (_mode == AppMode.UserSetup)
                {
                    await InstallService.RunUserSetupAsync(progress, _cts.Token);
                }
                else
                {
                    await InstallService.RunInstallAsync(progress, _cts.Token);
                }

                // 完了したら次のページへ
                NavigationService.Navigate(new PageComplete(_mode == AppMode.Update || _mode == AppMode.Config || _mode == AppMode.UserSetup));
            }
            catch (OperationCanceledException)
            {
                // キャンセル時は CleanupAsync が InstallService 内で呼ばれている
                Application.Current.Shutdown();
            }
            catch (Exception ex)
            {
                Logger.Log(ex, "Installation failed");
                MessageBox.Show($"エラーが発生しました:\n{ex.Message}", "実行失敗", MessageBoxButton.OK, MessageBoxImage.Error);
                // 失敗した場合は設定画面に戻す
                if (NavigationService.CanGoBack) NavigationService.GoBack();
            }
        }

        private void BtnCancel_Click(object sender, RoutedEventArgs e)
        {
            if (MessageBox.Show("インストールを中断しますか？", "中断の確認", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes)
            {
                BtnCancel.IsEnabled = false;
                TxtStatus.Text = "中断しています...";
                _cts.Cancel();
            }
        }
    }
}
