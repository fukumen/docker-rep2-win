using System;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Navigation;

namespace docker_rep2_win
{
    public partial class PageComposeLocal : Page
    {
        private readonly ConfigSessionContext _configContext;

        public PageComposeLocal(ConfigSessionContext context)
        {
            InitializeComponent();
            _configContext = context;
        }

        private void PageComposeLocal_Loaded(object sender, RoutedEventArgs e)
        {
            TxtEditor.Text = _configContext.LocalComposeText;
            
            BtnDelete.IsEnabled = _configContext.LocalComposeChanged || _configContext.HasSavedLocalCompose;

            var app = (App)Application.Current;
            TxtDescription.Text = $"※バインドマウントを使用する場合、カレントフォルダは {app.Settings.DataPath} になります";

            TxtEditor.Focus();
        }

        private void BtnDelete_Click(object sender, RoutedEventArgs e)
        {
            var result = MessageBox.Show("docker-compose.local.yml の設定を初期状態に戻してもよろしいですか？\n(実ファイルは一覧に戻って「更新」を押した際に削除されます)", "確認", MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (result == MessageBoxResult.Yes)
            {
                TxtEditor.Text = "";
                BtnSave_Click(sender, e);
            }
        }

        private void BtnBack_Click(object sender, RoutedEventArgs e)
        {
            if (NavigationService.CanGoBack)
            {
                NavigationService.GoBack();
            }
        }

        private void BtnSave_Click(object sender, RoutedEventArgs e)
        {
            _configContext.LocalComposeText = TxtEditor.Text;

            if (NavigationService.CanGoBack)
            {
                NavigationService.GoBack();
            }
        }
    }
}