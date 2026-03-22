using Microsoft.Win32;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Navigation;

namespace docker_rep2_win
{
    /// <summary>
    /// PageConfig2.xaml の相互作用ロジック
    /// </summary>
    public partial class PageConfig2 : Page
    {
        public bool IsConfigMode { get; }
        private readonly ConfigSessionContext _configContext = new();

        public PageConfig2(bool isConfigMode = false)
        {
            var settings = ((App)Application.Current).Settings;
            var user = settings.User;

            InitializeComponent();
            IsConfigMode = isConfigMode;

            BtnNext.IsEnabled = false;
            CmbVersions.IsEnabled = false;

            if (IsConfigMode)
            {
                _configContext.Load(settings.WindowsDataPath);

                TxtTitle.Text = "設定の変更";
                BtnNext.Content = "更新";
                BtnBack.Visibility = Visibility.Collapsed;
                PanelMaintenance.Visibility = Visibility.Visible;
                PanelAdvancedStartOptions.Visibility = Visibility.Visible;
                TxtConfigNotice.Visibility = Visibility.Collapsed;
            }

            TxtPhpMemoryLimit.Text = user.PhpMemoryLimit;
            TxtMonitorPort.Text = user.MonitorPort.ToString();
            ChkRunInBackground.IsChecked = user.RunInBackground;
            ChkAutoStart.IsChecked = user.AutoStart;
            ChkAutoLaunchBrowser.IsChecked = user.AutoLaunchBrowser;
            ChkAutoUpdateAlpine.IsChecked = user.AutoUpdateAlpine;
            ChkAutoUpdateApp.IsChecked = user.AutoUpdateApp;
            ChkDebugLogEnabled.IsChecked = user.DebugLogEnabled;

            // 選択中を選択することで表示させる
            if (CmbVersions.Items.Count > 0)
            {
                CmbVersions.SelectedIndex = 0;
            }

            CmbVersions.SelectionChanged += (s, e) => CheckChanges();
            TxtPhpMemoryLimit.TextChanged += (s, e) => CheckChanges();
            TxtMonitorPort.TextChanged += (s, e) => CheckChanges();
            ChkRunInBackground.Checked += (s, e) => CheckChanges();
            ChkRunInBackground.Unchecked += (s, e) => CheckChanges();
            ChkAutoStart.Checked += (s, e) => CheckChanges();
            ChkAutoStart.Unchecked += (s, e) => CheckChanges();
            ChkAutoLaunchBrowser.Checked += (s, e) => CheckChanges();
            ChkAutoLaunchBrowser.Unchecked += (s, e) => CheckChanges();
            ChkAutoUpdateAlpine.Checked += (s, e) => CheckChanges();
            ChkAutoUpdateAlpine.Unchecked += (s, e) => CheckChanges();
            ChkAutoUpdateApp.Checked += (s, e) => CheckChanges();
            ChkAutoUpdateApp.Unchecked += (s, e) => CheckChanges();
            ChkDebugLogEnabled.Checked += (s, e) => CheckChanges();
            ChkDebugLogEnabled.Unchecked += (s, e) => CheckChanges();

            _ = LoadVersions();
        }

        private void Page_Loaded(object sender, RoutedEventArgs e)
        {
            SetInitialFocus();
            CheckChanges();
        }

        private void SetInitialFocus()
        {
            if (BtnNext.IsEnabled)
            {
                BtnNext.Focus();
            }
            else if (BtnBack.Visibility == Visibility.Visible && BtnBack.IsEnabled)
            {
                BtnBack.Focus();
            }
            else
            {
                BtnCancel.Focus();
            }
        }

        private async Task LoadVersions()
        {
            var isRunning = ((App)Application.Current).Monitor?.IsRunning == true;

            // Webからのリスト取得と、WSLからの現行バージョン取得を並行して開始
            var fetchWebTask = VersionProvider.FetchVersionsAsync();
            var fetchWslTask = (isRunning && IsConfigMode && WslService.IsDistroInstalled()) 
                ? WslService.GetOsVersionAsync() 
                : Task.FromResult(string.Empty);

            // 両方の完了を待機
            await Task.WhenAll(fetchWebTask, fetchWslTask);

            var versions = await fetchWebTask;
            var actualWslVersion = await fetchWslTask;

            var settings = ((App)Application.Current).Settings;

            // WSLから実体バージョンが取れた場合は、メモリ上の設定を更新
            if (!string.IsNullOrEmpty(actualWslVersion))
            {
                settings.User.SelectedVersion = actualWslVersion;
            }

            var currentVersion = settings.User.SelectedVersion;
            var toSelect = versions.FirstOrDefault(v => string.Equals(v.Version, currentVersion, StringComparison.OrdinalIgnoreCase));

            // リストにない場合（古いバージョンを使用中など）は、リストに追加してそれを選択させる
            if (toSelect == null && !string.IsNullOrEmpty(currentVersion))
            {
                toSelect = new VersionInfo { Version = currentVersion, IsTested = false, IsCurrent = true };
                versions.Insert(0, toSelect);
            }
            else if (toSelect != null)
            {
                toSelect.IsCurrent = true;
            }

            // それでも決まらない場合は推奨バージョン
            if (toSelect == null)
            {
                toSelect = versions.FirstOrDefault(v => v.IsTested) ?? versions.FirstOrDefault();
            }

            CmbVersions.ItemsSource = null;
            CmbVersions.Items.Clear();
            foreach (var v in versions)
            {
                CmbVersions.Items.Add(v);
            }
            CmbVersions.IsEnabled = true;

            if (toSelect != null)
            {
                CmbVersions.SelectedItem = toSelect;
            }

            if (!IsConfigMode)
            {
                BtnNext.IsEnabled = true;
            }
            else
            {
                CheckChanges();
            }
            
            SetInitialFocus();
        }

        private void CheckChanges()
        {
            if (!IsConfigMode) return;

            if (!CmbVersions.IsEnabled)
            {
                BtnNext.IsEnabled = false;
                return;
            }

            var settings = ((App)Application.Current).Settings;
            var user = settings.User;

            // UI の値を現在の設定オブジェクトに一時的に反映（保存はまだされない）
            if (CmbVersions.SelectedItem is VersionInfo selectedVersion)
            {
                user.SelectedVersion = selectedVersion.Version;
            }
            user.PhpMemoryLimit = TxtPhpMemoryLimit.Text;
            if (int.TryParse(TxtMonitorPort.Text, out int port))
            {
                user.MonitorPort = port;
            }
            user.RunInBackground = ChkRunInBackground.IsChecked ?? false;
            user.AutoStart = ChkAutoStart.IsChecked ?? false;
            user.AutoLaunchBrowser = ChkAutoLaunchBrowser.IsChecked ?? false;
            user.AutoUpdateAlpine = ChkAutoUpdateAlpine.IsChecked ?? false;
            user.AutoUpdateApp = ChkAutoUpdateApp.IsChecked ?? false;
            user.DebugLogEnabled = ChkDebugLogEnabled.IsChecked ?? false;

            BtnNext.IsEnabled = user.HasAnyChanges || _configContext.IsChanged || user.PendingCertbotUpdate;
            BtnNext.Content = user.VersionChanged ? "更新(WSL再構築)" : "更新";
        }

        private void BtnBack_Click(object sender, RoutedEventArgs e)
        {
            if (NavigationService.CanGoBack)
            {
                NavigationService.GoBack();
            }
        }

        private void BtnCancel_Click(object sender, RoutedEventArgs e)
        {
            var user = ((App)Application.Current).Settings.User;

            if (_configContext.IsChanged || user.PendingCertbotUpdate)
            {
                var messages = new List<string>();
                messages.Add("以下の変更がアプリに反映されていません。");

                if (_configContext.IsChanged)
                {
                    messages.Add("・docker-compose.local.yml の変更は破棄されます。");
                }
                
                if (user.PendingCertbotUpdate)
                {
                    messages.Add("・Certbot設定の変更は未適用のまま保持され、次回以降の更新時に適用できます。");
                }

                messages.Add("\nキャンセルして設定画面を閉じますか？");

                var result = MessageBox.Show(string.Join("\n", messages), "確認", MessageBoxButton.YesNo, MessageBoxImage.Warning);
                if (result == MessageBoxResult.No)
                {
                    return;
                }
            }

            if (IsConfigMode)
            {
                Window.GetWindow(this)?.Close();
            }
            else
            {
                Application.Current.Shutdown();
            }
        }

        private void BtnShowDiff_Click(object sender, RoutedEventArgs e)
        {
            NavigationService.Navigate(new PageConfdiff());
        }

        private void BtnEditComposeLocal_Click(object sender, RoutedEventArgs e)
        {
            NavigationService.Navigate(new PageComposeLocal(_configContext));
        }

        private void BtnEditCertbot_Click(object sender, RoutedEventArgs e)
        {
            NavigationService.Navigate(new PageCertbot(_configContext));
        }

        private async void BtnUpdateAlpine_Click(object sender, RoutedEventArgs e)
        {
            var settings = ((App)Application.Current).Settings;
            var user = settings.User;
            try
            {
                UpdateOverlay.Visibility = Visibility.Visible;
                TxtUpdateStatus.Text = "Alpine Linux を更新中...";

                string versionBefore = await WslService.GetOsVersionAsync();

                var result = await WslService.UpgradeAlpineAsync();

                string versionAfter = await WslService.GetOsVersionAsync();

                bool isVersionChanged = !string.IsNullOrEmpty(versionBefore) && 
                                        !string.IsNullOrEmpty(versionAfter) && 
                                        versionBefore != versionAfter;

                if (isVersionChanged)
                {
                    await LoadVersions();
                    
                    user.Save();

                    MessageBox.Show($"Alpine Linux が更新されました。\nバージョン: {versionBefore} -> {versionAfter}", "完了", MessageBoxButton.OK, MessageBoxImage.Information);
                }
                else if (result.StdOut.Contains("Upgrading", StringComparison.OrdinalIgnoreCase))
                {
                    MessageBox.Show("Alpine Linux のパッケージが更新されました（OSバージョンに変更はありません）。", "完了", MessageBoxButton.OK, MessageBoxImage.Information);
                }
                else
                {
                    MessageBox.Show("Alpine Linux は既に最新です。", "完了", MessageBoxButton.OK, MessageBoxImage.Information);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show("Alpine Linux の更新に失敗しました:\n" + ex.Message, "エラー", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                UpdateOverlay.Visibility = Visibility.Collapsed;
                CheckChanges();
            }
        }

        private async void BtnUpdateApp_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                UpdateOverlay.Visibility = Visibility.Visible;
                TxtUpdateStatus.Text = $"{AppInfo.DockerName} を更新中...";
                var result = await DockerService.PullAsync();

                if (result.StdOut.Contains("Downloaded newer image", StringComparison.OrdinalIgnoreCase))
                {
                    MessageBox.Show($"{AppInfo.DockerName} が最新のイメージに更新されました。", "完了", MessageBoxButton.OK, MessageBoxImage.Information);
                }
                else
                {
                    MessageBox.Show($"{AppInfo.DockerName} は既に最新です。", "完了", MessageBoxButton.OK, MessageBoxImage.Information);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"{AppInfo.DockerName} の更新に失敗しました:\n" + ex.Message, "エラー", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                UpdateOverlay.Visibility = Visibility.Collapsed;
            }
        }

        private void BtnNext_Click(object sender, RoutedEventArgs e)
        {
            var settings = ((App)Application.Current).Settings;
            var user = settings.User;
            user.PhpMemoryLimit = TxtPhpMemoryLimit.Text;
            if (int.TryParse(TxtMonitorPort.Text, out int port))
            {
                if (!IsConfigMode && PortService.IsPortInUse(port))
                {
                    MessageBox.Show($"WSLモニターポートの番号 {port} は既に他のアプリケーションで使用されています。別のポート番号を指定してください。", "ポート使用中", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }
                user.MonitorPort = port;
            }
            user.RunInBackground = ChkRunInBackground.IsChecked ?? false;
            user.AutoStart = ChkAutoStart.IsChecked ?? false;
            user.AutoLaunchBrowser = ChkAutoLaunchBrowser.IsChecked ?? false;
            user.AutoUpdateAlpine = ChkAutoUpdateAlpine.IsChecked ?? false;
            user.AutoUpdateApp = ChkAutoUpdateApp.IsChecked ?? false;
            user.DebugLogEnabled = ChkDebugLogEnabled.IsChecked ?? false;

            if (CmbVersions.SelectedItem is VersionInfo selectedVersion)
            {
                user.SelectedVersion = selectedVersion.Version;
                settings.DownloadUrl = selectedVersion.Url;
                settings.ManifestDownloadUrl = !string.IsNullOrEmpty(selectedVersion.ManifestUrl) ? selectedVersion.ManifestUrl : selectedVersion.Url;
                settings.SelectedHash = selectedVersion.Hash;
            }

            if (IsConfigMode && !user.NeedsWslReboot && !_configContext.LocalComposeChanged && !user.PendingCertbotUpdate)
            {
                InstallService.SetStartup(user.AutoStart);
                
                user.Save();

                CheckChanges();
            }
            else
            {
                var hooks = new InstallService.InstallHooks();
                hooks.BeforeDeploy = () =>
                {
                    _configContext.Save(settings.WindowsDataPath);
                };

                NavigationService.Navigate(new PageInstall(IsConfigMode ? AppMode.Config : AppMode.Install, hooks));
            }
        }

        private async void BtnCheckSelfUpdate_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                UpdateOverlay.Visibility = Visibility.Visible;
                TxtUpdateStatus.Text = "更新を確認中...";

                using var client = new System.Net.Http.HttpClient();
                client.DefaultRequestHeaders.UserAgent.ParseAdd(AppInfo.AppShortName);
                client.Timeout = TimeSpan.FromSeconds(10);

                string apiUrl = "https://api.github.com/repos/fukumen/docker-rep2-win/releases/latest";
                var response = await client.GetStringAsync(apiUrl);

                using var doc = System.Text.Json.JsonDocument.Parse(response);
                var root = doc.RootElement;
                
                string tagName = root.GetProperty("tag_name").GetString() ?? "";
                string latestVersionStr = tagName.TrimStart('v', 'V');
                string currentVersionStr = AppInfo.AppVersion;

                if (Version.TryParse(latestVersionStr, out var latestVersion) && 
                    Version.TryParse(currentVersionStr, out var currentVersion))
                {
                    if (latestVersion > currentVersion)
                    {
                        var result = MessageBox.Show(
                            $"新しいバージョン (v{latestVersion}) が利用可能です。\n\nダウンロードページを開きますか？",
                            "アプリの更新", MessageBoxButton.YesNo, MessageBoxImage.Information);

                        if (result == MessageBoxResult.Yes)
                        {
                            string downloadUrl = root.GetProperty("html_url").GetString() ?? AppInfo.AppGitHubUrl;
                            
                            string arch = System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture == System.Runtime.InteropServices.Architecture.Arm64 ? "arm64" : "x64";
                            string targetAssetPrefix = $"docker-rep2-win-setup-";
                            string targetAssetSuffix = $"-win-{arch}.exe";

                            if (root.TryGetProperty("assets", out var assets) && assets.ValueKind == System.Text.Json.JsonValueKind.Array)
                            {
                                foreach (var asset in assets.EnumerateArray())
                                {
                                    string assetName = asset.GetProperty("name").GetString() ?? "";
                                    if (assetName.StartsWith(targetAssetPrefix, StringComparison.OrdinalIgnoreCase) &&
                                        assetName.EndsWith(targetAssetSuffix, StringComparison.OrdinalIgnoreCase))
                                    {
                                        downloadUrl = asset.GetProperty("browser_download_url").GetString() ?? downloadUrl;
                                        break;
                                    }
                                }
                            }

                            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                            {
                                FileName = downloadUrl,
                                UseShellExecute = true
                            });
                        }
                    }
                    else
                    {
                        MessageBox.Show("現在最新のバージョンを使用しています。", "アプリの更新", MessageBoxButton.OK, MessageBoxImage.Information);
                    }
                }
                else
                {
                    MessageBox.Show("バージョン情報の解析に失敗しました。", "エラー", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"更新の確認に失敗しました:\n{ex.Message}", "エラー", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                UpdateOverlay.Visibility = Visibility.Collapsed;
            }
        }
    }
}
