using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
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

        private async void NextFrameButton_Click(object sender, RoutedEventArgs e)
        {
            await _hostController.StepForwardAsync();
            _hostController.Refresh();
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

        private async void OpenRecentButton_Click(object sender, RoutedEventArgs e)
        {
            var entry = RecentFilesListBox.SelectedItem as RecentFileViewState;
            if (entry == null)
            {
                return;
            }

            if (!File.Exists(entry.FilePath))
            {
                _hostController.RemoveRecentFile(entry.FilePath);
                RefreshUi(_hostController.CurrentViewState);
                SetActionStatus("That recent file no longer exists, so it was removed from the list.", isError: true);
                return;
            }

            await OpenMediaAsync(entry.FilePath, GetFocusedOrPrimaryPaneId());
        }

        private void ClearRecentButton_Click(object sender, RoutedEventArgs e)
        {
            _hostController.ClearRecentFiles();
            RefreshUi(_hostController.CurrentViewState);
            SetActionStatus("Recent files cleared.");
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

        private void RecentFilesListBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            UpdateRecentFileButtonState(_hostController.CurrentViewState != null
                ? _hostController.CurrentViewState.RecentFiles
                : RecentFilesCommandState.Empty);
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

            OpenCompareButton.IsEnabled = true;
            CloseButton.IsEnabled = transport.CanCloseMedia;
            PlayPauseButton.IsEnabled = transport.CanTogglePlayPause;
            PreviousFrameButton.IsEnabled = transport.CanStepBackward;
            NextFrameButton.IsEnabled = transport.CanStepForward;
            SetLoopAButton.IsEnabled = effectiveLoopState.CanSetMarkers;
            SetLoopBButton.IsEnabled = effectiveLoopState.CanSetMarkers;
            ClearLoopButton.IsEnabled = effectiveLoopState.CanClearMarkers;
            LoopPlaybackButton.IsEnabled = transport.CanControlTransport;
            ExportClipButton.IsEnabled = canExport;
            ToolTip.SetTip(ExportClipButton, exportToolTip);
            PlayPauseButton.Content = transport.IsPlaying ? "Pause" : "Play";

            UpdateCompareModeVisualState();
            UpdateLoopPlaybackButtonContent(viewState);

            FocusedPaneTextBlock.Text = "Focused Pane: " + GetFocusedPaneDisplayLabel(viewState);
            PlaybackStatusTextBlock.Text = viewState != null ? viewState.PlaybackMessage : "Ready.";
            MediaSummaryTextBlock.Text = viewState != null ? viewState.MediaSummary : string.Empty;
            FrameStatusTextBlock.Text = BuildFocusedFrameStatus(viewState);

            RefreshPaneUi(viewState, PrimaryPaneId);
            RefreshPaneUi(viewState, ComparePaneId);

            UpdateActionStatusPresentation();
            RefreshRecentFilesUi(recentFiles, recentEntries);
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

        private void RefreshRecentFilesUi(RecentFilesCommandState recentFiles, IReadOnlyList<RecentFileViewState> recentEntries)
        {
            var selectedPath = (RecentFilesListBox.SelectedItem as RecentFileViewState)?.FilePath;
            RecentFilesListBox.ItemsSource = recentEntries;
            RecentFilesStatusTextBlock.Text = recentFiles != null ? recentFiles.StatusText : "No recent files.";
            RecentFilesStatusTextBlock.IsVisible = recentEntries == null || recentEntries.Count == 0;
            RecentFilesPanel.IsVisible = recentEntries != null && recentEntries.Count > 0;

            if (!string.IsNullOrWhiteSpace(selectedPath) && recentEntries != null)
            {
                RecentFilesListBox.SelectedItem = recentEntries.FirstOrDefault(
                    entry => string.Equals(entry.FilePath, selectedPath, StringComparison.OrdinalIgnoreCase));
            }

            UpdateRecentFileButtonState(recentFiles);
        }

        private void UpdateRecentFileButtonState(RecentFilesCommandState recentFiles)
        {
            var hasSelection = RecentFilesListBox != null && RecentFilesListBox.SelectedItem is RecentFileViewState;
            OpenRecentButton.IsEnabled = hasSelection;
            ClearRecentButton.IsEnabled = recentFiles != null && recentFiles.CanClear;
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

        private void RefreshPaneUi(ReviewWorkspaceViewState viewState, string paneId)
        {
            Border paneBorder;
            Border emptySurfaceOverlay;
            TextBlock paneTitleTextBlock;
            TextBlock paneFileTextBlock;
            TextBlock emptySurfaceTextBlock;
            Image videoImage;
            Slider positionSlider;
            TextBlock currentPositionTextBlock;
            TextBlock loopStatusTextBlock;
            TextBlock durationTextBlock;
            TextBlock frameStatusTextBlock;
            Button usePaneButton;
            Button openPaneButton;
            if (!TryGetPaneControls(
                paneId,
                out paneBorder,
                out emptySurfaceOverlay,
                out paneTitleTextBlock,
                out paneFileTextBlock,
                out emptySurfaceTextBlock,
                out videoImage,
                out positionSlider,
                out currentPositionTextBlock,
                out loopStatusTextBlock,
                out durationTextBlock,
                out frameStatusTextBlock,
                out usePaneButton,
                out openPaneButton))
            {
                return;
            }

            var paneState = FindPaneViewState(viewState, paneId);
            var paneHasLoadedMedia = PaneHasLoadedMedia(paneState);
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
            ToolTip.SetTip(paneFileTextBlock, paneHasLoadedMedia ? paneState.CurrentFilePath : string.Empty);

            usePaneButton.IsEnabled = !string.Equals(paneId, ComparePaneId, StringComparison.Ordinal) || _isCompareModeEnabled;
            openPaneButton.IsEnabled = !string.Equals(paneId, ComparePaneId, StringComparison.Ordinal) || _isCompareModeEnabled;

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
            out TextBlock emptySurfaceTextBlock,
            out Image videoImage,
            out Slider positionSlider,
            out TextBlock currentPositionTextBlock,
            out TextBlock loopStatusTextBlock,
            out TextBlock durationTextBlock,
            out TextBlock frameStatusTextBlock,
            out Button usePaneButton,
            out Button openPaneButton)
        {
            if (string.Equals(paneId, ComparePaneId, StringComparison.Ordinal))
            {
                paneBorder = ComparePaneBorder;
                emptySurfaceOverlay = CompareEmptySurfaceOverlay;
                paneTitleTextBlock = ComparePaneTitleTextBlock;
                paneFileTextBlock = ComparePaneFileTextBlock;
                emptySurfaceTextBlock = CompareEmptySurfaceText;
                videoImage = CompareVideoImage;
                positionSlider = ComparePositionSlider;
                currentPositionTextBlock = CompareCurrentPositionTextBlock;
                loopStatusTextBlock = CompareLoopStatusTextBlock;
                durationTextBlock = CompareDurationTextBlock;
                frameStatusTextBlock = CompareFrameStatusTextBlock;
                usePaneButton = UseComparePaneButton;
                openPaneButton = OpenComparePaneButton;
                return true;
            }

            paneBorder = PrimaryPaneBorder;
            emptySurfaceOverlay = PrimaryEmptySurfaceOverlay;
            paneTitleTextBlock = PrimaryPaneTitleTextBlock;
            paneFileTextBlock = PrimaryPaneFileTextBlock;
            emptySurfaceTextBlock = PrimaryEmptySurfaceText;
            videoImage = PrimaryVideoImage;
            positionSlider = PrimaryPositionSlider;
            currentPositionTextBlock = PrimaryCurrentPositionTextBlock;
            loopStatusTextBlock = PrimaryLoopStatusTextBlock;
            durationTextBlock = PrimaryDurationTextBlock;
            frameStatusTextBlock = PrimaryFrameStatusTextBlock;
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

        private bool IsLoopPlaybackEnabledForFocusedPane()
        {
            return IsLoopPlaybackEnabledForFocusedPane(_hostController.CurrentViewState);
        }

        private bool IsLoopPlaybackEnabledForFocusedPane(ReviewWorkspaceViewState viewState)
        {
            var paneId = GetFocusedPaneId(_workspaceCoordinator.GetWorkspaceSnapshot());
            return !string.IsNullOrWhiteSpace(paneId) && _loopPlaybackEnabledPaneIds.Contains(paneId);
        }
    }
}
