using System;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.VisualTree;
using FramePlayer.Core.Models;
using FramePlayer.Desktop.Views;
using Xunit;

namespace FramePlayer.Desktop.Tests
{
    public sealed class MainWindowUiContractTests : IClassFixture<AvaloniaHeadlessFixture>
    {
        private readonly AvaloniaHeadlessFixture _fixture;

        public MainWindowUiContractTests(AvaloniaHeadlessFixture fixture)
        {
            _fixture = fixture;
        }

        [Fact]
        public void MainWindow_UsesNativeChromeWithAvaloniaAndNativeMenus()
        {
            _fixture.Run(() =>
            {
                var window = new MainWindow();
                try
                {

                var nativeMenu = NativeMenu.GetMenu(window);
                Assert.NotNull(nativeMenu);
                Assert.Equal(WindowDecorations.None, window.WindowDecorations);
                Assert.NotNull(window.Icon);

                var menuPanel = RequireControl<Border>(window, "MenuPanel");
                Assert.Equal(0, Grid.GetRow(menuPanel));
                Assert.Equal(new Thickness(0, 0, 0, 1), menuPanel.BorderThickness);
                AssertBrushColor("#FFFFFF", menuPanel.Background);
                AssertBrushColor("#D1D5DB", menuPanel.BorderBrush);

                var visualMenu = RequireControl<Menu>(window, "WindowsMenuBar");
                var menuThemeScope = Assert.IsType<ThemeVariantScope>(visualMenu.Parent);
                Assert.Equal(1, Grid.GetColumn(menuThemeScope));
                Assert.Equal(
                    new[] { "File", "Playback", "Audio Insertion", "Help" },
                    visualMenu.Items
                        .OfType<MenuItem>()
                        .Select(item => item.Header?.ToString() ?? string.Empty)
                        .ToArray());
                AssertMenuItemHeaders(
                    RequireControl<MenuItem>(window, "FileRootMenuItem"),
                    "New Window",
                    "Open Video...",
                    "Open Recent",
                    "Close Video",
                    "Video Info...",
                    "Export Diagnostic Report...",
                    "Exit");
                AssertMenuItemHeaders(
                    RequireControl<MenuItem>(window, "PlaybackRootMenuItem"),
                    "Play",
                    "Rewind 5s",
                    "Fast Forward 5s",
                    "Previous Frame",
                    "Next Frame",
                    "Loop Playback",
                    "Set Loop In",
                    "Set Loop Out",
                    "Clear Loop Points",
                    "Save Loop As Clip...",
                    "Export Side-by-Side Compare...",
                    "Zoom In",
                    "Zoom Out",
                    "Reset Zoom",
                    "Use GPU Acceleration",
                    "Toggle Full Screen");
                AssertMenuItemHeaders(
                    RequireControl<MenuItem>(window, "AudioInsertionRootMenuItem"),
                    "Replace Audio Track...");
                AssertMenuItemHeaders(
                    RequireControl<MenuItem>(window, "HelpRootMenuItem"),
                    "Controls and Shortcuts...",
                    "About Frame Player");

                var topLevelHeaders = nativeMenu.Items
                    .OfType<NativeMenuItem>()
                    .Select(item => item.Header)
                    .ToArray();

                Assert.Equal(
                    new[] { "File", "Playback", "Audio Insertion", "Help" },
                    topLevelHeaders);
                }
                finally
                {
                    window.Close();
                }
            });
        }

        [Fact]
        public void MainWindow_LoadsSinglePaneWithoutRedundantPaneLocalControls()
        {
            _fixture.Run(() =>
            {
                var window = new MainWindow();
                try
                {

                var header = RequireControl<Border>(window, "HeaderPanel");
                var primaryPaneHeader = RequireControl<Border>(window, "PrimaryPaneHeaderBorder");
                var primaryPaneFooter = RequireControl<Border>(window, "PrimaryPaneFooterBorder");
                var primaryPaneLayout = RequireControl<Grid>(window, "PrimaryPaneLayoutGrid");
                var videoPaneGrid = RequireControl<Grid>(window, "VideoPaneGrid");
                var comparePaneBorder = RequireControl<Border>(window, "ComparePaneBorder");
                var compareToolbar = RequireControl<Border>(window, "CompareToolbarBorder");

                Assert.Equal(1, Grid.GetRow(header));
                Assert.False(primaryPaneHeader.IsVisible);
                Assert.False(primaryPaneFooter.IsVisible);
                Assert.Equal(0, primaryPaneLayout.RowDefinitions[0].Height.Value);
                Assert.Equal(0, primaryPaneLayout.RowDefinitions[2].Height.Value);
                Assert.Equal(0, videoPaneGrid.ColumnDefinitions[1].Width.Value);
                Assert.Equal(0, videoPaneGrid.ColumnSpacing);
                Assert.False(comparePaneBorder.IsVisible);
                Assert.False(compareToolbar.IsVisible);
                }
                finally
                {
                    window.Close();
                }
            });
        }

        [Fact]
        public void HeaderAndPrimaryPane_ShareOuterRailsAndTextInsets()
        {
            _fixture.Run(() =>
            {
                var window = new MainWindow();
                try
                {
                    var header = RequireControl<Border>(window, "HeaderPanel");
                    var videoPanel = RequireControl<Border>(window, "VideoPanel");
                    var primaryPaneBorder = RequireControl<Border>(window, "PrimaryPaneBorder");
                    var comparePaneBorder = RequireControl<Border>(window, "ComparePaneBorder");
                    var primaryHeader = RequireControl<Border>(window, "PrimaryPaneHeaderBorder");
                    var compareHeader = RequireControl<Border>(window, "ComparePaneHeaderBorder");

                    Assert.Equal(new Thickness(14, 6, 14, 0), header.Margin);
                    Assert.Equal(new Thickness(12), header.Padding);
                    Assert.Equal(new Thickness(14, 8, 14, 0), videoPanel.Margin);
                    Assert.Equal(new Thickness(0), videoPanel.Padding);
                    Assert.Equal(new CornerRadius(0), primaryPaneBorder.CornerRadius);
                    Assert.Equal(new CornerRadius(0), comparePaneBorder.CornerRadius);
                    Assert.Equal(new Thickness(12, 6, 12, 6), primaryHeader.Padding);
                    Assert.Equal(new Thickness(12, 6, 12, 6), compareHeader.Padding);
                }
                finally
                {
                    window.Close();
                }
            });
        }

        [Fact]
        public void CompareMode_TogglesPaneLocalControlsOnlyWhenCompareIsEnabled()
        {
            _fixture.Run(() =>
            {
                var window = new MainWindow();
                try
                {

                var compareMode = RequireControl<CheckBox>(window, "CompareModeCheckBox");
                var primaryPaneHeader = RequireControl<Border>(window, "PrimaryPaneHeaderBorder");
                var primaryPaneFooter = RequireControl<Border>(window, "PrimaryPaneFooterBorder");
                var primaryPaneLayout = RequireControl<Grid>(window, "PrimaryPaneLayoutGrid");
                var videoPaneGrid = RequireControl<Grid>(window, "VideoPaneGrid");
                var comparePaneBorder = RequireControl<Border>(window, "ComparePaneBorder");
                var compareToolbar = RequireControl<Border>(window, "CompareToolbarBorder");

                compareMode.IsChecked = true;

                Assert.True(primaryPaneHeader.IsVisible);
                Assert.True(primaryPaneFooter.IsVisible);
                Assert.Equal(46, primaryPaneLayout.RowDefinitions[0].Height.Value);
                Assert.Equal(118, primaryPaneLayout.RowDefinitions[2].Height.Value);
                Assert.Equal(GridUnitType.Star, videoPaneGrid.ColumnDefinitions[1].Width.GridUnitType);
                Assert.Equal(12, videoPaneGrid.ColumnSpacing);
                Assert.True(comparePaneBorder.IsVisible);
                Assert.True(compareToolbar.IsVisible);

                compareMode.IsChecked = false;

                Assert.False(primaryPaneHeader.IsVisible);
                Assert.False(primaryPaneFooter.IsVisible);
                Assert.Equal(0, primaryPaneLayout.RowDefinitions[0].Height.Value);
                Assert.Equal(0, primaryPaneLayout.RowDefinitions[2].Height.Value);
                Assert.Equal(0, videoPaneGrid.ColumnDefinitions[1].Width.Value);
                Assert.Equal(0, videoPaneGrid.ColumnSpacing);
                Assert.False(comparePaneBorder.IsVisible);
                Assert.False(compareToolbar.IsVisible);
                }
                finally
                {
                    window.Close();
                }
            });
        }

        [Fact]
        public void CompareMode_FileOpenCommandsTargetPersistentlyFocusedPane()
        {
            _fixture.Run(() =>
            {
                var window = new MainWindow();
                try
                {
                    var compareMode = RequireControl<CheckBox>(window, "CompareModeCheckBox");
                    compareMode.IsChecked = true;

                    InvokePrivate(window, "SelectPane", ParsePane("Compare"));

                    Assert.Equal("Compare", InvokePrivate(window, "GetFileOpenTargetPane").ToString());

                    compareMode.IsChecked = false;

                    Assert.Equal("Primary", InvokePrivate(window, "GetFileOpenTargetPane").ToString());
                }
                finally
                {
                    window.Close();
                }
            });
        }

        [Fact]
        public void CompareMode_SelectedPaneUsesWindowsFocusHighlight()
        {
            _fixture.Run(() =>
            {
                var window = new MainWindow();
                try
                {
                    var compareMode = RequireControl<CheckBox>(window, "CompareModeCheckBox");
                    var primaryPaneBorder = RequireControl<Border>(window, "PrimaryPaneBorder");
                    var comparePaneBorder = RequireControl<Border>(window, "ComparePaneBorder");

                    compareMode.IsChecked = true;
                    InvokePrivate(window, "SelectPane", ParsePane("Compare"));

                    Assert.Equal(new Thickness(1), primaryPaneBorder.BorderThickness);
                    Assert.Equal(new Thickness(1), comparePaneBorder.BorderThickness);
                    AssertBrushColor("#28313B", primaryPaneBorder.BorderBrush);
                    AssertBrushColor("#5AA9E6", comparePaneBorder.BorderBrush);

                    InvokePrivate(window, "SelectPane", ParsePane("Primary"));

                    Assert.Equal(new Thickness(1), primaryPaneBorder.BorderThickness);
                    Assert.Equal(new Thickness(1), comparePaneBorder.BorderThickness);
                    AssertBrushColor("#5AA9E6", primaryPaneBorder.BorderBrush);
                    AssertBrushColor("#28313B", comparePaneBorder.BorderBrush);
                }
                finally
                {
                    window.Close();
                }
            });
        }

        [Fact]
        public void TransportButtons_MatchWindowsReferenceSizing()
        {
            _fixture.Run(() =>
            {
                var window = new MainWindow();
                try
                {
                    foreach (var buttonName in new[]
                    {
                        "PreviousFrameButton",
                        "RewindButton",
                        "PlayPauseButton",
                        "FastForwardButton",
                        "NextFrameButton",
                        "ToggleFullScreenButton",
                        "PrimaryPaneStepBackButton",
                        "PrimaryPaneSkipBackHundredFramesButton",
                        "PrimaryPanePlayPauseButton",
                        "PrimaryPaneSkipForwardHundredFramesButton",
                        "PrimaryPaneStepForwardButton",
                        "ComparePaneStepBackButton",
                        "ComparePaneSkipBackHundredFramesButton",
                        "ComparePanePlayPauseButton",
                        "ComparePaneSkipForwardHundredFramesButton",
                        "ComparePaneStepForwardButton"
                    })
                    {
                        var button = RequireControl<Button>(window, buttonName);
                        Assert.Equal(38, button.Width);
                        Assert.Equal(34, button.Height);
                        Assert.Equal(38, button.MinWidth);
                        Assert.Equal(38, button.MaxWidth);
                        Assert.Equal(34, button.MinHeight);
                        Assert.Equal(34, button.MaxHeight);
                    }
                }
                finally
                {
                    window.Close();
                }
            });
        }

        [Fact]
        public void PaneTransports_ExposeSingleAndHundredFrameControlsPerPane()
        {
            _fixture.Run(() =>
            {
                var window = new MainWindow();
                try
                {
                    AssertPaneTransport(window, "Primary", "pane-primary");
                    AssertPaneTransport(window, "Compare", "pane-compare");

                    Assert.Null(window.FindControl<Button>("PrimaryPanePlayButton"));
                    Assert.Null(window.FindControl<Button>("PrimaryPanePauseButton"));
                    Assert.Null(window.FindControl<Button>("ComparePanePlayButton"));
                    Assert.Null(window.FindControl<Button>("ComparePanePauseButton"));
                }
                finally
                {
                    window.Close();
                }
            });
        }

        [Fact]
        public void LoopStatusLabels_DoNotShareTransportRows()
        {
            _fixture.Run(() =>
            {
                var window = new MainWindow();
                try
                {
                    var mainLoopStatus = RequireControl<TextBlock>(window, "LoopStatusTextBlock");
                    var mainTransportParent = Assert.IsType<StackPanel>(RequireControl<Button>(window, "PlayPauseButton").Parent);
                    Assert.Equal(240, mainLoopStatus.Width);
                    Assert.Equal(1, Grid.GetRow(mainLoopStatus));
                    Assert.Equal(1, Grid.GetColumn(mainLoopStatus));
                    Assert.Equal(2, Grid.GetRow(mainTransportParent));
                    Assert.Equal(2, Grid.GetColumnSpan(mainTransportParent));

                    AssertLoopStatusPlacement(window, "PrimaryPaneLoopStatusTextBlock", "PrimaryPanePlayPauseButton", 176);
                    AssertLoopStatusPlacement(window, "ComparePaneLoopStatusTextBlock", "ComparePanePlayPauseButton", 176);
                }
                finally
                {
                    window.Close();
                }
            });
        }

        [Fact]
        public void FrameEntries_UseFixedRightRailsAndStableBoxSizing()
        {
            _fixture.Run(() =>
            {
                var window = new MainWindow();
                try
                {
                    var frameTextBox = RequireControl<TextBox>(window, "FrameNumberTextBox");
                    var frameEntryParent = Assert.IsType<StackPanel>(frameTextBox.Parent);
                    var controlGrid = Assert.IsType<Grid>(frameEntryParent.Parent);
                    var durationTextBlock = RequireControl<TextBlock>(window, "DurationTextBlock");

                    Assert.Equal(1, Grid.GetColumn(frameEntryParent));
                    Assert.Equal(2, Grid.GetRow(frameEntryParent));
                    Assert.Equal(HorizontalAlignment.Right, frameEntryParent.HorizontalAlignment);
                    Assert.Equal(VerticalAlignment.Center, frameEntryParent.VerticalAlignment);
                    Assert.Equal(new Thickness(0), frameEntryParent.Margin);
                    Assert.Equal(GridUnitType.Pixel, controlGrid.ColumnDefinitions[1].Width.GridUnitType);
                    Assert.Equal(240, controlGrid.ColumnDefinitions[1].Width.Value);
                    Assert.Equal(2, Grid.GetColumn(durationTextBlock));
                    Assert.Equal(HorizontalAlignment.Right, durationTextBlock.HorizontalAlignment);
                    Assert.Equal(TextAlignment.Right, durationTextBlock.TextAlignment);
                    Assert.Equal(104, frameTextBox.Width);

                    AssertFrameEntryRail(window, "PrimaryPaneFrameNumberTextBox", "PrimaryPaneDurationTextBlock", 176, 92, 6);
                    AssertFrameEntryRail(window, "ComparePaneFrameNumberTextBox", "ComparePaneDurationTextBlock", 176, 92, 6);
                }
                finally
                {
                    window.Close();
                }
            });
        }

        [Fact]
        public void MainTransport_ReservesClearanceAboveStatusPanel()
        {
            _fixture.Run(() =>
            {
                var window = new MainWindow();
                try
                {
                    var controlPanel = RequireControl<Border>(window, "ControlPanel");
                    var statusPanel = RequireControl<Border>(window, "StatusPanelContainer");
                    var shellGrid = Assert.IsType<Grid>(controlPanel.Parent);

                    Assert.Equal(GridUnitType.Pixel, shellGrid.RowDefinitions[4].Height.GridUnitType);
                    Assert.Equal(134, shellGrid.RowDefinitions[4].Height.Value);
                    Assert.Equal(GridUnitType.Auto, shellGrid.RowDefinitions[5].Height.GridUnitType);
                    Assert.Equal(new Thickness(14, 8, 14, 0), controlPanel.Margin);
                    Assert.Equal(new Thickness(18, 14, 18, 16), controlPanel.Padding);
                    Assert.Equal(new Thickness(1), controlPanel.BorderThickness);
                    Assert.Equal(new CornerRadius(10), controlPanel.CornerRadius);
                    Assert.True(controlPanel.MinHeight >= 126);
                    Assert.True(statusPanel.MinHeight >= 38);
                    Assert.Equal(new Thickness(14, 8, 14, 10), statusPanel.Margin);
                    Assert.Equal(new Thickness(4, 3), statusPanel.Padding);
                    Assert.Equal(new CornerRadius(10), statusPanel.CornerRadius);

                    var timelineGrid = RequireControl<Grid>(window, "MainTimelineRailGrid");
                    var positionSlider = RequireControl<Slider>(window, "PositionSlider");
                    var currentPosition = RequireControl<TextBlock>(window, "CurrentPositionTextBlock");
                    var duration = RequireControl<TextBlock>(window, "DurationTextBlock");
                    Assert.Equal(0, Grid.GetRow(timelineGrid));
                    Assert.Equal(2, Grid.GetColumnSpan(timelineGrid));
                    Assert.Equal(GridUnitType.Auto, timelineGrid.ColumnDefinitions[0].Width.GridUnitType);
                    Assert.Equal(GridUnitType.Star, timelineGrid.ColumnDefinitions[1].Width.GridUnitType);
                    Assert.Equal(GridUnitType.Auto, timelineGrid.ColumnDefinitions[2].Width.GridUnitType);
                    Assert.Same(timelineGrid, positionSlider.Parent);
                    Assert.Equal(1, Grid.GetColumn(positionSlider));
                    Assert.Equal(new Thickness(16, 0), positionSlider.Margin);
                    Assert.Equal(34, currentPosition.Height);
                    Assert.Equal(34, duration.Height);

                    var transportParent = Assert.IsType<StackPanel>(RequireControl<Button>(window, "PlayPauseButton").Parent);
                    Assert.Equal(VerticalAlignment.Center, transportParent.VerticalAlignment);
                    Assert.Equal(new Thickness(0), transportParent.Margin);

                    var frameEntryParent = Assert.IsType<StackPanel>(RequireControl<TextBox>(window, "FrameNumberTextBox").Parent);
                    Assert.Equal(HorizontalAlignment.Right, frameEntryParent.HorizontalAlignment);
                    Assert.Equal(VerticalAlignment.Center, frameEntryParent.VerticalAlignment);
                    Assert.Equal(new Thickness(0), frameEntryParent.Margin);

                    var cacheStatus = RequireControl<TextBlock>(window, "CacheStatusTextBlock");
                    Assert.Equal("A/V playback + frame review", RequireControl<TextBlock>(window, "PlaybackStateTextBlock").Text);
                    Assert.Equal("Frame --", RequireControl<TextBlock>(window, "CurrentFrameTextBlock").Text);
                    Assert.Equal("--:--:--.--- / --:--:--.---", RequireControl<TextBlock>(window, "TimecodeTextBlock").Text);
                    Assert.Equal("Cache: idle", cacheStatus.Text);
                    Assert.Equal("Pixel: --", RequireControl<TextBlock>(window, "PointerCoordinatesTextBlock").Text);
                    Assert.DoesNotContain("runtime", cacheStatus.Text, StringComparison.OrdinalIgnoreCase);
                    Assert.Equal(TextTrimming.CharacterEllipsis, cacheStatus.TextTrimming);
                    Assert.Equal(TextWrapping.NoWrap, cacheStatus.TextWrapping);
                }
                finally
                {
                    window.Close();
                }
            });
        }

        [Fact]
        public void TimelineSliders_ReserveFullThumbClearanceAtEndpoints()
        {
            _fixture.Run(() =>
            {
                var window = new MainWindow();
                try
                {
                    foreach (var sliderName in new[]
                    {
                        "PositionSlider",
                        "PrimaryPanePositionSlider",
                        "ComparePanePositionSlider"
                    })
                    {
                        var slider = RequireControl<Slider>(window, sliderName);
                        Assert.Contains("timeline-slider", slider.Classes);
                        Assert.Equal(34, slider.Height);
                        Assert.Equal(34, slider.MinHeight);
                        Assert.False(slider.ClipToBounds);
                        Assert.Equal(VerticalAlignment.Center, slider.VerticalAlignment);
                    }
                }
                finally
                {
                    window.Close();
                }
            });
        }

        [Fact]
        public void FontSizes_DoNotForceGlobalShrink()
        {
            _fixture.Run(() =>
            {
                var window = new MainWindow();
                try
                {
                    Assert.True(window.FontSize > 12);
                    Assert.True(RequireControl<CheckBox>(window, "CompareModeCheckBox").FontSize > 12);
                    Assert.True(RequireControl<CheckBox>(window, "AllPanesCheckBox").FontSize > 12);
                    Assert.True(RequireControl<TextBlock>(window, "CompareStatusTextBlock").FontSize > 12);
                    Assert.Equal(12, RequireControl<TextBlock>(window, "PlaybackStateTextBlock").FontSize);
                    Assert.True(RequireControl<TextBlock>(window, "CurrentPositionTextBlock").FontSize > 12);

                    Assert.Equal(16, RequireControl<TextBlock>(window, "CurrentFileTextBlock").FontSize);
                    Assert.Equal(11, RequireControl<TextBlock>(window, "PrimaryPaneFileTextBlock").FontSize);
                    Assert.Equal(11, RequireControl<TextBlock>(window, "ComparePaneFileTextBlock").FontSize);
                    Assert.Equal(13, RequireControl<TextBlock>(window, "LoopStatusTextBlock").FontSize);
                    Assert.Equal(11, RequireControl<TextBlock>(window, "PrimaryPaneLoopStatusTextBlock").FontSize);
                    Assert.Equal(11, RequireControl<TextBlock>(window, "ComparePaneLoopStatusTextBlock").FontSize);
                    Assert.Equal(30, RequireControl<TextBlock>(window, "PrimaryEmptyStateTitleTextBlock").FontSize);
                    Assert.Equal(20, RequireControl<TextBlock>(window, "CompareEmptyStateTitleTextBlock").FontSize);

                    var primaryOpenVideoButton = RequireControl<Button>(window, "PrimaryOpenVideoButton");
                    Assert.Equal(HorizontalAlignment.Center, primaryOpenVideoButton.HorizontalContentAlignment);
                    Assert.Equal(VerticalAlignment.Center, primaryOpenVideoButton.VerticalContentAlignment);
                    Assert.Equal(new Thickness(0), primaryOpenVideoButton.Margin);
                }
                finally
                {
                    window.Close();
                }
            });
        }

        [Fact]
        public void CompareToolbar_UsesSyncTerminology_ForPaneTimingActions()
        {
            _fixture.Run(() =>
            {
                var window = new MainWindow();
                try
                {
                    var syncRightToLeft = RequireControl<Button>(window, "AlignRightToLeftButton");
                    var syncLeftToRight = RequireControl<Button>(window, "AlignLeftToRightButton");
                    var compareStatus = RequireControl<TextBlock>(window, "CompareStatusTextBlock");

                    Assert.Equal("Sync Right to Left", syncRightToLeft.Content);
                    Assert.Equal("Sync Left to Right", syncLeftToRight.Content);
                    Assert.Equal(
                        "Compare: Load two videos to begin | Last sync: none",
                        compareStatus.Text);
                    Assert.Equal(TextTrimming.CharacterEllipsis, compareStatus.TextTrimming);
                    Assert.Equal(TextWrapping.NoWrap, compareStatus.TextWrapping);
                    Assert.DoesNotContain("Align", syncRightToLeft.Content?.ToString() ?? string.Empty, StringComparison.Ordinal);
                    Assert.DoesNotContain("Align", syncLeftToRight.Content?.ToString() ?? string.Empty, StringComparison.Ordinal);
                    Assert.DoesNotContain("alignment", compareStatus.Text ?? string.Empty, StringComparison.OrdinalIgnoreCase);
                }
                finally
                {
                    window.Close();
                }
            });
        }

        [Fact]
        public void RightClickContextMenus_ExposeWindowsParityCommands()
        {
            _fixture.Run(() =>
            {
                var window = new MainWindow();
                try
                {
                    AssertContextMenuHeaders(
                        RequireControl<Border>(window, "CustomVideoSurfaceHost").ContextMenu,
                        "Video Info...",
                        "Reset Zoom",
                        "Save Loop As Clip...",
                        "Export Side-by-Side Compare...");
                    AssertContextMenuHeaders(
                        RequireControl<Border>(window, "CompareVideoSurfaceHost").ContextMenu,
                        "Video Info...",
                        "Reset Zoom",
                        "Save Loop As Clip...",
                        "Export Side-by-Side Compare...");

                    foreach (var sliderName in new[]
                    {
                        "PositionSlider",
                        "PrimaryPanePositionSlider",
                        "ComparePanePositionSlider"
                    })
                    {
                        AssertContextMenuHeaders(
                            RequireControl<Slider>(window, sliderName).ContextMenu,
                            "Set Position A Here",
                            "Set Position B Here",
                            "Loop Playback",
                            "Save Loop As Clip...");
                    }
                }
                finally
                {
                    window.Close();
                }
            });
        }

        [Fact]
        public void RightClickContextMenus_UseMainMenuPaletteClasses()
        {
            _fixture.Run(() =>
            {
                var window = new MainWindow();
                try
                {
                    AssertContextMenuPalette(
                        RequireControl<Border>(window, "CustomVideoSurfaceHost").ContextMenu);
                    AssertContextMenuPalette(
                        RequireControl<Border>(window, "CompareVideoSurfaceHost").ContextMenu);

                    foreach (var sliderName in new[]
                    {
                        "PositionSlider",
                        "PrimaryPanePositionSlider",
                        "ComparePanePositionSlider"
                    })
                    {
                        AssertContextMenuPalette(
                            RequireControl<Slider>(window, sliderName).ContextMenu);
                    }
                }
                finally
                {
                    window.Close();
                }
            });
        }

        [Fact]
        public void NativeMenu_ExposesExpectedReleaseCommandsAndGestures()
        {
            _fixture.Run(() =>
            {
                var window = new MainWindow();
                try
                {
                var nativeMenu = NativeMenu.GetMenu(window);
                Assert.NotNull(nativeMenu);

                var newWindow = RequireNativeMenuItem(nativeMenu, "New Window");
                var openVideo = RequireNativeMenuItem(nativeMenu, "Open Video...");
                var closeVideo = RequireNativeMenuItem(nativeMenu, "Close Video");
                var exportDiagnostics = RequireNativeMenuItem(nativeMenu, "Export Diagnostic Report...");
                var play = RequireNativeMenuItem(nativeMenu, "Play");
                var rewind = RequireNativeMenuItem(nativeMenu, "Rewind 5s");
                var fastForward = RequireNativeMenuItem(nativeMenu, "Fast Forward 5s");
                var previousFrame = RequireNativeMenuItem(nativeMenu, "Previous Frame");
                var nextFrame = RequireNativeMenuItem(nativeMenu, "Next Frame");
                var loopPlayback = RequireNativeMenuItem(nativeMenu, "Loop Playback");
                var setLoopIn = RequireNativeMenuItem(nativeMenu, "Set Loop In");
                var setLoopOut = RequireNativeMenuItem(nativeMenu, "Set Loop Out");
                var zoomIn = RequireNativeMenuItem(nativeMenu, "Zoom In");
                var zoomOut = RequireNativeMenuItem(nativeMenu, "Zoom Out");
                var resetZoom = RequireNativeMenuItem(nativeMenu, "Reset Zoom");
                var fullScreen = RequireNativeMenuItem(nativeMenu, "Toggle Full Screen");
                var audioInsertion = RequireNativeMenuItem(nativeMenu, "Replace Audio Track...");
                var help = RequireNativeMenuItem(nativeMenu, "Controls and Shortcuts...");

                AssertGesture(newWindow.Gesture, Key.N, ExpectedCommandModifier);
                AssertGesture(openVideo.Gesture, Key.O, ExpectedCommandModifier);
                AssertGesture(closeVideo.Gesture, Key.W, ExpectedCommandModifier);
                AssertGesture(exportDiagnostics.Gesture, Key.E, ExpectedCommandShiftModifier);
                AssertGesture(play.Gesture, Key.Space, KeyModifiers.None);
                AssertGesture(rewind.Gesture, Key.OemComma, KeyModifiers.None);
                AssertGesture(fastForward.Gesture, Key.OemPeriod, KeyModifiers.None);
                AssertGesture(previousFrame.Gesture, Key.Left, KeyModifiers.None);
                AssertGesture(nextFrame.Gesture, Key.Right, KeyModifiers.None);
                AssertGesture(loopPlayback.Gesture, Key.L, KeyModifiers.None);
                AssertGesture(setLoopIn.Gesture, Key.OemOpenBrackets, KeyModifiers.None);
                AssertGesture(setLoopOut.Gesture, Key.OemCloseBrackets, KeyModifiers.None);
                AssertGesture(fullScreen.Gesture, Key.F11, KeyModifiers.None);
                AssertGesture(help.Gesture, Key.F1, KeyModifiers.None);
                Assert.NotNull(zoomIn);
                Assert.NotNull(zoomOut);
                Assert.NotNull(resetZoom);
                Assert.NotNull(audioInsertion);
                }
                finally
                {
                    window.Close();
                }
            });
        }

        [Fact]
        public void NativeMenuAndTransport_DisableMediaCommandsBeforeMediaLoads()
        {
            _fixture.Run(() =>
            {
                var window = new MainWindow();
                try
                {
                    var nativeMenu = NativeMenu.GetMenu(window);
                    Assert.NotNull(nativeMenu);

                    Assert.False(RequireNativeMenuItem(nativeMenu, "Close Video").IsEnabled);
                    Assert.False(RequireNativeMenuItem(nativeMenu, "Video Info...").IsEnabled);
                    Assert.False(RequireNativeMenuItem(nativeMenu, "Play").IsEnabled);
                    Assert.False(RequireNativeMenuItem(nativeMenu, "Rewind 5s").IsEnabled);
                    Assert.False(RequireNativeMenuItem(nativeMenu, "Fast Forward 5s").IsEnabled);
                    Assert.False(RequireNativeMenuItem(nativeMenu, "Previous Frame").IsEnabled);
                    Assert.False(RequireNativeMenuItem(nativeMenu, "Next Frame").IsEnabled);
                    Assert.False(RequireNativeMenuItem(nativeMenu, "Loop Playback").IsEnabled);
                    Assert.False(RequireNativeMenuItem(nativeMenu, "Set Loop In").IsEnabled);
                    Assert.False(RequireNativeMenuItem(nativeMenu, "Set Loop Out").IsEnabled);
                    Assert.False(RequireNativeMenuItem(nativeMenu, "Save Loop As Clip...").IsEnabled);
                    Assert.False(RequireNativeMenuItem(nativeMenu, "Export Side-by-Side Compare...").IsEnabled);
                    Assert.False(RequireNativeMenuItem(nativeMenu, "Replace Audio Track...").IsEnabled);

                    Assert.False(RequireControl<MenuItem>(window, "CloseVideoMenuItem").IsEnabled);
                    Assert.False(RequireControl<MenuItem>(window, "VideoInfoMenuItem").IsEnabled);
                    Assert.False(RequireControl<MenuItem>(window, "PlayPauseMenuItem").IsEnabled);
                    Assert.False(RequireControl<MenuItem>(window, "RewindMenuItem").IsEnabled);
                    Assert.False(RequireControl<MenuItem>(window, "FastForwardMenuItem").IsEnabled);
                    Assert.False(RequireControl<MenuItem>(window, "PreviousFrameMenuItem").IsEnabled);
                    Assert.False(RequireControl<MenuItem>(window, "NextFrameMenuItem").IsEnabled);
                    Assert.False(RequireControl<MenuItem>(window, "LoopPlaybackMenuItem").IsEnabled);
                    Assert.False(RequireControl<MenuItem>(window, "SetLoopInMenuItem").IsEnabled);
                    Assert.False(RequireControl<MenuItem>(window, "SetLoopOutMenuItem").IsEnabled);
                    Assert.False(RequireControl<MenuItem>(window, "SaveLoopAsClipMenuItem").IsEnabled);
                    Assert.False(RequireControl<MenuItem>(window, "ExportSideBySideCompareMenuItem").IsEnabled);
                    Assert.False(RequireControl<MenuItem>(window, "ReplaceAudioTrackMenuItem").IsEnabled);

                    Assert.False(RequireControl<Slider>(window, "PositionSlider").IsEnabled);
                    Assert.False(RequireControl<TextBox>(window, "FrameNumberTextBox").IsEnabled);
                    Assert.False(RequireControl<Button>(window, "PlayPauseButton").IsEnabled);
                    Assert.False(RequireControl<Button>(window, "PreviousFrameButton").IsEnabled);
                    Assert.False(RequireControl<Button>(window, "NextFrameButton").IsEnabled);
                    Assert.False(RequireControl<Button>(window, "PrimaryPanePlayPauseButton").IsEnabled);
                    Assert.False(RequireControl<Button>(window, "ComparePanePlayPauseButton").IsEnabled);
                    Assert.False(RequireControl<Button>(window, "AlignRightToLeftButton").IsEnabled);
                    Assert.False(RequireControl<Button>(window, "AlignLeftToRightButton").IsEnabled);
                }
                finally
                {
                    window.Close();
                }
            });
        }

        [Fact]
        public void ZoomCommands_CropPresentedFramesThroughNativeMenuCommandPath()
        {
            _fixture.Run(() =>
            {
                var window = new MainWindow();
                try
                {
                    var primarySurface = RequireControl<Image>(window, "CustomVideoSurface");
                    var compareSurface = RequireControl<Image>(window, "CompareVideoSurface");
                    var primarySurfaceHost = RequireControl<Border>(window, "CustomVideoSurfaceHost");
                    var compareMode = RequireControl<CheckBox>(window, "CompareModeCheckBox");
                    var linkZoom = RequireControl<CheckBox>(window, "LinkPaneZoomCheckBox");
                    var nativeMenu = NativeMenu.GetMenu(window)
                        ?? throw new InvalidOperationException("Missing native menu.");
                    var zoomIn = RequireNativeMenuItem(nativeMenu, "Zoom In");
                    var resetZoom = RequireNativeMenuItem(nativeMenu, "Reset Zoom");

                    InvokePrivate(window, "SetPaneBitmap", ParsePane("Primary"), CreateFrameBuffer(8, 4));
                    Assert.Equal(new PixelSize(8, 4), RequireBitmap(primarySurface).PixelSize);

                    zoomIn.Command!.Execute(null);
                    var primaryZoomedBitmap = RequireBitmap(primarySurface);
                    Assert.True(primaryZoomedBitmap.PixelSize.Width < 8);
                    Assert.True(primaryZoomedBitmap.PixelSize.Height <= 4);
                    Assert.Null(compareSurface.Source);

                    resetZoom.Command!.Execute(null);
                    Assert.Equal(new PixelSize(8, 4), RequireBitmap(primarySurface).PixelSize);

                    primarySurfaceHost.RaiseEvent(CreatePointerWheelChangedEvent(primarySurfaceHost, 1d));
                    Assert.True(RequireBitmap(primarySurface).PixelSize.Width < 8);

                    primarySurfaceHost.RaiseEvent(CreatePointerWheelChangedEvent(primarySurfaceHost, -1d));
                    Assert.Equal(new PixelSize(8, 4), RequireBitmap(primarySurface).PixelSize);

                    primarySurfaceHost.RaiseEvent(CreateScrollGestureEvent(1d));
                    Assert.True(RequireBitmap(primarySurface).PixelSize.Width < 8);

                    resetZoom.Command!.Execute(null);
                    Assert.Equal(new PixelSize(8, 4), RequireBitmap(primarySurface).PixelSize);

                    primarySurfaceHost.RaiseEvent(CreateTouchPadMagnifyEvent(primarySurfaceHost, 0.25d));
                    Assert.True(RequireBitmap(primarySurface).PixelSize.Width < 8);

                    resetZoom.Command!.Execute(null);
                    Assert.Equal(new PixelSize(8, 4), RequireBitmap(primarySurface).PixelSize);

                    compareMode.IsChecked = true;
                    linkZoom.IsChecked = true;
                    InvokePrivate(window, "SetPaneBitmap", ParsePane("Compare"), CreateFrameBuffer(8, 4));
                    zoomIn.Command!.Execute(null);

                    Assert.Equal(
                        RequireBitmap(primarySurface).PixelSize,
                        RequireBitmap(compareSurface).PixelSize);
                }
                finally
                {
                    window.Close();
                }
            });
        }

        private static PointerWheelEventArgs CreatePointerWheelChangedEvent(Control source, double deltaY)
        {
            return new PointerWheelEventArgs(
                source,
                null!,
                source,
                new Point(),
                0,
                new PointerPointProperties(),
                KeyModifiers.None,
                new Vector(0d, deltaY));
        }

        private static ScrollGestureEventArgs CreateScrollGestureEvent(double deltaY)
        {
            return new ScrollGestureEventArgs(1, new Vector(0d, deltaY));
        }

        private static PointerDeltaEventArgs CreateTouchPadMagnifyEvent(Control source, double delta)
        {
            return new PointerDeltaEventArgs(
                InputElement.PointerTouchPadGestureMagnifyEvent,
                source,
                null!,
                source,
                new Point(),
                0,
                new PointerPointProperties(),
                KeyModifiers.None,
                new Vector(delta, 0d));
        }

        private static WriteableBitmap RequireBitmap(Image image)
        {
            return Assert.IsType<WriteableBitmap>(image.Source);
        }

        private static DecodedFrameBuffer CreateFrameBuffer(int width, int height)
        {
            var pixels = new byte[width * height * 4];
            for (var index = 0; index < pixels.Length; index += 4)
            {
                pixels[index] = 0x40;
                pixels[index + 1] = 0x80;
                pixels[index + 2] = 0xC0;
                pixels[index + 3] = 0xFF;
            }

            return new DecodedFrameBuffer(
                new FrameDescriptor(
                    0,
                    TimeSpan.Zero,
                    false,
                    true,
                    width,
                    height,
                    "bgra",
                    "bgra",
                    null,
                    null,
                    null),
                pixels,
                width * 4,
                "bgra");
        }

        private static T RequireControl<T>(Window window, string name)
            where T : Control
        {
            return window.FindControl<T>(name)
                ?? throw new InvalidOperationException("Missing control: " + name);
        }

        private static void AssertPaneTransport(Window window, string panePrefix, string paneId)
        {
            var orderedNames = new[]
            {
                panePrefix + "PaneStepBackButton",
                panePrefix + "PaneSkipBackHundredFramesButton",
                panePrefix + "PanePlayPauseButton",
                panePrefix + "PaneSkipForwardHundredFramesButton",
                panePrefix + "PaneStepForwardButton"
            };

            var expectedTips = new[]
            {
                "Previous Frame",
                "Rewind 100 Frames",
                "Play",
                "Fast Forward 100 Frames",
                "Next Frame"
            };

            for (var index = 0; index < orderedNames.Length; index++)
            {
                var button = RequireControl<Button>(window, orderedNames[index]);
                Assert.Equal(paneId, button.Tag);
                Assert.Equal(expectedTips[index], ToolTip.GetTip(button));
            }

            var parent = RequireControl<Button>(window, panePrefix + "PanePlayPauseButton").Parent as StackPanel;
            Assert.NotNull(parent);
            Assert.Equal(
                orderedNames,
                parent!.Children.OfType<Button>().Select(button => button.Name).ToArray());
        }

        private static void AssertLoopStatusPlacement(Window window, string loopStatusName, string transportButtonName, double expectedWidth)
        {
            var loopStatus = RequireControl<TextBlock>(window, loopStatusName);
            var transportParent = RequireControl<Button>(window, transportButtonName).Parent as Control;
            Assert.NotNull(transportParent);
            Assert.Equal(expectedWidth, loopStatus.Width);
            Assert.Equal(1, Grid.GetRow(loopStatus));
            Assert.Equal(2, Grid.GetColumn(loopStatus));
            Assert.Equal(2, Grid.GetRow(transportParent!));
        }

        private static void AssertFrameEntryRail(
            Window window,
            string frameTextBoxName,
            string durationTextBlockName,
            double expectedRailWidth,
            double expectedTextBoxWidth,
            double expectedTopMargin)
        {
            var frameTextBox = RequireControl<TextBox>(window, frameTextBoxName);
            var frameEntryParent = Assert.IsType<StackPanel>(frameTextBox.Parent);
            var footerGrid = Assert.IsType<Grid>(frameEntryParent.Parent);
            var durationTextBlock = RequireControl<TextBlock>(window, durationTextBlockName);

            Assert.Equal(2, Grid.GetColumn(frameEntryParent));
            Assert.Equal(2, Grid.GetRow(frameEntryParent));
            Assert.Equal(HorizontalAlignment.Right, frameEntryParent.HorizontalAlignment);
            Assert.Equal(VerticalAlignment.Center, frameEntryParent.VerticalAlignment);
            Assert.Equal(new Thickness(0, expectedTopMargin, 0, 0), frameEntryParent.Margin);

            Assert.Equal(GridUnitType.Pixel, footerGrid.ColumnDefinitions[2].Width.GridUnitType);
            Assert.Equal(expectedRailWidth, footerGrid.ColumnDefinitions[2].Width.Value);
            Assert.Equal(2, Grid.GetColumn(durationTextBlock));
            Assert.Equal(HorizontalAlignment.Right, durationTextBlock.HorizontalAlignment);
            Assert.Equal(TextAlignment.Right, durationTextBlock.TextAlignment);

            Assert.Contains("frame-number-box", frameTextBox.Classes);
            Assert.Equal(expectedTextBoxWidth, frameTextBox.Width);
            Assert.Equal(32, frameTextBox.Height);
            Assert.Equal(32, frameTextBox.MinHeight);
            Assert.Equal(32, frameTextBox.MaxHeight);
            Assert.Equal(HorizontalAlignment.Right, frameTextBox.HorizontalContentAlignment);
            Assert.Equal(VerticalAlignment.Center, frameTextBox.VerticalContentAlignment);
        }

        private static NativeMenuItem RequireNativeMenuItem(NativeMenu menu, string header)
        {
            return EnumerateMenuItems(menu)
                .FirstOrDefault(item => string.Equals(item.Header, header, StringComparison.Ordinal))
                ?? throw new InvalidOperationException("Missing native menu item: " + header);
        }

        private static void AssertGesture(KeyGesture? gesture, Key key, KeyModifiers modifiers)
        {
            Assert.NotNull(gesture);
            Assert.Equal(key, gesture.Key);
            Assert.Equal(modifiers, gesture.KeyModifiers);
        }

        private static KeyModifiers ExpectedCommandModifier
        {
            get
            {
                return RuntimeInformation.IsOSPlatform(OSPlatform.OSX)
                    ? KeyModifiers.Meta
                    : KeyModifiers.Control;
            }
        }

        private static KeyModifiers ExpectedCommandShiftModifier
        {
            get
            {
                return ExpectedCommandModifier | KeyModifiers.Shift;
            }
        }

        private static void AssertContextMenuHeaders(ContextMenu? menu, params string[] expectedHeaders)
        {
            Assert.NotNull(menu);
            Assert.Equal(
                expectedHeaders,
                menu!.Items
                    .OfType<MenuItem>()
                    .Select(item => item.Header?.ToString() ?? string.Empty)
                    .ToArray());
        }

        private static void AssertMenuItemHeaders(MenuItem menuItem, params string[] expectedHeaders)
        {
            Assert.Equal(
                expectedHeaders,
                menuItem.Items
                    .OfType<MenuItem>()
                    .Select(item => item.Header?.ToString() ?? string.Empty)
                    .ToArray());
        }

        private static void AssertContextMenuPalette(ContextMenu? menu)
        {
            Assert.NotNull(menu);
            Assert.Contains("frame-context-menu", menu!.Classes);

            foreach (var item in menu.Items.OfType<MenuItem>())
            {
                Assert.Contains("frame-context-menu-item", item.Classes);
            }
        }

        private static object ParsePane(string name)
        {
            var paneType = typeof(MainWindow).GetNestedType("Pane", BindingFlags.NonPublic)
                ?? throw new InvalidOperationException("Missing MainWindow.Pane enum.");
            return Enum.Parse(paneType, name);
        }

        private static object InvokePrivate(MainWindow window, string methodName, params object[] args)
        {
            var method = typeof(MainWindow).GetMethod(methodName, BindingFlags.Instance | BindingFlags.NonPublic)
                ?? throw new MissingMethodException(typeof(MainWindow).FullName, methodName);
            return method.Invoke(window, args) ?? new object();
        }

        private static void AssertBrushColor(string expectedColor, IBrush? brush)
        {
            var solid = Assert.IsAssignableFrom<ISolidColorBrush>(brush);
            Assert.Equal(Color.Parse(expectedColor), solid.Color);
        }

        private static IQueryable<NativeMenuItem> EnumerateMenuItems(NativeMenu menu)
        {
            return Enumerate(menu).AsQueryable();
        }

        private static System.Collections.Generic.IEnumerable<NativeMenuItem> Enumerate(NativeMenu menu)
        {
            foreach (var item in menu.Items.OfType<NativeMenuItem>())
            {
                yield return item;

                if (item.Menu != null)
                {
                    foreach (var child in Enumerate(item.Menu))
                    {
                        yield return child;
                    }
                }
            }
        }
    }
}
