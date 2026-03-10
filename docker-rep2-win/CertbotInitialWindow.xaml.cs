using System.Windows;

namespace docker_rep2_win
{
    public partial class CertbotInitialWindow : Window
    {
        public string Domain { get; private set; } = string.Empty;
        public string Email { get; private set; } = string.Empty;
        public string Plugin { get; private set; } = string.Empty;

        public CertbotInitialWindow()
        {
            InitializeComponent();
        }

        private void BtnOk_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(TxtDomain.Text) || 
                string.IsNullOrWhiteSpace(TxtEmail.Text) || 
                string.IsNullOrWhiteSpace(TxtPlugin.Text))
            {
                MessageBox.Show("全ての項目を入力してください。", "入力エラー", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            Domain = TxtDomain.Text.Trim();
            Email = TxtEmail.Text.Trim();
            Plugin = TxtPlugin.Text.Trim();

            DialogResult = true;
            Close();
        }

        private void BtnCancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }
    }
}