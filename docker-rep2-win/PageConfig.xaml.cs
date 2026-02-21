using Microsoft.Win32;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Navigation;

namespace docker_rep2_win
{
    /// <summary>
    /// PageConfig.xaml の相互作用ロジック
    /// </summary>
    public partial class PageConfig : Page
    {
        private readonly bool _isEasyMode;
        private List<VersionInfo>? _versions;

        public PageConfig(bool isEasyMode = false)
        {
            InitializeComponent();
            _isEasyMode = isEasyMode;
            TxtInstallPath.IsReadOnly = false;
            TxtDataPath.IsReadOnly = false;

            if (_isEasyMode)
            {
                BtnNext.Content = "インストール開始";
                BtnNext.IsEnabled = false; // バージョン取得まで無効化
                TxtAlpineStatus.Visibility = Visibility.Visible;
                InitializeVersions();
            }

            LoadSettings();
        }

        private void Page_Loaded(object sender, RoutedEventArgs e)
        {
            BtnNext.Focus();
        }

        private async void InitializeVersions()
        {
            TxtAlpineStatus.Text = "Alpine Linux の情報を取得しています...";
            _versions = await VersionProvider.FetchVersionsAsync();
            
            var toSelect = _versions?.FirstOrDefault(v => v.IsTested) ?? _versions?.FirstOrDefault();
            if (toSelect != null)
            {
                TxtAlpineStatus.Text = $"Alpine Linux バージョン {toSelect.Version} を使用します。";
            }
            else
            {
                TxtAlpineStatus.Text = "Alpine Linux のバージョン取得に失敗しました。";
            }

            BtnNext.IsEnabled = true;
            BtnNext.Focus();
        }

        private void LoadSettings()
        {
            var settings = ((App)Application.Current).Settings;

            // おまかせモードの場合は詳細設定を非表示にする
            if (_isEasyMode)
            {
                AdvancedSettingsArea.Visibility = Visibility.Collapsed;
            }
            
            // インストール先
            if (!string.IsNullOrEmpty(settings.InstallPath))
            {
                TxtInstallPath.Text = settings.InstallPath;
            }
            else
            {
                string progFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
                TxtInstallPath.Text = Path.Combine(progFiles, AppInfo.AppShortName);
            }

            // データ保存先
            if (!string.IsNullOrEmpty(settings.DataPath))
            {
                TxtDataPath.Text = settings.DataPath;
            }
            else
            {
                string docs = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
                TxtDataPath.Text = Path.Combine(docs, "rep2-data");
            }

            TxtPort.Text = settings.Port.ToString();
            ChkOpenFirewall.IsChecked = settings.OpenFirewall;
            ChkKeepWslRunning.IsChecked = settings.KeepWslRunning;
            ChkHostAddressLoopback.IsChecked = settings.HostAddressLoopback;
        }

        private void BtnBrowseInstall_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new OpenFolderDialog { Title = "インストール先フォルダを選択", Multiselect = false };
            string currentPath = TxtInstallPath.Text;
            if (!string.IsNullOrEmpty(currentPath))
            {
                try {
                    string? parent = Path.GetDirectoryName(currentPath);
                    if (!string.IsNullOrEmpty(parent) && Directory.Exists(parent)) dialog.InitialDirectory = parent;
                } catch { }
            }
            if (string.IsNullOrEmpty(dialog.InitialDirectory)) dialog.InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);

            if (dialog.ShowDialog() == true) TxtInstallPath.Text = Path.Combine(dialog.FolderName, AppInfo.AppShortName);
        }

        private void BtnBrowseData_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new OpenFolderDialog { Title = "データ保存先フォルダ(rep2-data)を選択", Multiselect = false };
            string currentPath = TxtDataPath.Text;
            if (!string.IsNullOrEmpty(currentPath))
            {
                try {
                    string? parent = Path.GetDirectoryName(currentPath);
                    if (!string.IsNullOrEmpty(parent) && Directory.Exists(parent)) dialog.InitialDirectory = parent;
                } catch { }
            }
            if (string.IsNullOrEmpty(dialog.InitialDirectory)) dialog.InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);

            if (dialog.ShowDialog() == true) TxtDataPath.Text = Path.Combine(dialog.FolderName, "rep2-data");
        }

        private void BtnBack_Click(object sender, RoutedEventArgs e)
        {
            if (NavigationService.CanGoBack) NavigationService.GoBack();
        }

        private void BtnCancel_Click(object sender, RoutedEventArgs e)
        {
            Window.GetWindow(this)?.Close();
        }

        private async void BtnNext_Click(object sender, RoutedEventArgs e)
        {
            var settings = ((App)Application.Current).Settings;
            if (int.TryParse(TxtPort.Text, out int port))
            {
                if (PortService.IsPortInUse(port))
                {
                    MessageBox.Show($"HTTPポート番号 {port} は既に他のアプリケーションで使用されています。別のポート番号を指定してください。", "ポート使用中", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }
                settings.Port = port;
            }
            else
            {
                MessageBox.Show("ポート番号には有効な数字を入力してください。", "入力エラー", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            settings.OpenFirewall = ChkOpenFirewall.IsChecked ?? false;
            settings.KeepWslRunning = ChkKeepWslRunning.IsChecked ?? true;
            settings.HostAddressLoopback = ChkHostAddressLoopback.IsChecked ?? true;
            settings.InstallPath = TxtInstallPath.Text;
            settings.DataPath = TxtDataPath.Text;

            if (!ValidateDirectories()) return;

            // ディレクトリ確定後、既存の設定ファイルがあれば読み込む
            settings.Load();

            if (_isEasyMode)
            {
                var toSelect = _versions?.FirstOrDefault(v => v.IsTested) ?? _versions?.FirstOrDefault();
                if (toSelect != null)
                {
                    settings.User.SelectedVersion = toSelect.Version;
                    settings.DownloadUrl = toSelect.Url;
                    settings.SelectedHash = toSelect.Hash;
                }
                
                NavigationService.Navigate(new PageInstall(AppMode.Install));
            }
            else
            {
                NavigationService.Navigate(new PageConfig2());
            }
        }

        private bool ValidateDirectories()
        {
            var settings = ((App)Application.Current).Settings;

            // インストール先のチェック
            if (Directory.Exists(settings.InstallPath) && Directory.EnumerateFileSystemEntries(settings.InstallPath).Any())
            {
                var result = MessageBox.Show(
                    $"インストール先 '{settings.InstallPath}' は空ではありません。\n既存のファイルを削除してインストールを続行しますか？",
                    "インストール先の確認",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Warning);

                if (result != MessageBoxResult.Yes) return false;
            }

            // データ保存先の確認
            if (Directory.Exists(settings.DataPath) && Directory.EnumerateFileSystemEntries(settings.DataPath).Any())
            {
                MessageBox.Show(
                    $"データ保存先 '{settings.DataPath}' には既にデータが存在します。\n既存のデータをそのまま利用します。",
                    "データ保存先の確認",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
            }

            return true;
        }
    }
}
