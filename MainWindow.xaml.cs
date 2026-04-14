using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using System.Windows.Interop;
using FramePlayer.Core.Abstractions;
using FramePlayer.Core.Coordination;
using FramePlayer.Core.Events;
using FramePlayer.Core.Models;
using FramePlayer.Controls;
using FramePlayer.Engines.FFmpeg;
using FramePlayer.Services;
using Microsoft.Win32;

namespace FramePlayer
{
    public partial class MainWindow : Window
    {
        private const double DefaultFramesPerSecond = 30.0;
        private const string PrimaryPaneId = "pane-primary";
        private const string ComparePaneId = "pane-compare-a";
        private const string CompareSessionId = "compare-a";
        private const string DefaultCompareAlignmentStatus = "Last align: none";
        private const double CompareModePreferredMinWindowWidth = 1180d;
        private const int ControlModifiedFrameStep = 10;
        private const int ShiftModifiedFrameStep = 100;
        private const int DefaultWindowCornerRadius = 14;
        private const int DwmaWindowCornerPreference = 33;
        private static readonly TimeSpan SeekJump = TimeSpan.FromSeconds(5);
        private static readonly TimeSpan FrameStepInitialDelay = TimeSpan.FromMilliseconds(550);
        private static readonly TimeSpan FrameStepRepeatInterval = TimeSpan.FromMilliseconds(60);
        private static readonly TimeSpan SliderScrubThrottleInterval = TimeSpan.FromMilliseconds(75);
        private static readonly TimeSpan LoopPlaybackMinimumEndTolerance = TimeSpan.FromMilliseconds(100);

        private readonly DispatcherTimer _positionTimer;
        private readonly DispatcherTimer _frameStepRepeatTimer;
        private readonly DispatcherTimer _sliderScrubTimer;
        private readonly DispatcherTimer _paneSliderScrubTimer;
        private readonly BuildVariantInfo _buildVariant;
        private readonly AppPreferencesService _appPreferencesService;
        private readonly ClipExportService _clipExportService;
        private readonly DiagnosticLogService _diagnosticLogService;
        private readonly FfmpegReviewEngineOptionsProvider _ffmpegReviewEngineOptionsProvider;
        private readonly RecentFilesService _recentFilesService;
        private readonly ReviewSessionCoordinator _sessionCoordinator;
        private readonly VideoReviewEngineFactory _videoReviewEngineFactory;
        private readonly ReviewWorkspaceCoordinator _workspaceCoordinator;
        private readonly IVideoReviewEngine _videoReviewEngine;
        private IVideoReviewEngine _compareVideoReviewEngine;
        private ReviewSessionCoordinator _compareSessionCoordinator;

        private WindowState _restoreWindowState;
        private WindowStyle _restoreWindowStyle;
        private ResizeMode _restoreResizeMode;
        private bool _restoreTopmost;
        private bool _isMediaLoaded;
        private bool _isFullScreen;
        private bool _isPlaying;
        private bool _isFrameStepInProgress;
        private int _heldFrameStepDirection;
        private bool _isSliderDragActive;
        private bool _isSliderScrubSeekInFlight;
        private bool _hasPendingSliderScrubTarget;
        private bool _isPaneSliderDragActive;
        private bool _isPaneSliderScrubSeekInFlight;
        private bool _hasPendingPaneSliderScrubTarget;
        private bool _suppressSliderUpdate;
        private bool _suppressPaneSliderUpdate;
        private bool _suppressLoopRestart;
        private bool _isLoopRestartInFlight;
        private double _framesPerSecond;
        private TimeSpan _pendingSliderScrubTarget;
        private TimeSpan _pendingPaneSliderScrubTarget;
        private TimeSpan _positionStep;
        private TimeSpan _mediaDuration;
        private string _currentFilePath;
        private string _activePaneSliderPaneId;
        private string _activeLoopCommandPaneId;
        private string _activeLoopPlaybackPaneId;
        private string _lastMediaErrorMessage;
        private bool _isCacheStatusActive;
        private string _lastCompareAlignmentStatus = DefaultCompareAlignmentStatus;
        private Task _activeSliderScrubSeekTask = Task.CompletedTask;
        private Task _activePaneSliderScrubSeekTask = Task.CompletedTask;
        private SynchronizedOperationScope _lastPlaybackScope = SynchronizedOperationScope.FocusedPane;

        private sealed class LoopRangeEvaluation
        {
            public LoopRangeEvaluation(
                ReviewWorkspaceSnapshot workspaceSnapshot,
                SynchronizedOperationScope operationScope,
                LoopPlaybackRangeSnapshot sharedRange,
                ReviewWorkspacePaneSnapshot[] targetPanes,
                IReadOnlyList<LoopPlaybackPaneRangeSnapshot> targetPaneRanges,
                LoopPlaybackPaneRangeSnapshot focusedPaneRange,
                bool missingTargetPaneRanges,
                bool hasPendingMarkers,
                bool isInvalidRange)
            {
                WorkspaceSnapshot = workspaceSnapshot ?? ReviewWorkspaceSnapshot.Empty;
                OperationScope = operationScope;
                SharedRange = sharedRange ?? LoopPlaybackRangeSnapshot.Empty;
                TargetPanes = targetPanes ?? Array.Empty<ReviewWorkspacePaneSnapshot>();
                TargetPaneRanges = targetPaneRanges ?? Array.Empty<LoopPlaybackPaneRangeSnapshot>();
                FocusedPaneRange = focusedPaneRange;
                MissingTargetPaneRanges = missingTargetPaneRanges;
                HasPendingMarkers = hasPendingMarkers;
                IsInvalidRange = isInvalidRange;
            }

            public ReviewWorkspaceSnapshot WorkspaceSnapshot { get; }

            public SynchronizedOperationScope OperationScope { get; }

            public LoopPlaybackRangeSnapshot SharedRange { get; }

            public ReviewWorkspacePaneSnapshot[] TargetPanes { get; }

            public IReadOnlyList<LoopPlaybackPaneRangeSnapshot> TargetPaneRanges { get; }

            public LoopPlaybackPaneRangeSnapshot FocusedPaneRange { get; }

            public bool MissingTargetPaneRanges { get; }

            public bool HasPendingMarkers { get; }

            public bool IsInvalidRange { get; }

            public bool HasMarkers
            {
                get { return SharedRange.HasMarkers; }
            }

            public bool CanLoopExactly
            {
                get
                {
                    return HasMarkers &&
                           !MissingTargetPaneRanges &&
                           !HasPendingMarkers &&
                           !IsInvalidRange &&
                           TargetPanes.Length > 0;
                }
            }
        }

        private sealed class ClipExportTarget
        {
            public ClipExportTarget(
                string paneId,
                string contextLabel,
                bool isPaneLocal,
                ReviewWorkspacePaneSnapshot paneSnapshot,
                LoopPlaybackPaneRangeSnapshot loopRange,
                FfmpegReviewEngine engine)
            {
                PaneId = paneId ?? string.Empty;
                ContextLabel = contextLabel ?? string.Empty;
                IsPaneLocal = isPaneLocal;
                PaneSnapshot = paneSnapshot;
                LoopRange = loopRange;
                Engine = engine;
            }

            public string PaneId { get; }

            public string ContextLabel { get; }

            public bool IsPaneLocal { get; }

            public ReviewWorkspacePaneSnapshot PaneSnapshot { get; }

            public LoopPlaybackPaneRangeSnapshot LoopRange { get; }

            public FfmpegReviewEngine Engine { get; }
        }

        public MainWindow()
        {
            InitializeComponent();

            PositionSlider.AddHandler(
                PreviewMouseLeftButtonDownEvent,
                new MouseButtonEventHandler(PositionSlider_PreviewMouseLeftButtonDown),
                true);
            PositionSlider.AddHandler(
                PreviewMouseLeftButtonUpEvent,
                new MouseButtonEventHandler(PositionSlider_PreviewMouseLeftButtonUp),
                true);
            PrimaryPanePositionSlider.AddHandler(
                PreviewMouseLeftButtonDownEvent,
                new MouseButtonEventHandler(PanePositionSlider_PreviewMouseLeftButtonDown),
                true);
            PrimaryPanePositionSlider.AddHandler(
                PreviewMouseLeftButtonUpEvent,
                new MouseButtonEventHandler(PanePositionSlider_PreviewMouseLeftButtonUp),
                true);
            ComparePanePositionSlider.AddHandler(
                PreviewMouseLeftButtonDownEvent,
                new MouseButtonEventHandler(PanePositionSlider_PreviewMouseLeftButtonDown),
                true);
            ComparePanePositionSlider.AddHandler(
                PreviewMouseLeftButtonUpEvent,
                new MouseButtonEventHandler(PanePositionSlider_PreviewMouseLeftButtonUp),
                true);

            _buildVariant = BuildVariantInfo.Current;
            CustomVideoSurfaceHost.Visibility = Visibility.Visible;

            _appPreferencesService = new AppPreferencesService();
            _diagnosticLogService = new DiagnosticLogService();
            _clipExportService = new ClipExportService();
            _ffmpegReviewEngineOptionsProvider = new FfmpegReviewEngineOptionsProvider(_appPreferencesService);
            _recentFilesService = new RecentFilesService();
            _videoReviewEngineFactory = new VideoReviewEngineFactory(_ffmpegReviewEngineOptionsProvider);
            UseGpuAccelerationMenuItem.IsChecked = _ffmpegReviewEngineOptionsProvider.UseGpuAcceleration;
            _videoReviewEngine = CreateVideoReviewEngine(PrimaryPaneId);
            _sessionCoordinator = new ReviewSessionCoordinator(_videoReviewEngine);
            _workspaceCoordinator = new ReviewWorkspaceCoordinator(_videoReviewEngine, _sessionCoordinator);
            _workspaceCoordinator.WorkspaceChanged += ReviewWorkspaceCoordinator_WorkspaceChanged;
            _workspaceCoordinator.PreparationStateChanged += ReviewWorkspaceCoordinator_PreparationStateChanged;
            _videoReviewEngine.FramePresented += VideoReviewEngine_FramePresented;
            _lastMediaErrorMessage = string.Empty;
            ApplySessionSnapshot(_workspaceCoordinator.CurrentSession);

            _positionTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(100)
            };
            _positionTimer.Tick += PositionTimer_Tick;
            _positionTimer.Start();

            _frameStepRepeatTimer = new DispatcherTimer
            {
                Interval = FrameStepInitialDelay
            };
            _frameStepRepeatTimer.Tick += FrameStepRepeatTimer_Tick;

            _sliderScrubTimer = new DispatcherTimer
            {
                Interval = SliderScrubThrottleInterval
            };
            _sliderScrubTimer.Tick += SliderScrubTimer_Tick;

            _paneSliderScrubTimer = new DispatcherTimer
            {
                Interval = SliderScrubThrottleInterval
            };
            _paneSliderScrubTimer.Tick += PaneSliderScrubTimer_Tick;

            RefreshRecentFilesMenu();
            UpdateCompareModeVisualState();
            UpdateHeader();
            UpdatePositionDisplay(TimeSpan.Zero);
            UpdateFullScreenVisualState();
            UpdateWindowStateButtonVisuals();
            UpdateTransportState();
            UpdateWorkspacePanePresentation();
            SourceInitialized += MainWindow_SourceInitialized;

            LogInfo(string.Format(
                CultureInfo.InvariantCulture,
                "Session started. Build {0}. Forced backend: {1}. Version {2}. Runtime available: {3}. Runtime source: {4}.",
                _buildVariant.BuildDisplayName,
                _buildVariant.ForcedBackend,
                GetApplicationVersion(),
                App.HasBundledFfmpegRuntime ? "Yes" : "No",
                App.HasBundledFfmpegRuntime
                    ? "Bundled runtime verified."
                    : GetRuntimeStatusMessage()));
        }

        private IVideoReviewEngine CreateVideoReviewEngine(string paneId)
        {
            return _videoReviewEngineFactory.Create(paneId);
        }

        private void FocusPreferredVideoSurface()
        {
            var snapshot = _workspaceCoordinator.GetWorkspaceSnapshot();
            if (IsCompareModeEnabled && string.Equals(snapshot.FocusedPaneId, ComparePaneId, StringComparison.Ordinal))
            {
                Keyboard.Focus(CompareVideoSurfaceHost);
                return;
            }

            Keyboard.Focus(CustomVideoSurfaceHost);
        }

        private void TitleBarDragRegion_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (_isFullScreen || e.ChangedButton != MouseButton.Left)
            {
                return;
            }

            if (e.ClickCount == 2)
            {
                ToggleWindowState();
                e.Handled = true;
                return;
            }

            try
            {
                DragMove();
                e.Handled = true;
            }
            catch (InvalidOperationException)
            {
            }
        }

        private void MinimizeWindowButton_Click(object sender, RoutedEventArgs e)
        {
            WindowState = WindowState.Minimized;
        }

        private void WindowStateButton_Click(object sender, RoutedEventArgs e)
        {
            ToggleWindowState();
        }

        private void CloseWindowButton_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }

        private void Window_StateChanged(object sender, EventArgs e)
        {
            UpdateWindowStateButtonVisuals();
            UpdateWindowFrameChrome();
        }

        private void ToggleWindowState()
        {
            if (_isFullScreen || ResizeMode == ResizeMode.NoResize)
            {
                return;
            }

            WindowState = WindowState == WindowState.Maximized
                ? WindowState.Normal
                : WindowState.Maximized;
            UpdateWindowStateButtonVisuals();
        }

        private void UpdateWindowStateButtonVisuals()
        {
            if (WindowStateGlyphTextBlock == null || WindowStateButton == null)
            {
                return;
            }

            var isMaximized = WindowState == WindowState.Maximized && !_isFullScreen;
            WindowStateGlyphTextBlock.Text = isMaximized ? "\uE923" : "\uE922";
            WindowStateButton.ToolTip = isMaximized ? "Restore" : "Maximize";
        }

        private void MainWindow_SourceInitialized(object sender, EventArgs e)
        {
            UpdateWindowFrameChrome();
        }

        private void UpdateWindowFrameChrome()
        {
            var useRoundedCorners = !_isFullScreen && WindowState != WindowState.Maximized;
            var handle = new WindowInteropHelper(this).Handle;
            if (handle == IntPtr.Zero)
            {
                return;
            }

            var preference = useRoundedCorners
                ? DwmWindowCornerPreference.Round
                : DwmWindowCornerPreference.DoNotRound;
            try
            {
                DwmSetWindowAttribute(
                    handle,
                    DwmaWindowCornerPreference,
                    ref preference,
                    sizeof(int));
            }
            catch (DllNotFoundException)
            {
            }
            catch (EntryPointNotFoundException)
            {
            }
        }

        private static string GetSupportedVideoExtensionsDescription()
        {
            return "AVI, MOV, M4V, MP4, MKV, WMV";
        }

        private static string GetOpenFileFilter()
        {
            return "Supported Video Files|*.avi;*.mov;*.m4v;*.mp4;*.mkv;*.wmv|AVI Files|*.avi|MOV Files|*.mov|M4V Files|*.m4v|MP4 Files|*.mp4|MKV Files|*.mkv|WMV Files|*.wmv|All Files|*.*";
        }

        private bool IsCompareModeEnabled
        {
            get { return CompareModeCheckBox != null && CompareModeCheckBox.IsChecked == true; }
        }

        private bool IsAllPaneTransportEnabled
        {
            get
            {
                if (!IsCompareModeEnabled)
                {
                    return false;
                }

                var snapshot = _workspaceCoordinator.GetWorkspaceSnapshot();
                if (snapshot == null)
                {
                    return false;
                }

                ReviewWorkspacePaneSnapshot primaryPaneSnapshot;
                ReviewWorkspacePaneSnapshot comparePaneSnapshot;
                return snapshot.TryGetPane(PrimaryPaneId, out primaryPaneSnapshot) &&
                    PaneHasLoadedMedia(primaryPaneSnapshot) &&
                    snapshot.TryGetPane(ComparePaneId, out comparePaneSnapshot) &&
                    PaneHasLoadedMedia(comparePaneSnapshot);
            }
        }

        private bool IsLoopPlaybackEnabled
        {
            get { return LoopPlaybackMenuItem != null && LoopPlaybackMenuItem.IsChecked; }
        }

        private SynchronizedOperationScope GetRequestedOperationScope()
        {
            if (!IsAllPaneTransportEnabled)
            {
                return SynchronizedOperationScope.FocusedPane;
            }

            var snapshot = _workspaceCoordinator.GetWorkspaceSnapshot();
            var loadedPaneCount = snapshot.Panes.Count(PaneHasLoadedMedia);
            return loadedPaneCount > 1
                ? SynchronizedOperationScope.AllPanes
                : SynchronizedOperationScope.FocusedPane;
        }

        private static string GetPaneIdFromSender(object sender)
        {
            return (sender as FrameworkElement)?.Tag as string;
        }

        private IVideoReviewEngine GetEngineForPane(string paneId)
        {
            if (string.Equals(paneId, ComparePaneId, StringComparison.Ordinal) && _compareVideoReviewEngine != null)
            {
                return _compareVideoReviewEngine;
            }

            return _videoReviewEngine;
        }

        private IVideoReviewEngine GetFocusedPaneEngine()
        {
            var snapshot = _workspaceCoordinator.GetWorkspaceSnapshot();
            return GetEngineForPane(
                string.IsNullOrWhiteSpace(snapshot.FocusedPaneId)
                    ? PrimaryPaneId
                    : snapshot.FocusedPaneId);
        }

        private FfmpegReviewEngine GetFocusedFfmpegEngine()
        {
            return GetFocusedPaneEngine() as FfmpegReviewEngine;
        }

        private string GetFocusedPaneId()
        {
            var snapshot = _workspaceCoordinator.GetWorkspaceSnapshot();
            return string.IsNullOrWhiteSpace(snapshot.FocusedPaneId)
                ? PrimaryPaneId
                : snapshot.FocusedPaneId;
        }

        private void SetSharedLoopCommandContext()
        {
            _activeLoopCommandPaneId = null;
        }

        private void SetPaneLoopCommandContext(string paneId)
        {
            if (string.IsNullOrWhiteSpace(paneId) ||
                !IsCompareModeEnabled ||
                !string.Equals(paneId, ComparePaneId, StringComparison.Ordinal) &&
                !string.Equals(paneId, PrimaryPaneId, StringComparison.Ordinal))
            {
                _activeLoopCommandPaneId = null;
                return;
            }

            _activeLoopCommandPaneId = paneId;
        }

        private string GetLoopCommandContextPaneId(ReviewWorkspaceSnapshot workspaceSnapshot = null)
        {
            if (!IsCompareModeEnabled || string.IsNullOrWhiteSpace(_activeLoopCommandPaneId))
            {
                return null;
            }

            var snapshot = workspaceSnapshot ?? _workspaceCoordinator.GetWorkspaceSnapshot();
            ReviewWorkspacePaneSnapshot paneSnapshot;
            return snapshot != null &&
                   snapshot.TryGetPane(_activeLoopCommandPaneId, out paneSnapshot) &&
                   PaneHasLoadedMedia(paneSnapshot)
                ? _activeLoopCommandPaneId
                : null;
        }

        private string GetLoopCommandContextLabel(ReviewWorkspaceSnapshot workspaceSnapshot = null)
        {
            var paneId = GetLoopCommandContextPaneId(workspaceSnapshot);
            if (string.IsNullOrWhiteSpace(paneId))
            {
                return "main transport";
            }

            return string.Equals(paneId, ComparePaneId, StringComparison.Ordinal)
                ? "Compare pane"
                : "Primary pane";
        }

        private string GetActiveLoopPlaybackPaneId(ReviewWorkspaceSnapshot workspaceSnapshot = null)
        {
            if (!IsCompareModeEnabled || string.IsNullOrWhiteSpace(_activeLoopPlaybackPaneId))
            {
                return null;
            }

            var snapshot = workspaceSnapshot ?? _workspaceCoordinator.GetWorkspaceSnapshot();
            ReviewWorkspacePaneSnapshot paneSnapshot;
            return snapshot != null &&
                   snapshot.TryGetPane(_activeLoopPlaybackPaneId, out paneSnapshot) &&
                   PaneHasLoadedMedia(paneSnapshot)
                ? _activeLoopPlaybackPaneId
                : null;
        }

        private static bool PaneHasLoadedMedia(ReviewWorkspacePaneSnapshot paneSnapshot)
        {
            return paneSnapshot != null &&
                   paneSnapshot.PlaybackState != ReviewPlaybackState.Closed &&
                   !string.IsNullOrWhiteSpace(paneSnapshot.CurrentFilePath);
        }

        private static bool CanRunCompareActions(
            ReviewWorkspacePaneSnapshot primaryPaneSnapshot,
            ReviewWorkspacePaneSnapshot comparePaneSnapshot)
        {
            return PaneHasLoadedMedia(primaryPaneSnapshot) &&
                   PaneHasLoadedMedia(comparePaneSnapshot);
        }

        private void ResetCompareAlignmentStatus()
        {
            _lastCompareAlignmentStatus = DefaultCompareAlignmentStatus;
        }

        private void EnsureComparePaneInitialized()
        {
            if (_compareSessionCoordinator != null && _compareVideoReviewEngine != null)
            {
                return;
            }

            _compareVideoReviewEngine = CreateVideoReviewEngine(ComparePaneId);
            _compareSessionCoordinator = new ReviewSessionCoordinator(
                _compareVideoReviewEngine,
                CompareSessionId,
                "Compare A");
            _compareVideoReviewEngine.FramePresented += VideoReviewEngine_FramePresented;
            _workspaceCoordinator.TryBindPane(
                ComparePaneId,
                _compareSessionCoordinator,
                displayLabel: "Compare A");
        }

        private void UpdateCompareModeVisualState()
        {
            var compareVisible = IsCompareModeEnabled;
            ComparePaneColumn.Width = compareVisible
                ? new GridLength(1d, GridUnitType.Star)
                : new GridLength(0d);
            ComparePaneBorder.Visibility = compareVisible
                ? Visibility.Visible
                : Visibility.Collapsed;
            CompareToolbarBorder.Visibility = compareVisible
                ? Visibility.Visible
                : Visibility.Collapsed;
            PrimaryPaneHeaderBorder.Visibility = compareVisible
                ? Visibility.Visible
                : Visibility.Collapsed;
            ComparePaneHeaderBorder.Visibility = compareVisible
                ? Visibility.Visible
                : Visibility.Collapsed;
            PrimaryPaneFooterBorder.Visibility = compareVisible
                ? Visibility.Visible
                : Visibility.Collapsed;
            ComparePaneFooterBorder.Visibility = compareVisible
                ? Visibility.Visible
                : Visibility.Collapsed;
            PrimaryPaneControlsPanel.Visibility = compareVisible
                ? Visibility.Visible
                : Visibility.Collapsed;
            ComparePaneControlsPanel.Visibility = compareVisible
                ? Visibility.Visible
                : Visibility.Collapsed;
            AllPanesCheckBox.Visibility = Visibility.Collapsed;
            if (!compareVisible)
            {
                CompareStatusTextBlock.Text = string.Empty;
                CompareStatusTextBlock.ToolTip = null;
            }
        }

        private void UpdateWorkspacePanePresentation()
        {
            var snapshot = _workspaceCoordinator.GetWorkspaceSnapshot();
            UpdatePanePresentation(
                snapshot,
                PrimaryPaneId,
                PrimaryPaneBorder,
                PrimaryPaneHeaderBorder,
                PrimaryPaneFooterBorder,
                PrimaryPaneTitleTextBlock,
                PrimaryPaneStateTextBlock,
                PrimaryPaneFileTextBlock,
                PrimaryEmptyStateOverlay,
                PrimaryEmptyStateTitleTextBlock,
                PrimaryEmptyStateBodyTextBlock);
            UpdatePanePresentation(
                snapshot,
                ComparePaneId,
                ComparePaneBorder,
                ComparePaneHeaderBorder,
                ComparePaneFooterBorder,
                ComparePaneTitleTextBlock,
                ComparePaneStateTextBlock,
                ComparePaneFileTextBlock,
                CompareEmptyStateOverlay,
                CompareEmptyStateTitleTextBlock,
                CompareEmptyStateBodyTextBlock);
            UpdateCompareControlState(snapshot);
            UpdateLoopUi();
        }

        private void UpdateCompareControlState(ReviewWorkspaceSnapshot workspaceSnapshot)
        {
            ReviewWorkspacePaneSnapshot primaryPaneSnapshot = null;
            var hasPrimaryPane = workspaceSnapshot != null &&
                                 workspaceSnapshot.TryGetPane(PrimaryPaneId, out primaryPaneSnapshot);
            ReviewWorkspacePaneSnapshot comparePaneSnapshot = null;
            var hasComparePane = workspaceSnapshot != null &&
                                 workspaceSnapshot.TryGetPane(ComparePaneId, out comparePaneSnapshot);
            var canRunCompareActions = IsCompareModeEnabled &&
                                       hasPrimaryPane &&
                                       hasComparePane &&
                                       CanRunCompareActions(primaryPaneSnapshot, comparePaneSnapshot);

            var canAlign = canRunCompareActions;
            AlignRightToLeftButton.IsEnabled = canAlign;
            AlignLeftToRightButton.IsEnabled = canAlign;
            AlignRightToLeftButton.ToolTip = canAlign
                ? "Frame first: aligns the right pane to the left pane with exact frame identity when available."
                : "Load videos in both panes before aligning them.";
            AlignLeftToRightButton.ToolTip = canAlign
                ? "Frame first: aligns the left pane to the right pane with exact frame identity when available."
                : "Load videos in both panes before aligning them.";

            AllPanesCheckBox.IsEnabled = false;
            AllPanesCheckBox.ToolTip = canRunCompareActions
                ? "The main playback, seek, and frame-step controls now apply to both loaded panes."
                : "Load videos in both panes to use shared transport controls.";

            UpdatePaneControlState(
                PrimaryPanePlayButton,
                PrimaryPanePauseButton,
                PrimaryPaneStepBackButton,
                PrimaryPaneStepForwardButton,
                hasPrimaryPane ? primaryPaneSnapshot : null);
            UpdatePaneControlState(
                ComparePanePlayButton,
                ComparePanePauseButton,
                ComparePaneStepBackButton,
                ComparePaneStepForwardButton,
                hasComparePane ? comparePaneSnapshot : null);

            if (!canRunCompareActions)
            {
                ResetCompareAlignmentStatus();
            }

            var compareStatusText = BuildCompareStatusText(
                hasPrimaryPane ? primaryPaneSnapshot : null,
                hasComparePane ? comparePaneSnapshot : null);
            CompareStatusTextBlock.Text = compareStatusText;
            CompareStatusTextBlock.ToolTip = compareStatusText;
        }

        private void UpdatePaneControlState(
            Button playButton,
            Button pauseButton,
            Button stepBackButton,
            Button stepForwardButton,
            ReviewWorkspacePaneSnapshot paneSnapshot)
        {
            var canControl = PaneHasLoadedMedia(paneSnapshot);
            var canPlay = canControl && _buildVariant.SupportsTimedPlayback;
            playButton.IsEnabled = canPlay;
            pauseButton.IsEnabled = canPlay;
            stepBackButton.IsEnabled = canControl;
            stepForwardButton.IsEnabled = canControl;
        }

        private void UpdatePaneNavigationSurface(string paneId, ReviewWorkspacePaneSnapshot paneSnapshot)
        {
            TextBlock currentPositionTextBlock;
            TextBlock durationTextBlock;
            Slider positionSlider;
            TextBox frameNumberTextBox;
            if (!TryGetPaneNavigationControls(
                paneId,
                out currentPositionTextBlock,
                out durationTextBlock,
                out positionSlider,
                out frameNumberTextBox))
            {
                return;
            }

            var paneHasLoadedMedia = PaneHasLoadedMedia(paneSnapshot);
            var currentPosition = paneSnapshot != null ? paneSnapshot.PresentationTime : TimeSpan.Zero;
            var mediaDuration = GetPaneMediaDuration(paneId);
            currentPositionTextBlock.Text = FormatTime(currentPosition);
            durationTextBlock.Text = FormatTime(mediaDuration);
            positionSlider.IsEnabled = paneHasLoadedMedia;
            frameNumberTextBox.IsEnabled = paneHasLoadedMedia;

            _suppressPaneSliderUpdate = true;
            try
            {
                positionSlider.Maximum = mediaDuration > TimeSpan.Zero ? mediaDuration.TotalSeconds : 1.0;
                if (!(_isPaneSliderDragActive && string.Equals(_activePaneSliderPaneId, paneId, StringComparison.Ordinal)))
                {
                    positionSlider.Value = Math.Min(currentPosition.TotalSeconds, positionSlider.Maximum);
                }
            }
            finally
            {
                _suppressPaneSliderUpdate = false;
            }

            long currentFrameIndex = 0L;
            bool isAbsoluteFrameIndex = false;
            var hasFrameIdentity = TryGetCurrentEngineFrameIndex(paneId, out currentFrameIndex, out isAbsoluteFrameIndex);
            if (!frameNumberTextBox.IsKeyboardFocusWithin)
            {
                frameNumberTextBox.Text = hasFrameIdentity && isAbsoluteFrameIndex
                    ? GetDisplayedFrameNumber(currentFrameIndex).ToString(CultureInfo.InvariantCulture)
                    : string.Empty;
            }

            frameNumberTextBox.ToolTip = BuildPaneFrameNumberInputToolTip(
                paneId,
                paneHasLoadedMedia,
                hasFrameIdentity,
                isAbsoluteFrameIndex,
                currentFrameIndex);
        }

        private string BuildPaneFrameNumberInputToolTip(
            string paneId,
            bool paneHasLoadedMedia,
            bool hasFrameIdentity,
            bool isAbsoluteFrameIndex,
            long currentFrameIndex)
        {
            if (!paneHasLoadedMedia)
            {
                return "Load media in this pane, then type a frame number and press Enter.";
            }

            if (hasFrameIdentity && !isAbsoluteFrameIndex)
            {
                var pendingFrameMessage = _buildVariant.UsesZeroIndexedFrameDisplay
                    ? "The current frame number is pending while the background index finishes. The visible frame is correct, but the absolute zero-indexed frame label is not ready yet."
                    : "The current frame number is pending while the background index finishes. The visible frame is correct, but the absolute frame label is not ready yet.";
                var pendingMaxFrameIndex = GetMaxFrameIndex(paneId);
                if (pendingMaxFrameIndex >= 0)
                {
                    var lastFrameNumber = GetDisplayedTotalFrameValue(pendingMaxFrameIndex + 1L);
                    return string.Format(
                        CultureInfo.InvariantCulture,
                        "{0} Last frame: {1}. Type a frame number and press Enter once the index is ready.",
                        pendingFrameMessage,
                        lastFrameNumber);
                }

                return pendingFrameMessage + " You can still type a frame number and press Enter.";
            }

            var maxFrameIndex = GetMaxFrameIndex(paneId);
            if (maxFrameIndex >= 0)
            {
                var lastFrameNumber = GetDisplayedTotalFrameValue(maxFrameIndex + 1L);
                return _buildVariant.UsesZeroIndexedFrameDisplay
                    ? string.Format(
                        CultureInfo.InvariantCulture,
                        "Type a zero-indexed frame number for this pane and press Enter. Last frame: {0}.",
                        lastFrameNumber)
                    : string.Format(
                        CultureInfo.InvariantCulture,
                        "Type a frame number for this pane and press Enter. Last frame: {0}.",
                        lastFrameNumber);
            }

            return "Type a frame number for this pane and press Enter.";
        }

        private bool TryGetPaneNavigationControls(
            string paneId,
            out TextBlock currentPositionTextBlock,
            out TextBlock durationTextBlock,
            out Slider positionSlider,
            out TextBox frameNumberTextBox)
        {
            if (string.Equals(paneId, ComparePaneId, StringComparison.Ordinal))
            {
                currentPositionTextBlock = ComparePaneCurrentPositionTextBlock;
                durationTextBlock = ComparePaneDurationTextBlock;
                positionSlider = ComparePanePositionSlider;
                frameNumberTextBox = ComparePaneFrameNumberTextBox;
                return currentPositionTextBlock != null &&
                       durationTextBlock != null &&
                       positionSlider != null &&
                       frameNumberTextBox != null;
            }

            currentPositionTextBlock = PrimaryPaneCurrentPositionTextBlock;
            durationTextBlock = PrimaryPaneDurationTextBlock;
            positionSlider = PrimaryPanePositionSlider;
            frameNumberTextBox = PrimaryPaneFrameNumberTextBox;
            return currentPositionTextBlock != null &&
                   durationTextBlock != null &&
                   positionSlider != null &&
                   frameNumberTextBox != null;
        }

        private TimeSpan GetPaneMediaDuration(string paneId)
        {
            var engine = GetEngineForPane(paneId);
            return engine != null && engine.IsMediaOpen
                ? engine.MediaInfo.Duration
                : TimeSpan.Zero;
        }

        private TimeSpan GetPanePositionStep(string paneId)
        {
            var engine = GetEngineForPane(paneId);
            return engine != null && engine.IsMediaOpen
                ? engine.MediaInfo.PositionStep
                : TimeSpan.Zero;
        }

        private TimeSpan GetPaneDisplayPosition(string paneId)
        {
            var engine = GetEngineForPane(paneId);
            return engine != null && engine.IsMediaOpen && engine.Position != null
                ? engine.Position.PresentationTime
                : TimeSpan.Zero;
        }

        private bool TryGetCurrentEngineFrameIndex(string paneId, out long frameIndex, out bool isAbsoluteFrameIndex)
        {
            frameIndex = 0L;
            isAbsoluteFrameIndex = false;

            var engine = GetEngineForPane(paneId);
            var enginePosition = engine != null ? engine.Position : null;
            if (enginePosition == null || !enginePosition.FrameIndex.HasValue)
            {
                return false;
            }

            frameIndex = Math.Max(0L, enginePosition.FrameIndex.Value);
            isAbsoluteFrameIndex = enginePosition.IsFrameIndexAbsolute;
            return true;
        }

        private long GetMaxFrameIndex(string paneId)
        {
            var ffmpegEngine = GetEngineForPane(paneId) as FfmpegReviewEngine;
            if (ffmpegEngine != null && ffmpegEngine.IsGlobalFrameIndexAvailable && ffmpegEngine.IndexedFrameCount > 0L)
            {
                return ffmpegEngine.IndexedFrameCount - 1L;
            }

            var mediaDuration = GetPaneMediaDuration(paneId);
            var positionStep = GetPanePositionStep(paneId);
            if (mediaDuration <= TimeSpan.Zero || positionStep <= TimeSpan.Zero)
            {
                return -1L;
            }

            return Math.Max(0L, (mediaDuration.Ticks + positionStep.Ticks - 1L) / positionStep.Ticks - 1L);
        }

        private void UpdatePanePresentation(
            ReviewWorkspaceSnapshot workspaceSnapshot,
            string paneId,
            Border paneBorder,
            Border paneHeaderBorder,
            Border paneFooterBorder,
            TextBlock titleTextBlock,
            TextBlock stateTextBlock,
            TextBlock fileTextBlock,
            Border emptyOverlay,
            TextBlock emptyTitleTextBlock,
            TextBlock emptyBodyTextBlock)
        {
            ReviewWorkspacePaneSnapshot paneSnapshot;
            if (workspaceSnapshot == null || !workspaceSnapshot.TryGetPane(paneId, out paneSnapshot))
            {
                paneSnapshot = new ReviewWorkspacePaneSnapshot(
                    paneId,
                    string.Empty,
                    string.Equals(paneId, ComparePaneId, StringComparison.Ordinal) ? "Compare A" : "Primary",
                    false,
                    string.Equals(paneId, PrimaryPaneId, StringComparison.Ordinal),
                    false,
                    false,
                    TimeSpan.Zero,
                    ReviewPlaybackState.Closed,
                    string.Empty,
                    ReviewPosition.Empty,
                    null);
            }

            titleTextBlock.Text = BuildPaneTitleText(paneSnapshot);
            stateTextBlock.Text = BuildPaneStateText(paneSnapshot);
            stateTextBlock.ToolTip = stateTextBlock.Text;

            var fileLabel = PaneHasLoadedMedia(paneSnapshot)
                ? Path.GetFileName(paneSnapshot.CurrentFilePath)
                : string.Empty;
            fileTextBlock.Text = fileLabel;
            fileTextBlock.Visibility = string.IsNullOrWhiteSpace(fileLabel)
                ? Visibility.Collapsed
                : Visibility.Visible;
            fileTextBlock.ToolTip = string.IsNullOrWhiteSpace(paneSnapshot.CurrentFilePath)
                ? null
                : paneSnapshot.CurrentFilePath;

            var highlightPaneSelection = IsCompareModeEnabled;
            var paneBorderBrush = FindResource(
                highlightPaneSelection && paneSnapshot.IsFocused
                    ? "PaneSelectedBorderBrush"
                    : "PanelBorderBrush") as Brush;
            if (paneBorderBrush != null)
            {
                paneBorder.BorderBrush = paneBorderBrush;
            }

            paneBorder.BorderThickness = highlightPaneSelection && paneSnapshot.IsFocused
                ? new Thickness(2d)
                : new Thickness(1d);

            var chromeBackground = FindResource(
                highlightPaneSelection && paneSnapshot.IsFocused
                    ? "PaneSelectedBrush"
                    : "PaneChromeBrush") as Brush;
            var chromeBorderBrush = FindResource(
                highlightPaneSelection && paneSnapshot.IsFocused
                    ? "PaneSelectedBorderBrush"
                    : "PaneChromeBorderBrush") as Brush;
            if (chromeBackground != null)
            {
                paneHeaderBorder.Background = chromeBackground;
                paneFooterBorder.Background = chromeBackground;
            }

            if (chromeBorderBrush != null)
            {
                paneHeaderBorder.BorderBrush = chromeBorderBrush;
                paneFooterBorder.BorderBrush = chromeBorderBrush;
            }

            var paneHasLoadedMedia = PaneHasLoadedMedia(paneSnapshot);
            emptyOverlay.Visibility = paneHasLoadedMedia
                ? Visibility.Collapsed
                : Visibility.Visible;
            UpdatePaneNavigationSurface(paneId, paneSnapshot);
            if (string.Equals(paneId, ComparePaneId, StringComparison.Ordinal))
            {
                emptyTitleTextBlock.Text = "Add a compare video";
                emptyBodyTextBlock.Text = string.IsNullOrWhiteSpace(_lastMediaErrorMessage) || !paneSnapshot.IsFocused
                    ? "Load a second video to compare against the left pane"
                    : _lastMediaErrorMessage;
                return;
            }

            if (!App.HasBundledFfmpegRuntime)
            {
                emptyTitleTextBlock.Text = "Playback runtime missing";
                emptyBodyTextBlock.Text = GetRuntimeStatusMessage();
                return;
            }

            emptyTitleTextBlock.Text = "Drop a video here";
            emptyBodyTextBlock.Text = !string.IsNullOrWhiteSpace(_lastMediaErrorMessage) && paneSnapshot.IsFocused
                ? _lastMediaErrorMessage
                : "Or open a video to begin";
        }

        private string BuildPaneTitleText(ReviewWorkspacePaneSnapshot paneSnapshot)
        {
            if (string.Equals(paneSnapshot.PaneId, ComparePaneId, StringComparison.Ordinal))
            {
                return "Compare";
            }

            return "Primary";
        }

        private string BuildPaneStateText(ReviewWorkspacePaneSnapshot paneSnapshot)
        {
            if (!PaneHasLoadedMedia(paneSnapshot))
            {
                return string.Equals(paneSnapshot.PaneId, ComparePaneId, StringComparison.Ordinal)
                    ? "Empty"
                    : "Ready";
            }

            var frameText = paneSnapshot.HasAbsoluteFrameIdentity && paneSnapshot.FrameIndex.HasValue
                ? "Frame " + GetDisplayedFrameNumber(paneSnapshot.FrameIndex.Value).ToString(CultureInfo.InvariantCulture)
                : "Frame pending";
            return string.Format(
                CultureInfo.InvariantCulture,
                "{0} \u00b7 {1} \u00b7 {2}",
                paneSnapshot.PlaybackState,
                frameText,
                FormatTime(paneSnapshot.PresentationTime));
        }

        private string BuildCompareStatusText(
            ReviewWorkspacePaneSnapshot primaryPaneSnapshot,
            ReviewWorkspacePaneSnapshot comparePaneSnapshot)
        {
            if (!IsCompareModeEnabled)
            {
                return string.Empty;
            }

            var relationshipText = BuildCompareRelationshipText(primaryPaneSnapshot, comparePaneSnapshot);
            return string.Format(
                CultureInfo.InvariantCulture,
                "{0} | {1}",
                relationshipText,
                _lastCompareAlignmentStatus);
        }

        private string BuildCompareRelationshipText(
            ReviewWorkspacePaneSnapshot primaryPaneSnapshot,
            ReviewWorkspacePaneSnapshot comparePaneSnapshot)
        {
            if (!PaneHasLoadedMedia(primaryPaneSnapshot) || !PaneHasLoadedMedia(comparePaneSnapshot))
            {
                return "Compare: Load two videos";
            }

            if (primaryPaneSnapshot.HasAbsoluteFrameIdentity &&
                primaryPaneSnapshot.FrameIndex.HasValue &&
                comparePaneSnapshot.HasAbsoluteFrameIdentity &&
                comparePaneSnapshot.FrameIndex.HasValue)
            {
                var frameDelta = comparePaneSnapshot.FrameIndex.Value - primaryPaneSnapshot.FrameIndex.Value;
                if (frameDelta == 0)
                {
                    return "Compare: Same frame";
                }

                var direction = frameDelta > 0 ? "Right" : "Left";
                return string.Format(
                    CultureInfo.InvariantCulture,
                    "Compare: {0} +{1} {2}",
                    direction,
                    Math.Abs(frameDelta),
                    Math.Abs(frameDelta) == 1 ? "frame" : "frames");
            }

            return "Compare: Time-based only";
        }

        private string BuildComparePanePositionText(ReviewWorkspacePaneSnapshot paneSnapshot)
        {
            if (paneSnapshot != null &&
                paneSnapshot.HasAbsoluteFrameIdentity &&
                paneSnapshot.FrameIndex.HasValue)
            {
                return string.Format(
                    CultureInfo.InvariantCulture,
                    "frame {0}",
                    GetDisplayedFrameNumber(paneSnapshot.FrameIndex.Value));
            }

            return "time " + FormatTime(paneSnapshot != null
                ? paneSnapshot.PresentationTime
                : TimeSpan.Zero);
        }

        private static string BuildCompareFrameAvailabilityText(
            ReviewWorkspacePaneSnapshot primaryPaneSnapshot,
            ReviewWorkspacePaneSnapshot comparePaneSnapshot)
        {
            var leftHasFrameIdentity = primaryPaneSnapshot != null && primaryPaneSnapshot.HasAbsoluteFrameIdentity;
            var rightHasFrameIdentity = comparePaneSnapshot != null && comparePaneSnapshot.HasAbsoluteFrameIdentity;
            if (!leftHasFrameIdentity && !rightHasFrameIdentity)
            {
                return "Frame identity unavailable on both panes.";
            }

            if (!leftHasFrameIdentity)
            {
                return "Frame identity unavailable on the left pane.";
            }

            if (!rightHasFrameIdentity)
            {
                return "Frame identity unavailable on the right pane.";
            }

            return "Frame identity unavailable.";
        }

        private static string FormatSignedFrameDelta(long frameDelta)
        {
            if (frameDelta > 0)
            {
                return "+" + frameDelta.ToString(CultureInfo.InvariantCulture);
            }

            return frameDelta.ToString(CultureInfo.InvariantCulture);
        }

        private static string GetComparePaneSideLabel(string paneId)
        {
            if (string.Equals(paneId, ComparePaneId, StringComparison.Ordinal))
            {
                return "Right";
            }

            return "Left";
        }

        private bool TrySelectPaneForShell(string paneId)
        {
            if (string.IsNullOrWhiteSpace(paneId))
            {
                return false;
            }

            if (string.Equals(paneId, ComparePaneId, StringComparison.Ordinal))
            {
                EnsureComparePaneInitialized();
            }

            return _workspaceCoordinator.TrySelectPane(paneId, WorkspacePaneSelectionMode.ActiveAndFocused);
        }

        private bool TrySelectPaneForPaneCommand(string paneId)
        {
            if (!TrySelectPaneForShell(paneId))
            {
                SetPlaybackMessage("The selected compare pane is not available.");
                return false;
            }

            return true;
        }

        private async Task RunFocusedPaneActionAsync(string paneId, Func<Task> action)
        {
            if (action == null)
            {
                throw new ArgumentNullException(nameof(action));
            }

            if (!TrySelectPaneForPaneCommand(paneId))
            {
                return;
            }

            SetPaneLoopCommandContext(paneId);
            await action();
            FocusPreferredVideoSurface();
        }

        private async void OpenFileButton_Click(object sender, RoutedEventArgs e)
        {
            await OpenPaneWithDialogAsync(GetFocusedPaneId());
        }

        private async void OpenPaneButton_Click(object sender, RoutedEventArgs e)
        {
            var paneId = (sender as FrameworkElement)?.Tag as string;
            await OpenPaneWithDialogAsync(paneId);
        }

        private async void CompareModeCheckBox_Checked(object sender, RoutedEventArgs e)
        {
            EnsureComparePaneInitialized();
            if (WindowState == WindowState.Normal && Width < CompareModePreferredMinWindowWidth)
            {
                Width = CompareModePreferredMinWindowWidth;
            }

            UpdateCompareModeVisualState();
            UpdateWorkspacePanePresentation();
            await Dispatcher.Yield(DispatcherPriority.Background);
        }

        private async void CompareModeCheckBox_Unchecked(object sender, RoutedEventArgs e)
        {
            SetSharedLoopCommandContext();
            _activeLoopPlaybackPaneId = null;
            if (AllPanesCheckBox != null)
            {
                AllPanesCheckBox.IsChecked = false;
            }

            ResetCompareAlignmentStatus();

            if (_compareVideoReviewEngine != null && _compareVideoReviewEngine.IsPlaying)
            {
                var previousFocusedPaneId = GetFocusedPaneId();
                if (_workspaceCoordinator.TrySetActiveAndFocusedPane(ComparePaneId))
                {
                    await _workspaceCoordinator.PauseAsync();
                }

                _workspaceCoordinator.TrySetActiveAndFocusedPane(
                    string.IsNullOrWhiteSpace(previousFocusedPaneId)
                        ? PrimaryPaneId
                        : previousFocusedPaneId);
            }

            _workspaceCoordinator.TrySetActiveAndFocusedPane(PrimaryPaneId);
            UpdateCompareModeVisualState();
            UpdateWorkspacePanePresentation();
            FocusPreferredVideoSurface();
        }

        private void AllPanesCheckBox_Checked(object sender, RoutedEventArgs e)
        {
            if (AllPanesCheckBox == null || AllPanesCheckBox.Visibility != Visibility.Visible)
            {
                return;
            }

            if (_isMediaLoaded)
            {
                UpdateTransportState();
            }
        }

        private void AllPanesCheckBox_Unchecked(object sender, RoutedEventArgs e)
        {
            if (AllPanesCheckBox != null && AllPanesCheckBox.Visibility == Visibility.Visible && _isMediaLoaded)
            {
                UpdateTransportState();
            }
        }

        private async void PanePlayButton_Click(object sender, RoutedEventArgs e)
        {
            var paneId = GetPaneIdFromSender(sender);
            await RunFocusedPaneActionAsync(
                paneId,
                () => StartPlaybackAsync(SynchronizedOperationScope.FocusedPane, paneId));
        }

        private async void PanePauseButton_Click(object sender, RoutedEventArgs e)
        {
            await RunFocusedPaneActionAsync(
                GetPaneIdFromSender(sender),
                () => PausePlaybackAsync(logAction: true, operationScope: SynchronizedOperationScope.FocusedPane));
        }

        private async void PaneStepBackButton_Click(object sender, RoutedEventArgs e)
        {
            await RunFocusedPaneActionAsync(
                GetPaneIdFromSender(sender),
                () => StepFrameAsync(-1, SynchronizedOperationScope.FocusedPane));
        }

        private async void PaneStepForwardButton_Click(object sender, RoutedEventArgs e)
        {
            await RunFocusedPaneActionAsync(
                GetPaneIdFromSender(sender),
                () => StepFrameAsync(1, SynchronizedOperationScope.FocusedPane));
        }

        private async void AlignRightToLeftButton_Click(object sender, RoutedEventArgs e)
        {
            await AlignPaneAsync(PrimaryPaneId, ComparePaneId);
        }

        private async void AlignLeftToRightButton_Click(object sender, RoutedEventArgs e)
        {
            await AlignPaneAsync(ComparePaneId, PrimaryPaneId);
        }

        private void ExitMenuItem_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }

        private async void CloseVideoMenuItem_Click(object sender, RoutedEventArgs e)
        {
            await CloseMediaAsync();
        }

        private void VideoInfoMenuItem_Click(object sender, RoutedEventArgs e)
        {
            ShowVideoInfoWindow();
        }

        private void VideoPaneVideoInfoMenuItem_Click(object sender, RoutedEventArgs e)
        {
            ShowVideoInfoWindow(GetPaneIdFromSender(sender));
        }

        private void HelpMenuItem_Click(object sender, RoutedEventArgs e)
        {
            ShowHelpWindow();
        }

        private void AboutMenuItem_Click(object sender, RoutedEventArgs e)
        {
            ShowAboutWindow();
        }

        private void ExportDiagnosticsMenuItem_Click(object sender, RoutedEventArgs e)
        {
            ExportDiagnostics();
        }

        private void ToggleFullScreenMenuItem_Click(object sender, RoutedEventArgs e)
        {
            ToggleFullScreen();
        }

        private void LoopPlaybackMenuItem_Checked(object sender, RoutedEventArgs e)
        {
            UpdateLoopUi();
            var evaluation = EvaluateSharedLoopRange(_workspaceCoordinator.GetWorkspaceSnapshot(), GetRequestedOperationScope());
            SetPlaybackMessage(evaluation.HasMarkers
                ? evaluation.CanLoopExactly
                    ? "Loop playback enabled for the current A/B range."
                    : "Loop playback enabled, but the current A/B range is not ready yet."
                : "Loop playback enabled for the full media range.");
            LogInfo("Loop playback enabled.");
        }

        private void LoopPlaybackMenuItem_Unchecked(object sender, RoutedEventArgs e)
        {
            UpdateLoopUi();
            SetPlaybackMessage("Loop playback disabled.");
            LogInfo("Loop playback disabled.");
        }

        private void SetLoopInMenuItem_Click(object sender, RoutedEventArgs e)
        {
            SetLoopMarker(LoopPlaybackMarkerEndpoint.In);
        }

        private void SetLoopOutMenuItem_Click(object sender, RoutedEventArgs e)
        {
            SetLoopMarker(LoopPlaybackMarkerEndpoint.Out);
        }

        private void ClearLoopPointsMenuItem_Click(object sender, RoutedEventArgs e)
        {
            ClearLoopPoints();
        }

        private async void SaveLoopAsClipMenuItem_Click(object sender, RoutedEventArgs e)
        {
            await ExportLoopClipAsync(null, null);
        }

        private async void PaneSaveLoopAsClipMenuItem_Click(object sender, RoutedEventArgs e)
        {
            await ExportLoopClipAsync(null, GetPaneIdFromSender(sender));
        }

        private void ToggleFullScreenButton_Click(object sender, RoutedEventArgs e)
        {
            ToggleFullScreen();
        }

        private async void PlayPauseButton_Click(object sender, RoutedEventArgs e)
        {
            SetSharedLoopCommandContext();
            await TogglePlaybackAsync();
        }

        private async void RewindButton_Click(object sender, RoutedEventArgs e)
        {
            SetSharedLoopCommandContext();
            await SeekRelativeAsync(-SeekJump);
        }

        private async void FastForwardButton_Click(object sender, RoutedEventArgs e)
        {
            SetSharedLoopCommandContext();
            await SeekRelativeAsync(SeekJump);
        }

        private async void PreviousFrameButton_Click(object sender, RoutedEventArgs e)
        {
            SetSharedLoopCommandContext();
            await StepFrameAsync(-1);
        }

        private async void NextFrameButton_Click(object sender, RoutedEventArgs e)
        {
            SetSharedLoopCommandContext();
            await StepFrameAsync(1);
        }

        private async void FrameNumberTextBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                await JumpToFrameFromInputAsync();
                e.Handled = true;
            }
        }

        private void FrameNumberTextBox_GotKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
        {
            SetSharedLoopCommandContext();
            FrameNumberTextBox.SelectAll();
        }

        private async void PaneFrameNumberTextBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key != Key.Enter)
            {
                return;
            }

            var paneId = GetPaneIdFromSender(sender);
            var frameNumberTextBox = sender as TextBox;
            if (frameNumberTextBox == null)
            {
                return;
            }

            await JumpToFrameFromInputAsync(paneId, frameNumberTextBox);
            e.Handled = true;
        }

        private void PaneFrameNumberTextBox_GotKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
        {
            SetPaneLoopCommandContext(GetPaneIdFromSender(sender));
            (sender as TextBox)?.SelectAll();
        }

        private async void PositionSlider_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (!_isMediaLoaded)
            {
                return;
            }

            SetSharedLoopCommandContext();
            if (IsSliderThumbSource(e.OriginalSource as DependencyObject))
            {
                EndHeldFrameStep();
                if (_isPlaying)
                {
                    _suppressLoopRestart = true;
                    await PausePlaybackAsync(logAction: false);
                }

                _isSliderDragActive = true;
                return;
            }

            _isSliderDragActive = false;
            e.Handled = true;

            TimeSpan clickedTarget;
            if (TryMoveSliderToPoint(PositionSlider, e, out clickedTarget))
            {
                await CommitSliderSeekAsync("click", clickedTarget);
            }
        }

        private async void PositionSlider_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (!_isMediaLoaded || !_isSliderDragActive)
            {
                _isSliderDragActive = false;
                return;
            }

            _isSliderDragActive = false;
            _hasPendingSliderScrubTarget = false;
            _sliderScrubTimer.Stop();
            await AwaitSliderScrubIdleAsync();
            await CommitSliderSeekAsync("drag", TimeSpan.FromSeconds(PositionSlider.Value));
        }

        private static bool TryMoveSliderToPoint(Slider slider, MouseButtonEventArgs e, out TimeSpan target)
        {
            target = TimeSpan.Zero;
            if (slider == null || slider.Maximum <= slider.Minimum)
            {
                return false;
            }

            var track = slider.Template.FindName("PART_Track", slider) as Track;
            double value;
            if (track != null)
            {
                value = track.ValueFromPoint(e.GetPosition(track));
            }
            else
            {
                if (slider.ActualWidth <= 0d)
                {
                    return false;
                }

                var point = e.GetPosition(slider);
                var ratio = Math.Max(0d, Math.Min(1d, point.X / slider.ActualWidth));
                if (slider.FlowDirection == FlowDirection.RightToLeft)
                {
                    ratio = 1d - ratio;
                }

                value = slider.Minimum + ((slider.Maximum - slider.Minimum) * ratio);
            }

            value = Math.Max(slider.Minimum, Math.Min(slider.Maximum, value));
            slider.Value = value;
            target = TimeSpan.FromSeconds(value);
            return true;
        }

        private static bool IsSliderThumbSource(DependencyObject source)
        {
            while (source != null)
            {
                if (source is Thumb)
                {
                    return true;
                }

                source = VisualTreeHelper.GetParent(source);
            }

            return false;
        }

        private void PositionSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_suppressSliderUpdate || !_isSliderDragActive)
            {
                return;
            }

            var previewPosition = TimeSpan.FromSeconds(PositionSlider.Value);
            CurrentPositionTextBlock.Text = FormatTime(previewPosition);
            QueueSliderScrub(previewPosition);
        }

        private async void PanePositionSlider_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            var slider = sender as Slider;
            var paneId = GetPaneIdFromSender(sender);
            if (slider == null || string.IsNullOrWhiteSpace(paneId))
            {
                return;
            }

            if (!TrySelectPaneForPaneCommand(paneId))
            {
                return;
            }

            SetPaneLoopCommandContext(paneId);
            if (IsSliderThumbSource(e.OriginalSource as DependencyObject))
            {
                EndHeldFrameStep();
                var engine = GetEngineForPane(paneId);
                if (engine != null && engine.IsPlaying)
                {
                    _suppressLoopRestart = true;
                    await PausePlaybackAsync(logAction: false, operationScope: SynchronizedOperationScope.FocusedPane);
                }

                _activePaneSliderPaneId = paneId;
                _isPaneSliderDragActive = true;
                return;
            }

            _isPaneSliderDragActive = false;
            _activePaneSliderPaneId = paneId;
            e.Handled = true;

            TimeSpan clickedTarget;
            if (TryMoveSliderToPoint(slider, e, out clickedTarget))
            {
                await CommitPaneSliderSeekAsync(paneId, "click", clickedTarget);
            }
        }

        private async void PanePositionSlider_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            var slider = sender as Slider;
            var paneId = GetPaneIdFromSender(sender);
            if (slider == null || string.IsNullOrWhiteSpace(paneId) || !_isPaneSliderDragActive)
            {
                _isPaneSliderDragActive = false;
                return;
            }

            _isPaneSliderDragActive = false;
            _activePaneSliderPaneId = paneId;
            _hasPendingPaneSliderScrubTarget = false;
            _paneSliderScrubTimer.Stop();
            await AwaitPaneSliderScrubIdleAsync();
            await CommitPaneSliderSeekAsync(paneId, "drag", TimeSpan.FromSeconds(slider.Value));
        }

        private void PanePositionSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            var paneId = GetPaneIdFromSender(sender);
            if (_suppressPaneSliderUpdate ||
                !_isPaneSliderDragActive ||
                !string.Equals(_activePaneSliderPaneId, paneId, StringComparison.Ordinal))
            {
                return;
            }

            TextBlock currentPositionTextBlock;
            TextBlock durationTextBlock;
            Slider positionSlider;
            TextBox frameNumberTextBox;
            if (!TryGetPaneNavigationControls(
                paneId,
                out currentPositionTextBlock,
                out durationTextBlock,
                out positionSlider,
                out frameNumberTextBox))
            {
                return;
            }

            var previewPosition = TimeSpan.FromSeconds(positionSlider.Value);
            currentPositionTextBlock.Text = FormatTime(previewPosition);
            QueuePaneSliderScrub(paneId, previewPosition);
        }

        private async void Window_Drop(object sender, DragEventArgs e)
        {
            await HandleDroppedFilesAsync(e, preferredPaneId: null);
        }

        private void Window_PreviewDragOver(object sender, DragEventArgs e)
        {
            UpdateDropEffects(e, preferredPaneId: null);
        }

        private async void VideoPane_Drop(object sender, DragEventArgs e)
        {
            await HandleDroppedFilesAsync(e, (sender as FrameworkElement)?.Tag as string);
        }

        private void VideoPane_PreviewDragOver(object sender, DragEventArgs e)
        {
            UpdateDropEffects(e, (sender as FrameworkElement)?.Tag as string);
        }

        private async Task HandleDroppedFilesAsync(DragEventArgs e, string preferredPaneId)
        {
            var files = GetDroppedFiles(e);
            if (files == null || files.Length == 0)
            {
                return;
            }

            var firstSupportedFile = files.FirstOrDefault(IsSupportedVideoFile);
            if (firstSupportedFile == null)
            {
                MessageBox.Show(
                    this,
                    "Drop a supported video file (" + GetSupportedVideoExtensionsDescription() + ").",
                    "Unsupported File",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
                e.Handled = true;
                return;
            }

            if (!EnsureRuntimeAvailable())
            {
                e.Handled = true;
                return;
            }

            var targetPaneId = ResolveDropTargetPaneId(preferredPaneId, e.OriginalSource as DependencyObject);
            await OpenMediaAsync(firstSupportedFile, targetPaneId);
            e.Handled = true;
        }

        private void UpdateDropEffects(DragEventArgs e, string preferredPaneId)
        {
            var files = GetDroppedFiles(e);
            var hasSupportedFile = files != null && files.Any(IsSupportedVideoFile);
            e.Effects = hasSupportedFile
                ? DragDropEffects.Copy
                : DragDropEffects.None;

            e.Handled = true;
        }

        private static string[] GetDroppedFiles(DragEventArgs e)
        {
            return e != null
                ? e.Data.GetData(DataFormats.FileDrop) as string[]
                : null;
        }

        private string ResolveDropTargetPaneId(string preferredPaneId, DependencyObject originalSource)
        {
            if (!string.IsNullOrWhiteSpace(preferredPaneId))
            {
                return preferredPaneId;
            }

            var resolvedPaneId = TryFindPaneId(originalSource);
            return string.IsNullOrWhiteSpace(resolvedPaneId)
                ? GetFocusedPaneId()
                : resolvedPaneId;
        }

        private string TryFindPaneId(DependencyObject originalSource)
        {
            var current = originalSource;
            while (current != null)
            {
                var frameworkElement = current as FrameworkElement;
                var taggedPaneId = frameworkElement != null ? frameworkElement.Tag as string : null;
                if (string.Equals(taggedPaneId, PrimaryPaneId, StringComparison.Ordinal) ||
                    string.Equals(taggedPaneId, ComparePaneId, StringComparison.Ordinal))
                {
                    return taggedPaneId;
                }

                var frameworkContentElement = current as FrameworkContentElement;
                current = frameworkContentElement != null
                    ? frameworkContentElement.Parent
                    : VisualTreeHelper.GetParent(current);
            }

            return null;
        }

        private async void Window_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (Keyboard.Modifiers == ModifierKeys.Control && e.Key == Key.O)
            {
                OpenFileButton_Click(sender, e);
                e.Handled = true;
                return;
            }

            if (Keyboard.Modifiers == ModifierKeys.Control && e.Key == Key.W)
            {
                await CloseMediaAsync();
                e.Handled = true;
                return;
            }

            if (Keyboard.Modifiers == (ModifierKeys.Control | ModifierKeys.Shift) && e.Key == Key.E)
            {
                ExportDiagnostics();
                e.Handled = true;
                return;
            }

            if (e.Key == Key.F1)
            {
                ShowHelpWindow();
                e.Handled = true;
                return;
            }

            if (Keyboard.Modifiers == ModifierKeys.Alt && e.SystemKey == Key.Enter)
            {
                ToggleFullScreen();
                e.Handled = true;
                return;
            }

            if (e.Key == Key.F11)
            {
                ToggleFullScreen();
                e.Handled = true;
                return;
            }

            if (e.Key == Key.Escape && _isFullScreen)
            {
                ExitFullScreen();
                e.Handled = true;
                return;
            }

            if (FrameNumberTextBox.IsKeyboardFocusWithin ||
                PrimaryPaneFrameNumberTextBox.IsKeyboardFocusWithin ||
                ComparePaneFrameNumberTextBox.IsKeyboardFocusWithin)
            {
                return;
            }

            LoopPlaybackMarkerEndpoint loopMarkerEndpoint;
            if (_isMediaLoaded &&
                Keyboard.Modifiers == ModifierKeys.None &&
                TryGetLoopMarkerEndpointKey(e.Key, out loopMarkerEndpoint))
            {
                SetLoopMarker(loopMarkerEndpoint);
                e.Handled = true;
                return;
            }

            int modifiedFrameStep;
            if (_isMediaLoaded &&
                !_isPlaying &&
                TryGetModifiedFrameStepCount(Keyboard.Modifiers, out modifiedFrameStep) &&
                (e.Key == Key.Left || e.Key == Key.Right))
            {
                await StepFrameAsync((e.Key == Key.Left ? -1 : 1) * modifiedFrameStep);
                e.Handled = true;
                return;
            }

            if (e.IsRepeat && (e.Key == Key.Space || e.Key == Key.Left || e.Key == Key.Right))
            {
                e.Handled = true;
                return;
            }

            if (!_isMediaLoaded || Keyboard.Modifiers != ModifierKeys.None)
            {
                return;
            }

            switch (e.Key)
            {
                case Key.Space:
                    await TogglePlaybackAsync();
                    e.Handled = true;
                    break;
                case Key.Left:
                    if (!_isPlaying)
                    {
                        BeginHeldFrameStep(-1);
                        e.Handled = true;
                    }
                    break;
                case Key.Right:
                    if (!_isPlaying)
                    {
                        BeginHeldFrameStep(1);
                        e.Handled = true;
                    }
                    break;
                case Key.J:
                    await SeekRelativeAsync(-SeekJump);
                    e.Handled = true;
                    break;
                case Key.L:
                    await SeekRelativeAsync(SeekJump);
                    e.Handled = true;
                    break;
            }
        }

        private void Window_PreviewKeyUp(object sender, KeyEventArgs e)
        {
            if ((e.Key == Key.Left && _heldFrameStepDirection < 0)
                || (e.Key == Key.Right && _heldFrameStepDirection > 0))
            {
                EndHeldFrameStep();
                e.Handled = true;
            }
        }

        private void Window_Closed(object sender, EventArgs e)
        {
            LogInfo("Session ended.");
            _positionTimer.Stop();
            _frameStepRepeatTimer.Stop();
            _workspaceCoordinator.WorkspaceChanged -= ReviewWorkspaceCoordinator_WorkspaceChanged;
            _workspaceCoordinator.PreparationStateChanged -= ReviewWorkspaceCoordinator_PreparationStateChanged;
            _workspaceCoordinator.Dispose();
            _sessionCoordinator.Dispose();
            _videoReviewEngine.FramePresented -= VideoReviewEngine_FramePresented;
            _videoReviewEngine.Dispose();
            if (_compareVideoReviewEngine != null)
            {
                _compareVideoReviewEngine.FramePresented -= VideoReviewEngine_FramePresented;
                _compareSessionCoordinator?.Dispose();
                _compareVideoReviewEngine.Dispose();
            }
        }

        private Task OpenMediaAsync(string filePath)
        {
            return OpenMediaAsync(filePath, null);
        }

        private async Task OpenMediaAsync(string filePath, string paneId)
        {
            if (!File.Exists(filePath))
            {
                LogWarning("Open requested for a file that does not exist: " + GetSafeFileDisplay(filePath));
                MessageBox.Show(this, "That file does not exist.", "Missing File", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (!IsSupportedVideoFile(filePath))
            {
                LogWarning("Open requested for an unsupported file type: " + GetSafeFileDisplay(filePath));
                MessageBox.Show(
                    this,
                    "Supported file types are " + GetSupportedVideoExtensionsDescription() + ".",
                    "Unsupported File",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
                return;
            }

            try
            {
                if (!string.IsNullOrWhiteSpace(paneId))
                {
                    TrySelectPaneForShell(paneId);
                }

                var targetEngine = GetFocusedPaneEngine();
                Mouse.OverrideCursor = Cursors.Wait;
                _lastMediaErrorMessage = string.Empty;
                if (IsCompareModeEnabled)
                {
                    ResetCompareAlignmentStatus();
                }
                SetPlaybackMessage("Opening media...");
                LogInfo("Opening media: " + GetSafeFileDisplay(filePath));

                if (_isMediaLoaded)
                {
                    LogInfo("Closing current media before opening the new file.");
                }

                await _workspaceCoordinator.OpenAsync(filePath);
                ApplySessionSnapshot(_workspaceCoordinator.RefreshFromEngine());

                _recentFilesService.Add(filePath);
                RefreshRecentFilesMenu();
                UpdateHeader();
                UpdatePositionDisplay(GetDisplayPosition());
                UpdateTransportState();
                Activate();
                Focus();
                FocusPreferredVideoSurface();

                LogInfo(string.Format(
                    CultureInfo.InvariantCulture,
                    "Media opened: {0} | FPS {1:0.###} | Step {2} | Duration {3}.",
                    GetSafeFileDisplay(filePath),
                    _framesPerSecond,
                    FormatStepDuration(_positionStep),
                    FormatTime(_mediaDuration)));
                var ffmpegEngine = targetEngine as FfmpegReviewEngine;
                if (ffmpegEngine != null)
                {
                    LogInfo(string.Format(
                        CultureInfo.InvariantCulture,
                        "Decode backend: {0} | GPU active {1} | GPU status {2} | Fallback {3} | Cache budget {4:0.0} MiB | Queue depth {5}.",
                        string.IsNullOrWhiteSpace(ffmpegEngine.ActiveDecodeBackend) ? "(unknown)" : ffmpegEngine.ActiveDecodeBackend,
                        ffmpegEngine.IsGpuActive ? "yes" : "no",
                        string.IsNullOrWhiteSpace(ffmpegEngine.GpuCapabilityStatus) ? "(none)" : ffmpegEngine.GpuCapabilityStatus,
                        string.IsNullOrWhiteSpace(ffmpegEngine.GpuFallbackReason) ? "(none)" : ffmpegEngine.GpuFallbackReason,
                        ffmpegEngine.DecodedFrameCacheBudgetBytes / 1048576d,
                        ffmpegEngine.OperationalQueueDepth));
                    LogInfo(string.Format(
                        CultureInfo.InvariantCulture,
                        "Open timing: total {0:0.0} ms | container/probe {1:0.0} ms | stream {2:0.0} ms | audio probe {3:0.0} ms | decoder {4:0.0} ms | first frame {5:0.0} ms | cache warm {6:0.0} ms | index {7}.",
                        ffmpegEngine.LastOpenTotalMilliseconds,
                        ffmpegEngine.LastOpenContainerProbeMilliseconds,
                        ffmpegEngine.LastOpenStreamDiscoveryMilliseconds,
                        ffmpegEngine.LastOpenAudioProbeMilliseconds,
                        ffmpegEngine.LastOpenVideoDecoderInitializationMilliseconds,
                        ffmpegEngine.LastOpenFirstFrameDecodeMilliseconds,
                        ffmpegEngine.LastOpenInitialCacheWarmMilliseconds,
                        ffmpegEngine.GlobalFrameIndexStatus));
                }
            }
            catch (Exception ex)
            {
                var targetEngine = GetFocusedPaneEngine();
                _lastMediaErrorMessage = targetEngine != null && !string.IsNullOrWhiteSpace(targetEngine.LastErrorMessage)
                    ? SanitizeSensitiveText(targetEngine.LastErrorMessage)
                    : SanitizeSensitiveText(ex.Message);
                ResetMediaState(clearFilePath: true, clearErrorMessage: false);
                SetPlaybackMessage("Could not open the selected media.");
                SetMediaSummary(_lastMediaErrorMessage);
                LogError("Open failed for " + GetSafeFileDisplay(filePath), ex);
            }
            finally
            {
                Mouse.OverrideCursor = null;
            }
        }

        private bool EnsureRuntimeAvailable()
        {
            if (App.HasBundledFfmpegRuntime)
            {
                return true;
            }

            LogError(GetRuntimeStatusMessage());
            MessageBox.Show(
                this,
                GetRuntimeStatusMessage(),
                "Playback Runtime Missing",
                MessageBoxButton.OK,
                MessageBoxImage.Error);

            return false;
        }

        private async Task OpenPaneWithDialogAsync(string paneId)
        {
            if (!EnsureRuntimeAvailable())
            {
                return;
            }

            var resolvedPaneId = string.IsNullOrWhiteSpace(paneId) ? GetFocusedPaneId() : paneId;
            TrySelectPaneForShell(resolvedPaneId);

            var dialog = new OpenFileDialog
            {
                Title = "Open Video File",
                Filter = GetOpenFileFilter()
            };

            if (dialog.ShowDialog(this) == true)
            {
                await OpenMediaAsync(dialog.FileName, resolvedPaneId);
            }
        }

        private Task TogglePlaybackAsync()
        {
            return TogglePlaybackAsync((SynchronizedOperationScope?)null);
        }

        private async Task TogglePlaybackAsync(SynchronizedOperationScope? operationScope)
        {
            EndHeldFrameStep();

            if (!_buildVariant.SupportsTimedPlayback)
            {
                SetPlaybackMessage(_buildVariant.PlaybackCapabilityText);
                LogInfo(_buildVariant.PlaybackCapabilityText);
                return;
            }

            if (_isPlaying)
            {
                await PausePlaybackAsync(operationScope: operationScope);
                return;
            }

            await StartPlaybackAsync(operationScope, null);
        }

        private async Task CloseMediaAsync()
        {
            EndHeldFrameStep();

            if (_isMediaLoaded)
            {
                LogInfo("Closing media: " + GetSafeFileDisplay(_currentFilePath));
                await _workspaceCoordinator.CloseAsync();
            }

            ResetMediaState(clearFilePath: true, clearErrorMessage: true);
        }

        private Task PausePlaybackAsync()
        {
            return PausePlaybackAsync(true);
        }

        private Task PausePlaybackAsync(bool logAction)
        {
            return PausePlaybackAsync(logAction, (SynchronizedOperationScope?)null);
        }

        private async Task PausePlaybackAsync(
            bool logAction = true,
            SynchronizedOperationScope? operationScope = null)
        {
            if (!_isMediaLoaded)
            {
                return;
            }

            _suppressLoopRestart = true;
            var positionBeforePause = GetDisplayPosition();
            var wasPlaying = _isPlaying;
            await _workspaceCoordinator.PauseAsync(operationScope ?? GetRequestedOperationScope());
            ApplySessionSnapshot(_workspaceCoordinator.RefreshFromEngine());
            UpdateTransportState();

            if (logAction && wasPlaying)
            {
                LogInfo("Playback paused at " + FormatTime(positionBeforePause) + ".");
            }
        }

        private Task StartPlaybackAsync()
        {
            return StartPlaybackAsync((SynchronizedOperationScope?)null, null);
        }

        private async Task StartPlaybackAsync(
            SynchronizedOperationScope? operationScope,
            string loopPlaybackPaneId = null)
        {
            if (!_isMediaLoaded)
            {
                return;
            }

            if (!_buildVariant.SupportsTimedPlayback)
            {
                SetPlaybackMessage(_buildVariant.PlaybackCapabilityText);
                SetMediaSummary(_buildVariant.StatusText);
                LogInfo(_buildVariant.PlaybackCapabilityText);
                return;
            }

            var targetEngine = GetFocusedPaneEngine();
            var activeScope = operationScope ?? GetRequestedOperationScope();
            _lastPlaybackScope = activeScope;
            _activeLoopPlaybackPaneId = activeScope == SynchronizedOperationScope.FocusedPane &&
                                        !string.IsNullOrWhiteSpace(loopPlaybackPaneId) &&
                                        IsCompareModeEnabled
                ? loopPlaybackPaneId
                : null;
            _suppressLoopRestart = false;
            await _workspaceCoordinator.PlayAsync(activeScope);
            ApplySessionSnapshot(_workspaceCoordinator.RefreshFromEngine());
            if (!_isPlaying)
            {
                _lastMediaErrorMessage = targetEngine != null && !string.IsNullOrWhiteSpace(targetEngine.LastErrorMessage)
                    ? SanitizeSensitiveText(targetEngine.LastErrorMessage)
                    : "Playback did not start.";
                SetMediaSummary(_lastMediaErrorMessage);
                LogWarning("Playback did not start.");
            }
            else
            {
                LogInfo("Playback started at " + FormatTime(GetDisplayPosition()) + ".");
            }

            UpdateTransportState();
        }

        private Task SeekRelativeAsync(TimeSpan offset)
        {
            return SeekRelativeAsync(offset, (SynchronizedOperationScope?)null);
        }

        private async Task SeekRelativeAsync(TimeSpan offset, SynchronizedOperationScope? operationScope)
        {
            EndHeldFrameStep();

            if (!_isMediaLoaded)
            {
                return;
            }

            _suppressLoopRestart = true;
            var target = ClampPosition(GetDisplayPosition() + offset);
            await RunWithCacheStatusAsync(
                "Cache: seeking and warming...",
                () => _workspaceCoordinator.SeekToTimeAsync(target, operationScope ?? GetRequestedOperationScope()));
            UpdatePositionDisplay(GetDisplayPosition());
            LogInfo(string.Format(
                CultureInfo.InvariantCulture,
                "Seeked {0}{1} to {2}; landed at {3}.",
                offset < TimeSpan.Zero ? "-" : "+",
                FormatTime(offset.Duration()),
                FormatTime(target),
                FormatTime(GetDisplayPosition())));
        }

        private Task StepFrameAsync(int delta)
        {
            return StepFrameAsync(delta, (SynchronizedOperationScope?)null);
        }

        private async Task StepFrameAsync(int delta, SynchronizedOperationScope? operationScope)
        {
            if (!_isMediaLoaded || _isFrameStepInProgress || delta == 0)
            {
                return;
            }

            _isFrameStepInProgress = true;
            var stepDirection = delta < 0 ? -1 : 1;
            var stepCount = Math.Max(1, Math.Abs(delta));

            try
            {
                _suppressLoopRestart = true;
                await PausePlaybackAsync(logAction: false, operationScope: operationScope);

                FrameStepResult stepResult = null;
                var activeScope = operationScope ?? GetRequestedOperationScope();
                await RunWithCacheStatusAsync(
                    stepDirection < 0 ? "Cache: checking backward window..." : "Cache: refilling forward window...",
                    async () =>
                    {
                        for (var stepIndex = 0; stepIndex < stepCount; stepIndex++)
                        {
                            stepResult = stepDirection < 0
                                ? await _workspaceCoordinator.StepBackwardAsync(activeScope)
                                : await _workspaceCoordinator.StepForwardAsync(activeScope);
                            if (!stepResult.Success)
                            {
                                break;
                            }
                        }
                    });

                if (stepResult != null && !stepResult.Success)
                {
                    SetPlaybackMessage(stepResult.Message);
                    LogWarning(stepResult.Message);
                }

                UpdatePositionDisplay(GetDisplayPosition());
            }
            finally
            {
                _isFrameStepInProgress = false;
            }
        }

        private async Task CommitSliderSeekAsync(string interactionName, TimeSpan target)
        {
            LogInfo(string.Format(
                CultureInfo.InvariantCulture,
                "Timeline {0} seek requested from slider at {1}.",
                interactionName,
                FormatTime(ClampPosition(target))));

            await SeekToAsync(target, "timeline " + interactionName);
            FocusPreferredVideoSurface();
        }

        private Task SeekToAsync(TimeSpan target, string diagnosticSource = null)
        {
            return SeekToAsync(target, diagnosticSource, (SynchronizedOperationScope?)null);
        }

        private async Task SeekToAsync(
            TimeSpan target,
            string diagnosticSource = null,
            SynchronizedOperationScope? operationScope = null)
        {
            EndHeldFrameStep();

            if (!_isMediaLoaded)
            {
                return;
            }

            _suppressLoopRestart = true;
            var clampedTarget = ClampPosition(target);
            await RunWithCacheStatusAsync(
                "Cache: seeking...",
                () => _workspaceCoordinator.SeekToTimeAsync(clampedTarget, operationScope ?? GetRequestedOperationScope()));
            UpdatePositionDisplay(GetDisplayPosition());

            if (!string.IsNullOrWhiteSpace(diagnosticSource))
            {
                LogSeekResult(diagnosticSource, target, clampedTarget);
            }
        }

        private async Task SeekPaneToAsync(string paneId, TimeSpan target, string diagnosticSource = null)
        {
            EndHeldFrameStep();

            if (!TrySelectPaneForPaneCommand(paneId))
            {
                return;
            }

            var engine = GetEngineForPane(paneId);
            if (engine == null || !engine.IsMediaOpen)
            {
                return;
            }

            _suppressLoopRestart = true;
            var clampedTarget = ClampPositionForPane(paneId, target);
            await RunWithCacheStatusAsync(
                "Cache: seeking...",
                () => _workspaceCoordinator.SeekToTimeAsync(clampedTarget, SynchronizedOperationScope.FocusedPane));
            UpdateWorkspacePanePresentation();

            if (!string.IsNullOrWhiteSpace(diagnosticSource))
            {
                LogInfo(string.Format(
                    CultureInfo.InvariantCulture,
                    "{0}: requested {1}; committed {2}; landed at {3}.",
                    diagnosticSource,
                    FormatTime(target),
                    FormatTime(clampedTarget),
                    FormatTime(GetPaneDisplayPosition(paneId))));
            }
        }

        private async Task AlignPaneAsync(string sourcePaneId, string targetPaneId)
        {
            if (!IsCompareModeEnabled)
            {
                return;
            }

            ReviewWorkspacePaneSnapshot sourcePaneSnapshot;
            ReviewWorkspacePaneSnapshot targetPaneSnapshot;
            var workspaceSnapshot = _workspaceCoordinator.GetWorkspaceSnapshot();
            if (workspaceSnapshot == null ||
                !workspaceSnapshot.TryGetPane(sourcePaneId, out sourcePaneSnapshot) ||
                !workspaceSnapshot.TryGetPane(targetPaneId, out targetPaneSnapshot) ||
                !PaneHasLoadedMedia(sourcePaneSnapshot) ||
                !PaneHasLoadedMedia(targetPaneSnapshot))
            {
                SetPlaybackMessage("Load media into both compare panes before aligning them.");
                return;
            }

            await PausePlaybackAsync(logAction: false, operationScope: SynchronizedOperationScope.AllPanes);
            workspaceSnapshot = _workspaceCoordinator.GetWorkspaceSnapshot();
            if (workspaceSnapshot == null ||
                !workspaceSnapshot.TryGetPane(sourcePaneId, out sourcePaneSnapshot))
            {
                SetPlaybackMessage("The source compare pane is no longer available.");
                return;
            }

            if (!TrySelectPaneForPaneCommand(targetPaneId))
            {
                return;
            }

            var sourceSide = GetComparePaneSideLabel(sourcePaneId);
            var targetSide = GetComparePaneSideLabel(targetPaneId);
            if (sourcePaneSnapshot.HasAbsoluteFrameIdentity && sourcePaneSnapshot.FrameIndex.HasValue)
            {
                var sourceFrameIndex = sourcePaneSnapshot.FrameIndex.Value;
                var sourceFrameNumber = GetDisplayedFrameNumber(sourceFrameIndex);
                try
                {
                    await RunWithCacheStatusAsync(
                        "Cache: aligning exact frame...",
                        () => _workspaceCoordinator.SeekToFrameAsync(sourceFrameIndex));
                    UpdatePositionDisplay(GetDisplayPosition());
                    _lastCompareAlignmentStatus = "Last align: exact frame";
                    SetPlaybackMessage(string.Format(
                        CultureInfo.InvariantCulture,
                        "Aligned the {0} pane to the {1} pane using exact frame {2}.",
                        targetSide.ToLowerInvariant(),
                        sourceSide.ToLowerInvariant(),
                        sourceFrameNumber));
                    LogInfo(string.Format(
                        CultureInfo.InvariantCulture,
                        "Aligned compare pane {0} to {1} using exact frame {2}.",
                        targetPaneId,
                        sourcePaneId,
                        sourceFrameNumber));
                    UpdateWorkspacePanePresentation();
                    FocusPreferredVideoSurface();
                    return;
                }
                catch (Exception ex)
                {
                    var failureDetail = SanitizeSensitiveText(ex.Message);
                    LogWarning(string.Format(
                        CultureInfo.InvariantCulture,
                        "Exact-frame align for {0} <- {1} failed. Falling back to presentation time.{2}",
                        targetSide,
                        sourceSide,
                        string.IsNullOrWhiteSpace(failureDetail)
                            ? string.Empty
                            : " " + failureDetail));
                }
            }

            var targetTime = sourcePaneSnapshot.PresentationTime;
            await SeekToAsync(
                targetTime,
                string.Format(
                    CultureInfo.InvariantCulture,
                    "align {0} -> {1}",
                    sourcePaneId,
                    targetPaneId),
                SynchronizedOperationScope.FocusedPane);
            var fallbackReason = sourcePaneSnapshot.HasAbsoluteFrameIdentity && sourcePaneSnapshot.FrameIndex.HasValue
                ? "exact frame seek unavailable"
                : "frame identity unavailable";
            _lastCompareAlignmentStatus = "Last align: time fallback";
            SetPlaybackMessage(string.Format(
                CultureInfo.InvariantCulture,
                "Aligned the {0} pane to the {1} pane using presentation time {2} ({3}).",
                targetSide.ToLowerInvariant(),
                sourceSide.ToLowerInvariant(),
                FormatTime(targetTime),
                fallbackReason));
            LogInfo(string.Format(
                CultureInfo.InvariantCulture,
                "Aligned compare pane {0} to {1} using presentation time {2} ({3}).",
                targetPaneId,
                sourcePaneId,
                FormatTime(targetTime),
                fallbackReason));
            UpdateWorkspacePanePresentation();
            FocusPreferredVideoSurface();
        }

        private async Task JumpToFrameFromInputAsync()
        {
            EndHeldFrameStep();

            if (!_isMediaLoaded || _positionStep <= TimeSpan.Zero)
            {
                return;
            }

            long requestedFrameNumber;
            if (!long.TryParse(FrameNumberTextBox.Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out requestedFrameNumber))
            {
                SetPlaybackMessage("Enter a valid frame number.");
                return;
            }

            var maxFrameIndex = GetMaxFrameIndex();
            var targetFrameIndex = GetFrameIndexFromDisplayedFrameNumber(requestedFrameNumber);
            if (maxFrameIndex >= 0 && targetFrameIndex > maxFrameIndex)
            {
                targetFrameIndex = maxFrameIndex;
            }

            await PausePlaybackAsync();
            await RunWithCacheStatusAsync(
                "Cache: seeking frame...",
                () => _workspaceCoordinator.SeekToFrameAsync(targetFrameIndex, GetRequestedOperationScope()));
            FocusPreferredVideoSurface();
            UpdatePositionDisplay(GetDisplayPosition());
            LogInfo(string.Format(
                CultureInfo.InvariantCulture,
                "Jumped to frame {0} at {1}.",
                GetDisplayedFrameNumber(targetFrameIndex),
                FormatTime(GetDisplayPosition())));
        }

        private async Task JumpToFrameFromInputAsync(string paneId, TextBox frameNumberTextBox)
        {
            EndHeldFrameStep();

            if (frameNumberTextBox == null || !TrySelectPaneForPaneCommand(paneId))
            {
                return;
            }

            var positionStep = GetPanePositionStep(paneId);
            if (positionStep <= TimeSpan.Zero)
            {
                return;
            }

            long requestedFrameNumber;
            if (!long.TryParse(frameNumberTextBox.Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out requestedFrameNumber))
            {
                SetPlaybackMessage("Enter a valid frame number for the selected pane.");
                return;
            }

            var maxFrameIndex = GetMaxFrameIndex(paneId);
            var targetFrameIndex = GetFrameIndexFromDisplayedFrameNumber(requestedFrameNumber);
            if (maxFrameIndex >= 0 && targetFrameIndex > maxFrameIndex)
            {
                targetFrameIndex = maxFrameIndex;
            }

            await PausePlaybackAsync(logAction: false, operationScope: SynchronizedOperationScope.FocusedPane);
            await RunWithCacheStatusAsync(
                "Cache: seeking frame...",
                () => _workspaceCoordinator.SeekToFrameAsync(targetFrameIndex, SynchronizedOperationScope.FocusedPane));
            UpdateWorkspacePanePresentation();
            FocusPreferredVideoSurface();
            LogInfo(string.Format(
                CultureInfo.InvariantCulture,
                "Jumped {0} to frame {1} at {2}.",
                GetComparePaneSideLabel(paneId),
                GetDisplayedFrameNumber(targetFrameIndex),
                FormatTime(GetPaneDisplayPosition(paneId))));
        }

        private void PositionTimer_Tick(object sender, EventArgs e)
        {
            if (!_isMediaLoaded || _isSliderDragActive)
            {
                return;
            }

            UpdatePositionDisplay(GetDisplayPosition());
            if (IsCompareModeEnabled)
            {
                UpdateWorkspacePanePresentation();
            }

            if (ShouldRestartLoopPlaybackAtBoundary(_workspaceCoordinator.CurrentWorkspace))
            {
                _ = RestartLoopPlaybackAsync();
            }
        }

        private async void RecentFileMenuItem_Click(object sender, RoutedEventArgs e)
        {
            var menuItem = sender as MenuItem;
            var recentPath = menuItem != null ? menuItem.Tag as string : null;
            if (string.IsNullOrWhiteSpace(recentPath))
            {
                return;
            }

            if (!File.Exists(recentPath))
            {
                _recentFilesService.Remove(recentPath);
                RefreshRecentFilesMenu();
                LogWarning("Recent file no longer exists: " + GetSafeFileDisplay(recentPath));
                MessageBox.Show(this, "That recent file no longer exists.", "Missing File", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            if (!EnsureRuntimeAvailable())
            {
                return;
            }

            await OpenMediaAsync(recentPath);
        }

        private void ClearRecentFilesMenuItem_Click(object sender, RoutedEventArgs e)
        {
            _recentFilesService.Clear();
            RefreshRecentFilesMenu();
            LogInfo("Recent files list cleared.");
        }

        private void RefreshRecentFilesMenu()
        {
            RecentFilesMenuItem.Items.Clear();

            var recentFiles = _recentFilesService.Load();
            if (recentFiles.Count == 0)
            {
                RecentFilesMenuItem.Items.Add(new MenuItem
                {
                    Header = "No Recent Files",
                    IsEnabled = false
                });
                return;
            }

            for (var i = 0; i < recentFiles.Count; i++)
            {
                var filePath = recentFiles[i];
                var item = new MenuItem
                {
                    Header = string.Format(CultureInfo.InvariantCulture, "_{0} {1}", i + 1, Path.GetFileName(filePath)),
                    Tag = filePath,
                    ToolTip = filePath
                };

                item.Click += RecentFileMenuItem_Click;
                RecentFilesMenuItem.Items.Add(item);
            }

            RecentFilesMenuItem.Items.Add(new Separator());

            var clearItem = new MenuItem
            {
                Header = "_Clear Recent Files"
            };
            clearItem.Click += ClearRecentFilesMenuItem_Click;
            RecentFilesMenuItem.Items.Add(clearItem);
        }

        private void ApplySessionSnapshot(ReviewSessionSnapshot session)
        {
            var mediaInfo = session != null
                ? session.MediaInfo
                : VideoMediaInfo.Empty;

            _isMediaLoaded = session != null && session.IsMediaOpen;
            _isPlaying = session != null && session.PlaybackState == ReviewPlaybackState.Playing;
            _currentFilePath = session != null ? session.CurrentFilePath ?? string.Empty : string.Empty;
            _positionStep = mediaInfo.PositionStep > TimeSpan.Zero
                ? mediaInfo.PositionStep
                : TimeSpan.FromSeconds(1d / DefaultFramesPerSecond);
            _framesPerSecond = mediaInfo.FramesPerSecond > 0
                ? mediaInfo.FramesPerSecond
                : _positionStep > TimeSpan.Zero
                    ? 1.0 / _positionStep.TotalSeconds
                : DefaultFramesPerSecond;
            _mediaDuration = mediaInfo.Duration > TimeSpan.Zero
                ? mediaInfo.Duration
                : TimeSpan.Zero;
        }

        private void UpdateHeader()
        {
            var titlePrefix = _buildVariant.BuildDisplayName;
            if (string.IsNullOrWhiteSpace(_currentFilePath))
            {
                CurrentFileTextBlock.Text = "No file loaded";
                CurrentFileTextBlock.ToolTip = null;
                Title = titlePrefix;
            }
            else
            {
                CurrentFileTextBlock.Text = Path.GetFileName(_currentFilePath);
                CurrentFileTextBlock.ToolTip = _currentFilePath;
                Title = titlePrefix + " - " + Path.GetFileName(_currentFilePath);
            }
        }

        private void UpdateTransportState()
        {
            var canControl = _isMediaLoaded;
            var canPlay = canControl && _buildVariant.SupportsTimedPlayback;

            PlayPauseButton.IsEnabled = canPlay;
            OverlayPlayPauseButton.IsEnabled = canPlay;
            PlayPauseMenuItem.IsEnabled = canPlay;
            RewindButton.IsEnabled = canControl;
            FastForwardButton.IsEnabled = canControl;
            PreviousFrameButton.IsEnabled = canControl;
            NextFrameButton.IsEnabled = canControl;
            CloseVideoMenuItem.IsEnabled = canControl;
            VideoInfoMenuItem.IsEnabled = canControl;
            SetLoopInMenuItem.IsEnabled = canControl;
            SetLoopOutMenuItem.IsEnabled = canControl;
            ClearLoopPointsMenuItem.IsEnabled = canControl;
            PositionSlider.IsEnabled = canControl;
            FrameNumberTextBox.IsEnabled = canControl;
            ToggleFullScreenButton.IsEnabled = canControl;
            OverlayToggleFullScreenButton.IsEnabled = canControl;
            FullscreenControlBar.IsEnabled = canControl;
            UpdatePlayPauseToggleVisuals();
            UpdateWorkspacePanePresentation();
            UpdateLoopUi();

            if (!_isMediaLoaded)
            {
                SetPlaybackMessage(
                    !string.IsNullOrWhiteSpace(_lastMediaErrorMessage)
                        ? "The last action did not complete."
                        : App.HasBundledFfmpegRuntime
                            ? _buildVariant.StatusText
                            : "Bundled playback runtime is missing.");
                SetMediaSummary(string.IsNullOrWhiteSpace(_lastMediaErrorMessage)
                    ? string.Empty
                    : _lastMediaErrorMessage);
                CurrentFrameTextBlock.Text = "Frame --";
                CurrentFrameTextBlock.ToolTip = null;
                TimecodeTextBlock.Text = "--:--:--.--- / --:--:--.---";
                TimecodeTextBlock.ToolTip = null;
                FrameNumberTextBox.Text = string.Empty;
                FrameNumberTextBox.ToolTip = GetFrameNumberInputToolTip();
                ClearPointerCoordinates();
                UpdateCacheStatusFromEngine();
                UpdateFullScreenButtonIcon();
                UpdateWorkspacePanePresentation();
                return;
            }

            if (_buildVariant.SupportsTimedPlayback)
            {
                SetPlaybackMessage(_isPlaying
                    ? "Playing"
                    : "Paused");
            }
            else
            {
                SetPlaybackMessage(_buildVariant.PlaybackCapabilityText);
            }

            SetMediaSummary(string.Format(
                CultureInfo.InvariantCulture,
                "Sample rate {0:0.###} fps | Interval {1}",
                _framesPerSecond,
                FormatStepDuration(_positionStep)));
            MediaSummaryTextBlock.ToolTip = string.Format(
                CultureInfo.InvariantCulture,
                "{0}. Frame rate {1:0.###} fps, frame step {2}, duration {3}. Audio: {4}.",
                "Media summary",
                _framesPerSecond,
                FormatStepDuration(_positionStep),
                FormatTime(_mediaDuration),
                GetAudioTooltipText(_workspaceCoordinator.CurrentSession.MediaInfo));

            UpdateCurrentFrameDisplay(GetDisplayPosition());
            UpdateCacheStatusFromEngine();
            UpdateFullScreenButtonIcon();
            UpdateWorkspacePanePresentation();
        }

        private void UpdatePlayPauseToggleVisuals()
        {
            var icon = FindResource(_isPlaying ? "PauseIcon" : "PlayIcon") as ImageSource;
            if (icon != null)
            {
                PlayPauseIcon.Source = icon;
                OverlayPlayPauseIcon.Source = icon;
            }

            var label = _isPlaying ? "Pause" : "Play";
            PlayPauseButton.ToolTip = label;
            OverlayPlayPauseButton.ToolTip = label;
            PlayPauseMenuItem.Header = _isPlaying ? "_Pause" : "_Play";
        }

        private void UpdatePositionDisplay(TimeSpan currentPosition)
        {
            CurrentPositionTextBlock.Text = FormatTime(currentPosition);
            DurationTextBlock.Text = FormatTime(_mediaDuration);

            _suppressSliderUpdate = true;
            try
            {
                var sliderMaximum = GetSliderMaximumPosition();
                PositionSlider.Maximum = sliderMaximum > TimeSpan.Zero ? sliderMaximum.TotalSeconds : 1.0;
                PositionSlider.Value = Math.Min(currentPosition.TotalSeconds, PositionSlider.Maximum);
            }
            finally
            {
                _suppressSliderUpdate = false;
            }

            UpdateLoopOverlay();

            if (_isMediaLoaded)
            {
                UpdateCurrentFrameDisplay(currentPosition);

                long currentFrameIndex;
                bool isAbsoluteFrameIndex;
                if (!FrameNumberTextBox.IsKeyboardFocusWithin &&
                    TryGetCurrentEngineFrameIndex(out currentFrameIndex, out isAbsoluteFrameIndex))
                {
                    FrameNumberTextBox.Text = isAbsoluteFrameIndex
                        ? GetDisplayedFrameNumber(currentFrameIndex).ToString(CultureInfo.InvariantCulture)
                        : string.Empty;
                }
            }
        }

        private void UpdateCurrentFrameDisplay(TimeSpan currentPosition)
        {
            if (!_isMediaLoaded)
            {
                CurrentFrameTextBlock.Text = "Frame --";
                CurrentFrameTextBlock.ToolTip = null;
                TimecodeTextBlock.Text = "--:--:--.--- / --:--:--.---";
                TimecodeTextBlock.ToolTip = null;
                FrameNumberTextBox.ToolTip = GetFrameNumberInputToolTip();
                return;
            }

            long currentFrameIndex;
            bool isAbsoluteFrameIndex;
            if (!TryGetCurrentEngineFrameIndex(out currentFrameIndex, out isAbsoluteFrameIndex))
            {
                CurrentFrameTextBlock.Text = "Frame --";
                CurrentFrameTextBlock.ToolTip = "No decoded frame identity is currently available from the engine.";
                TimecodeTextBlock.Text = string.Format(CultureInfo.InvariantCulture, "{0} / {1}", FormatTime(currentPosition), FormatTime(_mediaDuration));
                TimecodeTextBlock.ToolTip = "Timeline time is available, but frame identity is not.";
                if (!FrameNumberTextBox.IsKeyboardFocusWithin)
                {
                    FrameNumberTextBox.Text = string.Empty;
                }

                FrameNumberTextBox.ToolTip = "Frame identity is not available yet. Wait for the current frame to resolve, or type a frame number and press Enter.";
                return;
            }

            var currentFrame = GetDisplayedFrameNumber(currentFrameIndex);
            var totalFrames = GetTotalFrameCount();
            var totalFrameDisplay = totalFrames > 0
                ? GetDisplayedTotalFrameValue(totalFrames)
                : -1L;
            var frameDigits = Math.Max(
                4,
                totalFrames > 0
                    ? totalFrameDisplay.ToString(CultureInfo.InvariantCulture).Length
                    : currentFrame.ToString(CultureInfo.InvariantCulture).Length);
            var frameFormat = "D" + frameDigits.ToString(CultureInfo.InvariantCulture);
            var timecode = FormatTimecode(currentFrameIndex);

            if (!isAbsoluteFrameIndex)
            {
                CurrentFrameTextBlock.Text = totalFrames > 0
                    ? string.Format(
                        CultureInfo.InvariantCulture,
                        _buildVariant.UsesZeroIndexedFrameDisplay
                            ? "Frame -- / {0} (pending, 0-index)"
                            : "Frame -- / {0} (pending)",
                        totalFrameDisplay.ToString(frameFormat, CultureInfo.InvariantCulture))
                    : "Frame -- (pending)";
                CurrentFrameTextBlock.ToolTip = _buildVariant.UsesZeroIndexedFrameDisplay
                    ? string.Format(
                        CultureInfo.InvariantCulture,
                        "The visible frame is correct, but the absolute zero-indexed frame number is still pending while the background index finishes. Current segment-local frame: {0}.",
                        currentFrame)
                    : string.Format(
                        CultureInfo.InvariantCulture,
                        "The visible frame is correct, but the absolute frame number is still pending while the background index finishes. Current segment-local frame: {0}.",
                        currentFrame);
                TimecodeTextBlock.Text = string.Format(
                    CultureInfo.InvariantCulture,
                    "{0} / {1}",
                    FormatTime(currentPosition),
                    FormatTime(_mediaDuration));
                TimecodeTextBlock.ToolTip = string.Format(
                    CultureInfo.InvariantCulture,
                    "Timeline time has landed, but frame identity is still pending. Frame-derived timecode {0} uses nominal {1} fps buckets for {2:0.###} fps media.",
                    timecode,
                    GetNominalTimecodeFramesPerSecond(),
                    _framesPerSecond);
                if (!FrameNumberTextBox.IsKeyboardFocusWithin)
                {
                    FrameNumberTextBox.Text = string.Empty;
                }

                FrameNumberTextBox.ToolTip = _buildVariant.UsesZeroIndexedFrameDisplay
                    ? "The visible frame is correct, but the absolute zero-indexed frame number is still pending while the background index finishes. Type a frame number and press Enter once the index is ready."
                    : "The visible frame is correct, but the absolute frame number is still pending while the background index finishes. Type a frame number and press Enter once the index is ready.";
                return;
            }

            if (totalFrames > 0)
            {
                CurrentFrameTextBlock.Text = string.Format(
                    CultureInfo.InvariantCulture,
                    _buildVariant.UsesZeroIndexedFrameDisplay
                        ? "Frame {0} / {1} (0-index)"
                        : "Frame {0} / {1}",
                    currentFrame.ToString(frameFormat, CultureInfo.InvariantCulture),
                    totalFrameDisplay.ToString(frameFormat, CultureInfo.InvariantCulture));
                CurrentFrameTextBlock.ToolTip = _buildVariant.UsesZeroIndexedFrameDisplay
                    ? string.Format(
                        CultureInfo.InvariantCulture,
                        "Current zero-indexed frame {0}. Last zero-indexed frame {1}. Total decoded frames: {2}. Identity: {3}.",
                        currentFrame,
                        totalFrameDisplay,
                        totalFrames,
                        isAbsoluteFrameIndex ? "absolute" : "segment-local")
                    : string.Format(CultureInfo.InvariantCulture, "Current frame {0} of {1}.", currentFrame, totalFrames);
            }
            else
            {
                CurrentFrameTextBlock.Text = string.Format(
                    CultureInfo.InvariantCulture,
                    _buildVariant.UsesZeroIndexedFrameDisplay ? "Frame {0} (0-index)" : "Frame {0}",
                    currentFrame.ToString(frameFormat, CultureInfo.InvariantCulture));
                CurrentFrameTextBlock.ToolTip = _buildVariant.UsesZeroIndexedFrameDisplay
                    ? string.Format(
                        CultureInfo.InvariantCulture,
                        "Current zero-indexed frame {0}. Identity: {1}.",
                        currentFrame,
                        isAbsoluteFrameIndex ? "absolute" : "segment-local")
                    : string.Format(CultureInfo.InvariantCulture, "Current frame {0}.", currentFrame);
            }

            FrameNumberTextBox.ToolTip = totalFrames > 0
                ? _buildVariant.UsesZeroIndexedFrameDisplay
                    ? string.Format(
                        CultureInfo.InvariantCulture,
                        "Current / last zero-indexed frames: {0} / {1}. Identity: {2}. Type a zero-indexed frame number and press Enter.",
                        currentFrame,
                        totalFrameDisplay,
                        isAbsoluteFrameIndex ? "absolute" : "segment-local")
                    : string.Format(CultureInfo.InvariantCulture, "Current / total frames: {0} / {1}. Type a frame number and press Enter.", currentFrame, totalFrames)
                : string.Format(
                    CultureInfo.InvariantCulture,
                    "Current frame: {0}. Identity: {1}. {2}",
                    currentFrame,
                    isAbsoluteFrameIndex ? "absolute" : "segment-local",
                    GetFrameNumberInputToolTip());

            TimecodeTextBlock.Text = string.Format(
                CultureInfo.InvariantCulture,
                "{0} / {1}",
                FormatTime(currentPosition),
                FormatTime(_mediaDuration));
            TimecodeTextBlock.ToolTip = string.Format(
                CultureInfo.InvariantCulture,
                "Current frame timestamp from the engine. Frame-derived timecode {0} uses nominal {1} fps buckets for {2:0.###} fps media.",
                timecode,
                GetNominalTimecodeFramesPerSecond(),
                _framesPerSecond);
        }

        private long GetCurrentFrameIndex(TimeSpan currentPosition)
        {
            long frameIndex;
            bool isAbsoluteFrameIndex;
            if (TryGetCurrentEngineFrameIndex(out frameIndex, out isAbsoluteFrameIndex))
            {
                return frameIndex;
            }

            return 0L;
        }

        private bool TryGetCurrentEngineFrameIndex(out long frameIndex, out bool isAbsoluteFrameIndex)
        {
            frameIndex = 0L;
            isAbsoluteFrameIndex = false;

            var enginePosition = _workspaceCoordinator.CurrentSession.Position;
            if (enginePosition == null || !enginePosition.FrameIndex.HasValue)
            {
                return false;
            }

            frameIndex = Math.Max(0L, enginePosition.FrameIndex.Value);
            isAbsoluteFrameIndex = enginePosition.IsFrameIndexAbsolute;
            return true;
        }

        private long GetDisplayedFrameNumber(long frameIndex)
        {
            var clampedFrameIndex = Math.Max(0L, frameIndex);
            return _buildVariant.UsesZeroIndexedFrameDisplay
                ? clampedFrameIndex
                : clampedFrameIndex + 1L;
        }

        private long GetFrameIndexFromDisplayedFrameNumber(long displayedFrameNumber)
        {
            return _buildVariant.UsesZeroIndexedFrameDisplay
                ? Math.Max(0L, displayedFrameNumber)
                : Math.Max(1L, displayedFrameNumber) - 1L;
        }

        private long GetDisplayedTotalFrameValue(long totalFrameCount)
        {
            if (totalFrameCount <= 0L)
            {
                return -1L;
            }

            return _buildVariant.UsesZeroIndexedFrameDisplay
                ? totalFrameCount - 1L
                : totalFrameCount;
        }

        private string GetFrameNumberInputToolTip()
        {
            return _buildVariant.UsesZeroIndexedFrameDisplay
                ? "Type a zero-indexed frame number and press Enter."
                : "Type a frame number and press Enter.";
        }

        private static string GetAudioSummaryText(VideoMediaInfo mediaInfo)
        {
            if (mediaInfo == null || !mediaInfo.HasAudioStream)
            {
                return "none";
            }

            return mediaInfo.IsAudioPlaybackAvailable
                ? "enabled"
                : "stream present, playback unavailable";
        }

        private static string GetAudioTooltipText(VideoMediaInfo mediaInfo)
        {
            if (mediaInfo == null || !mediaInfo.HasAudioStream)
            {
                return "No audio stream detected";
            }

            var codec = string.IsNullOrWhiteSpace(mediaInfo.AudioCodecName)
                ? "unknown codec"
                : mediaInfo.AudioCodecName;
            var format = mediaInfo.AudioSampleRate > 0 && mediaInfo.AudioChannelCount > 0
                ? string.Format(
                    CultureInfo.InvariantCulture,
                    "{0}, {1} Hz, {2} channel(s)",
                    codec,
                    mediaInfo.AudioSampleRate,
                    mediaInfo.AudioChannelCount)
                : codec;

            return mediaInfo.IsAudioPlaybackAvailable
                ? format + " available for playback"
                : format + " detected, but playback is unavailable";
        }

        private long GetTotalFrameCount()
        {
            var ffmpegEngine = GetFocusedFfmpegEngine();
            if (ffmpegEngine != null && ffmpegEngine.IsGlobalFrameIndexAvailable && ffmpegEngine.IndexedFrameCount > 0L)
            {
                return ffmpegEngine.IndexedFrameCount;
            }

            return GetMaxFrameIndex() + 1L;
        }

        private TimeSpan ClampPosition(TimeSpan target)
        {
            if (target < TimeSpan.Zero)
            {
                return TimeSpan.Zero;
            }

            if (_mediaDuration > TimeSpan.Zero && target > _mediaDuration)
            {
                return _mediaDuration;
            }

            return target;
        }

        private TimeSpan ClampPositionForPane(string paneId, TimeSpan target)
        {
            if (target < TimeSpan.Zero)
            {
                return TimeSpan.Zero;
            }

            var mediaDuration = GetPaneMediaDuration(paneId);
            if (mediaDuration > TimeSpan.Zero && target > mediaDuration)
            {
                return mediaDuration;
            }

            return target;
        }

        private TimeSpan GetSliderMaximumPosition()
        {
            var ffmpegEngine = GetFocusedFfmpegEngine();
            if (ffmpegEngine != null &&
                ffmpegEngine.IsGlobalFrameIndexAvailable &&
                ffmpegEngine.LastIndexedFramePresentationTime > TimeSpan.Zero)
            {
                return ffmpegEngine.LastIndexedFramePresentationTime;
            }

            return _mediaDuration;
        }

        private long GetMaxFrameIndex()
        {
            var ffmpegEngine = GetFocusedFfmpegEngine();
            if (ffmpegEngine != null && ffmpegEngine.IsGlobalFrameIndexAvailable && ffmpegEngine.IndexedFrameCount > 0L)
            {
                return ffmpegEngine.IndexedFrameCount - 1L;
            }

            if (_mediaDuration <= TimeSpan.Zero || _positionStep <= TimeSpan.Zero)
            {
                return -1;
            }

            return Math.Max(0L, (_mediaDuration.Ticks + _positionStep.Ticks - 1L) / _positionStep.Ticks - 1L);
        }

        private static bool IsSupportedVideoFile(string filePath)
        {
            var extension = Path.GetExtension(filePath);
            if (string.IsNullOrWhiteSpace(extension))
            {
                return false;
            }

            return extension.Equals(".avi", StringComparison.OrdinalIgnoreCase)
                || extension.Equals(".mov", StringComparison.OrdinalIgnoreCase)
                || extension.Equals(".m4v", StringComparison.OrdinalIgnoreCase)
                || extension.Equals(".mp4", StringComparison.OrdinalIgnoreCase)
                || extension.Equals(".mkv", StringComparison.OrdinalIgnoreCase)
                || extension.Equals(".wmv", StringComparison.OrdinalIgnoreCase);
        }

        private static string FormatTime(TimeSpan value)
        {
            return string.Format(
                CultureInfo.InvariantCulture,
                "{0:00}:{1:00}:{2:00}.{3:000}",
                (int)value.TotalHours,
                value.Minutes,
                value.Seconds,
                value.Milliseconds);
        }

        private static string FormatStepDuration(TimeSpan value)
        {
            return value > TimeSpan.Zero
                ? string.Format(CultureInfo.InvariantCulture, "{0:0.###} ms", value.TotalMilliseconds)
                : "--";
        }

        private int GetNominalTimecodeFramesPerSecond()
        {
            if (_framesPerSecond <= 0)
            {
                return 30;
            }

            if (Math.Abs(_framesPerSecond - 23.976) < 0.05)
            {
                return 24;
            }

            if (Math.Abs(_framesPerSecond - 29.97) < 0.05)
            {
                return 30;
            }

            if (Math.Abs(_framesPerSecond - 59.94) < 0.05)
            {
                return 60;
            }

            return Math.Max(1, (int)Math.Round(_framesPerSecond, MidpointRounding.AwayFromZero));
        }

        private string FormatTimecode(long frameIndex)
        {
            var clampedFrameIndex = Math.Max(0L, frameIndex);
            var nominalFramesPerSecond = GetNominalTimecodeFramesPerSecond();
            var frames = clampedFrameIndex % nominalFramesPerSecond;
            var totalSeconds = clampedFrameIndex / nominalFramesPerSecond;
            var hours = totalSeconds / 3600L;
            var minutes = (totalSeconds % 3600L) / 60L;
            var seconds = totalSeconds % 60L;

            return string.Format(
                CultureInfo.InvariantCulture,
                "{0:00}:{1:00}:{2:00}:{3:00}",
                hours,
                minutes,
                seconds,
                frames);
        }

        private void ReviewWorkspaceCoordinator_WorkspaceChanged(object sender, ReviewWorkspaceChangedEventArgs e)
        {
            if (!Dispatcher.CheckAccess())
            {
                Dispatcher.BeginInvoke(new Action<object, ReviewWorkspaceChangedEventArgs>(ReviewWorkspaceCoordinator_WorkspaceChanged), sender, e);
                return;
            }

            var previousSession = e != null ? e.PreviousWorkspace.FocusedSession : ReviewSessionSnapshot.Empty;
            var currentSession = e != null ? e.CurrentWorkspace.FocusedSession : ReviewSessionSnapshot.Empty;
            var wasMediaLoaded = previousSession.IsMediaOpen;
            var shouldRestartLoopPlayback = ShouldRestartLoopPlayback(e);
            ApplySessionSnapshot(currentSession);
            UpdateWorkspacePanePresentation();

            var focusedEngine = GetFocusedPaneEngine();
            var engineErrorMessage = !_isMediaLoaded && focusedEngine != null
                ? SanitizeSensitiveText(focusedEngine.LastErrorMessage)
                : string.Empty;
            if (!string.IsNullOrWhiteSpace(engineErrorMessage) && !_isMediaLoaded)
            {
                EndHeldFrameStep();
                _lastMediaErrorMessage = engineErrorMessage;
                ApplySessionSnapshot(
                    new ReviewSessionSnapshot(
                        currentSession.SessionId,
                        currentSession.DisplayLabel,
                        ReviewPlaybackState.Closed,
                        currentSession.CurrentFilePath,
                        VideoMediaInfo.Empty,
                        ReviewPosition.Empty));
                ClearPaneSurface(GetFocusedPaneId());
                UpdateHeader();
                UpdatePositionDisplay(TimeSpan.Zero);
                UpdateTransportState();
                SetPlaybackMessage("Playback failed.");
                SetMediaSummary(_lastMediaErrorMessage);
                if (wasMediaLoaded)
                {
                    LogError("Playback failed: " + _lastMediaErrorMessage);
                }
                return;
            }

            if (_isPlaying)
            {
                EndHeldFrameStep();
            }

            if (!_isMediaLoaded)
            {
                UpdateHeader();
                UpdateTransportState();
                return;
            }

            _lastMediaErrorMessage = string.Empty;
            UpdateHeader();
            UpdatePositionDisplay(currentSession.Position.PresentationTime);
            UpdateTransportState();

            if (shouldRestartLoopPlayback)
            {
                _ = RestartLoopPlaybackAsync();
                return;
            }

            if (!wasMediaLoaded)
            {
                Activate();
                FocusPreferredVideoSurface();
            }
        }

        private void ReviewWorkspaceCoordinator_PreparationStateChanged(object sender, ReviewWorkspacePreparationChangedEventArgs e)
        {
            if (!Dispatcher.CheckAccess())
            {
                Dispatcher.BeginInvoke(
                    new Action<object, ReviewWorkspacePreparationChangedEventArgs>(ReviewWorkspaceCoordinator_PreparationStateChanged),
                    sender,
                    e);
                return;
            }

            ApplyWorkspacePreparationState(e != null
                ? e.CurrentState
                : _workspaceCoordinator.CurrentPreparationState);
        }

        private void VideoReviewEngine_FramePresented(object sender, FramePresentedEventArgs e)
        {
            if (!Dispatcher.CheckAccess())
            {
                Dispatcher.BeginInvoke(new Action<object, FramePresentedEventArgs>(VideoReviewEngine_FramePresented), sender, e);
                return;
            }

            var imageSource = e != null ? WpfFrameBufferPresenter.CreateBitmapSource(e.FrameBuffer) : null;
            if (ReferenceEquals(sender, _compareVideoReviewEngine))
            {
                CompareVideoSurface.Source = imageSource;
                return;
            }

            CustomVideoSurface.Source = imageSource;
        }

        private void UseGpuAccelerationMenuItem_Checked(object sender, RoutedEventArgs e)
        {
            SetGpuAccelerationPreference(true);
        }

        private void UseGpuAccelerationMenuItem_Unchecked(object sender, RoutedEventArgs e)
        {
            SetGpuAccelerationPreference(false);
        }

        private void SetGpuAccelerationPreference(bool useGpuAcceleration)
        {
            if (_ffmpegReviewEngineOptionsProvider.UseGpuAcceleration == useGpuAcceleration)
            {
                return;
            }

            _ffmpegReviewEngineOptionsProvider.SetUseGpuAcceleration(useGpuAcceleration);
            SetPlaybackMessage(useGpuAcceleration
                ? "GPU acceleration enabled for newly opened media."
                : "GPU acceleration disabled for newly opened media.");
            LogInfo(useGpuAcceleration
                ? "GPU acceleration preference enabled. Reopen media to apply the change."
                : "GPU acceleration preference disabled. Reopen media to apply the change.");
            UpdateCacheStatusFromEngine();
        }

        private void VideoPane_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            var paneId = (sender as FrameworkElement)?.Tag as string;
            if (!string.IsNullOrWhiteSpace(paneId))
            {
                TrySelectPaneForShell(paneId);
                UpdateWorkspacePanePresentation();
                FocusPreferredVideoSurface();
            }

            if (e.ClickCount >= 2 && _isMediaLoaded)
            {
                ToggleFullScreen();
                e.Handled = true;
            }
        }

        private void VideoSurfaceHost_MouseMove(object sender, MouseEventArgs e)
        {
            var host = sender as FrameworkElement;
            if (host == null)
            {
                ClearPointerCoordinates();
                return;
            }

            var paneId = host.Tag as string;
            if (string.IsNullOrWhiteSpace(paneId))
            {
                ClearPointerCoordinates();
                return;
            }

            var geometry = GetFrameSurfaceGeometry(paneId);
            int sourcePixelX;
            int sourcePixelY;
            if (!geometry.TryMapPointToSourcePixel(e.GetPosition(host), out sourcePixelX, out sourcePixelY))
            {
                ClearPointerCoordinates();
                return;
            }

            var paneLabel = string.Equals(paneId, ComparePaneId, StringComparison.Ordinal)
                ? "Compare"
                : "Primary";
            SetPointerCoordinates(string.Format(
                CultureInfo.InvariantCulture,
                "Pixel: {0} ({1},{2})",
                paneLabel,
                sourcePixelX,
                sourcePixelY));
        }

        private void VideoSurfaceHost_MouseLeave(object sender, MouseEventArgs e)
        {
            ClearPointerCoordinates();
        }

        private void ToggleFullScreen()
        {
            if (!_isMediaLoaded)
            {
                return;
            }

            if (_isFullScreen)
            {
                ExitFullScreen();
            }
            else
            {
                EnterFullScreen();
            }
        }

        private void EnterFullScreen()
        {
            if (_isFullScreen)
            {
                return;
            }

            _restoreWindowState = WindowState;
            _restoreWindowStyle = WindowStyle;
            _restoreResizeMode = ResizeMode;
            _restoreTopmost = Topmost;

            _isFullScreen = true;
            WindowState = WindowState.Normal;
            WindowStyle = WindowStyle.None;
            ResizeMode = ResizeMode.NoResize;
            Topmost = true;
            WindowState = WindowState.Maximized;

            UpdateFullScreenVisualState();
            LogInfo("Entered full screen.");
        }

        private void ExitFullScreen()
        {
            if (!_isFullScreen)
            {
                return;
            }

            _isFullScreen = false;
            WindowState = WindowState.Normal;
            WindowStyle = _restoreWindowStyle;
            ResizeMode = _restoreResizeMode;
            Topmost = _restoreTopmost;
            WindowState = _restoreWindowState;

            UpdateFullScreenVisualState();
            LogInfo("Exited full screen.");
        }

        private void UpdateFullScreenVisualState()
        {
            MenuPanel.Visibility = _isFullScreen ? Visibility.Collapsed : Visibility.Visible;
            HeaderPanel.Visibility = _isFullScreen ? Visibility.Collapsed : Visibility.Visible;
            ControlPanel.Visibility = _isFullScreen ? Visibility.Collapsed : Visibility.Visible;
            StatusPanelContainer.Visibility = _isFullScreen ? Visibility.Collapsed : Visibility.Visible;
            FullscreenControlBar.Visibility = _isFullScreen ? Visibility.Visible : Visibility.Collapsed;

            RootGrid.Margin = new Thickness(0);
            HeaderPanel.Margin = _isFullScreen ? new Thickness(0) : new Thickness(10, 10, 10, 0);
            VideoPanel.Margin = _isFullScreen ? new Thickness(0) : new Thickness(10, 8, 10, 0);
            ControlPanel.Margin = _isFullScreen ? new Thickness(0) : new Thickness(10, 8, 10, 0);
            StatusPanelContainer.Margin = _isFullScreen ? new Thickness(0) : new Thickness(10, 8, 10, 10);
            VideoPanel.CornerRadius = new CornerRadius(0);

            UpdateFullScreenButtonIcon();
            UpdateWindowStateButtonVisuals();
            UpdateWindowFrameChrome();
        }

        private void UpdateFullScreenButtonIcon()
        {
            var resourceKey = _isFullScreen ? "ExitFullScreenIcon" : "FullScreenIcon";
            var icon = FindResource(resourceKey) as ImageSource;
            if (icon == null)
            {
                return;
            }

            ToggleFullScreenIcon.Source = icon;
            OverlayToggleFullScreenIcon.Source = icon;

            var toolTip = _isFullScreen ? "Exit Full Screen" : "Enter Full Screen";
            ToggleFullScreenButton.ToolTip = toolTip;
            OverlayToggleFullScreenButton.ToolTip = toolTip;
        }

        private void ClearPaneSurface(string paneId)
        {
            if (string.Equals(paneId, ComparePaneId, StringComparison.Ordinal))
            {
                CompareVideoSurface.Source = null;
                ClearPointerCoordinates();
                return;
            }

            CustomVideoSurface.Source = null;
            ClearPointerCoordinates();
        }

        private void ResetMediaState(bool clearFilePath, bool clearErrorMessage)
        {
            _isFrameStepInProgress = false;
            _isCacheStatusActive = false;
            _heldFrameStepDirection = 0;
            _frameStepRepeatTimer.Stop();
            _isSliderDragActive = false;
            _isSliderScrubSeekInFlight = false;
            _hasPendingSliderScrubTarget = false;
            _sliderScrubTimer.Stop();
            _isPaneSliderDragActive = false;
            _isPaneSliderScrubSeekInFlight = false;
            _hasPendingPaneSliderScrubTarget = false;
            _paneSliderScrubTimer.Stop();
            _activePaneSliderPaneId = string.Empty;
            _activeLoopPlaybackPaneId = null;
            if (!IsCompareModeEnabled)
            {
                _activeLoopCommandPaneId = null;
            }
            _suppressLoopRestart = false;
            _isLoopRestartInFlight = false;
            ResetCompareAlignmentStatus();
            ClearPaneSurface(GetFocusedPaneId());
            ApplySessionSnapshot(_workspaceCoordinator.Reset(clearFilePath ? string.Empty : _currentFilePath));

            if (clearErrorMessage)
            {
                _lastMediaErrorMessage = string.Empty;
            }

            UpdateHeader();
            UpdatePositionDisplay(TimeSpan.Zero);
            UpdateTransportState();
            UpdateWorkspacePanePresentation();
        }

        private void SetPlaybackMessage(string message)
        {
            PlaybackStateTextBlock.Text = message;
            PlaybackStateTextBlock.ToolTip = message;
        }

        private void SetMediaSummary(string message)
        {
            MediaSummaryTextBlock.Text = message;
            MediaSummaryTextBlock.ToolTip = message;
        }

        private async Task RunWithCacheStatusAsync(string activeMessage, Func<Task> operation)
        {
            if (operation == null)
            {
                throw new ArgumentNullException(nameof(operation));
            }

            BeginCacheStatus(activeMessage);
            await Dispatcher.Yield(DispatcherPriority.Render);
            try
            {
                await operation();
            }
            finally
            {
                _isCacheStatusActive = false;
                UpdateCacheStatusFromEngine();
            }
        }

        private void BeginCacheStatus(string message)
        {
            _isCacheStatusActive = true;
            SetCacheStatus(
                string.IsNullOrWhiteSpace(message) ? "Cache: active" : message,
                "The engine is decoding and refreshing the local frame cache.",
                true);
        }

        private void ApplyWorkspacePreparationState(ReviewWorkspacePreparationState state)
        {
            var currentState = state ?? ReviewWorkspacePreparationState.Idle;
            switch (currentState.Phase)
            {
                case ReviewWorkspacePreparationPhase.Opening:
                    BeginCacheStatus("Cache: indexing and warming...");
                    return;
                case ReviewWorkspacePreparationPhase.PreparingFirstFrame:
                    BeginCacheStatus("Cache: warming first frame...");
                    return;
                case ReviewWorkspacePreparationPhase.Idle:
                case ReviewWorkspacePreparationPhase.Ready:
                case ReviewWorkspacePreparationPhase.Failed:
                default:
                    _isCacheStatusActive = false;
                    UpdateCacheStatusFromEngine();
                    return;
            }
        }

        private void UpdateCacheStatusFromEngine()
        {
            if (_isCacheStatusActive)
            {
                return;
            }

            if (!_isMediaLoaded)
            {
                SetCacheStatus("Cache: idle", "Open media to populate the decoded-frame cache.", false);
                return;
            }

            var focusedEngine = GetFocusedPaneEngine();
            var ffmpegEngine = focusedEngine as FfmpegReviewEngine;
            if (ffmpegEngine == null)
            {
                SetCacheStatus("Cache: unavailable", "Cache details are not available for this engine.", false);
                return;
            }

            var forwardCount = Math.Max(0, ffmpegEngine.ForwardCachedFrameCount);
            var previousCount = Math.Max(0, ffmpegEngine.PreviousCachedFrameCount);
            var approximateCacheMegabytes = ffmpegEngine.ApproximateCachedFrameBytes / 1048576d;
            var cacheBudgetMegabytes = ffmpegEngine.DecodedFrameCacheBudgetBytes / 1048576d;
            var positionIdentity = focusedEngine != null &&
                                   focusedEngine.Position != null &&
                                   focusedEngine.Position.IsFrameIndexAbsolute
                ? "absolute frame ready"
                : "frame number pending index";
            var message = string.Format(
                CultureInfo.InvariantCulture,
                ffmpegEngine.IsGlobalFrameIndexBuildInProgress
                    ? "Cache: indexing ({0} back / {1} ahead, {2})"
                    : "Cache: ready ({0} back / {1} ahead, {2})",
                previousCount,
                forwardCount,
                ffmpegEngine.IsGpuActive ? "GPU" : "CPU");
            var tooltip = string.Format(
                CultureInfo.InvariantCulture,
                "Backend: {0}. GPU status: {1}. Fallback: {2}. Queue depth: {3}. Index: {4}. Frame identity: {5}. Review cache budget is {6:0.0} MiB and currently uses about {7:0.0} MiB with up to {8} prior and {9} forward decoded frames. Last refill: {10} ({11:0.0} ms, {12}). Timeline seeks show the landed frame first.",
                string.IsNullOrWhiteSpace(ffmpegEngine.ActiveDecodeBackend) ? "(unknown)" : ffmpegEngine.ActiveDecodeBackend,
                string.IsNullOrWhiteSpace(ffmpegEngine.GpuCapabilityStatus) ? "(none)" : ffmpegEngine.GpuCapabilityStatus,
                string.IsNullOrWhiteSpace(ffmpegEngine.GpuFallbackReason) ? "(none)" : ffmpegEngine.GpuFallbackReason,
                ffmpegEngine.OperationalQueueDepth,
                ffmpegEngine.GlobalFrameIndexStatus,
                positionIdentity,
                cacheBudgetMegabytes,
                approximateCacheMegabytes,
                ffmpegEngine.MaxPreviousCachedFrameCount,
                ffmpegEngine.MaxForwardCachedFrameCount,
                string.IsNullOrWhiteSpace(ffmpegEngine.LastCacheRefillReason) ? "(none)" : ffmpegEngine.LastCacheRefillReason,
                ffmpegEngine.LastCacheRefillMilliseconds,
                string.IsNullOrWhiteSpace(ffmpegEngine.LastCacheRefillMode) ? "none" : ffmpegEngine.LastCacheRefillMode);
            SetCacheStatus(message, tooltip, false);
        }

        private void SetCacheStatus(string message, string tooltip, bool isActive)
        {
            CacheStatusTextBlock.Text = message;
            CacheStatusTextBlock.ToolTip = tooltip;

            var brush = FindResource(isActive ? "AccentBrush" : "MutedBrush") as Brush;
            if (brush != null)
            {
                CacheStatusTextBlock.Foreground = brush;
            }
        }

        private static bool TryGetModifiedFrameStepCount(ModifierKeys modifiers, out int frameStepCount)
        {
            switch (modifiers)
            {
                case ModifierKeys.Control:
                    frameStepCount = ControlModifiedFrameStep;
                    return true;
                case ModifierKeys.Shift:
                    frameStepCount = ShiftModifiedFrameStep;
                    return true;
                default:
                    frameStepCount = 0;
                    return false;
            }
        }

        private void QueueSliderScrub(TimeSpan target)
        {
            _pendingSliderScrubTarget = ClampPosition(target);
            _hasPendingSliderScrubTarget = true;
            if (!_sliderScrubTimer.IsEnabled)
            {
                _sliderScrubTimer.Start();
            }
        }

        private void SliderScrubTimer_Tick(object sender, EventArgs e)
        {
            if (!_isMediaLoaded)
            {
                _sliderScrubTimer.Stop();
                _hasPendingSliderScrubTarget = false;
                return;
            }

            if (!_isSliderDragActive)
            {
                _hasPendingSliderScrubTarget = false;
                if (!_isSliderScrubSeekInFlight)
                {
                    _sliderScrubTimer.Stop();
                }

                return;
            }

            if (_isSliderScrubSeekInFlight || !_hasPendingSliderScrubTarget)
            {
                if (!_isSliderScrubSeekInFlight)
                {
                    _sliderScrubTimer.Stop();
                }

                return;
            }

            _hasPendingSliderScrubTarget = false;
            DispatchSliderScrubSeek(_pendingSliderScrubTarget);
        }

        private void DispatchSliderScrubSeek(TimeSpan target)
        {
            if (_isSliderScrubSeekInFlight)
            {
                return;
            }

            _isSliderScrubSeekInFlight = true;
            _activeSliderScrubSeekTask = ExecuteSliderScrubSeekAsync(target);
        }

        private async Task ExecuteSliderScrubSeekAsync(TimeSpan target)
        {
            try
            {
                await SeekToAsync(target, diagnosticSource: null, operationScope: null);
            }
            catch (Exception ex)
            {
                var sanitizedMessage = SanitizeSensitiveText(ex.Message);
                SetPlaybackMessage("Timeline scrub failed.");
                SetMediaSummary(sanitizedMessage);
                LogError("Timeline scrub failed: " + sanitizedMessage);
            }
            finally
            {
                _isSliderScrubSeekInFlight = false;
                if (!_isSliderDragActive && !_hasPendingSliderScrubTarget)
                {
                    _sliderScrubTimer.Stop();
                }
            }
        }

        private async Task AwaitSliderScrubIdleAsync()
        {
            var pendingTask = _activeSliderScrubSeekTask;
            if (pendingTask == null)
            {
                return;
            }

            await pendingTask;
        }

        private void QueuePaneSliderScrub(string paneId, TimeSpan target)
        {
            _activePaneSliderPaneId = paneId ?? string.Empty;
            _pendingPaneSliderScrubTarget = ClampPositionForPane(paneId, target);
            _hasPendingPaneSliderScrubTarget = true;
            if (!_paneSliderScrubTimer.IsEnabled)
            {
                _paneSliderScrubTimer.Start();
            }
        }

        private void PaneSliderScrubTimer_Tick(object sender, EventArgs e)
        {
            ReviewWorkspacePaneSnapshot paneSnapshot;
            var workspaceSnapshot = _workspaceCoordinator.GetWorkspaceSnapshot();
            if (string.IsNullOrWhiteSpace(_activePaneSliderPaneId) ||
                workspaceSnapshot == null ||
                !workspaceSnapshot.TryGetPane(_activePaneSliderPaneId, out paneSnapshot) ||
                !PaneHasLoadedMedia(paneSnapshot))
            {
                _paneSliderScrubTimer.Stop();
                _hasPendingPaneSliderScrubTarget = false;
                return;
            }

            if (!_isPaneSliderDragActive)
            {
                _hasPendingPaneSliderScrubTarget = false;
                if (!_isPaneSliderScrubSeekInFlight)
                {
                    _paneSliderScrubTimer.Stop();
                }

                return;
            }

            if (_isPaneSliderScrubSeekInFlight || !_hasPendingPaneSliderScrubTarget)
            {
                if (!_isPaneSliderScrubSeekInFlight)
                {
                    _paneSliderScrubTimer.Stop();
                }

                return;
            }

            _hasPendingPaneSliderScrubTarget = false;
            DispatchPaneSliderScrubSeek(_activePaneSliderPaneId, _pendingPaneSliderScrubTarget);
        }

        private void DispatchPaneSliderScrubSeek(string paneId, TimeSpan target)
        {
            if (_isPaneSliderScrubSeekInFlight)
            {
                return;
            }

            _isPaneSliderScrubSeekInFlight = true;
            _activePaneSliderScrubSeekTask = ExecutePaneSliderScrubSeekAsync(paneId, target);
        }

        private async Task ExecutePaneSliderScrubSeekAsync(string paneId, TimeSpan target)
        {
            try
            {
                if (!TrySelectPaneForPaneCommand(paneId))
                {
                    return;
                }

                await SeekPaneToAsync(paneId, target, diagnosticSource: null);
            }
            catch (Exception ex)
            {
                var sanitizedMessage = SanitizeSensitiveText(ex.Message);
                SetPlaybackMessage("Pane timeline scrub failed.");
                SetMediaSummary(sanitizedMessage);
                LogError("Pane timeline scrub failed: " + sanitizedMessage);
            }
            finally
            {
                _isPaneSliderScrubSeekInFlight = false;
                if (!_isPaneSliderDragActive && !_hasPendingPaneSliderScrubTarget)
                {
                    _paneSliderScrubTimer.Stop();
                }
            }
        }

        private async Task AwaitPaneSliderScrubIdleAsync()
        {
            var pendingTask = _activePaneSliderScrubSeekTask;
            if (pendingTask == null)
            {
                return;
            }

            await pendingTask;
        }

        private async Task CommitPaneSliderSeekAsync(string paneId, string interactionName, TimeSpan target)
        {
            if (!TrySelectPaneForPaneCommand(paneId))
            {
                return;
            }

            LogInfo(string.Format(
                CultureInfo.InvariantCulture,
                "{0} timeline {1} seek requested from pane slider at {2}.",
                GetComparePaneSideLabel(paneId),
                interactionName,
                FormatTime(ClampPositionForPane(paneId, target))));

            await SeekPaneToAsync(
                paneId,
                target,
                string.Format(CultureInfo.InvariantCulture, "{0} timeline {1}", GetComparePaneSideLabel(paneId), interactionName));
            FocusPreferredVideoSurface();
        }

        private FrameSurfaceGeometry GetFrameSurfaceGeometry(string paneId)
        {
            FrameworkElement host;
            Image image;
            if (!TryGetVideoSurfaceElements(paneId, out host, out image))
            {
                return FrameSurfaceGeometry.Empty;
            }

            var bitmapSource = image.Source as BitmapSource;
            return bitmapSource == null
                ? FrameSurfaceGeometry.Empty
                : FrameSurfaceGeometry.Create(
                    new Size(host.ActualWidth, host.ActualHeight),
                    bitmapSource.PixelWidth,
                    bitmapSource.PixelHeight,
                    bitmapSource.Width,
                    bitmapSource.Height);
        }

        private bool TryGetVideoSurfaceElements(string paneId, out FrameworkElement host, out Image image)
        {
            if (string.Equals(paneId, ComparePaneId, StringComparison.Ordinal))
            {
                host = CompareVideoSurfaceHost;
                image = CompareVideoSurface;
                return host != null && image != null;
            }

            host = CustomVideoSurfaceHost;
            image = CustomVideoSurface;
            return host != null && image != null;
        }

        private void SetPointerCoordinates(string message)
        {
            var displayMessage = string.IsNullOrWhiteSpace(message) ? "Pixel: --" : message;
            PointerCoordinatesTextBlock.Text = displayMessage;
            PointerCoordinatesTextBlock.ToolTip = string.IsNullOrWhiteSpace(message)
                ? "Pixel location within the displayed video frame."
                : displayMessage + " within the displayed video frame.";
        }

        private void ClearPointerCoordinates()
        {
            SetPointerCoordinates(null);
        }

        private void SetLoopMarker(LoopPlaybackMarkerEndpoint endpoint)
        {
            if (!_isMediaLoaded)
            {
                return;
            }

            var workspaceSnapshot = _workspaceCoordinator.GetWorkspaceSnapshot();
            var loopContextPaneId = GetLoopCommandContextPaneId(workspaceSnapshot);
            var endpointLabel = endpoint == LoopPlaybackMarkerEndpoint.In ? "in" : "out";
            if (!string.IsNullOrWhiteSpace(loopContextPaneId))
            {
                var paneRange = _workspaceCoordinator.SetPaneLoopMarker(loopContextPaneId, endpoint);
                UpdateLoopUi();

                var paneLabel = string.Equals(loopContextPaneId, ComparePaneId, StringComparison.Ordinal)
                    ? "Compare pane"
                    : "Primary pane";
                var paneAnchor = endpoint == LoopPlaybackMarkerEndpoint.In
                    ? (paneRange != null ? paneRange.LoopIn : null)
                    : (paneRange != null ? paneRange.LoopOut : null);
                if (paneRange != null && paneRange.IsInvalidRange)
                {
                    SetPlaybackMessage(string.Format(
                        CultureInfo.InvariantCulture,
                        "{0} loop points are invalid. Set loop out after loop in, or clear the pane range.",
                        paneLabel));
                }
                else if (paneAnchor != null && paneAnchor.IsPending)
                {
                    SetPlaybackMessage(string.Format(
                        CultureInfo.InvariantCulture,
                        "{0} loop {1} set pending at {2}. Pane-local repeat will wait for exact frame identity.",
                        paneLabel,
                        endpointLabel,
                        FormatTime(paneAnchor.PresentationTime)));
                }
                else if (paneAnchor != null)
                {
                    SetPlaybackMessage(string.Format(
                        CultureInfo.InvariantCulture,
                        "{0} loop {1} set at {2}.",
                        paneLabel,
                        endpointLabel,
                        FormatTime(paneAnchor.PresentationTime)));
                }

                LogInfo(string.Format(
                    CultureInfo.InvariantCulture,
                    "Set {0} loop {1}.",
                    paneLabel.ToLowerInvariant(),
                    endpointLabel));
                return;
            }

            var scope = GetRequestedOperationScope();
            _workspaceCoordinator.SetSharedLoopMarker(endpoint, scope);
            UpdateLoopUi();

            var evaluation = EvaluateSharedLoopRange(_workspaceCoordinator.GetWorkspaceSnapshot(), scope);
            var focusedPaneRange = evaluation.FocusedPaneRange;
            var anchor = endpoint == LoopPlaybackMarkerEndpoint.In
                ? (focusedPaneRange != null ? focusedPaneRange.LoopIn : null)
                : (focusedPaneRange != null ? focusedPaneRange.LoopOut : null);
            if (evaluation.IsInvalidRange)
            {
                SetPlaybackMessage("Loop points are invalid. Set loop out after loop in, or clear the range.");
            }
            else if (evaluation.MissingTargetPaneRanges)
            {
                SetPlaybackMessage("Loop points were captured for a different pane scope. Reset them for the current transport scope.");
            }
            else if (anchor != null && anchor.IsPending)
            {
                SetPlaybackMessage(string.Format(
                    CultureInfo.InvariantCulture,
                    "Loop {0} set pending at {1}. A/B repeat will wait for exact frame identity.",
                    endpointLabel,
                    FormatTime(anchor.PresentationTime)));
            }
            else if (anchor != null)
            {
                SetPlaybackMessage(string.Format(
                    CultureInfo.InvariantCulture,
                    "Loop {0} set at {1}.",
                    endpointLabel,
                    FormatTime(anchor.PresentationTime)));
            }

            LogInfo(string.Format(
                CultureInfo.InvariantCulture,
                "Set loop {0} for scope {1}.",
                endpointLabel,
                scope));
        }

        private void ClearLoopPoints()
        {
            var snapshot = _workspaceCoordinator.GetWorkspaceSnapshot();
            var loopContextPaneId = GetLoopCommandContextPaneId(snapshot);
            if (!string.IsNullOrWhiteSpace(loopContextPaneId))
            {
                var paneRange = GetPaneLocalLoopRange(snapshot, loopContextPaneId);
                _workspaceCoordinator.ClearPaneLoopRange(loopContextPaneId);
                UpdateLoopUi();
                SetPlaybackMessage(string.Format(
                    CultureInfo.InvariantCulture,
                    "{0} loop points {1}.",
                    string.Equals(loopContextPaneId, ComparePaneId, StringComparison.Ordinal)
                        ? "Compare pane"
                        : "Primary pane",
                    paneRange != null && paneRange.HasAnyMarkers ? "cleared" : "already clear"));
                LogInfo(string.Format(
                    CultureInfo.InvariantCulture,
                    "Cleared {0} loop points.",
                    string.Equals(loopContextPaneId, ComparePaneId, StringComparison.Ordinal)
                        ? "compare pane"
                        : "primary pane"));
                return;
            }

            _workspaceCoordinator.ClearSharedLoopRange();
            UpdateLoopUi();
            SetPlaybackMessage("Loop points cleared.");
            LogInfo("Cleared loop points.");
        }

        private async Task<ClipExportResult> ExportLoopClipAsync(string outputPath, string paneId)
        {
            var workspaceSnapshot = _workspaceCoordinator.GetWorkspaceSnapshot();
            ClipExportTarget exportTarget;
            string exportFailureMessage;
            if (!TryResolveClipExportTarget(workspaceSnapshot, paneId, out exportTarget, out exportFailureMessage))
            {
                if (!string.IsNullOrWhiteSpace(exportFailureMessage))
                {
                    SetPlaybackMessage(exportFailureMessage);
                    LogWarning(exportFailureMessage);
                }

                return null;
            }

            var pauseScope = IsCompareModeEnabled
                ? SynchronizedOperationScope.AllPanes
                : GetRequestedOperationScope();
            await PausePlaybackAsync(logAction: false, operationScope: pauseScope);

            workspaceSnapshot = _workspaceCoordinator.GetWorkspaceSnapshot();
            if (!TryResolveClipExportTarget(workspaceSnapshot, paneId, out exportTarget, out exportFailureMessage))
            {
                if (!string.IsNullOrWhiteSpace(exportFailureMessage))
                {
                    SetPlaybackMessage(exportFailureMessage);
                    LogWarning(exportFailureMessage);
                }

                return null;
            }

            var resolvedOutputPath = outputPath;
            if (string.IsNullOrWhiteSpace(resolvedOutputPath))
            {
                if (!TryPromptForClipExportPath(exportTarget, out resolvedOutputPath))
                {
                    return null;
                }
            }

            var request = BuildClipExportRequest(exportTarget, resolvedOutputPath);
            try
            {
                SetPlaybackMessage(string.Format(
                    CultureInfo.InvariantCulture,
                    "Exporting {0} clip...",
                    exportTarget.ContextLabel.ToLowerInvariant()));
                LogInfo(string.Format(
                    CultureInfo.InvariantCulture,
                    "Starting clip export for {0}: {1}.",
                    exportTarget.ContextLabel.ToLowerInvariant(),
                    GetSafeFileDisplay(resolvedOutputPath)));

                var exportResult = await _clipExportService.ExportAsync(request).ConfigureAwait(true);
                if (exportResult != null && exportResult.Succeeded)
                {
                    var durationText = exportResult.ProbedDuration.HasValue
                        ? FormatTime(exportResult.ProbedDuration.Value)
                        : FormatTime(exportResult.Plan.Duration);
                    SetPlaybackMessage("Clip exported.");
                    LogInfo(string.Format(
                        CultureInfo.InvariantCulture,
                        "Clip export completed for {0}: source {1}, output {2}, start {3}, end-exclusive {4}, duration {5}, strategy {6}, elapsed {7:0.0} ms.",
                        exportTarget.ContextLabel.ToLowerInvariant(),
                        GetSafeFileDisplay(exportResult.Plan.SourceFilePath),
                        GetSafeFileDisplay(exportResult.Plan.OutputFilePath),
                        FormatTime(exportResult.Plan.StartTime),
                        FormatTime(exportResult.Plan.EndTimeExclusive),
                        durationText,
                        string.IsNullOrWhiteSpace(exportResult.Plan.EndBoundaryStrategy)
                            ? "(none)"
                            : exportResult.Plan.EndBoundaryStrategy,
                        exportResult.Elapsed.TotalMilliseconds));
                }
                else if (exportResult != null)
                {
                    var sanitizedMessage = SanitizeSensitiveText(exportResult.Message);
                    SetPlaybackMessage("Clip export failed.");
                    LogError("Clip export failed: " + sanitizedMessage);
                }

                return exportResult;
            }
            catch (Exception ex)
            {
                var sanitizedMessage = SanitizeSensitiveText(ex.Message);
                SetPlaybackMessage("Clip export failed.");
                LogError("Clip export failed: " + sanitizedMessage);
                return new ClipExportResult(
                    false,
                    null,
                    sanitizedMessage,
                    -1,
                    TimeSpan.Zero,
                    null,
                    string.Empty,
                    string.Empty);
            }
        }

        private bool TryResolveClipExportTarget(
            ReviewWorkspaceSnapshot workspaceSnapshot,
            string requestedPaneId,
            out ClipExportTarget exportTarget,
            out string failureMessage)
        {
            exportTarget = null;
            failureMessage = string.Empty;

            if (!_clipExportService.IsBundledToolingAvailable)
            {
                failureMessage = _clipExportService.GetToolAvailabilityMessage();
                return false;
            }

            if (workspaceSnapshot == null)
            {
                failureMessage = "Load a video and set a loop range before exporting a clip.";
                return false;
            }

            if (!IsCompareModeEnabled)
            {
                var evaluation = EvaluateSharedLoopRange(workspaceSnapshot, GetRequestedOperationScope());
                var paneSnapshot = evaluation.WorkspaceSnapshot != null
                    ? evaluation.WorkspaceSnapshot.FocusedPane ?? evaluation.WorkspaceSnapshot.PrimaryPane
                    : null;
                if (paneSnapshot == null || !PaneHasLoadedMedia(paneSnapshot))
                {
                    failureMessage = "Load a video and set a shared A/B range before exporting a clip.";
                    return false;
                }

                if (!evaluation.HasMarkers)
                {
                    failureMessage = "Set loop-in and loop-out on the main transport before exporting a clip.";
                    return false;
                }

                if (evaluation.MissingTargetPaneRanges)
                {
                    failureMessage = "The shared loop range was captured for a different transport scope. Reset the loop markers and try again.";
                    return false;
                }

                if (evaluation.HasPendingMarkers)
                {
                    failureMessage = "Clip export is disabled while the shared loop range is still pending exact frame identity.";
                    return false;
                }

                if (evaluation.IsInvalidRange)
                {
                    failureMessage = "Clip export is disabled because the shared loop range is invalid.";
                    return false;
                }

                var loopRange = evaluation.FocusedPaneRange;
                if (loopRange == null || !loopRange.HasLoopIn || !loopRange.HasLoopOut)
                {
                    failureMessage = "Clip export requires both loop-in and loop-out on the main transport.";
                    return false;
                }

                var engine = GetEngineForPane(paneSnapshot.PaneId) as FfmpegReviewEngine;
                if (engine == null || !engine.IsMediaOpen)
                {
                    failureMessage = "The active review engine is unavailable for clip export.";
                    return false;
                }

                exportTarget = new ClipExportTarget(
                    paneSnapshot.PaneId,
                    "Main transport",
                    false,
                    paneSnapshot,
                    loopRange,
                    engine);
                return true;
            }

            var resolvedPaneId = ResolveClipExportPaneId(workspaceSnapshot, requestedPaneId);
            ReviewWorkspacePaneSnapshot targetPaneSnapshot;
            if (!workspaceSnapshot.TryGetPane(resolvedPaneId, out targetPaneSnapshot) || !PaneHasLoadedMedia(targetPaneSnapshot))
            {
                failureMessage = "Load media into the selected compare pane before exporting a clip.";
                return false;
            }

            var paneLoopRange = targetPaneSnapshot.LoopRange;
            var paneLabel = GetPaneDisplayLabel(resolvedPaneId);
            if (paneLoopRange == null || !paneLoopRange.HasAnyMarkers)
            {
                failureMessage = paneLabel + " does not have a pane-local A/B range to export yet.";
                return false;
            }

            if (!paneLoopRange.HasLoopIn || !paneLoopRange.HasLoopOut)
            {
                failureMessage = paneLabel + " clip export requires both pane-local loop markers.";
                return false;
            }

            if (paneLoopRange.HasPendingMarkers)
            {
                failureMessage = paneLabel + " clip export is disabled while pane-local markers are still pending exact frame identity.";
                return false;
            }

            if (paneLoopRange.IsInvalidRange)
            {
                failureMessage = paneLabel + " clip export is disabled because the pane-local loop range is invalid.";
                return false;
            }

            var paneEngine = GetEngineForPane(resolvedPaneId) as FfmpegReviewEngine;
            if (paneEngine == null || !paneEngine.IsMediaOpen)
            {
                failureMessage = paneLabel + " review engine is unavailable for clip export.";
                return false;
            }

            exportTarget = new ClipExportTarget(
                resolvedPaneId,
                paneLabel,
                true,
                targetPaneSnapshot,
                paneLoopRange,
                paneEngine);
            return true;
        }

        private string ResolveClipExportPaneId(ReviewWorkspaceSnapshot workspaceSnapshot, string requestedPaneId)
        {
            ReviewWorkspacePaneSnapshot paneSnapshot;
            if (!string.IsNullOrWhiteSpace(requestedPaneId) &&
                workspaceSnapshot != null &&
                workspaceSnapshot.TryGetPane(requestedPaneId, out paneSnapshot) &&
                paneSnapshot != null)
            {
                return requestedPaneId;
            }

            return workspaceSnapshot != null &&
                   workspaceSnapshot.ActivePane != null &&
                   !string.IsNullOrWhiteSpace(workspaceSnapshot.ActivePane.PaneId)
                ? workspaceSnapshot.ActivePane.PaneId
                : GetFocusedPaneId();
        }

        private bool TryPromptForClipExportPath(ClipExportTarget exportTarget, out string outputPath)
        {
            outputPath = string.Empty;
            if (exportTarget == null)
            {
                return false;
            }

            var dialog = new SaveFileDialog
            {
                Title = exportTarget.IsPaneLocal
                    ? exportTarget.ContextLabel + " - Save Loop As Clip"
                    : "Save Loop As Clip",
                Filter = "MP4 Video|*.mp4",
                DefaultExt = ".mp4",
                AddExtension = true,
                OverwritePrompt = true,
                CheckPathExists = true,
                FileName = BuildSuggestedClipFileName(exportTarget)
            };

            var sourceDirectory = Path.GetDirectoryName(exportTarget.PaneSnapshot != null
                ? exportTarget.PaneSnapshot.CurrentFilePath
                : string.Empty);
            if (!string.IsNullOrWhiteSpace(sourceDirectory) && Directory.Exists(sourceDirectory))
            {
                dialog.InitialDirectory = sourceDirectory;
            }

            if (dialog.ShowDialog(this) != true)
            {
                return false;
            }

            outputPath = dialog.FileName;
            return true;
        }

        private ClipExportRequest BuildClipExportRequest(ClipExportTarget exportTarget, string outputPath)
        {
            var engine = exportTarget.Engine;
            var paneSnapshot = exportTarget.PaneSnapshot;
            var sessionSnapshot = engine != null
                ? new ReviewSessionSnapshot(
                    paneSnapshot != null ? paneSnapshot.SessionId : exportTarget.PaneId,
                    paneSnapshot != null ? paneSnapshot.DisplayLabel : exportTarget.ContextLabel,
                    paneSnapshot != null ? paneSnapshot.PlaybackState : ReviewPlaybackState.Paused,
                    engine.CurrentFilePath,
                    engine.MediaInfo,
                    engine.Position)
                : ReviewSessionSnapshot.Empty;

            return new ClipExportRequest(
                paneSnapshot != null ? paneSnapshot.CurrentFilePath : string.Empty,
                outputPath,
                exportTarget.ContextLabel,
                exportTarget.PaneId,
                exportTarget.IsPaneLocal,
                sessionSnapshot,
                exportTarget.LoopRange,
                exportTarget.Engine);
        }

        private bool CanExportLoopClip(ReviewWorkspaceSnapshot workspaceSnapshot, string paneId, out string toolTip)
        {
            ClipExportTarget exportTarget;
            string failureMessage;
            if (!TryResolveClipExportTarget(workspaceSnapshot, paneId, out exportTarget, out failureMessage))
            {
                toolTip = string.IsNullOrWhiteSpace(failureMessage)
                    ? "Set a valid reviewed loop range before exporting a clip."
                    : failureMessage;
                return false;
            }

            toolTip = string.Format(
                CultureInfo.InvariantCulture,
                "Save the current {0} A/B range as an MP4 clip.",
                exportTarget.IsPaneLocal
                    ? exportTarget.ContextLabel.ToLowerInvariant()
                    : "main transport");
            return true;
        }

        private static string BuildSuggestedClipFileName(ClipExportTarget exportTarget)
        {
            var sourceFilePath = exportTarget != null && exportTarget.PaneSnapshot != null
                ? exportTarget.PaneSnapshot.CurrentFilePath
                : string.Empty;
            var baseName = string.IsNullOrWhiteSpace(sourceFilePath)
                ? "clip"
                : Path.GetFileNameWithoutExtension(sourceFilePath);
            baseName = SanitizeFileNameSegment(baseName);

            var loopRange = exportTarget != null ? exportTarget.LoopRange : null;
            var startSegment = loopRange != null && loopRange.LoopIn != null
                ? FormatClipFileNameTime(loopRange.LoopIn.PresentationTime)
                : "start";
            var endSegment = loopRange != null && loopRange.LoopOut != null
                ? FormatClipFileNameTime(loopRange.LoopOut.PresentationTime)
                : "end";
            var paneSegment = exportTarget != null && exportTarget.IsPaneLocal
                ? "-" + SanitizeFileNameSegment(exportTarget.ContextLabel.ToLowerInvariant())
                : string.Empty;

            return string.Format(
                CultureInfo.InvariantCulture,
                "{0}{1}-{2}-{3}.mp4",
                string.IsNullOrWhiteSpace(baseName) ? "clip" : baseName,
                paneSegment,
                startSegment,
                endSegment);
        }

        private static string SanitizeFileNameSegment(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return string.Empty;
            }

            var invalidCharacters = Path.GetInvalidFileNameChars();
            var builder = new StringBuilder(value.Length);
            foreach (var character in value.Trim())
            {
                builder.Append(Array.IndexOf(invalidCharacters, character) >= 0 || char.IsWhiteSpace(character)
                    ? '-'
                    : character);
            }

            return builder.ToString().Trim('-');
        }

        private static string FormatClipFileNameTime(TimeSpan value)
        {
            value = value < TimeSpan.Zero ? TimeSpan.Zero : value;
            return string.Format(
                CultureInfo.InvariantCulture,
                "{0:00}{1:00}{2:00}{3:000}",
                (int)value.TotalHours,
                value.Minutes,
                value.Seconds,
                value.Milliseconds);
        }

        private static string GetPaneDisplayLabel(string paneId)
        {
            return string.Equals(paneId, ComparePaneId, StringComparison.Ordinal)
                ? "Compare pane"
                : "Primary pane";
        }

        private void UpdateLoopUi()
        {
            var snapshot = _workspaceCoordinator.GetWorkspaceSnapshot();
            var evaluation = EvaluateSharedLoopRange(snapshot, GetRequestedOperationScope());
            UpdateLoopOverlay(evaluation);
            SetLoopStatus(BuildLoopStatusText(evaluation), BuildLoopStatusToolTip(evaluation));
            UpdatePaneLoopUi(snapshot, PrimaryPaneId);
            UpdatePaneLoopUi(snapshot, ComparePaneId);
            UpdateLoopCommandMenuState(snapshot);
        }

        private void UpdateLoopOverlay()
        {
            UpdateLoopOverlay(EvaluateSharedLoopRange(_workspaceCoordinator.GetWorkspaceSnapshot(), GetRequestedOperationScope()));
        }

        private void UpdateLoopOverlay(LoopRangeEvaluation evaluation)
        {
            if (PositionLoopRangeOverlay == null)
            {
                return;
            }

            var paneRange = evaluation != null ? evaluation.FocusedPaneRange : null;
            if (!_isMediaLoaded || paneRange == null || !paneRange.HasAnyMarkers)
            {
                PositionLoopRangeOverlay.InPosition = double.NaN;
                PositionLoopRangeOverlay.OutPosition = double.NaN;
                PositionLoopRangeOverlay.IsInPending = false;
                PositionLoopRangeOverlay.IsOutPending = false;
                PositionLoopRangeOverlay.IsInvalid = false;
                return;
            }

            PositionLoopRangeOverlay.Maximum = Math.Max(1d, PositionSlider.Maximum);
            PositionLoopRangeOverlay.InPosition = paneRange.HasLoopIn
                ? paneRange.LoopIn.PresentationTime.TotalSeconds
                : double.NaN;
            PositionLoopRangeOverlay.OutPosition = paneRange.HasLoopOut
                ? paneRange.LoopOut.PresentationTime.TotalSeconds
                : double.NaN;
            PositionLoopRangeOverlay.IsInPending = paneRange.HasLoopIn &&
                                                   (paneRange.LoopIn.IsPending || evaluation.MissingTargetPaneRanges);
            PositionLoopRangeOverlay.IsOutPending = paneRange.HasLoopOut &&
                                                    (paneRange.LoopOut.IsPending || evaluation.MissingTargetPaneRanges);
            PositionLoopRangeOverlay.IsInvalid = paneRange.IsInvalidRange || evaluation.IsInvalidRange;
        }

        private void SetLoopStatus(string message, string toolTip)
        {
            if (LoopStatusTextBlock == null)
            {
                return;
            }

            LoopStatusTextBlock.Text = string.IsNullOrWhiteSpace(message) ? "Loop: off" : message;
            LoopStatusTextBlock.ToolTip = string.IsNullOrWhiteSpace(toolTip)
                ? "Loop playback status for the main transport."
                : toolTip;
        }

        private void UpdateLoopCommandMenuState(ReviewWorkspaceSnapshot workspaceSnapshot)
        {
            if (SetLoopInMenuItem == null || SetLoopOutMenuItem == null || ClearLoopPointsMenuItem == null || SaveLoopAsClipMenuItem == null)
            {
                return;
            }

            var contextLabel = GetLoopCommandContextLabel(workspaceSnapshot);
            SetLoopInMenuItem.ToolTip = string.Format(
                CultureInfo.InvariantCulture,
                "Set loop in for the {0} using [.",
                contextLabel);
            SetLoopOutMenuItem.ToolTip = string.Format(
                CultureInfo.InvariantCulture,
                "Set loop out for the {0} using ].",
                contextLabel);
            ClearLoopPointsMenuItem.ToolTip = string.Format(
                CultureInfo.InvariantCulture,
                "Clear loop points for the {0}.",
                contextLabel);

            string clipExportToolTip;
            var canExportLoopClip = CanExportLoopClip(workspaceSnapshot, null, out clipExportToolTip);
            SaveLoopAsClipMenuItem.IsEnabled = canExportLoopClip;
            SaveLoopAsClipMenuItem.ToolTip = clipExportToolTip;
        }

        private void UpdatePaneLoopUi(ReviewWorkspaceSnapshot workspaceSnapshot, string paneId)
        {
            LoopRangeOverlay loopOverlay;
            TextBlock loopStatusTextBlock;
            Slider positionSlider;
            if (!TryGetPaneLoopControls(paneId, out loopOverlay, out loopStatusTextBlock, out positionSlider))
            {
                return;
            }

            var paneRange = GetPaneLocalLoopRange(workspaceSnapshot, paneId);
            if (loopOverlay == null || loopStatusTextBlock == null || positionSlider == null || paneRange == null || !paneRange.HasAnyMarkers)
            {
                if (loopOverlay != null)
                {
                    loopOverlay.InPosition = double.NaN;
                    loopOverlay.OutPosition = double.NaN;
                    loopOverlay.IsInPending = false;
                    loopOverlay.IsOutPending = false;
                    loopOverlay.IsInvalid = false;
                }

                if (loopStatusTextBlock != null)
                {
                    loopStatusTextBlock.Text = IsLoopPlaybackEnabled ? "Loop: full media" : "Loop: off";
                    loopStatusTextBlock.ToolTip = string.Format(
                        CultureInfo.InvariantCulture,
                        "{0} loop playback status. With no pane-local A/B markers, pane playback loops the full media only when loop playback is enabled.",
                        string.Equals(paneId, ComparePaneId, StringComparison.Ordinal) ? "Compare pane" : "Primary pane");
                }

                return;
            }

            loopOverlay.Maximum = Math.Max(1d, positionSlider.Maximum);
            loopOverlay.InPosition = paneRange.HasLoopIn
                ? paneRange.LoopIn.PresentationTime.TotalSeconds
                : double.NaN;
            loopOverlay.OutPosition = paneRange.HasLoopOut
                ? paneRange.LoopOut.PresentationTime.TotalSeconds
                : double.NaN;
            loopOverlay.IsInPending = paneRange.HasLoopIn && paneRange.LoopIn.IsPending;
            loopOverlay.IsOutPending = paneRange.HasLoopOut && paneRange.LoopOut.IsPending;
            loopOverlay.IsInvalid = paneRange.IsInvalidRange;

            loopStatusTextBlock.Text = BuildPaneLoopStatusText(paneRange);
            loopStatusTextBlock.ToolTip = BuildPaneLoopStatusToolTip(paneId, paneRange);
        }

        private static string BuildPaneLoopStatusText(LoopPlaybackPaneRangeSnapshot paneRange)
        {
            if (paneRange == null || !paneRange.HasAnyMarkers)
            {
                return "Loop: off";
            }

            var rangeDisplay = string.Format(
                CultureInfo.InvariantCulture,
                "{0} -> {1}",
                FormatLoopBoundaryLabel(paneRange.LoopIn, isStartBoundary: true),
                FormatLoopBoundaryLabel(paneRange.LoopOut, isStartBoundary: false));

            if (paneRange.IsInvalidRange)
            {
                return "Loop: invalid";
            }

            if (paneRange.HasPendingMarkers)
            {
                return string.Format(CultureInfo.InvariantCulture, "Loop: pending ({0})", rangeDisplay);
            }

            return string.Format(CultureInfo.InvariantCulture, "Loop: {0}", rangeDisplay);
        }

        private static string BuildPaneLoopStatusToolTip(string paneId, LoopPlaybackPaneRangeSnapshot paneRange)
        {
            var paneLabel = string.Equals(paneId, ComparePaneId, StringComparison.Ordinal)
                ? "Compare pane"
                : "Primary pane";
            if (paneRange == null || !paneRange.HasAnyMarkers)
            {
                return paneLabel + " loop playback status.";
            }

            if (paneRange.IsInvalidRange)
            {
                return paneLabel + " loop-out currently lands before loop-in. Clear the pane loop or set the markers again in order.";
            }

            if (paneRange.HasPendingMarkers)
            {
                return paneLabel + " loop markers are pending exact frame identity. Pane-local A/B repeat will wait until the engine proves those frames.";
            }

            return paneLabel + " pane-local A/B loop range.";
        }

        private LoopPlaybackPaneRangeSnapshot GetPaneLocalLoopRange(ReviewWorkspaceSnapshot workspaceSnapshot, string paneId)
        {
            ReviewWorkspacePaneSnapshot paneSnapshot;
            return workspaceSnapshot != null &&
                   workspaceSnapshot.TryGetPane(paneId, out paneSnapshot) &&
                   paneSnapshot != null
                ? paneSnapshot.LoopRange
                : null;
        }

        private bool TryGetPaneLoopControls(
            string paneId,
            out LoopRangeOverlay loopOverlay,
            out TextBlock loopStatusTextBlock,
            out Slider positionSlider)
        {
            if (string.Equals(paneId, ComparePaneId, StringComparison.Ordinal))
            {
                loopOverlay = ComparePaneLoopRangeOverlay;
                loopStatusTextBlock = ComparePaneLoopStatusTextBlock;
                positionSlider = ComparePanePositionSlider;
                return loopOverlay != null && loopStatusTextBlock != null && positionSlider != null;
            }

            loopOverlay = PrimaryPaneLoopRangeOverlay;
            loopStatusTextBlock = PrimaryPaneLoopStatusTextBlock;
            positionSlider = PrimaryPanePositionSlider;
            return loopOverlay != null && loopStatusTextBlock != null && positionSlider != null;
        }

        private bool TryGetActivePaneLoopPlaybackTarget(
            MultiVideoWorkspaceState workspaceState,
            out ReviewPaneState paneState,
            out LoopPlaybackPaneRangeSnapshot paneRange)
        {
            paneState = null;
            paneRange = null;

            var paneId = _activeLoopPlaybackPaneId;
            if (!IsCompareModeEnabled || string.IsNullOrWhiteSpace(paneId) || workspaceState == null)
            {
                return false;
            }

            if (!workspaceState.TryGetPane(paneId, out paneState) ||
                paneState == null ||
                !paneState.Session.IsMediaOpen)
            {
                paneState = null;
                return false;
            }

            paneRange = paneState.LoopRange;
            return true;
        }

        private LoopRangeEvaluation EvaluateSharedLoopRange(
            ReviewWorkspaceSnapshot workspaceSnapshot,
            SynchronizedOperationScope operationScope)
        {
            var snapshot = workspaceSnapshot ?? ReviewWorkspaceSnapshot.Empty;
            var sharedRange = snapshot.SharedLoopRange ?? LoopPlaybackRangeSnapshot.Empty;
            var targetPanes = GetLoopTargetPanes(snapshot, operationScope);
            LoopPlaybackPaneRangeSnapshot focusedPaneRange = null;
            if (!string.IsNullOrWhiteSpace(snapshot.FocusedPaneId))
            {
                sharedRange.TryGetPaneRange(snapshot.FocusedPaneId, out focusedPaneRange);
            }

            if (!sharedRange.HasMarkers || targetPanes.Length == 0)
            {
                return new LoopRangeEvaluation(
                    snapshot,
                    operationScope,
                    sharedRange,
                    targetPanes,
                    Array.Empty<LoopPlaybackPaneRangeSnapshot>(),
                    focusedPaneRange,
                    false,
                    false,
                    false);
            }

            LoopPlaybackPaneRangeSnapshot[] targetPaneRanges;
            bool hasPendingMarkers;
            bool isInvalidRange;
            var missingTargetPaneRanges = !TryGetLoopTargetPaneRanges(
                sharedRange,
                targetPanes,
                out targetPaneRanges,
                out hasPendingMarkers,
                out isInvalidRange);
            return new LoopRangeEvaluation(
                snapshot,
                operationScope,
                sharedRange,
                targetPanes,
                targetPaneRanges ?? Array.Empty<LoopPlaybackPaneRangeSnapshot>(),
                focusedPaneRange,
                missingTargetPaneRanges,
                hasPendingMarkers,
                isInvalidRange);
        }

        private string BuildLoopStatusText(LoopRangeEvaluation evaluation)
        {
            if (evaluation == null || !evaluation.HasMarkers)
            {
                return IsLoopPlaybackEnabled ? "Loop: full media" : "Loop: off";
            }

            var focusedPaneRange = evaluation.FocusedPaneRange;
            if (focusedPaneRange == null || !focusedPaneRange.HasAnyMarkers)
            {
                return IsLoopPlaybackEnabled ? "Loop: pending scope" : "Loop: off";
            }

            var rangeDisplay = string.Format(
                CultureInfo.InvariantCulture,
                "{0} -> {1}",
                FormatLoopBoundaryLabel(focusedPaneRange.LoopIn, isStartBoundary: true),
                FormatLoopBoundaryLabel(focusedPaneRange.LoopOut, isStartBoundary: false));

            if (evaluation.IsInvalidRange)
            {
                return "Loop: invalid";
            }

            if (evaluation.MissingTargetPaneRanges)
            {
                return string.Format(CultureInfo.InvariantCulture, "Loop: pending scope ({0})", rangeDisplay);
            }

            if (evaluation.HasPendingMarkers)
            {
                return string.Format(CultureInfo.InvariantCulture, "Loop: pending ({0})", rangeDisplay);
            }

            if (!IsLoopPlaybackEnabled)
            {
                return string.Format(CultureInfo.InvariantCulture, "Loop: off ({0})", rangeDisplay);
            }

            return string.Format(CultureInfo.InvariantCulture, "Loop: {0}", rangeDisplay);
        }

        private string BuildLoopStatusToolTip(LoopRangeEvaluation evaluation)
        {
            if (evaluation == null || !evaluation.HasMarkers)
            {
                return IsLoopPlaybackEnabled
                    ? "Loop playback is enabled for the full media range because no A/B markers are currently set."
                    : "Loop playback is disabled. Use Playback > Set Loop In / Set Loop Out or [ and ] to define an A/B range.";
            }

            if (evaluation.IsInvalidRange)
            {
                return "The current loop-out marker lands before loop-in on at least one targeted pane. Clear the range or set the markers again in order.";
            }

            if (evaluation.MissingTargetPaneRanges)
            {
                return "The current shared loop box was captured for a different pane scope. Reset the loop markers after choosing the main transport scope you want to loop.";
            }

            if (evaluation.HasPendingMarkers)
            {
                return "One or more loop markers are still pending exact frame identity. The UI will keep showing the range, but A/B repeat will not start until the engine proves those frames.";
            }

            return IsLoopPlaybackEnabled
                ? "Loop playback will repeat the boxed A/B range for the current main transport scope."
                : "A/B markers are set and ready. Enable Playback > Loop Playback to repeat the boxed range.";
        }

        private static string FormatLoopBoundaryLabel(LoopPlaybackAnchorSnapshot anchor, bool isStartBoundary)
        {
            if (anchor == null)
            {
                return isStartBoundary ? "start" : "end";
            }

            return anchor.IsPending
                ? "pending"
                : FormatTime(anchor.PresentationTime);
        }

        private bool ShouldRestartLoopPlayback(ReviewWorkspaceChangedEventArgs e)
        {
            if (!IsLoopPlaybackEnabled ||
                _suppressLoopRestart ||
                _isLoopRestartInFlight ||
                !_buildVariant.SupportsTimedPlayback ||
                e == null)
            {
                return false;
            }

            ReviewPaneState previousPaneState;
            LoopPlaybackPaneRangeSnapshot paneLoopRange;
            ReviewPaneState currentPaneState;
            if (TryGetActivePaneLoopPlaybackTarget(e.CurrentWorkspace, out currentPaneState, out paneLoopRange))
            {
                if (e.PreviousWorkspace == null ||
                    !e.PreviousWorkspace.TryGetPane(currentPaneState.PaneId, out previousPaneState) ||
                    previousPaneState == null)
                {
                    return false;
                }

                if (previousPaneState.Session.PlaybackState != ReviewPlaybackState.Playing ||
                    currentPaneState.Session.PlaybackState == ReviewPlaybackState.Playing)
                {
                    return false;
                }

                if (paneLoopRange == null || !paneLoopRange.HasAnyMarkers)
                {
                    return IsSessionAtPlaybackEnd(currentPaneState.Session);
                }

                return !paneLoopRange.HasPendingMarkers &&
                       !paneLoopRange.IsInvalidRange &&
                       HasReachedLoopEnd(currentPaneState.Session, paneLoopRange);
            }

            var scope = ResolveLoopPlaybackScope(e.CurrentWorkspace);
            var previousTargetPanes = GetLoopTargetPanes(e.PreviousWorkspace, scope);
            var currentTargetPanes = GetLoopTargetPanes(e.CurrentWorkspace, scope);
            if (previousTargetPanes.Length == 0 || currentTargetPanes.Length == 0)
            {
                return false;
            }

            if (!previousTargetPanes.Any(pane => pane.Session.PlaybackState == ReviewPlaybackState.Playing))
            {
                return false;
            }

            if (currentTargetPanes.Any(pane => pane.Session.PlaybackState == ReviewPlaybackState.Playing))
            {
                return false;
            }

            var sharedRange = e.CurrentWorkspace != null
                ? e.CurrentWorkspace.SharedLoopRange
                : LoopPlaybackRangeSnapshot.Empty;
            if (sharedRange == null || !sharedRange.HasMarkers)
            {
                return currentTargetPanes.All(pane => IsSessionAtPlaybackEnd(pane.Session));
            }

            LoopPlaybackPaneRangeSnapshot[] targetPaneRanges;
            bool hasPendingMarkers;
            bool isInvalidRange;
            if (!TryGetLoopTargetPaneRanges(sharedRange, currentTargetPanes, out targetPaneRanges, out hasPendingMarkers, out isInvalidRange) ||
                hasPendingMarkers ||
                isInvalidRange)
            {
                return false;
            }

            return HaveLoopTargetPanesReachedBoundary(currentTargetPanes, targetPaneRanges);
        }

        private bool ShouldRestartLoopPlaybackAtBoundary(MultiVideoWorkspaceState workspaceState)
        {
            if (!IsLoopPlaybackEnabled ||
                _suppressLoopRestart ||
                _isLoopRestartInFlight ||
                !_buildVariant.SupportsTimedPlayback ||
                workspaceState == null)
            {
                return false;
            }

            ReviewPaneState paneState;
            LoopPlaybackPaneRangeSnapshot paneLoopRange;
            if (TryGetActivePaneLoopPlaybackTarget(workspaceState, out paneState, out paneLoopRange))
            {
                if (paneState.Session.PlaybackState != ReviewPlaybackState.Playing ||
                    paneLoopRange == null ||
                    !paneLoopRange.HasAnyMarkers ||
                    paneLoopRange.HasPendingMarkers ||
                    paneLoopRange.IsInvalidRange)
                {
                    return false;
                }

                return HasReachedLoopEnd(paneState.Session, paneLoopRange);
            }

            if (!_isPlaying)
            {
                return false;
            }

            var sharedRange = workspaceState.SharedLoopRange ?? LoopPlaybackRangeSnapshot.Empty;
            if (!sharedRange.HasMarkers)
            {
                return false;
            }

            var scope = ResolveLoopPlaybackScope(workspaceState);
            var targetPanes = GetLoopTargetPanes(workspaceState, scope);
            if (targetPanes.Length == 0)
            {
                return false;
            }

            LoopPlaybackPaneRangeSnapshot[] targetPaneRanges;
            bool hasPendingMarkers;
            bool isInvalidRange;
            if (!TryGetLoopTargetPaneRanges(sharedRange, targetPanes, out targetPaneRanges, out hasPendingMarkers, out isInvalidRange) ||
                hasPendingMarkers ||
                isInvalidRange)
            {
                return false;
            }

            return HaveLoopTargetPanesReachedBoundary(targetPanes, targetPaneRanges);
        }

        private async Task RestartLoopPlaybackAsync()
        {
            if (_isLoopRestartInFlight || !_isMediaLoaded)
            {
                return;
            }

            _isLoopRestartInFlight = true;
            try
            {
                var workspaceState = _workspaceCoordinator.CurrentWorkspace;
                ReviewPaneState paneState;
                LoopPlaybackPaneRangeSnapshot paneLoopRange;
                if (TryGetActivePaneLoopPlaybackTarget(workspaceState, out paneState, out paneLoopRange))
                {
                    long restartFrameIndex = 0L;
                    var usePaneRangeRestart = paneLoopRange != null &&
                                              paneLoopRange.HasAnyMarkers &&
                                              !paneLoopRange.HasPendingMarkers &&
                                              !paneLoopRange.IsInvalidRange &&
                                              paneLoopRange.TryGetRestartFrameIndex(out restartFrameIndex);

                    _suppressLoopRestart = true;
                    await RunWithCacheStatusAsync(
                        usePaneRangeRestart ? "Cache: looping pane to loop-in..." : "Cache: looping pane to start...",
                        async () =>
                        {
                            await _workspaceCoordinator.PausePaneAsync(paneState.PaneId);
                            if (usePaneRangeRestart)
                            {
                                await _workspaceCoordinator.SeekPaneToFrameAsync(paneState.PaneId, restartFrameIndex);
                                return;
                            }

                            await _workspaceCoordinator.SeekPaneToTimeAsync(paneState.PaneId, TimeSpan.Zero);
                        });
                    await _workspaceCoordinator.PlayPaneAsync(paneState.PaneId);
                    ApplySessionSnapshot(_workspaceCoordinator.RefreshFromEngine());
                    UpdateTransportState();

                    var paneEngine = GetEngineForPane(paneState.PaneId);
                    if (paneEngine != null && paneEngine.IsPlaying)
                    {
                        _lastPlaybackScope = SynchronizedOperationScope.FocusedPane;
                        _suppressLoopRestart = false;
                        LogInfo(usePaneRangeRestart
                            ? "Pane-local loop playback restarted from loop-in."
                            : "Pane-local loop playback restarted from the beginning.");
                    }
                    else
                    {
                        LogWarning("Pane-local loop playback restart did not resume playback.");
                    }

                    return;
                }

                var scope = ResolveLoopPlaybackScope(workspaceState);
                Dictionary<string, long> restartTargets;
                var useRangeRestart = TryBuildLoopRestartFrameTargets(workspaceState, scope, out restartTargets);

                _suppressLoopRestart = true;
                await RunWithCacheStatusAsync(
                    useRangeRestart ? "Cache: looping to loop-in..." : "Cache: looping to start...",
                    async () =>
                    {
                        await _workspaceCoordinator.PauseAsync(scope);
                        if (useRangeRestart)
                        {
                            await _workspaceCoordinator.SeekToPaneFramesAsync(restartTargets);
                            return;
                        }

                        await _workspaceCoordinator.SeekToTimeAsync(TimeSpan.Zero, scope);
                    });
                await _workspaceCoordinator.PlayAsync(scope);
                ApplySessionSnapshot(_workspaceCoordinator.RefreshFromEngine());
                UpdateTransportState();

                if (_isPlaying)
                {
                    _lastPlaybackScope = scope;
                    _suppressLoopRestart = false;
                    LogInfo(useRangeRestart
                        ? "Loop playback restarted from loop-in."
                        : "Loop playback restarted from the beginning.");
                }
                else
                {
                    LogWarning("Loop playback restart did not resume playback.");
                }
            }
            catch (Exception ex)
            {
                var sanitizedMessage = SanitizeSensitiveText(ex.Message);
                SetPlaybackMessage("Loop playback restart failed.");
                SetMediaSummary(sanitizedMessage);
                LogError("Loop playback restart failed: " + sanitizedMessage);
            }
            finally
            {
                _isLoopRestartInFlight = false;
            }
        }

        private bool TryBuildLoopRestartFrameTargets(
            MultiVideoWorkspaceState workspaceState,
            SynchronizedOperationScope scope,
            out Dictionary<string, long> restartTargets)
        {
            restartTargets = null;
            if (workspaceState == null)
            {
                return false;
            }

            var sharedRange = workspaceState.SharedLoopRange ?? LoopPlaybackRangeSnapshot.Empty;
            if (!sharedRange.HasMarkers)
            {
                return false;
            }

            var targetPanes = GetLoopTargetPanes(workspaceState, scope);
            LoopPlaybackPaneRangeSnapshot[] targetPaneRanges;
            bool hasPendingMarkers;
            bool isInvalidRange;
            if (!TryGetLoopTargetPaneRanges(sharedRange, targetPanes, out targetPaneRanges, out hasPendingMarkers, out isInvalidRange) ||
                hasPendingMarkers ||
                isInvalidRange)
            {
                return false;
            }

            restartTargets = new Dictionary<string, long>(StringComparer.Ordinal);
            for (var index = 0; index < targetPanes.Length; index++)
            {
                long restartFrameIndex;
                if (!targetPaneRanges[index].TryGetRestartFrameIndex(out restartFrameIndex))
                {
                    restartTargets = null;
                    return false;
                }

                restartTargets[targetPanes[index].PaneId] = restartFrameIndex;
            }

            return restartTargets.Count > 0;
        }

        private SynchronizedOperationScope ResolveLoopPlaybackScope(MultiVideoWorkspaceState workspaceState)
        {
            if (workspaceState == null)
            {
                return SynchronizedOperationScope.FocusedPane;
            }

            if (_lastPlaybackScope == SynchronizedOperationScope.AllPanes &&
                workspaceState.Panes.Count(pane => pane != null && pane.Session.IsMediaOpen) > 1)
            {
                return SynchronizedOperationScope.AllPanes;
            }

            return SynchronizedOperationScope.FocusedPane;
        }

        private static ReviewPaneState[] GetLoopTargetPanes(
            MultiVideoWorkspaceState workspaceState,
            SynchronizedOperationScope scope)
        {
            if (workspaceState == null)
            {
                return Array.Empty<ReviewPaneState>();
            }

            if (scope == SynchronizedOperationScope.AllPanes)
            {
                return workspaceState.Panes
                    .Where(pane => pane != null && pane.Session.IsMediaOpen)
                    .ToArray();
            }

            var focusedPane = workspaceState.FocusedPane;
            return focusedPane != null && focusedPane.Session.IsMediaOpen
                ? new[] { focusedPane }
                : Array.Empty<ReviewPaneState>();
        }

        private static ReviewWorkspacePaneSnapshot[] GetLoopTargetPanes(
            ReviewWorkspaceSnapshot workspaceSnapshot,
            SynchronizedOperationScope scope)
        {
            if (workspaceSnapshot == null)
            {
                return Array.Empty<ReviewWorkspacePaneSnapshot>();
            }

            if (scope == SynchronizedOperationScope.AllPanes)
            {
                return workspaceSnapshot.Panes
                    .Where(PaneHasLoadedMedia)
                    .ToArray();
            }

            var focusedPane = workspaceSnapshot.FocusedPane;
            return PaneHasLoadedMedia(focusedPane)
                ? new[] { focusedPane }
                : Array.Empty<ReviewWorkspacePaneSnapshot>();
        }

        private static bool TryGetLoopTargetPaneRanges(
            LoopPlaybackRangeSnapshot sharedRange,
            ReviewPaneState[] targetPanes,
            out LoopPlaybackPaneRangeSnapshot[] paneRanges,
            out bool hasPendingMarkers,
            out bool isInvalidRange)
        {
            paneRanges = Array.Empty<LoopPlaybackPaneRangeSnapshot>();
            hasPendingMarkers = false;
            isInvalidRange = false;
            if (sharedRange == null || !sharedRange.HasMarkers || targetPanes == null || targetPanes.Length == 0)
            {
                return false;
            }

            paneRanges = new LoopPlaybackPaneRangeSnapshot[targetPanes.Length];
            for (var index = 0; index < targetPanes.Length; index++)
            {
                var pane = targetPanes[index];
                LoopPlaybackPaneRangeSnapshot paneRange;
                if (pane == null || !sharedRange.TryGetPaneRange(pane.PaneId, out paneRange))
                {
                    return false;
                }

                paneRanges[index] = paneRange;
                hasPendingMarkers |= paneRange.HasPendingMarkers;
                isInvalidRange |= paneRange.IsInvalidRange;
            }

            return true;
        }

        private static bool TryGetLoopTargetPaneRanges(
            LoopPlaybackRangeSnapshot sharedRange,
            ReviewWorkspacePaneSnapshot[] targetPanes,
            out LoopPlaybackPaneRangeSnapshot[] paneRanges,
            out bool hasPendingMarkers,
            out bool isInvalidRange)
        {
            paneRanges = Array.Empty<LoopPlaybackPaneRangeSnapshot>();
            hasPendingMarkers = false;
            isInvalidRange = false;
            if (sharedRange == null || !sharedRange.HasMarkers || targetPanes == null || targetPanes.Length == 0)
            {
                return false;
            }

            paneRanges = new LoopPlaybackPaneRangeSnapshot[targetPanes.Length];
            for (var index = 0; index < targetPanes.Length; index++)
            {
                var pane = targetPanes[index];
                LoopPlaybackPaneRangeSnapshot paneRange;
                if (pane == null || !sharedRange.TryGetPaneRange(pane.PaneId, out paneRange))
                {
                    return false;
                }

                paneRanges[index] = paneRange;
                hasPendingMarkers |= paneRange.HasPendingMarkers;
                isInvalidRange |= paneRange.IsInvalidRange;
            }

            return true;
        }

        private static bool HaveLoopTargetPanesReachedBoundary(
            ReviewPaneState[] targetPanes,
            IReadOnlyList<LoopPlaybackPaneRangeSnapshot> paneRanges)
        {
            if (targetPanes == null || paneRanges == null || targetPanes.Length != paneRanges.Count)
            {
                return false;
            }

            for (var index = 0; index < targetPanes.Length; index++)
            {
                var pane = targetPanes[index];
                var paneRange = paneRanges[index];
                if (pane == null || paneRange == null || !HasReachedLoopEnd(pane.Session, paneRange))
                {
                    return false;
                }
            }

            return true;
        }

        private static bool HasReachedLoopEnd(
            ReviewSessionSnapshot session,
            LoopPlaybackPaneRangeSnapshot paneRange)
        {
            if (session == null || paneRange == null || !session.IsMediaOpen)
            {
                return false;
            }

            if (paneRange.LoopOut != null && paneRange.LoopOut.HasAbsoluteFrameIdentity)
            {
                if (session.HasAbsoluteFrameIdentity &&
                    session.Position.FrameIndex.GetValueOrDefault() >= paneRange.LoopOut.AbsoluteFrameIndex.GetValueOrDefault())
                {
                    return true;
                }

                var guardTolerance = GetLoopBoundaryGuardTolerance(session);
                return session.Position.PresentationTime >= paneRange.LoopOut.PresentationTime + guardTolerance;
            }

            return IsSessionAtPlaybackEnd(session);
        }

        private static TimeSpan GetLoopBoundaryGuardTolerance(ReviewSessionSnapshot session)
        {
            var stepToleranceTicks = session != null && session.MediaInfo.PositionStep > TimeSpan.Zero
                ? session.MediaInfo.PositionStep.Ticks * 2L
                : 0L;
            return TimeSpan.FromTicks(Math.Max(stepToleranceTicks, LoopPlaybackMinimumEndTolerance.Ticks));
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

        private static bool TryGetLoopMarkerEndpointKey(Key key, out LoopPlaybackMarkerEndpoint endpoint)
        {
            var virtualKey = KeyInterop.VirtualKeyFromKey(key);
            if (virtualKey == 0xDB)
            {
                endpoint = LoopPlaybackMarkerEndpoint.In;
                return true;
            }

            if (virtualKey == 0xDD)
            {
                endpoint = LoopPlaybackMarkerEndpoint.Out;
                return true;
            }

            endpoint = LoopPlaybackMarkerEndpoint.In;
            return false;
        }

        private void ShowVideoInfoWindow()
        {
            ShowVideoInfoWindow(null);
        }

        private void ShowVideoInfoWindow(string paneId)
        {
            if (!_isMediaLoaded)
            {
                SetPlaybackMessage("Load a video to inspect its details.");
                return;
            }

            var resolvedPaneId = string.IsNullOrWhiteSpace(paneId)
                ? GetFocusedPaneId()
                : paneId;
            TrySelectPaneForShell(resolvedPaneId);
            UpdateWorkspacePanePresentation();

            var paneLabel = string.Equals(resolvedPaneId, ComparePaneId, StringComparison.Ordinal)
                ? "Compare"
                : "Primary";
            var engine = GetEngineForPane(resolvedPaneId);
            if (engine == null || !engine.IsMediaOpen)
            {
                SetPlaybackMessage("The selected pane is not available.");
                return;
            }

            var mediaInfo = engine.MediaInfo ?? VideoMediaInfo.Empty;
            var infoWindow = new VideoInfoWindow(BuildVideoInfoSnapshot(paneLabel, mediaInfo))
            {
                Owner = this
            };
            var existingInspectorCount = OwnedWindows.OfType<VideoInfoWindow>().Count();
            infoWindow.WindowStartupLocation = WindowStartupLocation.Manual;
            infoWindow.Left = Left + 80 + (existingInspectorCount * 32);
            infoWindow.Top = Top + 80 + (existingInspectorCount * 32);
            infoWindow.Show();
            infoWindow.Activate();
        }

        private void VideoPane_PreviewMouseRightButtonDown(object sender, MouseButtonEventArgs e)
        {
            var paneId = GetPaneIdFromSender(sender);
            if (string.IsNullOrWhiteSpace(paneId))
            {
                return;
            }

            TrySelectPaneForShell(paneId);
            UpdateWorkspacePanePresentation();
        }

        private void VideoPane_ContextMenuOpening(object sender, ContextMenuEventArgs e)
        {
            var paneId = GetPaneIdFromSender(sender);
            if (string.IsNullOrWhiteSpace(paneId))
            {
                return;
            }

            TrySelectPaneForShell(paneId);
            UpdateWorkspacePanePresentation();

            var host = sender as FrameworkElement;
            var menuItems = host != null && host.ContextMenu != null
                ? host.ContextMenu.Items.OfType<MenuItem>().ToArray()
                : Array.Empty<MenuItem>();
            if (menuItems.Length > 0)
            {
                var engine = GetEngineForPane(paneId);
                menuItems[0].IsEnabled = engine != null && engine.IsMediaOpen;
            }

            if (menuItems.Length > 1)
            {
                string toolTip;
                var canExport = CanExportLoopClip(_workspaceCoordinator.GetWorkspaceSnapshot(), paneId, out toolTip);
                menuItems[1].IsEnabled = canExport;
                menuItems[1].ToolTip = toolTip;
            }
        }

        private VideoInfoSnapshot BuildVideoInfoSnapshot(string paneLabel, VideoMediaInfo mediaInfo)
        {
            var filePath = string.IsNullOrWhiteSpace(mediaInfo.FilePath)
                ? string.Empty
                : mediaInfo.FilePath;
            var fileName = Path.GetFileName(filePath);
            if (string.IsNullOrWhiteSpace(fileName))
            {
                fileName = paneLabel + " Video";
            }

            var summaryFields = new List<VideoInfoField>
            {
                new VideoInfoField("Duration", FormatInspectorDuration(mediaInfo.Duration)),
                new VideoInfoField("Frame rate", FormatInspectorFrameRate(mediaInfo.FramesPerSecond)),
                new VideoInfoField("Display resolution", FormatInspectorResolution(mediaInfo.DisplayWidth, mediaInfo.DisplayHeight)),
                new VideoInfoField(
                    "Display aspect",
                    FormatInspectorRatio(
                        mediaInfo.DisplayAspectRatioNumerator,
                        mediaInfo.DisplayAspectRatioDenominator))
            };

            var videoFields = new List<VideoInfoField>
            {
                new VideoInfoField("Codec", FormatInspectorText(mediaInfo.VideoCodecName)),
                new VideoInfoField("Coded resolution", FormatInspectorResolution(mediaInfo.PixelWidth, mediaInfo.PixelHeight)),
                new VideoInfoField("Display resolution", FormatInspectorResolution(mediaInfo.DisplayWidth, mediaInfo.DisplayHeight)),
                new VideoInfoField(
                    "Display aspect",
                    FormatInspectorRatio(
                        mediaInfo.DisplayAspectRatioNumerator,
                        mediaInfo.DisplayAspectRatioDenominator)),
                new VideoInfoField("Source pixel format", FormatInspectorText(mediaInfo.SourcePixelFormatName)),
                new VideoInfoField("Bit depth", FormatInspectorBitDepth(mediaInfo.VideoBitDepth)),
                new VideoInfoField("Bitrate", FormatInspectorBitRate(mediaInfo.VideoBitRate))
            };
            AddInspectorFieldIfKnown(videoFields, "Color space", mediaInfo.VideoColorSpace);
            AddInspectorFieldIfKnown(videoFields, "Color range", mediaInfo.VideoColorRange);
            AddInspectorFieldIfKnown(videoFields, "Primaries", mediaInfo.VideoColorPrimaries);
            AddInspectorFieldIfKnown(videoFields, "Transfer", mediaInfo.VideoColorTransfer);

            VideoInfoSection audioSection;
            if (!mediaInfo.HasAudioStream)
            {
                audioSection = new VideoInfoSection(
                    Array.Empty<VideoInfoField>(),
                    "No audio stream detected.");
            }
            else
            {
                audioSection = new VideoInfoSection(new[]
                {
                    new VideoInfoField("Codec", FormatInspectorText(mediaInfo.AudioCodecName)),
                    new VideoInfoField("Sample rate", FormatInspectorSampleRate(mediaInfo.AudioSampleRate)),
                    new VideoInfoField("Channels", FormatInspectorChannelCount(mediaInfo.AudioChannelCount)),
                    new VideoInfoField("Bit depth", FormatInspectorBitDepth(mediaInfo.AudioBitDepth)),
                    new VideoInfoField("Bitrate", FormatInspectorBitRate(mediaInfo.AudioBitRate))
                });
            }

            var advancedFields = new List<VideoInfoField>
            {
                new VideoInfoField("Video stream index", FormatInspectorIndex(mediaInfo.VideoStreamIndex)),
                new VideoInfoField(
                    "Nominal frame rate",
                    FormatInspectorFraction(
                        mediaInfo.NominalFrameRateNumerator,
                        mediaInfo.NominalFrameRateDenominator)),
                new VideoInfoField(
                    "Stream time base",
                    FormatInspectorFraction(
                        mediaInfo.StreamTimeBaseNumerator,
                        mediaInfo.StreamTimeBaseDenominator))
            };
            if (mediaInfo.HasAudioStream && mediaInfo.AudioStreamIndex >= 0)
            {
                advancedFields.Add(new VideoInfoField("Audio stream index", FormatInspectorIndex(mediaInfo.AudioStreamIndex)));
            }

            return new VideoInfoSnapshot(
                "Video Info - " + paneLabel,
                paneLabel,
                fileName,
                filePath,
                new VideoInfoSection(summaryFields),
                new VideoInfoSection(videoFields),
                audioSection,
                new VideoInfoSection(advancedFields));
        }

        private string FormatInspectorDuration(TimeSpan duration)
        {
            return duration > TimeSpan.Zero
                ? FormatTime(duration)
                : "Unknown";
        }

        private static void AddInspectorFieldIfKnown(ICollection<VideoInfoField> fields, string label, string value)
        {
            if (fields == null || string.IsNullOrWhiteSpace(label) || string.IsNullOrWhiteSpace(value))
            {
                return;
            }

            fields.Add(new VideoInfoField(label, value));
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
            if (string.IsNullOrWhiteSpace(value))
            {
                return "Unknown";
            }

            var trimmedValue = value.Trim();
            return trimmedValue.Equals("unknown", StringComparison.OrdinalIgnoreCase) ||
                   trimmedValue.Equals("unspecified", StringComparison.OrdinalIgnoreCase) ||
                   trimmedValue.StartsWith("reserved", StringComparison.OrdinalIgnoreCase)
                ? "Unknown"
                : trimmedValue;
        }

        private static string FormatInspectorBitDepth(int? bitDepth)
        {
            return bitDepth.HasValue && bitDepth.Value > 0
                ? string.Format(CultureInfo.InvariantCulture, "{0}-bit", bitDepth.Value)
                : "Unknown";
        }

        private static string FormatInspectorBitRate(long? bitsPerSecond)
        {
            if (!bitsPerSecond.HasValue || bitsPerSecond.Value <= 0L)
            {
                return "Unknown";
            }

            var units = new[] { "bit/s", "Kbit/s", "Mbit/s", "Gbit/s" };
            var displayValue = (double)bitsPerSecond.Value;
            var unitIndex = 0;
            while (displayValue >= 1000d && unitIndex < units.Length - 1)
            {
                displayValue /= 1000d;
                unitIndex++;
            }

            var kibibytesPerSecond = bitsPerSecond.Value / 8d / 1024d;
            return string.Format(
                CultureInfo.InvariantCulture,
                "{0:0.###} {1} ({2:0.###} KiB/s)",
                displayValue,
                units[unitIndex],
                kibibytesPerSecond);
        }

        private static string FormatInspectorSampleRate(int sampleRate)
        {
            return sampleRate > 0
                ? string.Format(CultureInfo.InvariantCulture, "{0:N0} Hz", sampleRate)
                : "Unknown";
        }

        private static string FormatInspectorChannelCount(int channelCount)
        {
            return channelCount > 0
                ? string.Format(CultureInfo.InvariantCulture, "{0} channel(s)", channelCount)
                : "Unknown";
        }

        private static string FormatInspectorIndex(int streamIndex)
        {
            return streamIndex >= 0
                ? streamIndex.ToString(CultureInfo.InvariantCulture)
                : "Unknown";
        }

        private static string FormatInspectorFraction(int numerator, int denominator)
        {
            return numerator > 0 && denominator > 0
                ? string.Format(CultureInfo.InvariantCulture, "{0}/{1}", numerator, denominator)
                : "Unknown";
        }

        private void ShowHelpWindow()
        {
            var helpWindow = new HelpWindow
            {
                Owner = this
            };
            helpWindow.ShowDialog();
        }

        private void ShowAboutWindow()
        {
            var aboutWindow = new AboutWindow(GetApplicationVersion())
            {
                Owner = this
            };
            aboutWindow.ShowDialog();
        }

        private void ExportDiagnostics()
        {
            var dialog = new SaveFileDialog
            {
                Title = "Export Diagnostics",
                Filter = "Text Files|*.txt|All Files|*.*",
                FileName = string.Format(
                    CultureInfo.InvariantCulture,
                    "FramePlayer-diagnostics-{0:yyyyMMdd-HHmmss}.txt",
                    DateTime.Now)
            };

            if (dialog.ShowDialog(this) != true)
            {
                return;
            }

            try
            {
                var currentPosition = GetDisplayPosition();
                var currentFrameIndex = _isMediaLoaded ? GetCurrentFrameIndex(currentPosition) : -1L;
                var totalFrames = _isMediaLoaded ? GetTotalFrameCount() : -1L;
                var displayedCurrentFrame = _isMediaLoaded
                    ? GetDisplayedFrameNumber(currentFrameIndex).ToString(CultureInfo.InvariantCulture)
                    : "--";
                var displayedTotalFrame = totalFrames > 0
                    ? GetDisplayedTotalFrameValue(totalFrames).ToString(CultureInfo.InvariantCulture)
                    : string.Empty;
                var focusedEngine = GetFocusedPaneEngine();
                var ffmpegEngine = focusedEngine as FfmpegReviewEngine;

                var report = _diagnosticLogService.BuildReport(new[]
                {
                    "Frame Player Diagnostics",
                    string.Format(CultureInfo.InvariantCulture, "Generated: {0:yyyy-MM-dd HH:mm:ss.fff zzz}", DateTime.Now),
                    string.Format(CultureInfo.InvariantCulture, "Session started: {0:yyyy-MM-dd HH:mm:ss.fff zzz}", _diagnosticLogService.SessionStarted),
                    "Build variant: " + _buildVariant.BuildDisplayName,
                    "Forced backend: " + _buildVariant.ForcedBackend,
                    "Timed playback supported: " + (_buildVariant.SupportsTimedPlayback ? "Yes" : "No"),
                    "Frame display mode: " + (_buildVariant.UsesZeroIndexedFrameDisplay ? "Zero-indexed" : "1-based"),
                    "Version: " + GetApplicationVersion(),
                    "OS: " + Environment.OSVersion,
                    ".NET: " + Environment.Version,
                    "Runtime available: " + (App.HasBundledFfmpegRuntime ? "Yes" : "No"),
                    "Runtime status: " + GetRuntimeStatusMessage(),
                    "Export tools available: " + (_clipExportService.IsBundledToolingAvailable ? "Yes" : "No"),
                    "Export tools status: " + _clipExportService.GetToolAvailabilityMessage(),
                    "Latest session log: " + GetSafeFileDisplay(_diagnosticLogService.LatestLogPath),
                    "Current file: " + GetSafeFileDisplay(_currentFilePath),
                    "Media loaded: " + (_isMediaLoaded ? "Yes" : "No"),
                    "Playback state: " + (_isPlaying ? "Playing" : "Paused/Idle"),
                    "Audio stream: " + (focusedEngine != null && focusedEngine.MediaInfo.HasAudioStream ? "Yes" : "No"),
                    "Audio playback available: " + (focusedEngine != null && focusedEngine.MediaInfo.IsAudioPlaybackAvailable ? "Yes" : "No"),
                    "Audio codec: " + (focusedEngine == null || string.IsNullOrWhiteSpace(focusedEngine.MediaInfo.AudioCodecName) ? "(none)" : focusedEngine.MediaInfo.AudioCodecName),
                    "Audio details: " + GetAudioTooltipText(focusedEngine != null ? focusedEngine.MediaInfo : VideoMediaInfo.Empty),
                    "Frame index status: " + (ffmpegEngine != null ? ffmpegEngine.GlobalFrameIndexStatus : "(unavailable)"),
                    "Frame index available: " + (ffmpegEngine != null && ffmpegEngine.IsGlobalFrameIndexAvailable ? "Yes" : "No"),
                    "Indexed frame count: " + (ffmpegEngine != null ? ffmpegEngine.IndexedFrameCount.ToString(CultureInfo.InvariantCulture) : "(unavailable)"),
                    "Decode backend: " + (ffmpegEngine != null ? ffmpegEngine.ActiveDecodeBackend : "(unavailable)"),
                    "GPU active: " + (ffmpegEngine != null && ffmpegEngine.IsGpuActive ? "Yes" : "No"),
                    "GPU status: " + (ffmpegEngine != null ? ffmpegEngine.GpuCapabilityStatus : "(unavailable)"),
                    "GPU fallback reason: " + (ffmpegEngine != null && !string.IsNullOrWhiteSpace(ffmpegEngine.GpuFallbackReason) ? ffmpegEngine.GpuFallbackReason : "(none)"),
                    "Decode cache budget: " + (ffmpegEngine != null
                        ? string.Format(CultureInfo.InvariantCulture, "{0:0.0} MiB", ffmpegEngine.DecodedFrameCacheBudgetBytes / 1048576d)
                        : "(unavailable)"),
                    "Operational queue depth: " + (ffmpegEngine != null ? ffmpegEngine.OperationalQueueDepth.ToString(CultureInfo.InvariantCulture) : "(unavailable)"),
                    "Review cache window: " + (ffmpegEngine != null
                        ? string.Format(
                            CultureInfo.InvariantCulture,
                            "{0} back / {1} ahead cached, max {2} back / {3} ahead, approx {4:0.0} MiB",
                            ffmpegEngine.PreviousCachedFrameCount,
                            ffmpegEngine.ForwardCachedFrameCount,
                            ffmpegEngine.MaxPreviousCachedFrameCount,
                            ffmpegEngine.MaxForwardCachedFrameCount,
                            ffmpegEngine.ApproximateCachedFrameBytes / 1048576d)
                        : "(unavailable)"),
                    "Last cache refill: " + (ffmpegEngine != null
                        ? string.Format(
                            CultureInfo.InvariantCulture,
                            "{0}, {1:0.0} ms, mode {2}, after landing {3}, forward {4}->{5}",
                            string.IsNullOrWhiteSpace(ffmpegEngine.LastCacheRefillReason) ? "(none)" : ffmpegEngine.LastCacheRefillReason,
                            ffmpegEngine.LastCacheRefillMilliseconds,
                            string.IsNullOrWhiteSpace(ffmpegEngine.LastCacheRefillMode) ? "none" : ffmpegEngine.LastCacheRefillMode,
                            ffmpegEngine.LastCacheRefillAfterLanding ? "yes" : "no",
                            ffmpegEngine.LastCacheRefillStartingForwardCount,
                            ffmpegEngine.LastCacheRefillCompletedForwardCount)
                        : "(unavailable)"),
                    "Open timing: " + (ffmpegEngine != null
                        ? string.Format(
                            CultureInfo.InvariantCulture,
                            "total {0:0.0} ms, container/probe {1:0.0} ms, stream {2:0.0} ms, audio probe {3:0.0} ms, decoder {4:0.0} ms, first frame {5:0.0} ms, cache warm {6:0.0} ms, index build {7:0.0} ms",
                            ffmpegEngine.LastOpenTotalMilliseconds,
                            ffmpegEngine.LastOpenContainerProbeMilliseconds,
                            ffmpegEngine.LastOpenStreamDiscoveryMilliseconds,
                            ffmpegEngine.LastOpenAudioProbeMilliseconds,
                            ffmpegEngine.LastOpenVideoDecoderInitializationMilliseconds,
                            ffmpegEngine.LastOpenFirstFrameDecodeMilliseconds,
                            ffmpegEngine.LastOpenInitialCacheWarmMilliseconds,
                            ffmpegEngine.LastGlobalFrameIndexBuildMilliseconds)
                        : "(unavailable)"),
                    "Full screen: " + (_isFullScreen ? "Yes" : "No"),
                    string.Format(CultureInfo.InvariantCulture, "Clock position: {0} / {1}", FormatTime(currentPosition), FormatTime(_mediaDuration)),
                    string.Format(CultureInfo.InvariantCulture, "Timecode: {0}", _isMediaLoaded ? FormatTimecode(currentFrameIndex) : "--:--:--:--"),
                    string.Format(
                        CultureInfo.InvariantCulture,
                        "Frame: {0}{1}",
                        displayedCurrentFrame,
                        !string.IsNullOrWhiteSpace(displayedTotalFrame) ? " / " + displayedTotalFrame : string.Empty),
                    string.Format(CultureInfo.InvariantCulture, "Frame rate: {0:0.###} fps", _framesPerSecond),
                    "Frame step: " + FormatStepDuration(_positionStep),
                    "Last error: " + (string.IsNullOrWhiteSpace(_lastMediaErrorMessage) ? "(none)" : _lastMediaErrorMessage)
                });

                File.WriteAllText(dialog.FileName, report);
                LogInfo("Diagnostics exported to " + GetSafeFileDisplay(dialog.FileName));
                SetPlaybackMessage("Diagnostics exported.");
            }
            catch (Exception ex)
            {
                LogError("Diagnostics export failed.", ex);
                MessageBox.Show(
                    this,
                    "Frame Player could not write the diagnostics file.\r\n\r\n" + ex.Message,
                    "Export Failed",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }

        private static string GetApplicationVersion()
        {
            var assembly = Assembly.GetExecutingAssembly();
            var informationalVersion = assembly
                .GetCustomAttribute<AssemblyInformationalVersionAttribute>()
                ?.InformationalVersion;
            if (!string.IsNullOrWhiteSpace(informationalVersion))
            {
                return informationalVersion;
            }

            var version = assembly.GetName().Version;
            return version != null
                ? version.ToString()
                : "unknown";
        }

        private static string GetSafeFileDisplay(string filePath)
        {
            return string.IsNullOrWhiteSpace(filePath)
                ? "(none)"
                : Path.GetFileName(filePath);
        }

        private static string GetRuntimeStatusMessage()
        {
            return !string.IsNullOrWhiteSpace(App.RuntimeValidationMessage)
                ? App.RuntimeValidationMessage
                : "The bundled FFmpeg runtime could not be validated.";
        }

        private static string SanitizeSensitiveText(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return string.Empty;
            }

            return Regex.Replace(
                value,
                @"(?i)(?:[A-Z]:\\|\\\\)[^\r\n]+?(?=(?:\s|$))",
                match => GetSafeFileDisplay(match.Value));
        }

        private void LogInfo(string message)
        {
            _diagnosticLogService.Info(message);
        }

        private void LogWarning(string message)
        {
            _diagnosticLogService.Warn(message);
        }

        private void LogError(string message)
        {
            _diagnosticLogService.Error(message);
        }

        private void LogError(string message, Exception exception)
        {
            if (exception == null)
            {
                LogError(message);
                return;
            }

            LogError(string.Format(
                CultureInfo.InvariantCulture,
                "{0} {1}: {2}",
                message,
                exception.GetType().Name,
                SanitizeSensitiveText(exception.Message)));
        }

        private void LogSeekResult(string diagnosticSource, TimeSpan requestedTarget, TimeSpan clampedTarget)
        {
            var landedPosition = GetDisplayPosition();
            var focusedEngine = GetFocusedPaneEngine();
            var enginePosition = focusedEngine != null ? focusedEngine.Position : null;
            var frameText = "(unavailable)";
            var frameIdentity = "unavailable";
            if (enginePosition != null && enginePosition.FrameIndex.HasValue)
            {
                frameText = GetDisplayedFrameNumber(enginePosition.FrameIndex.Value).ToString(CultureInfo.InvariantCulture);
                frameIdentity = enginePosition.IsFrameIndexAbsolute ? "absolute" : "segment-local";
            }

            LogInfo(string.Format(
                CultureInfo.InvariantCulture,
                "{0}: requested {1}; committed {2}; engine landed {3}; engine frame {4}; identity {5}; displayed frame input {6}{7}.",
                diagnosticSource,
                FormatTime(ClampPosition(requestedTarget)),
                FormatTime(clampedTarget),
                FormatTime(landedPosition),
                frameText,
                frameIdentity,
                string.IsNullOrWhiteSpace(FrameNumberTextBox.Text) ? "(blank)" : FrameNumberTextBox.Text,
                GetSeekTimingDiagnosticSuffix()));
        }

        private string GetSeekTimingDiagnosticSuffix()
        {
            var ffmpegEngine = GetFocusedFfmpegEngine();
            if (ffmpegEngine == null)
            {
                return string.Empty;
            }

            return string.Format(
                CultureInfo.InvariantCulture,
                "; seek timing total {0:0.0} ms, index wait {1:0.0} ms, materialize {2:0.0} ms, forward warm {3:0.0} ms, mode {4}; cache refill {5} {6:0.0} ms ({7}, forward {8}->{9})",
                ffmpegEngine.LastSeekTotalMilliseconds,
                ffmpegEngine.LastSeekIndexWaitMilliseconds,
                ffmpegEngine.LastSeekMaterializeMilliseconds,
                ffmpegEngine.LastSeekForwardCacheWarmMilliseconds,
                string.IsNullOrWhiteSpace(ffmpegEngine.LastSeekMode) ? "(none)" : ffmpegEngine.LastSeekMode,
                string.IsNullOrWhiteSpace(ffmpegEngine.LastCacheRefillReason) ? "(none)" : ffmpegEngine.LastCacheRefillReason,
                ffmpegEngine.LastCacheRefillMilliseconds,
                string.IsNullOrWhiteSpace(ffmpegEngine.LastCacheRefillMode) ? "none" : ffmpegEngine.LastCacheRefillMode,
                ffmpegEngine.LastCacheRefillStartingForwardCount,
                ffmpegEngine.LastCacheRefillCompletedForwardCount);
        }

        private TimeSpan GetDisplayPosition()
        {
            if (!_isMediaLoaded)
            {
                return TimeSpan.Zero;
            }

            return _workspaceCoordinator.CurrentSession.Position.PresentationTime;
        }

        private void BeginHeldFrameStep(int direction)
        {
            if (!_isMediaLoaded || _isPlaying || direction == 0)
            {
                return;
            }

            _heldFrameStepDirection = direction < 0 ? -1 : 1;
            _frameStepRepeatTimer.Stop();
            _frameStepRepeatTimer.Interval = FrameStepInitialDelay;
            _frameStepRepeatTimer.Start();
            _ = StepFrameAsync(_heldFrameStepDirection);
        }

        private void EndHeldFrameStep()
        {
            _heldFrameStepDirection = 0;
            _frameStepRepeatTimer.Stop();
            _frameStepRepeatTimer.Interval = FrameStepInitialDelay;
        }

        private void FrameStepRepeatTimer_Tick(object sender, EventArgs e)
        {
            if (_heldFrameStepDirection == 0 || !_isMediaLoaded || _isPlaying)
            {
                EndHeldFrameStep();
                return;
            }

            _frameStepRepeatTimer.Interval = FrameStepRepeatInterval;
            _ = StepFrameAsync(_heldFrameStepDirection);
        }

        [DllImport("dwmapi.dll")]
        private static extern int DwmSetWindowAttribute(
            IntPtr hwnd,
            int dwAttribute,
            ref DwmWindowCornerPreference pvAttribute,
            int cbAttribute);

        private enum DwmWindowCornerPreference
        {
            Default = 0,
            DoNotRound = 1,
            Round = 2,
            RoundSmall = 3
        }
    }
}
