using System;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Navigation;

namespace docker_rep2_win
{
    public partial class PageCertbot : Page
    {
        private const string CertbotComposeFile = "docker-compose.certbot.yml";
        private const string SetupProjectName = "certbot-setup";
        public const string DefaultCertbotDockerfile = """
            # 公式のイメージを使う場合
            FROM certbot/certbot:latest

            # MyDNS を利用する場合の例 (PyPIからインストール):
            # RUN pip install certbot-dns-mydnsjp

            # Cloudflare を利用する場合の例:
            # RUN pip install certbot-dns-cloudflare

            # ----------------------------------------------------
            # サードパーティの専用イメージを直接使う場合
            # 例 (MyDNS専用イメージを使う場合):
            # FROM uskjohnnys/certbot-dns-mydnsjp:latest
            """;

        public const string DefaultCertbotIni = """
            # MyDNS を利用する場合の例:
            # certbot_dns_mydnsjp:dns_mydnsjp_masterid = "masterid"
            # certbot_dns_mydnsjp:dns_mydnsjp_password = "password"

            # Cloudflare を利用する場合の例:
            # dns_cloudflare_api_token = "your_api_token_here"
            """;

        private readonly ConfigSessionContext _configContext;

        public PageCertbot(ConfigSessionContext context)
        {
            InitializeComponent();
            _configContext = context;
        }

        private void PageCertbot_Loaded(object sender, RoutedEventArgs e)
        {
            Load();
            UpdateStatusDisplay();
        }

        private void Load()
        {
            var settings = ((App)Application.Current).Settings;
            string certbotDir = Path.Combine(settings.WindowsDataPath, "certbot");

            string dockerfilePath = Path.Combine(certbotDir, "Dockerfile");
            TxtDockerfile.Text = File.Exists(dockerfilePath) ? File.ReadAllText(dockerfilePath) : DefaultCertbotDockerfile;

            string iniPath = Path.Combine(certbotDir, "certbot.ini");
            TxtCertbotIni.Text = File.Exists(iniPath) ? File.ReadAllText(iniPath) : DefaultCertbotIni;
        }

        private void Save()
        {
            var settings = ((App)Application.Current).Settings;
            string certbotDir = Path.Combine(settings.WindowsDataPath, "certbot");
            if (!Directory.Exists(certbotDir)) Directory.CreateDirectory(certbotDir);

            File.WriteAllText(Path.Combine(certbotDir, "Dockerfile"), TxtDockerfile.Text);
            File.WriteAllText(Path.Combine(certbotDir, "certbot.ini"), TxtCertbotIni.Text);
        }

        private void UpdateStatusDisplay()
        {
            var settings = ((App)Application.Current).Settings;
            bool enabled = settings.User.EnableCertbot;

            TxtCertbotStatus.Text = enabled ? "設定済み (HTTPS有効)" : "未設定 (HTTPモード)";
            TxtCertbotStatus.Foreground = enabled 
                ? new System.Windows.Media.SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#107C10")) 
                : new System.Windows.Media.SolidColorBrush(System.Windows.Media.Colors.DimGray);

            string certbotDir = Path.Combine(settings.WindowsDataPath, "certbot");
            bool hasFiles = File.Exists(Path.Combine(certbotDir, "Dockerfile")) || 
                            File.Exists(Path.Combine(certbotDir, "certbot.ini"));
            
            BtnDelete.IsEnabled = hasFiles;
            BtnCertonly.IsEnabled = hasFiles && !enabled;
            BtnSave.IsEnabled = !hasFiles || !enabled;
        }

        private void BtnSave_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                Save();
                UpdateStatusDisplay();
                MessageBox.Show("設定ファイルを保存しました。", "完了", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show("保存に失敗しました: " + ex.Message, "エラー", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async void BtnCertonly_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new CertbotInitialWindow
            {
                Owner = Window.GetWindow(this)
            };

            if (dialog.ShowDialog() != true) return;

            var settings = ((App)Application.Current).Settings;
            string appDataPath = settings.WindowsDataPath;
            string dataPathWsl = WslService.ConvertToWslPath(appDataPath);
            string composePath = Path.Combine(appDataPath, CertbotComposeFile);

            try
            {
                UpdateOverlay.Visibility = Visibility.Visible;
                
                Save();

                string composeContent = $"""
                    services:
                      certbot:
                        image: rep2-certbot
                        build:
                          context: ./certbot
                        volumes:
                          - ./certbot/conf:/etc/letsencrypt
                          - ./certbot/logs:/var/log/letsencrypt
                          - ./certbot/certbot.ini:/certbot.ini
                        network_mode: bridge
                    """;
                await File.WriteAllTextAsync(composePath, composeContent);

                TxtUpdateStatus.Text = "Certbot 環境を構築中...";
                await WslService.RunCommandAsync($"cd \"{dataPathWsl}\" && docker compose -f {CertbotComposeFile} -p {SetupProjectName} build", 300000);

                TxtUpdateStatus.Text = "証明書を取得中...";
                string certCommand = $"docker compose -f {CertbotComposeFile} -p {SetupProjectName} run --rm certbot certonly --authenticator {dialog.Plugin} --{dialog.Plugin}-credentials /certbot.ini --email {dialog.Email} --agree-tos -d {dialog.Domain}";
                var result = await WslService.RunCommandAsync($"cd \"{dataPathWsl}\" && {certCommand}", 120000, true);

                if (result.ExitCode == 0)
                {
                    settings.User.EnableCertbot = true;
                    settings.User.PendingCertbotUpdate = true;
                    settings.User.Save();

                    UpdateStatusDisplay();
                    var logWindow = new LogWindow("成功", "証明書の取得に成功しました！\n設定画面に戻って「更新」を押すと HTTPS が有効化されます。", result.StdOut) { Owner = Window.GetWindow(this) };
                    logWindow.ShowDialog();
                }
                else
                {
                    var logContent = $"--- StdErr ---\n{result.StdErr}\n\n--- StdOut ---\n{result.StdOut}";
                    var logWindow = new LogWindow("エラー", "証明書の取得に失敗しました。設定を確認してください。", logContent) { Owner = Window.GetWindow(this) };
                    logWindow.ShowDialog();
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show("エラーが発生しました: " + ex.Message, "エラー", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                try
                {
                    await WslService.RunCommandAsync($"cd \"{dataPathWsl}\" && docker compose -f {CertbotComposeFile} -p {SetupProjectName} down", 30000, true);
                    if (File.Exists(composePath)) File.Delete(composePath);
                }
                catch { }

                UpdateOverlay.Visibility = Visibility.Collapsed;
            }
        }

        private void BtnDelete_Click(object sender, RoutedEventArgs e)
        {
            if (((App)Application.Current).Monitor?.IsRunning == true)
            {
                MessageBox.Show("コンテナが起動中です。コンテナを停止してから操作してください。", "停止が必要", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var result = MessageBox.Show("Certbotの設定を初期状態に戻してもよろしいですか？\n(実ファイルは削除され、HTTPS設定も無効化されます)", "確認", MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (result == MessageBoxResult.Yes)
            {
                var settings = ((App)Application.Current).Settings;
                settings.User.EnableCertbot = false;
                settings.User.PendingCertbotUpdate = true;
                settings.User.Save();

                try
                {
                    string certbotDir = Path.Combine(settings.WindowsDataPath, "certbot");
                    if (Directory.Exists(certbotDir))
                    {
                        // 読み取り専用属性がついたファイルが含まれると削除に失敗するため、
                        // 再帰的に属性を解除してから削除を試みる (Windows-native approach)
                        var di = new DirectoryInfo(certbotDir);
                        foreach (var file in di.GetFiles("*", SearchOption.AllDirectories))
                        {
                            file.Attributes = FileAttributes.Normal;
                        }
                        foreach (var dir in di.GetDirectories("*", SearchOption.AllDirectories))
                        {
                            dir.Attributes = FileAttributes.Normal;
                        }
                        Directory.Delete(certbotDir, true);
                    }
                }
                catch (Exception ex)
                {
                    Logger.Log(ex, "Certbotディレクトリの削除に失敗しました。");
                }

                TxtDockerfile.Text = DefaultCertbotDockerfile;
                TxtCertbotIni.Text = DefaultCertbotIni;
                UpdateStatusDisplay();
                
                MessageBox.Show("初期化しました。設定画面に戻って「更新」を押すと HTTP モードに戻ります。", "完了", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }

        private void BtnBack_Click(object sender, RoutedEventArgs e)
        {
            if (NavigationService.CanGoBack)
            {
                NavigationService.GoBack();
            }
        }
    }
}
