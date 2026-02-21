using System.Windows;
using System.Windows.Media.Imaging;

namespace docker_rep2_win
{
    /// <summary>
    /// インストーラー、アンインストーラー、アップデート、初期ユーザー設定のための専用ウィンドウ
    /// </summary>
    public partial class WizardWindow : Window
    {
        public WizardWindow(AppMode mode)
        {
            InitializeComponent();
            SetWindowIcon();
            NavigateToPage(mode);

            // ウィンドウが表示されたときに確実に最前面へ持ってくるための処理
            Loaded += (s, e) =>
            {
                this.Topmost = true;
                this.Activate();
                this.Topmost = false;
                this.Focus();
            };
        }

        private void SetWindowIcon()
        {
            try
            {
                var iconUri = new Uri("pack://application:,,,/rep2.ico");
                this.Icon = BitmapFrame.Create(iconUri);
            }
            catch { /* アイコン読み込み失敗時はデフォルトに従う */ }
        }

        private void NavigateToPage(AppMode mode)
        {
            switch (mode)
            {
                case AppMode.Install:
                    MainFrame.Navigate(new PageWelcome());
                    break;
                case AppMode.Uninstall:
                    MainFrame.Navigate(new PageUninstall());
                    break;
                case AppMode.Update:
                    MainFrame.Navigate(new PageInstall(AppMode.Update));
                    break;
                case AppMode.UserSetup:
                    MainFrame.Navigate(new PageInstall(AppMode.UserSetup));
                    break;
            }
        }

        protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
        {
            var app = (App)Application.Current;
            // インストール中、または設定変更中の場合の終了確認
            if (MainFrame.Content is PageInstall pageInstall)
            {
                if (app.Settings.User.NeedsWslReboot) // Reconfigure時（厳密にはNeedsWslRebootが立っている時）
                {
                    var res = MessageBox.Show(
                        "現在設定を更新中です。今終了すると環境が壊れる可能性があるため、完了まで待つことを強く推奨します。\n\nそれでも終了しますか？",
                        "警告", MessageBoxButton.YesNo, MessageBoxImage.Warning);
                    if (res != MessageBoxResult.Yes)
                    {
                        e.Cancel = true;
                        return;
                    }
                }
                else
                {
                    var res = MessageBox.Show(
                        "インストールを中断しますか？",
                        "中断の確認", MessageBoxButton.YesNo, MessageBoxImage.Question);
                    if (res != MessageBoxResult.Yes)
                    {
                        e.Cancel = true;
                        return;
                    }
                }
            }

            base.OnClosing(e);
            Dispatcher.BeginInvoke(new Action(() => {
                app.CheckExitCondition();
            }), System.Windows.Threading.DispatcherPriority.ContextIdle);
        }
    }
}
