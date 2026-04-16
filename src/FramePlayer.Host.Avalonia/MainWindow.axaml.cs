using System;
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
        private static readonly TimeSpan LoopGuardTolerance = TimeSpan.FromMilliseconds(100);
        private static readonly IBrush ActionStatusInfoBrush = new SolidColorBrush(Color.Parse("#9BB0C7"));
        private static readonly IBrush ActionStatusErrorBrush = new SolidColorBrush(Color.Parse("#F29BA7"));

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
        private bool _loopPlaybackEnabled;
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
            _videoReviewEngine = _videoReviewEngineFactory.Create("pane-primary");
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
            _hostController.ViewStateChanged -= HostController_ViewStateChanged;
            _hostController.Dispose();
            _workspaceCoordinator.Dispose();
            _sessionCoordinator.Dispose();
            _videoReviewEngine.Dispose();
        }

        private async void OpenButton_Click(object sender, RoutedEventArgs e)
        {
            var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = "Open Media",
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
                return;
            }

            var filePath = files[0].TryGetLocalPath();
            if (string.IsNullOrWhiteSpace(filePath))
            {
                return;
            }

            await OpenMediaAsync(filePath);
        }

        private async void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            await _hostController.CloseAsync();
            _hostController.Refresh();
            SetActionStatus("Media closed.");
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
            _hostController.SetSharedLoopMarker(LoopPlaybackMarkerEndpoint.In, SynchronizedOperationScope.FocusedPane);
        }

        private void SetLoopBButton_Click(object sender, RoutedEventArgs e)
        {
            _hostController.SetSharedLoopMarker(LoopPlaybackMarkerEndpoint.Out, SynchronizedOperationScope.FocusedPane);
        }

        private void ClearLoopButton_Click(object sender, RoutedEventArgs e)
        {
            _hostController.ClearSharedLoopRange();
        }

        private void LoopPlaybackButton_Click(object sender, RoutedEventArgs e)
        {
            _loopPlaybackEnabled = !_loopPlaybackEnabled;
            UpdateLoopPlaybackButtonContent();
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

            await OpenMediaAsync(entry.FilePath);
        }

        private void ClearRecentButton_Click(object sender, RoutedEventArgs e)
        {
            _hostController.ClearRecentFiles();
            RefreshUi(_hostController.CurrentViewState);
            SetActionStatus("Recent files cleared.");
        }

        private async void PositionSlider_PropertyChanged(object sender, AvaloniaPropertyChangedEventArgs e)
        {
            if (_suppressSliderUpdate || e.Property != Slider.ValueProperty || !_hostController.CurrentViewState.Transport.CanSeek)
            {
                return;
            }

            await _hostController.SeekToTimeAsync(TimeSpan.FromSeconds(PositionSlider.Value));
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

            await OpenMediaAsync(startupOpenFilePath);
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
                VideoImage.Source = e != null ? AvaloniaFrameBufferPresenter.CreateBitmap(e.FrameBuffer) : null;
                EmptySurfaceText.IsVisible = VideoImage.Source == null;
            });
        }

        private async void PositionTimer_Tick(object sender, EventArgs e)
        {
            if (_loopPlaybackEnabled &&
                ShouldRestartLoopPlaybackAtBoundary())
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

            PlayPauseButton.IsEnabled = transport.CanTogglePlayPause;
            CloseButton.IsEnabled = transport.CanCloseMedia;
            PreviousFrameButton.IsEnabled = transport.CanStepBackward;
            NextFrameButton.IsEnabled = transport.CanStepForward;
            SetLoopAButton.IsEnabled = viewState != null && viewState.Loop.CanSetMarkers;
            SetLoopBButton.IsEnabled = viewState != null && viewState.Loop.CanSetMarkers;
            ClearLoopButton.IsEnabled = viewState != null && viewState.Loop.CanClearMarkers;
            LoopPlaybackButton.IsEnabled = transport.CanControlTransport;
            ExportClipButton.IsEnabled = viewState != null && viewState.Export.CanExportCurrentLoop;
            ToolTip.SetTip(ExportClipButton, viewState != null ? viewState.Export.StatusText : string.Empty);
            PlayPauseButton.Content = transport.IsPlaying ? "Pause" : "Play";
            UpdateLoopPlaybackButtonContent();

            PlaybackStatusTextBlock.Text = viewState != null ? viewState.PlaybackMessage : "Ready.";
            MediaSummaryTextBlock.Text = viewState != null ? viewState.MediaSummary : string.Empty;
            LoopStatusTextBlock.Text = viewState != null ? viewState.Loop.StatusText : "Loop: off";

            var position = _workspaceCoordinator.CurrentSession.Position.PresentationTime;
            var duration = _workspaceCoordinator.CurrentSession.MediaInfo.Duration;
            CurrentPositionTextBlock.Text = FormatTime(position);
            DurationTextBlock.Text = FormatTime(duration);
            FrameStatusTextBlock.Text = BuildFrameStatus();

            _suppressSliderUpdate = true;
            try
            {
                PositionSlider.Maximum = duration > TimeSpan.Zero ? duration.TotalSeconds : 1d;
                PositionSlider.Value = Math.Min(PositionSlider.Maximum, position.TotalSeconds);
            }
            finally
            {
                _suppressSliderUpdate = false;
            }

            if (_workspaceCoordinator.CurrentSession == null || !_workspaceCoordinator.CurrentSession.IsMediaOpen)
            {
                VideoImage.Source = null;
                EmptySurfaceText.IsVisible = true;
            }

            UpdateActionStatusPresentation();
            RefreshRecentFilesUi(recentFiles, recentEntries);
        }

        private bool TryBuildSeedClipExportRequest(out ClipExportRequest request, out string failureMessage)
        {
            request = null;
            var session = _workspaceCoordinator.CurrentSession ?? ReviewSessionSnapshot.Empty;
            var loopRange = ResolveFocusedLoopRange();
            var viewState = _hostController.CurrentViewState ?? ReviewWorkspaceViewState.Empty;
            if (!viewState.Export.IsToolingAvailable)
            {
                failureMessage = viewState.Export.StatusText;
                return false;
            }

            if (!session.IsMediaOpen ||
                loopRange == null ||
                !loopRange.HasLoopIn ||
                !loopRange.HasLoopOut ||
                loopRange.HasPendingMarkers ||
                loopRange.IsInvalidRange)
            {
                failureMessage = !string.IsNullOrWhiteSpace(viewState.Export.StatusText)
                    ? viewState.Export.StatusText
                    : "Set an exact A/B loop before exporting a clip.";
                return false;
            }

            var snapshot = _workspaceCoordinator.GetWorkspaceSnapshot();
            request = new ClipExportRequest(
                session.CurrentFilePath,
                string.Empty,
                session.DisplayLabel,
                snapshot.FocusedPaneId,
                false,
                session,
                loopRange,
                new IndexedFrameTimeResolverAdapter(_videoReviewEngine as FfmpegReviewEngine));
            failureMessage = string.Empty;
            return true;
        }

        private LoopPlaybackPaneRangeSnapshot ResolveFocusedLoopRange()
        {
            var snapshot = _workspaceCoordinator.GetWorkspaceSnapshot();
            var paneId = !string.IsNullOrWhiteSpace(snapshot.FocusedPaneId)
                ? snapshot.FocusedPaneId
                : snapshot.ActivePaneId;

            ReviewWorkspacePaneSnapshot paneSnapshot;
            if (!string.IsNullOrWhiteSpace(paneId) &&
                snapshot.TryGetPane(paneId, out paneSnapshot) &&
                paneSnapshot != null &&
                paneSnapshot.LoopRange != null &&
                paneSnapshot.LoopRange.HasAnyMarkers)
            {
                return paneSnapshot.LoopRange;
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

        private string BuildFrameStatus()
        {
            var position = _workspaceCoordinator.CurrentSession.Position;
            if (position == null || !position.FrameIndex.HasValue)
            {
                return "Frame --";
            }

            return string.Format(
                CultureInfo.InvariantCulture,
                position.IsFrameIndexAbsolute
                    ? "Frame {0}"
                    : "Frame {0} (pending absolute)",
                position.FrameIndex.Value + 1L);
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

        private async Task OpenMediaAsync(string filePath)
        {
            if (string.IsNullOrWhiteSpace(filePath))
            {
                return;
            }

            await _hostController.OpenAsync(filePath);
            _hostController.Refresh();
            SetActionStatus(string.Format(
                CultureInfo.InvariantCulture,
                "Opened {0}.",
                Path.GetFileName(filePath)));
        }

        private void RefreshRecentFilesUi(RecentFilesCommandState recentFiles, System.Collections.Generic.IReadOnlyList<RecentFileViewState> recentEntries)
        {
            var selectedPath = (RecentFilesListBox.SelectedItem as RecentFileViewState)?.FilePath;
            RecentFilesListBox.ItemsSource = recentEntries;
            RecentFilesStatusTextBlock.Text = recentFiles != null ? recentFiles.StatusText : "No recent files.";
            RecentFilesStatusTextBlock.IsVisible = recentEntries == null || recentEntries.Count == 0;

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
                var statusMessage = "Loop playback restarted from the beginning.";

                if (loopRange != null && loopRange.HasAnyMarkers && !loopRange.IsInvalidRange)
                {
                    restartTime = loopRange.EffectiveStartTime;
                    statusMessage = loopRange.HasPendingMarkers
                        ? "Loop playback restarted from the pending A/B range."
                        : "Loop playback restarted from loop-in.";
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
            if (!_loopPlaybackEnabled || _isLoopRestartInFlight)
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

        private void UpdateLoopPlaybackButtonContent()
        {
            LoopPlaybackButton.Content = _loopPlaybackEnabled
                ? "Loop Playback: On"
                : "Loop Playback: Off";
        }

        private string BuildLoopPlaybackStatusMessage()
        {
            if (!_loopPlaybackEnabled)
            {
                return "Loop playback disabled.";
            }

            var loopRange = ResolveFocusedLoopRange();
            if (loopRange == null || !loopRange.HasAnyMarkers)
            {
                return "Loop playback enabled for the full media range.";
            }

            if (loopRange.IsInvalidRange)
            {
                return "Loop playback enabled, but the current A/B range is invalid.";
            }

            return loopRange.HasPendingMarkers
                ? "Loop playback enabled, but the current A/B range is still pending exact frame identity."
                : "Loop playback enabled for the current A/B range.";
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
    }
}
