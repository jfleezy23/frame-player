using Avalonia.Controls;
using Avalonia.Interactivity;
using FramePlayer.Avalonia.Views;
using Xunit;

namespace FramePlayer.Avalonia.Tests
{
    public sealed class VideoInfoWindowTests : IClassFixture<AvaloniaHeadlessFixture>
    {
        private readonly AvaloniaHeadlessFixture _fixture;

        public VideoInfoWindowTests(AvaloniaHeadlessFixture fixture)
        {
            _fixture = fixture;
        }

        [Fact]
        public void Window_RendersStructuredSectionsFallbacksAndCloseAction()
        {
            _fixture.Run(() =>
            {
                var snapshot = new VideoInfoSnapshot
                {
                    WindowTitle = "Video Info - Primary",
                    FileName = "review.mp4",
                    FilePath = "/media/review.mp4",
                    SummarySection = CreateSection(("Duration", "00:00:10.000")),
                    VideoSection = CreateSection(("Codec", "H.264")),
                    AudioSection = CreateSection(("Codec", "AAC")),
                    AdvancedSection = CreateSection(("Video stream index", "0"))
                };

                var window = new VideoInfoWindow(snapshot);
                try
                {
                    Assert.Equal(snapshot.WindowTitle, window.Title);
                    Assert.Equal(560d, window.Width);
                    Assert.Equal(480d, window.Height);
                    var root = Assert.IsType<Grid>(window.Content);
                    Assert.Equal(3, root.Children.Count);

                    var header = Assert.IsType<StackPanel>(root.Children[0]);
                    Assert.Equal("review.mp4", Assert.IsType<TextBlock>(header.Children[0]).Text);
                    Assert.Equal("/media/review.mp4", Assert.IsType<TextBlock>(header.Children[1]).Text);

                    var scrollViewer = Assert.IsType<ScrollViewer>(root.Children[1]);
                    var content = Assert.IsType<StackPanel>(scrollViewer.Content);
                    Assert.Equal(4, content.Children.Count);
                    AssertSection(content.Children[0], "Summary", "Duration", "00:00:10.000");
                    AssertSection(content.Children[1], "Video", "Codec", "H.264");
                    AssertSection(content.Children[2], "Audio", "Codec", "AAC");
                    var advanced = Assert.IsType<Expander>(content.Children[3]);
                    Assert.Equal("Advanced", Assert.IsType<TextBlock>(advanced.Header).Text);
                    AssertFieldGrid(Assert.IsType<Grid>(advanced.Content), "Video stream index", "0");

                    var footer = Assert.IsType<Border>(root.Children[2]);
                    var closeButton = Assert.IsType<Button>(footer.Child);
                    Assert.Equal("Close", closeButton.Content);
                    window.Show();
                    Assert.True(window.IsVisible);
                    closeButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    Assert.False(window.IsVisible);
                }
                finally
                {
                    if (window.IsVisible)
                    {
                        window.Close();
                    }
                }

                var fallbackWindow = new VideoInfoWindow(new VideoInfoSnapshot
                {
                    AudioSection = new VideoInfoSection(null!, "No audio stream")
                });
                Assert.Equal("Video Info", fallbackWindow.Title);
                var fallbackRoot = Assert.IsType<Grid>(fallbackWindow.Content);
                var fallbackHeader = Assert.IsType<StackPanel>(fallbackRoot.Children[0]);
                Assert.Equal("Video Info", Assert.IsType<TextBlock>(fallbackHeader.Children[0]).Text);
                Assert.Equal("Unknown path", Assert.IsType<TextBlock>(fallbackHeader.Children[1]).Text);
                var fallbackContent = Assert.IsType<StackPanel>(Assert.IsType<ScrollViewer>(fallbackRoot.Children[1]).Content);
                var emptyAudioMessage = Assert.Single(fallbackContent.Children);
                Assert.Equal("No audio stream", Assert.IsType<TextBlock>(emptyAudioMessage).Text);
                fallbackWindow.Close();
            });
        }

        private static VideoInfoSection CreateSection(params (string Label, string Value)[] fields)
        {
            var entries = new VideoInfoField[fields.Length];
            for (var index = 0; index < fields.Length; index++)
            {
                entries[index] = new VideoInfoField(fields[index].Label, fields[index].Value);
            }

            return new VideoInfoSection(entries);
        }

        private static void AssertSection(
            object sectionControl,
            string title,
            string label,
            string value)
        {
            var section = Assert.IsType<StackPanel>(sectionControl);
            Assert.Equal(title, Assert.IsType<TextBlock>(section.Children[0]).Text);
            AssertFieldGrid(Assert.IsType<Grid>(section.Children[1]), label, value);
        }

        private static void AssertFieldGrid(Grid grid, string label, string value)
        {
            Assert.Equal(2, grid.Children.Count);
            Assert.Equal(label, Assert.IsType<TextBlock>(grid.Children[0]).Text);
            Assert.Equal(value, Assert.IsType<SelectableTextBlock>(grid.Children[1]).Text);
        }
    }
}
