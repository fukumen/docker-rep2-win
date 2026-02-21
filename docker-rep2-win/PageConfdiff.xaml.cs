using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Navigation;

namespace docker_rep2_win
{
    /// <summary>
    /// PageConfdiff.xaml の相互作用ロジック
    /// </summary>
    public partial class PageConfdiff : Page
    {
        public PageConfdiff()
        {
            InitializeComponent();
        }

        private async void PageConfdiff_Loaded(object sender, RoutedEventArgs e)
        {
            BtnBack.Focus();
            await RefreshDiffAsync();
        }

        private async Task RefreshDiffAsync()
        {
            try
            {
                TxtDiff.Text = "読み込み中...";
                BtnRefresh.IsEnabled = false;

                var settings = ((App)Application.Current).Settings;
                string wslPath = WslService.ConvertToWslPath(settings.WindowsDataPath);
                
                string cmd = $"cd \"{wslPath}\" && docker compose exec -T rep2php8 diff /var/www/conf.orig /ext/conf | iconv -f SHIFT_JIS -t UTF-8";
                
                var result = await WslService.RunCommandAsync(cmd, ignoreExitCode: true);

                string allOutput = (result.StdOut.Trim() + "\n" + result.StdErr.Trim()).Trim();

                if (string.IsNullOrEmpty(allOutput))
                {
                    TxtDiff.Text = "設定ファイルに差異はありません。";
                }
                else
                {
                    TxtDiff.Text = allOutput;
                }
            }
            catch (Exception ex)
            {
                Logger.Log(ex, "PageConfdiff.RefreshDiffAsync failed");
                TxtDiff.Text = $"エラーが発生しました:\n{ex.Message}";
            }
            finally
            {
                BtnRefresh.IsEnabled = true;
            }
        }

        private void BtnBack_Click(object sender, RoutedEventArgs e)
        {
            if (NavigationService.CanGoBack)
            {
                NavigationService.GoBack();
            }
        }

        private void BtnOpenConf_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var settings = ((App)Application.Current).Settings;
                string confPath = Path.Combine(settings.DataPath, "conf");
                if (Directory.Exists(confPath))
                {
                    Process.Start("explorer.exe", confPath);
                }
                else
                {
                    MessageBox.Show("設定フォルダが見つかりません: " + confPath, "エラー", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
            catch (Exception ex)
            {
                Logger.Log(ex, "BtnOpenConf_Click failed");
                MessageBox.Show("エクスプローラーを開けませんでした: " + ex.Message, "エラー", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async void BtnRefresh_Click(object sender, RoutedEventArgs e)
        {
            await RefreshDiffAsync();
        }
    }
}
