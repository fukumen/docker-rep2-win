using System.Windows;
using System.Windows.Navigation;
using System.Diagnostics;

namespace docker_rep2_win
{
    public partial class VersionWindow : Window
    {
        public string PublisherInfo => $"発行元: {AppInfo.AppPublisher}";
        public string VersionInfo => $"バージョン: {AppInfo.AppVersion}";
        public Uri AppGitHubUri => new Uri(AppInfo.AppGitHubUrl);

        public VersionWindow()
        {
            InitializeComponent();
            DataContext = this;
        }

        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            BtnClose.Focus();
        }

        private void Hyperlink_RequestNavigate(object sender, RequestNavigateEventArgs e)
        {
            try
            {
                Process.Start(new ProcessStartInfo(e.Uri.AbsoluteUri) { UseShellExecute = true });
                e.Handled = true;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"URLを開けませんでした: {ex.Message}");
            }
        }

        private void Close_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }

        protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
        {
            base.OnClosing(e);
            Dispatcher.BeginInvoke(new Action(() => {
                ((App)Application.Current).CheckExitCondition();
            }), System.Windows.Threading.DispatcherPriority.ContextIdle);
        }
    }
}
