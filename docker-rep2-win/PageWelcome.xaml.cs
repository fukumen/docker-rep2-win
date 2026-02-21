using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace docker_rep2_win
{
    public partial class PageWelcome : Page
    {
        private bool _wslInstalled = false;
        private bool _wslVersionOk = false;

        public PageWelcome()
        {
            InitializeComponent();
            RunCheck();
        }

        private void Page_Loaded(object sender, RoutedEventArgs e)
        {
            BtnCancel.Focus();
        }

        private async void RunCheck()
        {
            TxtError.Text = "システム環境をチェック中...";
            
            var (isOsOk, wsl, isWslVersionOk, virt, biosVirt) = await WslService.CheckSystemRequirements();

            _wslInstalled = wsl;
            _wslVersionOk = isWslVersionOk;

            if (isOsOk) { IconOS.Text = "✅"; IconOS.Foreground = Brushes.Green; }
            else        { IconOS.Text = "❌"; IconOS.Foreground = Brushes.Red; }

            if (biosVirt) { IconBios.Text = "✅"; IconBios.Foreground = Brushes.Green; }
            else          { IconBios.Text = "❌"; IconBios.Foreground = Brushes.Red; }

            if (virt) { IconVirt.Text = "✅"; IconVirt.Foreground = Brushes.Green; }
            else      { IconVirt.Text = "❌"; IconVirt.Foreground = Brushes.Red; }

            if (wsl && isWslVersionOk) { IconWsl.Text = "✅"; IconWsl.Foreground = Brushes.Green; }
            else                       { IconWsl.Text = "❌"; IconWsl.Foreground = Brushes.Red; }

            if (isOsOk && biosVirt && virt && wsl && isWslVersionOk)
            {
                // 合格
                var (configExists, configNeedsUpdate) = await WslService.CheckWslConfigStatus();

                if (configExists && configNeedsUpdate)
                {
                    TxtError.Text = """
                        システム要件を満たしています。

                        【注意】
                        本アプリはWSL2の「Mirrored Mode」を使用します。
                        インストール時に .wslconfig を更新しますが、この設定は
                        PC内の全てのWSLディストリビューションに影響します。
                        (既存のWSL環境のネットワーク挙動が変わる可能性があります)
                        """;
                    TxtError.Foreground = Brushes.DarkOrange;
                }
                else
                {
                    TxtError.Text = "システム要件を満たしています。";
                    TxtError.Foreground = Brushes.Blue;
                }
                BtnEasy.IsEnabled = true;
                BtnCustom.IsEnabled = true;
                BtnFix.IsEnabled = false;
                BtnEasy.Focus();
            }
            else
            {
                // 不合格
                var sb = new System.Text.StringBuilder();

                // 1. Windows
                if (!isOsOk) sb.AppendLine("・Windows 11 バージョン 22H2 (Build 22621) 以降が必要です。");

                // 2. BIOS
                if (!biosVirt)
                {
                    sb.AppendLine("・BIOS/UEFI設定で仮想化支援機能 (VT-x/SVM) が無効になっています。");
                    sb.AppendLine("  PCを再起動してBIOS設定を有効にしてください。");
                }

                // 3. 仮想化
                if (!virt && biosVirt) sb.AppendLine("・Windowsの機能「仮想マシンプラットフォーム」が有効になっていません。");
                
                // 4. WSL
                if (!wsl) sb.AppendLine("・WSLがインストールされていません。");
                else if (!isWslVersionOk) sb.AppendLine("・WSLのバージョンが古いか、Store版ではありません (2.0.0以上が必要)。");
                
                if (!isOsOk)
                {
                    sb.Append("このアプリはWSL2のMirrored Modeを利用するため、Windows10では動作しません。");
                }
                else
                {
                    if (!biosVirt)
                        sb.Append("BIOS/UEFI設定の変更が必要です。");
                    else
                        sb.Append("「不足機能を有効化する」ボタンを押して設定を行ってください。設定後、PCの再起動が必要です。");
                }
                
                TxtError.Text = sb.ToString();
                TxtError.Foreground = Brushes.Red;
                BtnEasy.IsEnabled = false;
                BtnCustom.IsEnabled = false;
                BtnFix.IsEnabled = (!wsl || !isWslVersionOk || (!virt && biosVirt));
                if (BtnFix.IsEnabled) BtnFix.Focus();
            }
        }

        private void BtnFix_Click(object sender, RoutedEventArgs e)
        {
            bool isUpdate = _wslInstalled && !_wslVersionOk;
            WslService.EnableWslFeatures(isUpdate);
            MessageBox.Show("機能の有効化を開始しました。完了後、PCを再起動してから再度このインストーラーを実行してください。", "再起動の必要性");
            Window.GetWindow(this)?.Close();
        }

        private void BtnEasy_Click(object sender, RoutedEventArgs e)
        {
            var settings = ((App)Application.Current).Settings;
            int httpPort = settings.Port;
            int monitorPort = settings.User.MonitorPort;

            bool httpInUse = PortService.IsPortInUse(httpPort);
            bool monitorInUse = PortService.IsPortInUse(monitorPort);

            if (httpInUse || monitorInUse)
            {
                var sb = new System.Text.StringBuilder();
                sb.AppendLine("以下のポート番号が既に他のアプリケーションで使用されているため、「おまかせインストール」は行えません。");
                sb.AppendLine();
                if (httpInUse) sb.AppendLine($"・HTTPポート: {httpPort}");
                if (monitorInUse) sb.AppendLine($"・WSLモニターポート: {monitorPort}");
                sb.AppendLine();
                sb.AppendLine("「上級者向けインストール」を選択し、詳細設定で別のポート番号を指定してください。");

                MessageBox.Show(sb.ToString(), "ポート番号の競合", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            NavigationService.Navigate(new PageConfig(isEasyMode: true));
        }

        private void BtnCustom_Click(object sender, RoutedEventArgs e)
        {
            NavigationService.Navigate(new PageConfig(isEasyMode: false));
        }

        private void BtnCancel_Click(object sender, RoutedEventArgs e)
        {
            Window.GetWindow(this)?.Close();
        }
    }
}
        