using System.Windows;

namespace FramePlayer
{
    public partial class VideoInfoWindow : Window
    {
        public VideoInfoWindow(string windowTitle, string heading, string subheading, string details)
        {
            InitializeComponent();
            Title = string.IsNullOrWhiteSpace(windowTitle) ? "Video Info" : windowTitle;
            HeadingTextBlock.Text = string.IsNullOrWhiteSpace(heading) ? "Video Info" : heading;
            SubheadingTextBlock.Text = string.IsNullOrWhiteSpace(subheading)
                ? "Focused pane media details"
                : subheading;
            DetailsTextBox.Text = details ?? string.Empty;
            DetailsTextBox.CaretIndex = 0;
            DetailsTextBox.ScrollToHome();
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }
    }
}
