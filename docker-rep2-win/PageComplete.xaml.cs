using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;

namespace docker_rep2_win
{
    /// <summary>
    /// PageComplete.xaml の相互作用ロジック
    /// </summary>
    public partial class PageComplete : Page
    {
        private readonly bool _isConfigMode;

        public PageComplete(bool isConfigMode = false)
        {
            InitializeComponent();
            _isConfigMode = isConfigMode;

            var settings = ((App)Application.Current).Settings;
            int port = settings.Port;
            TxtUrl.Text = port == 80 ? "http://localhost" : $"http://localhost:{port}";

            if (_isConfigMode)
            {
                TxtTitle.Text = "設定の更新が完了しました！";
                TxtSubtitle.Text = $"{AppInfo.AppShortName} の設定更新が正常に終了しました。";
                ChkCreateShortcut.Visibility = Visibility.Collapsed;
                ChkCreateShortcut.IsChecked = false;
            }
        }

        private void Page_Loaded(object sender, RoutedEventArgs e)
        {
            BtnFinish.Focus();
        }

        private void BtnFinish_Click(object sender, RoutedEventArgs e)
        {
            if (ChkCreateShortcut.IsChecked == true)
            {
                CreateDesktopShortcut();
            }

            var settings = ((App)Application.Current).Settings;
            bool launchNow = ChkLaunchNow.IsChecked == true;

            // 「今すぐ起動」または「バックグラウンド実行」が有効な場合、本体アプリを起動する
            if (launchNow || settings.User.RunInBackground)
            {
                try
                {
                    string exePath = settings.InstalledExePath;
                    if (File.Exists(exePath))
                    {
                        App.ReleaseMutex();

                        // 今すぐ起動する場合は引数なし（メイン画面表示）、
                        // そうでない場合は --bg-start（タスクトレイ常駐）で起動する
                        string args = launchNow ? "" : "--bg-start";
                        ProcessProvider.StartNonElevated(exePath, args);
                    }
                }
                catch (Exception ex)
                {
                    Logger.Log(ex, "Failed to launch app via ProcessProvider");
                }
            }

            Window.GetWindow(this)?.Close();
        }

        private void CreateDesktopShortcut()
        {
            try
            {
                var settings = ((App)Application.Current).Settings;
                string exePath = settings.InstalledExePath;
                if (string.IsNullOrEmpty(exePath)) return;

                string desktopPath = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
                string? workingDir = Path.GetDirectoryName(exePath);

                string startLnkPath = Path.Combine(desktopPath, $"{AppInfo.AppShortName}.lnk");
                ShellLinkHelper.CreateShortcut(startLnkPath, exePath, "", $"{AppInfo.AppShortName}", exePath + ",0", workingDir, AppInfo.AppShortName);
            }
            catch (Exception ex)
            {
                Logger.Log(ex, "CreateDesktopShortcut failed");
                MessageBox.Show("ショートカットの作成に失敗しました: " + ex.Message);
            }
        }
    }
}
