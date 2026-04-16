using System;
using System.Globalization;
using System.IO;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
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

        private readonly ClipExportService _clipExportService;
        private readonly ReviewWorkspaceHostController _hostController;
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

        public MainWindow()
        {
            InitializeComponent();

            _preferencesService = new AppPreferencesService();
            _clipExportService = new ClipExportService();
            _optionsProvider = new FfmpegReviewEngineOptionsProvider(_preferencesService);
            _videoReviewEngineFactory = new VideoReviewEngineFactory(_optionsProvider, SdlAudioOutputFactory.Instance);
            _videoReviewEngine = _videoReviewEngineFactory.Create("pane-primary");
            _sessionCoordinator = new ReviewSessionCoordinator(_videoReviewEngine);
            _workspaceCoordinator = new ReviewWorkspaceCoordinator(_videoReviewEngine, _sessionCoordinator);
            _hostController = new ReviewWorkspaceHostController(
                _workspaceCoordinator,
                new ReviewHostCapabilities(
                    supportsTimedPlayback: true,
                    hasBundledRuntime: true,
                    exportToolingAvailable: _clipExportService.IsBundledToolingAvailable,
                    idleStatusText: "Avalonia preview host ready.",
                    runtimeMissingStatusText: "Bundled playback runtime is missing.",
                    timedPlaybackCapabilityText: "Timed playback is unavailable in this host.",
                    exportToolingStatusText: _clipExportService.GetToolAvailabilityMessage()));

            _videoReviewEngine.FramePresented += VideoReviewEngine_FramePresented;
            _hostController.ViewStateChanged += HostController_ViewStateChanged;

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

            await _hostController.OpenAsync(filePath);
            _hostController.Refresh();
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
            LoopPlaybackButton.Content = _loopPlaybackEnabled
                ? "Loop Playback: On"
                : "Loop Playback: Off";
        }

        private async void ExportClipButton_Click(object sender, RoutedEventArgs e)
        {
            var seedRequest = BuildSeedClipExportRequest();
            if (seedRequest == null)
            {
                return;
            }

            var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
            {
                Title = "Save Loop As Clip",
                SuggestedFileName = Path.GetFileNameWithoutExtension(seedRequest.SourceFilePath) + "-clip.mp4",
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

            var request = new ClipExportRequest(
                seedRequest.SourceFilePath,
                outputPath,
                seedRequest.DisplayLabel,
                seedRequest.PaneId,
                seedRequest.IsPaneLocal,
                seedRequest.SessionSnapshot,
                seedRequest.LoopRange,
                seedRequest.IndexedFrameTimeResolver);
            await _clipExportService.ExportAsync(request);
            _hostController.Refresh();
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
                !_isLoopRestartInFlight &&
                _hostController.CurrentViewState.Transport.IsPlaying)
            {
                var loopRange = ResolveFocusedLoopRange();
                if (loopRange != null &&
                    loopRange.HasLoopIn &&
                    loopRange.HasLoopOut &&
                    !loopRange.IsInvalidRange &&
                    _workspaceCoordinator.CurrentSession.Position.PresentationTime >= loopRange.EffectiveEndTime + LoopGuardTolerance)
                {
                    _isLoopRestartInFlight = true;
                    try
                    {
                        await _hostController.SeekToTimeAsync(loopRange.EffectiveStartTime);
                        await _hostController.PlayAsync();
                    }
                    finally
                    {
                        _isLoopRestartInFlight = false;
                    }
                }
            }

            RefreshUi(_hostController.Refresh());
        }

        private void RefreshUi(ReviewWorkspaceViewState viewState)
        {
            var transport = viewState != null ? viewState.Transport : TransportCommandState.Disabled;
            PlayPauseButton.IsEnabled = transport.CanTogglePlayPause;
            PreviousFrameButton.IsEnabled = transport.CanStepBackward;
            NextFrameButton.IsEnabled = transport.CanStepForward;
            SetLoopAButton.IsEnabled = viewState != null && viewState.Loop.CanSetMarkers;
            SetLoopBButton.IsEnabled = viewState != null && viewState.Loop.CanSetMarkers;
            ClearLoopButton.IsEnabled = viewState != null && viewState.Loop.CanClearMarkers;
            ExportClipButton.IsEnabled = viewState != null && viewState.Export.CanExportCurrentLoop;
            PlayPauseButton.Content = transport.IsPlaying ? "Pause" : "Play";

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
        }

        private ClipExportRequest BuildSeedClipExportRequest()
        {
            var session = _workspaceCoordinator.CurrentSession ?? ReviewSessionSnapshot.Empty;
            var loopRange = ResolveFocusedLoopRange();
            if (!session.IsMediaOpen ||
                loopRange == null ||
                !loopRange.HasLoopIn ||
                !loopRange.HasLoopOut ||
                loopRange.HasPendingMarkers ||
                loopRange.IsInvalidRange)
            {
                return null;
            }

            var snapshot = _workspaceCoordinator.GetWorkspaceSnapshot();
            return new ClipExportRequest(
                session.CurrentFilePath,
                string.Empty,
                session.DisplayLabel,
                snapshot.FocusedPaneId,
                false,
                session,
                loopRange,
                _videoReviewEngine as FfmpegReviewEngine);
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
    }
}
