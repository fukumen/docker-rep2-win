using System.Windows;

namespace docker_rep2_win
{
    public enum UpdateChoice
    {
        Cancel,
        Update,
        Uninstall
    }

    /// <summary>
    /// UpdateChoiceWindow.xaml の相互作用ロジック
    /// </summary>
    public partial class UpdateChoiceWindow : Window
    {
        public UpdateChoice Result { get; private set; } = UpdateChoice.Cancel;

        public UpdateChoiceWindow()
        {
            InitializeComponent();
        }

        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            BtnUpdate.Focus();
        }

        private void Update_Click(object sender, RoutedEventArgs e)
        {
            Result = UpdateChoice.Update;
            DialogResult = true;
            Close();
        }

        private void Reinstall_Click(object sender, RoutedEventArgs e)
        {
            Result = UpdateChoice.Uninstall;
            DialogResult = true;
            Close();
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            Result = UpdateChoice.Cancel;
            DialogResult = false;
            Close();
        }
    }
}
