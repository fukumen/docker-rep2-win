using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Threading;
using Windows.Win32;

namespace docker_rep2_win
{
    /// <summary>
    /// Interaction logic for App.xaml
    /// </summary>
    public partial class App : Application
    {
        private static System.Threading.Mutex? _mutex;

        public AppSettings Settings { get; } = new AppSettings();

        public StatusMonitor? Monitor { get; private set; }

        protected override void OnStartup(StartupEventArgs e)
        {
            // ダイアログを閉じた瞬間にアプリが終了しないように、明示的な終了モードに設定
            ShutdownMode = ShutdownMode.OnExplicitShutdown;

            PInvoke.SetCurrentProcessExplicitAppUserModelID(AppInfo.AppShortName);

            Settings.Load();

            Monitor = new StatusMonitor(Settings.User.MonitorPort);

            base.OnStartup(e);
            
            // グローバル例外ハンドラの設定
            DispatcherUnhandledException += OnDispatcherUnhandledException;

            var startupInfo = StartupService.AnalyzeStartup(e.Args);

            Logger.Debug($"AppStartup: Mode={startupInfo.Mode}, State={startupInfo.State}, IsAdmin={StartupService.IsAdministrator()}, Args={string.Join(" ", e.Args)}");

            // システム状態に基づいて最終的な AppMode を確定
            switch (startupInfo.State)
            {
                case SystemState.NotInstalled:
                    startupInfo.Mode = AppMode.Install;
                    break;

                case SystemState.Installed_Ready:
                    startupInfo.Mode = AppMode.Menu;
                    break;

                case SystemState.Installed_NeedsSetup:
                    if (MessageBox.Show(
                        $"{AppInfo.AppShortName} の実行環境（このユーザー用の設定や仮想環境）を構築、または修復する必要があります。\n\nユーザーセットアップを開始しますか？",
                        "ユーザーセットアップの確認",
                        MessageBoxButton.YesNo,
                        MessageBoxImage.Information) != MessageBoxResult.Yes)
                    {
                        Shutdown();
                        return;
                    }
                    startupInfo.Mode = AppMode.UserSetup;
                    break;

                case SystemState.UpdateAvailable:
                    var updateWindow = new UpdateChoiceWindow();
                    updateWindow.ShowDialog();

                    if (updateWindow.Result == UpdateChoice.Update)
                    {
                        startupInfo.Mode = AppMode.Update;
                    }
                    else if (updateWindow.Result == UpdateChoice.Uninstall)
                    {
                        startupInfo.Mode = AppMode.Uninstall;
                    }
                    else
                    {
                        Shutdown();
                        return;
                    }
                    break;

                case SystemState.UpdateBlocked:
                    MessageBox.Show(
                        $"既に {AppInfo.AppShortName} がインストールされていますが、既存の実行環境が不完全です。\n\n" +
                        "インストール済みのプログラムを起動してユーザーセットアップを完了させるか、一旦  {AppInfo.AppShortName} をアンインストールしてください。",
                        "既存環境の不備",
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning);
                    Shutdown();
                    return;

                case SystemState.Manual:
                    // 引数で明示的に指定されている場合はそのまま進む
                    break;
            }

            // 常駐プロセスへの委譲を試みる
            if (TryDelegateToRunningInstance(startupInfo))
            {
                Logger.Debug("Delegated to running instance. Shutting down.");
                Shutdown();
                return;
            }

            // 二重起動防止
            if (!AcquireMutex())
            {
                Logger.Debug("Failed to acquire mutex. App is already running.");
                MessageBox.Show($"{AppInfo.AppShortName} は既に起動しています。", "二重起動エラー", MessageBoxButton.OK, MessageBoxImage.Warning);
                Shutdown();
                return;
            }

            // モード確定後に管理者権限が必要か再評価
            startupInfo.IsAdminRequired = StartupService.RequiresAdmin(startupInfo.Mode);

            // 管理者権限チェック
            if (startupInfo.IsAdminRequired && !StartupService.IsAdministrator())
            {
                RestartAsAdminAndShutdown(StartupService.GetArgumentString(startupInfo.Mode));
                return;
            }

            Monitor?.Start();

            if (startupInfo.Mode == AppMode.Version)
            {
                var versionWindow = new VersionWindow();
                versionWindow.ShowDialog();
                Shutdown();
            }
            else if (startupInfo.Mode is AppMode.Install or AppMode.Uninstall or AppMode.Update or AppMode.UserSetup)
            {
                var wizardWindow = new WizardWindow(startupInfo.Mode);
                wizardWindow.Show();
            }
            else
            {
                var mainWindow = new MainWindow(startupInfo);

                if (startupInfo.Mode != AppMode.SilentStart && startupInfo.Mode != AppMode.BackgroundStart)
                {
                    mainWindow.Show();
                }
            }
        }

        public void UpdateMonitorPort(int port)
        {
            if (Monitor != null)
            {
                Monitor.Port = port;
            }
        }

        public void CheckExitCondition()
        {
            // 他に表示されているウィンドウがあるか確認
            bool hasVisibleWindows = false;
            foreach (Window window in Windows)
            {
                if (window.IsVisible)
                {
                    hasVisibleWindows = true;
                    break;
                }
            }

            if (hasVisibleWindows) return;

            // すべてのウィンドウが閉じている、または非表示の場合：
            // 「バックグラウンド実行設定」が有効、かつ「常駐を管理する本体（MainWindow）が存在する」
            // という条件を満たさない限り、アプリを完全に終了する。
            bool isResidentCapable = false;
            foreach (Window window in Windows)
            {
                if (window is MainWindow)
                {
                    isResidentCapable = true;
                    break;
                }
            }

            if (Settings.User.RunInBackground && isResidentCapable)
            {
                return;
            }

            Shutdown();
        }

        public static void ReleaseMutex()
        {
            _mutex?.Dispose();
            _mutex = null;
        }

        private bool AcquireMutex()
        {
            string mutexName = $"Global\\{AppInfo.AppShortName}-installer-mutex";
            bool createdNew = false;
            
            for (int i = 0; i < 5; i++)
            {
                try
                {
                    _mutex = new System.Threading.Mutex(true, mutexName, out createdNew);
                    if (createdNew)
                    {
                        Logger.Debug($"Acquired mutex: {mutexName}");
                        return true;
                    }

                    _mutex.Dispose();
                    _mutex = null;
                }
                catch (UnauthorizedAccessException ex)
                {
                    Logger.Debug($"Mutex access denied: {ex.Message}");
                    createdNew = false;
                }
                catch (Exception ex)
                {
                    Logger.Debug($"Mutex error: {ex.Message}");
                    createdNew = false;
                }
                System.Threading.Thread.Sleep(500);
            }
            return false;
        }

        private bool TryDelegateToRunningInstance(StartupInfo info)
        {
            var ipcConfig = StartupService.GetIpcConfig(info.Mode);
            bool sent = SendIpcCommand(ipcConfig.Command);

            // ExitOnSuccess が true かつ送信に成功した場合は、常駐プロセスに任せて終了する (Shutdown() する)
            // それ以外（送信失敗、または ExitOnSuccess が false）は、現在のプロセスで処理を続行する
            return ipcConfig.ExitOnSuccess && sent;
        }

        private bool SendIpcCommand(string command)
        {
            string pipeName = $"{AppInfo.AppShortName}-pipe";
            try
            {
                using var client = new NamedPipeClientStream(".", pipeName, PipeDirection.Out);
                client.Connect(500); // 0.5秒だけ待つ

                using var writer = new StreamWriter(client);
                writer.AutoFlush = true;
                writer.Write(command);
                Logger.Debug($"Sent IPC command: {command}");
                return true;
            }
            catch (TimeoutException)
            {
                Logger.Debug($"IPC connection timeout: {pipeName}");
                return false;
            }
            catch (Exception ex)
            {
                Logger.Debug($"IPC connection error: {ex.Message}");
                return false;
            }
        }

        private void RestartAsAdminAndShutdown(string arguments)
        {
            if (Debugger.IsAttached)
            {
                MessageBox.Show(
                    "デバッグを行う場合は、Visual Studio を管理者として実行してからデバッグを開始してください。",
                    "デバッグ中の権限エラー",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                Shutdown();
                return;
            }

            var startInfo = new ProcessStartInfo
            {
                FileName = AppInfo.CurrentExePath,
                Arguments = arguments,
                UseShellExecute = true,
                Verb = "runas" // 管理者として実行
            };

            try
            {
                Process.Start(startInfo);
            }
            catch
            {
                MessageBox.Show(
                    "管理者権限が必要です。操作はキャンセルされました。",
                    "権限エラー",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
            }

            Shutdown();
        }

        private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
        {
            Logger.Log(e.Exception, "Unhandled Exception");
            MessageBox.Show($"予期せぬエラーが発生しました:\n{e.Exception.Message}\n\n{e.Exception.StackTrace}", 
                            "起動エラー", MessageBoxButton.OK, MessageBoxImage.Error);
            e.Handled = true;
            Shutdown();
        }
    }
}
