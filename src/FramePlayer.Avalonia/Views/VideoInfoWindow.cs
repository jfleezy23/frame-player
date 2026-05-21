using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;

namespace FramePlayer.Avalonia.Views
{
    public class VideoInfoWindow : Window
    {
        private static readonly IBrush BackgroundBrush = Brush.Parse("#101418");
        private static readonly IBrush TextBrush = Brush.Parse("#F3F4F6");
        private static readonly IBrush LabelBrush = Brush.Parse("#B7BDC6");
        private static readonly IBrush SectionBorderBrush = Brush.Parse("#28313B");

        internal VideoInfoWindow(VideoInfoSnapshot snapshot)
        {
            Title = string.IsNullOrWhiteSpace(snapshot.WindowTitle) ? "Video Info" : snapshot.WindowTitle;
            Width = 560;
            Height = 480;
            MinWidth = 420;
            MinHeight = 320;
            Background = BackgroundBrush;
            Foreground = TextBrush;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;

            var rootGrid = new Grid
            {
                RowDefinitions = new RowDefinitions("Auto,*,Auto")
            };

            // Header
            var headerPanel = new StackPanel { Margin = new Thickness(20), Spacing = 4 };
            var headingText = new TextBlock
            {
                Text = string.IsNullOrWhiteSpace(snapshot.FileName) ? "Video Info" : snapshot.FileName,
                FontSize = 18,
                FontWeight = FontWeight.SemiBold,
                TextTrimming = TextTrimming.CharacterEllipsis
            };
            var pathText = new TextBlock
            {
                Text = string.IsNullOrWhiteSpace(snapshot.FilePath) ? "Unknown path" : snapshot.FilePath,
                Foreground = LabelBrush,
                FontSize = 12,
                TextTrimming = TextTrimming.CharacterEllipsis
            };
            headerPanel.Children.Add(headingText);
            headerPanel.Children.Add(pathText);
            Grid.SetRow(headerPanel, 0);
            rootGrid.Children.Add(headerPanel);

            // Scrollable Content
            var scrollViewer = new ScrollViewer
            {
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                Margin = new Thickness(20, 0)
            };

            var contentStack = new StackPanel { Spacing = 20 };

            if (snapshot.SummarySection.HasFields)
            {
                contentStack.Children.Add(BuildSection("Summary", snapshot.SummarySection));
            }

            if (snapshot.VideoSection.HasFields)
            {
                contentStack.Children.Add(BuildSection("Video", snapshot.VideoSection));
            }

            if (snapshot.AudioSection.HasFields)
            {
                contentStack.Children.Add(BuildSection("Audio", snapshot.AudioSection));
            }
            else if (!string.IsNullOrWhiteSpace(snapshot.AudioSection.EmptyMessage))
            {
                contentStack.Children.Add(new TextBlock
                {
                    Text = snapshot.AudioSection.EmptyMessage,
                    Foreground = LabelBrush,
                    FontStyle = FontStyle.Italic
                });
            }

            if (snapshot.AdvancedSection.HasFields)
            {
                var expander = new Expander
                {
                    Header = new TextBlock
                    {
                        Text = "Advanced",
                        FontSize = 14,
                        FontWeight = FontWeight.SemiBold
                    },
                    Content = BuildFieldGrid(snapshot.AdvancedSection),
                    Margin = new Thickness(0, 10, 0, 0),
                    BorderBrush = SectionBorderBrush,
                    CornerRadius = new CornerRadius(4)
                };
                contentStack.Children.Add(expander);
            }

            scrollViewer.Content = contentStack;
            Grid.SetRow(scrollViewer, 1);
            rootGrid.Children.Add(scrollViewer);

            // Footer
            var footerBorder = new Border
            {
                BorderBrush = SectionBorderBrush,
                BorderThickness = new Thickness(0, 1, 0, 0),
                Padding = new Thickness(20),
                Background = Brush.Parse("#171C22")
            };
            var closeButton = new Button
            {
                Content = "Close",
                Width = 100,
                HorizontalAlignment = HorizontalAlignment.Right,
                HorizontalContentAlignment = HorizontalAlignment.Center
            };
            closeButton.Click += (s, e) => Close();
            footerBorder.Child = closeButton;
            Grid.SetRow(footerBorder, 2);
            rootGrid.Children.Add(footerBorder);

            Content = rootGrid;
        }

        private static Control BuildSection(string title, VideoInfoSection section)
        {
            var stack = new StackPanel { Spacing = 10 };
            var header = new TextBlock
            {
                Text = title,
                FontSize = 14,
                FontWeight = FontWeight.SemiBold,
                Foreground = TextBrush
            };
            stack.Children.Add(header);
            stack.Children.Add(BuildFieldGrid(section));
            return stack;
        }

        private static Grid BuildFieldGrid(VideoInfoSection section)
        {
            var grid = new Grid
            {
                ColumnDefinitions = new ColumnDefinitions("160,*")
            };

            for (int i = 0; i < section.Fields.Length; i++)
            {
                grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));

                var label = new TextBlock
                {
                    Text = section.Fields[i].Label,
                    Foreground = LabelBrush,
                    Margin = new Thickness(0, 4, 10, 4)
                };
                Grid.SetRow(label, i);
                Grid.SetColumn(label, 0);

                var value = new SelectableTextBlock
                {
                    Text = section.Fields[i].Value,
                    Foreground = TextBrush,
                    Margin = new Thickness(0, 4, 0, 4),
                    TextWrapping = TextWrapping.Wrap
                };
                Grid.SetRow(value, i);
                Grid.SetColumn(value, 1);

                grid.Children.Add(label);
                grid.Children.Add(value);
            }

            return grid;
        }
    }
}
