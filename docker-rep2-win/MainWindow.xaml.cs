using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Net.Sockets;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Hardcodet.Wpf.TaskbarNotification;

namespace docker_rep2_win
{
    /// <summary>
    /// 常駐ユーティリティ（設定・起動管理）用のメインウィンドウ
    /// </summary>
    public partial class MainWindow : Window
    {
        private TaskbarIcon? _taskbarIcon;
        private ImageSource? _originalIcon;
        private ImageSource? _invertedIcon;
        private MenuItem? _openBrowserMenuItem;
        private MenuItem? _startMenuItem;
        private MenuItem? _stopMenuItem;
        private MenuItem? _configMenuItem;
        private MenuItem? _terminalMenuItem;
        private MenuItem? _versionMenuItem;
        private RelayCommand? _showMenuCommand;
        private RelayCommand? _focusCommand;
        private bool _forceExit = false;
        private bool _isRunning = false;

        public MainWindow(StartupInfo startupInfo)
        {
            InitializeComponent();
            SetWindowIcon();

            _focusCommand = new RelayCommand(_ => { Show(); Activate(); });

            StartIpcServer();

            var app = (App)Application.Current;
            if (app.Monitor != null)
            {
                app.Monitor.StatusChanged += OnStatusChanged;
                _isRunning = app.Monitor.IsRunning;
            }

            if (startupInfo.Mode == AppMode.SilentStart)
            {
                RunSilentStart();
            }
            else if (startupInfo.Mode == AppMode.BackgroundStart)
            {
                RunBackgroundStart();
            }
            else
            {
                NavigateToPage(startupInfo.Mode);
            }
        }

        private void OnStatusChanged(object? sender, bool isRunning)
        {
            Dispatcher.Invoke(() => {
                _isRunning = isRunning;
                if (_taskbarIcon != null)
                {
                    _taskbarIcon.IconSource = isRunning ? _originalIcon : _invertedIcon;
                    _taskbarIcon.ToolTipText = $"{AppInfo.AppFullName} ({(isRunning ? "実行中" : "停止中")})";
                    _taskbarIcon.Visibility = Visibility.Visible;
                }
                UpdateMenuStates();
            });
        }

        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);
            SetWindowIcon();
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

        protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
        {
            var app = (App)Application.Current;
            if (_forceExit)
            {
                if (app.Monitor != null) app.Monitor.StatusChanged -= OnStatusChanged;
                _taskbarIcon?.Dispose();
                base.OnClosing(e);
                Application.Current.Shutdown();
                return;
            }

            if (app.Settings.User.RunInBackground)
            {
                e.Cancel = true;
                InitializeTaskbarIcon();
                Hide();
                UpdateMenuStates();
            }
            else
            {
                if (app.Monitor != null) app.Monitor.StatusChanged -= OnStatusChanged;
                _taskbarIcon?.Dispose();
                base.OnClosing(e);
                Dispatcher.BeginInvoke(new Action(() => {
                    app.CheckExitCondition();
                }), System.Windows.Threading.DispatcherPriority.ContextIdle);
            }
        }

        private void NavigateToPage(AppMode mode)
        {
            switch (mode)
            {
                case AppMode.Config:
                    MainFrame.Navigate(new PageConfig2(true));
                    break;
                case AppMode.Start:
                    MainFrame.Navigate(new PageLauncher());
                    break;
                case AppMode.Stop:
                    MainFrame.Navigate(new PageStop());
                    break;
                case AppMode.Menu:
                    MainFrame.Navigate(new PageMenu());
                    break;
            }
        }

        private async void RunSilentStart()
        {
            var launcher = new LauncherService();
            await launcher.RunMaintenanceAndLaunchAsync();
            
            var app = (App)Application.Current;
            if (app.Settings.User.RunInBackground)
            {
                InitializeTaskbarIcon();
            }
            else
            {
                Application.Current.Shutdown();
            }
        }

        private void RunBackgroundStart()
        {
            var app = (App)Application.Current;
            if (app.Settings.User.RunInBackground)
            {
                InitializeTaskbarIcon();
            }
        }

        // --- 常駐・IPC・監視関連 ---

        private async void StartIpcServer()
        {
            while (true)
            {
                try
                {
                    using var server = new NamedPipeServerStream($"{AppInfo.AppShortName}-pipe", PipeDirection.In, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
                    await server.WaitForConnectionAsync();

                    using var reader = new StreamReader(server);
                    string command = await reader.ReadToEndAsync();

                    Dispatcher.Invoke(() => {
                        try { HandleIpcCommand(command); }
                        catch (Exception) { }
                    });
                }
                catch
                {
                    await Task.Delay(1000);
                }
            }
        }

        private void HandleIpcCommand(string command)
        {
            switch (command)
            {
                case "SHOW_MAIN":
                    Show();
                    Activate();
                    UpdateMenuStates();
                    MainFrame.Navigate(new PageLauncher());
                    break;
                case "SHOW_CONFIG":
                    Show();
                    Activate();
                    UpdateMenuStates();
                    MainFrame.Navigate(new PageConfig2(true));
                    break;
                case "STOP_WSL":
                    Show();
                    Activate();
                    UpdateMenuStates();
                    MainFrame.Navigate(new PageStop());
                    break;
                case "SHOW_MENU":
                    Show();
                    Activate();
                    UpdateMenuStates();
                    MainFrame.Navigate(new PageMenu());
                    break;
                case "SHOW_VERSION":
                    ShowVersionWindow();
                    break;
                case "EXIT_APP":
                    _forceExit = true;
                    Application.Current.Shutdown();
                    break;
            }
        }

        private Window? _versionWindow;
        private void ShowVersionWindow()
        {
            if (_versionWindow != null)
            {
                _versionWindow.Activate();
                return;
            }

            _versionWindow = new VersionWindow();
            _versionWindow.Closed += (s, e) => _versionWindow = null;
            _versionWindow.Show();
        }

        public void OnLaunchCompleted() => this.Close();
        public void OnStopCompleted() => this.Close();

        private void InitializeTaskbarIcon()
        {
            if (_taskbarIcon != null) return;

            _taskbarIcon = new TaskbarIcon();
            try
            {
                var iconUri = new Uri("pack://application:,,,/rep2.ico");
                _originalIcon = BitmapFrame.Create(iconUri);
            }
            catch { }

            if (_originalIcon is BitmapSource source)
            {
                _invertedIcon = InvertIcon(source);
                _taskbarIcon.IconSource = _originalIcon;
            }

            _taskbarIcon.ToolTipText = $"{AppInfo.AppFullName} ({(_isRunning ? "実行中" : "停止中")})";
            _taskbarIcon.IconSource = _isRunning ? _originalIcon : _invertedIcon;
            _taskbarIcon.Visibility = Visibility.Visible;
            
            _showMenuCommand = new RelayCommand(_ => HandleIpcCommand("SHOW_MENU"));
            _taskbarIcon.LeftClickCommand = _showMenuCommand;

            _openBrowserMenuItem = new MenuItem { Header = "ブラウザで開く", Command = new RelayCommand(_ => OpenBrowser()) };
            _configMenuItem = new MenuItem { Header = "設定", Command = new RelayCommand(_ => HandleIpcCommand("SHOW_CONFIG")) };
            _startMenuItem = new MenuItem { Header = "開始", Command = new RelayCommand(_ => HandleIpcCommand("SHOW_MAIN")) };
            _stopMenuItem = new MenuItem { Header = "停止", Command = new RelayCommand(_ => HandleIpcCommand("STOP_WSL")) };
            _terminalMenuItem = new MenuItem { Header = "ターミナルを開く", Command = new RelayCommand(_ => OpenTerminal()) };
            _versionMenuItem = new MenuItem { Header = "バージョン情報", Command = new RelayCommand(_ => HandleIpcCommand("SHOW_VERSION")) };
            var exitMenuItem = new MenuItem { Header = "終了", Command = new RelayCommand(_ => HandleIpcCommand("EXIT_APP")) };

            var menu = new ContextMenu();
            menu.Opened += (s, e) => UpdateMenuStates();
            menu.Items.Add(_openBrowserMenuItem);
            menu.Items.Add(new Separator());
            menu.Items.Add(_configMenuItem);
            menu.Items.Add(new Separator());
            menu.Items.Add(_startMenuItem);
            menu.Items.Add(_stopMenuItem);
            menu.Items.Add(new Separator());
            menu.Items.Add(_terminalMenuItem);
            menu.Items.Add(_versionMenuItem);
            menu.Items.Add(exitMenuItem);
            _taskbarIcon.ContextMenu = menu;
        }

        private void UpdateMenuStates()
        {
            bool isVisible = this.IsVisible;

            if (_taskbarIcon != null)
            {
                if (_taskbarIcon.ContextMenu != null)
                {
                    _taskbarIcon.ContextMenu.IsEnabled = !isVisible;
                }
                _taskbarIcon.LeftClickCommand = isVisible ? _focusCommand : _showMenuCommand;
            }

            if (_openBrowserMenuItem != null) _openBrowserMenuItem.IsEnabled = _isRunning;
            if (_terminalMenuItem != null) _terminalMenuItem.IsEnabled = _isRunning;
            if (_startMenuItem != null) _startMenuItem.IsEnabled = !_isRunning;
            if (_stopMenuItem != null) _stopMenuItem.IsEnabled = _isRunning;
        }

        private ImageSource InvertIcon(BitmapSource source)
        {
            var converted = new FormatConvertedBitmap(source, PixelFormats.Bgra32, null, 0);
            int width = converted.PixelWidth;
            int height = converted.PixelHeight;
            int stride = width * 4;
            byte[] pixels = new byte[height * stride];
            converted.CopyPixels(pixels, stride, 0);

            for (int i = 0; i < pixels.Length; i += 4)
            {
                pixels[i] = (byte)(255 - pixels[i]);         // B
                pixels[i + 1] = (byte)(255 - pixels[i + 1]); // G
                pixels[i + 2] = (byte)(255 - pixels[i + 2]); // R
            }

            return BitmapSource.Create(width, height, converted.DpiX, converted.DpiY, PixelFormats.Bgra32, null, pixels, stride);
        }

        private void OpenBrowser()
        {
            var settings = ((App)Application.Current).Settings;
            int port = settings.Port;
            string url = port == 80 ? "http://localhost" : $"http://localhost:{port}";
            Process.Start(new ProcessStartInfo { FileName = url, UseShellExecute = true });
        }

        private void OpenTerminal()
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
        }
    }

    public class RelayCommand : System.Windows.Input.ICommand
    {
        private readonly Action<object?> _execute;
        public RelayCommand(Action<object?> execute) => _execute = execute;
        public bool CanExecute(object? parameter) => true;
        public void Execute(object? parameter) => _execute(parameter);
        public event EventHandler? CanExecuteChanged { add { } remove { } }
    }
}
