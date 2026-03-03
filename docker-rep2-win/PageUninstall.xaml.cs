using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;

namespace docker_rep2_win
{
    /// <summary>
    /// PageUninstall.xaml の相互作用ロジック
    /// </summary>
    public partial class PageUninstall : Page
    {
        public PageUninstall()
        {
            InitializeComponent();
        }

        private void Page_Loaded(object sender, RoutedEventArgs e)
        {
            BtnExecute.Focus();
        }

        private void BtnCancel_Click(object sender, RoutedEventArgs e)
        {
            Window.GetWindow(this)?.Close();
        }

        private async void BtnExecute_Click(object sender, RoutedEventArgs e)
        {
            BtnExecute.IsEnabled = false;

            string exePath = AppInfo.CurrentExePath;
            string? directory = !string.IsNullOrEmpty(exePath) ? Path.GetDirectoryName(exePath) : null;

            if (string.IsNullOrEmpty(exePath) || directory == null)
            {
                MessageBox.Show("実行ファイルのパスを取得できませんでした。", "エラー", MessageBoxButton.OK, MessageBoxImage.Error);
                BtnExecute.IsEnabled = true;
                return;
            }

            // アンインストール処理でレジストリが消える前に、インストール先パスを取得しておく
            var settings = ((App)Application.Current).Settings;
            string installedPath = settings.InstallPath;

            try
            {
                UpdateOverlay.Visibility = Visibility.Visible;
                TxtUpdateStatus.Text = "アンインストール中...";

                await UninstallService.ExecuteUninstallAsync(ChkRemoveData.IsChecked == true);

                // 実行場所がインストール先かどうか判定
                bool isRunningFromInstallDir = false;
                if (!string.IsNullOrEmpty(installedPath))
                {
                    try
                    {
                        isRunningFromInstallDir = string.Equals(
                            Path.GetFullPath(directory).TrimEnd('\\'), 
                            Path.GetFullPath(installedPath).TrimEnd('\\'), 
                            StringComparison.OrdinalIgnoreCase);
                    }
                    catch { }
                }

                if (isRunningFromInstallDir)
                {
                    MessageBox.Show("アンインストールが完了しました。", "完了");
                    // インストール先から実行されている場合は自爆（フォルダごと削除）
                    SelfDestruct(directory);
                }
                else
                {
                    // 外部から実行されている場合はインストールフォルダを削除
                    if (!string.IsNullOrEmpty(installedPath) && Directory.Exists(installedPath))
                    {
                        try
                        {
                            Directory.Delete(installedPath, true);
                        }
                        catch { }
                    }

                    MessageBox.Show("アンインストールが完了しました。", "完了");
                    Window.GetWindow(this)?.Close();
                }
            }
            catch (Exception ex)
            {
                Logger.Log(ex, "Uninstallation error");
                MessageBox.Show($"アンインストール中にエラーが発生しました。\n{ex.Message}", "エラー", MessageBoxButton.OK, MessageBoxImage.Error);
                // エラー時は削除せずに残す（ユーザーが確認できるようにする）
                BtnExecute.IsEnabled = true;
            }
            finally
            {
                UpdateOverlay.Visibility = Visibility.Collapsed;
            }
        }

        private void SelfDestruct(string directory)
        {
            // インストールフォルダとみなしてディレクトリごと削除
            // pingで少し待機してから削除を実行する
            string command = $"/c ping 127.0.0.1 -n 4 > nul & rd /s /q \"{directory}\"";

            Process.Start(new ProcessStartInfo
            {
                FileName = "cmd.exe",
                Arguments = command,
                WindowStyle = ProcessWindowStyle.Hidden,
                CreateNoWindow = true,
                UseShellExecute = false
            });

            Window.GetWindow(this)?.Close();
        }
    }
}
