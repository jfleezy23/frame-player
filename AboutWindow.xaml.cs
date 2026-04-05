using System.Windows;

namespace Rpcs3VideoPlayer
{
    public partial class AboutWindow : Window
    {
        public AboutWindow(string version)
        {
            InitializeComponent();
            VersionTextBlock.Text = "Version " + version;
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }
    }
}
