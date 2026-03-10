using System.Windows;

namespace docker_rep2_win
{
    public partial class LogWindow : Window
    {
        public LogWindow(string title, string message, string logContent)
        {
            InitializeComponent();
            
            Title = title;
            TxtMessage.Text = message;
            TxtMessage.Visibility = string.IsNullOrWhiteSpace(message) ? Visibility.Collapsed : Visibility.Visible;
            TxtLog.Text = logContent;
        }

        private void BtnCopy_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                Clipboard.SetText(TxtLog.Text);
                MessageBox.Show("クリップボードにコピーしました。", "コピー完了", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch
            {
                MessageBox.Show("クリップボードへのコピーに失敗しました。", "エラー", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void BtnClose_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }
    }
}