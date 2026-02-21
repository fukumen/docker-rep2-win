using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Input;

namespace docker_rep2_win
{
    public partial class PageMenu : Page
    {
        public PageMenu()
        {
            InitializeComponent();
            Loaded += PageMenu_Loaded;
            Unloaded += PageMenu_Unloaded;
        }

        private void PageMenu_Loaded(object sender, RoutedEventArgs e)
        {
            var app = (App)Application.Current;
            if (app.Monitor != null)
            {
                app.Monitor.StatusChanged += OnStatusChanged;
                UpdateUI(app.Monitor.IsRunning);
            }

            // 初期フォーカスの設定 (描画完了後に実行)
            Dispatcher.BeginInvoke(new Action(() => {
                IInputElement target = BtnBrowser.IsEnabled ? BtnBrowser : (BtnStart.IsVisible ? BtnStart : BtnStop);
                if (target != null)
                {
                    Keyboard.Focus(target);
                    FocusManager.SetFocusedElement(this, target);
                }
            }), System.Windows.Threading.DispatcherPriority.Render);
        }

        private void PageMenu_Unloaded(object sender, RoutedEventArgs e)
        {
            var app = (App)Application.Current;
            if (app.Monitor != null)
            {
                app.Monitor.StatusChanged -= OnStatusChanged;
            }
        }

        private void OnStatusChanged(object? sender, bool isRunning)
        {
            Dispatcher.Invoke(() => UpdateUI(isRunning));
        }

        private void UpdateUI(bool isRunning)
        {
            if (isRunning)
            {
                if (BtnStart.IsFocused)
                {
                    Dispatcher.BeginInvoke(new Action(() => BtnBrowser.Focus()), System.Windows.Threading.DispatcherPriority.Render);
                }
            }
            else
            {
                if (BtnStop.IsFocused || BtnBrowser.IsFocused || BtnTerminal.IsFocused)
                {
                    Dispatcher.BeginInvoke(new Action(() => BtnStart.Focus()), System.Windows.Threading.DispatcherPriority.Render);
                }
            }

            BtnStart.Visibility = isRunning ? Visibility.Collapsed : Visibility.Visible;
            BtnStop.Visibility = isRunning ? Visibility.Visible : Visibility.Collapsed;
            
            BtnBrowser.IsEnabled = isRunning;
            BtnTerminal.IsEnabled = isRunning;

            if (isRunning)
            {
                StatusIndicator.Background = new SolidColorBrush(Color.FromRgb(0xE9, 0xF5, 0xE9));
                StatusDot.Fill = new SolidColorBrush(Color.FromRgb(0x4C, 0xAF, 0x50));
                StatusLabel.Text = "起動中";
                StatusLabel.Foreground = new SolidColorBrush(Color.FromRgb(0x2E, 0x7D, 0x32));
            }
            else
            {
                StatusIndicator.Background = new SolidColorBrush(Color.FromRgb(0xF5, 0xE9, 0xE9));
                StatusDot.Fill = new SolidColorBrush(Color.FromRgb(0xF4, 0x43, 0x36));
                StatusLabel.Text = "停止中";
                StatusLabel.Foreground = new SolidColorBrush(Color.FromRgb(0xC6, 0x28, 0x28));
            }
        }

        private void Start_Click(object sender, RoutedEventArgs e)
        {
            if (Application.Current.MainWindow is MainWindow mw)
            {
                mw.MainFrame.Navigate(new PageLauncher());
            }
        }

        private void OpenBrowser_Click(object sender, RoutedEventArgs e)
        {
            var settings = ((App)Application.Current).Settings;
            int port = settings.Port;
            string url = port == 80 ? "http://localhost" : $"http://localhost:{port}";
            try
            {
                Process.Start(new ProcessStartInfo { FileName = url, UseShellExecute = true });
            }
            catch (Exception ex)
            {
                MessageBox.Show("ブラウザの起動に失敗しました: " + ex.Message, "エラー", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            Exit();
        }

        private void Config_Click(object sender, RoutedEventArgs e)
        {
            if (Application.Current.MainWindow is MainWindow mw)
            {
                mw.MainFrame.Navigate(new PageConfig2(true));
            }
        }

        private void Stop_Click(object sender, RoutedEventArgs e)
        {
            if (Application.Current.MainWindow is MainWindow mw)
            {
                mw.MainFrame.Navigate(new PageStop());
            }
        }

        private void Terminal_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                string wtPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), @"Microsoft\WindowsApps\wt.exe");
                if (File.Exists(wtPath))
                {
                    string wslPath = WslService.GetAppDataWslPath() ?? throw new Exception("設定ファイルの保存先が見つかりません。");
                    string args = $"-w 0 new-tab --title \"{AppInfo.AppShortName} Shell\" wsl -d {AppInfo.DistroName} --cd \"{wslPath}\"";
                    Process.Start(new ProcessStartInfo { FileName = wtPath, Arguments = args, UseShellExecute = true });
                }
                else
                {
                    string wslPath = WslService.GetAppDataWslPath() ?? "/";
                    Process.Start(new ProcessStartInfo { FileName = "wsl.exe", Arguments = $"-d {AppInfo.DistroName} --cd \"{wslPath}\"", UseShellExecute = true });
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show("ターミナルの起動に失敗しました: " + ex.Message, "エラー", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            Exit();
        }

        private void Version_Click(object sender, RoutedEventArgs e)
        {
            var versionWindow = new VersionWindow();
            versionWindow.Show();
            Exit();
        }

        private void Exit_Click(object sender, RoutedEventArgs e)
        {
            Exit();
        }

        private void Exit()
        {
            Application.Current.MainWindow?.Close();
        }
    }
}
