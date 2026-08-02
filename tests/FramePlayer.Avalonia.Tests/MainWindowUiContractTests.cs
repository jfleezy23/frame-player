using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;
using Avalonia.VisualTree;
using FramePlayer.Core.Abstractions;
using FramePlayer.Core.Coordination;
using FramePlayer.Core.Events;
using FramePlayer.Core.Models;
using FramePlayer.Avalonia.Views;
using Microsoft.Win32.SafeHandles;
using Xunit;

namespace FramePlayer.Avalonia.Tests
{
    public sealed class MainWindowUiContractTests : IClassFixture<AvaloniaHeadlessFixture>
    {
        private static readonly string[] TopLevelMenuHeaders = { "File", "Playback", "Audio Insertion", "Help" };
        private static readonly string[] TransportButtonNames =
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
        };
        private static readonly string[] TimelineSliderNames =
        {
            "PositionSlider",
            "PrimaryPanePositionSlider",
            "ComparePanePositionSlider"
        };
        private readonly AvaloniaHeadlessFixture _fixture;

        public MainWindowUiContractTests(AvaloniaHeadlessFixture fixture)
        {
            _fixture = fixture;
        }

        [Fact]
        public void MainWindow_UsesPlatformChromeWithAvaloniaAndNativeMenus()
        {
            _fixture.Run(() =>
            {
                var window = new MainWindow();
                try
                {

                    var nativeMenu = NativeMenu.GetMenu(window);
                    Assert.NotNull(nativeMenu);
                    var menuPanel = RequireControl<Border>(window, "MenuPanel");
                    if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
                    {
                        Assert.Equal(WindowDecorations.Full, window.WindowDecorations);
                        Assert.False(menuPanel.IsVisible);
                        Assert.Empty(window.GetVisualDescendants().OfType<Menu>());
                    }
                    else
                    {
                        Assert.Equal(WindowDecorations.None, window.WindowDecorations);
                        Assert.NotNull(window.Icon);

                        Assert.Equal(0, Grid.GetRow(menuPanel));
                        Assert.Equal(new Thickness(0, 0, 0, 1), menuPanel.BorderThickness);
                        AssertBrushColor("#FFFFFF", menuPanel.Background);
                        AssertBrushColor("#D1D5DB", menuPanel.BorderBrush);

                        var visualMenu = RequireControl<Menu>(window, "WindowsMenuBar");
                        var menuThemeScope = Assert.IsType<ThemeVariantScope>(visualMenu.Parent);
                        Assert.Equal(1, Grid.GetColumn(menuThemeScope));
                        Assert.Equal(
                            TopLevelMenuHeaders,
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
                    }

                    var topLevelHeaders = nativeMenu.Items
                        .OfType<NativeMenuItem>()
                        .Select(item => item.Header)
                        .ToArray();

                    Assert.Equal(
                        TopLevelMenuHeaders,
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
        public void CompareMode_SelectingAPaneDoesNotDispatchTransport()
        {
            _fixture.Run(() =>
            {
                var window = new MainWindow();
                try
                {
                    var primaryEngine = new TestVideoReviewEngine { IsMediaOpen = true };
                    var compareEngine = new TestVideoReviewEngine { IsMediaOpen = true };
                    SetPrivateField(window, "_primaryEngine", primaryEngine);
                    SetPrivateField(window, "_compareEngine", compareEngine);
                    RequireControl<CheckBox>(window, "CompareModeCheckBox").IsChecked = true;

                    InvokePrivate(window, "SelectPane", ParsePane("Primary"));
                    InvokePrivate(window, "SelectPane", ParsePane("Compare"));

                    Assert.False(primaryEngine.IsPlaying);
                    Assert.False(compareEngine.IsPlaying);
                    Assert.Equal(0, primaryEngine.PlayCallCount);
                    Assert.Equal(0, compareEngine.PlayCallCount);
                    Assert.Equal(0, primaryEngine.PauseCallCount);
                    Assert.Equal(0, compareEngine.PauseCallCount);
                    Assert.Equal(0, primaryEngine.SeekToTimeCallCount);
                    Assert.Equal(0, compareEngine.SeekToTimeCallCount);
                }
                finally
                {
                    window.Close();
                }
            });
        }

        [Theory]
        [InlineData("Primary")]
        [InlineData("Compare")]
        public async Task ComparePlayback_PanePlayPauseDoesNotAdvanceMasterTransportIntent(
            string paneName)
        {
            await _fixture.RunAsync(async () =>
            {
                var window = new MainWindow();
                try
                {
                    var primaryEngine = new TestVideoReviewEngine { IsMediaOpen = true };
                    var compareEngine = new TestVideoReviewEngine { IsMediaOpen = true };
                    SetPrivateField(window, "_primaryEngine", primaryEngine);
                    SetPrivateField(window, "_compareEngine", compareEngine);
                    RequireControl<CheckBox>(window, "CompareModeCheckBox").IsChecked = true;
                    var masterIntentBefore = GetPrivateField<int>(
                        window,
                        "_allPaneTransportIntentGeneration");
                    var pane = ParsePane(paneName);

                    await InvokePrivateTask(
                        window,
                        "TogglePanePlaybackAsync",
                        new[] { pane.GetType() },
                        pane);

                    Assert.Equal(
                        masterIntentBefore,
                        GetPrivateField<int>(
                            window,
                            "_allPaneTransportIntentGeneration"));
                    Assert.Equal(
                        paneName == "Primary",
                        primaryEngine.IsPlaying);
                    Assert.Equal(
                        paneName == "Compare",
                        compareEngine.IsPlaying);
                }
                finally
                {
                    window.Close();
                }
            });
        }

        [Fact]
        public async Task ComparePlayback_LocalPanePlayDoesNotWaitForPeerPanePlayToFinish()
        {
            await _fixture.RunAsync(async () =>
            {
                var window = new MainWindow();
                Task? delayedRightPlay = null;
                Task? leftPlay = null;
                var rightPlayCompletion = new TaskCompletionSource<bool>(
                    TaskCreationOptions.RunContinuationsAsynchronously);
                try
                {
                    var primaryEngine = new TestVideoReviewEngine
                    {
                        IsMediaOpen = true
                    };
                    var compareEngine = new TestVideoReviewEngine
                    {
                        IsMediaOpen = true,
                        PlayCompletion = rightPlayCompletion
                    };
                    SetPrivateField(window, "_primaryEngine", primaryEngine);
                    SetPrivateField(window, "_compareEngine", compareEngine);
                    RequireControl<CheckBox>(window, "CompareModeCheckBox").IsChecked = true;

                    delayedRightPlay = InvokePrivateTask(
                        window,
                        "TogglePanePlaybackAsync",
                        new[] { ParsePane("Compare").GetType() },
                        ParsePane("Compare"));
                    Assert.True(
                        SpinWait.SpinUntil(
                            () => compareEngine.PlayCallCount == 1,
                            TimeSpan.FromSeconds(2)),
                        "The delayed right-pane play did not reach its engine.");
                    Assert.True(compareEngine.IsPlaying);
                    Assert.False(delayedRightPlay.IsCompleted);

                    leftPlay = InvokePrivateTask(
                        window,
                        "TogglePanePlaybackAsync",
                        new[] { ParsePane("Primary").GetType() },
                        ParsePane("Primary"));
                    var leftCompleted =
                        await Task.WhenAny(
                            leftPlay,
                            Task.Delay(TimeSpan.FromMilliseconds(250))) ==
                        leftPlay;
                    if (leftCompleted)
                    {
                        await leftPlay;
                    }

                    Assert.True(
                        leftCompleted,
                        "A left local play waited for the right local play to finish.");
                    Assert.Equal(1, primaryEngine.PlayCallCount);
                    Assert.Equal(1, compareEngine.PlayCallCount);
                    Assert.True(primaryEngine.IsPlaying);
                    Assert.True(compareEngine.IsPlaying);
                    Assert.False(delayedRightPlay.IsCompleted);
                }
                finally
                {
                    rightPlayCompletion.TrySetResult(true);
                    if (leftPlay != null)
                    {
                        await leftPlay.WaitAsync(TimeSpan.FromSeconds(2));
                    }

                    if (delayedRightPlay != null)
                    {
                        await delayedRightPlay.WaitAsync(TimeSpan.FromSeconds(2));
                    }

                    window.Close();
                }
            });
        }

        [Fact]
        public void CompareMode_DisablePausesHiddenComparePlayback()
        {
            _fixture.Run(() =>
            {
                var window = new MainWindow();
                try
                {
                    var compareMode = RequireControl<CheckBox>(window, "CompareModeCheckBox");
                    var compareEngine = new TestVideoReviewEngine
                    {
                        IsMediaOpen = true,
                        IsPlaying = true
                    };

                    compareMode.IsChecked = true;
                    SetPrivateField(window, "_compareEngine", compareEngine);

                    compareMode.IsChecked = false;

                    Assert.False(compareEngine.IsPlaying);
                    Assert.Equal(1, compareEngine.PauseCallCount);
                }
                finally
                {
                    window.Close();
                }
            });
        }

        [Fact]
        public async Task CompareMode_DisableHidesPaneBeforePauseCompletes()
        {
            await _fixture.RunAsync(async () =>
            {
                var window = new MainWindow();
                try
                {
                    var compareMode = RequireControl<CheckBox>(window, "CompareModeCheckBox");
                    var comparePaneBorder = RequireControl<Border>(window, "ComparePaneBorder");
                    var primaryPaneHeader = RequireControl<Border>(window, "PrimaryPaneHeaderBorder");
                    var pauseCompletion = new TaskCompletionSource<bool>();
                    var compareEngine = new TestVideoReviewEngine
                    {
                        IsMediaOpen = true,
                        IsPlaying = true,
                        PauseCompletion = pauseCompletion
                    };

                    compareMode.IsChecked = true;
                    SetPrivateField(window, "_compareEngine", compareEngine);

                    compareMode.IsChecked = false;

                    Assert.False(comparePaneBorder.IsVisible);
                    Assert.False(primaryPaneHeader.IsVisible);
                    Assert.Equal(1, compareEngine.PauseCallCount);

                    compareMode.IsChecked = true;
                    pauseCompletion.SetResult(true);
                    await Task.Yield();

                    Assert.True(comparePaneBorder.IsVisible);
                    Assert.True(primaryPaneHeader.IsVisible);
                }
                finally
                {
                    window.Close();
                }
            });
        }

        [Fact]
        public void CompareFramePresented_WhenCompareHidden_DoesNotReplaceCompareBitmap()
        {
            MainWindow? window = null;
            try
            {
                _fixture.Run(() =>
                {
                    window = new MainWindow();
                    RequireControl<CheckBox>(window, "CompareModeCheckBox").IsChecked = true;
                    InvokePrivate(
                        window,
                        "CompareEngine_FramePresented",
                        null!,
                        new FramePresentedEventArgs(CreateFrameBuffer(8, 4)));
                });

                _fixture.Run(() =>
                {
                    var compareSurface = RequireControl<Image>(window!, "CompareVideoSurface");
                    Assert.Equal(new PixelSize(8, 4), RequireBitmap(compareSurface).PixelSize);
                    RequireControl<CheckBox>(window!, "CompareModeCheckBox").IsChecked = false;
                    InvokePrivate(
                        window!,
                        "CompareEngine_FramePresented",
                        null!,
                        new FramePresentedEventArgs(CreateFrameBuffer(4, 4)));
                });

                _fixture.Run(() =>
                {
                    var compareSurface = RequireControl<Image>(window!, "CompareVideoSurface");
                    Assert.Equal(new PixelSize(8, 4), RequireBitmap(compareSurface).PixelSize);
                });
            }
            finally
            {
                if (window != null)
                {
                    _fixture.Run(() => window.Close());
                }
            }
        }

        [Fact]
        public async Task ComparePlayback_PairsEarliestUnmatchedFramesAcrossTransportTransitions()
        {
            await _fixture.RunAsync(async () =>
            {
                var window = new MainWindow();
                try
                {
                    var frameStep = TimeSpan.FromSeconds(1d / 30d);
                    var mediaInfo = new VideoMediaInfo(
                        "shared.mp4",
                        TimeSpan.FromSeconds(10),
                        frameStep,
                        30d,
                        3840,
                        2160,
                        "h264",
                        0,
                        30,
                        1,
                        1,
                        90_000);
                    var primaryEngine = new TestVideoReviewEngine
                    {
                        IsMediaOpen = true,
                        CurrentFilePath = "shared.mp4",
                        MediaInfo = mediaInfo
                    };
                    var compareEngine = new TestVideoReviewEngine
                    {
                        IsMediaOpen = true,
                        CurrentFilePath = "shared.mp4",
                        MediaInfo = mediaInfo
                    };

                    SetPrivateField(window, "_primaryEngine", primaryEngine);
                    SetPrivateField(window, "_compareEngine", compareEngine);
                    RequireControl<CheckBox>(window, "CompareModeCheckBox").IsChecked = true;
                    await InvokePrivateTask(window, "StartAllPanePlaybackAsync", Type.EmptyTypes);

                    Assert.True(GetPrivateField<bool>(window, "_isSynchronizedFramePresentationActive"));

                    var preSeekGeneration = GetPrivateField<long>(window, "_synchronizedFramePresentationGeneration");
                    using var preSeekPrimary = CreateFrameBuffer(8, 4, TimeSpan.FromSeconds(9));
                    InvokePrivate(
                        window,
                        "PrimaryEngine_FramePresented",
                        null!,
                        new FramePresentedEventArgs(preSeekPrimary));
                    await InvokePrivateTask(
                        window,
                        "SeekAllPaneToTimesPreservingPlaybackAsync",
                        new[] { typeof(TimeSpan), typeof(TimeSpan), typeof(CancellationToken) },
                        TimeSpan.FromSeconds(1),
                        TimeSpan.FromSeconds(1),
                        CancellationToken.None);

                    var postSeekGeneration = GetPrivateField<long>(window, "_synchronizedFramePresentationGeneration");
                    Assert.True(postSeekGeneration > preSeekGeneration);
                    Assert.Null(GetPrivateField<DecodedFrameBuffer>(window, "_pendingSynchronizedPrimaryFrame"));
                    InvokePrivate(window, "PresentPendingSynchronizedFrames", preSeekGeneration);
                    Assert.Null(GetPrivateField<DecodedFrameBuffer>(window, "_primaryFrameBuffer"));

                    primaryEngine.IsPlaying = false;
                    compareEngine.IsPlaying = false;
                    var firstTime = TimeSpan.FromSeconds(1);
                    using var firstPrimary = CreateFrameBuffer(8, 4, firstTime);
                    using var firstCompare = CreateFrameBuffer(8, 4, firstTime);
                    InvokePrivate(
                        window,
                        "PrimaryEngine_FramePresented",
                        null!,
                        new FramePresentedEventArgs(firstPrimary));
                    InvokePrivate(
                        window,
                        "CompareEngine_FramePresented",
                        null!,
                        new FramePresentedEventArgs(firstCompare));
                    InvokePrivate(
                        window,
                        "PresentPendingSynchronizedFrames",
                        GetPrivateField<long>(window, "_synchronizedFramePresentationGeneration"));

                    Assert.Equal(firstTime, GetPrivateField<DecodedFrameBuffer>(window, "_primaryFrameBuffer")!.Descriptor.PresentationTime);
                    Assert.Equal(firstTime, GetPrivateField<DecodedFrameBuffer>(window, "_compareFrameBuffer")!.Descriptor.PresentationTime);

                    var earliestPrimaryTime = TimeSpan.FromSeconds(2);
                    var latestPrimaryTime = earliestPrimaryTime + frameStep;
                    var latestCompareTime = latestPrimaryTime + TimeSpan.FromMilliseconds(5);
                    using var earliestPrimary = CreateFrameBuffer(8, 4, earliestPrimaryTime);
                    using var latestPrimary = CreateFrameBuffer(8, 4, latestPrimaryTime);
                    using var earliestCompare = CreateFrameBuffer(8, 4, earliestPrimaryTime);
                    using var latestCompare = CreateFrameBuffer(8, 4, latestCompareTime);
                    InvokePrivate(
                        window,
                        "PrimaryEngine_FramePresented",
                        null!,
                        new FramePresentedEventArgs(earliestPrimary));
                    InvokePrivate(
                        window,
                        "PrimaryEngine_FramePresented",
                        null!,
                        new FramePresentedEventArgs(latestPrimary));
                    InvokePrivate(
                        window,
                        "CompareEngine_FramePresented",
                        null!,
                        new FramePresentedEventArgs(earliestCompare));
                    InvokePrivate(
                        window,
                        "PresentPendingSynchronizedFrames",
                        GetPrivateField<long>(window, "_synchronizedFramePresentationGeneration"));

                    Assert.Equal(
                        earliestPrimaryTime,
                        GetPrivateField<DecodedFrameBuffer>(window, "_primaryFrameBuffer")!.Descriptor.PresentationTime);
                    Assert.Equal(
                        earliestPrimaryTime,
                        GetPrivateField<DecodedFrameBuffer>(window, "_compareFrameBuffer")!.Descriptor.PresentationTime);

                    InvokePrivate(
                        window,
                        "CompareEngine_FramePresented",
                        null!,
                        new FramePresentedEventArgs(latestCompare));
                    InvokePrivate(
                        window,
                        "PrimaryEngine_FramePresented",
                        null!,
                        new FramePresentedEventArgs(latestPrimary));
                    InvokePrivate(
                        window,
                        "PresentPendingSynchronizedFrames",
                        GetPrivateField<long>(window, "_synchronizedFramePresentationGeneration"));

                    var presentedPrimary = GetPrivateField<DecodedFrameBuffer>(window, "_primaryFrameBuffer")!;
                    var presentedCompare = GetPrivateField<DecodedFrameBuffer>(window, "_compareFrameBuffer")!;
                    Assert.Equal(latestPrimaryTime, presentedPrimary.Descriptor.PresentationTime);
                    Assert.Equal(latestCompareTime, presentedCompare.Descriptor.PresentationTime);
                    Assert.True(
                        (presentedPrimary.Descriptor.PresentationTime - presentedCompare.Descriptor.PresentationTime).Duration() <
                        frameStep);
                    Assert.Null(GetPrivateField<DecodedFrameBuffer>(window, "_pendingSynchronizedPrimaryFrame"));
                    Assert.Null(GetPrivateField<DecodedFrameBuffer>(window, "_pendingSynchronizedCompareFrame"));

                    InvokePrivate(
                        window,
                        "PaneStepForwardButton_Click",
                        RequireControl<Button>(window, "PrimaryPaneStepForwardButton"),
                        new RoutedEventArgs());
                    Assert.False(GetPrivateField<bool>(window, "_isSynchronizedFramePresentationActive"));

                    await InvokePrivateTask(
                        window,
                        "PausePlaybackAsync",
                        new[] { typeof(bool), typeof(SynchronizedOperationScope?) },
                        true,
                        (SynchronizedOperationScope?)SynchronizedOperationScope.AllPanes);
                    Assert.False(GetPrivateField<bool>(window, "_isSynchronizedFramePresentationActive"));

                    primaryEngine.IsPlaying = true;
                    compareEngine.IsPlaying = false;
                    await InvokePrivateTask(
                        window,
                        "SeekAllPaneToTimesPreservingPlaybackAsync",
                        new[] { typeof(TimeSpan), typeof(TimeSpan), typeof(CancellationToken) },
                        TimeSpan.FromSeconds(5),
                        TimeSpan.FromSeconds(5),
                        CancellationToken.None);
                    Assert.True(primaryEngine.IsPlaying);
                    Assert.False(compareEngine.IsPlaying);
                    Assert.False(GetPrivateField<bool>(window, "_isSynchronizedFramePresentationActive"));

                    primaryEngine.IsPlaying = true;
                    compareEngine.IsPlaying = true;
                    var primaryPlayCount = primaryEngine.PlayCallCount;
                    var comparePlayCount = compareEngine.PlayCallCount;
                    var delayedResume = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                    primaryEngine.PlayCompletion = delayedResume;
                    compareEngine.PlayCompletion = delayedResume;
                    var staleSeekTask = InvokePrivateTask(
                        window,
                        "SeekAllPaneToTimesPreservingPlaybackAsync",
                        new[] { typeof(TimeSpan), typeof(TimeSpan), typeof(CancellationToken) },
                        TimeSpan.FromSeconds(6),
                        TimeSpan.FromSeconds(6),
                        CancellationToken.None);
                    Assert.True(
                        SpinWait.SpinUntil(
                            () => primaryEngine.PlayCallCount > primaryPlayCount &&
                                compareEngine.PlayCallCount > comparePlayCount,
                            TimeSpan.FromSeconds(2)),
                        "The delayed shared-seek resume did not start both panes.");

                    var queuedPauseTask = InvokePrivateTask(
                        window,
                        "PausePlaybackAsync",
                        new[] { typeof(bool), typeof(SynchronizedOperationScope?) },
                        true,
                        (SynchronizedOperationScope?)SynchronizedOperationScope.AllPanes);
                    delayedResume.TrySetResult(true);
                    await Task.WhenAll(staleSeekTask, queuedPauseTask);
                    primaryEngine.PlayCompletion = null;
                    compareEngine.PlayCompletion = null;

                    Assert.False(primaryEngine.IsPlaying);
                    Assert.False(compareEngine.IsPlaying);
                    Assert.False(GetPrivateField<bool>(window, "_isSynchronizedFramePresentationActive"));

                    primaryEngine.IsPlaying = true;
                    compareEngine.IsPlaying = true;
                    var primarySeekCount = primaryEngine.SeekToTimeCallCount;
                    var compareSeekCount = compareEngine.SeekToTimeCallCount;
                    var firstSeekDelay = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                    primaryEngine.SeekTimeCompletion = firstSeekDelay;
                    compareEngine.SeekTimeCompletion = firstSeekDelay;
                    var firstSeek = InvokePrivateTask(
                        window,
                        "SeekAllPaneToTimesPreservingPlaybackAsync",
                        new[] { typeof(TimeSpan), typeof(TimeSpan), typeof(CancellationToken) },
                        TimeSpan.FromSeconds(7),
                        TimeSpan.FromSeconds(7),
                        CancellationToken.None);
                    Assert.True(
                        SpinWait.SpinUntil(
                            () => primaryEngine.SeekToTimeCallCount > primarySeekCount &&
                                compareEngine.SeekToTimeCallCount > compareSeekCount,
                            TimeSpan.FromSeconds(2)),
                        "The first serialized shared seek did not reach both engines.");

                    primaryEngine.SeekTimeCompletion = null;
                    compareEngine.SeekTimeCompletion = null;
                    var secondSeek = InvokePrivateTask(
                        window,
                        "SeekAllPaneToTimesPreservingPlaybackAsync",
                        new[] { typeof(TimeSpan), typeof(TimeSpan), typeof(CancellationToken) },
                        TimeSpan.FromSeconds(8),
                        TimeSpan.FromSeconds(8),
                        CancellationToken.None);
                    firstSeekDelay.TrySetResult(true);
                    await Task.WhenAll(firstSeek, secondSeek);

                    Assert.True(primaryEngine.IsPlaying);
                    Assert.True(compareEngine.IsPlaying);
                    Assert.True(GetPrivateField<bool>(window, "_isSynchronizedFramePresentationActive"));

                    await InvokePrivateTask(
                        window,
                        "PausePlaybackAsync",
                        new[] { typeof(bool), typeof(SynchronizedOperationScope?) },
                        true,
                        (SynchronizedOperationScope?)SynchronizedOperationScope.AllPanes);
                }
                finally
                {
                    window.Close();
                }
            });
        }

        [Fact]
        public void ComparePlayback_ReadoutsFollowPresentedFramesAcrossSynchronization()
        {
            _fixture.Run(() =>
            {
                var window = new MainWindow();
                try
                {
                    var frameStep = TimeSpan.FromSeconds(1d / 30d);
                    var mediaInfo = new VideoMediaInfo(
                        "readout-sync.mp4",
                        TimeSpan.FromSeconds(10),
                        frameStep,
                        30d,
                        1920,
                        1080,
                        "h264",
                        0,
                        30,
                        1,
                        1,
                        90_000);
                    var primaryEngine = new TestVideoReviewEngine
                    {
                        IsMediaOpen = true,
                        CurrentFilePath = "readout-sync.mp4",
                        MediaInfo = mediaInfo
                    };
                    var compareEngine = new TestVideoReviewEngine
                    {
                        IsMediaOpen = true,
                        CurrentFilePath = "readout-sync.mp4",
                        MediaInfo = mediaInfo
                    };
                    SetPrivateField(window, "_primaryEngine", primaryEngine);
                    SetPrivateField(window, "_compareEngine", compareEngine);
                    RequireControl<CheckBox>(window, "CompareModeCheckBox").IsChecked = true;

                    var initialPrimaryState = new VideoReviewEngineStateChangedEventArgs(
                        isMediaOpen: true,
                        isPlaying: false,
                        currentFilePath: "readout-sync.mp4",
                        lastErrorMessage: string.Empty,
                        mediaInfo: mediaInfo,
                        position: new ReviewPosition(
                            TimeSpan.FromSeconds(2),
                            60,
                            isFrameAccurate: true,
                            isFrameIndexAbsolute: true,
                            presentationTimestamp: 180_000,
                            decodeTimestamp: 180_000));
                    var initialCompareState = new VideoReviewEngineStateChangedEventArgs(
                        isMediaOpen: true,
                        isPlaying: false,
                        currentFilePath: "readout-sync.mp4",
                        lastErrorMessage: string.Empty,
                        mediaInfo: mediaInfo,
                        position: new ReviewPosition(
                            TimeSpan.FromSeconds(5),
                            150,
                            isFrameAccurate: true,
                            isFrameIndexAbsolute: true,
                            presentationTimestamp: 450_000,
                            decodeTimestamp: 450_000));
                    InvokePrivate(window, "ApplyState", ParsePane("Primary"), initialPrimaryState);
                    InvokePrivate(window, "ApplyState", ParsePane("Compare"), initialCompareState);

                    var transportIntentGeneration = (int)InvokePrivate(
                        window,
                        "BeginAllPaneTransportIntent");
                    Assert.True((bool)InvokePrivate(
                        window,
                        "TryBeginSynchronizedFramePresentationAtOffset",
                        transportIntentGeneration,
                        TimeSpan.FromSeconds(-3)));
                    using var primaryPairFrame = CreateFrameBuffer(
                        8,
                        4,
                        TimeSpan.FromSeconds(2.2));
                    using var comparePairFrame = CreateFrameBuffer(
                        8,
                        4,
                        TimeSpan.FromSeconds(5.2));
                    InvokePrivate(
                        window,
                        "SetPaneBitmap",
                        ParsePane("Primary"),
                        primaryPairFrame);
                    InvokePrivate(
                        window,
                        "SetPaneBitmap",
                        ParsePane("Compare"),
                        comparePairFrame);
                    InvokePrivate(
                        window,
                        "ApplyPresentedFramePosition",
                        ParsePane("Primary"),
                        primaryPairFrame.Descriptor,
                        true);
                    InvokePrivate(
                        window,
                        "ApplyPresentedFramePosition",
                        ParsePane("Compare"),
                        comparePairFrame.Descriptor,
                        false);

                    Assert.Equal(2.2d, RequireControl<Slider>(window, "PositionSlider").Value);
                    Assert.Equal(2.2d, RequireControl<Slider>(window, "PrimaryPanePositionSlider").Value);
                    Assert.Equal("00:00:02.200", RequireControl<TextBlock>(window, "CurrentPositionTextBlock").Text);
                    Assert.Equal("00:00:02.200", RequireControl<TextBlock>(window, "PrimaryPaneCurrentPositionTextBlock").Text);
                    Assert.Equal("Frame 67", RequireControl<TextBlock>(window, "CurrentFrameTextBlock").Text);
                    Assert.Equal("00:00:02.200 / 00:00:10.000", RequireControl<TextBlock>(window, "TimecodeTextBlock").Text);
                    Assert.Equal("67", RequireControl<TextBox>(window, "FrameNumberTextBox").Text);
                    Assert.Equal("67", RequireControl<TextBox>(window, "PrimaryPaneFrameNumberTextBox").Text);
                    Assert.Equal(5.2d, RequireControl<Slider>(window, "ComparePanePositionSlider").Value);
                    Assert.Equal("00:00:05.200", RequireControl<TextBlock>(window, "ComparePaneCurrentPositionTextBlock").Text);
                    Assert.Equal("157", RequireControl<TextBox>(window, "ComparePaneFrameNumberTextBox").Text);

                    var stalePrimaryState = new VideoReviewEngineStateChangedEventArgs(
                        isMediaOpen: true,
                        isPlaying: true,
                        currentFilePath: "readout-sync.mp4",
                        lastErrorMessage: string.Empty,
                        mediaInfo: mediaInfo,
                        position: new ReviewPosition(
                            TimeSpan.FromSeconds(9),
                            270,
                            isFrameAccurate: true,
                            isFrameIndexAbsolute: true,
                            presentationTimestamp: 810_000,
                            decodeTimestamp: 810_000));
                    var staleCompareState = new VideoReviewEngineStateChangedEventArgs(
                        isMediaOpen: true,
                        isPlaying: false,
                        currentFilePath: "readout-sync.mp4",
                        lastErrorMessage: string.Empty,
                        mediaInfo: mediaInfo,
                        position: new ReviewPosition(
                            TimeSpan.FromSeconds(1),
                            30,
                            isFrameAccurate: true,
                            isFrameIndexAbsolute: true,
                            presentationTimestamp: 90_000,
                            decodeTimestamp: 90_000));
                    InvokePrivate(window, "ApplyState", ParsePane("Primary"), stalePrimaryState);
                    InvokePrivate(window, "ApplyState", ParsePane("Compare"), staleCompareState);

                    Assert.Equal(
                        TimeSpan.FromSeconds(2.2),
                        GetPrivateField<DecodedFrameBuffer>(
                            window,
                            "_primaryFrameBuffer")!.Descriptor.PresentationTime);
                    Assert.Equal(
                        TimeSpan.FromSeconds(5.2),
                        GetPrivateField<DecodedFrameBuffer>(
                            window,
                            "_compareFrameBuffer")!.Descriptor.PresentationTime);
                    Assert.Equal(2.2d, RequireControl<Slider>(window, "PositionSlider").Value);
                    Assert.Equal(2.2d, RequireControl<Slider>(window, "PrimaryPanePositionSlider").Value);
                    Assert.Equal(5.2d, RequireControl<Slider>(window, "ComparePanePositionSlider").Value);
                    Assert.Equal("00:00:02.200", RequireControl<TextBlock>(window, "CurrentPositionTextBlock").Text);
                    Assert.Equal("00:00:02.200", RequireControl<TextBlock>(window, "PrimaryPaneCurrentPositionTextBlock").Text);
                    Assert.Equal("Frame 67", RequireControl<TextBlock>(window, "CurrentFrameTextBlock").Text);
                    Assert.Equal("00:00:02.200 / 00:00:10.000", RequireControl<TextBlock>(window, "TimecodeTextBlock").Text);
                    Assert.Equal("67", RequireControl<TextBox>(window, "FrameNumberTextBox").Text);
                    Assert.Equal("67", RequireControl<TextBox>(window, "PrimaryPaneFrameNumberTextBox").Text);
                    Assert.Equal("00:00:05.200", RequireControl<TextBlock>(window, "ComparePaneCurrentPositionTextBlock").Text);
                    Assert.Equal("157", RequireControl<TextBox>(window, "ComparePaneFrameNumberTextBox").Text);
                    Assert.False(GetPrivateField<bool>(window, "_hasPendingSliderScrubTarget"));
                    Assert.False(GetPrivateField<bool>(window, "_hasPendingPaneSliderScrubTarget"));
                    Assert.True(RequireControl<Control>(window, "PrimaryPanePlayPausePauseIcon").IsVisible);
                    Assert.True(RequireControl<Control>(window, "ComparePanePlayPausePlayIcon").IsVisible);

                    SetPrivateField(window, "_isSynchronizedFramePresentationActive", false);
                    InvokePrivate(window, "ApplyState", ParsePane("Primary"), stalePrimaryState);
                    InvokePrivate(window, "ApplyState", ParsePane("Compare"), staleCompareState);

                    Assert.Equal(2.2d, RequireControl<Slider>(window, "PositionSlider").Value);
                    Assert.Equal(2.2d, RequireControl<Slider>(window, "PrimaryPanePositionSlider").Value);
                    Assert.Equal(5.2d, RequireControl<Slider>(window, "ComparePanePositionSlider").Value);
                    Assert.Equal("00:00:02.200", RequireControl<TextBlock>(window, "PrimaryPaneCurrentPositionTextBlock").Text);
                    Assert.Equal("67", RequireControl<TextBox>(window, "PrimaryPaneFrameNumberTextBox").Text);
                    Assert.Equal("00:00:05.200", RequireControl<TextBlock>(window, "ComparePaneCurrentPositionTextBlock").Text);
                    Assert.Equal("157", RequireControl<TextBox>(window, "ComparePaneFrameNumberTextBox").Text);
                    Assert.False(GetPrivateField<bool>(window, "_hasPendingSliderScrubTarget"));
                    Assert.False(GetPrivateField<bool>(window, "_hasPendingPaneSliderScrubTarget"));

                    using var independentPrimaryFrame = CreateFrameBuffer(
                        8,
                        4,
                        TimeSpan.FromSeconds(7));
                    InvokePrivate(
                        window,
                        "PrimaryEngine_FramePresented",
                        null!,
                        new FramePresentedEventArgs(independentPrimaryFrame));
                    InvokePrivate(window, "PresentPendingFrame", ParsePane("Primary"));

                    Assert.Equal("00:00:07.000", RequireControl<TextBlock>(window, "PrimaryPaneCurrentPositionTextBlock").Text);
                    Assert.Equal("211", RequireControl<TextBox>(window, "PrimaryPaneFrameNumberTextBox").Text);
                    Assert.Equal("00:00:05.200", RequireControl<TextBlock>(window, "ComparePaneCurrentPositionTextBlock").Text);
                    Assert.Equal("157", RequireControl<TextBox>(window, "ComparePaneFrameNumberTextBox").Text);
                    Assert.Equal(2.2d, RequireControl<Slider>(window, "PositionSlider").Value);
                    Assert.Equal("00:00:02.200", RequireControl<TextBlock>(window, "CurrentPositionTextBlock").Text);
                    Assert.Equal("Frame 67", RequireControl<TextBlock>(window, "CurrentFrameTextBlock").Text);
                    Assert.Equal("00:00:02.200 / 00:00:10.000", RequireControl<TextBlock>(window, "TimecodeTextBlock").Text);
                    Assert.Equal("67", RequireControl<TextBox>(window, "FrameNumberTextBox").Text);
                }
                finally
                {
                    window.Close();
                }
            });
        }

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public void ComparePlayback_MasterReadoutTracksLongerPaneDuringSynchronization(
            bool compareIsLonger)
        {
            _fixture.Run(() =>
            {
                var window = new MainWindow();
                try
                {
                    var primaryDuration = TimeSpan.FromSeconds(
                        compareIsLonger ? 5 : 12);
                    var compareDuration = TimeSpan.FromSeconds(
                        compareIsLonger ? 12 : 5);
                    var primaryMediaInfo = CreateMediaInfo(
                        "left.mp4",
                        primaryDuration);
                    var compareMediaInfo = CreateMediaInfo(
                        "right.mp4",
                        compareDuration);
                    var primaryEngine = new TestVideoReviewEngine
                    {
                        IsMediaOpen = true,
                        CurrentFilePath = "left.mp4",
                        MediaInfo = primaryMediaInfo
                    };
                    var compareEngine = new TestVideoReviewEngine
                    {
                        IsMediaOpen = true,
                        CurrentFilePath = "right.mp4",
                        MediaInfo = compareMediaInfo
                    };
                    SetPrivateField(window, "_primaryEngine", primaryEngine);
                    SetPrivateField(window, "_compareEngine", compareEngine);
                    RequireControl<CheckBox>(
                        window,
                        "CompareModeCheckBox").IsChecked = true;

                    InvokePrivate(
                        window,
                        "ApplyState",
                        ParsePane("Primary"),
                        new VideoReviewEngineStateChangedEventArgs(
                            isMediaOpen: true,
                            isPlaying: false,
                            currentFilePath: "left.mp4",
                            lastErrorMessage: string.Empty,
                            mediaInfo: primaryMediaInfo,
                            position: ReviewPosition.Empty));
                    InvokePrivate(
                        window,
                        "ApplyState",
                        ParsePane("Compare"),
                        new VideoReviewEngineStateChangedEventArgs(
                            isMediaOpen: true,
                            isPlaying: false,
                            currentFilePath: "right.mp4",
                            lastErrorMessage: string.Empty,
                            mediaInfo: compareMediaInfo,
                            position: ReviewPosition.Empty));

                    using var primaryPairFrame = CreateFrameBuffer(
                        8,
                        4,
                        TimeSpan.FromSeconds(2.2));
                    using var comparePairFrame = CreateFrameBuffer(
                        8,
                        4,
                        TimeSpan.FromSeconds(4.2));
                    InvokePrivate(
                        window,
                        "ApplyPresentedFramePosition",
                        ParsePane("Primary"),
                        primaryPairFrame.Descriptor,
                        !compareIsLonger);
                    InvokePrivate(
                        window,
                        "ApplyPresentedFramePosition",
                        ParsePane("Compare"),
                        comparePairFrame.Descriptor,
                        compareIsLonger);

                    var expectedMasterPosition = compareIsLonger
                        ? TimeSpan.FromSeconds(4.2)
                        : TimeSpan.FromSeconds(2.2);
                    var expectedDuration = compareIsLonger
                        ? compareDuration
                        : primaryDuration;
                    var expectedMasterFrame = compareIsLonger
                        ? "127"
                        : "67";
                    Assert.Equal(
                        expectedDuration.TotalSeconds,
                        RequireControl<Slider>(
                            window,
                            "PositionSlider").Maximum);
                    Assert.Equal(
                        expectedMasterPosition.TotalSeconds,
                        RequireControl<Slider>(
                            window,
                            "PositionSlider").Value);
                    Assert.Equal(
                        FormatExpectedTime(expectedMasterPosition),
                        RequireControl<TextBlock>(
                            window,
                            "CurrentPositionTextBlock").Text);
                    Assert.Equal(
                        FormatExpectedTime(expectedMasterPosition) +
                            " / " +
                            FormatExpectedTime(expectedDuration),
                        RequireControl<TextBlock>(
                            window,
                            "TimecodeTextBlock").Text);
                    Assert.Equal(
                        "Frame " + expectedMasterFrame,
                        RequireControl<TextBlock>(
                            window,
                            "CurrentFrameTextBlock").Text);
                    Assert.Equal(
                        expectedMasterFrame,
                        RequireControl<TextBox>(
                            window,
                            "FrameNumberTextBox").Text);
                    Assert.Equal(
                        FormatExpectedTime(expectedDuration),
                        RequireControl<TextBlock>(
                            window,
                            "DurationTextBlock").Text);
                    Assert.Equal(
                        TimeSpan.FromSeconds(2.2).TotalSeconds,
                        RequireControl<Slider>(
                            window,
                            "PrimaryPanePositionSlider").Value);
                    Assert.Equal(
                        TimeSpan.FromSeconds(4.2).TotalSeconds,
                        RequireControl<Slider>(
                            window,
                            "ComparePanePositionSlider").Value);
                }
                finally
                {
                    window.Close();
                }
            });
        }

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public void ComparePlayback_MasterReadoutTracksLongerPaneAfterShortPaneEnds(
            bool compareIsLonger)
        {
            _fixture.Run(() =>
            {
                var window = new MainWindow();
                try
                {
                    var primaryDuration = TimeSpan.FromSeconds(
                        compareIsLonger ? 4 : 12);
                    var compareDuration = TimeSpan.FromSeconds(
                        compareIsLonger ? 12 : 4);
                    var primaryMediaInfo = CreateMediaInfo(
                        "left.mp4",
                        primaryDuration);
                    var compareMediaInfo = CreateMediaInfo(
                        "right.mp4",
                        compareDuration);
                    var primaryEngine = new TestVideoReviewEngine
                    {
                        IsMediaOpen = true,
                        IsPlaying = !compareIsLonger,
                        CurrentFilePath = "left.mp4",
                        MediaInfo = primaryMediaInfo
                    };
                    var compareEngine = new TestVideoReviewEngine
                    {
                        IsMediaOpen = true,
                        IsPlaying = compareIsLonger,
                        CurrentFilePath = "right.mp4",
                        MediaInfo = compareMediaInfo
                    };
                    SetPrivateField(window, "_primaryEngine", primaryEngine);
                    SetPrivateField(window, "_compareEngine", compareEngine);
                    RequireControl<CheckBox>(
                        window,
                        "CompareModeCheckBox").IsChecked = true;

                    InvokePrivate(
                        window,
                        "ApplyState",
                        ParsePane("Primary"),
                        new VideoReviewEngineStateChangedEventArgs(
                            isMediaOpen: true,
                            isPlaying: primaryEngine.IsPlaying,
                            currentFilePath: "left.mp4",
                            lastErrorMessage: string.Empty,
                            mediaInfo: primaryMediaInfo,
                            position: ReviewPosition.Empty));
                    InvokePrivate(
                        window,
                        "ApplyState",
                        ParsePane("Compare"),
                        new VideoReviewEngineStateChangedEventArgs(
                            isMediaOpen: true,
                            isPlaying: compareEngine.IsPlaying,
                            currentFilePath: "right.mp4",
                            lastErrorMessage: string.Empty,
                            mediaInfo: compareMediaInfo,
                            position: ReviewPosition.Empty));

                    var transportIntentGeneration = (int)InvokePrivate(
                        window,
                        "BeginAllPaneTransportIntent");
                    Assert.True((bool)InvokePrivate(
                        window,
                        "TryBeginSynchronizedFramePresentation",
                        transportIntentGeneration));
                    SetPrivateField(
                        window,
                        "_isAllPanePlaybackControlActive",
                        true);

                    var shorterPane = ParsePane(
                        compareIsLonger ? "Primary" : "Compare");
                    var longerPane = ParsePane(
                        compareIsLonger ? "Compare" : "Primary");
                    var shorterEngine = compareIsLonger
                        ? primaryEngine
                        : compareEngine;
                    var shorterMediaInfo = compareIsLonger
                        ? primaryMediaInfo
                        : compareMediaInfo;
                    var longerMediaInfo = compareIsLonger
                        ? compareMediaInfo
                        : primaryMediaInfo;
                    var shortEndState = new VideoReviewEngineStateChangedEventArgs(
                        isMediaOpen: true,
                        isPlaying: false,
                        currentFilePath: compareIsLonger ? "left.mp4" : "right.mp4",
                        lastErrorMessage: string.Empty,
                        mediaInfo: shorterMediaInfo,
                        position: new ReviewPosition(
                            shorterMediaInfo.Duration,
                            119,
                            isFrameAccurate: true,
                            isFrameIndexAbsolute: true,
                            presentationTimestamp: 360_000,
                            decodeTimestamp: 360_000));

                    InvokePrivate(
                        window,
                        "ReleaseSynchronizedPresentationIfPaneStopped",
                        shorterPane,
                        shorterEngine,
                        shortEndState);
                    Assert.True(
                        SpinWait.SpinUntil(
                            () => !GetPrivateField<bool>(
                                window,
                                "_isSynchronizedFramePresentationActive"),
                            TimeSpan.FromSeconds(2)),
                        "The short pane ending did not release paired presentation.");

                    var longerPosition = TimeSpan.FromSeconds(6.4);
                    InvokePrivate(
                        window,
                        "ApplyState",
                        longerPane,
                        new VideoReviewEngineStateChangedEventArgs(
                            isMediaOpen: true,
                            isPlaying: true,
                            currentFilePath: compareIsLonger ? "right.mp4" : "left.mp4",
                            lastErrorMessage: string.Empty,
                            mediaInfo: longerMediaInfo,
                            position: new ReviewPosition(
                                longerPosition,
                                191,
                                isFrameAccurate: true,
                                isFrameIndexAbsolute: true,
                                presentationTimestamp: 576_000,
                                decodeTimestamp: 576_000)));

                    InvokePrivate(
                        window,
                        "ApplyState",
                        shorterPane,
                        new VideoReviewEngineStateChangedEventArgs(
                            isMediaOpen: true,
                            isPlaying: false,
                            currentFilePath: compareIsLonger ? "left.mp4" : "right.mp4",
                            lastErrorMessage: string.Empty,
                            mediaInfo: shorterMediaInfo,
                            position: new ReviewPosition(
                                TimeSpan.FromSeconds(0.2),
                                5,
                                isFrameAccurate: true,
                                isFrameIndexAbsolute: true,
                                presentationTimestamp: 18_000,
                                decodeTimestamp: 18_000)));

                    Assert.Equal(
                        longerMediaInfo.Duration.TotalSeconds,
                        RequireControl<Slider>(
                            window,
                            "PositionSlider").Maximum);
                    Assert.Equal(
                        longerPosition.TotalSeconds,
                        RequireControl<Slider>(
                            window,
                            "PositionSlider").Value);
                    Assert.Equal(
                        FormatExpectedTime(longerPosition),
                        RequireControl<TextBlock>(
                            window,
                            "CurrentPositionTextBlock").Text);
                    Assert.Equal(
                        FormatExpectedTime(longerPosition) +
                            " / " +
                            FormatExpectedTime(longerMediaInfo.Duration),
                        RequireControl<TextBlock>(
                            window,
                            "TimecodeTextBlock").Text);
                    Assert.Equal(
                        "Frame 192",
                        RequireControl<TextBlock>(
                            window,
                            "CurrentFrameTextBlock").Text);
                    Assert.Equal(
                        "192",
                        RequireControl<TextBox>(
                            window,
                            "FrameNumberTextBox").Text);
                }
                finally
                {
                    window.Close();
                }
            });
        }

        [Fact]
        public async Task ComparePlayback_SynchronizedPresentationPreservesPausedPaneOffset()
        {
            await _fixture.RunAsync(async () =>
            {
                var window = new MainWindow();
                try
                {
                    var frameStep = TimeSpan.FromSeconds(1d / 30d);
                    var mediaInfo = new VideoMediaInfo(
                        "offset.mp4",
                        TimeSpan.FromSeconds(10),
                        frameStep,
                        30d,
                        1920,
                        1080,
                        "h264",
                        0,
                        30,
                        1,
                        1,
                        90_000);
                    var primaryEngine = new TestVideoReviewEngine
                    {
                        IsMediaOpen = true,
                        CurrentFilePath = "boundary-step-primary.mp4",
                        MediaInfo = mediaInfo,
                        Position = new ReviewPosition(
                            TimeSpan.FromSeconds(2),
                            null,
                            false,
                            false,
                            null,
                            null)
                    };
                    var compareEngine = new TestVideoReviewEngine
                    {
                        IsMediaOpen = true,
                        CurrentFilePath = "boundary-step-compare.mp4",
                        MediaInfo = mediaInfo,
                        Position = new ReviewPosition(
                            TimeSpan.FromSeconds(5),
                            null,
                            false,
                            false,
                            null,
                            null)
                    };
                    SetPrivateField(window, "_primaryEngine", primaryEngine);
                    SetPrivateField(window, "_compareEngine", compareEngine);
                    RequireControl<CheckBox>(window, "CompareModeCheckBox").IsChecked = true;
                    await InvokePrivateTask(window, "StartAllPanePlaybackAsync", Type.EmptyTypes);

                    Assert.True(GetPrivateField<bool>(
                        window,
                        "_isSynchronizedFramePresentationActive"));
                    Assert.Equal(
                        TimeSpan.FromSeconds(-3),
                        GetPrivateField<TimeSpan>(
                            window,
                            "_synchronizedFramePresentationTimeOffset"));
                    Assert.Equal(0, primaryEngine.SeekToTimeCallCount);
                    Assert.Equal(0, compareEngine.SeekToTimeCallCount);
                    Assert.Equal(0, primaryEngine.SeekToFrameCallCount);
                    Assert.Equal(0, compareEngine.SeekToFrameCallCount);

                    using var firstPrimary = CreateFrameBuffer(
                        8,
                        4,
                        TimeSpan.FromSeconds(2.2));
                    using var firstCompare = CreateFrameBuffer(
                        8,
                        4,
                        TimeSpan.FromSeconds(5.2));
                    InvokePrivate(
                        window,
                        "PrimaryEngine_FramePresented",
                        null!,
                        new FramePresentedEventArgs(firstPrimary));
                    InvokePrivate(
                        window,
                        "CompareEngine_FramePresented",
                        null!,
                        new FramePresentedEventArgs(firstCompare));
                    InvokePrivate(
                        window,
                        "PresentPendingSynchronizedFrames",
                        GetPrivateField<long>(
                            window,
                            "_synchronizedFramePresentationGeneration"));

                    Assert.Equal(
                        TimeSpan.FromSeconds(2.2),
                        GetPrivateField<DecodedFrameBuffer>(
                            window,
                            "_primaryFrameBuffer")!.Descriptor.PresentationTime);
                    Assert.Equal(
                        TimeSpan.FromSeconds(5.2),
                        GetPrivateField<DecodedFrameBuffer>(
                            window,
                            "_compareFrameBuffer")!.Descriptor.PresentationTime);

                    primaryEngine.Position = new ReviewPosition(
                        TimeSpan.FromSeconds(9),
                        270,
                        true,
                        true,
                        810_000,
                        810_000);
                    compareEngine.Position = new ReviewPosition(
                        TimeSpan.FromSeconds(9),
                        270,
                        true,
                        true,
                        810_000,
                        810_000);
                    await InvokePrivateTask(
                        window,
                        "SeekAllPaneRelativePreservingPlaybackAsync",
                        new[] { typeof(TimeSpan) },
                        TimeSpan.FromSeconds(1));

                    Assert.Equal(
                        TimeSpan.FromSeconds(3.2),
                        primaryEngine.Position.PresentationTime);
                    Assert.Equal(
                        TimeSpan.FromSeconds(6.2),
                        compareEngine.Position.PresentationTime);
                    Assert.Equal(
                        TimeSpan.FromSeconds(-3),
                        GetPrivateField<TimeSpan>(
                            window,
                            "_synchronizedFramePresentationTimeOffset"));
                    Assert.True(GetPrivateField<bool>(
                        window,
                        "_isSynchronizedFramePresentationActive"));

                    using var secondPrimary = CreateFrameBuffer(
                        8,
                        4,
                        TimeSpan.FromSeconds(3.2));
                    using var secondCompare = CreateFrameBuffer(
                        8,
                        4,
                        TimeSpan.FromSeconds(6.2));
                    InvokePrivate(
                        window,
                        "PrimaryEngine_FramePresented",
                        null!,
                        new FramePresentedEventArgs(secondPrimary));
                    InvokePrivate(
                        window,
                        "CompareEngine_FramePresented",
                        null!,
                        new FramePresentedEventArgs(secondCompare));
                    InvokePrivate(
                        window,
                        "PresentPendingSynchronizedFrames",
                        GetPrivateField<long>(
                            window,
                            "_synchronizedFramePresentationGeneration"));

                    Assert.Equal(
                        TimeSpan.FromSeconds(3.2),
                        GetPrivateField<DecodedFrameBuffer>(
                            window,
                            "_primaryFrameBuffer")!.Descriptor.PresentationTime);
                    Assert.Equal(
                        TimeSpan.FromSeconds(6.2),
                        GetPrivateField<DecodedFrameBuffer>(
                            window,
                            "_compareFrameBuffer")!.Descriptor.PresentationTime);

                    await InvokePrivateTask(
                        window,
                        "PausePlaybackAsync",
                        new[] { typeof(bool), typeof(SynchronizedOperationScope?) },
                        true,
                        (SynchronizedOperationScope?)SynchronizedOperationScope.AllPanes);
                    await InvokePrivateTask(
                        window,
                        "SeekAllPaneToTimesPreservingPlaybackAsync",
                        new[] { typeof(TimeSpan), typeof(TimeSpan), typeof(CancellationToken) },
                        TimeSpan.FromSeconds(4),
                        TimeSpan.FromSeconds(4),
                        CancellationToken.None);

                    Assert.Equal(
                        TimeSpan.Zero,
                        GetPrivateField<TimeSpan>(
                            window,
                            "_synchronizedFramePresentationTimeOffset"));
                    using var equalTimePrimary = CreateFrameBuffer(
                        8,
                        4,
                        TimeSpan.FromSeconds(4));
                    using var equalTimeCompare = CreateFrameBuffer(
                        8,
                        4,
                        TimeSpan.FromSeconds(4));
                    InvokePrivate(
                        window,
                        "PrimaryEngine_FramePresented",
                        null!,
                        new FramePresentedEventArgs(equalTimePrimary));
                    InvokePrivate(
                        window,
                        "CompareEngine_FramePresented",
                        null!,
                        new FramePresentedEventArgs(equalTimeCompare));
                    InvokePrivate(
                        window,
                        "PresentPendingSynchronizedFrames",
                        GetPrivateField<long>(
                            window,
                            "_synchronizedFramePresentationGeneration"));

                    Assert.Equal(
                        TimeSpan.FromSeconds(4),
                        GetPrivateField<DecodedFrameBuffer>(
                            window,
                            "_primaryFrameBuffer")!.Descriptor.PresentationTime);
                    Assert.Equal(
                        TimeSpan.FromSeconds(4),
                        GetPrivateField<DecodedFrameBuffer>(
                            window,
                            "_compareFrameBuffer")!.Descriptor.PresentationTime);

                    primaryEngine.Position = new ReviewPosition(
                        TimeSpan.FromSeconds(1),
                        null,
                        false,
                        false,
                        null,
                        null);
                    compareEngine.Position = new ReviewPosition(
                        TimeSpan.FromSeconds(5),
                        null,
                        false,
                        false,
                        null,
                        null);
                    await InvokePrivateTask(
                        window,
                        "SeekAllPaneToFramePreservingPlaybackAsync",
                        new[] { typeof(long) },
                        90L);

                    Assert.False(GetPrivateField<bool>(
                        window,
                        "_captureSynchronizedFramePresentationTimeOffset"));
                    using var sameFramePrimary = CreateFrameBuffer(
                        8,
                        4,
                        TimeSpan.FromSeconds(3));
                    using var sameFrameCompare = CreateFrameBuffer(
                        8,
                        4,
                        TimeSpan.FromSeconds(5));
                    InvokePrivate(
                        window,
                        "PrimaryEngine_FramePresented",
                        null!,
                        new FramePresentedEventArgs(sameFramePrimary));
                    InvokePrivate(
                        window,
                        "CompareEngine_FramePresented",
                        null!,
                        new FramePresentedEventArgs(sameFrameCompare));
                    InvokePrivate(
                        window,
                        "PresentPendingSynchronizedFrames",
                        GetPrivateField<long>(
                            window,
                            "_synchronizedFramePresentationGeneration"));

                    Assert.Equal(
                        TimeSpan.Zero,
                        GetPrivateField<TimeSpan>(
                            window,
                            "_synchronizedFramePresentationTimeOffset"));
                    Assert.Equal(
                        TimeSpan.FromSeconds(4),
                        GetPrivateField<DecodedFrameBuffer>(
                            window,
                            "_primaryFrameBuffer")!.Descriptor.PresentationTime);
                    Assert.Equal(
                        TimeSpan.FromSeconds(4),
                        GetPrivateField<DecodedFrameBuffer>(
                            window,
                            "_compareFrameBuffer")!.Descriptor.PresentationTime);

                    using var catchUpPrimaryFrame = CreateFrameBuffer(
                        8,
                        4,
                        TimeSpan.FromSeconds(5));
                    InvokePrivate(
                        window,
                        "PrimaryEngine_FramePresented",
                        null!,
                        new FramePresentedEventArgs(catchUpPrimaryFrame));
                    InvokePrivate(
                        window,
                        "PresentPendingSynchronizedFrames",
                        GetPrivateField<long>(
                            window,
                            "_synchronizedFramePresentationGeneration"));

                    Assert.Equal(
                        TimeSpan.FromSeconds(5),
                        GetPrivateField<DecodedFrameBuffer>(
                            window,
                            "_primaryFrameBuffer")!.Descriptor.PresentationTime);
                    Assert.Equal(
                        TimeSpan.FromSeconds(5),
                        GetPrivateField<DecodedFrameBuffer>(
                            window,
                            "_compareFrameBuffer")!.Descriptor.PresentationTime);
                }
                finally
                {
                    window.Close();
                }
            });
        }

        [Theory]
        [InlineData("Primary", "Compare")]
        [InlineData("Compare", "Primary")]
        public async Task CompareSync_WhilePlayingUsesPresentedSourceFrameAndResumesBothPanes(
            string sourcePaneName,
            string targetPaneName)
        {
            await _fixture.RunAsync(async () =>
            {
                var window = new MainWindow();
                try
                {
                    var mediaInfo = new VideoMediaInfo(
                        "sync.mp4",
                        TimeSpan.FromSeconds(20),
                        TimeSpan.FromSeconds(1d / 30d),
                        30d,
                        1920,
                        1080,
                        "h264",
                        0,
                        30,
                        1,
                        1,
                        90_000);
                    var primaryIsSource = sourcePaneName == "Primary";
                    var primaryEngine = new TestVideoReviewEngine
                    {
                        IsMediaOpen = true,
                        IsPlaying = true,
                        CurrentFilePath = "sync.mp4",
                        MediaInfo = mediaInfo,
                        Position = new ReviewPosition(
                            primaryIsSource ? TimeSpan.FromSeconds(10) : TimeSpan.FromSeconds(11),
                            primaryIsSource ? 300 : 330,
                            true,
                            true,
                            null,
                            null),
                        StopPlayingOnSeek = true
                    };
                    var compareEngine = new TestVideoReviewEngine
                    {
                        IsMediaOpen = true,
                        IsPlaying = true,
                        CurrentFilePath = "sync.mp4",
                        MediaInfo = mediaInfo,
                        Position = new ReviewPosition(
                            primaryIsSource ? TimeSpan.FromSeconds(11) : TimeSpan.FromSeconds(10),
                            primaryIsSource ? 330 : 300,
                            true,
                            true,
                            null,
                            null),
                        StopPlayingOnSeek = true
                    };
                    SetPrivateField(window, "_primaryEngine", primaryEngine);
                    SetPrivateField(window, "_compareEngine", compareEngine);
                    RequireControl<CheckBox>(window, "CompareModeCheckBox").IsChecked = true;
                    SetPrivateField(window, "_isCompareModeSelected", true);
                    SetPrivateField(window, "_isAllPaneTransportSelected", true);

                    var sourceTime = TimeSpan.FromSeconds(4);
                    var targetTime = TimeSpan.FromSeconds(3);
                    using var initialPrimary = CreateFrameBuffer(
                        8,
                        4,
                        primaryIsSource ? sourceTime : targetTime);
                    using var initialCompare = CreateFrameBuffer(
                        8,
                        4,
                        primaryIsSource ? targetTime : sourceTime);
                    InvokePrivate(window, "SetPaneBitmap", ParsePane("Primary"), initialPrimary);
                    InvokePrivate(window, "SetPaneBitmap", ParsePane("Compare"), initialCompare);

                    primaryEngine.FrameSought = frameIndex =>
                    {
                        using var frame = CreateFrameBuffer(
                            8,
                            4,
                            TimeSpan.FromSeconds(frameIndex / 30d));
                        InvokePrivate(
                            window,
                            "PrimaryEngine_FramePresented",
                            null!,
                            new FramePresentedEventArgs(frame));
                    };
                    compareEngine.FrameSought = frameIndex =>
                    {
                        using var frame = CreateFrameBuffer(
                            8,
                            4,
                            TimeSpan.FromSeconds(frameIndex / 30d));
                        InvokePrivate(
                            window,
                            "CompareEngine_FramePresented",
                            null!,
                            new FramePresentedEventArgs(frame));
                    };

                    var result = await (Task<bool>)InvokePrivate(
                        window,
                        "AlignPaneToPaneAsync",
                        ParsePane(sourcePaneName),
                        ParsePane(targetPaneName));

                    Assert.True(result);
                    Assert.Equal(120L, primaryEngine.Position.FrameIndex);
                    Assert.Equal(120L, compareEngine.Position.FrameIndex);
                    Assert.Equal(1, primaryEngine.PauseCallCount);
                    Assert.Equal(1, compareEngine.PauseCallCount);
                    Assert.Equal(1, primaryEngine.SeekToFrameCallCount);
                    Assert.Equal(1, compareEngine.SeekToFrameCallCount);
                    Assert.Equal(1, primaryEngine.PlayCallCount);
                    Assert.Equal(1, compareEngine.PlayCallCount);
                    Assert.True(primaryEngine.IsPlaying);
                    Assert.True(compareEngine.IsPlaying);
                    Assert.True(GetPrivateField<bool>(
                        window,
                        "_isSynchronizedFramePresentationActive"));
                    Assert.Equal(
                        120L,
                        GetPrivateField<DecodedFrameBuffer>(
                            window,
                            "_primaryFrameBuffer")!.Descriptor.FrameIndex);
                    Assert.Equal(
                        120L,
                        GetPrivateField<DecodedFrameBuffer>(
                            window,
                            "_compareFrameBuffer")!.Descriptor.FrameIndex);
                    Assert.Equal(
                        GetPrivateField<DecodedFrameBuffer>(
                            window,
                            "_primaryFrameBuffer")!.Descriptor.PresentationTime,
                        GetPrivateField<DecodedFrameBuffer>(
                            window,
                            "_compareFrameBuffer")!.Descriptor.PresentationTime);
                    Assert.Equal(
                        -1,
                        GetPrivateField<int>(
                            window,
                            "_primaryPendingSeekResumeGeneration"));
                    Assert.Equal(
                        -1,
                        GetPrivateField<int>(
                            window,
                            "_comparePendingSeekResumeGeneration"));
                }
                finally
                {
                    window.Close();
                }
            });
        }

        [Theory]
        [InlineData(
            "AlignRightToLeftButton",
            "Compare: synced right to left",
            120L)]
        [InlineData(
            "AlignLeftToRightButton",
            "Compare: synced left to right",
            180L)]
        public async Task CompareSync_ToolbarButtonsUseTheDocumentedReferencePane(
            string buttonName,
            string expectedStatus,
            long expectedFrameIndex)
        {
            await _fixture.RunAsync(async () =>
            {
                var window = new MainWindow();
                try
                {
                    var mediaInfo = new VideoMediaInfo(
                        "sync-toolbar.mp4",
                        TimeSpan.FromSeconds(20),
                        TimeSpan.FromSeconds(1d / 30d),
                        30d,
                        1920,
                        1080,
                        "h264",
                        0,
                        30,
                        1,
                        1,
                        90_000);
                    var primaryEngine = new TestVideoReviewEngine
                    {
                        IsMediaOpen = true,
                        CurrentFilePath = "sync-toolbar.mp4",
                        MediaInfo = mediaInfo
                    };
                    var compareEngine = new TestVideoReviewEngine
                    {
                        IsMediaOpen = true,
                        CurrentFilePath = "sync-toolbar.mp4",
                        MediaInfo = mediaInfo
                    };
                    SetPrivateField(window, "_primaryEngine", primaryEngine);
                    SetPrivateField(window, "_compareEngine", compareEngine);
                    RequireControl<CheckBox>(
                        window,
                        "CompareModeCheckBox").IsChecked = true;
                    SetPrivateField(window, "_isCompareModeSelected", true);

                    using var primaryFrame = CreateFrameBuffer(
                        8,
                        4,
                        TimeSpan.FromSeconds(4));
                    using var compareFrame = CreateFrameBuffer(
                        8,
                        4,
                        TimeSpan.FromSeconds(6));
                    InvokePrivate(
                        window,
                        "SetPaneBitmap",
                        ParsePane("Primary"),
                        primaryFrame);
                    InvokePrivate(
                        window,
                        "SetPaneBitmap",
                        ParsePane("Compare"),
                        compareFrame);
                    primaryEngine.FrameSought = frameIndex =>
                    {
                        using var frame = CreateFrameBuffer(
                            8,
                            4,
                            TimeSpan.FromSeconds(frameIndex / 30d));
                        InvokePrivate(
                            window,
                            "PrimaryEngine_FramePresented",
                            null!,
                            new FramePresentedEventArgs(frame));
                    };
                    compareEngine.FrameSought = frameIndex =>
                    {
                        using var frame = CreateFrameBuffer(
                            8,
                            4,
                            TimeSpan.FromSeconds(frameIndex / 30d));
                        InvokePrivate(
                            window,
                            "CompareEngine_FramePresented",
                            null!,
                            new FramePresentedEventArgs(frame));
                    };

                    RequireControl<Button>(window, buttonName).RaiseEvent(
                        new RoutedEventArgs(Button.ClickEvent));
                    var deadline =
                        DateTime.UtcNow + TimeSpan.FromSeconds(2);
                    while (RequireControl<TextBlock>(
                            window,
                            "CompareStatusTextBlock").Text != expectedStatus &&
                        DateTime.UtcNow < deadline)
                    {
                        await Task.Delay(TimeSpan.FromMilliseconds(10));
                    }

                    Assert.Equal(
                        expectedStatus,
                        RequireControl<TextBlock>(
                            window,
                            "CompareStatusTextBlock").Text);
                    Assert.Equal(
                        expectedFrameIndex,
                        GetPrivateField<DecodedFrameBuffer>(
                            window,
                            "_primaryFrameBuffer")!.Descriptor.FrameIndex);
                    Assert.Equal(
                        expectedFrameIndex,
                        GetPrivateField<DecodedFrameBuffer>(
                            window,
                            "_compareFrameBuffer")!.Descriptor.FrameIndex);
                }
                finally
                {
                    window.Close();
                }
            });
        }

        [Fact]
        public async Task CompareSync_ReplacementFramesAreCommittedOnlyAsAPair()
        {
            await _fixture.RunAsync(async () =>
            {
                var window = new MainWindow();
                var compareSeekCompletion = new TaskCompletionSource<bool>(
                    TaskCreationOptions.RunContinuationsAsynchronously);
                var primaryFrameQueued = new TaskCompletionSource<bool>(
                    TaskCreationOptions.RunContinuationsAsynchronously);
                try
                {
                    var mediaInfo = new VideoMediaInfo(
                        "sync-atomic.mp4",
                        TimeSpan.FromSeconds(20),
                        TimeSpan.FromSeconds(1d / 30d),
                        30d,
                        1920,
                        1080,
                        "h264",
                        0,
                        30,
                        1,
                        1,
                        90_000);
                    var primaryEngine = new TestVideoReviewEngine
                    {
                        IsMediaOpen = true,
                        IsPlaying = true,
                        CurrentFilePath = "sync-atomic.mp4",
                        MediaInfo = mediaInfo
                    };
                    var compareEngine = new TestVideoReviewEngine
                    {
                        IsMediaOpen = true,
                        IsPlaying = true,
                        CurrentFilePath = "sync-atomic.mp4",
                        MediaInfo = mediaInfo,
                        SeekFrameCompletion = compareSeekCompletion
                    };
                    SetPrivateField(window, "_primaryEngine", primaryEngine);
                    SetPrivateField(window, "_compareEngine", compareEngine);
                    RequireControl<CheckBox>(window, "CompareModeCheckBox").IsChecked = true;
                    SetPrivateField(window, "_isCompareModeSelected", true);

                    var target = TimeSpan.FromSeconds(4);
                    using var primaryFrame = CreateFrameBuffer(
                        8,
                        4,
                        target,
                        red: 0xC0,
                        green: 0x20,
                        blue: 0x20);
                    using var compareFrame = CreateFrameBuffer(
                        8,
                        4,
                        target,
                        red: 0x20,
                        green: 0x20,
                        blue: 0xC0);
                    InvokePrivate(window, "SetPaneBitmap", ParsePane("Primary"), primaryFrame);
                    InvokePrivate(window, "SetPaneBitmap", ParsePane("Compare"), compareFrame);
                    var originalPrimaryDescriptor = GetPrivateField<DecodedFrameBuffer>(
                        window,
                        "_primaryFrameBuffer")!.Descriptor;
                    var originalCompareDescriptor = GetPrivateField<DecodedFrameBuffer>(
                        window,
                        "_compareFrameBuffer")!.Descriptor;
                    primaryEngine.FrameSought = frameIndex =>
                    {
                        using var frame = CreateFrameBuffer(
                            8,
                            4,
                            TimeSpan.FromSeconds(frameIndex / 30d),
                            red: 0x20,
                            green: 0xC0,
                            blue: 0x20);
                        InvokePrivate(
                            window,
                            "PrimaryEngine_FramePresented",
                            null!,
                            new FramePresentedEventArgs(frame));
                        primaryFrameQueued.TrySetResult(true);
                    };
                    compareEngine.FrameSought = frameIndex =>
                    {
                        using var frame = CreateFrameBuffer(
                            8,
                            4,
                            TimeSpan.FromSeconds(frameIndex / 30d),
                            red: 0x20,
                            green: 0xC0,
                            blue: 0x20);
                        InvokePrivate(
                            window,
                            "CompareEngine_FramePresented",
                            null!,
                            new FramePresentedEventArgs(frame));
                    };

                    var alignmentTask = (Task<bool>)InvokePrivate(
                        window,
                        "AlignPaneToPaneAsync",
                        ParsePane("Primary"),
                        ParsePane("Compare"));
                    await primaryFrameQueued.Task.WaitAsync(
                        TimeSpan.FromSeconds(2));
                    await Dispatcher.UIThread.InvokeAsync(
                        () => { },
                        DispatcherPriority.Background);
                    Assert.True(
                        primaryEngine.SeekToFrameCallCount == 1 &&
                            compareEngine.SeekToFrameCallCount == 1,
                        "The alignment did not reach both engines.");

                    Assert.Same(
                        originalPrimaryDescriptor,
                        GetPrivateField<DecodedFrameBuffer>(
                            window,
                            "_primaryFrameBuffer")!.Descriptor);
                    Assert.Same(
                        originalCompareDescriptor,
                        GetPrivateField<DecodedFrameBuffer>(
                            window,
                            "_compareFrameBuffer")!.Descriptor);

                    compareSeekCompletion.TrySetResult(true);
                    Assert.True(await alignmentTask);
                    Assert.NotSame(
                        originalPrimaryDescriptor,
                        GetPrivateField<DecodedFrameBuffer>(
                            window,
                            "_primaryFrameBuffer")!.Descriptor);
                    Assert.NotSame(
                        originalCompareDescriptor,
                        GetPrivateField<DecodedFrameBuffer>(
                            window,
                            "_compareFrameBuffer")!.Descriptor);
                    Assert.Equal(
                        GetPrivateField<DecodedFrameBuffer>(
                            window,
                            "_primaryFrameBuffer")!.Descriptor.FrameIndex,
                        GetPrivateField<DecodedFrameBuffer>(
                            window,
                            "_compareFrameBuffer")!.Descriptor.FrameIndex);
                }
                finally
                {
                    compareSeekCompletion.TrySetResult(true);
                    window.Close();
                }
            });
        }

        [Fact]
        public async Task CompareSync_QueuedToolbarAlignmentCannotOverrideANewerSharedPause()
        {
            await _fixture.RunAsync(async () =>
            {
                var window = new MainWindow();
                var allPaneGate = GetPrivateField<SemaphoreSlim>(
                    window,
                    "_allPaneTransportOperationGate")
                    ?? throw new InvalidOperationException(
                        "Missing all-pane transport gate.");
                var gateHeld = false;
                try
                {
                    var mediaInfo = new VideoMediaInfo(
                        "sync-newer-pause.mp4",
                        TimeSpan.FromSeconds(20),
                        TimeSpan.FromSeconds(1d / 30d),
                        30d,
                        1920,
                        1080,
                        "h264",
                        0,
                        30,
                        1,
                        1,
                        90_000);
                    var primaryEngine = new TestVideoReviewEngine
                    {
                        IsMediaOpen = true,
                        IsPlaying = true,
                        CurrentFilePath = "sync-newer-pause.mp4",
                        MediaInfo = mediaInfo
                    };
                    var compareEngine = new TestVideoReviewEngine
                    {
                        IsMediaOpen = true,
                        IsPlaying = true,
                        CurrentFilePath = "sync-newer-pause.mp4",
                        MediaInfo = mediaInfo
                    };
                    SetPrivateField(window, "_primaryEngine", primaryEngine);
                    SetPrivateField(window, "_compareEngine", compareEngine);
                    RequireControl<CheckBox>(
                        window,
                        "CompareModeCheckBox").IsChecked = true;
                    SetPrivateField(window, "_isCompareModeSelected", true);

                    var target = TimeSpan.FromSeconds(4);
                    using var primaryFrame = CreateFrameBuffer(8, 4, target);
                    using var compareFrame = CreateFrameBuffer(8, 4, target);
                    InvokePrivate(
                        window,
                        "SetPaneBitmap",
                        ParsePane("Primary"),
                        primaryFrame);
                    InvokePrivate(
                        window,
                        "SetPaneBitmap",
                        ParsePane("Compare"),
                        compareFrame);
                    primaryEngine.FrameSought = frameIndex =>
                    {
                        using var frame = CreateFrameBuffer(
                            8,
                            4,
                            TimeSpan.FromSeconds(frameIndex / 30d));
                        InvokePrivate(
                            window,
                            "PrimaryEngine_FramePresented",
                            null!,
                            new FramePresentedEventArgs(frame));
                    };
                    compareEngine.FrameSought = frameIndex =>
                    {
                        using var frame = CreateFrameBuffer(
                            8,
                            4,
                            TimeSpan.FromSeconds(frameIndex / 30d));
                        InvokePrivate(
                            window,
                            "CompareEngine_FramePresented",
                            null!,
                            new FramePresentedEventArgs(frame));
                    };
                    var compareStatus = RequireControl<TextBlock>(
                        window,
                        "CompareStatusTextBlock");
                    compareStatus.Text = "Compare: awaiting command";

                    await allPaneGate.WaitAsync();
                    gateHeld = true;
                    var initialTransportIntentGeneration = GetPrivateField<int>(
                        window,
                        "_allPaneTransportIntentGeneration");
                    RequireControl<Button>(
                        window,
                        "AlignRightToLeftButton").RaiseEvent(
                        new RoutedEventArgs(Button.ClickEvent));
                    var alignmentIntentDeadline =
                        DateTime.UtcNow + TimeSpan.FromSeconds(2);
                    while (GetPrivateField<int>(
                               window,
                               "_allPaneTransportIntentGeneration") ==
                            initialTransportIntentGeneration &&
                        DateTime.UtcNow < alignmentIntentDeadline)
                    {
                        await Task.Delay(TimeSpan.FromMilliseconds(10));
                    }

                    Assert.NotEqual(
                        initialTransportIntentGeneration,
                        GetPrivateField<int>(
                            window,
                            "_allPaneTransportIntentGeneration"));
                    var pauseTask = InvokePrivateTask(
                        window,
                        "PausePlaybackAsync",
                        new[]
                        {
                            typeof(bool),
                            typeof(SynchronizedOperationScope?)
                        },
                        true,
                        (SynchronizedOperationScope?)
                            SynchronizedOperationScope.AllPanes);

                    allPaneGate.Release();
                    gateHeld = false;
                    await pauseTask;
                    await Task.Delay(TimeSpan.FromMilliseconds(50));

                    Assert.False(primaryEngine.IsPlaying);
                    Assert.False(compareEngine.IsPlaying);
                    Assert.Equal(0, primaryEngine.PlayCallCount);
                    Assert.Equal(0, compareEngine.PlayCallCount);
                    Assert.Equal(0, primaryEngine.SeekToFrameCallCount);
                    Assert.Equal(0, compareEngine.SeekToFrameCallCount);
                    Assert.Equal(
                        "Compare: awaiting command",
                        compareStatus.Text);
                    Assert.False(GetPrivateField<bool>(
                        window,
                        "_isSynchronizedFramePresentationActive"));
                }
                finally
                {
                    if (gateHeld)
                    {
                        allPaneGate.Release();
                    }

                    window.Close();
                }
            });
        }

        [Theory]
        [InlineData(false, false)]
        [InlineData(true, false)]
        [InlineData(false, true)]
        public async Task CompareSync_DoesNotResumeAOneSidedPlaybackState(
            bool primaryPlaying,
            bool comparePlaying)
        {
            await _fixture.RunAsync(async () =>
            {
                var window = new MainWindow();
                try
                {
                    var mediaInfo = new VideoMediaInfo(
                        "sync-paused.mp4",
                        TimeSpan.FromSeconds(20),
                        TimeSpan.FromSeconds(1d / 30d),
                        30d,
                        1920,
                        1080,
                        "h264",
                        0,
                        30,
                        1,
                        1,
                        90_000);
                    var primaryEngine = new TestVideoReviewEngine
                    {
                        IsMediaOpen = true,
                        IsPlaying = primaryPlaying,
                        CurrentFilePath = "sync-paused.mp4",
                        MediaInfo = mediaInfo
                    };
                    var compareEngine = new TestVideoReviewEngine
                    {
                        IsMediaOpen = true,
                        IsPlaying = comparePlaying,
                        CurrentFilePath = "sync-paused.mp4",
                        MediaInfo = mediaInfo
                    };
                    SetPrivateField(window, "_primaryEngine", primaryEngine);
                    SetPrivateField(window, "_compareEngine", compareEngine);
                    RequireControl<CheckBox>(window, "CompareModeCheckBox").IsChecked = true;
                    SetPrivateField(window, "_isCompareModeSelected", true);

                    var target = TimeSpan.FromSeconds(4);
                    using var primaryFrame = CreateFrameBuffer(8, 4, target);
                    using var compareFrame = CreateFrameBuffer(8, 4, target);
                    InvokePrivate(window, "SetPaneBitmap", ParsePane("Primary"), primaryFrame);
                    InvokePrivate(window, "SetPaneBitmap", ParsePane("Compare"), compareFrame);
                    primaryEngine.FrameSought = frameIndex =>
                    {
                        using var frame = CreateFrameBuffer(
                            8,
                            4,
                            TimeSpan.FromSeconds(frameIndex / 30d));
                        InvokePrivate(
                            window,
                            "PrimaryEngine_FramePresented",
                            null!,
                            new FramePresentedEventArgs(frame));
                    };
                    compareEngine.FrameSought = frameIndex =>
                    {
                        using var frame = CreateFrameBuffer(
                            8,
                            4,
                            TimeSpan.FromSeconds(frameIndex / 30d));
                        InvokePrivate(
                            window,
                            "CompareEngine_FramePresented",
                            null!,
                            new FramePresentedEventArgs(frame));
                    };

                    var result = await (Task<bool>)InvokePrivate(
                        window,
                        "AlignPaneToPaneAsync",
                        ParsePane("Primary"),
                        ParsePane("Compare"));

                    Assert.True(result);
                    Assert.False(primaryEngine.IsPlaying);
                    Assert.False(compareEngine.IsPlaying);
                    Assert.Equal(0, primaryEngine.PlayCallCount);
                    Assert.Equal(0, compareEngine.PlayCallCount);
                }
                finally
                {
                    window.Close();
                }
            });
        }

        [Fact]
        public async Task CompareSync_PartialResumeFailureFailsClosedWithBothPanesPaused()
        {
            await _fixture.RunAsync(async () =>
            {
                var window = new MainWindow();
                var comparePlayCompletion = new TaskCompletionSource<bool>(
                    TaskCreationOptions.RunContinuationsAsynchronously);
                try
                {
                    var mediaInfo = new VideoMediaInfo(
                        "sync-resume-failure.mp4",
                        TimeSpan.FromSeconds(20),
                        TimeSpan.FromSeconds(1d / 30d),
                        30d,
                        1920,
                        1080,
                        "h264",
                        0,
                        30,
                        1,
                        1,
                        90_000);
                    var primaryEngine = new TestVideoReviewEngine
                    {
                        IsMediaOpen = true,
                        IsPlaying = true,
                        CurrentFilePath = "sync-resume-failure.mp4",
                        MediaInfo = mediaInfo
                    };
                    var compareEngine = new TestVideoReviewEngine
                    {
                        IsMediaOpen = true,
                        IsPlaying = true,
                        CurrentFilePath = "sync-resume-failure.mp4",
                        MediaInfo = mediaInfo,
                        PlayCompletion = comparePlayCompletion
                    };
                    SetPrivateField(window, "_primaryEngine", primaryEngine);
                    SetPrivateField(window, "_compareEngine", compareEngine);
                    RequireControl<CheckBox>(window, "CompareModeCheckBox").IsChecked = true;
                    SetPrivateField(window, "_isCompareModeSelected", true);

                    var target = TimeSpan.FromSeconds(4);
                    using var primaryFrame = CreateFrameBuffer(8, 4, target);
                    using var compareFrame = CreateFrameBuffer(8, 4, target);
                    InvokePrivate(window, "SetPaneBitmap", ParsePane("Primary"), primaryFrame);
                    InvokePrivate(window, "SetPaneBitmap", ParsePane("Compare"), compareFrame);
                    primaryEngine.FrameSought = frameIndex =>
                    {
                        using var frame = CreateFrameBuffer(
                            8,
                            4,
                            TimeSpan.FromSeconds(frameIndex / 30d));
                        InvokePrivate(
                            window,
                            "PrimaryEngine_FramePresented",
                            null!,
                            new FramePresentedEventArgs(frame));
                    };
                    compareEngine.FrameSought = frameIndex =>
                    {
                        using var frame = CreateFrameBuffer(
                            8,
                            4,
                            TimeSpan.FromSeconds(frameIndex / 30d));
                        InvokePrivate(
                            window,
                            "CompareEngine_FramePresented",
                            null!,
                            new FramePresentedEventArgs(frame));
                    };
                    comparePlayCompletion.TrySetException(
                        new InvalidOperationException("Expected compare resume failure."));

                    var result = await (Task<bool>)InvokePrivate(
                        window,
                        "AlignPaneToPaneAsync",
                        ParsePane("Primary"),
                        ParsePane("Compare"));

                    Assert.False(result);
                    Assert.False(primaryEngine.IsPlaying);
                    Assert.False(compareEngine.IsPlaying);
                    Assert.Equal(1, primaryEngine.PlayCallCount);
                    Assert.Equal(1, compareEngine.PlayCallCount);
                    Assert.Equal(2, primaryEngine.PauseCallCount);
                    Assert.Equal(2, compareEngine.PauseCallCount);
                    Assert.False(GetPrivateField<bool>(
                        window,
                        "_isSynchronizedFramePresentationActive"));
                }
                finally
                {
                    comparePlayCompletion.TrySetResult(true);
                    window.Close();
                }
            });
        }

        [Fact]
        public async Task CompareSync_SeekFailureLeavesBothPanesPausedAndOldPairVisible()
        {
            await _fixture.RunAsync(async () =>
            {
                var window = new MainWindow();
                var compareSeekCompletion = new TaskCompletionSource<bool>(
                    TaskCreationOptions.RunContinuationsAsynchronously);
                try
                {
                    var mediaInfo = new VideoMediaInfo(
                        "sync-failure.mp4",
                        TimeSpan.FromSeconds(20),
                        TimeSpan.FromSeconds(1d / 30d),
                        30d,
                        1920,
                        1080,
                        "h264",
                        0,
                        30,
                        1,
                        1,
                        90_000);
                    var primaryEngine = new TestVideoReviewEngine
                    {
                        IsMediaOpen = true,
                        IsPlaying = true,
                        CurrentFilePath = "sync-failure.mp4",
                        MediaInfo = mediaInfo
                    };
                    var compareEngine = new TestVideoReviewEngine
                    {
                        IsMediaOpen = true,
                        IsPlaying = true,
                        CurrentFilePath = "sync-failure.mp4",
                        MediaInfo = mediaInfo,
                        SeekFrameCompletion = compareSeekCompletion
                    };
                    SetPrivateField(window, "_primaryEngine", primaryEngine);
                    SetPrivateField(window, "_compareEngine", compareEngine);
                    RequireControl<CheckBox>(window, "CompareModeCheckBox").IsChecked = true;
                    SetPrivateField(window, "_isCompareModeSelected", true);

                    var target = TimeSpan.FromSeconds(4);
                    using var primaryFrame = CreateFrameBuffer(8, 4, target);
                    using var compareFrame = CreateFrameBuffer(8, 4, target);
                    InvokePrivate(window, "SetPaneBitmap", ParsePane("Primary"), primaryFrame);
                    InvokePrivate(window, "SetPaneBitmap", ParsePane("Compare"), compareFrame);
                    var originalPrimaryDescriptor = GetPrivateField<DecodedFrameBuffer>(
                        window,
                        "_primaryFrameBuffer")!.Descriptor;
                    var originalCompareDescriptor = GetPrivateField<DecodedFrameBuffer>(
                        window,
                        "_compareFrameBuffer")!.Descriptor;
                    primaryEngine.FrameSought = frameIndex =>
                    {
                        using var frame = CreateFrameBuffer(
                            8,
                            4,
                            TimeSpan.FromSeconds(frameIndex / 30d));
                        InvokePrivate(
                            window,
                            "PrimaryEngine_FramePresented",
                            null!,
                            new FramePresentedEventArgs(frame));
                    };
                    compareSeekCompletion.TrySetException(
                        new InvalidOperationException("Expected compare seek failure."));

                    var result = await (Task<bool>)InvokePrivate(
                        window,
                        "AlignPaneToPaneAsync",
                        ParsePane("Primary"),
                        ParsePane("Compare"));

                    Assert.False(result);
                    Assert.False(primaryEngine.IsPlaying);
                    Assert.False(compareEngine.IsPlaying);
                    Assert.Equal(0, primaryEngine.PlayCallCount);
                    Assert.Equal(0, compareEngine.PlayCallCount);
                    Assert.Equal(1, primaryEngine.SeekToFrameCallCount);
                    Assert.Equal(1, compareEngine.SeekToFrameCallCount);
                    Assert.False(GetPrivateField<bool>(
                        window,
                        "_isSynchronizedFramePresentationActive"));
                    Assert.Same(
                        originalPrimaryDescriptor,
                        GetPrivateField<DecodedFrameBuffer>(
                            window,
                            "_primaryFrameBuffer")!.Descriptor);
                    Assert.Same(
                        originalCompareDescriptor,
                        GetPrivateField<DecodedFrameBuffer>(
                            window,
                            "_compareFrameBuffer")!.Descriptor);
                    Assert.Null(GetPrivateField<DecodedFrameBuffer>(
                        window,
                        "_pendingSynchronizedPrimaryFrame"));
                    Assert.Null(GetPrivateField<DecodedFrameBuffer>(
                        window,
                        "_pendingSynchronizedCompareFrame"));
                    Assert.Equal(
                        -1,
                        GetPrivateField<int>(
                            window,
                            "_primaryPendingSeekResumeGeneration"));
                    Assert.Equal(
                        -1,
                        GetPrivateField<int>(
                            window,
                            "_comparePendingSeekResumeGeneration"));
                }
                finally
                {
                    compareSeekCompletion.TrySetResult(true);
                    window.Close();
                }
            });
        }

        [Fact]
        public async Task CompareSync_DoesNotAcceptAnAlreadyVisiblePairWithoutNewSeekFrames()
        {
            await _fixture.RunAsync(async () =>
            {
                var window = new MainWindow();
                try
                {
                    var mediaInfo = new VideoMediaInfo(
                        "sync-no-new-frames.mp4",
                        TimeSpan.FromSeconds(20),
                        TimeSpan.FromSeconds(1d / 30d),
                        30d,
                        1920,
                        1080,
                        "h264",
                        0,
                        30,
                        1,
                        1,
                        90_000);
                    var primaryEngine = new TestVideoReviewEngine
                    {
                        IsMediaOpen = true,
                        IsPlaying = true,
                        CurrentFilePath = "sync-no-new-frames.mp4",
                        MediaInfo = mediaInfo
                    };
                    var compareEngine = new TestVideoReviewEngine
                    {
                        IsMediaOpen = true,
                        IsPlaying = true,
                        CurrentFilePath = "sync-no-new-frames.mp4",
                        MediaInfo = mediaInfo
                    };
                    SetPrivateField(window, "_primaryEngine", primaryEngine);
                    SetPrivateField(window, "_compareEngine", compareEngine);
                    RequireControl<CheckBox>(window, "CompareModeCheckBox").IsChecked = true;
                    SetPrivateField(window, "_isCompareModeSelected", true);

                    var target = TimeSpan.FromSeconds(4);
                    using var primaryFrame = CreateFrameBuffer(8, 4, target);
                    using var compareFrame = CreateFrameBuffer(8, 4, target);
                    InvokePrivate(window, "SetPaneBitmap", ParsePane("Primary"), primaryFrame);
                    InvokePrivate(window, "SetPaneBitmap", ParsePane("Compare"), compareFrame);
                    var originalPrimaryDescriptor = GetPrivateField<DecodedFrameBuffer>(
                        window,
                        "_primaryFrameBuffer")!.Descriptor;
                    var originalCompareDescriptor = GetPrivateField<DecodedFrameBuffer>(
                        window,
                        "_compareFrameBuffer")!.Descriptor;

                    var result = await (Task<bool>)InvokePrivate(
                        window,
                        "AlignPaneToPaneAsync",
                        ParsePane("Primary"),
                        ParsePane("Compare"));

                    Assert.False(result);
                    Assert.False(primaryEngine.IsPlaying);
                    Assert.False(compareEngine.IsPlaying);
                    Assert.Equal(0, primaryEngine.PlayCallCount);
                    Assert.Equal(0, compareEngine.PlayCallCount);
                    Assert.Equal(1, primaryEngine.SeekToFrameCallCount);
                    Assert.Equal(1, compareEngine.SeekToFrameCallCount);
                    Assert.Same(
                        originalPrimaryDescriptor,
                        GetPrivateField<DecodedFrameBuffer>(
                            window,
                            "_primaryFrameBuffer")!.Descriptor);
                    Assert.Same(
                        originalCompareDescriptor,
                        GetPrivateField<DecodedFrameBuffer>(
                            window,
                            "_compareFrameBuffer")!.Descriptor);
                }
                finally
                {
                    window.Close();
                }
            });
        }

        [Fact]
        public async Task CompareSync_RejectsMismatchedSeekPairBeforeEitherPaneChanges()
        {
            await _fixture.RunAsync(async () =>
            {
                var window = new MainWindow();
                try
                {
                    var mediaInfo = new VideoMediaInfo(
                        "sync-reject-mismatch.mp4",
                        TimeSpan.FromSeconds(20),
                        TimeSpan.FromSeconds(1d / 30d),
                        30d,
                        1920,
                        1080,
                        "h264",
                        0,
                        30,
                        1,
                        1,
                        90_000);
                    var primaryEngine = new TestVideoReviewEngine
                    {
                        IsMediaOpen = true,
                        IsPlaying = true,
                        CurrentFilePath = "sync-reject-mismatch.mp4",
                        MediaInfo = mediaInfo
                    };
                    var compareEngine = new TestVideoReviewEngine
                    {
                        IsMediaOpen = true,
                        IsPlaying = true,
                        CurrentFilePath = "sync-reject-mismatch.mp4",
                        MediaInfo = mediaInfo
                    };
                    SetPrivateField(window, "_primaryEngine", primaryEngine);
                    SetPrivateField(window, "_compareEngine", compareEngine);
                    RequireControl<CheckBox>(window, "CompareModeCheckBox").IsChecked = true;
                    SetPrivateField(window, "_isCompareModeSelected", true);

                    var target = TimeSpan.FromSeconds(4);
                    using var originalPrimary = CreateFrameBuffer(8, 4, target);
                    using var originalCompare = CreateFrameBuffer(
                        8,
                        4,
                        TimeSpan.FromSeconds(3));
                    InvokePrivate(window, "SetPaneBitmap", ParsePane("Primary"), originalPrimary);
                    InvokePrivate(window, "SetPaneBitmap", ParsePane("Compare"), originalCompare);
                    var originalPrimaryDescriptor = GetPrivateField<DecodedFrameBuffer>(
                        window,
                        "_primaryFrameBuffer")!.Descriptor;
                    var originalCompareDescriptor = GetPrivateField<DecodedFrameBuffer>(
                        window,
                        "_compareFrameBuffer")!.Descriptor;
                    primaryEngine.FrameSought = frameIndex =>
                    {
                        using var frame = CreateFrameBuffer(
                            8,
                            4,
                            TimeSpan.FromSeconds(frameIndex / 30d));
                        InvokePrivate(
                            window,
                            "PrimaryEngine_FramePresented",
                            null!,
                            new FramePresentedEventArgs(frame));
                    };
                    compareEngine.FrameSought = frameIndex =>
                    {
                        using var frame = CreateFrameBuffer(
                            8,
                            4,
                            TimeSpan.FromSeconds((frameIndex + 1L) / 30d));
                        InvokePrivate(
                            window,
                            "CompareEngine_FramePresented",
                            null!,
                            new FramePresentedEventArgs(frame));
                    };

                    var result = await (Task<bool>)InvokePrivate(
                        window,
                        "AlignPaneToPaneAsync",
                        ParsePane("Primary"),
                        ParsePane("Compare"));

                    Assert.False(result);
                    Assert.False(primaryEngine.IsPlaying);
                    Assert.False(compareEngine.IsPlaying);
                    Assert.Equal(1, primaryEngine.SeekToFrameCallCount);
                    Assert.Equal(1, compareEngine.SeekToFrameCallCount);
                    Assert.Same(
                        originalPrimaryDescriptor,
                        GetPrivateField<DecodedFrameBuffer>(
                            window,
                            "_primaryFrameBuffer")!.Descriptor);
                    Assert.Same(
                        originalCompareDescriptor,
                        GetPrivateField<DecodedFrameBuffer>(
                            window,
                            "_compareFrameBuffer")!.Descriptor);
                    Assert.Null(GetPrivateField<DecodedFrameBuffer>(
                        window,
                        "_pendingSynchronizedPrimaryFrame"));
                    Assert.Null(GetPrivateField<DecodedFrameBuffer>(
                        window,
                        "_pendingSynchronizedCompareFrame"));
                }
                finally
                {
                    window.Close();
                }
            });
        }

        [Fact]
        public void ComparePlayback_InvalidSecondBitmapLeavesBothDisplayedFramesUnchanged()
        {
            _fixture.Run(() =>
            {
                var window = new MainWindow();
                try
                {
                    RequireControl<CheckBox>(
                        window,
                        "CompareModeCheckBox").IsChecked = true;
                    using var originalPrimary = CreateFrameBuffer(
                        8,
                        4,
                        TimeSpan.FromSeconds(1),
                        red: 0xC0,
                        green: 0x20,
                        blue: 0x20);
                    using var originalCompare = CreateFrameBuffer(
                        8,
                        4,
                        TimeSpan.FromSeconds(1),
                        red: 0x20,
                        green: 0x20,
                        blue: 0xC0);
                    InvokePrivate(
                        window,
                        "SetPaneBitmap",
                        ParsePane("Primary"),
                        originalPrimary);
                    InvokePrivate(
                        window,
                        "SetPaneBitmap",
                        ParsePane("Compare"),
                        originalCompare);
                    var originalPrimaryDescriptor =
                        GetPrivateField<DecodedFrameBuffer>(
                            window,
                            "_primaryFrameBuffer")!.Descriptor;
                    var originalCompareDescriptor =
                        GetPrivateField<DecodedFrameBuffer>(
                            window,
                            "_compareFrameBuffer")!.Descriptor;
                    var originalPrimaryPixel = ReadBitmapPixel(
                        RequireBitmap(RequireControl<Image>(
                            window,
                            "CustomVideoSurface")),
                        0,
                        0);
                    var originalComparePixel = ReadBitmapPixel(
                        RequireBitmap(RequireControl<Image>(
                            window,
                            "CompareVideoSurface")),
                        0,
                        0);

                    using var replacementPrimary = CreateFrameBuffer(
                        8,
                        4,
                        TimeSpan.FromSeconds(2),
                        red: 0x20,
                        green: 0xC0,
                        blue: 0x20);
                    using var invalidCompare = new DecodedFrameBuffer(
                        replacementPrimary.Descriptor,
                        Array.Empty<byte>(),
                        8 * 4,
                        "bgra");

                    var result = (bool)InvokePrivate(
                        window,
                        "TrySetSynchronizedPaneBitmaps",
                        replacementPrimary,
                        invalidCompare);

                    Assert.False(result);
                    Assert.Same(
                        originalPrimaryDescriptor,
                        GetPrivateField<DecodedFrameBuffer>(
                            window,
                            "_primaryFrameBuffer")!.Descriptor);
                    Assert.Same(
                        originalCompareDescriptor,
                        GetPrivateField<DecodedFrameBuffer>(
                            window,
                            "_compareFrameBuffer")!.Descriptor);
                    var currentPrimaryPixel = ReadBitmapPixel(
                        RequireBitmap(RequireControl<Image>(
                            window,
                            "CustomVideoSurface")),
                        0,
                        0);
                    var currentComparePixel = ReadBitmapPixel(
                        RequireBitmap(RequireControl<Image>(
                            window,
                            "CompareVideoSurface")),
                        0,
                        0);
                    Assert.Equal(
                        originalPrimaryPixel.Red,
                        currentPrimaryPixel.Red);
                    Assert.Equal(
                        originalComparePixel.Blue,
                        currentComparePixel.Blue);

                    using var replacementCompare = CreateFrameBuffer(
                        8,
                        4,
                        TimeSpan.FromSeconds(2),
                        red: 0x20,
                        green: 0xC0,
                        blue: 0x20);
                    var compareSurface = RequireControl<Image>(
                        window,
                        "CompareVideoSurface");
                    var throwOnSourceCommit = true;
                    void ThrowDuringCompareSourceCommit(
                        object? sender,
                        AvaloniaPropertyChangedEventArgs eventArgs)
                    {
                        if (throwOnSourceCommit &&
                            eventArgs.Property == Image.SourceProperty)
                        {
                            throwOnSourceCommit = false;
                            throw new InvalidOperationException(
                                "Expected compare source commit failure.");
                        }
                    }

                    compareSurface.PropertyChanged +=
                        ThrowDuringCompareSourceCommit;
                    try
                    {
                        Assert.Throws<TargetInvocationException>(() =>
                            InvokePrivate(
                                window,
                                "TrySetSynchronizedPaneBitmaps",
                                replacementPrimary,
                                replacementCompare));
                    }
                    finally
                    {
                        compareSurface.PropertyChanged -=
                            ThrowDuringCompareSourceCommit;
                    }

                    Assert.Same(
                        originalPrimaryDescriptor,
                        GetPrivateField<DecodedFrameBuffer>(
                            window,
                            "_primaryFrameBuffer")!.Descriptor);
                    Assert.Same(
                        originalCompareDescriptor,
                        GetPrivateField<DecodedFrameBuffer>(
                            window,
                            "_compareFrameBuffer")!.Descriptor);
                    currentPrimaryPixel = ReadBitmapPixel(
                        RequireBitmap(RequireControl<Image>(
                            window,
                            "CustomVideoSurface")),
                        0,
                        0);
                    currentComparePixel = ReadBitmapPixel(
                        RequireBitmap(compareSurface),
                        0,
                        0);
                    Assert.Equal(
                        originalPrimaryPixel.Red,
                        currentPrimaryPixel.Red);
                    Assert.Equal(
                        originalComparePixel.Blue,
                        currentComparePixel.Blue);
                }
                finally
                {
                    window.Close();
                }
            });
        }

        [Theory]
        [InlineData("Primary", "Compare", 97L)]
        [InlineData("Compare", "Primary", 124L)]
        public async Task CompareSync_DifferentFrameRatesAcceptValidAtOrAfterLandings(
            string sourcePaneName,
            string targetPaneName,
            long sourceFrameIndex)
        {
            await _fixture.RunAsync(async () =>
            {
                var window = new MainWindow();
                try
                {
                    var primaryStep = TimeSpan.FromSeconds(1d / 24d);
                    var compareStep = TimeSpan.FromSeconds(1d / 30d);
                    var primaryMediaInfo = new VideoMediaInfo(
                        "left-24fps.mp4",
                        TimeSpan.FromSeconds(20),
                        primaryStep,
                        24d,
                        1920,
                        1080,
                        "h264",
                        0,
                        24,
                        1,
                        1,
                        90_000);
                    var compareMediaInfo = new VideoMediaInfo(
                        "right-30fps.mp4",
                        TimeSpan.FromSeconds(20),
                        compareStep,
                        30d,
                        1920,
                        1080,
                        "h264",
                        0,
                        30,
                        1,
                        1,
                        90_000);
                    var primaryEngine = new TestVideoReviewEngine
                    {
                        IsMediaOpen = true,
                        CurrentFilePath = "left-24fps.mp4",
                        MediaInfo = primaryMediaInfo,
                        Position = new ReviewPosition(
                            TimeSpan.FromSeconds(10),
                            240,
                            true,
                            true,
                            null,
                            null)
                    };
                    var compareEngine = new TestVideoReviewEngine
                    {
                        IsMediaOpen = true,
                        CurrentFilePath = "right-30fps.mp4",
                        MediaInfo = compareMediaInfo,
                        Position = new ReviewPosition(
                            TimeSpan.FromSeconds(11),
                            330,
                            true,
                            true,
                            null,
                            null)
                    };
                    SetPrivateField(window, "_primaryEngine", primaryEngine);
                    SetPrivateField(window, "_compareEngine", compareEngine);
                    RequireControl<CheckBox>(window, "CompareModeCheckBox").IsChecked = true;
                    SetPrivateField(window, "_isCompareModeSelected", true);

                    var primaryIsSource = sourcePaneName == "Primary";
                    var sourceStep = primaryIsSource
                        ? primaryStep
                        : compareStep;
                    var sourceTime = TimeSpan.FromTicks(
                        sourceFrameIndex * sourceStep.Ticks);
                    using var primaryFrame = CreateFrameBuffer(
                        8,
                        4,
                        primaryIsSource
                            ? sourceTime
                            : TimeSpan.FromSeconds(2));
                    using var compareFrame = CreateFrameBuffer(
                        8,
                        4,
                        primaryIsSource
                            ? TimeSpan.FromSeconds(2)
                            : sourceTime);
                    InvokePrivate(window, "SetPaneBitmap", ParsePane("Primary"), primaryFrame);
                    InvokePrivate(window, "SetPaneBitmap", ParsePane("Compare"), compareFrame);
                    primaryEngine.TimeSought = position =>
                    {
                        var quantizedTicks =
                            (long)Math.Ceiling(
                                (double)position.Ticks / primaryStep.Ticks) *
                            primaryStep.Ticks;
                        using var frame = CreateFrameBuffer(
                            8,
                            4,
                            TimeSpan.FromTicks(quantizedTicks));
                        InvokePrivate(
                            window,
                            "PrimaryEngine_FramePresented",
                            null!,
                            new FramePresentedEventArgs(frame));
                    };
                    compareEngine.TimeSought = position =>
                    {
                        var quantizedTicks =
                            (long)Math.Ceiling(
                                (double)position.Ticks / compareStep.Ticks) *
                            compareStep.Ticks;
                        using var frame = CreateFrameBuffer(
                            8,
                            4,
                            TimeSpan.FromTicks(quantizedTicks));
                        InvokePrivate(
                            window,
                            "CompareEngine_FramePresented",
                            null!,
                            new FramePresentedEventArgs(frame));
                    };

                    var result = await (Task<bool>)InvokePrivate(
                        window,
                        "AlignPaneToPaneAsync",
                        ParsePane(sourcePaneName),
                        ParsePane(targetPaneName));

                    Assert.True(result);
                    Assert.Equal(1, primaryEngine.SeekToTimeCallCount);
                    Assert.Equal(1, compareEngine.SeekToTimeCallCount);
                    Assert.Equal(0, primaryEngine.SeekToFrameCallCount);
                    Assert.Equal(0, compareEngine.SeekToFrameCallCount);
                    Assert.Equal(sourceTime, primaryEngine.Position.PresentationTime);
                    Assert.Equal(sourceTime, compareEngine.Position.PresentationTime);
                    var presentedPrimary = GetPrivateField<DecodedFrameBuffer>(
                        window,
                        "_primaryFrameBuffer")!.Descriptor.PresentationTime;
                    var presentedCompare = GetPrivateField<DecodedFrameBuffer>(
                        window,
                        "_compareFrameBuffer")!.Descriptor.PresentationTime;
                    var primaryDelta = presentedPrimary - sourceTime;
                    var compareDelta = presentedCompare - sourceTime;
                    Assert.True(primaryDelta >= TimeSpan.Zero);
                    Assert.True(compareDelta >= TimeSpan.Zero);
                    Assert.True(
                        primaryDelta < primaryStep);
                    Assert.True(
                        compareDelta < compareStep);
                    Assert.True(
                        (presentedPrimary - presentedCompare).Duration() <
                            TimeSpan.FromTicks(Math.Max(
                                primaryStep.Ticks,
                                compareStep.Ticks)));
                    Assert.True(
                        Math.Max(primaryDelta.Ticks, compareDelta.Ticks) >
                            Math.Max(
                                primaryStep.Ticks,
                                compareStep.Ticks) / 2L,
                        "The case must exercise a valid landing beyond the old half-frame tolerance.");
                    Assert.NotEqual(presentedPrimary, presentedCompare);
                }
                finally
                {
                    window.Close();
                }
            });
        }

        [Fact]
        public async Task ComparePlayback_PausedRepeatedMasterStepsUseLastPresentedPair()
        {
            await _fixture.RunAsync(async () =>
            {
                var window = new MainWindow();
                try
                {
                    const string filePath = "paused-master-step.mp4";
                    var frameStep = TimeSpan.FromSeconds(1d / 30d);
                    var mediaInfo = new VideoMediaInfo(
                        filePath,
                        TimeSpan.FromSeconds(20),
                        frameStep,
                        30d,
                        1920,
                        1080,
                        "h264",
                        0,
                        30,
                        1,
                        1,
                        90_000);
                    var primaryEngine = new TestVideoReviewEngine
                    {
                        IsMediaOpen = true,
                        CurrentFilePath = filePath,
                        MediaInfo = mediaInfo
                    };
                    var compareEngine = new TestVideoReviewEngine
                    {
                        IsMediaOpen = true,
                        CurrentFilePath = filePath,
                        MediaInfo = mediaInfo
                    };
                    SetPrivateField(window, "_primaryEngine", primaryEngine);
                    SetPrivateField(window, "_compareEngine", compareEngine);
                    RequireControl<CheckBox>(window, "CompareModeCheckBox").IsChecked = true;
                    const long initialPresentedFrame = 24L;
                    var initialPresentedTime = TimeSpan.FromTicks(
                        frameStep.Ticks * initialPresentedFrame);
                    primaryEngine.Position = new ReviewPosition(
                        TimeSpan.FromTicks(frameStep.Ticks * 29L),
                        29L,
                        true,
                        true,
                        null,
                        null);
                    compareEngine.Position = new ReviewPosition(
                        TimeSpan.FromTicks(frameStep.Ticks * 21L),
                        21L,
                        true,
                        true,
                        null,
                        null);
                    primaryEngine.IsPlaying = true;
                    compareEngine.IsPlaying = true;

                    using var initialPrimary = CreateFrameBuffer(
                        8,
                        4,
                        initialPresentedTime);
                    using var initialCompare = CreateFrameBuffer(
                        8,
                        4,
                        initialPresentedTime);
                    InvokePrivate(
                        window,
                        "SetPaneBitmap",
                        ParsePane("Primary"),
                        initialPrimary);
                    InvokePrivate(
                        window,
                        "SetPaneBitmap",
                        ParsePane("Compare"),
                        initialCompare);

                    await InvokePrivateTask(
                        window,
                        "PausePlaybackAsync",
                        new[]
                        {
                            typeof(bool),
                            typeof(SynchronizedOperationScope?)
                        },
                        true,
                        (SynchronizedOperationScope?)
                            SynchronizedOperationScope.AllPanes);

                    Assert.False(primaryEngine.IsPlaying);
                    Assert.False(compareEngine.IsPlaying);
                    Assert.NotEqual(
                        primaryEngine.Position.FrameIndex,
                        compareEngine.Position.FrameIndex);
                    Assert.Equal(
                        initialPresentedFrame,
                        GetPrivateField<DecodedFrameBuffer>(
                            window,
                            "_primaryFrameBuffer")!.Descriptor.FrameIndex);
                    Assert.Equal(
                        initialPresentedFrame,
                        GetPrivateField<DecodedFrameBuffer>(
                            window,
                            "_compareFrameBuffer")!.Descriptor.FrameIndex);

                    void EmitFrame(
                        TestVideoReviewEngine engine,
                        string eventMethod,
                        long frameIndex)
                    {
                        var presentationTime = TimeSpan.FromTicks(
                            frameStep.Ticks * frameIndex);
                        engine.Position = new ReviewPosition(
                            presentationTime,
                            frameIndex,
                            true,
                            true,
                            null,
                            null);
                        using var frame = CreateFrameBuffer(
                            8,
                            4,
                            presentationTime,
                            red: (byte)(0x20 + frameIndex),
                            green: (byte)(0x40 + frameIndex),
                            blue: (byte)(0x60 + frameIndex));
                        InvokePrivate(
                            window,
                            eventMethod,
                            null!,
                            new FramePresentedEventArgs(frame));
                    }

                    primaryEngine.FrameSought = frameIndex =>
                        EmitFrame(
                            primaryEngine,
                            "PrimaryEngine_FramePresented",
                            frameIndex);
                    compareEngine.FrameSought = frameIndex =>
                        EmitFrame(
                            compareEngine,
                            "CompareEngine_FramePresented",
                            frameIndex);
                    primaryEngine.StepForwarded = _ =>
                        EmitFrame(
                            primaryEngine,
                            "PrimaryEngine_FramePresented",
                            primaryEngine.Position.FrameIndex!.Value + 1L);
                    compareEngine.StepForwarded = _ =>
                        EmitFrame(
                            compareEngine,
                            "CompareEngine_FramePresented",
                            compareEngine.Position.FrameIndex!.Value + 1L);
                    primaryEngine.StepBackwarded = _ =>
                        EmitFrame(
                            primaryEngine,
                            "PrimaryEngine_FramePresented",
                            Math.Max(
                                0L,
                                primaryEngine.Position.FrameIndex!.Value - 1L));
                    compareEngine.StepBackwarded = _ =>
                        EmitFrame(
                            compareEngine,
                            "CompareEngine_FramePresented",
                            Math.Max(
                                0L,
                                compareEngine.Position.FrameIndex!.Value - 1L));

                    var expectedFrame = initialPresentedFrame;
                    foreach (var delta in new[] { 1, 1, 1, -1, -1, -1 })
                    {
                        expectedFrame += delta;
                        await InvokePrivateTask(
                            window,
                            "StepFrameAsync",
                            new[] { typeof(int) },
                            delta);

                        var primaryDescriptor =
                            GetPrivateField<DecodedFrameBuffer>(
                                window,
                                "_primaryFrameBuffer")!.Descriptor;
                        var compareDescriptor =
                            GetPrivateField<DecodedFrameBuffer>(
                                window,
                                "_compareFrameBuffer")!.Descriptor;
                        Assert.Equal(expectedFrame, primaryDescriptor.FrameIndex);
                        Assert.Equal(expectedFrame, compareDescriptor.FrameIndex);
                        Assert.Equal(
                            primaryDescriptor.PresentationTime,
                            compareDescriptor.PresentationTime);
                        Assert.True(primaryDescriptor.IsFrameIndexAbsolute);
                        Assert.True(compareDescriptor.IsFrameIndexAbsolute);
                        Assert.False(primaryEngine.IsPlaying);
                        Assert.False(compareEngine.IsPlaying);
                        Assert.False(GetPrivateField<bool>(
                            window,
                            "_isSynchronizedFramePresentationDeferred"));
                        Assert.Null(GetPrivateField<DecodedFrameBuffer>(
                            window,
                            "_pendingSynchronizedPrimaryFrame"));
                        Assert.Null(GetPrivateField<DecodedFrameBuffer>(
                            window,
                            "_pendingSynchronizedCompareFrame"));
                    }

                    var blockedSeek = new TaskCompletionSource<bool>(
                        TaskCreationOptions.RunContinuationsAsynchronously);
                    primaryEngine.SeekFrameCompletion = blockedSeek;
                    compareEngine.SeekFrameCompletion = blockedSeek;
                    try
                    {
                        var priorPrimarySeekCount =
                            primaryEngine.SeekToFrameCallCount;
                        var priorCompareSeekCount =
                            compareEngine.SeekToFrameCallCount;
                        var firstRapidStep = InvokePrivateTask(
                            window,
                            "StepFrameAsync",
                            new[] { typeof(int) },
                            1);
                        Assert.True(
                            SpinWait.SpinUntil(
                                () =>
                                    primaryEngine.SeekToFrameCallCount ==
                                        priorPrimarySeekCount + 1 &&
                                    compareEngine.SeekToFrameCallCount ==
                                        priorCompareSeekCount + 1,
                                TimeSpan.FromSeconds(2)),
                            "The first rapid master step did not reach both engines.");
                        var secondRapidStep = InvokePrivateTask(
                            window,
                            "StepFrameAsync",
                            new[] { typeof(int) },
                            1);
                        var thirdRapidStep = InvokePrivateTask(
                            window,
                            "StepFrameAsync",
                            new[] { typeof(int) },
                            1);
                        var fourthRapidStep = InvokePrivateTask(
                            window,
                            "StepFrameAsync",
                            new[] { typeof(int) },
                            1);
                        Assert.False(
                            secondRapidStep.IsCompleted,
                            "The second master step bypassed the paired-step gate.");
                        Assert.False(
                            thirdRapidStep.IsCompleted,
                            "The third master step bypassed the paired-step gate.");
                        Assert.False(
                            fourthRapidStep.IsCompleted,
                            "The fourth master step bypassed the paired-step gate.");

                        blockedSeek.TrySetResult(true);
                        await Task.WhenAll(
                            firstRapidStep,
                            secondRapidStep,
                            thirdRapidStep,
                            fourthRapidStep);

                        var expectedRapidFrame =
                            initialPresentedFrame + 4L;
                        Assert.Equal(
                            expectedRapidFrame,
                            GetPrivateField<DecodedFrameBuffer>(
                                window,
                                "_primaryFrameBuffer")!.Descriptor.FrameIndex);
                        Assert.Equal(
                            expectedRapidFrame,
                            GetPrivateField<DecodedFrameBuffer>(
                                window,
                                "_compareFrameBuffer")!.Descriptor.FrameIndex);
                        Assert.Equal(
                            priorPrimarySeekCount + 4,
                            primaryEngine.SeekToFrameCallCount);
                        Assert.Equal(
                            priorCompareSeekCount + 4,
                            compareEngine.SeekToFrameCallCount);
                    }
                    finally
                    {
                        blockedSeek.TrySetResult(true);
                        primaryEngine.SeekFrameCompletion = null;
                        compareEngine.SeekFrameCompletion = null;
                    }

                    var staleStepBlock = new TaskCompletionSource<bool>(
                        TaskCreationOptions.RunContinuationsAsynchronously);
                    primaryEngine.SeekFrameCompletion = staleStepBlock;
                    compareEngine.SeekFrameCompletion = staleStepBlock;
                    try
                    {
                        var priorPrimarySeekCount =
                            primaryEngine.SeekToFrameCallCount;
                        var priorCompareSeekCount =
                            compareEngine.SeekToFrameCallCount;
                        var blockedOlderStep = InvokePrivateTask(
                            window,
                            "StepFrameAsync",
                            new[] { typeof(int) },
                            1);
                        Assert.True(
                            SpinWait.SpinUntil(
                                () =>
                                    primaryEngine.SeekToFrameCallCount ==
                                        priorPrimarySeekCount + 1 &&
                                    compareEngine.SeekToFrameCallCount ==
                                        priorCompareSeekCount + 1,
                                TimeSpan.FromSeconds(2)),
                            "The blocked older step did not reach both engines.");
                        var queuedOlderStep = InvokePrivateTask(
                            window,
                            "StepFrameAsync",
                            new[] { typeof(int) },
                            1);
                        var newerPlay = InvokePrivateTask(
                            window,
                            "StartAllPanePlaybackAsync",
                            Type.EmptyTypes);

                        staleStepBlock.TrySetResult(true);
                        await Task.WhenAll(
                            blockedOlderStep,
                            queuedOlderStep,
                            newerPlay);

                        Assert.Equal(
                            priorPrimarySeekCount + 1,
                            primaryEngine.SeekToFrameCallCount);
                        Assert.Equal(
                            priorCompareSeekCount + 1,
                            compareEngine.SeekToFrameCallCount);
                        Assert.Equal(1, primaryEngine.PlayCallCount);
                        Assert.Equal(1, compareEngine.PlayCallCount);
                        Assert.True(primaryEngine.IsPlaying);
                        Assert.True(compareEngine.IsPlaying);
                        Assert.Equal(
                            initialPresentedFrame + 4L,
                            GetPrivateField<DecodedFrameBuffer>(
                                window,
                                "_primaryFrameBuffer")!.Descriptor.FrameIndex);
                        Assert.Equal(
                            initialPresentedFrame + 4L,
                            GetPrivateField<DecodedFrameBuffer>(
                                window,
                                "_compareFrameBuffer")!.Descriptor.FrameIndex);
                    }
                    finally
                    {
                        staleStepBlock.TrySetResult(true);
                        primaryEngine.SeekFrameCompletion = null;
                        compareEngine.SeekFrameCompletion = null;
                    }

                    primaryEngine.Position = new ReviewPosition(
                        TimeSpan.FromTicks(frameStep.Ticks * 31L),
                        31L,
                        true,
                        true,
                        null,
                        null);
                    compareEngine.Position = new ReviewPosition(
                        TimeSpan.FromTicks(frameStep.Ticks * 19L),
                        19L,
                        true,
                        true,
                        null,
                        null);
                    var playingStepBlock =
                        new TaskCompletionSource<bool>(
                            TaskCreationOptions.RunContinuationsAsynchronously);
                    primaryEngine.SeekFrameCompletion = playingStepBlock;
                    compareEngine.SeekFrameCompletion = playingStepBlock;
                    try
                    {
                        var priorPrimarySeekCount =
                            primaryEngine.SeekToFrameCallCount;
                        var priorCompareSeekCount =
                            compareEngine.SeekToFrameCallCount;
                        var firstPlayingStep = InvokePrivateTask(
                            window,
                            "StepFrameAsync",
                            new[] { typeof(int) },
                            1);
                        Assert.True(
                            SpinWait.SpinUntil(
                                () =>
                                    primaryEngine.SeekToFrameCallCount ==
                                        priorPrimarySeekCount + 1 &&
                                    compareEngine.SeekToFrameCallCount ==
                                        priorCompareSeekCount + 1,
                                TimeSpan.FromSeconds(2)),
                            "The playing master step did not pause and seek both engines.");
                        var secondPlayingStep = InvokePrivateTask(
                            window,
                            "StepFrameAsync",
                            new[] { typeof(int) },
                            1);

                        playingStepBlock.TrySetResult(true);
                        await Task.WhenAll(
                            firstPlayingStep,
                            secondPlayingStep);

                        var expectedPlayingRapidFrame =
                            initialPresentedFrame + 6L;
                        Assert.Equal(
                            expectedPlayingRapidFrame,
                            GetPrivateField<DecodedFrameBuffer>(
                                window,
                                "_primaryFrameBuffer")!.Descriptor.FrameIndex);
                        Assert.Equal(
                            expectedPlayingRapidFrame,
                            GetPrivateField<DecodedFrameBuffer>(
                                window,
                                "_compareFrameBuffer")!.Descriptor.FrameIndex);
                        Assert.Equal(
                            priorPrimarySeekCount + 2,
                            primaryEngine.SeekToFrameCallCount);
                        Assert.Equal(
                            priorCompareSeekCount + 2,
                            compareEngine.SeekToFrameCallCount);
                        Assert.False(primaryEngine.IsPlaying);
                        Assert.False(compareEngine.IsPlaying);
                    }
                    finally
                    {
                        playingStepBlock.TrySetResult(true);
                        primaryEngine.SeekFrameCompletion = null;
                        compareEngine.SeekFrameCompletion = null;
                    }
                }
                finally
                {
                    window.Close();
                }
            });
        }

        [Fact]
        public async Task ComparePlayback_MultiStepAllPaneCommandPresentsFinalPair()
        {
            await _fixture.RunAsync(async () =>
            {
                var window = new MainWindow();
                try
                {
                    var primaryFrameStep = TimeSpan.FromSeconds(1d / 24d);
                    var compareFrameStep = TimeSpan.FromSeconds(1d / 30d);
                    var primaryStart = TimeSpan.FromSeconds(2);
                    var compareStart = TimeSpan.FromSeconds(5);
                    var primaryMediaInfo = new VideoMediaInfo(
                        "multi-step-primary.mp4",
                        TimeSpan.FromSeconds(30),
                        primaryFrameStep,
                        24d,
                        1920,
                        1080,
                        "h264",
                        0,
                        24,
                        1,
                        1,
                        90_000);
                    var compareMediaInfo = new VideoMediaInfo(
                        "multi-step-compare.mp4",
                        TimeSpan.FromSeconds(30),
                        compareFrameStep,
                        30d,
                        1920,
                        1080,
                        "h264",
                        0,
                        30,
                        1,
                        1,
                        90_000);
                    var primaryEngine = new TestVideoReviewEngine
                    {
                        IsMediaOpen = true,
                        MediaInfo = primaryMediaInfo,
                        Position = new ReviewPosition(
                            primaryStart,
                            null,
                            false,
                            false,
                            null,
                            null)
                    };
                    var compareEngine = new TestVideoReviewEngine
                    {
                        IsMediaOpen = true,
                        MediaInfo = compareMediaInfo,
                        Position = new ReviewPosition(
                            compareStart,
                            null,
                            false,
                            false,
                            null,
                            null)
                    };
                    primaryEngine.StepForwarded = step =>
                    {
                        using var frame = CreateFrameBuffer(
                            8,
                            4,
                            primaryStart +
                                TimeSpan.FromTicks(
                                    primaryFrameStep.Ticks * step));
                        InvokePrivate(
                            window,
                            "PrimaryEngine_FramePresented",
                            null!,
                            new FramePresentedEventArgs(frame));
                    };
                    compareEngine.StepForwarded = step =>
                    {
                        using var frame = CreateFrameBuffer(
                            8,
                            4,
                            compareStart +
                                TimeSpan.FromTicks(
                                    compareFrameStep.Ticks * step));
                        InvokePrivate(
                            window,
                            "CompareEngine_FramePresented",
                            null!,
                            new FramePresentedEventArgs(frame));
                    };
                    SetPrivateField(window, "_primaryEngine", primaryEngine);
                    SetPrivateField(window, "_compareEngine", compareEngine);
                    RequireControl<CheckBox>(window, "CompareModeCheckBox").IsChecked = true;
                    await InvokePrivateTask(
                        window,
                        "StepFrameAsync",
                        new[] { typeof(int) },
                        10);
                    InvokePrivate(
                        window,
                        "PresentPendingSynchronizedFrames",
                        GetPrivateField<long>(
                            window,
                            "_synchronizedFramePresentationGeneration"));

                    var expectedPrimary =
                        primaryStart +
                            TimeSpan.FromTicks(primaryFrameStep.Ticks * 10);
                    var expectedCompare =
                        compareStart +
                            TimeSpan.FromTicks(compareFrameStep.Ticks * 10);
                    Assert.Equal(10, primaryEngine.StepForwardCallCount);
                    Assert.Equal(10, compareEngine.StepForwardCallCount);
                    Assert.Equal(
                        expectedPrimary,
                        GetPrivateField<DecodedFrameBuffer>(
                            window,
                            "_primaryFrameBuffer")!.Descriptor.PresentationTime);
                    Assert.Equal(
                        expectedCompare,
                        GetPrivateField<DecodedFrameBuffer>(
                            window,
                            "_compareFrameBuffer")!.Descriptor.PresentationTime);
                    Assert.Equal(
                        expectedPrimary - expectedCompare,
                        GetPrivateField<TimeSpan>(
                            window,
                            "_synchronizedFramePresentationTimeOffset"));
                    Assert.False(GetPrivateField<bool>(
                        window,
                        "_isSynchronizedFramePresentationDeferred"));
                    Assert.Null(GetPrivateField<DecodedFrameBuffer>(
                        window,
                        "_pendingSynchronizedPrimaryFrame"));
                    Assert.Null(GetPrivateField<DecodedFrameBuffer>(
                        window,
                        "_pendingSynchronizedCompareFrame"));
                }
                finally
                {
                    window.Close();
                }
            });
        }

        [Fact]
        public async Task ComparePlayback_PausedMasterStepPresentsQuantizedDifferentMediaPair()
        {
            await _fixture.RunAsync(async () =>
            {
                var window = new MainWindow();
                try
                {
                    var primaryFrameStep =
                        TimeSpan.FromSeconds(1d / 24d);
                    var compareFrameStep =
                        TimeSpan.FromSeconds(1d / 30d);
                    var primaryStart = TimeSpan.FromTicks(
                        primaryFrameStep.Ticks * 48L);
                    var compareStart = TimeSpan.FromTicks(
                        compareFrameStep.Ticks * 150L);
                    var primaryEngine = new TestVideoReviewEngine
                    {
                        IsMediaOpen = true,
                        CurrentFilePath = "master-24fps.mp4",
                        MediaInfo = new VideoMediaInfo(
                            "master-24fps.mp4",
                            TimeSpan.FromSeconds(20),
                            primaryFrameStep,
                            24d,
                            1920,
                            1080,
                            "h264",
                            0,
                            24,
                            1,
                            1,
                            90_000),
                        Position = new ReviewPosition(
                            primaryStart,
                            null,
                            false,
                            false,
                            null,
                            null)
                    };
                    var compareEngine = new TestVideoReviewEngine
                    {
                        IsMediaOpen = true,
                        CurrentFilePath = "compare-30fps.mp4",
                        MediaInfo = new VideoMediaInfo(
                            "compare-30fps.mp4",
                            TimeSpan.FromSeconds(20),
                            compareFrameStep,
                            30d,
                            1920,
                            1080,
                            "h264",
                            0,
                            30,
                            1,
                            1,
                            90_000),
                        Position = new ReviewPosition(
                            compareStart,
                            null,
                            false,
                            false,
                            null,
                            null)
                    };
                    SetPrivateField(window, "_primaryEngine", primaryEngine);
                    SetPrivateField(window, "_compareEngine", compareEngine);
                    RequireControl<CheckBox>(
                        window,
                        "CompareModeCheckBox").IsChecked = true;
                    using var initialPrimary = CreateFrameBuffer(
                        8,
                        4,
                        primaryStart);
                    using var initialCompare = CreateFrameBuffer(
                        8,
                        4,
                        compareStart);
                    InvokePrivate(
                        window,
                        "SetPaneBitmap",
                        ParsePane("Primary"),
                        initialPrimary);
                    InvokePrivate(
                        window,
                        "SetPaneBitmap",
                        ParsePane("Compare"),
                        initialCompare);
                    primaryEngine.TimeSought = position =>
                    {
                        var frameIndex =
                            (position.Ticks + primaryFrameStep.Ticks - 1L) /
                            primaryFrameStep.Ticks;
                        using var frame = CreateFrameBuffer(
                            8,
                            4,
                            TimeSpan.FromTicks(
                                primaryFrameStep.Ticks * frameIndex));
                        InvokePrivate(
                            window,
                            "PrimaryEngine_FramePresented",
                            null!,
                            new FramePresentedEventArgs(frame));
                    };
                    compareEngine.TimeSought = position =>
                    {
                        var frameIndex =
                            (position.Ticks + compareFrameStep.Ticks - 1L) /
                            compareFrameStep.Ticks;
                        using var frame = CreateFrameBuffer(
                            8,
                            4,
                            TimeSpan.FromTicks(
                                compareFrameStep.Ticks * frameIndex));
                        InvokePrivate(
                            window,
                            "CompareEngine_FramePresented",
                            null!,
                            new FramePresentedEventArgs(frame));
                    };

                    await InvokePrivateTask(
                        window,
                        "StepFrameAsync",
                        new[] { typeof(int) },
                        1);

                    var expectedPrimary =
                        primaryStart + primaryFrameStep;
                    var expectedCompare = TimeSpan.FromTicks(
                        compareFrameStep.Ticks * 152L);
                    Assert.Equal(
                        expectedPrimary,
                        GetPrivateField<DecodedFrameBuffer>(
                            window,
                            "_primaryFrameBuffer")!.Descriptor.PresentationTime);
                    Assert.Equal(
                        expectedCompare,
                        GetPrivateField<DecodedFrameBuffer>(
                            window,
                            "_compareFrameBuffer")!.Descriptor.PresentationTime);
                    Assert.NotEqual(
                        primaryStart - compareStart,
                        expectedPrimary - expectedCompare);
                    Assert.Equal(
                        expectedPrimary - expectedCompare,
                        GetPrivateField<TimeSpan>(
                            window,
                            "_synchronizedFramePresentationTimeOffset"));
                    Assert.Equal(1, primaryEngine.SeekToTimeCallCount);
                    Assert.Equal(1, compareEngine.SeekToTimeCallCount);
                    Assert.Equal(0, primaryEngine.StepForwardCallCount);
                    Assert.Equal(0, compareEngine.StepForwardCallCount);
                }
                finally
                {
                    window.Close();
                }
            });
        }

        [Theory]
        [InlineData(1)]
        [InlineData(-1)]
        public async Task ComparePlayback_AllPaneBoundaryStepKeepsPriorPair(
            int delta)
        {
            await _fixture.RunAsync(async () =>
            {
                var window = new MainWindow();
                try
                {
                    var frameStep = TimeSpan.FromSeconds(1d / 30d);
                    var primaryTime = delta > 0
                        ? TimeSpan.FromSeconds(10)
                        : TimeSpan.Zero;
                    var compareTime = TimeSpan.FromSeconds(5);
                    var mediaInfo = new VideoMediaInfo(
                        "boundary-step.mp4",
                        TimeSpan.FromSeconds(10),
                        frameStep,
                        30d,
                        1920,
                        1080,
                        "h264",
                        0,
                        30,
                        1,
                        1,
                        90_000);
                    var primaryEngine = new TestVideoReviewEngine
                    {
                        IsMediaOpen = true,
                        MediaInfo = mediaInfo,
                        Position = new ReviewPosition(
                            primaryTime,
                            null,
                            false,
                            false,
                            null,
                            null)
                    };
                    var compareEngine = new TestVideoReviewEngine
                    {
                        IsMediaOpen = true,
                        MediaInfo = mediaInfo,
                        Position = new ReviewPosition(
                            compareTime,
                            null,
                            false,
                            false,
                            null,
                            null)
                    };
                    SetPrivateField(window, "_primaryEngine", primaryEngine);
                    SetPrivateField(window, "_compareEngine", compareEngine);
                    RequireControl<CheckBox>(window, "CompareModeCheckBox").IsChecked = true;
                    using var initialPrimary = CreateFrameBuffer(
                        8,
                        4,
                        primaryTime);
                    using var initialCompare = CreateFrameBuffer(
                        8,
                        4,
                        compareTime);
                    InvokePrivate(
                        window,
                        "SetPaneBitmap",
                        ParsePane("Primary"),
                        initialPrimary);
                    InvokePrivate(
                        window,
                        "SetPaneBitmap",
                        ParsePane("Compare"),
                        initialCompare);

                    await InvokePrivateTask(
                        window,
                        "StepFrameAsync",
                        new[] { typeof(int) },
                        delta);
                    InvokePrivate(
                        window,
                        "PresentPendingSynchronizedFrames",
                        GetPrivateField<long>(
                            window,
                            "_synchronizedFramePresentationGeneration"));

                    Assert.Equal(
                        primaryTime,
                        GetPrivateField<DecodedFrameBuffer>(
                            window,
                            "_primaryFrameBuffer")!.Descriptor.PresentationTime);
                    Assert.Equal(
                        compareTime,
                        GetPrivateField<DecodedFrameBuffer>(
                            window,
                            "_compareFrameBuffer")!.Descriptor.PresentationTime);
                    Assert.Equal(0, primaryEngine.StepForwardCallCount);
                    Assert.Equal(0, primaryEngine.StepBackwardCallCount);
                    Assert.Equal(0, compareEngine.StepForwardCallCount);
                    Assert.Equal(0, compareEngine.StepBackwardCallCount);
                    Assert.Equal(0, primaryEngine.SeekToTimeCallCount);
                    Assert.Equal(0, compareEngine.SeekToTimeCallCount);
                    Assert.Null(GetPrivateField<DecodedFrameBuffer>(
                        window,
                        "_pendingSynchronizedPrimaryFrame"));
                    Assert.Null(GetPrivateField<DecodedFrameBuffer>(
                        window,
                        "_pendingSynchronizedCompareFrame"));
                    Assert.False(GetPrivateField<bool>(
                        window,
                        "_isSynchronizedFramePresentationDeferred"));
                }
                finally
                {
                    window.Close();
                }
            });
        }

        [Fact]
        public async Task ComparePlayback_SharedSeekPresentsTargetFramesOnlyAsAPair()
        {
            await _fixture.RunAsync(async () =>
            {
                var window = new MainWindow();
                var seekCompletion = new TaskCompletionSource<bool>(
                    TaskCreationOptions.RunContinuationsAsynchronously);
                try
                {
                    var frameStep = TimeSpan.FromSeconds(1d / 30d);
                    var mediaInfo = new VideoMediaInfo(
                        "shared.mp4",
                        TimeSpan.FromSeconds(10),
                        frameStep,
                        30d,
                        1920,
                        1080,
                        "h264",
                        0,
                        30,
                        1,
                        1,
                        90_000);
                    var primaryEngine = new TestVideoReviewEngine
                    {
                        IsMediaOpen = true,
                        MediaInfo = mediaInfo,
                        SeekTimeCompletion = seekCompletion
                    };
                    var compareEngine = new TestVideoReviewEngine
                    {
                        IsMediaOpen = true,
                        MediaInfo = mediaInfo,
                        SeekTimeCompletion = seekCompletion
                    };
                    SetPrivateField(window, "_primaryEngine", primaryEngine);
                    SetPrivateField(window, "_compareEngine", compareEngine);
                    RequireControl<CheckBox>(window, "CompareModeCheckBox").IsChecked = true;

                    var sharedSeek = InvokePrivateTask(
                        window,
                        "SeekAllPaneToTimesPreservingPlaybackAsync",
                        new[] { typeof(TimeSpan), typeof(TimeSpan), typeof(CancellationToken) },
                        TimeSpan.FromSeconds(2),
                        TimeSpan.FromSeconds(2),
                        CancellationToken.None);
                    Assert.True(
                        SpinWait.SpinUntil(
                            () => primaryEngine.SeekToTimeCallCount == 1 &&
                                compareEngine.SeekToTimeCallCount == 1,
                            TimeSpan.FromSeconds(2)),
                        "The shared seek did not reach both engines.");
                    Assert.True(GetPrivateField<bool>(window, "_isSynchronizedFramePresentationActive"));

                    var target = TimeSpan.FromSeconds(2);
                    using var primaryFrame = CreateFrameBuffer(8, 4, target);
                    using var compareFrame = CreateFrameBuffer(8, 4, target);
                    InvokePrivate(
                        window,
                        "PrimaryEngine_FramePresented",
                        null!,
                        new FramePresentedEventArgs(primaryFrame));
                    InvokePrivate(
                        window,
                        "PresentPendingSynchronizedFrames",
                        GetPrivateField<long>(window, "_synchronizedFramePresentationGeneration"));

                    Assert.Null(GetPrivateField<DecodedFrameBuffer>(window, "_primaryFrameBuffer"));
                    Assert.Null(GetPrivateField<DecodedFrameBuffer>(window, "_compareFrameBuffer"));

                    InvokePrivate(
                        window,
                        "CompareEngine_FramePresented",
                        null!,
                        new FramePresentedEventArgs(compareFrame));
                    InvokePrivate(
                        window,
                        "PresentPendingSynchronizedFrames",
                        GetPrivateField<long>(window, "_synchronizedFramePresentationGeneration"));

                    Assert.Equal(
                        target,
                        GetPrivateField<DecodedFrameBuffer>(window, "_primaryFrameBuffer")!.Descriptor.PresentationTime);
                    Assert.Equal(
                        target,
                        GetPrivateField<DecodedFrameBuffer>(window, "_compareFrameBuffer")!.Descriptor.PresentationTime);

                    seekCompletion.TrySetResult(true);
                    await sharedSeek;
                    Assert.True(GetPrivateField<bool>(window, "_isSynchronizedFramePresentationActive"));
                }
                finally
                {
                    seekCompletion.TrySetResult(true);
                    window.Close();
                }
            });
        }

        [Fact]
        public void ComparePlayback_IndividualFrameQueuedBeforeSyncCannotLandAfterTransition()
        {
            _fixture.Run(() =>
            {
                var window = new MainWindow();
                try
                {
                    using var frame = CreateFrameBuffer(8, 4, TimeSpan.FromSeconds(1));
                    InvokePrivate(
                        window,
                        "QueueFramePresentation",
                        ParsePane("Primary"),
                        frame);
                    SetPrivateField(window, "_isSynchronizedFramePresentationActive", true);
                    SetPrivateField(
                        window,
                        "_synchronizedFramePresentationGeneration",
                        GetPrivateField<long>(window, "_synchronizedFramePresentationGeneration") + 1L);

                    InvokePrivate(window, "PresentPendingFrame", ParsePane("Primary"));

                    Assert.Null(GetPrivateField<DecodedFrameBuffer>(window, "_primaryFrameBuffer"));
                    Assert.Null(RequireControl<Image>(window, "CustomVideoSurface").Source);
                }
                finally
                {
                    window.Close();
                }
            });
        }

        [Fact]
        public async Task PrimaryPlaybackStateChanged_FromBackgroundThreadPostsUiWork()
        {
            MainWindow? window = null;
            try
            {
                await _fixture.RunAsync(async () =>
                {
                    window = new MainWindow();
                    var state = new VideoReviewEngineStateChangedEventArgs(
                        isMediaOpen: false,
                        isPlaying: false,
                        currentFilePath: string.Empty,
                        lastErrorMessage: string.Empty,
                        mediaInfo: VideoMediaInfo.Empty,
                        position: ReviewPosition.Empty);

                    await Task.Run(() => InvokePrivate(
                        window!,
                        "PrimaryEngine_StateChanged",
                        null!,
                        state));
                });

                _fixture.Run(() =>
                {
                    Assert.Equal(
                        "Ready",
                        RequireControl<TextBlock>(
                            window!,
                            "PrimaryPaneStateTextBlock").Text);
                });
            }
            finally
            {
                if (window != null)
                {
                    _fixture.Run(() => window.Close());
                }
            }
        }

        [Fact]
        public void ComparePlayback_StaleSyncTeardownCannotDisableNewerPresentationGeneration()
        {
            _fixture.Run(() =>
            {
                var window = new MainWindow();
                try
                {
                    var staleTransportGeneration = (int)InvokePrivate(
                        window,
                        "BeginAllPaneTransportIntent");
                    var currentTransportGeneration = (int)InvokePrivate(
                        window,
                        "BeginAllPaneTransportIntent");

                    Assert.True((bool)InvokePrivate(
                        window,
                        "TryBeginSynchronizedFramePresentation",
                        currentTransportGeneration));
                    var presentationGeneration = GetPrivateField<long>(
                        window,
                        "_synchronizedFramePresentationGeneration");
                    var endPresentation = typeof(MainWindow).GetMethod(
                        "EndSynchronizedFramePresentation",
                        BindingFlags.Instance | BindingFlags.NonPublic,
                        binder: null,
                        types: new[] { typeof(int) },
                        modifiers: null)
                        ?? throw new MissingMethodException(
                            typeof(MainWindow).FullName,
                            "EndSynchronizedFramePresentation(Int32)");

                    endPresentation.Invoke(window, new object[] { staleTransportGeneration });

                    Assert.Equal(
                        currentTransportGeneration,
                        GetPrivateField<int>(window, "_allPaneTransportIntentGeneration"));
                    Assert.Equal(
                        presentationGeneration,
                        GetPrivateField<long>(window, "_synchronizedFramePresentationGeneration"));
                    Assert.True(GetPrivateField<bool>(
                        window,
                        "_isSynchronizedFramePresentationActive"));
                }
                finally
                {
                    window.Close();
                }
            });
        }

        [Fact]
        public async Task ComparePlayback_SharedPauseKeepsUnmatchedFinalFrameOffScreenUntilBothPanesStop()
        {
            await _fixture.RunAsync(async () =>
            {
                var window = new MainWindow();
                var primaryPauseCompletion =
                    new TaskCompletionSource<bool>(
                        TaskCreationOptions.RunContinuationsAsynchronously);
                var comparePauseCompletion =
                    new TaskCompletionSource<bool>(
                        TaskCreationOptions.RunContinuationsAsynchronously);
                try
                {
                    var primaryEngine = new TestVideoReviewEngine
                    {
                        IsMediaOpen = true,
                        IsPlaying = true,
                        PauseCompletion = primaryPauseCompletion
                    };
                    var compareEngine = new TestVideoReviewEngine
                    {
                        IsMediaOpen = true,
                        IsPlaying = true,
                        PauseCompletion = comparePauseCompletion
                    };
                    SetPrivateField(window, "_primaryEngine", primaryEngine);
                    SetPrivateField(window, "_compareEngine", compareEngine);
                    RequireControl<CheckBox>(
                        window,
                        "CompareModeCheckBox").IsChecked = true;
                    SetPrivateField(window, "_isCompareModeSelected", true);
                    SetPrivateField(window, "_isAllPaneTransportSelected", true);

                    using var initialPrimary = CreateFrameBuffer(
                        8,
                        4,
                        TimeSpan.FromSeconds(1));
                    using var initialCompare = CreateFrameBuffer(
                        8,
                        4,
                        TimeSpan.FromSeconds(1));
                    InvokePrivate(
                        window,
                        "SetPaneBitmap",
                        ParsePane("Primary"),
                        initialPrimary);
                    InvokePrivate(
                        window,
                        "SetPaneBitmap",
                        ParsePane("Compare"),
                        initialCompare);
                    Assert.True((bool)InvokePrivate(
                        window,
                        "TryBeginSynchronizedFramePresentation",
                        GetPrivateField<int>(
                            window,
                            "_allPaneTransportIntentGeneration")));

                    var displayedPrimaryDescriptor =
                        GetPrivateField<DecodedFrameBuffer>(
                            window,
                            "_primaryFrameBuffer")!.Descriptor;
                    var displayedCompareDescriptor =
                        GetPrivateField<DecodedFrameBuffer>(
                            window,
                            "_compareFrameBuffer")!.Descriptor;
                    var pauseTask = InvokePrivateTask(
                        window,
                        "PausePlaybackAsync",
                        new[]
                        {
                            typeof(bool),
                            typeof(SynchronizedOperationScope?)
                        },
                        true,
                        (SynchronizedOperationScope?)
                            SynchronizedOperationScope.AllPanes);
                    Assert.True(
                        SpinWait.SpinUntil(
                            () => primaryEngine.PauseCallCount == 1 &&
                                compareEngine.PauseCallCount == 1,
                            TimeSpan.FromSeconds(2)),
                        "Shared pause did not reach both engines.");
                    Assert.False(pauseTask.IsCompleted);
                    Assert.False(GetPrivateField<bool>(
                        window,
                        "_isSynchronizedFramePresentationActive"));
                    Assert.Equal(
                        GetPrivateField<int>(
                            window,
                            "_allPaneTransportIntentGeneration"),
                        GetPrivateField<int>(
                            window,
                            "_allPanePausePresentationHoldGeneration"));

                    using var unmatchedPrimary = CreateFrameBuffer(
                        8,
                        4,
                        TimeSpan.FromSeconds(2));
                    InvokePrivate(
                        window,
                        "PrimaryEngine_FramePresented",
                        null!,
                        new FramePresentedEventArgs(unmatchedPrimary));
                    InvokePrivate(
                        window,
                        "PresentPendingFrame",
                        ParsePane("Primary"));

                    Assert.Same(
                        displayedPrimaryDescriptor,
                        GetPrivateField<DecodedFrameBuffer>(
                            window,
                            "_primaryFrameBuffer")!.Descriptor);
                    Assert.Same(
                        displayedCompareDescriptor,
                        GetPrivateField<DecodedFrameBuffer>(
                            window,
                            "_compareFrameBuffer")!.Descriptor);
                    Assert.Null(GetPrivateField<DecodedFrameBuffer>(
                        window,
                        "_pendingSynchronizedPrimaryFrame"));

                    primaryPauseCompletion.SetResult(true);
                    comparePauseCompletion.SetResult(true);
                    await pauseTask.WaitAsync(TimeSpan.FromSeconds(2));

                    Assert.False(GetPrivateField<bool>(
                        window,
                        "_isSynchronizedFramePresentationActive"));
                    Assert.Null(GetPrivateField<DecodedFrameBuffer>(
                        window,
                        "_pendingSynchronizedPrimaryFrame"));
                    Assert.Same(
                        displayedPrimaryDescriptor,
                        GetPrivateField<DecodedFrameBuffer>(
                            window,
                            "_primaryFrameBuffer")!.Descriptor);
                    Assert.Same(
                        displayedCompareDescriptor,
                        GetPrivateField<DecodedFrameBuffer>(
                            window,
                            "_compareFrameBuffer")!.Descriptor);
                }
                finally
                {
                    primaryPauseCompletion.TrySetResult(true);
                    comparePauseCompletion.TrySetResult(true);
                    window.Close();
                }
            });
        }

        [Fact]
        public async Task ComparePlayback_AllPaneStartInvalidatesQueuedLoopRestartsBeforeWaiting()
        {
            await _fixture.RunAsync(async () =>
            {
                var window = new MainWindow();
                var operationGate = GetPrivateField<SemaphoreSlim>(
                    window,
                    "_allPaneTransportOperationGate")
                    ?? throw new InvalidOperationException("Missing all-pane transport operation gate.");
                var operationGateHeld = false;
                try
                {
                    var primaryEngine = new TestVideoReviewEngine { IsMediaOpen = true };
                    var compareEngine = new TestVideoReviewEngine { IsMediaOpen = true };
                    SetPrivateField(window, "_primaryEngine", primaryEngine);
                    SetPrivateField(window, "_compareEngine", compareEngine);
                    SetPrivateField(window, "_isCompareModeSelected", true);
                    SetPrivateField(window, "_isAllPaneTransportSelected", true);
                    SetPrivateField(window, "_isPrimaryLoopPlaybackEnabled", true);
                    SetPrivateField(window, "_isCompareLoopPlaybackEnabled", true);
                    var primaryPane = ParsePane("Primary");
                    var comparePane = ParsePane("Compare");
                    var stalePrimaryLoopGeneration = GetPrivateField<int>(
                        window,
                        "_primaryLoopRestartGeneration");
                    var staleCompareLoopGeneration = GetPrivateField<int>(
                        window,
                        "_compareLoopRestartGeneration");

                    await operationGate.WaitAsync();
                    operationGateHeld = true;
                    var queuedStart = InvokePrivateTask(
                        window,
                        "StartAllPanePlaybackAsync",
                        Type.EmptyTypes);

                    Assert.NotEqual(
                        stalePrimaryLoopGeneration,
                        GetPrivateField<int>(window, "_primaryLoopRestartGeneration"));
                    Assert.NotEqual(
                        staleCompareLoopGeneration,
                        GetPrivateField<int>(window, "_compareLoopRestartGeneration"));
                    Assert.False((bool)InvokePrivate(
                        window,
                        "CanContinueLoopRestart",
                        primaryPane,
                        primaryEngine,
                        stalePrimaryLoopGeneration));
                    Assert.False((bool)InvokePrivate(
                        window,
                        "CanContinueLoopRestart",
                        comparePane,
                        compareEngine,
                        staleCompareLoopGeneration));
                    Assert.False(queuedStart.IsCompleted);

                    operationGate.Release();
                    operationGateHeld = false;
                    await queuedStart;

                    Assert.Equal(1, primaryEngine.PlayCallCount);
                    Assert.Equal(1, compareEngine.PlayCallCount);
                    Assert.True(primaryEngine.IsPlaying);
                    Assert.True(compareEngine.IsPlaying);
                }
                finally
                {
                    if (operationGateHeld)
                    {
                        operationGate.Release();
                    }

                    window.Close();
                }
            });
        }

        [Fact]
        public async Task ComparePlayback_NewerPauseCancelsQueuedAllPaneStart()
        {
            await _fixture.RunAsync(async () =>
            {
                var window = new MainWindow();
                var operationGate = GetPrivateField<SemaphoreSlim>(window, "_allPaneTransportOperationGate")
                    ?? throw new InvalidOperationException("Missing all-pane transport operation gate.");
                var operationGateHeld = false;
                try
                {
                    var primaryEngine = new TestVideoReviewEngine { IsMediaOpen = true };
                    var compareEngine = new TestVideoReviewEngine { IsMediaOpen = true };
                    SetPrivateField(window, "_primaryEngine", primaryEngine);
                    SetPrivateField(window, "_compareEngine", compareEngine);
                    SetPrivateField(window, "_isCompareModeSelected", true);
                    SetPrivateField(window, "_isAllPaneTransportSelected", true);

                    await operationGate.WaitAsync();
                    operationGateHeld = true;
                    var queuedStart = InvokePrivateTask(window, "StartAllPanePlaybackAsync", Type.EmptyTypes);
                    var queuedPause = InvokePrivateTask(
                        window,
                        "PauseAllPanePlaybackAsync",
                        new[] { typeof(bool) },
                        true);
                    Assert.False(queuedPause.IsCompleted);

                    operationGate.Release();
                    operationGateHeld = false;
                    await Task.WhenAll(queuedStart, queuedPause);

                    Assert.Equal(0, primaryEngine.PlayCallCount);
                    Assert.Equal(0, compareEngine.PlayCallCount);
                    Assert.False(primaryEngine.IsPlaying);
                    Assert.False(compareEngine.IsPlaying);
                    Assert.False(GetPrivateField<bool>(window, "_isSynchronizedFramePresentationActive"));
                }
                finally
                {
                    if (operationGateHeld)
                    {
                        operationGate.Release();
                    }

                    window.Close();
                }
            });
        }

        [Fact]
        public async Task ComparePlayback_NewerPauseWaitsForDelayedSeekAndPreventsResume()
        {
            await _fixture.RunAsync(async () =>
            {
                var window = new MainWindow();
                var seekCompletion = new TaskCompletionSource<bool>(
                    TaskCreationOptions.RunContinuationsAsynchronously);
                try
                {
                    var primaryEngine = new TestVideoReviewEngine
                    {
                        IsMediaOpen = true,
                        SeekTimeCompletion = seekCompletion
                    };
                    var compareEngine = new TestVideoReviewEngine
                    {
                        IsMediaOpen = true,
                        SeekTimeCompletion = seekCompletion
                    };
                    SetPrivateField(window, "_primaryEngine", primaryEngine);
                    SetPrivateField(window, "_compareEngine", compareEngine);
                    RequireControl<CheckBox>(window, "CompareModeCheckBox").IsChecked = true;
                    primaryEngine.IsPlaying = true;
                    compareEngine.IsPlaying = true;

                    var delayedSeek = InvokePrivateTask(
                        window,
                        "SeekAllPaneToTimesPreservingPlaybackAsync",
                        new[] { typeof(TimeSpan), typeof(TimeSpan), typeof(CancellationToken) },
                        TimeSpan.FromSeconds(1),
                        TimeSpan.FromSeconds(1),
                        CancellationToken.None);
                    Assert.True(
                        SpinWait.SpinUntil(
                            () => primaryEngine.SeekToTimeCallCount == 1 &&
                                compareEngine.SeekToTimeCallCount == 1,
                            TimeSpan.FromSeconds(2)),
                        "The delayed shared seek did not reach both engines.");
                    Assert.Equal(1, primaryEngine.PauseCallCount);
                    Assert.Equal(1, compareEngine.PauseCallCount);

                    var newerPause = InvokePrivateTask(
                        window,
                        "PausePlaybackAsync",
                        new[] { typeof(bool), typeof(SynchronizedOperationScope?) },
                        true,
                        (SynchronizedOperationScope?)SynchronizedOperationScope.AllPanes);
                    await Task.Delay(TimeSpan.FromMilliseconds(25));

                    Assert.False(newerPause.IsCompleted);
                    Assert.Equal(1, primaryEngine.PauseCallCount);
                    Assert.Equal(1, compareEngine.PauseCallCount);

                    seekCompletion.TrySetResult(true);
                    await Task.WhenAll(delayedSeek, newerPause);

                    Assert.Equal(2, primaryEngine.PauseCallCount);
                    Assert.Equal(2, compareEngine.PauseCallCount);
                    Assert.Equal(0, primaryEngine.PlayCallCount);
                    Assert.Equal(0, compareEngine.PlayCallCount);
                    Assert.False(primaryEngine.WasPausedWithActiveOperation);
                    Assert.False(compareEngine.WasPausedWithActiveOperation);
                    Assert.False(primaryEngine.IsPlaying);
                    Assert.False(compareEngine.IsPlaying);
                }
                finally
                {
                    seekCompletion.TrySetResult(true);
                    window.Close();
                }
            });
        }

        [Fact]
        public async Task ComparePlayback_NewerSharedStepCancelsDelayedSeekResume()
        {
            await _fixture.RunAsync(async () =>
            {
                var window = new MainWindow();
                var seekCompletion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                try
                {
                    var primaryEngine = new TestVideoReviewEngine
                    {
                        IsMediaOpen = true,
                        SeekTimeCompletion = seekCompletion
                    };
                    var compareEngine = new TestVideoReviewEngine
                    {
                        IsMediaOpen = true,
                        SeekTimeCompletion = seekCompletion
                    };
                    SetPrivateField(window, "_primaryEngine", primaryEngine);
                    SetPrivateField(window, "_compareEngine", compareEngine);
                    RequireControl<CheckBox>(window, "CompareModeCheckBox").IsChecked = true;
                    primaryEngine.IsPlaying = true;
                    compareEngine.IsPlaying = true;

                    var delayedSeek = InvokePrivateTask(
                        window,
                        "SeekAllPaneToTimesPreservingPlaybackAsync",
                        new[] { typeof(TimeSpan), typeof(TimeSpan), typeof(CancellationToken) },
                        TimeSpan.FromSeconds(1),
                        TimeSpan.FromSeconds(1),
                        CancellationToken.None);
                    Assert.True(
                        SpinWait.SpinUntil(
                            () => primaryEngine.SeekToTimeCallCount == 1 &&
                                compareEngine.SeekToTimeCallCount == 1,
                            TimeSpan.FromSeconds(2)),
                        "The delayed shared seek did not reach both engines.");

                    primaryEngine.SeekTimeCompletion = null;
                    compareEngine.SeekTimeCompletion = null;
                    var newerStep = InvokePrivateTask(
                        window,
                        "StepFrameAsync",
                        new[] { typeof(int) },
                        1);
                    seekCompletion.TrySetResult(true);
                    await Task.WhenAll(delayedSeek, newerStep);

                    Assert.Equal(0, primaryEngine.PlayCallCount);
                    Assert.Equal(0, compareEngine.PlayCallCount);
                    Assert.Equal(1, primaryEngine.StepForwardCallCount);
                    Assert.Equal(1, compareEngine.StepForwardCallCount);
                    Assert.False(primaryEngine.IsPlaying);
                    Assert.False(compareEngine.IsPlaying);
                    Assert.True(GetPrivateField<bool>(window, "_isSynchronizedFramePresentationActive"));
                }
                finally
                {
                    seekCompletion.TrySetResult(true);
                    window.Close();
                }
            });
        }

        [Fact]
        public async Task ComparePlayback_NewerFocusedPlayRunsAfterDelayedSharedSeek()
        {
            await _fixture.RunAsync(async () =>
            {
                var window = new MainWindow();
                var seekCompletion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                try
                {
                    var primaryEngine = new TestVideoReviewEngine
                    {
                        IsMediaOpen = true,
                        IsPlaying = true,
                        SeekTimeCompletion = seekCompletion
                    };
                    var compareEngine = new TestVideoReviewEngine
                    {
                        IsMediaOpen = true,
                        IsPlaying = true,
                        SeekTimeCompletion = seekCompletion
                    };
                    SetPrivateField(window, "_primaryEngine", primaryEngine);
                    SetPrivateField(window, "_compareEngine", compareEngine);

                    var delayedSeek = InvokePrivateTask(
                        window,
                        "SeekAllPaneToTimesPreservingPlaybackAsync",
                        new[] { typeof(TimeSpan), typeof(TimeSpan), typeof(CancellationToken) },
                        TimeSpan.FromSeconds(1),
                        TimeSpan.FromSeconds(1),
                        CancellationToken.None);
                    Assert.True(
                        SpinWait.SpinUntil(
                            () => primaryEngine.SeekToTimeCallCount == 1 &&
                                compareEngine.SeekToTimeCallCount == 1,
                            TimeSpan.FromSeconds(2)),
                        "The delayed shared seek did not reach both engines.");

                    primaryEngine.SeekTimeCompletion = null;
                    compareEngine.SeekTimeCompletion = null;
                    var newerFocusedPlay = InvokePrivateTask(
                        window,
                        "StartPlaybackAsync",
                        new[] { typeof(SynchronizedOperationScope?), typeof(string) },
                        (SynchronizedOperationScope?)SynchronizedOperationScope.FocusedPane,
                        null!);
                    seekCompletion.TrySetResult(true);
                    await Task.WhenAll(delayedSeek, newerFocusedPlay);

                    Assert.Equal(1, primaryEngine.PlayCallCount);
                    Assert.Equal(0, compareEngine.PlayCallCount);
                    Assert.True(primaryEngine.IsPlaying);
                    Assert.False(compareEngine.IsPlaying);
                    Assert.False(GetPrivateField<bool>(window, "_isSynchronizedFramePresentationActive"));
                }
                finally
                {
                    seekCompletion.TrySetResult(true);
                    window.Close();
                }
            });
        }

        [Fact]
        public async Task ComparePlayback_NewerLeftPanePlayDoesNotResumeRightAfterDelayedSharedSeek()
        {
            await _fixture.RunAsync(async () =>
            {
                var window = new MainWindow();
                var seekCompletion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                try
                {
                    var primaryEngine = new TestVideoReviewEngine
                    {
                        IsMediaOpen = true,
                        IsPlaying = true,
                        SeekTimeCompletion = seekCompletion
                    };
                    var compareEngine = new TestVideoReviewEngine
                    {
                        IsMediaOpen = true,
                        IsPlaying = true,
                        SeekTimeCompletion = seekCompletion
                    };
                    SetPrivateField(window, "_primaryEngine", primaryEngine);
                    SetPrivateField(window, "_compareEngine", compareEngine);
                    RequireControl<CheckBox>(window, "CompareModeCheckBox").IsChecked = true;

                    var delayedSeek = InvokePrivateTask(
                        window,
                        "SeekAllPaneToTimesPreservingPlaybackAsync",
                        new[] { typeof(TimeSpan), typeof(TimeSpan), typeof(CancellationToken) },
                        TimeSpan.FromSeconds(1),
                        TimeSpan.FromSeconds(1),
                        CancellationToken.None);
                    Assert.True(
                        SpinWait.SpinUntil(
                            () => primaryEngine.SeekToTimeCallCount == 1 &&
                                compareEngine.SeekToTimeCallCount == 1,
                            TimeSpan.FromSeconds(2)),
                        "The delayed shared seek did not reach both engines.");
                    var masterIntentBefore = GetPrivateField<int>(
                        window,
                        "_allPaneTransportIntentGeneration");

                    primaryEngine.SeekTimeCompletion = null;
                    compareEngine.SeekTimeCompletion = null;
                    var newerLeftPanePlay = InvokePrivateTask(
                        window,
                        "TogglePanePlaybackAsync",
                        new[] { ParsePane("Primary").GetType() },
                        ParsePane("Primary"));
                    seekCompletion.TrySetResult(true);
                    await Task.WhenAll(delayedSeek, newerLeftPanePlay);

                    Assert.Equal(
                        masterIntentBefore,
                        GetPrivateField<int>(
                            window,
                            "_allPaneTransportIntentGeneration"));
                    Assert.Equal(1, primaryEngine.PlayCallCount);
                    Assert.Equal(0, compareEngine.PlayCallCount);
                    Assert.True(primaryEngine.IsPlaying);
                    Assert.False(compareEngine.IsPlaying);
                    Assert.False(GetPrivateField<bool>(window, "_isSynchronizedFramePresentationActive"));
                }
                finally
                {
                    seekCompletion.TrySetResult(true);
                    window.Close();
                }
            });
        }

        [Fact]
        public async Task ComparePlayback_LeftPanePlaySupersedesSharedSeekWhileResumeWaitsForStartGate()
        {
            await _fixture.RunAsync(async () =>
            {
                var window = new MainWindow();
                var seekCompletion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                var playbackStartGate = GetPrivateField<SemaphoreSlim>(
                    window,
                    "_playbackStartGate") ??
                    throw new InvalidOperationException("Missing playback start gate.");
                var playbackStartGateHeld = false;
                try
                {
                    var primaryEngine = new TestVideoReviewEngine
                    {
                        IsMediaOpen = true,
                        IsPlaying = true,
                        SeekTimeCompletion = seekCompletion
                    };
                    var compareEngine = new TestVideoReviewEngine
                    {
                        IsMediaOpen = true,
                        IsPlaying = true,
                        SeekTimeCompletion = seekCompletion
                    };
                    SetPrivateField(window, "_primaryEngine", primaryEngine);
                    SetPrivateField(window, "_compareEngine", compareEngine);
                    RequireControl<CheckBox>(window, "CompareModeCheckBox").IsChecked = true;

                    var delayedSeek = InvokePrivateTask(
                        window,
                        "SeekAllPaneToTimesPreservingPlaybackAsync",
                        new[] { typeof(TimeSpan), typeof(TimeSpan), typeof(CancellationToken) },
                        TimeSpan.FromSeconds(1),
                        TimeSpan.FromSeconds(1),
                        CancellationToken.None);
                    Assert.True(
                        SpinWait.SpinUntil(
                            () => primaryEngine.SeekToTimeCallCount == 1 &&
                                compareEngine.SeekToTimeCallCount == 1,
                            TimeSpan.FromSeconds(2)),
                        "The delayed shared seek did not reach both engines.");

                    await playbackStartGate.WaitAsync();
                    playbackStartGateHeld = true;
                    primaryEngine.SeekTimeCompletion = null;
                    compareEngine.SeekTimeCompletion = null;
                    seekCompletion.TrySetResult(true);
                    await Task.Delay(TimeSpan.FromMilliseconds(25));

                    var newerLeftPanePlay = InvokePrivateTask(
                        window,
                        "TogglePanePlaybackAsync",
                        new[] { ParsePane("Primary").GetType() },
                        ParsePane("Primary"));
                    playbackStartGate.Release();
                    playbackStartGateHeld = false;
                    await Task.WhenAll(delayedSeek, newerLeftPanePlay);

                    Assert.Equal(1, primaryEngine.PlayCallCount);
                    Assert.Equal(0, compareEngine.PlayCallCount);
                    Assert.True(primaryEngine.IsPlaying);
                    Assert.False(compareEngine.IsPlaying);
                }
                finally
                {
                    if (playbackStartGateHeld)
                    {
                        playbackStartGate.Release();
                    }

                    seekCompletion.TrySetResult(true);
                    window.Close();
                }
            });
        }

        [Fact]
        public async Task ComparePlayback_LeftPanePlaySupersedesAlignmentWhileResumeWaitsForStartGate()
        {
            await _fixture.RunAsync(async () =>
            {
                var window = new MainWindow();
                var playbackStartGate = GetPrivateField<SemaphoreSlim>(
                    window,
                    "_playbackStartGate") ??
                    throw new InvalidOperationException("Missing playback start gate.");
                var playbackStartGateHeld = false;
                try
                {
                    var mediaInfo = new VideoMediaInfo(
                        "sync-stale-resume.mp4",
                        TimeSpan.FromSeconds(20),
                        TimeSpan.FromSeconds(1d / 30d),
                        30d,
                        1920,
                        1080,
                        "h264",
                        0,
                        30,
                        1,
                        1,
                        90_000);
                    var primaryEngine = new TestVideoReviewEngine
                    {
                        IsMediaOpen = true,
                        IsPlaying = true,
                        CurrentFilePath = "sync-stale-resume.mp4",
                        MediaInfo = mediaInfo,
                        StopPlayingOnSeek = true
                    };
                    var compareEngine = new TestVideoReviewEngine
                    {
                        IsMediaOpen = true,
                        IsPlaying = true,
                        CurrentFilePath = "sync-stale-resume.mp4",
                        MediaInfo = mediaInfo,
                        StopPlayingOnSeek = true
                    };
                    SetPrivateField(window, "_primaryEngine", primaryEngine);
                    SetPrivateField(window, "_compareEngine", compareEngine);
                    RequireControl<CheckBox>(window, "CompareModeCheckBox").IsChecked = true;
                    SetPrivateField(window, "_isCompareModeSelected", true);

                    var target = TimeSpan.FromSeconds(4);
                    using var primaryFrame = CreateFrameBuffer(8, 4, target);
                    using var compareFrame = CreateFrameBuffer(8, 4, target);
                    InvokePrivate(window, "SetPaneBitmap", ParsePane("Primary"), primaryFrame);
                    InvokePrivate(window, "SetPaneBitmap", ParsePane("Compare"), compareFrame);
                    primaryEngine.FrameSought = frameIndex =>
                    {
                        using var frame = CreateFrameBuffer(
                            8,
                            4,
                            TimeSpan.FromSeconds(frameIndex / 30d));
                        InvokePrivate(
                            window,
                            "PrimaryEngine_FramePresented",
                            null!,
                            new FramePresentedEventArgs(frame));
                    };
                    compareEngine.FrameSought = frameIndex =>
                    {
                        using var frame = CreateFrameBuffer(
                            8,
                            4,
                            TimeSpan.FromSeconds(frameIndex / 30d));
                        InvokePrivate(
                            window,
                            "CompareEngine_FramePresented",
                            null!,
                            new FramePresentedEventArgs(frame));
                    };

                    await playbackStartGate.WaitAsync();
                    playbackStartGateHeld = true;
                    var alignment = (Task<bool>)InvokePrivate(
                        window,
                        "AlignPaneToPaneAsync",
                        ParsePane("Primary"),
                        ParsePane("Compare"));
                    Assert.True(
                        SpinWait.SpinUntil(
                            () => primaryEngine.SeekToFrameCallCount == 1 &&
                                compareEngine.SeekToFrameCallCount == 1,
                            TimeSpan.FromSeconds(2)),
                        "The alignment did not reach both engines.");
                    await Task.Delay(TimeSpan.FromMilliseconds(50));

                    var newerLeftPanePlay = InvokePrivateTask(
                        window,
                        "TogglePanePlaybackAsync",
                        new[] { ParsePane("Primary").GetType() },
                        ParsePane("Primary"));
                    await Task.Delay(TimeSpan.FromMilliseconds(25));
                    playbackStartGate.Release();
                    playbackStartGateHeld = false;
                    Assert.False(await alignment);
                    await newerLeftPanePlay;

                    Assert.Equal(1, primaryEngine.PlayCallCount);
                    Assert.Equal(0, compareEngine.PlayCallCount);
                    Assert.True(primaryEngine.IsPlaying);
                    Assert.False(compareEngine.IsPlaying);
                    Assert.False(GetPrivateField<bool>(
                        window,
                        "_isSynchronizedFramePresentationActive"));
                }
                finally
                {
                    if (playbackStartGateHeld)
                    {
                        playbackStartGate.Release();
                    }

                    window.Close();
                }
            });
        }

        [Fact]
        public async Task ComparePlayback_NewerFocusedSeeksRunAfterDelayedSharedSeek()
        {
            await _fixture.RunAsync(async () =>
            {
                var window = new MainWindow();
                var firstSeekCompletion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                var secondSeekCompletion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                try
                {
                    var mediaInfo = new VideoMediaInfo(
                        "shared.mp4",
                        TimeSpan.FromSeconds(10),
                        TimeSpan.FromSeconds(1d / 30d),
                        30d,
                        1920,
                        1080,
                        "h264",
                        0,
                        30,
                        1,
                        1,
                        90_000);
                    var primaryEngine = new TestVideoReviewEngine
                    {
                        IsMediaOpen = true,
                        IsPlaying = true,
                        CurrentFilePath = "left.mp4",
                        MediaInfo = mediaInfo,
                        Position = new ReviewPosition(TimeSpan.Zero, 0, true, true, 0, 0),
                        SeekTimeCompletion = firstSeekCompletion
                    };
                    var compareEngine = new TestVideoReviewEngine
                    {
                        IsMediaOpen = true,
                        IsPlaying = true,
                        CurrentFilePath = "right.mp4",
                        MediaInfo = mediaInfo,
                        Position = new ReviewPosition(TimeSpan.Zero, 0, true, true, 0, 0),
                        SeekTimeCompletion = firstSeekCompletion
                    };
                    SetPrivateField(window, "_primaryEngine", primaryEngine);
                    SetPrivateField(window, "_compareEngine", compareEngine);
                    SetPrivateField(window, "_primaryLoopRange", CreateLoopRange("pane-primary", "left.mp4"));

                    var delayedSeek = InvokePrivateTask(
                        window,
                        "SeekAllPaneToTimesPreservingPlaybackAsync",
                        new[] { typeof(TimeSpan), typeof(TimeSpan), typeof(CancellationToken) },
                        TimeSpan.FromSeconds(1),
                        TimeSpan.FromSeconds(1),
                        CancellationToken.None);
                    Assert.True(
                        SpinWait.SpinUntil(
                            () => primaryEngine.SeekToTimeCallCount == 1 &&
                                compareEngine.SeekToTimeCallCount == 1,
                            TimeSpan.FromSeconds(2)),
                        "The first delayed shared seek did not reach both engines.");

                    primaryEngine.SeekTimeCompletion = null;
                    compareEngine.SeekTimeCompletion = null;
                    var focusedFrameSeek = (Task<bool>)InvokePrivate(
                        window,
                        "SeekPaneToFrameAsync",
                        ParsePane("Primary"),
                        42L,
                        CancellationToken.None);
                    firstSeekCompletion.TrySetResult(true);
                    await Task.WhenAll(delayedSeek, focusedFrameSeek);

                    Assert.True(await focusedFrameSeek);
                    Assert.Equal(1, primaryEngine.SeekToFrameCallCount);
                    Assert.Equal(0, compareEngine.SeekToFrameCallCount);
                    Assert.Equal(42L, primaryEngine.Position.FrameIndex);

                    primaryEngine.IsPlaying = true;
                    compareEngine.IsPlaying = true;
                    primaryEngine.SeekTimeCompletion = secondSeekCompletion;
                    compareEngine.SeekTimeCompletion = secondSeekCompletion;
                    var secondDelayedSeek = InvokePrivateTask(
                        window,
                        "SeekAllPaneToTimesPreservingPlaybackAsync",
                        new[] { typeof(TimeSpan), typeof(TimeSpan), typeof(CancellationToken) },
                        TimeSpan.FromSeconds(2),
                        TimeSpan.FromSeconds(2),
                        CancellationToken.None);
                    Assert.True(
                        SpinWait.SpinUntil(
                            () => primaryEngine.SeekToTimeCallCount == 2 &&
                                compareEngine.SeekToTimeCallCount == 2,
                            TimeSpan.FromSeconds(2)),
                        "The second delayed shared seek did not reach both engines.");

                    primaryEngine.SeekTimeCompletion = null;
                    compareEngine.SeekTimeCompletion = null;
                    var loopMarkerSeek = (Task<bool>)InvokePrivate(
                        window,
                        "SetTimelineLoopMarkerAtAsync",
                        "pane-primary",
                        LoopPlaybackMarkerEndpoint.In,
                        TimeSpan.FromMilliseconds(500));
                    secondSeekCompletion.TrySetResult(true);
                    await Task.WhenAll(secondDelayedSeek, loopMarkerSeek);

                    Assert.True(await loopMarkerSeek);
                    Assert.Equal(3, primaryEngine.SeekToTimeCallCount);
                    Assert.Equal(TimeSpan.FromMilliseconds(500), primaryEngine.Position.PresentationTime);
                    Assert.False(GetPrivateField<bool>(window, "_isSynchronizedFramePresentationActive"));
                }
                finally
                {
                    firstSeekCompletion.TrySetResult(true);
                    secondSeekCompletion.TrySetResult(true);
                    window.Close();
                }
            });
        }

        [Fact]
        public async Task ComparePlayback_RightFocusedSeekDoesNotCancelDelayedLeftSeekResume()
        {
            await _fixture.RunAsync(async () =>
            {
                var window = new MainWindow();
                var leftSeekCompletion = new TaskCompletionSource<bool>(
                    TaskCreationOptions.RunContinuationsAsynchronously);
                try
                {
                    var primaryEngine = new TestVideoReviewEngine
                    {
                        IsMediaOpen = true,
                        IsPlaying = true,
                        SeekTimeCompletion = leftSeekCompletion
                    };
                    var compareEngine = new TestVideoReviewEngine
                    {
                        IsMediaOpen = true,
                        IsPlaying = true
                    };
                    SetPrivateField(window, "_primaryEngine", primaryEngine);
                    SetPrivateField(window, "_compareEngine", compareEngine);

                    var delayedLeftSeek = (Task)InvokePrivate(
                        window,
                        "SeekPaneToTimePreservingPlaybackAsync",
                        ParsePane("Primary"),
                        TimeSpan.FromSeconds(1),
                        CancellationToken.None);
                    Assert.True(
                        SpinWait.SpinUntil(
                            () => primaryEngine.SeekToTimeCallCount == 1,
                            TimeSpan.FromSeconds(2)),
                        "The delayed left-pane seek did not reach its engine.");

                    var newerRightSeek = (Task)InvokePrivate(
                        window,
                        "SeekPaneToTimePreservingPlaybackAsync",
                        ParsePane("Compare"),
                        TimeSpan.FromSeconds(2),
                        CancellationToken.None);
                    await newerRightSeek;

                    Assert.Equal(1, compareEngine.SeekToTimeCallCount);
                    Assert.Equal(1, compareEngine.PlayCallCount);
                    Assert.True(compareEngine.IsPlaying);

                    leftSeekCompletion.TrySetResult(true);
                    await delayedLeftSeek;

                    Assert.Equal(1, primaryEngine.PlayCallCount);
                    Assert.True(primaryEngine.IsPlaying);
                    Assert.Equal(TimeSpan.FromSeconds(1), primaryEngine.Position.PresentationTime);
                    Assert.Equal(TimeSpan.FromSeconds(2), compareEngine.Position.PresentationTime);
                }
                finally
                {
                    leftSeekCompletion.TrySetResult(true);
                    window.Close();
                }
            });
        }

        [Fact]
        public async Task ComparePlayback_QueuedMasterScrubPreservesPendingSharedSeekResume()
        {
            await _fixture.RunAsync(async () =>
            {
                var window = new MainWindow();
                var firstSeekCompletion = new TaskCompletionSource<bool>(
                    TaskCreationOptions.RunContinuationsAsynchronously);
                try
                {
                    var primaryEngine = new TestVideoReviewEngine { IsMediaOpen = true };
                    var compareEngine = new TestVideoReviewEngine { IsMediaOpen = true };
                    SetPrivateField(window, "_primaryEngine", primaryEngine);
                    SetPrivateField(window, "_compareEngine", compareEngine);
                    RequireControl<CheckBox>(window, "CompareModeCheckBox").IsChecked = true;

                    primaryEngine.IsPlaying = true;
                    compareEngine.IsPlaying = true;
                    primaryEngine.SeekTimeCompletion = firstSeekCompletion;
                    compareEngine.SeekTimeCompletion = firstSeekCompletion;
                    var firstSeek = InvokePrivateTask(
                        window,
                        "SeekAllPaneToTimesPreservingPlaybackAsync",
                        new[] { typeof(TimeSpan), typeof(TimeSpan), typeof(CancellationToken) },
                        TimeSpan.FromSeconds(1),
                        TimeSpan.FromSeconds(1),
                        CancellationToken.None);
                    Assert.True(
                        SpinWait.SpinUntil(
                            () => primaryEngine.SeekToTimeCallCount == 1 &&
                                compareEngine.SeekToTimeCallCount == 1,
                            TimeSpan.FromSeconds(2)),
                        "The delayed shared seek did not reach both engines.");
                    Assert.False(primaryEngine.IsPlaying);
                    Assert.False(compareEngine.IsPlaying);

                    InvokePrivate(window, "QueueSliderScrub", TimeSpan.FromSeconds(2));
                    primaryEngine.SeekTimeCompletion = null;
                    compareEngine.SeekTimeCompletion = null;
                    var replacementSeek = InvokePrivateTask(
                        window,
                        "CommitSliderSeekAsync",
                        new[] { typeof(string), typeof(TimeSpan) },
                        "test",
                        TimeSpan.FromSeconds(2));

                    firstSeekCompletion.TrySetResult(true);
                    await Task.WhenAll(firstSeek, replacementSeek);

                    Assert.Equal(1, primaryEngine.PlayCallCount);
                    Assert.Equal(1, compareEngine.PlayCallCount);
                    Assert.True(primaryEngine.IsPlaying);
                    Assert.True(compareEngine.IsPlaying);
                    Assert.Equal(TimeSpan.FromSeconds(2), primaryEngine.Position.PresentationTime);
                    Assert.Equal(TimeSpan.FromSeconds(2), compareEngine.Position.PresentationTime);
                }
                finally
                {
                    firstSeekCompletion.TrySetResult(true);
                    window.Close();
                }
            });
        }

        [Fact]
        public async Task ComparePlayback_RepeatedMasterScrubWhilePlayingResumesBothPanesAtFinalTarget()
        {
            await _fixture.RunAsync(async () =>
            {
                var window = new MainWindow();
                try
                {
                    var primaryEngine = new TestVideoReviewEngine
                    {
                        IsMediaOpen = true,
                        IsPlaying = true
                    };
                    var compareEngine = new TestVideoReviewEngine
                    {
                        IsMediaOpen = true,
                        IsPlaying = true
                    };
                    SetPrivateField(window, "_primaryEngine", primaryEngine);
                    SetPrivateField(window, "_compareEngine", compareEngine);
                    RequireControl<CheckBox>(window, "CompareModeCheckBox").IsChecked = true;

                    InvokePrivate(window, "QueueSliderScrub", TimeSpan.FromSeconds(4));
                    InvokePrivate(window, "QueueSliderScrub", TimeSpan.FromSeconds(12));
                    InvokePrivate(window, "QueueSliderScrub", TimeSpan.FromSeconds(6));
                    GetPrivateField<DispatcherTimer>(window, "_sliderScrubTimer")!.Stop();
                    InvokePrivate(
                        window,
                        "SliderScrubTimer_Tick",
                        null!,
                        EventArgs.Empty);

                    for (var attempt = 0;
                         attempt < 200 &&
                         (primaryEngine.SeekToTimeCallCount != 1 ||
                             compareEngine.SeekToTimeCallCount != 1);
                         attempt++)
                    {
                        await Task.Delay(TimeSpan.FromMilliseconds(10));
                    }

                    Assert.Equal(1, primaryEngine.SeekToTimeCallCount);
                    Assert.Equal(1, compareEngine.SeekToTimeCallCount);

                    for (var attempt = 0;
                         attempt < 200 &&
                         GetPrivateField<bool>(
                             window,
                             "_isSliderScrubSeekInFlight");
                         attempt++)
                    {
                        await Task.Delay(TimeSpan.FromMilliseconds(10));
                    }

                    Assert.False(GetPrivateField<bool>(window, "_isSliderScrubSeekInFlight"));

                    Assert.True(primaryEngine.IsPlaying);
                    Assert.True(compareEngine.IsPlaying);
                    Assert.Equal(1, primaryEngine.PlayCallCount);
                    Assert.Equal(1, compareEngine.PlayCallCount);
                    Assert.Equal(TimeSpan.FromSeconds(6), primaryEngine.Position.PresentationTime);
                    Assert.Equal(TimeSpan.FromSeconds(6), compareEngine.Position.PresentationTime);
                }
                finally
                {
                    window.Close();
                }
            });
        }

        [Fact]
        public async Task ComparePlayback_MasterScrubDoesNotCommitAdjacentFramesForIdenticalMedia()
        {
            await _fixture.RunAsync(async () =>
            {
                var window = new MainWindow();
                try
                {
                    var frameStep = TimeSpan.FromSeconds(1d / 30d);
                    var mediaInfo = new VideoMediaInfo(
                        "same-media.mp4",
                        TimeSpan.FromSeconds(20),
                        frameStep,
                        30d,
                        1920,
                        1080,
                        "h264",
                        0,
                        30,
                        1,
                        1,
                        90_000);
                    var primaryEngine = new TestVideoReviewEngine
                    {
                        IsMediaOpen = true,
                        IsPlaying = true,
                        CurrentFilePath = "same-media.mp4",
                        MediaInfo = mediaInfo
                    };
                    var compareEngine = new TestVideoReviewEngine
                    {
                        IsMediaOpen = true,
                        IsPlaying = true,
                        CurrentFilePath = "same-media.mp4",
                        MediaInfo = mediaInfo
                    };
                    SetPrivateField(window, "_primaryEngine", primaryEngine);
                    SetPrivateField(window, "_compareEngine", compareEngine);
                    RequireControl<CheckBox>(window, "CompareModeCheckBox").IsChecked = true;
                    using var initialPrimary = CreateFrameBuffer(8, 4, TimeSpan.Zero);
                    using var initialCompare = CreateFrameBuffer(
                        8,
                        4,
                        TimeSpan.FromSeconds(1));
                    InvokePrivate(window, "SetPaneBitmap", ParsePane("Primary"), initialPrimary);
                    InvokePrivate(window, "SetPaneBitmap", ParsePane("Compare"), initialCompare);
                    primaryEngine.TimeSought = _ =>
                    {
                        using var frame = CreateFrameBuffer(
                            8,
                            4,
                            TimeSpan.FromTicks(143L * frameStep.Ticks),
                            red: 0xC0,
                            green: 0x20,
                            blue: 0x20);
                        InvokePrivate(
                            window,
                            "PrimaryEngine_FramePresented",
                            null!,
                            new FramePresentedEventArgs(frame));
                    };
                    compareEngine.TimeSought = _ =>
                    {
                        using var frame = CreateFrameBuffer(
                            8,
                            4,
                            TimeSpan.FromTicks(144L * frameStep.Ticks),
                            red: 0x20,
                            green: 0x20,
                            blue: 0xC0);
                        InvokePrivate(
                            window,
                            "CompareEngine_FramePresented",
                            null!,
                            new FramePresentedEventArgs(frame));
                    };

                    InvokePrivate(window, "QueueSliderScrub", TimeSpan.FromSeconds(4));
                    InvokePrivate(window, "QueueSliderScrub", TimeSpan.FromSeconds(12));
                    InvokePrivate(window, "QueueSliderScrub", TimeSpan.FromSeconds(6));
                    GetPrivateField<DispatcherTimer>(window, "_sliderScrubTimer")!.Stop();
                    InvokePrivate(
                        window,
                        "SliderScrubTimer_Tick",
                        null!,
                        EventArgs.Empty);

                    for (var attempt = 0;
                         attempt < 200 &&
                         GetPrivateField<bool>(window, "_isSliderScrubSeekInFlight");
                         attempt++)
                    {
                        await Task.Delay(TimeSpan.FromMilliseconds(10));
                    }

                    Assert.False(GetPrivateField<bool>(window, "_isSliderScrubSeekInFlight"));
                    var afterAdjacentPrimary = GetPrivateField<DecodedFrameBuffer>(
                        window,
                        "_primaryFrameBuffer")!.Descriptor;
                    var afterAdjacentCompare = GetPrivateField<DecodedFrameBuffer>(
                        window,
                        "_compareFrameBuffer")!.Descriptor;
                    Assert.Equal(0L, afterAdjacentPrimary.FrameIndex);
                    Assert.Equal(30L, afterAdjacentCompare.FrameIndex);
                    Assert.Equal(TimeSpan.Zero, GetPrivateField<TimeSpan>(
                        window,
                        "_synchronizedFramePresentationTimeOffset"));

                    using var correctedPrimary = CreateFrameBuffer(
                        8,
                        4,
                        TimeSpan.FromTicks(144L * frameStep.Ticks),
                        red: 0x20,
                        green: 0xC0,
                        blue: 0x20);
                    using var correctedCompare = CreateFrameBuffer(
                        8,
                        4,
                        TimeSpan.FromTicks(144L * frameStep.Ticks),
                        red: 0x20,
                        green: 0xC0,
                        blue: 0x20);
                    InvokePrivate(
                        window,
                        "PrimaryEngine_FramePresented",
                        null!,
                        new FramePresentedEventArgs(correctedPrimary));
                    InvokePrivate(
                        window,
                        "CompareEngine_FramePresented",
                        null!,
                        new FramePresentedEventArgs(correctedCompare));
                    InvokePrivate(
                        window,
                        "PresentPendingSynchronizedFrames",
                        GetPrivateField<long>(
                            window,
                            "_synchronizedFramePresentationGeneration"));

                    var correctedPrimaryDescriptor = GetPrivateField<DecodedFrameBuffer>(
                        window,
                        "_primaryFrameBuffer")!.Descriptor;
                    var correctedCompareDescriptor = GetPrivateField<DecodedFrameBuffer>(
                        window,
                        "_compareFrameBuffer")!.Descriptor;
                    Assert.Equal(144L, correctedPrimaryDescriptor.FrameIndex);
                    Assert.Equal(
                        correctedPrimaryDescriptor.FrameIndex,
                        correctedCompareDescriptor.FrameIndex);
                    Assert.Equal(
                        correctedPrimaryDescriptor.PresentationTime,
                        correctedCompareDescriptor.PresentationTime);
                    Assert.Equal("145", RequireControl<TextBox>(
                        window,
                        "PrimaryPaneFrameNumberTextBox").Text);
                    Assert.Equal("145", RequireControl<TextBox>(
                        window,
                        "ComparePaneFrameNumberTextBox").Text);
                    Assert.Equal("145", RequireControl<TextBox>(
                        window,
                        "FrameNumberTextBox").Text);
                }
                finally
                {
                    window.Close();
                }
            });
        }

        [Fact]
        public void ComparePlayback_QueuedMasterScrubKeepsLastPairLockedUntilSharedSeekStarts()
        {
            _fixture.Run(() =>
            {
                var window = new MainWindow();
                try
                {
                    var frameStep = TimeSpan.FromSeconds(1d / 30d);
                    var mediaInfo = new VideoMediaInfo(
                        "same-media.mp4",
                        TimeSpan.FromSeconds(20),
                        frameStep,
                        30d,
                        1920,
                        1080,
                        "h264",
                        0,
                        30,
                        1,
                        1,
                        90_000);
                    var primaryEngine = new TestVideoReviewEngine
                    {
                        IsMediaOpen = true,
                        IsPlaying = true,
                        CurrentFilePath = "same-media.mp4",
                        MediaInfo = mediaInfo
                    };
                    var compareEngine = new TestVideoReviewEngine
                    {
                        IsMediaOpen = true,
                        IsPlaying = true,
                        CurrentFilePath = "same-media.mp4",
                        MediaInfo = mediaInfo
                    };
                    SetPrivateField(window, "_primaryEngine", primaryEngine);
                    SetPrivateField(window, "_compareEngine", compareEngine);
                    RequireControl<CheckBox>(window, "CompareModeCheckBox").IsChecked = true;
                    using var initialPrimary = CreateFrameBuffer(
                        8,
                        4,
                        TimeSpan.FromTicks(100L * frameStep.Ticks));
                    using var initialCompare = CreateFrameBuffer(
                        8,
                        4,
                        TimeSpan.FromTicks(100L * frameStep.Ticks));
                    InvokePrivate(window, "SetPaneBitmap", ParsePane("Primary"), initialPrimary);
                    InvokePrivate(window, "SetPaneBitmap", ParsePane("Compare"), initialCompare);
                    InvokePrivate(
                        window,
                        "TryBeginSynchronizedFramePresentation",
                        GetPrivateField<int>(window, "_allPaneTransportIntentGeneration"));

                    InvokePrivate(window, "QueueSliderScrub", TimeSpan.FromSeconds(4));
                    using var primaryAdvance = CreateFrameBuffer(
                        8,
                        4,
                        TimeSpan.FromTicks(101L * frameStep.Ticks));
                    InvokePrivate(
                        window,
                        "PrimaryEngine_FramePresented",
                        null!,
                        new FramePresentedEventArgs(primaryAdvance));
                    InvokePrivate(window, "PresentPendingFrame", ParsePane("Primary"));

                    Assert.True(GetPrivateField<bool>(
                        window,
                        "_isSynchronizedFramePresentationActive"));
                    Assert.Equal(
                        100L,
                        GetPrivateField<DecodedFrameBuffer>(
                            window,
                            "_primaryFrameBuffer")!.Descriptor.FrameIndex);
                    Assert.Equal(
                        100L,
                        GetPrivateField<DecodedFrameBuffer>(
                            window,
                            "_compareFrameBuffer")!.Descriptor.FrameIndex);
                }
                finally
                {
                    window.Close();
                }
            });
        }

        [Fact]
        public async Task ComparePlayback_ScrubQueuedDuringInFlightMasterSeekResumesBothPanesAtFinalTarget()
        {
            await _fixture.RunAsync(async () =>
            {
                var window = new MainWindow();
                var firstSeekCompletion = new TaskCompletionSource<bool>(
                    TaskCreationOptions.RunContinuationsAsynchronously);
                try
                {
                    var primaryEngine = new TestVideoReviewEngine
                    {
                        IsMediaOpen = true,
                        IsPlaying = true,
                        SeekTimeCompletion = firstSeekCompletion
                    };
                    var compareEngine = new TestVideoReviewEngine
                    {
                        IsMediaOpen = true,
                        IsPlaying = true,
                        SeekTimeCompletion = firstSeekCompletion
                    };
                    SetPrivateField(window, "_primaryEngine", primaryEngine);
                    SetPrivateField(window, "_compareEngine", compareEngine);
                    RequireControl<CheckBox>(window, "CompareModeCheckBox").IsChecked = true;

                    InvokePrivate(window, "QueueSliderScrub", TimeSpan.FromSeconds(4));
                    GetPrivateField<DispatcherTimer>(window, "_sliderScrubTimer")!.Stop();
                    InvokePrivate(
                        window,
                        "SliderScrubTimer_Tick",
                        null!,
                        EventArgs.Empty);

                    for (var attempt = 0;
                         attempt < 200 &&
                         (primaryEngine.SeekToTimeCallCount != 1 ||
                             compareEngine.SeekToTimeCallCount != 1);
                         attempt++)
                    {
                        await Task.Delay(TimeSpan.FromMilliseconds(10));
                    }

                    Assert.Equal(1, primaryEngine.SeekToTimeCallCount);
                    Assert.Equal(1, compareEngine.SeekToTimeCallCount);
                    Assert.False(primaryEngine.IsPlaying);
                    Assert.False(compareEngine.IsPlaying);

                    InvokePrivate(window, "QueueSliderScrub", TimeSpan.FromSeconds(12));
                    InvokePrivate(window, "QueueSliderScrub", TimeSpan.FromSeconds(6));
                    primaryEngine.SeekTimeCompletion = null;
                    compareEngine.SeekTimeCompletion = null;
                    firstSeekCompletion.TrySetResult(true);

                    for (var attempt = 0;
                         attempt < 300 &&
                         (GetPrivateField<bool>(
                              window,
                              "_isSliderScrubSeekInFlight") ||
                          GetPrivateField<bool>(
                              window,
                              "_hasPendingSliderScrubTarget"));
                         attempt++)
                    {
                        await Task.Delay(TimeSpan.FromMilliseconds(10));
                    }

                    Assert.False(GetPrivateField<bool>(window, "_isSliderScrubSeekInFlight"));
                    Assert.False(GetPrivateField<bool>(window, "_hasPendingSliderScrubTarget"));
                    Assert.Equal(2, primaryEngine.SeekToTimeCallCount);
                    Assert.Equal(2, compareEngine.SeekToTimeCallCount);
                    Assert.True(primaryEngine.IsPlaying);
                    Assert.True(compareEngine.IsPlaying);
                    Assert.Equal(1, primaryEngine.PlayCallCount);
                    Assert.Equal(1, compareEngine.PlayCallCount);
                    Assert.Equal(TimeSpan.FromSeconds(6), primaryEngine.Position.PresentationTime);
                    Assert.Equal(TimeSpan.FromSeconds(6), compareEngine.Position.PresentationTime);
                }
                finally
                {
                    firstSeekCompletion.TrySetResult(true);
                    window.Close();
                }
            });
        }

        [Fact]
        public async Task ComparePlayback_CanceledPaneScrubDoesNotLeakResumeIntent()
        {
            await _fixture.RunAsync(async () =>
            {
                var window = new MainWindow();
                try
                {
                    var primaryEngine = new TestVideoReviewEngine { IsMediaOpen = true };
                    var compareEngine = new TestVideoReviewEngine
                    {
                        IsMediaOpen = true,
                        IsPlaying = true
                    };
                    SetPrivateField(window, "_primaryEngine", primaryEngine);
                    SetPrivateField(window, "_compareEngine", compareEngine);

                    InvokePrivate(
                        window,
                        "QueuePaneSliderScrub",
                        ParsePane("Compare"),
                        TimeSpan.FromSeconds(1));
                    compareEngine.IsPlaying = false;

                    var primaryPane = ParsePane("Primary");
                    await InvokePrivateTask(
                        window,
                        "StepFrameAsync",
                        new[] { typeof(int), primaryPane.GetType() },
                        1,
                        primaryPane);
                    await (Task)InvokePrivate(
                        window,
                        "SeekPaneToTimePreservingPlaybackAsync",
                        ParsePane("Compare"),
                        TimeSpan.FromSeconds(2),
                        CancellationToken.None);

                    Assert.Equal(0, compareEngine.PlayCallCount);
                    Assert.False(compareEngine.IsPlaying);
                    Assert.Equal(TimeSpan.FromSeconds(2), compareEngine.Position.PresentationTime);
                }
                finally
                {
                    window.Close();
                }
            });
        }

        [Fact]
        public async Task ComparePlayback_DelayedPaneScrubRunsQueuedReplacementOnUiDispatcher()
        {
            await _fixture.RunAsync(async () =>
            {
                var window = new MainWindow();
                var firstSeekCompletion = new TaskCompletionSource<bool>(
                    TaskCreationOptions.RunContinuationsAsynchronously);
                try
                {
                    var compareEngine = new TestVideoReviewEngine
                    {
                        IsMediaOpen = true,
                        IsPlaying = true,
                        SeekTimeCompletion = firstSeekCompletion
                    };
                    SetPrivateField(window, "_compareEngine", compareEngine);
                    var comparePane = ParsePane("Compare");

                    InvokePrivate(
                        window,
                        "QueuePaneSliderScrub",
                        comparePane,
                        TimeSpan.FromSeconds(1));
                    InvokePrivate(
                        window,
                        "PaneSliderScrubTimer_Tick",
                        null!,
                        EventArgs.Empty);
                    Assert.True(
                        SpinWait.SpinUntil(
                            () => compareEngine.SeekToTimeCallCount == 1,
                            TimeSpan.FromSeconds(2)),
                        "The first pane scrub did not reach the compare engine.");

                    InvokePrivate(
                        window,
                        "QueuePaneSliderScrub",
                        comparePane,
                        TimeSpan.FromSeconds(2));
                    compareEngine.SeekTimeCompletion = null;
                    firstSeekCompletion.TrySetResult(true);

                    var replacementDeadline = DateTime.UtcNow + TimeSpan.FromSeconds(2);
                    while (compareEngine.SeekToTimeCallCount < 2 &&
                        DateTime.UtcNow < replacementDeadline)
                    {
                        await Task.Delay(TimeSpan.FromMilliseconds(20));
                    }

                    Assert.Equal(2, compareEngine.SeekToTimeCallCount);
                    Assert.Equal(
                        TimeSpan.FromSeconds(2),
                        compareEngine.Position.PresentationTime);
                    Assert.False(GetPrivateField<bool>(
                        window,
                        "_isPaneSliderScrubSeekInFlight"));
                    Assert.False(GetPrivateField<bool>(
                        window,
                        "_hasPendingPaneSliderScrubTarget"));
                }
                finally
                {
                    firstSeekCompletion.TrySetResult(true);
                    window.Close();
                }
            });
        }

        [Fact]
        public async Task ComparePlayback_CanceledFocusedSeekRestoresCurrentPlaybackIntent()
        {
            await _fixture.RunAsync(async () =>
            {
                var window = new MainWindow();
                var seekCompletion = new TaskCompletionSource<bool>(
                    TaskCreationOptions.RunContinuationsAsynchronously);
                using var cancellation = new CancellationTokenSource();
                try
                {
                    var primaryEngine = new TestVideoReviewEngine
                    {
                        IsMediaOpen = true,
                        IsPlaying = true,
                        SeekTimeCompletion = seekCompletion
                    };
                    SetPrivateField(window, "_primaryEngine", primaryEngine);

                    var canceledSeek = (Task)InvokePrivate(
                        window,
                        "SeekPaneToTimePreservingPlaybackAsync",
                        ParsePane("Primary"),
                        TimeSpan.FromSeconds(1),
                        cancellation.Token);
                    Assert.True(
                        SpinWait.SpinUntil(
                            () => primaryEngine.SeekToTimeCallCount == 1,
                            TimeSpan.FromSeconds(2)),
                        "The cancelable focused seek did not reach its engine.");

                    primaryEngine.IsPlaying = false;
                    cancellation.Cancel();
                    await Assert.ThrowsAnyAsync<OperationCanceledException>(() => canceledSeek);

                    Assert.Equal(1, primaryEngine.PlayCallCount);
                    Assert.True(primaryEngine.IsPlaying);
                }
                finally
                {
                    seekCompletion.TrySetResult(true);
                    window.Close();
                }
            });
        }

        [Fact]
        public async Task ComparePlayback_CloseInvalidatesDelayedSeekResumeBeforeDisposal()
        {
            await _fixture.RunAsync(async () =>
            {
                var window = new MainWindow();
                var seekCompletion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                var primaryEngine = new TestVideoReviewEngine
                {
                    IsMediaOpen = true,
                    IsPlaying = true,
                    SeekTimeCompletion = seekCompletion
                };
                var compareEngine = new TestVideoReviewEngine
                {
                    IsMediaOpen = true,
                    IsPlaying = true,
                    SeekTimeCompletion = seekCompletion
                };
                try
                {
                    SetPrivateField(window, "_primaryEngine", primaryEngine);
                    SetPrivateField(window, "_compareEngine", compareEngine);
                    SetPrivateField(window, "_isCompareModeSelected", true);
                    SetPrivateField(window, "_isAllPaneTransportSelected", true);
                    window.Show();

                    var delayedSeek = InvokePrivateTask(
                        window,
                        "SeekAllPaneToTimesPreservingPlaybackAsync",
                        new[] { typeof(TimeSpan), typeof(TimeSpan), typeof(CancellationToken) },
                        TimeSpan.FromSeconds(1),
                        TimeSpan.FromSeconds(1),
                        CancellationToken.None);
                    Assert.True(
                        SpinWait.SpinUntil(
                            () => primaryEngine.SeekToTimeCallCount == 1 &&
                                compareEngine.SeekToTimeCallCount == 1,
                            TimeSpan.FromSeconds(2)),
                        "The delayed shared seek did not reach both engines.");

                    window.Close();
                    Assert.Equal(0, primaryEngine.DisposeCallCount);
                    Assert.Equal(0, compareEngine.DisposeCallCount);
                    seekCompletion.TrySetResult(true);
                    await delayedSeek;
                    var disposalTask = GetPrivateField<Task>(window, "_engineDisposalTask")
                        ?? throw new InvalidOperationException("Missing deferred engine disposal task.");
                    await disposalTask.WaitAsync(TimeSpan.FromSeconds(2));

                    Assert.Equal(1, primaryEngine.DisposeCallCount);
                    Assert.Equal(1, compareEngine.DisposeCallCount);
                    Assert.False(primaryEngine.WasDisposedWithActiveOperation);
                    Assert.False(compareEngine.WasDisposedWithActiveOperation);
                    Assert.Equal(0, primaryEngine.PlayCallCount);
                    Assert.Equal(0, compareEngine.PlayCallCount);
                    Assert.False(GetPrivateField<bool>(window, "_isSynchronizedFramePresentationActive"));
                }
                finally
                {
                    seekCompletion.TrySetResult(true);
                    if (primaryEngine.DisposeCallCount == 0)
                    {
                        window.Close();
                    }
                }
            });
        }

        [Fact]
        public void ClosedWindow_DropsLateFramePresentationAndLoopRestart()
        {
            _fixture.Run(() =>
            {
                var window = new MainWindow();
                var primaryEngine = new TestVideoReviewEngine
                {
                    IsMediaOpen = true,
                    IsPlaying = true,
                    CurrentFilePath = "left.mp4",
                    MediaInfo = new VideoMediaInfo(
                        "left.mp4",
                        TimeSpan.FromSeconds(2),
                        TimeSpan.FromSeconds(1d / 30d),
                        30d,
                        1920,
                        1080,
                        "h264",
                        0,
                        30,
                        1,
                        1,
                        90_000)
                };
                SetPrivateField(window, "_primaryEngine", primaryEngine);
                SetPrivateField(window, "_isPrimaryLoopPlaybackEnabled", true);
                window.Show();
                window.Close();

                using var lateFrame = CreateFrameBuffer(8, 4, TimeSpan.FromSeconds(2));
                InvokePrivate(
                    window,
                    "QueueFramePresentation",
                    ParsePane("Primary"),
                    lateFrame);
                InvokePrivate(window, "PresentPendingFrame", ParsePane("Primary"));

                var endState = new VideoReviewEngineStateChangedEventArgs(
                    isMediaOpen: true,
                    isPlaying: true,
                    currentFilePath: "left.mp4",
                    lastErrorMessage: string.Empty,
                    mediaInfo: primaryEngine.MediaInfo,
                    position: new ReviewPosition(
                        TimeSpan.FromSeconds(2),
                        60,
                        isFrameAccurate: true,
                        isFrameIndexAbsolute: true,
                        presentationTimestamp: 180_000,
                        decodeTimestamp: 180_000));
                InvokePrivate(window, "RestartLoopPlaybackIfNeeded", ParsePane("Primary"), endState);

                Assert.Null(GetPrivateField<DecodedFrameBuffer>(window, "_pendingPrimaryFrameBuffer"));
                Assert.Null(GetPrivateField<DecodedFrameBuffer>(window, "_primaryFrameBuffer"));
                Assert.Null(RequireControl<Image>(window, "CustomVideoSurface").Source);
                Assert.Equal(0, primaryEngine.PauseCallCount);
                Assert.Equal(0, primaryEngine.SeekToTimeCallCount);
                Assert.Equal(0, primaryEngine.PlayCallCount);
                Assert.Equal(0, GetPrivateField<int>(window, "_primaryLoopRestartInFlight"));
            });
        }

        [Fact]
        public async Task ClosedWindow_RejectsQueuedPauseAndLateCompareOpenAfterDisposal()
        {
            await _fixture.RunAsync(async () =>
            {
                var window = new MainWindow();
                var primaryGate = GetPrivateField<SemaphoreSlim>(
                    window,
                    "_primaryPaneTransportOperationGate")
                    ?? throw new InvalidOperationException("Missing primary transport operation gate.");
                var gateHeld = false;
                var primaryEngine = new TestVideoReviewEngine
                {
                    IsMediaOpen = true,
                    IsPlaying = true
                };
                var compareEngine = new TestVideoReviewEngine();
                try
                {
                    SetPrivateField(window, "_primaryEngine", primaryEngine);
                    SetPrivateField(window, "_compareEngine", compareEngine);
                    window.Show();
                    await primaryGate.WaitAsync();
                    gateHeld = true;

                    window.Close();
                    var disposalTask = GetPrivateField<Task>(window, "_engineDisposalTask")
                        ?? throw new InvalidOperationException("Missing deferred engine disposal task.");
                    var queuedPause = InvokePrivateTask(
                        window,
                        "PausePlaybackAsync",
                        new[] { typeof(bool), typeof(SynchronizedOperationScope?) },
                        true,
                        (SynchronizedOperationScope?)SynchronizedOperationScope.FocusedPane);

                    primaryGate.Release();
                    gateHeld = false;
                    await Task.WhenAll(disposalTask, queuedPause).WaitAsync(TimeSpan.FromSeconds(2));

                    Assert.Equal(1, primaryEngine.DisposeCallCount);
                    Assert.Equal(1, compareEngine.DisposeCallCount);
                    Assert.Equal(0, primaryEngine.PauseCallCount);

                    await (Task)InvokePrivate(
                        window,
                        "OpenPathAsync",
                        "late-compare.mp4",
                        ParsePane("Compare"));

                    Assert.Same(
                        compareEngine,
                        GetPrivateField<IVideoReviewEngine>(window, "_compareEngine"));
                    Assert.Equal(0, compareEngine.OpenCallCount);
                }
                finally
                {
                    if (gateHeld)
                    {
                        primaryGate.Release();
                    }

                    if (primaryEngine.DisposeCallCount == 0)
                    {
                        window.Close();
                    }
                }
            });
        }

        [Fact]
        public void PrimaryVideoSurface_BitmapPixelsChangeWhenSameSizedFrameUpdates()
        {
            _fixture.Run(() =>
            {
                var window = new MainWindow();

                try
                {
                    var primarySurface = RequireControl<Image>(window, "CustomVideoSurface");
                    using var firstFrame = CreateFrameBuffer(16, 16, red: 0xC0, green: 0x20, blue: 0x20);
                    using var secondFrame = CreateFrameBuffer(16, 16, red: 0x20, green: 0xC0, blue: 0x20);

                    InvokePrivate(window, "SetPaneBitmap", ParsePane("Primary"), firstFrame);
                    var firstBitmap = RequireBitmap(primarySurface);
                    var firstColor = ReadBitmapPixel(firstBitmap, 8, 8);

                    InvokePrivate(window, "SetPaneBitmap", ParsePane("Primary"), secondFrame);
                    var secondBitmap = RequireBitmap(primarySurface);
                    var secondColor = ReadBitmapPixel(secondBitmap, 8, 8);

                    Assert.Same(firstBitmap, secondBitmap);
                    Assert.True(
                        secondColor.Green > firstColor.Green + 64,
                        "The reusable video bitmap did not receive the newer frame pixels.");
                    Assert.True(
                        firstColor.Red > secondColor.Red + 64,
                        "The reusable video bitmap retained the first frame's pixels.");
                }
                finally
                {
                    window.Close();
                }
            });
        }

        [Fact]
        public void PaneBitmapPresentation_InvalidatesBothVideoSurfaces()
        {
            var mainWindowSource = ReadRepositoryFile(
                "src",
                "FramePlayer.Avalonia",
                "Views",
                "MainWindow.axaml.cs");
            var setPaneBitmapMethod = ExtractMethodBody(
                mainWindowSource,
                "private void SetPaneBitmap(",
                "private Pane GetFileOpenTargetPane()");

            Assert.Contains(
                "CompareVideoSurface.InvalidateVisual();",
                setPaneBitmapMethod,
                StringComparison.Ordinal);
            Assert.Contains(
                "CustomVideoSurface.InvalidateVisual();",
                setPaneBitmapMethod,
                StringComparison.Ordinal);
        }

        [Fact]
        public void FramePresented_DoesNotRefreshCacheStatusEveryFrame()
        {
            var mainWindowSource = ReadRepositoryFile(
                "src",
                "FramePlayer.Avalonia",
                "Views",
                "MainWindow.axaml.cs");
            var primaryFramePresentedMethod = ExtractMethodBody(
                mainWindowSource,
                "private void PrimaryEngine_FramePresented(",
                "private void CompareEngine_FramePresented(");
            var compareFramePresentedMethod = ExtractMethodBody(
                mainWindowSource,
                "private void CompareEngine_FramePresented(",
                "private void QueueFramePresentation(");

            Assert.Contains("QueueFramePresentation(Pane.Primary, e.FrameBuffer);", primaryFramePresentedMethod, StringComparison.Ordinal);
            Assert.Contains("QueueFramePresentation(Pane.Compare, e.FrameBuffer);", compareFramePresentedMethod, StringComparison.Ordinal);
            Assert.DoesNotContain("UpdateCacheStatusFromEngine", primaryFramePresentedMethod, StringComparison.Ordinal);
            Assert.DoesNotContain("UpdateCacheStatusFromEngine", compareFramePresentedMethod, StringComparison.Ordinal);
        }

        [Fact]
        public void PlaybackStateChanged_QueuesCacheStatusRefreshWithoutImmediateUpdate()
        {
            var mainWindowSource = ReadRepositoryFile(
                "src",
                "FramePlayer.Avalonia",
                "Views",
                "MainWindow.axaml.cs");
            var primaryStateChangedMethod = ExtractMethodBody(
                mainWindowSource,
                "private void PrimaryEngine_StateChanged(",
                "private void CompareEngine_StateChanged(");
            var compareStateChangedMethod = ExtractMethodBody(
                mainWindowSource,
                "private void CompareEngine_StateChanged(",
                "private void PrimaryEngine_FramePresented(");
            var queueRefreshMethod = ExtractMethodBody(
                mainWindowSource,
                "private void RefreshCacheStatusAfterState(",
                "private void QueueCacheStatusRefresh()");
            var queueTimerMethod = ExtractMethodBody(
                mainWindowSource,
                "private void QueueCacheStatusRefresh()",
                "private void CacheStatusRefreshTimer_Tick(");

            Assert.Contains("RefreshCacheStatusAfterState(e);", primaryStateChangedMethod, StringComparison.Ordinal);
            Assert.Contains("RefreshCacheStatusAfterState(e);", compareStateChangedMethod, StringComparison.Ordinal);
            Assert.DoesNotContain("UpdateCacheStatusFromEngine", primaryStateChangedMethod, StringComparison.Ordinal);
            Assert.DoesNotContain("UpdateCacheStatusFromEngine", compareStateChangedMethod, StringComparison.Ordinal);
            Assert.Contains("QueueCacheStatusRefresh();", queueRefreshMethod, StringComparison.Ordinal);
            Assert.Contains("_cacheStatusRefreshTimer.Start();", queueTimerMethod, StringComparison.Ordinal);
        }

        [Fact]
        public void PlaybackStateChanged_PreservesErrorStatusInsteadOfQueueingCacheRefresh()
        {
            _fixture.Run(() =>
            {
                var window = new MainWindow();
                try
                {
                    var cacheStatus = RequireControl<TextBlock>(window, "CacheStatusTextBlock");
                    var state = new VideoReviewEngineStateChangedEventArgs(
                        isMediaOpen: true,
                        isPlaying: false,
                        currentFilePath: string.Empty,
                        lastErrorMessage: "Decoder failed",
                        mediaInfo: VideoMediaInfo.Empty,
                        position: ReviewPosition.Empty);

                    InvokePrivate(window, "QueueCacheStatusRefresh");
                    InvokePrivate(window, "ApplyState", ParsePane("Primary"), state);
                    InvokePrivate(window, "RefreshCacheStatusAfterState", state);

                    var cacheRefreshTimer = GetPrivateField<DispatcherTimer>(window, "_cacheStatusRefreshTimer")
                        ?? throw new InvalidOperationException("Missing cache refresh timer.");
                    Assert.Equal("Decoder failed", cacheStatus.Text);
                    Assert.False(GetPrivateField<bool>(window, "_hasPendingCacheStatusRefresh"));
                    Assert.False(cacheRefreshTimer.IsEnabled);
                }
                finally
                {
                    window.Close();
                }
            });
        }

        [Fact]
        public void MainSharedTransport_StartsBothPanePlaybackOperationsBeforeAwaiting()
        {
            var mainWindowSource = ReadRepositoryFile(
                "src",
                "FramePlayer.Avalonia",
                "Views",
                "MainWindow.axaml.cs");
            var startAllPaneMethod = ExtractMethodBody(
                mainWindowSource,
                "private async Task StartAllPanePlaybackAsync()",
                "private async Task PauseAllPanePlaybackAsync()");
            var pauseAllPaneMethod = ExtractMethodBody(
                mainWindowSource,
                "private async Task PauseAllPanePlaybackAsync()",
                "private async Task SeekRelativeAsync(");

            Assert.Contains("await Task.WhenAll(", startAllPaneMethod, StringComparison.Ordinal);
            Assert.Contains(
                "Task.Run(() => primaryEngine.PlayAsync(), CancellationToken.None)",
                startAllPaneMethod,
                StringComparison.Ordinal);
            Assert.Contains(
                "Task.Run(() => compareEngine.PlayAsync(), CancellationToken.None)",
                startAllPaneMethod,
                StringComparison.Ordinal);
            Assert.Contains(".ConfigureAwait(false);", startAllPaneMethod, StringComparison.Ordinal);
            Assert.Contains("UpdateCommandStatesOnUiThread();", startAllPaneMethod, StringComparison.Ordinal);
            Assert.Contains("await Task.WhenAll(pauseTasks).ConfigureAwait(false);", pauseAllPaneMethod, StringComparison.Ordinal);
            Assert.Contains("private void UpdateCommandStatesOnUiThread()", mainWindowSource, StringComparison.Ordinal);
            Assert.Contains("Dispatcher.UIThread.Post(() =>", mainWindowSource, StringComparison.Ordinal);
            Assert.Contains("if (Volatile.Read(ref _isClosed) == 0)", mainWindowSource, StringComparison.Ordinal);
            Assert.DoesNotContain("if (!_primaryEngine.IsPlaying)", startAllPaneMethod, StringComparison.Ordinal);
            Assert.DoesNotContain("if (!compareEngine.IsPlaying)", startAllPaneMethod, StringComparison.Ordinal);
            Assert.DoesNotContain("await _primaryEngine.PlayAsync();", startAllPaneMethod, StringComparison.Ordinal);
            Assert.Contains(
                "synchronizePresentation: _isCompareModeSelected &&",
                startAllPaneMethod,
                StringComparison.Ordinal);
            Assert.Contains(
                "_isAllPaneTransportSelected).ConfigureAwait(false);",
                startAllPaneMethod,
                StringComparison.Ordinal);
            Assert.Contains("await StartPlaybackAsync(null, null);", mainWindowSource, StringComparison.Ordinal);
        }

        [Theory]
        [InlineData(false, false, false)]
        [InlineData(true, false, false)]
        [InlineData(false, true, false)]
        [InlineData(true, true, true)]
        public void MainSharedTransport_PausesOnlyWhenBothPanesArePlaying(
            bool primaryPlaying,
            bool comparePlaying,
            bool expectedShouldPause)
        {
            var primaryEngine = new TestVideoReviewEngine
            {
                IsMediaOpen = true,
                IsPlaying = primaryPlaying
            };
            var compareEngine = new TestVideoReviewEngine
            {
                IsMediaOpen = true,
                IsPlaying = comparePlaying
            };

            var shouldPause = InvokePrivateStatic<bool>(
                "ShouldPauseAllPanePlayback",
                primaryEngine,
                compareEngine);

            Assert.Equal(expectedShouldPause, shouldPause);
        }

        [Fact]
        public void MainSharedTransport_StartsBothPanesWithoutImplicitResync()
        {
            var mainWindowSource = ReadRepositoryFile(
                "src",
                "FramePlayer.Avalonia",
                "Views",
                "MainWindow.axaml.cs");
            var toggleAllPaneMethod = ExtractMethodBody(
                mainWindowSource,
                "private async Task ToggleAllPanePlaybackAsync()",
                "private async Task ToggleFocusedPanePlaybackAsync()");

            Assert.Contains("if (ShouldPauseAllPanePlayback())", toggleAllPaneMethod, StringComparison.Ordinal);
            Assert.Contains("await PauseAllPanePlaybackAsync(endSynchronizedPresentation: true);", toggleAllPaneMethod, StringComparison.Ordinal);
            Assert.Contains("await StartAllPanePlaybackAsync();", toggleAllPaneMethod, StringComparison.Ordinal);
            Assert.DoesNotContain("RestoreAndStartAllPanePlaybackAsync", toggleAllPaneMethod, StringComparison.Ordinal);
        }

        [Fact]
        public void PaneLocalTransport_DoesNotUseGlobalPlaybackStartGate()
        {
            var mainWindowSource = ReadRepositoryFile(
                "src",
                "FramePlayer.Avalonia",
                "Views",
                "MainWindow.axaml.cs");
            var startPaneMethod = ExtractMethodBody(
                mainWindowSource,
                "private async Task<bool> StartPanePlaybackForIntentCoreAsync(",
                "private async Task PausePanePlaybackAsync(");
            var pausePaneMethod = ExtractMethodBody(
                mainWindowSource,
                "private async Task PausePanePlaybackAsync(",
                "[SuppressMessage(\"Major Code Smell\", \"S1144:Unused private types or members\"");
            var stepPaneMethod = ExtractMethodBody(
                mainWindowSource,
                "private async Task StepFrameAsync(int delta, Pane pane)",
                "private async Task StepFrameCoreAsync(");
            var seekPaneMethod = ExtractMethodBody(
                mainWindowSource,
                "private async Task SeekPaneToTimePreservingPlaybackAsync(",
                "private async Task<bool> SeekPaneToFrameAsync(");
            var loopRestartMethod = ExtractMethodBody(
                mainWindowSource,
                "private async Task RestartLoopPlaybackAsync(",
                "private bool CanContinueLoopRestart(");

            Assert.DoesNotContain("_playbackStartGate", startPaneMethod, StringComparison.Ordinal);
            Assert.DoesNotContain("_playbackStartGate", pausePaneMethod, StringComparison.Ordinal);
            Assert.DoesNotContain("_playbackStartGate", stepPaneMethod, StringComparison.Ordinal);
            Assert.DoesNotContain("_playbackStartGate", seekPaneMethod, StringComparison.Ordinal);
            Assert.DoesNotContain("_playbackStartGate", loopRestartMethod, StringComparison.Ordinal);
        }

        [Fact]
        public async Task ComparePlayback_MasterPlayRestoresBothPanesAfterPanePause()
        {
            await _fixture.RunAsync(async () =>
            {
                var window = new MainWindow();
                try
                {
                    var primaryEngine = new TestVideoReviewEngine
                    {
                        IsMediaOpen = true
                    };
                    var compareEngine = new TestVideoReviewEngine
                    {
                        IsMediaOpen = true
                    };
                    SetPrivateField(window, "_primaryEngine", primaryEngine);
                    SetPrivateField(window, "_compareEngine", compareEngine);
                    RequireControl<CheckBox>(window, "CompareModeCheckBox").IsChecked = true;

                    await InvokePrivateTask(
                        window,
                        "TogglePlaybackAsync",
                        Type.EmptyTypes);

                    Assert.True(primaryEngine.IsPlaying);
                    Assert.True(compareEngine.IsPlaying);
                    Assert.True(GetPrivateField<bool>(
                        window,
                        "_isSynchronizedFramePresentationActive"));

                    await InvokePrivateTask(
                        window,
                        "ToggleFocusedPanePlaybackAsync",
                        Type.EmptyTypes);

                    Assert.False(primaryEngine.IsPlaying);
                    Assert.True(compareEngine.IsPlaying);
                    Assert.False(GetPrivateField<bool>(
                        window,
                        "_isSynchronizedFramePresentationActive"));

                    await InvokePrivateTask(
                        window,
                        "TogglePlaybackAsync",
                        Type.EmptyTypes);

                    Assert.True(primaryEngine.IsPlaying);
                    Assert.True(compareEngine.IsPlaying);
                    Assert.True(GetPrivateField<bool>(
                        window,
                        "_isSynchronizedFramePresentationActive"));
                }
                finally
                {
                    window.Close();
                }
            });
        }

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public async Task ComparePlayback_NonLoopingPaneAtEndDoesNotFreezeLoopingPeerVideo(
            bool primaryStopsAtEnd)
        {
            await _fixture.RunAsync(async () =>
            {
                var window = new MainWindow();
                try
                {
                    var frameStep = TimeSpan.FromSeconds(1d / 30d);
                    var primaryMediaInfo = new VideoMediaInfo(
                        "left.mp4",
                        TimeSpan.FromSeconds(
                            primaryStopsAtEnd ? 2 : 10),
                        frameStep,
                        30d,
                        1920,
                        1080,
                        "h264",
                        0,
                        30,
                        1,
                        1,
                        90_000);
                    var compareMediaInfo = new VideoMediaInfo(
                        "right.mp4",
                        TimeSpan.FromSeconds(
                            primaryStopsAtEnd ? 10 : 2),
                        frameStep,
                        30d,
                        1920,
                        1080,
                        "h264",
                        0,
                        30,
                        1,
                        1,
                        90_000);
                    var primaryEngine = new TestVideoReviewEngine
                    {
                        IsMediaOpen = true,
                        IsPlaying = true,
                        CurrentFilePath = "left.mp4",
                        MediaInfo = primaryMediaInfo,
                        Position = new ReviewPosition(
                            TimeSpan.FromSeconds(2),
                            60,
                            true,
                            true,
                            180_000,
                            180_000)
                    };
                    var compareEngine = new TestVideoReviewEngine
                    {
                        IsMediaOpen = true,
                        IsPlaying = true,
                        CurrentFilePath = "right.mp4",
                        MediaInfo = compareMediaInfo,
                        Position = new ReviewPosition(
                            TimeSpan.FromSeconds(2),
                            60,
                            true,
                            true,
                            180_000,
                            180_000)
                    };
                    SetPrivateField(window, "_primaryEngine", primaryEngine);
                    SetPrivateField(window, "_compareEngine", compareEngine);
                    RequireControl<CheckBox>(
                        window,
                        "CompareModeCheckBox").IsChecked = true;
                    SetPrivateField(
                        window,
                        "_isAllPaneTransportSelected",
                        true);
                    SetPrivateField(
                        window,
                        "_isPrimaryLoopPlaybackEnabled",
                        !primaryStopsAtEnd);
                    SetPrivateField(
                        window,
                        "_isCompareLoopPlaybackEnabled",
                        primaryStopsAtEnd);

                    using var primaryEndFrame = CreateFrameBuffer(
                        8,
                        4,
                        TimeSpan.FromSeconds(2));
                    using var initialCompareFrame = CreateFrameBuffer(
                        8,
                        4,
                        TimeSpan.FromSeconds(2));
                    InvokePrivate(
                        window,
                        "SetPaneBitmap",
                        ParsePane("Primary"),
                        primaryEndFrame);
                    InvokePrivate(
                        window,
                        "SetPaneBitmap",
                        ParsePane("Compare"),
                        initialCompareFrame);

                    var transportIntentGeneration = (int)InvokePrivate(
                        window,
                        "BeginAllPaneTransportIntent");
                    Assert.True((bool)InvokePrivate(
                        window,
                        "TryBeginSynchronizedFramePresentation",
                        transportIntentGeneration));

                    using var stalePlayingFrame = CreateFrameBuffer(
                        8,
                        4,
                        TimeSpan.FromSeconds(2.05),
                        red: 0x20,
                        green: 0x40,
                        blue: 0xC0);
                    InvokePrivate(
                        window,
                        primaryStopsAtEnd
                            ? "CompareEngine_FramePresented"
                            : "PrimaryEngine_FramePresented",
                        primaryStopsAtEnd
                            ? compareEngine
                            : primaryEngine,
                        new FramePresentedEventArgs(
                            stalePlayingFrame));
                    Assert.NotNull(GetPrivateField<DecodedFrameBuffer>(
                        window,
                        primaryStopsAtEnd
                            ? "_pendingSynchronizedCompareFrame"
                            : "_pendingSynchronizedPrimaryFrame"));

                    var stoppedEngine = primaryStopsAtEnd
                        ? primaryEngine
                        : compareEngine;
                    var playingEngine = primaryStopsAtEnd
                        ? compareEngine
                        : primaryEngine;
                    var stoppedMediaInfo = primaryStopsAtEnd
                        ? primaryMediaInfo
                        : compareMediaInfo;
                    stoppedEngine.IsPlaying = false;
                    var stoppedEndState =
                        new VideoReviewEngineStateChangedEventArgs(
                            isMediaOpen: true,
                            isPlaying: false,
                            currentFilePath:
                                stoppedEngine.CurrentFilePath,
                            lastErrorMessage: string.Empty,
                            mediaInfo: stoppedMediaInfo,
                            position: stoppedEngine.Position);
                    InvokePrivate(
                        window,
                        primaryStopsAtEnd
                            ? "PrimaryEngine_StateChanged"
                            : "CompareEngine_StateChanged",
                        stoppedEngine,
                        stoppedEndState);

                    Assert.True(
                        SpinWait.SpinUntil(
                            () => !GetPrivateField<bool>(
                                window,
                                "_isSynchronizedFramePresentationActive"),
                            TimeSpan.FromSeconds(2)),
                        "The stopped non-looping pane did not release paired presentation.");
                    Assert.Null(GetPrivateField<DecodedFrameBuffer>(
                        window,
                        "_pendingSynchronizedPrimaryFrame"));
                    Assert.Null(GetPrivateField<DecodedFrameBuffer>(
                        window,
                        "_pendingSynchronizedCompareFrame"));

                    using var advancingPlayingFrame = CreateFrameBuffer(
                        8,
                        4,
                        TimeSpan.FromSeconds(2.1));
                    InvokePrivate(
                        window,
                        primaryStopsAtEnd
                            ? "CompareEngine_FramePresented"
                            : "PrimaryEngine_FramePresented",
                        playingEngine,
                        new FramePresentedEventArgs(
                            advancingPlayingFrame));
                    InvokePrivate(
                        window,
                        "PresentPendingFrame",
                        ParsePane(
                            primaryStopsAtEnd
                                ? "Compare"
                                : "Primary"));

                    Assert.Equal(
                        TimeSpan.FromSeconds(
                            primaryStopsAtEnd ? 2 : 2.1),
                        GetPrivateField<DecodedFrameBuffer>(
                            window,
                            "_primaryFrameBuffer")!.Descriptor.PresentationTime);
                    Assert.Equal(
                        TimeSpan.FromSeconds(
                            primaryStopsAtEnd ? 2.1 : 2),
                        GetPrivateField<DecodedFrameBuffer>(
                            window,
                            "_compareFrameBuffer")!.Descriptor.PresentationTime);
                    Assert.True(playingEngine.IsPlaying);
                }
                finally
                {
                    window.Close();
                }
            });
        }

        [Fact]
        public async Task ComparePlayback_TransientPaneStopDoesNotReleaseResumedPairing()
        {
            await _fixture.RunAsync(async () =>
            {
                var window = new MainWindow();
                try
                {
                    var primaryEngine = new TestVideoReviewEngine
                    {
                        IsMediaOpen = true,
                        IsPlaying = true
                    };
                    var compareEngine = new TestVideoReviewEngine
                    {
                        IsMediaOpen = true,
                        IsPlaying = true
                    };
                    SetPrivateField(
                        window,
                        "_primaryEngine",
                        primaryEngine);
                    SetPrivateField(
                        window,
                        "_compareEngine",
                        compareEngine);
                    SetPrivateField(
                        window,
                        "_isCompareModeSelected",
                        true);
                    SetPrivateField(
                        window,
                        "_isAllPaneTransportSelected",
                        true);
                    var transportIntentGeneration = (int)InvokePrivate(
                        window,
                        "BeginAllPaneTransportIntent");
                    Assert.True((bool)InvokePrivate(
                        window,
                        "TryBeginSynchronizedFramePresentation",
                        transportIntentGeneration));

                    await (Task)InvokePrivate(
                        window,
                        "ReleaseSynchronizedPresentationIfPaneStoppedAsync",
                        ParsePane("Primary"),
                        primaryEngine,
                        compareEngine,
                        transportIntentGeneration);

                    Assert.True(GetPrivateField<bool>(
                        window,
                        "_isSynchronizedFramePresentationActive"));
                }
                finally
                {
                    window.Close();
                }
            });
        }

        [Fact]
        public async Task ComparePlayback_MidFilePaneStopDoesNotReleaseResumedPairing()
        {
            await _fixture.RunAsync(async () =>
            {
                var window = new MainWindow();
                try
                {
                    var mediaInfo = new VideoMediaInfo(
                        "compare-stop.mp4",
                        TimeSpan.FromSeconds(10),
                        TimeSpan.FromSeconds(1d / 30d),
                        30d,
                        1920,
                        1080,
                        "h264",
                        0,
                        30,
                        1,
                        1,
                        90_000);
                    var primaryEngine = new TestVideoReviewEngine
                    {
                        IsMediaOpen = true,
                        IsPlaying = false,
                        MediaInfo = mediaInfo
                    };
                    var compareEngine = new TestVideoReviewEngine
                    {
                        IsMediaOpen = true,
                        IsPlaying = true,
                        MediaInfo = mediaInfo
                    };
                    var midFileStoppedState = new VideoReviewEngineStateChangedEventArgs(
                        isMediaOpen: true,
                        isPlaying: false,
                        currentFilePath: "left.mp4",
                        lastErrorMessage: string.Empty,
                        mediaInfo: mediaInfo,
                        position: new ReviewPosition(
                            TimeSpan.FromSeconds(2),
                            60,
                            isFrameAccurate: true,
                            isFrameIndexAbsolute: true,
                            presentationTimestamp: 180_000,
                            decodeTimestamp: 180_000));

                    SetPrivateField(window, "_primaryEngine", primaryEngine);
                    SetPrivateField(window, "_compareEngine", compareEngine);
                    SetPrivateField(window, "_isCompareModeSelected", true);
                    SetPrivateField(window, "_isAllPaneTransportSelected", true);
                    var transportIntentGeneration = (int)InvokePrivate(
                        window,
                        "BeginAllPaneTransportIntent");
                    Assert.True((bool)InvokePrivate(
                        window,
                        "TryBeginSynchronizedFramePresentation",
                        transportIntentGeneration));

                    InvokePrivate(
                        window,
                        "ReleaseSynchronizedPresentationIfPaneStopped",
                        ParsePane("Primary"),
                        primaryEngine,
                        midFileStoppedState);
                    await Task.Delay(TimeSpan.FromMilliseconds(100));

                    Assert.True(GetPrivateField<bool>(
                        window,
                        "_isSynchronizedFramePresentationActive"));
                }
                finally
                {
                    window.Close();
                }
            });
        }

        [Fact]
        public async Task ComparePlayback_PlaybackBoundaryPaneStopReleasesPairing()
        {
            await _fixture.RunAsync(async () =>
            {
                var window = new MainWindow();
                try
                {
                    var mediaInfo = new VideoMediaInfo(
                        "compare-stop.mp4",
                        TimeSpan.FromSeconds(10),
                        TimeSpan.FromSeconds(1d / 30d),
                        30d,
                        1920,
                        1080,
                        "h264",
                        0,
                        30,
                        1,
                        1,
                        90_000);
                    var primaryEngine = new TestVideoReviewEngine
                    {
                        IsMediaOpen = true,
                        IsPlaying = false,
                        MediaInfo = mediaInfo
                    };
                    var compareEngine = new TestVideoReviewEngine
                    {
                        IsMediaOpen = true,
                        IsPlaying = true,
                        MediaInfo = mediaInfo
                    };
                    var endStoppedState = new VideoReviewEngineStateChangedEventArgs(
                        isMediaOpen: true,
                        isPlaying: false,
                        currentFilePath: "left.mp4",
                        lastErrorMessage: string.Empty,
                        mediaInfo: mediaInfo,
                        position: new ReviewPosition(
                            TimeSpan.FromSeconds(10),
                            300,
                            isFrameAccurate: true,
                            isFrameIndexAbsolute: true,
                            presentationTimestamp: 900_000,
                            decodeTimestamp: 900_000));

                    SetPrivateField(window, "_primaryEngine", primaryEngine);
                    SetPrivateField(window, "_compareEngine", compareEngine);
                    SetPrivateField(window, "_isCompareModeSelected", true);
                    SetPrivateField(window, "_isAllPaneTransportSelected", true);
                    var transportIntentGeneration = (int)InvokePrivate(
                        window,
                        "BeginAllPaneTransportIntent");
                    Assert.True((bool)InvokePrivate(
                        window,
                        "TryBeginSynchronizedFramePresentation",
                        transportIntentGeneration));

                    InvokePrivate(
                        window,
                        "ReleaseSynchronizedPresentationIfPaneStopped",
                        ParsePane("Primary"),
                        primaryEngine,
                        endStoppedState);

                    Assert.True(
                        SpinWait.SpinUntil(
                            () => !GetPrivateField<bool>(
                                window,
                                "_isSynchronizedFramePresentationActive"),
                            TimeSpan.FromSeconds(2)),
                        "A stopped pane at the playback boundary did not release synchronized presentation.");
                }
                finally
                {
                    window.Close();
                }
            });
        }

        [Fact]
        public void CompareLoopPlayback_RestartsLeftPaneWhileRightRestartIsInFlight()
        {
            _fixture.Run(() =>
            {
                var window = new MainWindow();
                var primaryPauseCompletion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                var comparePauseCompletion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                try
                {
                    var mediaInfo = new VideoMediaInfo(
                        "compare-loop.mp4",
                        TimeSpan.FromSeconds(10),
                        TimeSpan.FromSeconds(1d / 30d),
                        30d,
                        1920,
                        1080,
                        "h264",
                        0,
                        30,
                        1,
                        1,
                        90_000);
                    var primaryEngine = new TestVideoReviewEngine
                    {
                        IsMediaOpen = true,
                        IsPlaying = true,
                        CurrentFilePath = "left.mp4",
                        MediaInfo = mediaInfo,
                        PauseCompletion = primaryPauseCompletion
                    };
                    var compareEngine = new TestVideoReviewEngine
                    {
                        IsMediaOpen = true,
                        IsPlaying = true,
                        CurrentFilePath = "right.mp4",
                        MediaInfo = mediaInfo,
                        PauseCompletion = comparePauseCompletion
                    };
                    var state = new VideoReviewEngineStateChangedEventArgs(
                        isMediaOpen: true,
                        isPlaying: true,
                        currentFilePath: "compare-loop.mp4",
                        lastErrorMessage: string.Empty,
                        mediaInfo: mediaInfo,
                        position: new ReviewPosition(
                            TimeSpan.FromSeconds(2),
                            60,
                            isFrameAccurate: true,
                            isFrameIndexAbsolute: true,
                            presentationTimestamp: 180_000,
                            decodeTimestamp: 180_000));

                    SetPrivateField(window, "_primaryEngine", primaryEngine);
                    SetPrivateField(window, "_compareEngine", compareEngine);
                    SetPrivateField(window, "_primaryLoopRange", CreateLoopRange("pane-primary", "left.mp4"));
                    SetPrivateField(window, "_compareLoopRange", CreateLoopRange("pane-compare", "right.mp4"));
                    SetPrivateField(window, "_isPrimaryLoopPlaybackEnabled", true);
                    SetPrivateField(window, "_isCompareLoopPlaybackEnabled", true);

                    InvokePrivate(window, "RestartLoopPlaybackIfNeeded", ParsePane("Compare"), state);
                    Assert.True(
                        SpinWait.SpinUntil(() => compareEngine.PauseCallCount == 1, TimeSpan.FromSeconds(2)),
                        "The right pane did not begin its loop restart.");

                    InvokePrivate(window, "RestartLoopPlaybackIfNeeded", ParsePane("Primary"), state);

                    Assert.True(
                        SpinWait.SpinUntil(() => primaryEngine.PauseCallCount == 1, TimeSpan.FromSeconds(2)),
                        "The left pane loop restart was blocked by the right pane restart.");
                }
                finally
                {
                    primaryPauseCompletion.TrySetResult(true);
                    comparePauseCompletion.TrySetResult(true);
                    window.Close();
                }
            });
        }

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public void CompareLoopPlayback_AllPaneTransportRestartsAtLongerBoundary(
            bool compareIsLonger)
        {
            _fixture.Run(() =>
            {
                var window = new MainWindow();
                var primaryPauseCompletion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                var comparePauseCompletion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                var primarySeekCompletion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                var compareSeekCompletion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                try
                {
                    var primaryMediaInfo = CreateMediaInfo(
                        "left.mp4",
                        TimeSpan.FromSeconds(compareIsLonger ? 2 : 10));
                    var compareMediaInfo = CreateMediaInfo(
                        "right.mp4",
                        TimeSpan.FromSeconds(compareIsLonger ? 10 : 2));
                    var primaryEngine = new TestVideoReviewEngine
                    {
                        IsMediaOpen = true,
                        IsPlaying = true,
                        CurrentFilePath = "left.mp4",
                        MediaInfo = primaryMediaInfo,
                        PauseCompletion = primaryPauseCompletion,
                        SeekTimeCompletion = primarySeekCompletion
                    };
                    var compareEngine = new TestVideoReviewEngine
                    {
                        IsMediaOpen = true,
                        IsPlaying = true,
                        CurrentFilePath = "right.mp4",
                        MediaInfo = compareMediaInfo,
                        PauseCompletion = comparePauseCompletion,
                        SeekTimeCompletion = compareSeekCompletion
                    };
                    var shorterMediaInfo = compareIsLonger
                        ? primaryMediaInfo
                        : compareMediaInfo;
                    var longerMediaInfo = compareIsLonger
                        ? compareMediaInfo
                        : primaryMediaInfo;
                    var shorterState = new VideoReviewEngineStateChangedEventArgs(
                        isMediaOpen: true,
                        isPlaying: true,
                        currentFilePath: compareIsLonger ? "left.mp4" : "right.mp4",
                        lastErrorMessage: string.Empty,
                        mediaInfo: shorterMediaInfo,
                        position: new ReviewPosition(
                            shorterMediaInfo.Duration,
                            60,
                            isFrameAccurate: true,
                            isFrameIndexAbsolute: true,
                            presentationTimestamp: 180_000,
                            decodeTimestamp: 180_000));
                    var longerState = new VideoReviewEngineStateChangedEventArgs(
                        isMediaOpen: true,
                        isPlaying: true,
                        currentFilePath: compareIsLonger ? "right.mp4" : "left.mp4",
                        lastErrorMessage: string.Empty,
                        mediaInfo: longerMediaInfo,
                        position: new ReviewPosition(
                            longerMediaInfo.Duration,
                            300,
                            isFrameAccurate: true,
                            isFrameIndexAbsolute: true,
                            presentationTimestamp: 900_000,
                            decodeTimestamp: 900_000));

                    SetPrivateField(window, "_primaryEngine", primaryEngine);
                    SetPrivateField(window, "_compareEngine", compareEngine);
                    SetPrivateField(
                        window,
                        "_primaryLoopRange",
                        CreateLoopRange(
                            "pane-primary",
                            "left.mp4",
                            primaryMediaInfo.Duration));
                    SetPrivateField(
                        window,
                        "_compareLoopRange",
                        CreateLoopRange(
                            "pane-compare",
                            "right.mp4",
                            compareMediaInfo.Duration));
                    SetPrivateField(window, "_isPrimaryLoopPlaybackEnabled", true);
                    SetPrivateField(window, "_isCompareLoopPlaybackEnabled", true);
                    RequireControl<CheckBox>(window, "CompareModeCheckBox").IsChecked = true;
                    SetPrivateField(window, "_isAllPanePlaybackControlActive", true);

                    InvokePrivate(
                        window,
                        "RestartLoopPlaybackIfNeeded",
                        ParsePane(compareIsLonger ? "Primary" : "Compare"),
                        shorterState);
                    Assert.False(
                        SpinWait.SpinUntil(
                            () => primaryEngine.PauseCallCount > 0 ||
                                compareEngine.PauseCallCount > 0,
                            TimeSpan.FromMilliseconds(150)),
                        "The shorter pane boundary should not trigger a master loop restart.");

                    InvokePrivate(
                        window,
                        "RestartLoopPlaybackIfNeeded",
                        ParsePane(compareIsLonger ? "Compare" : "Primary"),
                        longerState);

                    Assert.True(
                        SpinWait.SpinUntil(
                            () => primaryEngine.PauseCallCount == 1 && compareEngine.PauseCallCount == 1,
                            TimeSpan.FromSeconds(2)),
                        "The longer all-pane loop boundary did not restart both panes.");

                    primaryPauseCompletion.TrySetResult(true);
                    comparePauseCompletion.TrySetResult(true);
                    Assert.True(
                        SpinWait.SpinUntil(
                            () => primaryEngine.SeekToTimeCallCount == 1 && compareEngine.SeekToTimeCallCount == 1,
                            TimeSpan.FromSeconds(2)),
                        "The synchronized restart did not seek both panes.");

                    primarySeekCompletion.TrySetResult(true);
                    Assert.False(
                        SpinWait.SpinUntil(
                            () => primaryEngine.PlayCallCount > 0 || compareEngine.PlayCallCount > 0,
                            TimeSpan.FromMilliseconds(150)),
                        "A pane resumed before both synchronized loop seeks completed.");

                    compareSeekCompletion.TrySetResult(true);
                    Assert.True(
                        SpinWait.SpinUntil(
                            () => primaryEngine.PlayCallCount == 1 && compareEngine.PlayCallCount == 1,
                            TimeSpan.FromSeconds(2)),
                        "The synchronized restart did not resume both panes.");
                }
                finally
                {
                    primaryPauseCompletion.TrySetResult(true);
                    comparePauseCompletion.TrySetResult(true);
                    primarySeekCompletion.TrySetResult(true);
                    compareSeekCompletion.TrySetResult(true);
                    window.Close();
                }
            });
        }

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public void CompareLoopPlayback_StoppedShortPaneUnderMasterWaitsForLongerBoundary(
            bool compareIsLonger)
        {
            _fixture.Run(() =>
            {
                var window = new MainWindow();
                var primaryPauseCompletion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                var comparePauseCompletion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                try
                {
                    var primaryMediaInfo = CreateMediaInfo(
                        "left.mp4",
                        TimeSpan.FromSeconds(compareIsLonger ? 2 : 10));
                    var compareMediaInfo = CreateMediaInfo(
                        "right.mp4",
                        TimeSpan.FromSeconds(compareIsLonger ? 10 : 2));
                    var primaryEngine = new TestVideoReviewEngine
                    {
                        IsMediaOpen = true,
                        IsPlaying = !compareIsLonger,
                        CurrentFilePath = "left.mp4",
                        MediaInfo = primaryMediaInfo,
                        PauseCompletion = primaryPauseCompletion
                    };
                    var compareEngine = new TestVideoReviewEngine
                    {
                        IsMediaOpen = true,
                        IsPlaying = compareIsLonger,
                        CurrentFilePath = "right.mp4",
                        MediaInfo = compareMediaInfo,
                        PauseCompletion = comparePauseCompletion
                    };
                    var shorterMediaInfo = compareIsLonger
                        ? primaryMediaInfo
                        : compareMediaInfo;
                    var longerMediaInfo = compareIsLonger
                        ? compareMediaInfo
                        : primaryMediaInfo;
                    var stoppedState = new VideoReviewEngineStateChangedEventArgs(
                        isMediaOpen: true,
                        isPlaying: false,
                        currentFilePath: compareIsLonger ? "left.mp4" : "right.mp4",
                        lastErrorMessage: string.Empty,
                        mediaInfo: shorterMediaInfo,
                        position: new ReviewPosition(
                            shorterMediaInfo.Duration,
                            60,
                            isFrameAccurate: true,
                            isFrameIndexAbsolute: true,
                            presentationTimestamp: 180_000,
                            decodeTimestamp: 180_000));
                    var longerBoundaryState = new VideoReviewEngineStateChangedEventArgs(
                        isMediaOpen: true,
                        isPlaying: true,
                        currentFilePath: compareIsLonger ? "right.mp4" : "left.mp4",
                        lastErrorMessage: string.Empty,
                        mediaInfo: longerMediaInfo,
                        position: new ReviewPosition(
                            longerMediaInfo.Duration,
                            300,
                            isFrameAccurate: true,
                            isFrameIndexAbsolute: true,
                            presentationTimestamp: 900_000,
                            decodeTimestamp: 900_000));

                    SetPrivateField(window, "_primaryEngine", primaryEngine);
                    SetPrivateField(window, "_compareEngine", compareEngine);
                    SetPrivateField(
                        window,
                        "_primaryLoopRange",
                        CreateLoopRange(
                            "pane-primary",
                            "left.mp4",
                            primaryMediaInfo.Duration));
                    SetPrivateField(
                        window,
                        "_compareLoopRange",
                        CreateLoopRange(
                            "pane-compare",
                            "right.mp4",
                            compareMediaInfo.Duration));
                    SetPrivateField(window, "_isPrimaryLoopPlaybackEnabled", true);
                    SetPrivateField(window, "_isCompareLoopPlaybackEnabled", true);
                    RequireControl<CheckBox>(window, "CompareModeCheckBox").IsChecked = true;
                    var transportIntentGeneration = (int)InvokePrivate(
                        window,
                        "BeginAllPaneTransportIntent");
                    Assert.True((bool)InvokePrivate(
                        window,
                        "TryBeginSynchronizedFramePresentation",
                        transportIntentGeneration));
                    SetPrivateField(window, "_isAllPanePlaybackControlActive", true);

                    InvokePrivate(
                        window,
                        "ReleaseSynchronizedPresentationIfPaneStopped",
                        ParsePane(compareIsLonger ? "Primary" : "Compare"),
                        compareIsLonger ? primaryEngine : compareEngine,
                        stoppedState);
                    Assert.True(
                        SpinWait.SpinUntil(
                            () => !GetPrivateField<bool>(
                                window,
                                "_isSynchronizedFramePresentationActive"),
                            TimeSpan.FromSeconds(2)),
                        "The stopped shorter pane did not release synchronized presentation.");

                    InvokePrivate(
                        window,
                        "RestartLoopPlaybackIfNeeded",
                        ParsePane(compareIsLonger ? "Primary" : "Compare"),
                        stoppedState);
                    Assert.False(
                        SpinWait.SpinUntil(
                            () => primaryEngine.PauseCallCount > 0 ||
                                compareEngine.PauseCallCount > 0,
                            TimeSpan.FromMilliseconds(150)),
                        "The stopped shorter pane should not trigger a loop restart.");

                    InvokePrivate(
                        window,
                        "RestartLoopPlaybackIfNeeded",
                        ParsePane(compareIsLonger ? "Compare" : "Primary"),
                        longerBoundaryState);
                    Assert.True(
                        SpinWait.SpinUntil(
                            () => primaryEngine.PauseCallCount == 1 &&
                                compareEngine.PauseCallCount == 1,
                            TimeSpan.FromSeconds(2)),
                        "The longer boundary did not restart both panes after the shorter pane stopped.");
                }
                finally
                {
                    primaryPauseCompletion.TrySetResult(true);
                    comparePauseCompletion.TrySetResult(true);
                    window.Close();
                }
            });
        }

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public void CompareLoopPlayback_LocalPaneTransportRestartsAtOwnBoundary(
            bool primaryIsShorter)
        {
            _fixture.Run(() =>
            {
                var window = new MainWindow();
                try
                {
                    var primaryMediaInfo = CreateMediaInfo(
                        "left.mp4",
                        TimeSpan.FromSeconds(primaryIsShorter ? 2 : 10));
                    var compareMediaInfo = CreateMediaInfo(
                        "right.mp4",
                        TimeSpan.FromSeconds(primaryIsShorter ? 10 : 2));
                    var primaryEngine = new TestVideoReviewEngine
                    {
                        IsMediaOpen = true,
                        IsPlaying = true,
                        CurrentFilePath = "left.mp4",
                        MediaInfo = primaryMediaInfo
                    };
                    var compareEngine = new TestVideoReviewEngine
                    {
                        IsMediaOpen = true,
                        IsPlaying = true,
                        CurrentFilePath = "right.mp4",
                        MediaInfo = compareMediaInfo
                    };
                    var loopingPane = ParsePane(
                        primaryIsShorter ? "Primary" : "Compare");
                    var loopingMediaInfo = primaryIsShorter
                        ? primaryMediaInfo
                        : compareMediaInfo;
                    var loopingState = new VideoReviewEngineStateChangedEventArgs(
                        isMediaOpen: true,
                        isPlaying: true,
                        currentFilePath: primaryIsShorter ? "left.mp4" : "right.mp4",
                        lastErrorMessage: string.Empty,
                        mediaInfo: loopingMediaInfo,
                        position: new ReviewPosition(
                            loopingMediaInfo.Duration,
                            60,
                            isFrameAccurate: true,
                            isFrameIndexAbsolute: true,
                            presentationTimestamp: 180_000,
                            decodeTimestamp: 180_000));

                    SetPrivateField(window, "_primaryEngine", primaryEngine);
                    SetPrivateField(window, "_compareEngine", compareEngine);
                    SetPrivateField(
                        window,
                        "_primaryLoopRange",
                        CreateLoopRange(
                            "pane-primary",
                            "left.mp4",
                            primaryMediaInfo.Duration));
                    SetPrivateField(
                        window,
                        "_compareLoopRange",
                        CreateLoopRange(
                            "pane-compare",
                            "right.mp4",
                            compareMediaInfo.Duration));
                    SetPrivateField(window, "_isPrimaryLoopPlaybackEnabled", true);
                    SetPrivateField(window, "_isCompareLoopPlaybackEnabled", true);
                    RequireControl<CheckBox>(window, "CompareModeCheckBox").IsChecked = true;
                    SetPrivateField(window, "_isAllPanePlaybackControlActive", false);

                    InvokePrivate(
                        window,
                        "RestartLoopPlaybackIfNeeded",
                        loopingPane,
                        loopingState);

                    var loopingEngine = primaryIsShorter
                        ? primaryEngine
                        : compareEngine;
                    var peerEngine = primaryIsShorter
                        ? compareEngine
                        : primaryEngine;
                    Assert.True(
                        SpinWait.SpinUntil(
                            () => loopingEngine.PlayCallCount == 1,
                            TimeSpan.FromSeconds(2)),
                        "A locally controlled pane with loop enabled did not restart at its own boundary.");
                    Assert.Equal(1, loopingEngine.PauseCallCount);
                    Assert.Equal(1, loopingEngine.SeekToTimeCallCount);
                    Assert.Equal(0, peerEngine.PauseCallCount);
                    Assert.Equal(0, peerEngine.SeekToTimeCallCount);
                    Assert.Equal(0, peerEngine.PlayCallCount);
                }
                finally
                {
                    window.Close();
                }
            });
        }

        [Fact]
        public async Task CompareLoopPlayback_AllPaneRestartPreservesPresentedOffsetAtBoundary()
        {
            await _fixture.RunAsync(async () =>
            {
                var window = new MainWindow();
                try
                {
                    var primaryEngine = new TestVideoReviewEngine
                    {
                        IsMediaOpen = true,
                        IsPlaying = true,
                        CurrentFilePath = "left.mp4"
                    };
                    var compareEngine = new TestVideoReviewEngine
                    {
                        IsMediaOpen = true,
                        IsPlaying = true,
                        CurrentFilePath = "right.mp4"
                    };
                    TimeSpan? primarySeekTarget = null;
                    TimeSpan? compareSeekTarget = null;
                    primaryEngine.TimeSought = value => primarySeekTarget = value;
                    compareEngine.TimeSought = value => compareSeekTarget = value;

                    SetPrivateField(window, "_primaryEngine", primaryEngine);
                    SetPrivateField(window, "_compareEngine", compareEngine);
                    SetPrivateField(window, "_primaryLoopRange", CreateLoopRange("pane-primary", "left.mp4"));
                    SetPrivateField(window, "_compareLoopRange", CreateLoopRange("pane-compare", "right.mp4"));
                    SetPrivateField(window, "_isPrimaryLoopPlaybackEnabled", true);
                    SetPrivateField(window, "_isCompareLoopPlaybackEnabled", true);
                    SetPrivateField(window, "_isCompareModeSelected", true);
                    SetPrivateField(window, "_isAllPaneTransportSelected", true);

                    var transportIntentGeneration = (int)InvokePrivate(
                        window,
                        "BeginAllPaneTransportIntent");
                    Assert.True((bool)InvokePrivate(
                        window,
                        "TryBeginSynchronizedFramePresentationAtOffset",
                        transportIntentGeneration,
                        TimeSpan.FromSeconds(-0.5)));

                    await (Task)InvokePrivate(
                        window,
                        "RestartAllPaneLoopPlaybackAsync",
                        primaryEngine,
                        CreateLoopRange("pane-primary", "left.mp4"),
                        GetPrivateField<int>(window, "_primaryLoopRestartGeneration"),
                        compareEngine,
                        CreateLoopRange("pane-compare", "right.mp4"),
                        GetPrivateField<int>(window, "_compareLoopRestartGeneration"),
                        ParsePane("Compare"),
                        transportIntentGeneration);

                    Assert.Equal(TimeSpan.FromSeconds(1.5), primarySeekTarget);
                    Assert.Equal(TimeSpan.FromSeconds(1), compareSeekTarget);
                    Assert.Equal(1, primaryEngine.PlayCallCount);
                    Assert.Equal(1, compareEngine.PlayCallCount);
                }
                finally
                {
                    window.Close();
                }
            });
        }

        [Fact]
        public async Task CompareLoopPlayback_AllPaneRestartLeavesInvalidatedPaneUntouched()
        {
            await _fixture.RunAsync(async () =>
            {
                var window = new MainWindow();
                try
                {
                    var primaryEngine = new TestVideoReviewEngine
                    {
                        IsMediaOpen = true,
                        IsPlaying = true
                    };
                    var compareEngine = new TestVideoReviewEngine
                    {
                        IsMediaOpen = true,
                        IsPlaying = true
                    };
                    SetPrivateField(window, "_primaryEngine", primaryEngine);
                    SetPrivateField(window, "_compareEngine", compareEngine);
                    SetPrivateField(window, "_isPrimaryLoopPlaybackEnabled", true);
                    SetPrivateField(window, "_isCompareLoopPlaybackEnabled", true);
                    SetPrivateField(window, "_isCompareModeSelected", true);
                    SetPrivateField(window, "_isAllPaneTransportSelected", true);

                    var primaryGeneration = GetPrivateField<int>(window, "_primaryLoopRestartGeneration");
                    var compareGeneration = GetPrivateField<int>(window, "_compareLoopRestartGeneration");
                    var transportIntentGeneration = GetPrivateField<int>(window, "_allPaneTransportIntentGeneration");
                    InvokePrivate(window, "InvalidateLoopRestart", ParsePane("Primary"));

                    await (Task)InvokePrivate(
                        window,
                        "RestartAllPaneLoopPlaybackAsync",
                        primaryEngine,
                        CreateLoopRange("pane-primary", "left.mp4"),
                        primaryGeneration,
                        compareEngine,
                        CreateLoopRange("pane-compare", "right.mp4"),
                        compareGeneration,
                        ParsePane("Compare"),
                        transportIntentGeneration);

                    Assert.Equal(0, primaryEngine.PauseCallCount);
                    Assert.Equal(0, primaryEngine.SeekToTimeCallCount);
                    Assert.Equal(0, primaryEngine.PlayCallCount);
                    Assert.True(primaryEngine.IsPlaying);
                    Assert.Equal(1, compareEngine.PauseCallCount);
                    Assert.Equal(1, compareEngine.SeekToTimeCallCount);
                    Assert.Equal(1, compareEngine.PlayCallCount);
                    Assert.True(compareEngine.IsPlaying);
                }
                finally
                {
                    window.Close();
                }
            });
        }

        [Fact]
        public async Task CompareLoopPlayback_SupersededAllPaneRestartDoesNotSeekPanes()
        {
            await _fixture.RunAsync(async () =>
            {
                var window = new MainWindow();
                var primaryPauseCompletion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                var comparePauseCompletion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                try
                {
                    var primaryEngine = new TestVideoReviewEngine
                    {
                        IsMediaOpen = true,
                        IsPlaying = true,
                        PauseCompletion = primaryPauseCompletion
                    };
                    var compareEngine = new TestVideoReviewEngine
                    {
                        IsMediaOpen = true,
                        IsPlaying = true,
                        PauseCompletion = comparePauseCompletion
                    };
                    SetPrivateField(window, "_primaryEngine", primaryEngine);
                    SetPrivateField(window, "_compareEngine", compareEngine);
                    SetPrivateField(window, "_isPrimaryLoopPlaybackEnabled", true);
                    SetPrivateField(window, "_isCompareLoopPlaybackEnabled", true);
                    SetPrivateField(window, "_isCompareModeSelected", true);
                    SetPrivateField(window, "_isAllPaneTransportSelected", true);

                    var primaryGeneration = GetPrivateField<int>(window, "_primaryLoopRestartGeneration");
                    var compareGeneration = GetPrivateField<int>(window, "_compareLoopRestartGeneration");
                    var transportIntentGeneration = GetPrivateField<int>(window, "_allPaneTransportIntentGeneration");
                    var restartTask = (Task)InvokePrivate(
                        window,
                        "RestartAllPaneLoopPlaybackAsync",
                        primaryEngine,
                        CreateLoopRange("pane-primary", "left.mp4"),
                        primaryGeneration,
                        compareEngine,
                        CreateLoopRange("pane-compare", "right.mp4"),
                        compareGeneration,
                        ParsePane("Compare"),
                        transportIntentGeneration);

                    Assert.True(
                        SpinWait.SpinUntil(
                            () => primaryEngine.PauseCallCount == 1 &&
                                compareEngine.PauseCallCount == 1,
                            TimeSpan.FromSeconds(2)),
                        "The all-pane loop restart did not reach its pause boundary.");

                    InvokePrivate(window, "BeginAllPaneTransportIntent");
                    primaryPauseCompletion.TrySetResult(true);
                    comparePauseCompletion.TrySetResult(true);
                    await restartTask.WaitAsync(TimeSpan.FromSeconds(2));

                    Assert.Equal(0, primaryEngine.SeekToTimeCallCount);
                    Assert.Equal(0, compareEngine.SeekToTimeCallCount);
                    Assert.Equal(0, primaryEngine.PlayCallCount);
                    Assert.Equal(0, compareEngine.PlayCallCount);
                }
                finally
                {
                    primaryPauseCompletion.TrySetResult(true);
                    comparePauseCompletion.TrySetResult(true);
                    window.Close();
                }
            });
        }

        [Fact]
        public void CompareLoopPlayback_PaneAndAllPaneRestartsHaveExclusiveOwnership()
        {
            _fixture.Run(() =>
            {
                var window = new MainWindow();
                var primaryPane = ParsePane("Primary");
                var comparePane = ParsePane("Compare");
                try
                {
                    Assert.True((bool)InvokePrivate(window, "TryBeginLoopRestart", primaryPane));
                    Assert.False((bool)InvokePrivate(window, "TryBeginAllPaneLoopRestart"));
                    InvokePrivate(window, "EndLoopRestart", primaryPane);

                    Assert.True((bool)InvokePrivate(window, "TryBeginAllPaneLoopRestart"));
                    Assert.False((bool)InvokePrivate(window, "TryBeginLoopRestart", primaryPane));
                    Assert.False((bool)InvokePrivate(window, "TryBeginLoopRestart", comparePane));
                }
                finally
                {
                    InvokePrivate(window, "EndLoopRestart", primaryPane);
                    InvokePrivate(window, "EndLoopRestart", comparePane);
                    InvokePrivate(window, "EndAllPaneLoopRestart");
                    window.Close();
                }
            });
        }

        [Theory]
        [InlineData(SynchronizedOperationScope.FocusedPane, 1)]
        [InlineData(SynchronizedOperationScope.AllPanes, 0)]
        public async Task CompareLoopPlayback_InterruptedAllPaneRestartHonorsCommandScope(
            SynchronizedOperationScope operationScope,
            int expectedComparePlayCount)
        {
            await _fixture.RunAsync(async () =>
            {
                var window = new MainWindow();
                var primaryPauseCompletion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                var comparePauseCompletion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                try
                {
                    var mediaInfo = new VideoMediaInfo(
                        "compare-loop.mp4",
                        TimeSpan.FromSeconds(10),
                        TimeSpan.FromSeconds(1d / 30d),
                        30d,
                        1920,
                        1080,
                        "h264",
                        0,
                        30,
                        1,
                        1,
                        90_000);
                    var primaryEngine = new TestVideoReviewEngine
                    {
                        IsMediaOpen = true,
                        IsPlaying = true,
                        CurrentFilePath = "left.mp4",
                        MediaInfo = mediaInfo,
                        PauseCompletion = primaryPauseCompletion
                    };
                    var compareEngine = new TestVideoReviewEngine
                    {
                        IsMediaOpen = true,
                        IsPlaying = true,
                        CurrentFilePath = "right.mp4",
                        MediaInfo = mediaInfo,
                        PauseCompletion = comparePauseCompletion
                    };
                    var state = new VideoReviewEngineStateChangedEventArgs(
                        isMediaOpen: true,
                        isPlaying: true,
                        currentFilePath: "left.mp4",
                        lastErrorMessage: string.Empty,
                        mediaInfo: mediaInfo,
                        position: new ReviewPosition(
                            TimeSpan.FromSeconds(2),
                            60,
                            isFrameAccurate: true,
                            isFrameIndexAbsolute: true,
                            presentationTimestamp: 180_000,
                            decodeTimestamp: 180_000));

                    SetPrivateField(window, "_primaryEngine", primaryEngine);
                    SetPrivateField(window, "_compareEngine", compareEngine);
                    SetPrivateField(window, "_primaryLoopRange", CreateLoopRange("pane-primary", "left.mp4"));
                    SetPrivateField(window, "_compareLoopRange", CreateLoopRange("pane-compare", "right.mp4"));
                    SetPrivateField(window, "_isPrimaryLoopPlaybackEnabled", true);
                    SetPrivateField(window, "_isCompareLoopPlaybackEnabled", true);
                    SetPrivateField(window, "_focusedPane", ParsePane("Primary"));
                    RequireControl<CheckBox>(window, "CompareModeCheckBox").IsChecked = true;
                    var transportIntentGeneration = (int)InvokePrivate(
                        window,
                        "BeginAllPaneTransportIntent");
                    Assert.True((bool)InvokePrivate(
                        window,
                        "TryBeginSynchronizedFramePresentation",
                        transportIntentGeneration));
                    SetPrivateField(
                        window,
                        "_isAllPanePlaybackControlActive",
                        true);

                    InvokePrivate(window, "RestartLoopPlaybackIfNeeded", ParsePane("Primary"), state);
                    Assert.True(
                        SpinWait.SpinUntil(
                            () => primaryEngine.PauseCallCount == 1 && compareEngine.PauseCallCount == 1,
                            TimeSpan.FromSeconds(2)),
                        "The synchronized loop restart did not begin on both panes.");

                    var explicitPauseTask = InvokePrivateTask(
                        window,
                        "PausePlaybackAsync",
                        new[] { typeof(bool), typeof(SynchronizedOperationScope?) },
                        true,
                        operationScope);
                    var expectedPrimaryPauseCount = 2;
                    var expectedComparePauseCount = operationScope == SynchronizedOperationScope.AllPanes ? 2 : 1;
                    await Task.Delay(TimeSpan.FromMilliseconds(25));
                    Assert.False(explicitPauseTask.IsCompleted);
                    Assert.Equal(1, primaryEngine.PauseCallCount);
                    Assert.Equal(1, compareEngine.PauseCallCount);

                    primaryPauseCompletion.TrySetResult(true);
                    comparePauseCompletion.TrySetResult(true);
                    await explicitPauseTask;
                    Assert.Equal(expectedPrimaryPauseCount, primaryEngine.PauseCallCount);
                    Assert.Equal(expectedComparePauseCount, compareEngine.PauseCallCount);

                    Assert.True(
                        SpinWait.SpinUntil(
                            () => GetPrivateField<int>(window, "_allPaneLoopRestartInFlight") == 0,
                            TimeSpan.FromSeconds(2)),
                        "The interrupted all-pane loop restart did not finish.");
                    Assert.Equal(0, primaryEngine.PlayCallCount);
                    Assert.Equal(expectedComparePlayCount, compareEngine.PlayCallCount);
                    Assert.False(primaryEngine.IsPlaying);
                    Assert.Equal(
                        operationScope == SynchronizedOperationScope.FocusedPane,
                        compareEngine.IsPlaying);
                }
                finally
                {
                    primaryPauseCompletion.TrySetResult(true);
                    comparePauseCompletion.TrySetResult(true);
                    window.Close();
                }
            });
        }

        [Fact]
        public void CompareLoopPlayback_CrossThreadLoopStateIsVolatile()
        {
            var fieldNames = new[]
            {
                "_primaryLoopRange",
                "_compareLoopRange",
                "_isPrimaryLoopPlaybackEnabled",
                "_isCompareLoopPlaybackEnabled"
            };

            foreach (var fieldName in fieldNames)
            {
                var field = typeof(MainWindow).GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic)
                    ?? throw new MissingFieldException(typeof(MainWindow).FullName, fieldName);

                Assert.Contains(typeof(IsVolatile), field.GetRequiredCustomModifiers());
            }
        }

        [Fact]
        public async Task ComparePlayback_MasterPauseRefreshesMasterStatusReadout()
        {
            await _fixture.RunAsync(async () =>
            {
                var window = new MainWindow();
                try
                {
                    var mediaInfo = new VideoMediaInfo(
                        "compare-pause.mp4",
                        TimeSpan.FromSeconds(20),
                        TimeSpan.FromSeconds(1d / 24d),
                        24d,
                        1920,
                        1080,
                        "h264",
                        0,
                        24,
                        1,
                        1,
                        90_000);
                    var primaryEngine = new TestVideoReviewEngine
                    {
                        IsMediaOpen = true,
                        IsPlaying = true,
                        CurrentFilePath = "left.mp4",
                        MediaInfo = mediaInfo,
                        Position = new ReviewPosition(
                            TimeSpan.FromSeconds(2),
                            48,
                            isFrameAccurate: true,
                            isFrameIndexAbsolute: true,
                            presentationTimestamp: 180_000,
                            decodeTimestamp: 180_000)
                    };
                    var compareEngine = new TestVideoReviewEngine
                    {
                        IsMediaOpen = true,
                        IsPlaying = true,
                        CurrentFilePath = "right.mp4",
                        MediaInfo = mediaInfo,
                        Position = primaryEngine.Position
                    };

                    SetPrivateField(window, "_primaryEngine", primaryEngine);
                    SetPrivateField(window, "_compareEngine", compareEngine);
                    SetPrivateField(window, "_isCompareModeSelected", true);
                    SetPrivateField(window, "_isAllPaneTransportSelected", true);
                    RequireControl<CheckBox>(window, "CompareModeCheckBox").IsChecked = true;
                    RequireControl<TextBlock>(window, "PlaybackStateTextBlock").Text = "Playing";
                    var transportIntentGeneration = (int)InvokePrivate(
                        window,
                        "BeginAllPaneTransportIntent");
                    Assert.True((bool)InvokePrivate(
                        window,
                        "TryBeginSynchronizedFramePresentation",
                        transportIntentGeneration));

                    await InvokePrivateTask(
                            window,
                            "PausePlaybackAsync",
                            new[] { typeof(bool), typeof(SynchronizedOperationScope?) },
                            true,
                            (SynchronizedOperationScope?)SynchronizedOperationScope.AllPanes)
                        .WaitAsync(TimeSpan.FromSeconds(2));

                    Assert.False(primaryEngine.IsPlaying);
                    Assert.False(compareEngine.IsPlaying);
                    Assert.Equal(
                        "Paused",
                        RequireControl<TextBlock>(
                            window,
                            "PlaybackStateTextBlock").Text);
                    Assert.Equal(
                        "49",
                        RequireControl<TextBox>(window, "FrameNumberTextBox").Text);
                }
                finally
                {
                    window.Close();
                }
            });
        }

        [Fact]
        public async Task ComparePlayback_MasterPauseHoldsPresentedOffsetAgainstLateFrames()
        {
            await _fixture.RunAsync(async () =>
            {
                var window = new MainWindow();
                try
                {
                    var mediaInfo = new VideoMediaInfo(
                        "compare-pause.mp4",
                        TimeSpan.FromSeconds(20),
                        TimeSpan.FromSeconds(1d / 24d),
                        24d,
                        1920,
                        1080,
                        "h264",
                        0,
                        24,
                        1,
                        1,
                        90_000);
                    var primaryEngine = new TestVideoReviewEngine
                    {
                        IsMediaOpen = true,
                        IsPlaying = true,
                        CurrentFilePath = "left.mp4",
                        MediaInfo = mediaInfo,
                        Position = new ReviewPosition(
                            TimeSpan.FromSeconds(5),
                            120,
                            isFrameAccurate: true,
                            isFrameIndexAbsolute: true,
                            presentationTimestamp: 450_000,
                            decodeTimestamp: 450_000)
                    };
                    var compareEngine = new TestVideoReviewEngine
                    {
                        IsMediaOpen = true,
                        IsPlaying = true,
                        CurrentFilePath = "right.mp4",
                        MediaInfo = mediaInfo,
                        Position = primaryEngine.Position
                    };

                    SetPrivateField(window, "_primaryEngine", primaryEngine);
                    SetPrivateField(window, "_compareEngine", compareEngine);
                    SetPrivateField(window, "_isCompareModeSelected", true);
                    SetPrivateField(window, "_isAllPaneTransportSelected", true);
                    RequireControl<CheckBox>(window, "CompareModeCheckBox").IsChecked = true;
                    var rawPlayingState = new VideoReviewEngineStateChangedEventArgs(
                        isMediaOpen: true,
                        isPlaying: true,
                        currentFilePath: "left.mp4",
                        lastErrorMessage: string.Empty,
                        mediaInfo: mediaInfo,
                        position: primaryEngine.Position);
                    InvokePrivate(
                        window,
                        "ApplyState",
                        ParsePane("Primary"),
                        rawPlayingState);
                    InvokePrivate(
                        window,
                        "ApplyState",
                        ParsePane("Compare"),
                        rawPlayingState);

                    using var primaryPresentedFrame = CreateFrameBuffer(
                        8,
                        4,
                        TimeSpan.FromSeconds(2),
                        255,
                        0,
                        0);
                    using var comparePresentedFrame = CreateFrameBuffer(
                        8,
                        4,
                        TimeSpan.FromSeconds(2.5),
                        0,
                        255,
                        0);
                    InvokePrivate(
                        window,
                        "SetPaneBitmap",
                        ParsePane("Primary"),
                        primaryPresentedFrame);
                    InvokePrivate(
                        window,
                        "SetPaneBitmap",
                        ParsePane("Compare"),
                        comparePresentedFrame);
                    InvokePrivate(
                        window,
                        "ApplyPresentedFramePosition",
                        ParsePane("Primary"),
                        primaryPresentedFrame.Descriptor,
                        true);
                    InvokePrivate(
                        window,
                        "ApplyPresentedFramePosition",
                        ParsePane("Compare"),
                        comparePresentedFrame.Descriptor,
                        false);
                    var transportIntentGeneration = (int)InvokePrivate(
                        window,
                        "BeginAllPaneTransportIntent");
                    Assert.True((bool)InvokePrivate(
                        window,
                        "TryBeginSynchronizedFramePresentation",
                        transportIntentGeneration));

                    await InvokePrivateTask(
                            window,
                            "PausePlaybackAsync",
                            new[] { typeof(bool), typeof(SynchronizedOperationScope?) },
                            true,
                            (SynchronizedOperationScope?)SynchronizedOperationScope.AllPanes)
                        .WaitAsync(TimeSpan.FromSeconds(2));

                    using var latePrimaryFrame = CreateFrameBuffer(
                        8,
                        4,
                        TimeSpan.FromSeconds(5),
                        0,
                        0,
                        255);
                    using var lateCompareFrame = CreateFrameBuffer(
                        8,
                        4,
                        TimeSpan.FromSeconds(5),
                        0,
                        0,
                        255);
                    InvokePrivate(
                        window,
                        "QueueFramePresentation",
                        ParsePane("Primary"),
                        latePrimaryFrame);
                    InvokePrivate(
                        window,
                        "QueueFramePresentation",
                        ParsePane("Compare"),
                        lateCompareFrame);
                    await Task.Delay(TimeSpan.FromMilliseconds(50));
                    Dispatcher.UIThread.RunJobs();

                    Assert.Equal(
                        2d,
                        RequireControl<Slider>(
                            window,
                            "PrimaryPanePositionSlider").Value,
                        precision: 3);
                    Assert.Equal(
                        2.5d,
                        RequireControl<Slider>(
                            window,
                            "ComparePanePositionSlider").Value,
                        precision: 3);
                    Assert.Equal(
                        2d,
                        RequireControl<Slider>(
                            window,
                            "PositionSlider").Value,
                        precision: 3);
                }
                finally
                {
                    window.Close();
                }
            });
        }

        [Fact]
        public async Task LoopPlayback_ExplicitPausePreventsInFlightRestartFromResumingPane()
        {
            await _fixture.RunAsync(async () =>
            {
                var window = new MainWindow();
                var pauseCompletion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                try
                {
                    var mediaInfo = new VideoMediaInfo(
                        "left-loop.mp4",
                        TimeSpan.FromSeconds(10),
                        TimeSpan.FromSeconds(1d / 30d),
                        30d,
                        1920,
                        1080,
                        "h264",
                        0,
                        30,
                        1,
                        1,
                        90_000);
                    var primaryEngine = new TestVideoReviewEngine
                    {
                        IsMediaOpen = true,
                        IsPlaying = true,
                        CurrentFilePath = "left-loop.mp4",
                        MediaInfo = mediaInfo,
                        PauseCompletion = pauseCompletion
                    };
                    var state = new VideoReviewEngineStateChangedEventArgs(
                        isMediaOpen: true,
                        isPlaying: true,
                        currentFilePath: "left-loop.mp4",
                        lastErrorMessage: string.Empty,
                        mediaInfo: mediaInfo,
                        position: new ReviewPosition(
                            TimeSpan.FromSeconds(2),
                            60,
                            isFrameAccurate: true,
                            isFrameIndexAbsolute: true,
                            presentationTimestamp: 180_000,
                            decodeTimestamp: 180_000));

                    SetPrivateField(window, "_primaryEngine", primaryEngine);
                    SetPrivateField(window, "_primaryLoopRange", CreateLoopRange("pane-primary", "left-loop.mp4"));
                    SetPrivateField(window, "_isPrimaryLoopPlaybackEnabled", true);

                    InvokePrivate(window, "RestartLoopPlaybackIfNeeded", ParsePane("Primary"), state);
                    Assert.True(
                        SpinWait.SpinUntil(() => primaryEngine.PauseCallCount == 1, TimeSpan.FromSeconds(2)),
                        "The left pane did not begin its loop restart.");

                    var explicitPauseTask = InvokePrivateTask(window, "PausePlaybackAsync", new[] { typeof(bool) }, true);
                    await Task.Delay(TimeSpan.FromMilliseconds(25));
                    Assert.False(explicitPauseTask.IsCompleted);
                    Assert.Equal(1, primaryEngine.PauseCallCount);

                    pauseCompletion.TrySetResult(true);
                    await explicitPauseTask;
                    Assert.Equal(2, primaryEngine.PauseCallCount);

                    Assert.True(
                        SpinWait.SpinUntil(
                            () => GetPrivateField<int>(window, "_primaryLoopRestartInFlight") == 0,
                            TimeSpan.FromSeconds(2)),
                        "The canceled left-pane loop restart did not finish.");
                    Assert.False(primaryEngine.IsPlaying);
                    Assert.Equal(0, primaryEngine.PlayCallCount);
                }
                finally
                {
                    pauseCompletion.TrySetResult(true);
                    window.Close();
                }
            });
        }

        [Fact]
        public void MainSharedTransport_MasterVisualTracksAllPanePauseRule()
        {
            var mainWindowSource = ReadRepositoryFile(
                "src",
                "FramePlayer.Avalonia",
                "Views",
                "MainWindow.axaml.cs");
            var applyPrimaryMethod = ExtractMethodBody(
                mainWindowSource,
                "private void ApplyPrimaryState(",
                "private void ApplyCompareState(");
            var visualMethod = ExtractMethodBody(
                mainWindowSource,
                "private void UpdateMainPlayPauseVisual()",
                "private bool ShouldShowMainPauseAction()");
            var visualRuleMethod = ExtractMethodBody(
                mainWindowSource,
                "private bool ShouldShowMainPauseAction()",
                "private static string FormatFrameNumberEntry(");

            Assert.Contains("UpdateMainPlayPauseVisual();", applyPrimaryMethod, StringComparison.Ordinal);
            Assert.DoesNotContain("\n            PlayPausePlayIcon.IsVisible = !state.IsPlaying;", applyPrimaryMethod, StringComparison.Ordinal);
            Assert.Contains("ShouldShowMainPauseAction()", visualMethod, StringComparison.Ordinal);
            Assert.Contains("return ShouldPauseAllPanePlayback();", visualRuleMethod, StringComparison.Ordinal);
        }

        [Fact]
        public void ComparePlayback_LeftPaneStateDoesNotDriveMasterReadoutsOutsideSynchronizedPresentation()
        {
            var mainWindowSource = ReadRepositoryFile(
                "src",
                "FramePlayer.Avalonia",
                "Views",
                "MainWindow.axaml.cs");
            var applyPrimaryStateMethod = ExtractMethodBody(
                mainWindowSource,
                "private void ApplyPrimaryState(",
                "private void ApplyCompareState(");
            var applyPanePositionMethod = ExtractMethodBody(
                mainWindowSource,
                "private void ApplyPanePosition(",
                "private static ReviewPosition CreateReviewPosition(");
            var synchronizedPresentationMethod = ExtractMethodBody(
                mainWindowSource,
                "private void PresentPendingSynchronizedFrames(",
                "private (DecodedFrameBuffer? Primary, DecodedFrameBuffer? Compare) TakeSynchronizedFramePair(");
            var primaryMasterStateMethod = ExtractMethodBody(
                mainWindowSource,
                "private bool ShouldApplyPrimaryStateToMasterTransport()",
                "private bool ShouldApplyPaneStateToMasterTransport(");
            var paneMasterStateMethod = ExtractMethodBody(
                mainWindowSource,
                "private bool ShouldApplyPaneStateToMasterTransport(",
                "private static string FormatFrameNumberEntry(");

            Assert.Contains(
                "if (ShouldApplyPrimaryStateToMasterTransport())",
                applyPrimaryStateMethod,
                StringComparison.Ordinal);
            Assert.Contains(
                "ShouldApplyPaneStateToMasterTransport(Pane.Compare)",
                applyPanePositionMethod,
                StringComparison.Ordinal);
            Assert.Contains(
                "ApplyMasterTransportPosition(position);",
                applyPanePositionMethod,
                StringComparison.Ordinal);
            Assert.Contains(
                "var masterPane = GetMasterTransportPane();",
                synchronizedPresentationMethod,
                StringComparison.Ordinal);
            Assert.Contains(
                "applyMasterTransport: masterPane == Pane.Primary",
                synchronizedPresentationMethod,
                StringComparison.Ordinal);
            Assert.Contains(
                "applyMasterTransport: masterPane == Pane.Compare",
                synchronizedPresentationMethod,
                StringComparison.Ordinal);
            Assert.Contains(
                "return ShouldApplyPaneStateToMasterTransport(Pane.Primary);",
                primaryMasterStateMethod,
                StringComparison.Ordinal);
            Assert.Contains(
                "if (pane != GetMasterTransportPane())",
                paneMasterStateMethod,
                StringComparison.Ordinal);
            Assert.Contains(
                "Volatile.Read(ref _isAllPanePlaybackControlActive)",
                paneMasterStateMethod,
                StringComparison.Ordinal);
        }

        [Fact]
        public void MainSharedTransport_MasterTimelineQueuesThrottledSeekBeforeResuming()
        {
            var mainWindowSource = ReadRepositoryFile(
                "src",
                "FramePlayer.Avalonia",
                "Views",
                "MainWindow.axaml.cs");
            var masterSliderMethod = ExtractMethodBody(
                mainWindowSource,
                "private void PositionSlider_ValueChanged(",
                "private void QueueSliderScrub(");
            var scrubTimerMethod = ExtractMethodBody(
                mainWindowSource,
                "private async void SliderScrubTimer_Tick(",
                "private void PanePositionSlider_ValueChanged(");
            var queueSliderScrubMethod = ExtractMethodBody(
                mainWindowSource,
                "private void QueueSliderScrub(",
                "private async void SliderScrubTimer_Tick(");
            var commitSliderMethod = ExtractMethodBody(
                mainWindowSource,
                "private async Task CommitSliderSeekAsync(",
                "private void PositionSlider_ValueChanged(");
            var cancelQueuedScrubsMethod = ExtractMethodBody(
                mainWindowSource,
                "private void CancelQueuedSliderScrubs(",
                "private async void PaneSliderScrubTimer_Tick(");
            var paneScrubTimerMethod = ExtractMethodBody(
                mainWindowSource,
                "private async void PaneSliderScrubTimer_Tick(",
                "private async Task SeekPaneToTimePreservingPlaybackAsync(");
            var masterSeekMethod = ExtractMethodBody(
                mainWindowSource,
                "private async Task SeekMasterTimelineAsync(",
                "private async Task SeekAllPaneRelativePreservingPlaybackAsync(");
            var allPaneSeekMethod = ExtractMethodBody(
                mainWindowSource,
                "private async Task SeekAllPaneToTimesPreservingPlaybackCoreAsync(",
                "private static TimeSpan ClampSeekTarget(");

            Assert.Contains("QueueSliderScrub(TimeSpan.FromSeconds(PositionSlider.Value));", masterSliderMethod, StringComparison.Ordinal);
            var timerSeekIndex = scrubTimerMethod.IndexOf(
                "var seekTask = SeekMasterTimelineAsync(target, _sliderScrubCts.Token);",
                StringComparison.Ordinal);
            var timerAwaitIndex = scrubTimerMethod.IndexOf("await seekTask;", StringComparison.Ordinal);
            Assert.True(timerSeekIndex >= 0 && timerSeekIndex < timerAwaitIndex);
            Assert.DoesNotContain(
                "BeginAllPanePreservingSeekIntent",
                queueSliderScrubMethod,
                StringComparison.Ordinal);
            Assert.DoesNotContain(
                "BeginPanePreservingSeekIntent",
                queueSliderScrubMethod,
                StringComparison.Ordinal);
            Assert.DoesNotContain(
                "_sliderScrubCts?.Cancel()",
                queueSliderScrubMethod,
                StringComparison.Ordinal);
            Assert.Contains(
                "lock (_sliderScrubStateLock)",
                scrubTimerMethod,
                StringComparison.Ordinal);
            var commitCancelIndex = commitSliderMethod.IndexOf(
                "CancelQueuedSliderScrubsCore(clearResumeReservations: false);",
                StringComparison.Ordinal);
            var commitSeekIndex = commitSliderMethod.IndexOf(
                "var seekTask = SeekMasterTimelineAsync(target, CancellationToken.None);",
                StringComparison.Ordinal);
            var commitHandoffIndex = commitSliderMethod.IndexOf(
                "ClearAllQueuedSliderScrubResumeReservations();",
                StringComparison.Ordinal);
            var commitAwaitIndex = commitSliderMethod.IndexOf(
                "await seekTask.ConfigureAwait(false);",
                StringComparison.Ordinal);
            Assert.True(commitCancelIndex >= 0 && commitCancelIndex < commitSeekIndex);
            Assert.True(commitSeekIndex < commitHandoffIndex);
            Assert.True(commitHandoffIndex < commitAwaitIndex);
            Assert.Contains("_hasPendingSliderScrubTarget = false;", cancelQueuedScrubsMethod, StringComparison.Ordinal);
            Assert.Contains("_hasPendingPaneSliderScrubTarget = false;", cancelQueuedScrubsMethod, StringComparison.Ordinal);
            Assert.Contains("_sliderScrubTimer.Stop();", cancelQueuedScrubsMethod, StringComparison.Ordinal);
            Assert.Contains("_paneSliderScrubTimer.Stop();", cancelQueuedScrubsMethod, StringComparison.Ordinal);
            Assert.Contains("await seekTask;", paneScrubTimerMethod, StringComparison.Ordinal);
            Assert.DoesNotContain(
                "await seekTask.ConfigureAwait(false);",
                paneScrubTimerMethod,
                StringComparison.Ordinal);
            Assert.Contains("if (IsAllPaneTransportEnabled)", masterSeekMethod, StringComparison.Ordinal);
            Assert.Contains(
                "await SeekAllPaneToTimePreservingPlaybackAsync(target, cancellationToken).ConfigureAwait(false);",
                masterSeekMethod,
                StringComparison.Ordinal);
            var tryIndex = allPaneSeekMethod.IndexOf("try", StringComparison.Ordinal);
            var seekIndex = allPaneSeekMethod.IndexOf("await Task.WhenAll(", StringComparison.Ordinal);
            var finallyIndex = allPaneSeekMethod.IndexOf("finally", StringComparison.Ordinal);
            var resumeIndex = allPaneSeekMethod.IndexOf("await ResumePlaybackForIntentAsync(", StringComparison.Ordinal);
            Assert.NotEqual(-1, tryIndex);
            Assert.NotEqual(-1, seekIndex);
            Assert.NotEqual(-1, finallyIndex);
            Assert.NotEqual(-1, resumeIndex);
            Assert.Contains("Task.Run(() => _primaryEngine.SeekToTimeAsync(primaryTarget, cancellationToken), cancellationToken)", allPaneSeekMethod, StringComparison.Ordinal);
            Assert.Contains("Task.Run(() => compareEngine.SeekToTimeAsync(compareTarget, cancellationToken), cancellationToken)", allPaneSeekMethod, StringComparison.Ordinal);
            Assert.Contains("await ResumePlaybackForIntentAsync(", allPaneSeekMethod, StringComparison.Ordinal);
            Assert.Contains("await PauseAllPanePlaybackAsync().ConfigureAwait(false);", allPaneSeekMethod, StringComparison.Ordinal);
            Assert.Contains(".ConfigureAwait(false);", allPaneSeekMethod, StringComparison.Ordinal);
            Assert.Contains("UpdateCommandStatesOnUiThread();", allPaneSeekMethod, StringComparison.Ordinal);
            Assert.DoesNotContain("Task.Run(() => _primaryEngine.PlayAsync(), cancellationToken)", allPaneSeekMethod, StringComparison.Ordinal);
            Assert.DoesNotContain("Task.Run(() => compareEngine.PlayAsync(), cancellationToken)", allPaneSeekMethod, StringComparison.Ordinal);
            Assert.Contains(
                "ShouldDeferAllPaneResumeForPendingSliderScrub()",
                allPaneSeekMethod,
                StringComparison.Ordinal);
            Assert.Contains(
                "lock (_sliderScrubStateLock)",
                mainWindowSource,
                StringComparison.Ordinal);
            Assert.True(tryIndex < seekIndex);
            Assert.True(seekIndex < finallyIndex);
            Assert.True(finallyIndex < resumeIndex);
        }

        [Fact]
        public async Task MainSharedTransport_CanceledSharedSeekRestoresPlayingPanes()
        {
            await _fixture.RunAsync(async () =>
            {
                var window = new MainWindow();
                try
                {
                    var primaryEngine = new TestVideoReviewEngine
                    {
                        IsMediaOpen = true,
                        IsPlaying = true
                    };
                    var compareEngine = new TestVideoReviewEngine
                    {
                        IsMediaOpen = true,
                        IsPlaying = true
                    };
                    using var seekCancellation = new CancellationTokenSource();

                    SetPrivateField(window, "_primaryEngine", primaryEngine);
                    SetPrivateField(window, "_compareEngine", compareEngine);
                    SetPrivateField(window, "_isCompareModeSelected", true);
                    SetPrivateField(window, "_isAllPaneTransportSelected", true);
                    seekCancellation.Cancel();

                    await Assert.ThrowsAsync<TaskCanceledException>(
                        async () => await (Task)InvokePrivate(
                            window,
                            "SeekAllPaneToTimesPreservingPlaybackAsync",
                            TimeSpan.FromSeconds(1),
                            TimeSpan.FromSeconds(2),
                            seekCancellation.Token));

                    Assert.True(primaryEngine.IsPlaying);
                    Assert.True(compareEngine.IsPlaying);
                    Assert.Equal(1, primaryEngine.PauseCallCount);
                    Assert.Equal(1, compareEngine.PauseCallCount);
                    Assert.Equal(1, primaryEngine.PlayCallCount);
                    Assert.Equal(1, compareEngine.PlayCallCount);
                    Assert.False(GetPrivateField<bool>(
                        window,
                        "_isSynchronizedFramePresentationActive"));
                }
                finally
                {
                    window.Close();
                }
            });
        }

        [Fact]
        public async Task MainSharedTransport_PartialTimeSeekFailureEndsSynchronizedPresentation()
        {
            await _fixture.RunAsync(async () =>
            {
                var window = new MainWindow();
                var primarySeekCompletion = new TaskCompletionSource<bool>(
                    TaskCreationOptions.RunContinuationsAsynchronously);
                try
                {
                    var primaryEngine = new TestVideoReviewEngine
                    {
                        IsMediaOpen = true,
                        IsPlaying = true,
                        SeekTimeCompletion = primarySeekCompletion
                    };
                    var compareEngine = new TestVideoReviewEngine
                    {
                        IsMediaOpen = true,
                        IsPlaying = true
                    };
                    SetPrivateField(window, "_primaryEngine", primaryEngine);
                    SetPrivateField(window, "_compareEngine", compareEngine);
                    SetPrivateField(window, "_isCompareModeSelected", true);
                    SetPrivateField(window, "_isAllPaneTransportSelected", true);
                    primarySeekCompletion.TrySetException(
                        new InvalidOperationException("Expected primary time seek failure."));

                    var error = await Assert.ThrowsAsync<InvalidOperationException>(
                        async () => await (Task)InvokePrivate(
                            window,
                            "SeekAllPaneToTimesPreservingPlaybackAsync",
                            TimeSpan.FromSeconds(1),
                            TimeSpan.FromSeconds(2),
                            CancellationToken.None));

                    Assert.Equal(
                        "Expected primary time seek failure.",
                        error.Message);
                    Assert.Equal(
                        TimeSpan.Zero,
                        primaryEngine.Position.PresentationTime);
                    Assert.Equal(
                        TimeSpan.FromSeconds(2),
                        compareEngine.Position.PresentationTime);
                    Assert.True(primaryEngine.IsPlaying);
                    Assert.True(compareEngine.IsPlaying);
                    Assert.Equal(1, primaryEngine.PlayCallCount);
                    Assert.Equal(1, compareEngine.PlayCallCount);
                    Assert.False(GetPrivateField<bool>(
                        window,
                        "_isSynchronizedFramePresentationActive"));
                }
                finally
                {
                    primarySeekCompletion.TrySetResult(true);
                    window.Close();
                }
            });
        }

        [Fact]
        public async Task MainSharedTransport_PartialFrameSeekFailureEndsSynchronizedPresentation()
        {
            await _fixture.RunAsync(async () =>
            {
                var window = new MainWindow();
                var primarySeekCompletion = new TaskCompletionSource<bool>(
                    TaskCreationOptions.RunContinuationsAsynchronously);
                try
                {
                    var primaryEngine = new TestVideoReviewEngine
                    {
                        IsMediaOpen = true,
                        IsPlaying = true,
                        SeekFrameCompletion = primarySeekCompletion
                    };
                    var compareEngine = new TestVideoReviewEngine
                    {
                        IsMediaOpen = true,
                        IsPlaying = true
                    };
                    SetPrivateField(window, "_primaryEngine", primaryEngine);
                    SetPrivateField(window, "_compareEngine", compareEngine);
                    SetPrivateField(window, "_isCompareModeSelected", true);
                    SetPrivateField(window, "_isAllPaneTransportSelected", true);
                    primarySeekCompletion.TrySetException(
                        new InvalidOperationException("Expected primary frame seek failure."));

                    var error = await Assert.ThrowsAsync<InvalidOperationException>(
                        async () => await (Task)InvokePrivate(
                            window,
                            "SeekAllPaneToFramePreservingPlaybackAsync",
                            42L));

                    Assert.Equal(
                        "Expected primary frame seek failure.",
                        error.Message);
                    Assert.Null(primaryEngine.Position.FrameIndex);
                    Assert.Equal(42L, compareEngine.Position.FrameIndex);
                    Assert.True(primaryEngine.IsPlaying);
                    Assert.True(compareEngine.IsPlaying);
                    Assert.Equal(1, primaryEngine.PlayCallCount);
                    Assert.Equal(1, compareEngine.PlayCallCount);
                    Assert.False(GetPrivateField<bool>(
                        window,
                        "_isSynchronizedFramePresentationActive"));
                }
                finally
                {
                    primarySeekCompletion.TrySetResult(true);
                    window.Close();
                }
            });
        }

        [Fact]
        public void CloseVideosAsync_CancelsQueuedSliderScrubsBeforeClosingEngines()
        {
            var mainWindowSource = ReadRepositoryFile(
                "src",
                "FramePlayer.Avalonia",
                "Views",
                "MainWindow.axaml.cs");
            var closeVideosMethod = ExtractMethodBody(
                mainWindowSource,
                "private async Task CloseVideosAsync()",
                "private async void PlayPauseButton_Click(");

            Assert.Contains("CancelQueuedSliderScrubs();", closeVideosMethod, StringComparison.Ordinal);
            Assert.True(
                closeVideosMethod.IndexOf("CancelQueuedSliderScrubs();", StringComparison.Ordinal) <
                closeVideosMethod.IndexOf("await _primaryEngine.CloseAsync();", StringComparison.Ordinal));
        }

        [Fact]
        public void MainSharedTransport_MasterFrameEntrySeeksBothPanes()
        {
            var mainWindowSource = ReadRepositoryFile(
                "src",
                "FramePlayer.Avalonia",
                "Views",
                "MainWindow.axaml.cs");
            var frameEntryMethod = ExtractMethodBody(
                mainWindowSource,
                "private async void FrameNumberTextBox_KeyDown(",
                "private async Task SeekAllPaneToFramePreservingPlaybackAsync(");
            var allPaneFrameSeekMethod = ExtractMethodBody(
                mainWindowSource,
                "private async Task SeekAllPaneToFramePreservingPlaybackAsync(",
                "private void SetLoopMarker(");

            Assert.Contains("textBox == FrameNumberTextBox && IsAllPaneTransportEnabled", frameEntryMethod, StringComparison.Ordinal);
            var frameSeekIndex = frameEntryMethod.IndexOf(
                "var seekTask = SeekAllPaneToFramePreservingPlaybackAsync(oneBasedFrame - 1);",
                StringComparison.Ordinal);
            var frameHandoffIndex = frameEntryMethod.IndexOf(
                "ClearAllQueuedSliderScrubResumeReservations();",
                StringComparison.Ordinal);
            var frameAwaitIndex = frameEntryMethod.IndexOf("await seekTask;", StringComparison.Ordinal);
            Assert.True(frameSeekIndex >= 0 && frameSeekIndex < frameHandoffIndex);
            Assert.True(frameHandoffIndex < frameAwaitIndex);
            Assert.Contains("await Task.WhenAll(", allPaneFrameSeekMethod, StringComparison.Ordinal);
            Assert.Contains(
                "_primaryEngine.SeekToFrameAsync(targetFrameIndex, CancellationToken.None)",
                allPaneFrameSeekMethod,
                StringComparison.Ordinal);
            Assert.Contains(
                "compareEngine.SeekToFrameAsync(targetFrameIndex, CancellationToken.None)",
                allPaneFrameSeekMethod,
                StringComparison.Ordinal);
            Assert.Contains("await PauseAllPanePlaybackAsync().ConfigureAwait(false);", allPaneFrameSeekMethod, StringComparison.Ordinal);
            Assert.Contains(".ConfigureAwait(false);", allPaneFrameSeekMethod, StringComparison.Ordinal);
            Assert.Contains("UpdateCommandStatesOnUiThread();", allPaneFrameSeekMethod, StringComparison.Ordinal);
            Assert.Contains("finally", allPaneFrameSeekMethod, StringComparison.Ordinal);
            Assert.True(
                allPaneFrameSeekMethod.IndexOf("await Task.WhenAll(", StringComparison.Ordinal) <
                allPaneFrameSeekMethod.IndexOf("await ResumePlaybackForIntentAsync(", StringComparison.Ordinal));
        }

        [Fact]
        public void PaneFrameStep_CancelsQueuedSliderScrubsBeforeStepping()
        {
            var mainWindowSource = ReadRepositoryFile(
                "src",
                "FramePlayer.Avalonia",
                "Views",
                "MainWindow.axaml.cs");
            var paneStepMethod = ExtractMethodBody(
                mainWindowSource,
                "private async Task StepFrameAsync(int delta, Pane pane)",
                "private Pane ResolvePaneFromSender(");

            Assert.Contains("CancelQueuedSliderScrubs();", paneStepMethod, StringComparison.Ordinal);
            Assert.True(
                paneStepMethod.IndexOf("CancelQueuedSliderScrubs();", StringComparison.Ordinal) <
                paneStepMethod.IndexOf("var engine = GetEngine(pane);", StringComparison.Ordinal));
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
                    foreach (var buttonName in TransportButtonNames)
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
        public void CompareLoopPlayback_UsesOnlyTheMainLoopControl()
        {
            _fixture.Run(() =>
            {
                var window = new MainWindow();
                try
                {
                    var mainLoopStatus = RequireControl<Button>(window, "LoopStatusButton");
                    var mainTransportParent = Assert.IsType<StackPanel>(RequireControl<Button>(window, "PlayPauseButton").Parent);
                    Assert.Equal(240, mainLoopStatus.Width);
                    Assert.Equal(1, Grid.GetRow(mainLoopStatus));
                    Assert.Equal(1, Grid.GetColumn(mainLoopStatus));
                    Assert.Equal(2, Grid.GetRow(mainTransportParent));
                    Assert.Equal(2, Grid.GetColumnSpan(mainTransportParent));

                    Assert.Equal("Toggle Unified Loop Playback (L)", ToolTip.GetTip(mainLoopStatus));
                    Assert.Null(window.FindControl<Button>("PrimaryPaneLoopStatusButton"));
                    Assert.Null(window.FindControl<Button>("ComparePaneLoopStatusButton"));
                }
                finally
                {
                    window.Close();
                }
            });
        }

        [Fact]
        public void MainLoopStatus_TogglesLoopPlaybackForLoadedSinglePane()
        {
            _fixture.Run(() =>
            {
                var window = new MainWindow();
                try
                {
                    SetPrivateField(
                        window,
                        "_primaryEngine",
                        new TestVideoReviewEngine
                        {
                            IsMediaOpen = true,
                            CurrentFilePath = "/tmp/review.mp4"
                        });
                    InvokePrivate(window, "UpdateCommandStates");
                    var loopStatus = RequireControl<Button>(window, "LoopStatusButton");

                    loopStatus.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

                    Assert.Equal("Loop: full media", RequireControl<TextBlock>(window, "LoopStatusTextBlock").Text);
                    Assert.True(RequireControl<MenuItem>(window, "LoopPlaybackMenuItem").IsChecked);
                }
                finally
                {
                    window.Close();
                }
            });
        }

        [Fact]
        public void CompareLoopPlayback_RejectsPaneLocalEnablement()
        {
            _fixture.Run(() =>
            {
                var window = new MainWindow();
                try
                {
                    SetPrivateField(
                        window,
                        "_primaryEngine",
                        new TestVideoReviewEngine
                        {
                            IsMediaOpen = true,
                            CurrentFilePath = "/tmp/left.mp4"
                        });
                    SetPrivateField(
                        window,
                        "_compareEngine",
                        new TestVideoReviewEngine
                        {
                            IsMediaOpen = true,
                            CurrentFilePath = "/tmp/right.mp4"
                        });
                    RequireControl<CheckBox>(window, "CompareModeCheckBox").IsChecked = true;
                    InvokePrivate(window, "UpdateCommandStates");
                    Assert.Null(window.FindControl<Button>("PrimaryPaneLoopStatusButton"));
                    Assert.Null(window.FindControl<Button>("ComparePaneLoopStatusButton"));

                    InvokePrivate(
                        window,
                        "SetPaneLoopPlaybackEnabled",
                        ParsePane("Primary"),
                        true);

                    Assert.True(GetPrivateField<bool>(window, "_isPrimaryLoopPlaybackEnabled"));
                    Assert.True(GetPrivateField<bool>(window, "_isCompareLoopPlaybackEnabled"));
                    Assert.Equal("Loop: both on", RequireControl<TextBlock>(window, "LoopStatusTextBlock").Text);
                    Assert.True(RequireControl<MenuItem>(window, "LoopPlaybackMenuItem").IsChecked);

                    InvokePrivate(
                        window,
                        "SetPaneLoopPlaybackEnabled",
                        ParsePane("Compare"),
                        false);

                    Assert.False(GetPrivateField<bool>(window, "_isPrimaryLoopPlaybackEnabled"));
                    Assert.False(GetPrivateField<bool>(window, "_isCompareLoopPlaybackEnabled"));
                    Assert.Equal("Loop: off", RequireControl<TextBlock>(window, "LoopStatusTextBlock").Text);
                    Assert.False(RequireControl<MenuItem>(window, "LoopPlaybackMenuItem").IsChecked);
                }
                finally
                {
                    window.Close();
                }
            });
        }

        [Fact]
        public void CompareMode_HidesPaneTimelineLoopPlaybackToggle()
        {
            _fixture.Run(() =>
            {
                var window = new MainWindow();
                try
                {
                    SetPrivateField(
                        window,
                        "_primaryEngine",
                        new TestVideoReviewEngine { IsMediaOpen = true });
                    SetPrivateField(
                        window,
                        "_compareEngine",
                        new TestVideoReviewEngine { IsMediaOpen = true });
                    RequireControl<CheckBox>(window, "CompareModeCheckBox").IsChecked = true;

                    var timeline = RequireControl<Slider>(
                        window,
                        "PrimaryPanePositionSlider");
                    var menu = Assert.IsType<ContextMenu>(timeline.ContextMenu);
                    menu.Open(timeline);

                    var loopPlaybackItem = Assert.Single(
                        menu.Items
                            .OfType<MenuItem>()
                            .Where(item => string.Equals(
                                item.Header?.ToString(),
                                "Loop Playback",
                                StringComparison.Ordinal)));
                    Assert.False(loopPlaybackItem.IsVisible);
                    menu.Close();
                }
                finally
                {
                    window.Close();
                }
            });
        }

        [Fact]
        public void CompareMode_UsesPrimaryLoopStateToNormalizeLegacyPaneState()
        {
            _fixture.Run(() =>
            {
                var window = new MainWindow();
                try
                {
                    SetPrivateField(
                        window,
                        "_primaryEngine",
                        new TestVideoReviewEngine { IsMediaOpen = true });
                    SetPrivateField(
                        window,
                        "_compareEngine",
                        new TestVideoReviewEngine { IsMediaOpen = true });
                    SetPrivateField(window, "_isPrimaryLoopPlaybackEnabled", true);
                    SetPrivateField(window, "_isCompareLoopPlaybackEnabled", false);

                    RequireControl<CheckBox>(window, "CompareModeCheckBox").IsChecked = true;

                    Assert.True(GetPrivateField<bool>(window, "_isPrimaryLoopPlaybackEnabled"));
                    Assert.True(GetPrivateField<bool>(window, "_isCompareLoopPlaybackEnabled"));
                    Assert.Equal("Loop: both on", RequireControl<TextBlock>(window, "LoopStatusTextBlock").Text);
                }
                finally
                {
                    window.Close();
                }
            });
        }

        [Fact]
        public void CompareMainLoopStatus_UnifiesBothLoadedPanes()
        {
            _fixture.Run(() =>
            {
                var window = new MainWindow();
                try
                {
                    SetPrivateField(
                        window,
                        "_primaryEngine",
                        new TestVideoReviewEngine
                        {
                            IsMediaOpen = true,
                            CurrentFilePath = "/tmp/left.mp4"
                        });
                    SetPrivateField(
                        window,
                        "_compareEngine",
                        new TestVideoReviewEngine
                        {
                            IsMediaOpen = true,
                            CurrentFilePath = "/tmp/right.mp4"
                        });
                    RequireControl<CheckBox>(window, "CompareModeCheckBox").IsChecked = true;
                    InvokePrivate(window, "UpdateCommandStates");
                    var mainLoopStatus = RequireControl<Button>(window, "LoopStatusButton");

                    mainLoopStatus.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

                    Assert.True(GetPrivateField<bool>(window, "_isPrimaryLoopPlaybackEnabled"));
                    Assert.True(GetPrivateField<bool>(window, "_isCompareLoopPlaybackEnabled"));
                    Assert.Equal("Loop: both on", RequireControl<TextBlock>(window, "LoopStatusTextBlock").Text);
                    Assert.True(RequireControl<MenuItem>(window, "LoopPlaybackMenuItem").IsChecked);

                    mainLoopStatus.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

                    Assert.False(GetPrivateField<bool>(window, "_isPrimaryLoopPlaybackEnabled"));
                    Assert.False(GetPrivateField<bool>(window, "_isCompareLoopPlaybackEnabled"));
                    Assert.Equal("Loop: off", RequireControl<TextBlock>(window, "LoopStatusTextBlock").Text);
                    Assert.False(RequireControl<MenuItem>(window, "LoopPlaybackMenuItem").IsChecked);
                }
                finally
                {
                    window.Close();
                }
            });
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public async Task CompareLoopPlayback_OpenCompletionDoesNotOverwriteNewerMasterSelection(
            bool openingPrimaryPane)
        {
            await _fixture.RunAsync(async () =>
            {
                var window = new MainWindow();
                var openCompletion = new TaskCompletionSource<bool>(
                    TaskCreationOptions.RunContinuationsAsynchronously);
                try
                {
                    var openingEngine = new TestVideoReviewEngine
                    {
                        OpenCompletion = openCompletion
                    };
                    var loadedEngine = new TestVideoReviewEngine
                    {
                        IsMediaOpen = true,
                        CurrentFilePath = openingPrimaryPane
                            ? "/tmp/right.mp4"
                            : "/tmp/left.mp4"
                    };
                    SetPrivateField(
                        window,
                        "_primaryEngine",
                        openingPrimaryPane ? openingEngine : loadedEngine);
                    SetPrivateField(
                        window,
                        "_compareEngine",
                        openingPrimaryPane ? loadedEngine : openingEngine);
                    RequireControl<CheckBox>(window, "CompareModeCheckBox").IsChecked = true;

                    var openCompare = (Task)InvokePrivate(
                        window,
                        "OpenPathAsync",
                        openingPrimaryPane ? "/tmp/left.mp4" : "/tmp/right.mp4",
                        ParsePane(openingPrimaryPane ? "Primary" : "Compare"));
                    Assert.True(
                        SpinWait.SpinUntil(
                            () => openingEngine.OpenCallCount == 1,
                            TimeSpan.FromSeconds(2)),
                        "The pane open did not begin.");

                    InvokePrivate(window, "SetUnifiedLoopPlaybackEnabled", true);
                    openCompletion.TrySetResult(true);
                    await openCompare;

                    Assert.True(GetPrivateField<bool>(window, "_isPrimaryLoopPlaybackEnabled"));
                    Assert.True(GetPrivateField<bool>(window, "_isCompareLoopPlaybackEnabled"));
                    Assert.Equal("Loop: both on", RequireControl<TextBlock>(window, "LoopStatusTextBlock").Text);
                }
                finally
                {
                    openCompletion.TrySetResult(true);
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
                    foreach (var sliderName in TimelineSliderNames)
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
                    Assert.True(RequireControl<TextBlock>(window, "CompareStatusTextBlock").FontSize > 12);
                    Assert.Equal(12, RequireControl<TextBlock>(window, "PlaybackStateTextBlock").FontSize);
                    Assert.True(RequireControl<TextBlock>(window, "CurrentPositionTextBlock").FontSize > 12);

                    Assert.Equal(16, RequireControl<TextBlock>(window, "CurrentFileTextBlock").FontSize);
                    Assert.Equal(11, RequireControl<TextBlock>(window, "PrimaryPaneFileTextBlock").FontSize);
                    Assert.Equal(11, RequireControl<TextBlock>(window, "ComparePaneFileTextBlock").FontSize);
                    Assert.Equal(13, RequireControl<Button>(window, "LoopStatusButton").FontSize);
                    AssertLoopStatusTrimming(window, "LoopStatusButton");
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

                    foreach (var sliderName in TimelineSliderNames)
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

                    foreach (var sliderName in TimelineSliderNames)
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

        [Fact]
        public void ZoomCommand_KeepsNativeFrameVisibleAfterCallerReleasesItsBuffer()
        {
            _fixture.Run(() =>
            {
                var window = new MainWindow();
                HGlobalPixelBufferHandle? nativeHandle = null;
                try
                {
                    var primarySurface = RequireControl<Image>(window, "CustomVideoSurface");
                    var nativeMenu = NativeMenu.GetMenu(window)
                        ?? throw new InvalidOperationException("Missing native menu.");
                    var zoomIn = RequireNativeMenuItem(nativeMenu, "Zoom In");
                    using (var nativeFrame = CreateNativeFrameBuffer(8, 4, out nativeHandle))
                    {
                        InvokePrivate(window, "SetPaneBitmap", ParsePane("Primary"), nativeFrame);
                    }

                    Assert.Equal(0, nativeHandle.ReleaseCount);
                    Assert.Equal(new PixelSize(8, 4), RequireBitmap(primarySurface).PixelSize);

                    zoomIn.Command!.Execute(null);

                    Assert.NotNull(primarySurface.Source);
                    Assert.True(RequireBitmap(primarySurface).PixelSize.Width < 8);
                    Assert.Equal(0, nativeHandle.ReleaseCount);
                }
                finally
                {
                    window.Close();
                }

                Assert.NotNull(nativeHandle);
                Assert.Equal(1, nativeHandle!.ReleaseCount);
            });
        }

        [Fact]
        public async Task CloseVideosAsync_ReleasesReusablePaneBitmaps()
        {
            await _fixture.RunAsync(async () =>
            {
                var window = new MainWindow();
                try
                {
                    var primarySurface = RequireControl<Image>(window, "CustomVideoSurface");
                    var compareSurface = RequireControl<Image>(window, "CompareVideoSurface");
                    using var primaryFrame = CreateFrameBuffer(8, 4);
                    using var compareFrame = CreateFrameBuffer(8, 4);

                    InvokePrivate(window, "SetPaneBitmap", ParsePane("Primary"), primaryFrame);
                    InvokePrivate(window, "SetPaneBitmap", ParsePane("Compare"), compareFrame);

                    Assert.NotNull(GetPrivateField<WriteableBitmap?>(window, "_primaryReusableBitmap"));
                    Assert.NotNull(GetPrivateField<WriteableBitmap?>(window, "_compareReusableBitmap"));

                    await (Task)InvokePrivate(window, "CloseVideosAsync");

                    Assert.Null(primarySurface.Source);
                    Assert.Null(compareSurface.Source);
                    Assert.Null(GetPrivateField<WriteableBitmap?>(window, "_primaryReusableBitmap"));
                    Assert.Null(GetPrivateField<WriteableBitmap?>(window, "_compareReusableBitmap"));
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

        private static RenderedColor ReadBitmapPixel(Bitmap bitmap, int x, int y)
        {
            var bytes = new byte[bitmap.PixelSize.Width * bitmap.PixelSize.Height * 4];
            var handle = GCHandle.Alloc(bytes, GCHandleType.Pinned);
            try
            {
                using var framebuffer = new ArrayLockedFramebuffer(
                    handle.AddrOfPinnedObject(),
                    bitmap.PixelSize,
                    bitmap.PixelSize.Width * 4);
                bitmap.CopyPixels(framebuffer);
            }
            finally
            {
                handle.Free();
            }

            var offset = (y * bitmap.PixelSize.Width * 4) + (x * 4);
            return new RenderedColor(
                bytes[offset + 2],
                bytes[offset + 1],
                bytes[offset],
                bytes[offset + 3]);
        }

        private static DecodedFrameBuffer CreateFrameBuffer(int width, int height)
        {
            return CreateFrameBuffer(width, height, red: 0xC0, green: 0x80, blue: 0x40);
        }

        private static VideoMediaInfo CreateMediaInfo(
            string filePath,
            TimeSpan duration)
        {
            return new VideoMediaInfo(
                filePath,
                duration,
                TimeSpan.FromSeconds(1d / 30d),
                30d,
                1920,
                1080,
                "h264",
                0,
                30,
                1,
                1,
                90_000);
        }

        private static string FormatExpectedTime(TimeSpan value)
        {
            return value.ToString(@"hh\:mm\:ss\.fff", CultureInfo.InvariantCulture);
        }

        private static DecodedFrameBuffer CreateFrameBuffer(int width, int height, TimeSpan presentationTime)
        {
            return CreateFrameBuffer(width, height, presentationTime, red: 0xC0, green: 0x80, blue: 0x40);
        }

        private static DecodedFrameBuffer CreateFrameBuffer(int width, int height, byte red, byte green, byte blue)
        {
            return CreateFrameBuffer(width, height, TimeSpan.Zero, red, green, blue);
        }

        private static DecodedFrameBuffer CreateFrameBuffer(
            int width,
            int height,
            TimeSpan presentationTime,
            byte red,
            byte green,
            byte blue)
        {
            var pixels = new byte[width * height * 4];
            for (var index = 0; index < pixels.Length; index += 4)
            {
                pixels[index] = blue;
                pixels[index + 1] = green;
                pixels[index + 2] = red;
                pixels[index + 3] = 0xFF;
            }

            return new DecodedFrameBuffer(
                new FrameDescriptor(
                    Math.Max(0L, (long)Math.Round(presentationTime.TotalSeconds * 30d)),
                    presentationTime,
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

        private static DecodedFrameBuffer CreateNativeFrameBuffer(
            int width,
            int height,
            out HGlobalPixelBufferHandle handle)
        {
            var pixels = new byte[width * height * 4];
            for (var index = 0; index < pixels.Length; index += 4)
            {
                pixels[index] = 0x40;
                pixels[index + 1] = 0x80;
                pixels[index + 2] = 0xC0;
                pixels[index + 3] = 0xFF;
            }

            var pointer = Marshal.AllocHGlobal(pixels.Length);
            Marshal.Copy(pixels, 0, pointer, pixels.Length);
            handle = new HGlobalPixelBufferHandle(pointer);
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
                handle,
                pointer,
                pixels.Length,
                width * 4,
                "bgra");
        }

        [Fact]
        public async Task CompareMode_FocusedUnloadedPaneDisablesMainTransportCommands()
        {
            await _fixture.RunAsync(async () =>
            {
                var window = new MainWindow();
                try
                {
                    RequireControl<CheckBox>(window, "CompareModeCheckBox").IsChecked = true;
                    var playPause = RequireControl<Button>(window, "PlayPauseButton");
                    var previousFrame = RequireControl<Button>(window, "PreviousFrameButton");
                    var nextFrame = RequireControl<Button>(window, "NextFrameButton");
                    playPause.IsEnabled = true;
                    previousFrame.IsEnabled = true;
                    nextFrame.IsEnabled = true;

                    InvokePrivate(window, "SelectPane", ParsePane("Compare"));

                    Assert.False(playPause.IsEnabled);
                    Assert.False(previousFrame.IsEnabled);
                    Assert.False(nextFrame.IsEnabled);
                }
                finally
                {
                    window.Close();
                }
            });
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
                panePrefix == "Primary" ? "Play left pane" : "Play right pane",
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

        private static void AssertLoopStatusTrimming(Window window, string loopStatusButtonName)
        {
            var loopStatusButton = RequireControl<Button>(window, loopStatusButtonName);
            var loopStatusText = Assert.IsType<TextBlock>(loopStatusButton.Content);
            Assert.Equal(TextTrimming.CharacterEllipsis, loopStatusText.TextTrimming);
            Assert.Equal(TextWrapping.NoWrap, loopStatusText.TextWrapping);
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

        private static Task InvokePrivateTask(
            MainWindow window,
            string methodName,
            Type[] parameterTypes,
            params object[] args)
        {
            var method = typeof(MainWindow).GetMethod(
                methodName,
                BindingFlags.Instance | BindingFlags.NonPublic,
                binder: null,
                types: parameterTypes,
                modifiers: null)
                ?? throw new MissingMethodException(typeof(MainWindow).FullName, methodName);
            return (Task)(method.Invoke(window, args)
                ?? throw new InvalidOperationException("Missing task result for " + methodName + "."));
        }

        private static T InvokePrivateStatic<T>(string methodName, params object[] args)
        {
            var method = typeof(MainWindow)
                .GetMethods(BindingFlags.Static | BindingFlags.NonPublic)
                .FirstOrDefault(candidate =>
                    string.Equals(candidate.Name, methodName, StringComparison.Ordinal) &&
                    candidate.GetParameters().Length == args.Length)
                ?? throw new MissingMethodException(typeof(MainWindow).FullName, methodName);
            return (T)(method.Invoke(null, args) ?? throw new InvalidOperationException("Missing result for " + methodName + "."));
        }

        private static void SetPrivateField(MainWindow window, string fieldName, object value)
        {
            var field = typeof(MainWindow).GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic)
                ?? throw new MissingFieldException(typeof(MainWindow).FullName, fieldName);
            field.SetValue(window, value);
        }

        private static LoopPlaybackPaneRangeSnapshot CreateLoopRange(string paneId, string filePath)
        {
            return new LoopPlaybackPaneRangeSnapshot(
                paneId,
                paneId,
                paneId,
                filePath,
                TimeSpan.FromSeconds(10),
                new LoopPlaybackAnchorSnapshot(
                    paneId,
                    paneId,
                    paneId,
                    TimeSpan.FromSeconds(1),
                    new LoopPlaybackFrameIdentitySnapshot(30, true, 90_000, 90_000)),
                new LoopPlaybackAnchorSnapshot(
                    paneId,
                    paneId,
                    paneId,
                    TimeSpan.FromSeconds(2),
                    new LoopPlaybackFrameIdentitySnapshot(60, true, 180_000, 180_000)));
        }

        private static LoopPlaybackPaneRangeSnapshot CreateLoopRange(
            string paneId,
            string filePath,
            TimeSpan duration)
        {
            return new LoopPlaybackPaneRangeSnapshot(
                paneId,
                paneId,
                paneId,
                filePath,
                duration,
                null,
                null);
        }

        private static T? GetPrivateField<T>(MainWindow window, string fieldName)
        {
            var field = typeof(MainWindow).GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic)
                ?? throw new MissingFieldException(typeof(MainWindow).FullName, fieldName);
            return (T?)field.GetValue(window);
        }

        private sealed class TestVideoReviewEngine : IVideoReviewEngine
        {
            public bool IsMediaOpen { get; set; }

            public bool IsPlaying { get; set; }

            public string CurrentFilePath { get; set; } = string.Empty;

            public TaskCompletionSource<bool>? PauseCompletion { get; set; }

            public TaskCompletionSource<bool>? OpenCompletion { get; set; }

            public TaskCompletionSource<bool>? PlayCompletion { get; set; }

            public TaskCompletionSource<bool>? SeekTimeCompletion { get; set; }

            public TaskCompletionSource<bool>? SeekFrameCompletion { get; set; }

            public bool StopPlayingOnSeek { get; set; }

            public Action<TimeSpan>? TimeSought { get; set; }

            public Action<long>? FrameSought { get; set; }

            public Action<int>? StepForwarded { get; set; }

            public Action<int>? StepBackwarded { get; set; }

            public string LastErrorMessage { get; set; } = string.Empty;

            public VideoMediaInfo MediaInfo { get; set; } = VideoMediaInfo.Empty;

            public ReviewPosition Position { get; set; } = ReviewPosition.Empty;

            public int PauseCallCount { get; private set; }

            public int OpenCallCount { get; private set; }

            public int PlayCallCount { get; private set; }

            public int SeekToTimeCallCount { get; private set; }

            public int StepForwardCallCount { get; private set; }

            public int StepBackwardCallCount { get; private set; }

            public int SeekToFrameCallCount { get; private set; }

            public int DisposeCallCount { get; private set; }

            public bool WasDisposedWithActiveOperation { get; private set; }

            public bool WasPausedWithActiveOperation { get; private set; }

            private int _activeOperationCount;

            public event EventHandler<VideoReviewEngineStateChangedEventArgs> StateChanged
            {
                add { }
                remove { }
            }

            public event EventHandler<FramePresentedEventArgs> FramePresented
            {
                add { }
                remove { }
            }

            public async Task OpenAsync(string filePath, CancellationToken cancellationToken = default(CancellationToken))
            {
                OpenCallCount++;
                CurrentFilePath = filePath;
                IsMediaOpen = true;
                if (OpenCompletion != null)
                {
                    await OpenCompletion.Task.WaitAsync(cancellationToken);
                }
            }

            public Task CloseAsync()
            {
                IsMediaOpen = false;
                IsPlaying = false;
                return Task.CompletedTask;
            }

            public Task PlayAsync()
            {
                PlayCallCount++;
                IsPlaying = true;
                return PlayCompletion == null ? Task.CompletedTask : PlayCompletion.Task;
            }

            public Task PauseAsync()
            {
                WasPausedWithActiveOperation |= Volatile.Read(ref _activeOperationCount) != 0;
                PauseCallCount++;
                IsPlaying = false;
                return PauseCompletion == null ? Task.CompletedTask : PauseCompletion.Task;
            }

            public Task<FrameStepResult> StepForwardAsync(CancellationToken cancellationToken = default(CancellationToken))
            {
                StepForwardCallCount++;
                StepForwarded?.Invoke(StepForwardCallCount);
                return Task.FromResult(FrameStepResult.Failed(1, Position, "Not implemented.", false));
            }

            public Task<FrameStepResult> StepBackwardAsync(CancellationToken cancellationToken = default(CancellationToken))
            {
                StepBackwardCallCount++;
                StepBackwarded?.Invoke(StepBackwardCallCount);
                return Task.FromResult(FrameStepResult.Failed(-1, Position, "Not implemented.", false, false));
            }

            public async Task SeekToTimeAsync(TimeSpan position, CancellationToken cancellationToken = default(CancellationToken))
            {
                SeekToTimeCallCount++;
                if (StopPlayingOnSeek)
                {
                    IsPlaying = false;
                }

                Interlocked.Increment(ref _activeOperationCount);
                try
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (SeekTimeCompletion != null)
                    {
                        await SeekTimeCompletion.Task.WaitAsync(cancellationToken);
                    }

                    Position = new ReviewPosition(
                        position,
                        Position.FrameIndex,
                        Position.IsFrameAccurate,
                        Position.IsFrameIndexAbsolute,
                        Position.PresentationTimestamp,
                        Position.DecodeTimestamp);
                    TimeSought?.Invoke(position);
                }
                finally
                {
                    Interlocked.Decrement(ref _activeOperationCount);
                }
            }

            public async Task SeekToFrameAsync(long frameIndex, CancellationToken cancellationToken = default(CancellationToken))
            {
                SeekToFrameCallCount++;
                if (StopPlayingOnSeek)
                {
                    IsPlaying = false;
                }

                Interlocked.Increment(ref _activeOperationCount);
                try
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (SeekFrameCompletion != null)
                    {
                        await SeekFrameCompletion.Task.WaitAsync(cancellationToken);
                    }

                    Position = new ReviewPosition(
                        Position.PresentationTime,
                        Math.Max(0L, frameIndex),
                        true,
                        true,
                        Position.PresentationTimestamp,
                        Position.DecodeTimestamp);
                    FrameSought?.Invoke(Math.Max(0L, frameIndex));
                }
                finally
                {
                    Interlocked.Decrement(ref _activeOperationCount);
                }
            }

            public void Dispose()
            {
                WasDisposedWithActiveOperation = Volatile.Read(ref _activeOperationCount) != 0;
                DisposeCallCount++;
            }
        }

        private sealed class HGlobalPixelBufferHandle : SafeHandleZeroOrMinusOneIsInvalid
        {
            public HGlobalPixelBufferHandle(IntPtr pointer)
                : base(ownsHandle: true)
            {
                SetHandle(pointer);
            }

            public int ReleaseCount { get; private set; }

            protected override bool ReleaseHandle()
            {
                Marshal.FreeHGlobal(handle);
                ReleaseCount++;
                return true;
            }
        }

        private static void AssertBrushColor(string expectedColor, IBrush? brush)
        {
            var solid = Assert.IsAssignableFrom<ISolidColorBrush>(brush);
            Assert.Equal(Color.Parse(expectedColor), solid.Color);
        }

        private readonly struct RenderedColor
        {
            public RenderedColor(byte red, byte green, byte blue, byte alpha)
            {
                Red = red;
                Green = green;
                Blue = blue;
                Alpha = alpha;
            }

            public byte Red { get; }

            public byte Green { get; }

            public byte Blue { get; }

            public byte Alpha { get; }
        }

        private sealed class ArrayLockedFramebuffer : ILockedFramebuffer
        {
            public ArrayLockedFramebuffer(IntPtr address, PixelSize size, int rowBytes)
            {
                Address = address;
                Size = size;
                RowBytes = rowBytes;
            }

            public IntPtr Address { get; }

            public PixelSize Size { get; }

            public int RowBytes { get; }

            public Vector Dpi
            {
                get { return new Vector(96d, 96d); }
            }

            public PixelFormat Format
            {
                get { return PixelFormat.Bgra8888; }
            }

            public AlphaFormat AlphaFormat
            {
                get { return AlphaFormat.Premul; }
            }

            public void Dispose()
            {
            }
        }

        private static string ReadRepositoryFile(params string[] pathParts)
        {
            var fullPath = Path.Combine(FindRepositoryRoot(AppContext.BaseDirectory), Path.Combine(pathParts));
            return File.ReadAllText(fullPath);
        }

        private static string ExtractMethodBody(string source, string methodStart, string nextMethodStart)
        {
            var start = source.IndexOf(methodStart, StringComparison.Ordinal);
            Assert.True(start >= 0, "Missing method: " + methodStart);

            var end = source.IndexOf(nextMethodStart, start, StringComparison.Ordinal);
            Assert.True(end > start, "Missing next method: " + nextMethodStart);

            return source.Substring(start, end - start);
        }

        private static string FindRepositoryRoot(string startDirectory)
        {
            var directory = new DirectoryInfo(startDirectory);
            while (directory != null)
            {
                if (File.Exists(Path.Combine(
                    directory.FullName,
                    "src",
                    "FramePlayer.Avalonia",
                    "FramePlayer.Avalonia.csproj")))
                {
                    return directory.FullName;
                }

                directory = directory.Parent;
            }

            throw new DirectoryNotFoundException("Could not find the frame-player repository root.");
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
