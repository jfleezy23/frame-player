using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;

namespace FramePlayer
{
    public partial class VideoInfoWindow : Window
    {
        internal VideoInfoWindow(VideoInfoSnapshot snapshot)
        {
            if (snapshot == null)
            {
                throw new ArgumentNullException(nameof(snapshot));
            }

            InitializeComponent();
            Title = string.IsNullOrWhiteSpace(snapshot.WindowTitle) ? "Video Info" : snapshot.WindowTitle;
            PaneTextBlock.Text = string.IsNullOrWhiteSpace(snapshot.PaneLabel)
                ? "Focused pane"
                : snapshot.PaneLabel + " pane";
            HeadingTextBlock.Text = string.IsNullOrWhiteSpace(snapshot.FileName)
                ? "Video Info"
                : snapshot.FileName;
            PathTextBox.Text = string.IsNullOrWhiteSpace(snapshot.FilePath)
                ? "Unknown path"
                : snapshot.FilePath;

            ApplySection(SummaryItemsControl, snapshot.SummarySection);
            ApplySection(VideoItemsControl, snapshot.VideoSection);
            ApplySection(AudioItemsControl, snapshot.AudioSection);
            ApplySection(AdvancedItemsControl, snapshot.AdvancedSection);

            AudioEmptyTextBlock.Text = string.IsNullOrWhiteSpace(snapshot.AudioSection.EmptyMessage)
                ? string.Empty
                : snapshot.AudioSection.EmptyMessage;
            AudioEmptyTextBlock.Visibility = string.IsNullOrWhiteSpace(snapshot.AudioSection.EmptyMessage)
                ? Visibility.Collapsed
                : Visibility.Visible;
            AudioItemsControl.Visibility = snapshot.AudioSection.HasFields
                ? Visibility.Visible
                : Visibility.Collapsed;
            AdvancedExpander.Visibility = snapshot.AdvancedSection.HasFields
                ? Visibility.Visible
                : Visibility.Collapsed;
        }

        private static void ApplySection(System.Windows.Controls.ItemsControl itemsControl, VideoInfoSection section)
        {
            if (itemsControl == null)
            {
                return;
            }

            itemsControl.ItemsSource = section != null
                ? section.Fields
                : Array.Empty<VideoInfoField>();
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }
    }

    internal sealed class VideoInfoSnapshot
    {
        public string WindowTitle { get; set; } = string.Empty;

        public string PaneLabel { get; set; } = string.Empty;

        public string FileName { get; set; } = string.Empty;

        public string FilePath { get; set; } = string.Empty;

        public VideoInfoSection SummarySection { get; set; } = VideoInfoSection.Empty;

        public VideoInfoSection VideoSection { get; set; } = VideoInfoSection.Empty;

        public VideoInfoSection AudioSection { get; set; } = VideoInfoSection.Empty;

        public VideoInfoSection AdvancedSection { get; set; } = VideoInfoSection.Empty;
    }

    internal sealed class VideoInfoSection
    {
        public static VideoInfoSection Empty { get; } = new VideoInfoSection(Array.Empty<VideoInfoField>());

        public VideoInfoSection(IEnumerable<VideoInfoField> fields, string emptyMessage = null)
        {
            Fields = fields == null
                ? Array.Empty<VideoInfoField>()
                : fields.Where(field => field != null).ToArray();
            EmptyMessage = emptyMessage ?? string.Empty;
        }

        public VideoInfoField[] Fields { get; }

        public string EmptyMessage { get; }

        public bool HasFields
        {
            get { return Fields.Length > 0; }
        }
    }

    internal sealed class VideoInfoField
    {
        public VideoInfoField(string label, string value)
        {
            Label = label ?? string.Empty;
            Value = value ?? string.Empty;
        }

        public string Label { get; }

        public string Value { get; }
    }
}
