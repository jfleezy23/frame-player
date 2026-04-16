using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using FramePlayer.Core.Abstractions;
using FramePlayer.Core.Coordination;
using FramePlayer.Core.Events;
using FramePlayer.Core.Hosting;
using FramePlayer.Core.Models;
using FramePlayer.Engines.FFmpeg;
using FramePlayer.Services;

namespace FramePlayer.Host.Avalonia
{
    public sealed partial class MainWindow : Window
    {
        private const string PrimaryPaneId = "pane-primary";
        private const string ComparePaneId = "pane-compare-a";
        private const string CompareSessionId = "compare-a";

        private static readonly TimeSpan LoopGuardTolerance = TimeSpan.FromMilliseconds(100);
        private static readonly IBrush ActionStatusInfoBrush = new SolidColorBrush(Color.Parse("#9BB0C7"));
        private static readonly IBrush ActionStatusErrorBrush = new SolidColorBrush(Color.Parse("#F29BA7"));
        private static readonly IBrush SelectedPaneBrush = new SolidColorBrush(Color.Parse("#4A90E2"));
        private static readonly IBrush IdlePaneBrush = new SolidColorBrush(Color.Parse("#263342"));

        private readonly ClipExportService _clipExportService;
        private readonly ReviewWorkspaceHostController _hostController;
        private readonly FileBackedRecentFilesCatalog _recentFilesCatalog;
        private readonly AppPreferencesService _preferencesService;
        private readonly FfmpegReviewEngineOptionsProvider _optionsProvider;
        private readonly VideoReviewEngineFactory _videoReviewEngineFactory;
        private readonly IVideoReviewEngine _videoReviewEngine;
        private readonly ReviewSessionCoordinator _sessionCoordinator;
        private readonly ReviewWorkspaceCoordinator _workspaceCoordinator;
        private readonly DispatcherTimer _positionTimer;
        private readonly HashSet<string> _loopPlaybackEnabledPaneIds =
            new HashSet<string>(StringComparer.Ordinal);

        private IVideoReviewEngine _compareVideoReviewEngine;
        private ReviewSessionCoordinator _compareSessionCoordinator;
        private bool _isCompareModeEnabled;
        private bool _isLoopRestartInFlight;
        private bool _suppressSliderUpdate;
        private string _lastActionMessage = string.Empty;
        private bool _lastActionIsError;

        private sealed class TimelineContextCommandTarget
        {
            public TimelineContextCommandTarget(string paneId, TimeSpan target)
            {
                PaneId = string.IsNullOrWhiteSpace(paneId) ? string.Empty : paneId;
                Target = target < TimeSpan.Zero ? TimeSpan.Zero : target;
            }

            public string PaneId { get; }

            public TimeSpan Target { get; }
        }

        public MainWindow()
        {
            InitializeComponent();

            _preferencesService = new AppPreferencesService();
            _clipExportService = new ClipExportService();
            _recentFilesCatalog = new FileBackedRecentFilesCatalog();
            _optionsProvider = new FfmpegReviewEngineOptionsProvider(_preferencesService);
            _videoReviewEngineFactory = new VideoReviewEngineFactory(_optionsProvider, SdlAudioOutputFactory.Instance);
            _videoReviewEngine = _videoReviewEngineFactory.Create(PrimaryPaneId);
            _sessionCoordinator = new ReviewSessionCoordinator(_videoReviewEngine);
            _workspaceCoordinator = new ReviewWorkspaceCoordinator(_videoReviewEngine, _sessionCoordinator);

            string runtimeValidationMessage;
            var hasBundledRuntime = RuntimeManifestService.TryValidateRuntimeDirectory(AppContext.BaseDirectory, out runtimeValidationMessage);
            _hostController = new ReviewWorkspaceHostController(
                _workspaceCoordinator,
                new ReviewHostCapabilities(
                    supportsTimedPlayback: true,
                    hasBundledRuntime: hasBundledRuntime,
                    exportToolingAvailable: _clipExportService.IsBundledToolingAvailable,
                    idleStatusText: "Avalonia preview host ready.",
                    runtimeMissingStatusText: string.IsNullOrWhiteSpace(runtimeValidationMessage)
                        ? "Bundled playback runtime is missing."
                        : runtimeValidationMessage,
                    timedPlaybackCapabilityText: "Timed playback is unavailable in this host.",
                    exportToolingStatusText: _clipExportService.GetToolAvailabilityMessage()),
                _recentFilesCatalog);
            _hostController.SetStartupOpenFilePath(AppLaunchOptions.ConsumeStartupOpenFilePath());

            _videoReviewEngine.FramePresented += VideoReviewEngine_FramePresented;
            _hostController.ViewStateChanged += HostController_ViewStateChanged;
            Opened += MainWindow_Opened;

            _positionTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(60)
            };
            _positionTimer.Tick += PositionTimer_Tick;
            _positionTimer.Start();

            RefreshUi(_hostController.CurrentViewState);
        }

        protected override void OnClosed(EventArgs e)
        {
            base.OnClosed(e);

            _positionTimer.Stop();
            Opened -= MainWindow_Opened;
            _videoReviewEngine.FramePresented -= VideoReviewEngine_FramePresented;
            if (_compareVideoReviewEngine != null)
            {
                _compareVideoReviewEngine.FramePresented -= VideoReviewEngine_FramePresented;
            }

            _hostController.ViewStateChanged -= HostController_ViewStateChanged;
            _hostController.Dispose();
            _workspaceCoordinator.Dispose();
            _compareSessionCoordinator?.Dispose();
            _sessionCoordinator.Dispose();
            _compareVideoReviewEngine?.Dispose();
            _videoReviewEngine.Dispose();
        }

        private async void OpenButton_Click(object sender, RoutedEventArgs e)
        {
            var filePath = await PickMediaFileAsync("Open Media");
            await OpenMediaAsync(filePath, GetFocusedOrPrimaryPaneId());
        }

        private async void OpenCompareButton_Click(object sender, RoutedEventArgs e)
        {
            SetCompareModeEnabled(true, updateCheckBox: true);
            var filePath = await PickMediaFileAsync("Open Compare Media");
            await OpenMediaAsync(filePath, ComparePaneId);
        }

        private async void OpenPaneButton_Click(object sender, RoutedEventArgs e)
        {
            var paneId = GetPaneIdFromSender(sender);
            if (string.IsNullOrWhiteSpace(paneId))
            {
                return;
            }

            if (string.Equals(paneId, ComparePaneId, StringComparison.Ordinal))
            {
                SetCompareModeEnabled(true, updateCheckBox: true);
            }

            var filePath = await PickMediaFileAsync(
                string.Equals(paneId, ComparePaneId, StringComparison.Ordinal)
                    ? "Open Compare Media"
                    : "Open Primary Media");
            await OpenMediaAsync(filePath, paneId);
        }

        private void UsePaneButton_Click(object sender, RoutedEventArgs e)
        {
            var paneId = GetPaneIdFromSender(sender);
            if (!string.IsNullOrWhiteSpace(paneId))
            {
                TryFocusPane(paneId);
            }
        }

        private void CompareModeCheckBox_Click(object sender, RoutedEventArgs e)
        {
            SetCompareModeEnabled(CompareModeCheckBox.IsChecked == true, updateCheckBox: false);
        }

        private async void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            var paneLabel = GetFocusedPaneDisplayLabel();
            await _hostController.CloseAsync();
            _hostController.Refresh();
            SetActionStatus(paneLabel + " closed.");
        }

        private async void PlayPauseButton_Click(object sender, RoutedEventArgs e)
        {
            if (_hostController.CurrentViewState.Transport.IsPlaying)
            {
                await _hostController.PauseAsync();
            }
            else
            {
                await _hostController.PlayAsync();
            }

            _hostController.Refresh();
        }

        private async void PreviousFrameButton_Click(object sender, RoutedEventArgs e)
        {
            await _hostController.StepBackwardAsync();
            _hostController.Refresh();
        }

        private async void RewindButton_Click(object sender, RoutedEventArgs e)
        {
            await SeekFocusedPaneBySecondsAsync(-5d);
        }

        private async void NextFrameButton_Click(object sender, RoutedEventArgs e)
        {
            await _hostController.StepForwardAsync();
            _hostController.Refresh();
        }

        private async void FastForwardButton_Click(object sender, RoutedEventArgs e)
        {
            await SeekFocusedPaneBySecondsAsync(5d);
        }

        private void SetLoopAButton_Click(object sender, RoutedEventArgs e)
        {
            if (ShouldUsePaneLocalLoopCommands())
            {
                _hostController.SetPaneLoopMarker(GetFocusedOrPrimaryPaneId(), LoopPlaybackMarkerEndpoint.In);
                return;
            }

            _hostController.SetSharedLoopMarker(LoopPlaybackMarkerEndpoint.In, SynchronizedOperationScope.FocusedPane);
        }

        private void SetLoopBButton_Click(object sender, RoutedEventArgs e)
        {
            if (ShouldUsePaneLocalLoopCommands())
            {
                _hostController.SetPaneLoopMarker(GetFocusedOrPrimaryPaneId(), LoopPlaybackMarkerEndpoint.Out);
                return;
            }

            _hostController.SetSharedLoopMarker(LoopPlaybackMarkerEndpoint.Out, SynchronizedOperationScope.FocusedPane);
        }

        private void ClearLoopButton_Click(object sender, RoutedEventArgs e)
        {
            if (ShouldUsePaneLocalLoopCommands())
            {
                _hostController.ClearPaneLoopRange(GetFocusedOrPrimaryPaneId());
                return;
            }

            _hostController.ClearSharedLoopRange();
        }

        private void LoopPlaybackButton_Click(object sender, RoutedEventArgs e)
        {
            var paneId = GetFocusedOrPrimaryPaneId();
            if (string.IsNullOrWhiteSpace(paneId))
            {
                return;
            }

            if (!_loopPlaybackEnabledPaneIds.Add(paneId))
            {
                _loopPlaybackEnabledPaneIds.Remove(paneId);
            }

            UpdateLoopPlaybackButtonContent(_hostController.CurrentViewState);
            SetActionStatus(BuildLoopPlaybackStatusMessage());
        }

        private async void ExportClipButton_Click(object sender, RoutedEventArgs e)
        {
            await ExportLoopClipAsync(null);
        }

        private async void PaneStepBackButton_Click(object sender, RoutedEventArgs e)
        {
            var paneId = GetPaneIdFromSender(sender);
            if (!TryFocusPane(paneId))
            {
                return;
            }

            await _hostController.StepBackwardAsync();
            _hostController.Refresh();
        }

        private async void PanePlayPauseButton_Click(object sender, RoutedEventArgs e)
        {
            var paneId = GetPaneIdFromSender(sender);
            if (!TryFocusPane(paneId))
            {
                return;
            }

            if (_hostController.CurrentViewState.Transport.IsPlaying)
            {
                await _hostController.PauseAsync();
            }
            else
            {
                await _hostController.PlayAsync();
            }

            _hostController.Refresh();
        }

        private async void PaneStepForwardButton_Click(object sender, RoutedEventArgs e)
        {
            var paneId = GetPaneIdFromSender(sender);
            if (!TryFocusPane(paneId))
            {
                return;
            }

            await _hostController.StepForwardAsync();
            _hostController.Refresh();
        }

        private void VideoSurface_ContextRequested(object sender, ContextRequestedEventArgs e)
        {
            var host = sender as Control;
            var paneId = GetPaneIdFromSender(sender);
            if (host == null || string.IsNullOrWhiteSpace(paneId))
            {
                return;
            }

            TryFocusPane(paneId);

            var menuItems = host.ContextMenu != null
                ? host.ContextMenu.Items.OfType<MenuItem>().ToArray()
                : Array.Empty<MenuItem>();
            if (menuItems.Length > 0)
            {
                var transport = _hostController.CurrentViewState.Transport;
                menuItems[0].IsEnabled = transport != null && transport.CanInspectMedia;
                ToolTip.SetTip(menuItems[0], transport != null && transport.CanInspectMedia
                    ? "Show file, stream, and timing details for the selected pane."
                    : "Load media into the selected pane before opening video info.");
            }

            if (menuItems.Length > 1)
            {
                string toolTip;
                var canExport = CanExportFocusedLoop(out toolTip);
                menuItems[1].IsEnabled = canExport;
                ToolTip.SetTip(menuItems[1], toolTip);
            }
        }

        private void TimelineSlider_ContextRequested(object sender, ContextRequestedEventArgs e)
        {
            var slider = sender as Slider;
            var paneId = GetPaneIdFromSender(sender);
            if (slider == null)
            {
                return;
            }

            if (!string.IsNullOrWhiteSpace(paneId))
            {
                TryFocusPane(paneId);
            }

            var target = ResolveTimelineContextTarget(slider, e);
            var menuItems = slider.ContextMenu != null
                ? slider.ContextMenu.Items.OfType<MenuItem>().ToArray()
                : Array.Empty<MenuItem>();
            if (menuItems.Length < 4)
            {
                return;
            }

            var contextTarget = new TimelineContextCommandTarget(paneId, target);
            for (var index = 0; index < menuItems.Length; index++)
            {
                menuItems[index].Tag = contextTarget;
            }

            string toolTip;
            var canSetPositionA = CanOfferTimelineLoopMarkerAtTarget(
                paneId,
                LoopPlaybackMarkerEndpoint.In,
                target,
                out toolTip);
            menuItems[0].IsEnabled = canSetPositionA;
            ToolTip.SetTip(menuItems[0], toolTip);

            var canSetPositionB = CanOfferTimelineLoopMarkerAtTarget(
                paneId,
                LoopPlaybackMarkerEndpoint.Out,
                target,
                out toolTip);
            menuItems[1].IsEnabled = canSetPositionB;
            ToolTip.SetTip(menuItems[1], toolTip);

            menuItems[2].IsEnabled = _hostController.CurrentViewState.Transport.CanControlTransport;
            menuItems[2].IsChecked = IsLoopPlaybackEnabledForPane(paneId);
            ToolTip.SetTip(menuItems[2], "Enable or disable A/B loop playback for the selected pane.");

            var canExport = CanExportFocusedLoop(out toolTip);
            menuItems[3].IsEnabled = canExport;
            ToolTip.SetTip(menuItems[3], toolTip);
        }

        private async void VideoInfoMenuItem_Click(object sender, RoutedEventArgs e)
        {
            var paneId = GetPaneIdFromSender(sender);
            await Task.Yield();
            await ShowVideoInfoAsync(paneId);
        }

        private async void PaneSaveLoopAsClipMenuItem_Click(object sender, RoutedEventArgs e)
        {
            var paneId = GetPaneIdFromSender(sender);
            await Task.Yield();
            await ExportLoopClipAsync(paneId);
        }

        private async void TimelineSetLoopPositionAMenuItem_Click(object sender, RoutedEventArgs e)
        {
            TimelineContextCommandTarget contextTarget;
            if (!TryGetTimelineContextCommandTarget(sender, out contextTarget))
            {
                return;
            }

            await SetTimelineLoopMarkerAtAsync(
                contextTarget.PaneId,
                LoopPlaybackMarkerEndpoint.In,
                contextTarget.Target);
        }

        private async void TimelineSetLoopPositionBMenuItem_Click(object sender, RoutedEventArgs e)
        {
            TimelineContextCommandTarget contextTarget;
            if (!TryGetTimelineContextCommandTarget(sender, out contextTarget))
            {
                return;
            }

            await SetTimelineLoopMarkerAtAsync(
                contextTarget.PaneId,
                LoopPlaybackMarkerEndpoint.Out,
                contextTarget.Target);
        }

        private void TimelineLoopPlaybackMenuItem_Click(object sender, RoutedEventArgs e)
        {
            var menuItem = sender as MenuItem;
            if (menuItem == null)
            {
                return;
            }

            var contextTarget = menuItem.Tag as TimelineContextCommandTarget;
            var paneId = contextTarget != null ? contextTarget.PaneId : GetFocusedOrPrimaryPaneId();
            if (!string.IsNullOrWhiteSpace(paneId))
            {
                TryFocusPane(paneId);
            }

            var resolvedPaneId = GetFocusedOrPrimaryPaneId();
            if (string.IsNullOrWhiteSpace(resolvedPaneId))
            {
                return;
            }

            if (menuItem.IsChecked)
            {
                _loopPlaybackEnabledPaneIds.Add(resolvedPaneId);
            }
            else
            {
                _loopPlaybackEnabledPaneIds.Remove(resolvedPaneId);
            }

            UpdateLoopPlaybackButtonContent(_hostController.CurrentViewState);
            SetActionStatus(BuildLoopPlaybackStatusMessage());
        }

        private async void TimelineSaveLoopAsClipMenuItem_Click(object sender, RoutedEventArgs e)
        {
            TimelineContextCommandTarget contextTarget;
            if (!TryGetTimelineContextCommandTarget(sender, out contextTarget))
            {
                return;
            }

            await Task.Yield();
            await ExportLoopClipAsync(contextTarget.PaneId);
        }

        private async void RecentFileMenuItem_Click(object sender, RoutedEventArgs e)
        {
            var menuItem = sender as MenuItem;
            await OpenRecentFileAsync(menuItem != null ? menuItem.Tag as string : string.Empty);
        }

        private void ClearRecentFilesMenuItem_Click(object sender, RoutedEventArgs e)
        {
            _hostController.ClearRecentFiles();
            _hostController.Refresh();
            SetActionStatus("Recent files cleared.");
        }

        private void ExitMenuItem_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }

        private async void PanePositionSlider_PropertyChanged(object sender, AvaloniaPropertyChangedEventArgs e)
        {
            if (_suppressSliderUpdate || e.Property != Slider.ValueProperty)
            {
                return;
            }

            var slider = sender as Slider;
            var paneId = slider != null ? GetPaneIdFromTag(slider.Tag) : string.Empty;
            if (slider == null || string.IsNullOrWhiteSpace(paneId))
            {
                return;
            }

            if (!TryFocusPane(paneId) || !_hostController.CurrentViewState.Transport.CanSeek)
            {
                return;
            }

            await _hostController.SeekToTimeAsync(TimeSpan.FromSeconds(slider.Value));
            _hostController.Refresh();
        }

        private void PaneFrameTextBox_GotFocus(object sender, RoutedEventArgs e)
        {
            var textBox = sender as TextBox;
            if (textBox != null)
            {
                textBox.SelectAll();
            }
        }

        private async void PaneFrameTextBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key != Key.Enter)
            {
                return;
            }

            var textBox = sender as TextBox;
            var paneId = textBox != null ? GetPaneIdFromTag(textBox.Tag) : string.Empty;
            if (textBox == null || string.IsNullOrWhiteSpace(paneId))
            {
                return;
            }

            long requestedFrameNumber;
            if (!long.TryParse(textBox.Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out requestedFrameNumber) ||
                requestedFrameNumber <= 0)
            {
                SetActionStatus("Enter a 1-based frame number and press Enter.", isError: true);
                return;
            }

            if (!TryFocusPane(paneId) || !_hostController.CurrentViewState.Transport.CanSeek)
            {
                return;
            }

            await _hostController.SeekToFrameAsync(requestedFrameNumber - 1L);
            _hostController.Refresh();
            SetActionStatus(
                GetPaneDisplayLabel(paneId) + " moved to frame " +
                requestedFrameNumber.ToString(CultureInfo.InvariantCulture) + ".");
            e.Handled = true;
        }

        private void HostController_ViewStateChanged(object sender, ReviewWorkspaceViewStateChangedEventArgs e)
        {
            Dispatcher.UIThread.Post(() => RefreshUi(e.Current));
        }

        private async void MainWindow_Opened(object sender, EventArgs e)
        {
            Opened -= MainWindow_Opened;

            string startupOpenFilePath;
            if (!_hostController.TryConsumeStartupOpenFilePath(out startupOpenFilePath))
            {
                return;
            }

            await OpenMediaAsync(startupOpenFilePath, PrimaryPaneId);
        }

        private void VideoReviewEngine_FramePresented(object sender, FramePresentedEventArgs e)
        {
            Dispatcher.UIThread.Post(() =>
            {
                var image = ReferenceEquals(sender, _compareVideoReviewEngine)
                    ? CompareVideoImage
                    : PrimaryVideoImage;
                var emptySurfaceText = ReferenceEquals(sender, _compareVideoReviewEngine)
                    ? CompareEmptySurfaceText
                    : PrimaryEmptySurfaceText;

                image.Source = e != null ? AvaloniaFrameBufferPresenter.CreateBitmap(e.FrameBuffer) : null;
                emptySurfaceText.IsVisible = image.Source == null;
            });
        }

        private async void PositionTimer_Tick(object sender, EventArgs e)
        {
            if (ShouldRestartLoopPlaybackAtBoundary())
            {
                await RestartLoopPlaybackAsync();
            }

            RefreshUi(_hostController.CurrentViewState);
        }

        private void RefreshUi(ReviewWorkspaceViewState viewState)
        {
            var transport = viewState != null ? viewState.Transport : TransportCommandState.Disabled;
            var recentFiles = viewState != null ? viewState.RecentFiles : RecentFilesCommandState.Empty;
            var recentEntries = recentFiles != null
                ? recentFiles.Entries ?? Array.Empty<RecentFileViewState>()
                : Array.Empty<RecentFileViewState>();
            var effectiveLoopState = ResolveEffectiveFocusedLoopState(viewState);
            string exportToolTip;
            var canExport = CanExportFocusedLoop(out exportToolTip);

            OpenCompareMenuItem.IsEnabled = true;
            CloseFocusedMenuItem.IsEnabled = transport.CanCloseMedia;
            VideoInfoMenuActionItem.IsEnabled = transport.CanInspectMedia;
            PlayPauseButton.IsEnabled = transport.CanTogglePlayPause;
            PreviousFrameButton.IsEnabled = transport.CanStepBackward;
            RewindButton.IsEnabled = transport.CanSeek;
            NextFrameButton.IsEnabled = transport.CanStepForward;
            FastForwardButton.IsEnabled = transport.CanSeek;
            SetLoopAButton.IsEnabled = effectiveLoopState.CanSetMarkers;
            SetLoopBButton.IsEnabled = effectiveLoopState.CanSetMarkers;
            ClearLoopButton.IsEnabled = effectiveLoopState.CanClearMarkers;
            LoopPlaybackButton.IsEnabled = transport.CanControlTransport;
            ExportClipButton.IsEnabled = canExport;
            ToolTip.SetTip(ExportClipButton, exportToolTip);
            PlayPauseButton.Content = transport.IsPlaying ? "Pause" : "Play";

            UpdateCompareModeVisualState();
            UpdateLoopPlaybackButtonContent(viewState);

            var currentFilePath = viewState != null ? viewState.CurrentFilePath : string.Empty;
            CurrentFileTextBlock.Text = string.IsNullOrWhiteSpace(currentFilePath)
                ? "No file loaded"
                : Path.GetFileName(currentFilePath);
            ToolTip.SetTip(CurrentFileTextBlock, currentFilePath);
            CompareStatusTextBlock.Text = BuildCompareStatusText(viewState);
            FocusedPaneTextBlock.Text = "Focused Pane: " + GetFocusedPaneDisplayLabel(viewState);
            PlaybackStatusTextBlock.Text = viewState != null ? viewState.PlaybackMessage : "Ready.";
            MediaSummaryTextBlock.Text = viewState != null ? viewState.MediaSummary : string.Empty;
            FrameStatusTextBlock.Text = BuildFocusedFrameStatus(viewState);

            RefreshPaneUi(viewState, PrimaryPaneId);
            RefreshPaneUi(viewState, ComparePaneId);

            UpdateActionStatusPresentation();
            RefreshRecentFilesMenu(recentFiles, recentEntries);
        }

        private bool TryBuildSeedClipExportRequest(out ClipExportRequest request, out string failureMessage)
        {
            request = null;

            if (!_clipExportService.IsBundledToolingAvailable)
            {
                failureMessage = _clipExportService.GetToolAvailabilityMessage();
                return false;
            }

            var snapshot = _workspaceCoordinator.GetWorkspaceSnapshot();
            var focusedPaneId = GetFocusedPaneId(snapshot);
            ReviewWorkspacePaneSnapshot paneSnapshot;
            if (string.IsNullOrWhiteSpace(focusedPaneId) ||
                !snapshot.TryGetPane(focusedPaneId, out paneSnapshot) ||
                !PaneHasLoadedMedia(paneSnapshot))
            {
                failureMessage = "Load media into the focused pane before exporting a clip.";
                return false;
            }

            var loopRange = ResolveFocusedLoopRange(snapshot);
            if (loopRange == null || !loopRange.HasLoopIn || !loopRange.HasLoopOut)
            {
                failureMessage = _isCompareModeEnabled
                    ? "Set exact pane-local A/B markers on the focused pane before exporting a clip."
                    : "Set exact A/B markers before exporting a reviewed clip.";
                return false;
            }

            if (loopRange.HasPendingMarkers)
            {
                failureMessage = "Wait for the focused pane loop markers to resolve exact frame identity before exporting a clip.";
                return false;
            }

            if (loopRange.IsInvalidRange)
            {
                failureMessage = "The focused pane loop-out marker currently lands before loop-in.";
                return false;
            }

            var session = _workspaceCoordinator.CurrentSession ?? ReviewSessionSnapshot.Empty;
            var ffmpegEngine = GetEngineForPane(focusedPaneId) as FfmpegReviewEngine;
            if (ffmpegEngine == null)
            {
                failureMessage = "The focused pane is not backed by an FFmpeg review engine.";
                return false;
            }

            request = new ClipExportRequest(
                session.CurrentFilePath,
                string.Empty,
                paneSnapshot.DisplayLabel,
                focusedPaneId,
                _isCompareModeEnabled,
                session,
                loopRange,
                new IndexedFrameTimeResolverAdapter(ffmpegEngine));
            failureMessage = "The current loop is ready to export.";
            return true;
        }

        private LoopPlaybackPaneRangeSnapshot ResolveFocusedLoopRange()
        {
            return ResolveFocusedLoopRange(_workspaceCoordinator.GetWorkspaceSnapshot());
        }

        private LoopPlaybackPaneRangeSnapshot ResolveFocusedLoopRange(ReviewWorkspaceSnapshot snapshot)
        {
            snapshot = snapshot ?? ReviewWorkspaceSnapshot.Empty;
            var paneId = GetFocusedPaneId(snapshot);

            ReviewWorkspacePaneSnapshot paneSnapshot;
            if (!string.IsNullOrWhiteSpace(paneId) &&
                snapshot.TryGetPane(paneId, out paneSnapshot) &&
                paneSnapshot != null)
            {
                if (_isCompareModeEnabled)
                {
                    return paneSnapshot.LoopRange;
                }

                if (paneSnapshot.LoopRange != null && paneSnapshot.LoopRange.HasAnyMarkers)
                {
                    return paneSnapshot.LoopRange;
                }
            }

            if (_isCompareModeEnabled)
            {
                return null;
            }

            LoopPlaybackPaneRangeSnapshot sharedPaneRange;
            if (!string.IsNullOrWhiteSpace(paneId) &&
                snapshot.SharedLoopRange != null &&
                snapshot.SharedLoopRange.TryGetPaneRange(paneId, out sharedPaneRange))
            {
                return sharedPaneRange;
            }

            return snapshot.SharedLoopRange != null && snapshot.SharedLoopRange.PaneRanges.Count > 0
                ? snapshot.SharedLoopRange.PaneRanges[0]
                : null;
        }

        private static string FormatTime(TimeSpan value)
        {
            if (value < TimeSpan.Zero)
            {
                value = TimeSpan.Zero;
            }

            return string.Format(
                CultureInfo.InvariantCulture,
                "{0:00}:{1:00}:{2:00}.{3:000}",
                (int)value.TotalHours,
                value.Minutes,
                value.Seconds,
                value.Milliseconds);
        }

        private async Task OpenMediaAsync(string filePath, string paneId)
        {
            if (string.IsNullOrWhiteSpace(filePath))
            {
                return;
            }

            if (!string.IsNullOrWhiteSpace(paneId) &&
                string.Equals(paneId, ComparePaneId, StringComparison.Ordinal))
            {
                EnsureComparePaneInitialized();
            }

            await _hostController.OpenInPaneAsync(string.IsNullOrWhiteSpace(paneId) ? PrimaryPaneId : paneId, filePath);
            _hostController.Refresh();
            SetActionStatus(string.Format(
                CultureInfo.InvariantCulture,
                "Opened {0} in {1}.",
                Path.GetFileName(filePath),
                GetPaneDisplayLabel(paneId)));
        }

        private Task<string> PickMediaFileAsync(string title)
        {
            return PickLocalMediaFileAsync(title);
        }

        private async Task<string> PickLocalMediaFileAsync(string title)
        {
            var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = title,
                AllowMultiple = false,
                FileTypeFilter = new[]
                {
                    new FilePickerFileType("Supported Media")
                    {
                        Patterns = new[] { "*.avi", "*.m4v", "*.mkv", "*.mov", "*.mp4", "*.wmv" }
                    }
                }
            });
            if (files == null || files.Count == 0)
            {
                return string.Empty;
            }

            return files[0].TryGetLocalPath() ?? string.Empty;
        }

        private void RefreshRecentFilesMenu(RecentFilesCommandState recentFiles, IReadOnlyList<RecentFileViewState> recentEntries)
        {
            var menuItems = new List<object>();
            var safeEntries = recentEntries ?? Array.Empty<RecentFileViewState>();

            if (safeEntries.Count == 0)
            {
                menuItems.Add(new MenuItem
                {
                    Header = recentFiles != null && !string.IsNullOrWhiteSpace(recentFiles.StatusText)
                        ? recentFiles.StatusText
                        : "No recent files.",
                    IsEnabled = false
                });
            }
            else
            {
                for (var index = 0; index < safeEntries.Count; index++)
                {
                    var entry = safeEntries[index];
                    var item = new MenuItem
                    {
                        Header = entry.DisplayLabel,
                        Tag = entry.FilePath
                    };
                    ToolTip.SetTip(item, entry.ToolTip);
                    item.Click += RecentFileMenuItem_Click;
                    menuItems.Add(item);
                }

                menuItems.Add(new Separator());

                var clearItem = new MenuItem
                {
                    Header = "_Clear Recent Files",
                    IsEnabled = recentFiles != null && recentFiles.CanClear
                };
                clearItem.Click += ClearRecentFilesMenuItem_Click;
                menuItems.Add(clearItem);
            }

            RecentFilesMenuItem.ItemsSource = menuItems;
        }

        private async Task OpenRecentFileAsync(string filePath)
        {
            if (string.IsNullOrWhiteSpace(filePath))
            {
                return;
            }

            if (!File.Exists(filePath))
            {
                _hostController.RemoveRecentFile(filePath);
                _hostController.Refresh();
                SetActionStatus("That recent file no longer exists, so it was removed from the list.", isError: true);
                return;
            }

            await OpenMediaAsync(filePath, GetFocusedOrPrimaryPaneId());
        }

        private async Task ExportLoopClipAsync(string paneId)
        {
            if (!string.IsNullOrWhiteSpace(paneId) && !TryFocusPane(paneId))
            {
                return;
            }

            ClipExportRequest seedRequest;
            string failureMessage;
            if (!TryBuildSeedClipExportRequest(out seedRequest, out failureMessage))
            {
                SetActionStatus(failureMessage, isError: true);
                return;
            }

            await PausePlaybackForExportAsync();

            if (!TryBuildSeedClipExportRequest(out seedRequest, out failureMessage))
            {
                SetActionStatus(failureMessage, isError: true);
                return;
            }

            var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
            {
                Title = "Save Loop As Clip",
                SuggestedFileName = BuildSuggestedClipFileName(seedRequest),
                DefaultExtension = "mp4",
                FileTypeChoices = new[]
                {
                    new FilePickerFileType("MP4 Video")
                    {
                        Patterns = new[] { "*.mp4" }
                    }
                }
            });
            var outputPath = file != null ? file.TryGetLocalPath() : string.Empty;
            if (string.IsNullOrWhiteSpace(outputPath))
            {
                return;
            }

            SetActionStatus("Exporting clip...");

            try
            {
                var request = CreateOutputClipExportRequest(seedRequest, outputPath);
                var exportResult = await _clipExportService.ExportAsync(request);
                _hostController.Refresh();

                if (exportResult != null && exportResult.Succeeded)
                {
                    SetActionStatus(BuildClipExportSuccessMessage(exportResult));
                    return;
                }

                SetActionStatus(
                    exportResult != null
                        ? SanitizeActionMessage(exportResult.Message)
                        : "Clip export failed.",
                    isError: true);
            }
            catch (Exception ex)
            {
                SetActionStatus("Clip export failed. " + SanitizeActionMessage(ex.Message), isError: true);
            }
        }

        private async Task ShowVideoInfoAsync(string paneId)
        {
            if (!string.IsNullOrWhiteSpace(paneId) && !TryFocusPane(paneId))
            {
                return;
            }

            var focusedPaneId = GetFocusedOrPrimaryPaneId();
            var transport = _hostController.CurrentViewState.Transport;
            if (string.IsNullOrWhiteSpace(focusedPaneId) || transport == null || !transport.CanInspectMedia)
            {
                SetActionStatus("Load media into the selected pane before opening video info.", isError: true);
                return;
            }

            var paneLabel = GetFocusedPaneDisplayLabel();
            var engine = GetEngineForPane(focusedPaneId) as FfmpegReviewEngine;
            var mediaInfo = engine != null ? engine.MediaInfo ?? VideoMediaInfo.Empty : VideoMediaInfo.Empty;
            if (mediaInfo == null || string.IsNullOrWhiteSpace(mediaInfo.FilePath))
            {
                SetActionStatus("Video info is unavailable until media is loaded into the selected pane.", isError: true);
                return;
            }

            var dialog = new Window
            {
                Title = paneLabel + " Video Info",
                Width = 680,
                Height = 720,
                MinWidth = 560,
                MinHeight = 520,
                Background = new SolidColorBrush(Color.Parse("#171C22")),
                Foreground = Brushes.White,
                Content = new Border
                {
                    Padding = new Thickness(16d),
                    Child = new ScrollViewer
                    {
                        Content = new TextBox
                        {
                            Text = BuildVideoInfoText(paneLabel, mediaInfo),
                            AcceptsReturn = true,
                            IsReadOnly = true,
                            TextWrapping = TextWrapping.Wrap,
                            Background = new SolidColorBrush(Color.Parse("#10151B")),
                            Foreground = Brushes.White,
                            BorderBrush = new SolidColorBrush(Color.Parse("#2D3743")),
                            BorderThickness = new Thickness(1d),
                            HorizontalAlignment = HorizontalAlignment.Stretch,
                            VerticalAlignment = VerticalAlignment.Stretch
                        }
                    }
                }
            };

            await dialog.ShowDialog(this);
        }

        private async Task PausePlaybackForExportAsync()
        {
            if (!_hostController.CurrentViewState.Transport.IsPlaying)
            {
                return;
            }

            await _hostController.PauseAsync();
            _hostController.Refresh();
        }

        private async Task RestartLoopPlaybackAsync()
        {
            if (_isLoopRestartInFlight)
            {
                return;
            }

            _isLoopRestartInFlight = true;
            try
            {
                var session = _workspaceCoordinator.CurrentSession ?? ReviewSessionSnapshot.Empty;
                var loopRange = ResolveFocusedLoopRange();
                var restartTime = TimeSpan.Zero;
                var paneLabel = GetFocusedPaneDisplayLabel();
                var statusMessage = paneLabel + " loop playback restarted from the beginning.";

                if (loopRange != null && loopRange.HasAnyMarkers && !loopRange.IsInvalidRange)
                {
                    restartTime = loopRange.EffectiveStartTime;
                    statusMessage = loopRange.HasPendingMarkers
                        ? paneLabel + " loop playback restarted from the pending A/B range."
                        : paneLabel + " loop playback restarted from loop-in.";
                }

                await _hostController.PauseAsync();
                await _hostController.SeekToTimeAsync(restartTime);
                await _hostController.PlayAsync();
                _hostController.Refresh();

                if (session.IsMediaOpen)
                {
                    SetActionStatus(statusMessage);
                }
            }
            catch (Exception ex)
            {
                SetActionStatus("Loop playback restart failed. " + SanitizeActionMessage(ex.Message), isError: true);
            }
            finally
            {
                _isLoopRestartInFlight = false;
            }
        }

        private bool ShouldRestartLoopPlaybackAtBoundary()
        {
            if (!IsLoopPlaybackEnabledForFocusedPane() || _isLoopRestartInFlight)
            {
                return false;
            }

            var session = _workspaceCoordinator.CurrentSession ?? ReviewSessionSnapshot.Empty;
            if (session.PlaybackState != ReviewPlaybackState.Playing)
            {
                return false;
            }

            var loopRange = ResolveFocusedLoopRange();
            if (loopRange == null || !loopRange.HasAnyMarkers)
            {
                return IsSessionAtPlaybackEnd(session);
            }

            if (loopRange.IsInvalidRange)
            {
                return false;
            }

            return HasReachedLoopEnd(session, loopRange);
        }

        private static bool HasReachedLoopEnd(ReviewSessionSnapshot session, LoopPlaybackPaneRangeSnapshot loopRange)
        {
            if (session == null || !session.IsMediaOpen || loopRange == null)
            {
                return false;
            }

            if (loopRange.LoopOut != null)
            {
                if (loopRange.LoopOut.HasAbsoluteFrameIdentity &&
                    session.HasAbsoluteFrameIdentity &&
                    session.Position.FrameIndex.GetValueOrDefault() >= loopRange.LoopOut.AbsoluteFrameIndex.GetValueOrDefault())
                {
                    return true;
                }

                return session.Position.PresentationTime >= loopRange.LoopOut.PresentationTime + GetLoopBoundaryGuardTolerance(session);
            }

            return IsSessionAtPlaybackEnd(session);
        }

        private static bool IsSessionAtPlaybackEnd(ReviewSessionSnapshot session)
        {
            if (session == null || !session.IsMediaOpen || session.MediaInfo.Duration <= TimeSpan.Zero)
            {
                return false;
            }

            var tolerance = GetLoopBoundaryGuardTolerance(session);
            var endThreshold = session.MediaInfo.Duration - tolerance;
            if (endThreshold < TimeSpan.Zero)
            {
                endThreshold = TimeSpan.Zero;
            }

            return session.Position.PresentationTime >= endThreshold;
        }

        private static TimeSpan GetLoopBoundaryGuardTolerance(ReviewSessionSnapshot session)
        {
            var stepToleranceTicks = session != null && session.MediaInfo.PositionStep > TimeSpan.Zero
                ? session.MediaInfo.PositionStep.Ticks * 2L
                : 0L;
            return TimeSpan.FromTicks(Math.Max(stepToleranceTicks, LoopGuardTolerance.Ticks));
        }

        private void UpdateLoopPlaybackButtonContent(ReviewWorkspaceViewState viewState)
        {
            LoopPlaybackButton.Content = IsLoopPlaybackEnabledForFocusedPane(viewState)
                ? "Loop Playback: On"
                : "Loop Playback: Off";
            ToolTip.SetTip(
                LoopPlaybackButton,
                "Applies to the focused pane" + (_isCompareModeEnabled ? " using pane-local loop markers." : "."));
        }

        private string BuildLoopPlaybackStatusMessage()
        {
            var paneLabel = GetFocusedPaneDisplayLabel();
            if (!IsLoopPlaybackEnabledForFocusedPane())
            {
                return paneLabel + " loop playback disabled.";
            }

            var loopRange = ResolveFocusedLoopRange();
            if (loopRange == null || !loopRange.HasAnyMarkers)
            {
                return paneLabel + " loop playback enabled for the full media range.";
            }

            if (loopRange.IsInvalidRange)
            {
                return paneLabel + " loop playback is enabled, but the current A/B range is invalid.";
            }

            return loopRange.HasPendingMarkers
                ? paneLabel + " loop playback is enabled, but the current A/B range is still pending exact frame identity."
                : paneLabel + " loop playback is enabled for the current A/B range.";
        }

        private static ClipExportRequest CreateOutputClipExportRequest(ClipExportRequest seedRequest, string outputPath)
        {
            return new ClipExportRequest(
                seedRequest.SourceFilePath,
                outputPath,
                seedRequest.DisplayLabel,
                seedRequest.PaneId,
                seedRequest.IsPaneLocal,
                seedRequest.SessionSnapshot,
                seedRequest.LoopRange,
                seedRequest.IndexedFrameTimeResolver);
        }

        private static string BuildSuggestedClipFileName(ClipExportRequest request)
        {
            var baseName = string.IsNullOrWhiteSpace(request.SourceFilePath)
                ? "clip"
                : Path.GetFileNameWithoutExtension(request.SourceFilePath);
            var startSegment = request.LoopRange != null && request.LoopRange.LoopIn != null
                ? FormatClipFileNameTime(request.LoopRange.LoopIn.PresentationTime)
                : "start";
            var endSegment = request.LoopRange != null && request.LoopRange.LoopOut != null
                ? FormatClipFileNameTime(request.LoopRange.LoopOut.PresentationTime)
                : "end";

            return string.Format(
                CultureInfo.InvariantCulture,
                "{0}-{1}-{2}.mp4",
                SanitizeFileNameSegment(baseName),
                startSegment,
                endSegment);
        }

        private static string FormatClipFileNameTime(TimeSpan value)
        {
            if (value < TimeSpan.Zero)
            {
                value = TimeSpan.Zero;
            }

            return string.Format(
                CultureInfo.InvariantCulture,
                "{0:00}{1:00}{2:00}-{3:000}",
                (int)value.TotalHours,
                value.Minutes,
                value.Seconds,
                value.Milliseconds);
        }

        private static string SanitizeFileNameSegment(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return "clip";
            }

            var invalidCharacters = Path.GetInvalidFileNameChars();
            var sanitized = new string(value.Select(character => invalidCharacters.Contains(character) ? '-' : character).ToArray()).Trim();
            return string.IsNullOrWhiteSpace(sanitized) ? "clip" : sanitized;
        }

        private static string BuildClipExportSuccessMessage(ClipExportResult exportResult)
        {
            var duration = exportResult.ProbedDuration.HasValue
                ? exportResult.ProbedDuration.Value
                : exportResult.Plan.Duration;
            return string.Format(
                CultureInfo.InvariantCulture,
                "Clip exported to {0} ({1}).",
                Path.GetFileName(exportResult.Plan.OutputFilePath),
                FormatTime(duration));
        }

        private static string SanitizeActionMessage(string message)
        {
            return (message ?? string.Empty)
                .Replace(Environment.NewLine, " ")
                .Trim();
        }

        private void SetActionStatus(string message, bool isError = false)
        {
            _lastActionMessage = message ?? string.Empty;
            _lastActionIsError = isError && !string.IsNullOrWhiteSpace(_lastActionMessage);
            UpdateActionStatusPresentation();
        }

        private void UpdateActionStatusPresentation()
        {
            if (ActionStatusTextBlock == null)
            {
                return;
            }

            ActionStatusTextBlock.Text = _lastActionMessage;
            ActionStatusTextBlock.IsVisible = !string.IsNullOrWhiteSpace(_lastActionMessage);
            ActionStatusTextBlock.Foreground = _lastActionIsError
                ? ActionStatusErrorBrush
                : ActionStatusInfoBrush;
        }

        private void SetCompareModeEnabled(bool enabled, bool updateCheckBox)
        {
            if (_isCompareModeEnabled == enabled)
            {
                if (updateCheckBox && CompareModeCheckBox.IsChecked != enabled)
                {
                    CompareModeCheckBox.IsChecked = enabled;
                }

                RefreshUi(_hostController.CurrentViewState);
                return;
            }

            _isCompareModeEnabled = enabled;
            if (enabled)
            {
                EnsureComparePaneInitialized();
                SetActionStatus("Two-pane compare enabled. Transport, loop, and export commands now follow the focused pane.");
            }
            else
            {
                _hostController.TrySelectPane(PrimaryPaneId);
                SetActionStatus("Two-pane compare disabled. Returning focus to the primary pane.");
            }

            if (updateCheckBox && CompareModeCheckBox.IsChecked != enabled)
            {
                CompareModeCheckBox.IsChecked = enabled;
            }

            RefreshUi(_hostController.CurrentViewState);
        }

        private void EnsureComparePaneInitialized()
        {
            if (_compareSessionCoordinator != null && _compareVideoReviewEngine != null)
            {
                return;
            }

            _compareVideoReviewEngine = _videoReviewEngineFactory.Create(ComparePaneId);
            _compareSessionCoordinator = new ReviewSessionCoordinator(
                _compareVideoReviewEngine,
                CompareSessionId,
                "Compare A");
            _compareVideoReviewEngine.FramePresented += VideoReviewEngine_FramePresented;
            _workspaceCoordinator.TryBindPane(
                ComparePaneId,
                _compareSessionCoordinator,
                displayLabel: "Compare A");
            _hostController.Refresh();
        }

        private void UpdateCompareModeVisualState()
        {
            ComparePaneBorder.IsVisible = _isCompareModeEnabled;
            CompareStatusBorder.IsVisible = _isCompareModeEnabled;
            UseComparePaneButton.IsVisible = _isCompareModeEnabled;
            OpenComparePaneButton.IsVisible = _isCompareModeEnabled;
            if (PaneHostGrid != null && PaneHostGrid.ColumnDefinitions.Count > 1)
            {
                PaneHostGrid.ColumnDefinitions[1].Width = _isCompareModeEnabled
                    ? new GridLength(1d, GridUnitType.Star)
                    : new GridLength(0d);
            }
        }

        private bool TryFocusPane(string paneId)
        {
            if (string.IsNullOrWhiteSpace(paneId))
            {
                return false;
            }

            if (string.Equals(paneId, ComparePaneId, StringComparison.Ordinal))
            {
                EnsureComparePaneInitialized();
            }

            return _hostController.TrySelectPane(paneId);
        }

        private string GetFocusedOrPrimaryPaneId()
        {
            return GetFocusedPaneId(_workspaceCoordinator.GetWorkspaceSnapshot());
        }

        private static string GetFocusedPaneId(ReviewWorkspaceSnapshot snapshot)
        {
            snapshot = snapshot ?? ReviewWorkspaceSnapshot.Empty;
            if (!string.IsNullOrWhiteSpace(snapshot.FocusedPaneId))
            {
                return snapshot.FocusedPaneId;
            }

            if (!string.IsNullOrWhiteSpace(snapshot.ActivePaneId))
            {
                return snapshot.ActivePaneId;
            }

            return string.IsNullOrWhiteSpace(snapshot.PrimaryPaneId)
                ? PrimaryPaneId
                : snapshot.PrimaryPaneId;
        }

        private async Task SeekFocusedPaneBySecondsAsync(double deltaSeconds)
        {
            var viewState = _hostController.CurrentViewState;
            var paneState = FindPaneViewState(viewState, GetFocusedPaneId(_workspaceCoordinator.GetWorkspaceSnapshot()));
            if (paneState == null || !paneState.IsMediaOpen || !viewState.Transport.CanSeek)
            {
                return;
            }

            var targetSeconds = paneState.CurrentPosition.TotalSeconds + deltaSeconds;
            if (paneState.Duration > TimeSpan.Zero)
            {
                targetSeconds = Math.Min(paneState.Duration.TotalSeconds, targetSeconds);
            }

            targetSeconds = Math.Max(0d, targetSeconds);
            await _hostController.SeekToTimeAsync(TimeSpan.FromSeconds(targetSeconds));
            _hostController.Refresh();
        }

        private static string GetPaneIdFromSender(object sender)
        {
            var control = sender as Control;
            return control != null ? GetPaneIdFromTag(control.Tag) : string.Empty;
        }

        private static string GetPaneIdFromTag(object tag)
        {
            var paneId = tag as string;
            return string.IsNullOrWhiteSpace(paneId) ? string.Empty : paneId;
        }

        private string GetFocusedPaneDisplayLabel()
        {
            return GetFocusedPaneDisplayLabel(_hostController.CurrentViewState);
        }

        private string GetFocusedPaneDisplayLabel(ReviewWorkspaceViewState viewState)
        {
            var paneState = FindPaneViewState(viewState, GetFocusedPaneId(_workspaceCoordinator.GetWorkspaceSnapshot()));
            return paneState != null && !string.IsNullOrWhiteSpace(paneState.DisplayLabel)
                ? paneState.DisplayLabel
                : "Primary";
        }

        private static string GetPaneDisplayLabel(string paneId)
        {
            return string.Equals(paneId, ComparePaneId, StringComparison.Ordinal)
                ? "Compare pane"
                : "Primary pane";
        }

        private static PaneViewState FindPaneViewState(ReviewWorkspaceViewState viewState, string paneId)
        {
            if (viewState == null || viewState.Panes == null)
            {
                return null;
            }

            return viewState.Panes.FirstOrDefault(
                pane => pane != null && string.Equals(pane.PaneId, paneId, StringComparison.Ordinal));
        }

        private LoopCommandState ResolveEffectiveFocusedLoopState(ReviewWorkspaceViewState viewState)
        {
            if (!_isCompareModeEnabled)
            {
                return viewState != null ? viewState.Loop ?? LoopCommandState.Empty : LoopCommandState.Empty;
            }

            var paneState = FindPaneViewState(viewState, GetFocusedPaneId(_workspaceCoordinator.GetWorkspaceSnapshot()));
            return paneState != null ? paneState.Loop ?? LoopCommandState.Empty : LoopCommandState.Empty;
        }

        private bool CanExportFocusedLoop(out string toolTip)
        {
            ClipExportRequest request;
            if (TryBuildSeedClipExportRequest(out request, out toolTip))
            {
                toolTip = "The current loop is ready to export.";
                return true;
            }

            return false;
        }

        private string BuildCompareStatusText(ReviewWorkspaceViewState viewState)
        {
            var primaryState = FindPaneViewState(viewState, PrimaryPaneId);
            var compareState = FindPaneViewState(viewState, ComparePaneId);
            return string.Format(
                CultureInfo.InvariantCulture,
                "Compare: Primary {0} | Compare {1} | Focused pane: {2}. Loop and export stay bound to the selected pane.",
                BuildComparePaneStatus(primaryState),
                BuildComparePaneStatus(compareState),
                GetFocusedPaneDisplayLabel(viewState));
        }

        private static string BuildComparePaneStatus(PaneViewState paneState)
        {
            if (!PaneHasLoadedMedia(paneState))
            {
                return "not loaded";
            }

            return paneState.PlaybackState == ReviewPlaybackState.Playing
                ? "playing"
                : "paused";
        }

        private static string BuildPanePlaybackStateText(PaneViewState paneState)
        {
            if (!PaneHasLoadedMedia(paneState))
            {
                return "No video loaded";
            }

            switch (paneState.PlaybackState)
            {
                case ReviewPlaybackState.Playing:
                    return paneState.IsFocused ? "Playing | Focused" : "Playing";
                case ReviewPlaybackState.Paused:
                    return paneState.IsFocused ? "Paused | Focused" : "Paused";
                default:
                    return paneState.IsFocused ? "Ready | Focused" : "Ready";
            }
        }

        private TimeSpan ResolveTimelineContextTarget(Slider slider, ContextRequestedEventArgs e)
        {
            var maximum = Math.Max(slider.Minimum, slider.Maximum);
            var targetSeconds = slider.Value;
            Point point;
            if (e != null && e.TryGetPosition(slider, out point) && slider.Bounds.Width > 0d)
            {
                var ratio = Math.Max(0d, Math.Min(1d, point.X / slider.Bounds.Width));
                targetSeconds = slider.Minimum + ((maximum - slider.Minimum) * ratio);
            }

            targetSeconds = Math.Max(slider.Minimum, Math.Min(maximum, targetSeconds));
            return TimeSpan.FromSeconds(targetSeconds);
        }

        private LoopPlaybackPaneRangeSnapshot ResolveLoopRangeForPane(ReviewWorkspaceSnapshot snapshot, string paneId)
        {
            snapshot = snapshot ?? ReviewWorkspaceSnapshot.Empty;
            var resolvedPaneId = string.IsNullOrWhiteSpace(paneId)
                ? GetFocusedPaneId(snapshot)
                : paneId;

            ReviewWorkspacePaneSnapshot paneSnapshot;
            if (!string.IsNullOrWhiteSpace(resolvedPaneId) &&
                snapshot.TryGetPane(resolvedPaneId, out paneSnapshot) &&
                paneSnapshot != null)
            {
                if (_isCompareModeEnabled)
                {
                    return paneSnapshot.LoopRange;
                }

                LoopPlaybackPaneRangeSnapshot sharedPaneRange;
                if (snapshot.SharedLoopRange != null &&
                    snapshot.SharedLoopRange.TryGetPaneRange(resolvedPaneId, out sharedPaneRange))
                {
                    return sharedPaneRange;
                }

                if (paneSnapshot.LoopRange != null && paneSnapshot.LoopRange.HasAnyMarkers)
                {
                    return paneSnapshot.LoopRange;
                }
            }

            return snapshot.SharedLoopRange != null && snapshot.SharedLoopRange.PaneRanges.Count > 0
                ? snapshot.SharedLoopRange.PaneRanges[0]
                : null;
        }

        private bool CanOfferTimelineLoopMarkerAtTarget(
            string paneId,
            LoopPlaybackMarkerEndpoint endpoint,
            TimeSpan target,
            out string toolTip)
        {
            var snapshot = _workspaceCoordinator.GetWorkspaceSnapshot();
            var resolvedPaneId = string.IsNullOrWhiteSpace(paneId)
                ? GetFocusedPaneId(snapshot)
                : paneId;

            ReviewWorkspacePaneSnapshot paneSnapshot;
            if (string.IsNullOrWhiteSpace(resolvedPaneId) ||
                !snapshot.TryGetPane(resolvedPaneId, out paneSnapshot) ||
                !PaneHasLoadedMedia(paneSnapshot))
            {
                toolTip = "Load media into the selected pane before setting loop markers from the timeline.";
                return false;
            }

            var loopRange = ResolveLoopRangeForPane(snapshot, resolvedPaneId);
            var currentLabel = endpoint == LoopPlaybackMarkerEndpoint.In ? "Set Position A" : "Set Position B";
            var oppositeLabel = endpoint == LoopPlaybackMarkerEndpoint.In ? "Position B" : "Position A";
            var oppositeAnchor = endpoint == LoopPlaybackMarkerEndpoint.In
                ? loopRange != null ? loopRange.LoopOut : null
                : loopRange != null ? loopRange.LoopIn : null;

            if (oppositeAnchor != null && oppositeAnchor.IsPending)
            {
                toolTip = currentLabel + " is unavailable until " + oppositeLabel + " resolves exact frame identity.";
                return false;
            }

            if (oppositeAnchor != null && !IsTimelineLoopMarkerTimeOrderValid(endpoint, target, oppositeAnchor.PresentationTime))
            {
                toolTip = GetTimelineLoopMarkerOrderFailure(endpoint);
                return false;
            }

            toolTip = currentLabel + " at " + FormatTime(target) + ".";
            return true;
        }

        private async Task SetTimelineLoopMarkerAtAsync(
            string paneId,
            LoopPlaybackMarkerEndpoint endpoint,
            TimeSpan target)
        {
            if (!string.IsNullOrWhiteSpace(paneId) && !TryFocusPane(paneId))
            {
                return;
            }

            string toolTip;
            if (!CanOfferTimelineLoopMarkerAtTarget(paneId, endpoint, target, out toolTip))
            {
                SetActionStatus(toolTip, isError: true);
                return;
            }

            if (_hostController.CurrentViewState.Transport.IsPlaying)
            {
                await _hostController.PauseAsync();
            }

            await _hostController.SeekToTimeAsync(target);
            _hostController.Refresh();

            if (ShouldUsePaneLocalLoopCommands())
            {
                _hostController.SetPaneLoopMarker(GetFocusedOrPrimaryPaneId(), endpoint);
            }
            else
            {
                _hostController.SetSharedLoopMarker(endpoint, SynchronizedOperationScope.FocusedPane);
            }

            _hostController.Refresh();
            SetActionStatus(
                string.Format(
                    CultureInfo.InvariantCulture,
                    "{0} set at {1} on the {2} timeline.",
                    endpoint == LoopPlaybackMarkerEndpoint.In ? "Position A" : "Position B",
                    FormatTime(target),
                    GetFocusedPaneDisplayLabel().ToLowerInvariant()));
        }

        private static bool IsTimelineLoopMarkerTimeOrderValid(
            LoopPlaybackMarkerEndpoint endpoint,
            TimeSpan target,
            TimeSpan oppositeTarget)
        {
            return endpoint == LoopPlaybackMarkerEndpoint.In
                ? target <= oppositeTarget
                : target >= oppositeTarget;
        }

        private static string GetTimelineLoopMarkerOrderFailure(LoopPlaybackMarkerEndpoint endpoint)
        {
            return endpoint == LoopPlaybackMarkerEndpoint.In
                ? "Position A must be on or before position B."
                : "Position B must be on or after position A.";
        }

        private static bool TryGetTimelineContextCommandTarget(object sender, out TimelineContextCommandTarget contextTarget)
        {
            contextTarget = (sender as MenuItem)?.Tag as TimelineContextCommandTarget;
            return contextTarget != null;
        }

        private static string BuildVideoInfoText(string paneLabel, VideoMediaInfo mediaInfo)
        {
            var text = new System.Text.StringBuilder();
            text.AppendLine(paneLabel + " Video");
            text.AppendLine(new string('=', paneLabel.Length + 6));
            text.AppendLine();
            text.AppendLine("File");
            text.AppendLine("----");
            text.AppendLine(FormatInspectorText(mediaInfo.FilePath));
            text.AppendLine();
            text.AppendLine("Summary");
            text.AppendLine("-------");
            text.AppendLine("Duration: " + FormatInspectorDuration(mediaInfo.Duration));
            text.AppendLine("Frame rate: " + FormatInspectorFrameRate(mediaInfo.FramesPerSecond));
            text.AppendLine("Display resolution: " + FormatInspectorResolution(mediaInfo.DisplayWidth, mediaInfo.DisplayHeight));
            text.AppendLine("Display aspect: " + FormatInspectorRatio(
                mediaInfo.DisplayAspectRatioNumerator,
                mediaInfo.DisplayAspectRatioDenominator));
            text.AppendLine();
            text.AppendLine("Video");
            text.AppendLine("-----");
            text.AppendLine("Codec: " + FormatInspectorText(mediaInfo.VideoCodecName));
            text.AppendLine("Coded resolution: " + FormatInspectorResolution(mediaInfo.PixelWidth, mediaInfo.PixelHeight));
            text.AppendLine("Source pixel format: " + FormatInspectorText(mediaInfo.SourcePixelFormatName));
            text.AppendLine("Bit depth: " + FormatInspectorBitDepth(mediaInfo.VideoBitDepth));
            text.AppendLine("Bitrate: " + FormatInspectorBitRate(mediaInfo.VideoBitRate));
            text.AppendLine("Color space: " + FormatInspectorText(mediaInfo.VideoColorSpace));
            text.AppendLine("Color range: " + FormatInspectorText(mediaInfo.VideoColorRange));
            text.AppendLine("Primaries: " + FormatInspectorText(mediaInfo.VideoColorPrimaries));
            text.AppendLine("Transfer: " + FormatInspectorText(mediaInfo.VideoColorTransfer));
            text.AppendLine();
            text.AppendLine("Audio");
            text.AppendLine("-----");
            text.AppendLine("Has audio stream: " + (mediaInfo.HasAudioStream ? "Yes" : "No"));
            text.AppendLine("Audio playback available: " + (mediaInfo.IsAudioPlaybackAvailable ? "Yes" : "No"));
            text.AppendLine("Codec: " + FormatInspectorText(mediaInfo.AudioCodecName));
            text.AppendLine("Sample rate: " + FormatInspectorSampleRate(mediaInfo.AudioSampleRate));
            text.AppendLine("Channels: " + FormatInspectorChannelCount(mediaInfo.AudioChannelCount));
            text.AppendLine("Bit depth: " + FormatInspectorBitDepth(mediaInfo.AudioBitDepth));
            text.AppendLine("Bitrate: " + FormatInspectorBitRate(mediaInfo.AudioBitRate));
            text.AppendLine();
            text.AppendLine("Advanced");
            text.AppendLine("--------");
            text.AppendLine("Video stream index: " + FormatInspectorIndex(mediaInfo.VideoStreamIndex));
            text.AppendLine("Audio stream index: " + FormatInspectorIndex(mediaInfo.AudioStreamIndex));
            text.AppendLine("Nominal frame rate: " + FormatInspectorFraction(
                mediaInfo.NominalFrameRateNumerator,
                mediaInfo.NominalFrameRateDenominator));
            text.AppendLine("Stream time base: " + FormatInspectorFraction(
                mediaInfo.StreamTimeBaseNumerator,
                mediaInfo.StreamTimeBaseDenominator));
            text.AppendLine("Position step: " + FormatInspectorDuration(mediaInfo.PositionStep));
            return text.ToString().Trim();
        }

        private static string FormatInspectorDuration(TimeSpan duration)
        {
            return duration > TimeSpan.Zero ? FormatTime(duration) : "Unknown";
        }

        private static string FormatInspectorFrameRate(double framesPerSecond)
        {
            return framesPerSecond > 0d
                ? string.Format(CultureInfo.InvariantCulture, "{0:0.###} fps", framesPerSecond)
                : "Unknown";
        }

        private static string FormatInspectorResolution(int width, int height)
        {
            return width > 0 && height > 0
                ? string.Format(CultureInfo.InvariantCulture, "{0} x {1}", width, height)
                : "Unknown";
        }

        private static string FormatInspectorResolution(int? width, int? height)
        {
            return width.HasValue && height.HasValue
                ? FormatInspectorResolution(width.Value, height.Value)
                : "Unknown";
        }

        private static string FormatInspectorRatio(int? numerator, int? denominator)
        {
            return numerator.HasValue &&
                   denominator.HasValue &&
                   numerator.Value > 0 &&
                   denominator.Value > 0
                ? string.Format(CultureInfo.InvariantCulture, "{0}:{1}", numerator.Value, denominator.Value)
                : "Unknown";
        }

        private static string FormatInspectorText(string value)
        {
            return string.IsNullOrWhiteSpace(value) ? "Unknown" : value;
        }

        private static string FormatInspectorBitDepth(int? value)
        {
            return value.HasValue && value.Value > 0
                ? string.Format(CultureInfo.InvariantCulture, "{0}-bit", value.Value)
                : "Unknown";
        }

        private static string FormatInspectorBitRate(long? value)
        {
            return value.HasValue && value.Value > 0L
                ? string.Format(CultureInfo.InvariantCulture, "{0:N0} bps", value.Value)
                : "Unknown";
        }

        private static string FormatInspectorSampleRate(int value)
        {
            return value > 0
                ? string.Format(CultureInfo.InvariantCulture, "{0:N0} Hz", value)
                : "Unknown";
        }

        private static string FormatInspectorChannelCount(int value)
        {
            return value > 0 ? value.ToString(CultureInfo.InvariantCulture) : "Unknown";
        }

        private static string FormatInspectorIndex(int value)
        {
            return value >= 0 ? value.ToString(CultureInfo.InvariantCulture) : "Unknown";
        }

        private static string FormatInspectorFraction(int numerator, int denominator)
        {
            return numerator > 0 && denominator > 0
                ? string.Format(CultureInfo.InvariantCulture, "{0}/{1}", numerator, denominator)
                : "Unknown";
        }

        private void RefreshPaneUi(ReviewWorkspaceViewState viewState, string paneId)
        {
            Border paneBorder;
            Border emptySurfaceOverlay;
            TextBlock paneTitleTextBlock;
            TextBlock paneFileTextBlock;
            TextBlock paneStateTextBlock;
            TextBlock emptySurfaceTextBlock;
            Image videoImage;
            Slider positionSlider;
            TextBlock currentPositionTextBlock;
            TextBlock loopStatusTextBlock;
            TextBlock durationTextBlock;
            TextBlock frameStatusTextBlock;
            TextBox frameTextBox;
            Button stepBackButton;
            Button playPauseButton;
            Button stepForwardButton;
            Button usePaneButton;
            Button openPaneButton;
            if (!TryGetPaneControls(
                paneId,
                out paneBorder,
                out emptySurfaceOverlay,
                out paneTitleTextBlock,
                out paneFileTextBlock,
                out paneStateTextBlock,
                out emptySurfaceTextBlock,
                out videoImage,
                out positionSlider,
                out currentPositionTextBlock,
                out loopStatusTextBlock,
                out durationTextBlock,
                out frameStatusTextBlock,
                out frameTextBox,
                out stepBackButton,
                out playPauseButton,
                out stepForwardButton,
                out usePaneButton,
                out openPaneButton))
            {
                return;
            }

            var paneState = FindPaneViewState(viewState, paneId);
            var paneHasLoadedMedia = PaneHasLoadedMedia(paneState);
            if (string.Equals(paneId, PrimaryPaneId, StringComparison.Ordinal))
            {
                PrimaryPaneHeaderBorder.IsVisible = _isCompareModeEnabled;
            }

            paneBorder.BorderBrush = paneState != null && paneState.IsFocused
                ? SelectedPaneBrush
                : IdlePaneBrush;
            paneBorder.BorderThickness = paneState != null && paneState.IsFocused
                ? new Thickness(2d)
                : new Thickness(1d);

            paneTitleTextBlock.Text = string.Format(
                CultureInfo.InvariantCulture,
                "{0}{1}",
                string.Equals(paneId, ComparePaneId, StringComparison.Ordinal) ? "Compare" : "Primary",
                paneState != null && paneState.IsFocused ? " (Focused)" : string.Empty);
            paneFileTextBlock.Text = paneHasLoadedMedia
                ? Path.GetFileName(paneState.CurrentFilePath)
                : string.Empty;
            paneStateTextBlock.Text = BuildPanePlaybackStateText(paneState);
            ToolTip.SetTip(paneFileTextBlock, paneHasLoadedMedia ? paneState.CurrentFilePath : string.Empty);

            usePaneButton.IsEnabled = !string.Equals(paneId, ComparePaneId, StringComparison.Ordinal) || _isCompareModeEnabled;
            openPaneButton.IsEnabled = !string.Equals(paneId, ComparePaneId, StringComparison.Ordinal) || _isCompareModeEnabled;
            stepBackButton.IsEnabled = paneHasLoadedMedia;
            playPauseButton.IsEnabled = paneHasLoadedMedia;
            stepForwardButton.IsEnabled = paneHasLoadedMedia;
            playPauseButton.Content = paneState != null && paneState.PlaybackState == ReviewPlaybackState.Playing
                ? "Pause"
                : "Play";

            currentPositionTextBlock.Text = paneHasLoadedMedia
                ? FormatTime(paneState.CurrentPosition)
                : "00:00:00.000";
            durationTextBlock.Text = paneHasLoadedMedia
                ? FormatTime(paneState.Duration)
                : "00:00:00.000";
            loopStatusTextBlock.Text = paneState != null && paneState.Loop != null
                ? paneState.Loop.StatusText
                : "Loop: off";
            ToolTip.SetTip(
                loopStatusTextBlock,
                paneState != null && paneState.Loop != null ? paneState.Loop.ToolTip : "No loop markers are active.");
            frameStatusTextBlock.Text = BuildPaneFrameStatus(paneState);
            frameTextBox.IsEnabled = paneHasLoadedMedia;
            ToolTip.SetTip(frameTextBox, "Enter a 1-based frame number and press Enter.");
            if (!frameTextBox.IsFocused)
            {
                frameTextBox.Text = paneState != null && paneState.FrameIndex.HasValue
                    ? (paneState.FrameIndex.Value + 1L).ToString(CultureInfo.InvariantCulture)
                    : string.Empty;
            }

            _suppressSliderUpdate = true;
            try
            {
                positionSlider.IsEnabled = paneHasLoadedMedia;
                positionSlider.Maximum = paneHasLoadedMedia && paneState.Duration > TimeSpan.Zero
                    ? paneState.Duration.TotalSeconds
                    : 1d;
                positionSlider.Value = paneHasLoadedMedia
                    ? Math.Min(positionSlider.Maximum, paneState.CurrentPosition.TotalSeconds)
                    : 0d;
            }
            finally
            {
                _suppressSliderUpdate = false;
            }

            if (!paneHasLoadedMedia)
            {
                emptySurfaceTextBlock.Text = string.Equals(paneId, ComparePaneId, StringComparison.Ordinal)
                    ? "Open a compare video to review both panes together."
                    : "Open a primary video to start review playback.";
            }

            emptySurfaceOverlay.IsVisible = !paneHasLoadedMedia && videoImage.Source == null;
        }

        private static bool PaneHasLoadedMedia(PaneViewState paneState)
        {
            return paneState != null &&
                   paneState.IsMediaOpen &&
                   !string.IsNullOrWhiteSpace(paneState.CurrentFilePath);
        }

        private static bool PaneHasLoadedMedia(ReviewWorkspacePaneSnapshot paneSnapshot)
        {
            return paneSnapshot != null &&
                   paneSnapshot.PlaybackState != ReviewPlaybackState.Closed &&
                   !string.IsNullOrWhiteSpace(paneSnapshot.CurrentFilePath);
        }

        private static string BuildPaneFrameStatus(PaneViewState paneState)
        {
            if (paneState == null || !paneState.FrameIndex.HasValue)
            {
                return "Frame --";
            }

            return string.Format(
                CultureInfo.InvariantCulture,
                paneState.IsFrameIndexAbsolute
                    ? "Frame {0}"
                    : "Frame {0} (pending absolute)",
                paneState.FrameIndex.Value + 1L);
        }

        private string BuildFocusedFrameStatus(ReviewWorkspaceViewState viewState)
        {
            return BuildPaneFrameStatus(FindPaneViewState(viewState, GetFocusedPaneId(_workspaceCoordinator.GetWorkspaceSnapshot())));
        }

        private bool TryGetPaneControls(
            string paneId,
            out Border paneBorder,
            out Border emptySurfaceOverlay,
            out TextBlock paneTitleTextBlock,
            out TextBlock paneFileTextBlock,
            out TextBlock paneStateTextBlock,
            out TextBlock emptySurfaceTextBlock,
            out Image videoImage,
            out Slider positionSlider,
            out TextBlock currentPositionTextBlock,
            out TextBlock loopStatusTextBlock,
            out TextBlock durationTextBlock,
            out TextBlock frameStatusTextBlock,
            out TextBox frameTextBox,
            out Button stepBackButton,
            out Button playPauseButton,
            out Button stepForwardButton,
            out Button usePaneButton,
            out Button openPaneButton)
        {
            if (string.Equals(paneId, ComparePaneId, StringComparison.Ordinal))
            {
                paneBorder = ComparePaneBorder;
                emptySurfaceOverlay = CompareEmptySurfaceOverlay;
                paneTitleTextBlock = ComparePaneTitleTextBlock;
                paneFileTextBlock = ComparePaneFileTextBlock;
                paneStateTextBlock = ComparePaneStateTextBlock;
                emptySurfaceTextBlock = CompareEmptySurfaceText;
                videoImage = CompareVideoImage;
                positionSlider = ComparePositionSlider;
                currentPositionTextBlock = CompareCurrentPositionTextBlock;
                loopStatusTextBlock = CompareLoopStatusTextBlock;
                durationTextBlock = CompareDurationTextBlock;
                frameStatusTextBlock = CompareFrameStatusTextBlock;
                frameTextBox = ComparePaneFrameTextBox;
                stepBackButton = ComparePaneStepBackButton;
                playPauseButton = ComparePanePlayPauseButton;
                stepForwardButton = ComparePaneStepForwardButton;
                usePaneButton = UseComparePaneButton;
                openPaneButton = OpenComparePaneButton;
                return true;
            }

            paneBorder = PrimaryPaneBorder;
            emptySurfaceOverlay = PrimaryEmptySurfaceOverlay;
            paneTitleTextBlock = PrimaryPaneTitleTextBlock;
            paneFileTextBlock = PrimaryPaneFileTextBlock;
            paneStateTextBlock = PrimaryPaneStateTextBlock;
            emptySurfaceTextBlock = PrimaryEmptySurfaceText;
            videoImage = PrimaryVideoImage;
            positionSlider = PrimaryPositionSlider;
            currentPositionTextBlock = PrimaryCurrentPositionTextBlock;
            loopStatusTextBlock = PrimaryLoopStatusTextBlock;
            durationTextBlock = PrimaryDurationTextBlock;
            frameStatusTextBlock = PrimaryFrameStatusTextBlock;
            frameTextBox = PrimaryPaneFrameTextBox;
            stepBackButton = PrimaryPaneStepBackButton;
            playPauseButton = PrimaryPanePlayPauseButton;
            stepForwardButton = PrimaryPaneStepForwardButton;
            usePaneButton = UsePrimaryPaneButton;
            openPaneButton = OpenPrimaryPaneButton;
            return true;
        }

        private IVideoReviewEngine GetEngineForPane(string paneId)
        {
            if (string.Equals(paneId, ComparePaneId, StringComparison.Ordinal) && _compareVideoReviewEngine != null)
            {
                return _compareVideoReviewEngine;
            }

            return _videoReviewEngine;
        }

        private bool ShouldUsePaneLocalLoopCommands()
        {
            return _isCompareModeEnabled;
        }

        private bool IsLoopPlaybackEnabledForPane(string paneId)
        {
            var resolvedPaneId = string.IsNullOrWhiteSpace(paneId)
                ? GetFocusedPaneId(_workspaceCoordinator.GetWorkspaceSnapshot())
                : paneId;
            return !string.IsNullOrWhiteSpace(resolvedPaneId) && _loopPlaybackEnabledPaneIds.Contains(resolvedPaneId);
        }

        private bool IsLoopPlaybackEnabledForFocusedPane()
        {
            return IsLoopPlaybackEnabledForFocusedPane(_hostController.CurrentViewState);
        }

        private bool IsLoopPlaybackEnabledForFocusedPane(ReviewWorkspaceViewState viewState)
        {
            return IsLoopPlaybackEnabledForPane(GetFocusedPaneId(_workspaceCoordinator.GetWorkspaceSnapshot()));
        }
    }
}
