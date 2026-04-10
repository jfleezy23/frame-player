using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using FramePlayer.Core.Abstractions;
using FramePlayer.Core.Coordination;
using FramePlayer.Core.Events;
using FramePlayer.Core.Models;
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
        private static readonly TimeSpan SeekJump = TimeSpan.FromSeconds(5);
        private static readonly TimeSpan FrameStepInitialDelay = TimeSpan.FromMilliseconds(550);
        private static readonly TimeSpan FrameStepRepeatInterval = TimeSpan.FromMilliseconds(60);

        private readonly DispatcherTimer _positionTimer;
        private readonly DispatcherTimer _frameStepRepeatTimer;
        private readonly BuildVariantInfo _buildVariant;
        private readonly AppPreferencesService _appPreferencesService;
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
        private bool _suppressSliderUpdate;
        private double _framesPerSecond;
        private TimeSpan _positionStep;
        private TimeSpan _mediaDuration;
        private string _currentFilePath;
        private string _lastMediaErrorMessage;
        private bool _isCacheStatusActive;
        private string _lastCompareAlignmentStatus = DefaultCompareAlignmentStatus;

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

            _buildVariant = BuildVariantInfo.Current;
            CustomVideoSurfaceHost.Visibility = Visibility.Visible;

            _appPreferencesService = new AppPreferencesService();
            _diagnosticLogService = new DiagnosticLogService();
            _ffmpegReviewEngineOptionsProvider = new FfmpegReviewEngineOptionsProvider(_appPreferencesService);
            _recentFilesService = new RecentFilesService();
            _videoReviewEngineFactory = new VideoReviewEngineFactory(_ffmpegReviewEngineOptionsProvider);
            UseGpuAccelerationMenuItem.IsChecked = _ffmpegReviewEngineOptionsProvider.UseGpuAcceleration;
            _videoReviewEngine = CreateVideoReviewEngine();
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

            RefreshRecentFilesMenu();
            UpdateCompareModeVisualState();
            UpdateHeader();
            UpdatePositionDisplay(TimeSpan.Zero);
            UpdateFullScreenVisualState();
            UpdateTransportState();
            UpdateWorkspacePanePresentation();

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

        private IVideoReviewEngine CreateVideoReviewEngine()
        {
            return _videoReviewEngineFactory.Create();
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

        private static string GetSupportedVideoExtensionsDescription()
        {
            return "AVI, MOV, M4V, MP4, MKV, WMV, TS";
        }

        private static string GetOpenFileFilter()
        {
            return "Supported Video Files|*.avi;*.mov;*.m4v;*.mp4;*.mkv;*.wmv;*.ts|AVI Files|*.avi|MOV Files|*.mov|M4V Files|*.m4v|MP4 Files|*.mp4|MKV Files|*.mkv|WMV Files|*.wmv|TS Files|*.ts|All Files|*.*";
        }

        private bool IsCompareModeEnabled
        {
            get { return CompareModeCheckBox != null && CompareModeCheckBox.IsChecked == true; }
        }

        private bool IsAllPaneTransportEnabled
        {
            get { return IsCompareModeEnabled && AllPanesCheckBox != null && AllPanesCheckBox.IsChecked == true; }
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

            _compareVideoReviewEngine = CreateVideoReviewEngine();
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

            var canAlign = canRunCompareActions;
            AlignRightToLeftButton.IsEnabled = canAlign;
            AlignLeftToRightButton.IsEnabled = canAlign;
            AlignRightToLeftButton.ToolTip = canAlign
                ? "Frame first: aligns the right pane to the left pane with exact frame identity when available."
                : "Load videos in both panes before aligning them.";
            AlignLeftToRightButton.ToolTip = canAlign
                ? "Frame first: aligns the left pane to the right pane with exact frame identity when available."
                : "Load videos in both panes before aligning them.";

            AllPanesCheckBox.IsEnabled = canRunCompareActions;
            AllPanesCheckBox.ToolTip = canRunCompareActions
                ? "The main playback, seek, and frame-step controls apply to both loaded panes."
                : "Load videos in both panes before controlling both panes together.";
            if (!canRunCompareActions)
            {
                ResetCompareAlignmentStatus();
                if (AllPanesCheckBox.IsChecked == true)
                {
                    AllPanesCheckBox.IsChecked = false;
                }
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
                    ReviewPosition.Empty);
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
            if (AllPanesCheckBox != null && !AllPanesCheckBox.IsEnabled)
            {
                AllPanesCheckBox.IsChecked = false;
                SetPlaybackMessage("Load videos in both panes to control both together.");
                return;
            }

            SetPlaybackMessage("Controlling both loaded panes.");
        }

        private void AllPanesCheckBox_Unchecked(object sender, RoutedEventArgs e)
        {
            if (_isMediaLoaded)
            {
                UpdateTransportState();
            }
        }

        private async void PanePlayButton_Click(object sender, RoutedEventArgs e)
        {
            await RunFocusedPaneActionAsync(
                GetPaneIdFromSender(sender),
                () => StartPlaybackAsync(SynchronizedOperationScope.FocusedPane));
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

        private void ToggleFullScreenButton_Click(object sender, RoutedEventArgs e)
        {
            ToggleFullScreen();
        }

        private async void PlayPauseButton_Click(object sender, RoutedEventArgs e)
        {
            await TogglePlaybackAsync();
        }

        private async void RewindButton_Click(object sender, RoutedEventArgs e)
        {
            await SeekRelativeAsync(-SeekJump);
        }

        private async void FastForwardButton_Click(object sender, RoutedEventArgs e)
        {
            await SeekRelativeAsync(SeekJump);
        }

        private async void PreviousFrameButton_Click(object sender, RoutedEventArgs e)
        {
            await StepFrameAsync(-1);
        }

        private async void NextFrameButton_Click(object sender, RoutedEventArgs e)
        {
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
            FrameNumberTextBox.SelectAll();
        }

        private async void PositionSlider_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (!_isMediaLoaded)
            {
                return;
            }

            if (IsSliderThumbSource(e.OriginalSource as DependencyObject))
            {
                _isSliderDragActive = true;
                return;
            }

            _isSliderDragActive = false;
            e.Handled = true;

            TimeSpan clickedTarget;
            if (TryMoveSliderToPoint(e, out clickedTarget))
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
            await CommitSliderSeekAsync("drag", TimeSpan.FromSeconds(PositionSlider.Value));
        }

        private bool TryMoveSliderToPoint(MouseButtonEventArgs e, out TimeSpan target)
        {
            target = TimeSpan.Zero;
            if (PositionSlider.Maximum <= PositionSlider.Minimum)
            {
                return false;
            }

            var track = PositionSlider.Template.FindName("PART_Track", PositionSlider) as Track;
            double value;
            if (track != null)
            {
                value = track.ValueFromPoint(e.GetPosition(track));
            }
            else
            {
                if (PositionSlider.ActualWidth <= 0d)
                {
                    return false;
                }

                var point = e.GetPosition(PositionSlider);
                var ratio = Math.Max(0d, Math.Min(1d, point.X / PositionSlider.ActualWidth));
                if (PositionSlider.FlowDirection == FlowDirection.RightToLeft)
                {
                    ratio = 1d - ratio;
                }

                value = PositionSlider.Minimum + ((PositionSlider.Maximum - PositionSlider.Minimum) * ratio);
            }

            value = Math.Max(PositionSlider.Minimum, Math.Min(PositionSlider.Maximum, value));
            PositionSlider.Value = value;
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
        }

        private async void Window_Drop(object sender, DragEventArgs e)
        {
            var files = e.Data.GetData(DataFormats.FileDrop) as string[];
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
                return;
            }

            if (!EnsureRuntimeAvailable())
            {
                return;
            }

            await OpenMediaAsync(firstSupportedFile);
        }

        private void Window_PreviewDragOver(object sender, DragEventArgs e)
        {
            var files = e.Data.GetData(DataFormats.FileDrop) as string[];
            e.Effects = files != null && files.Any(IsSupportedVideoFile)
                ? DragDropEffects.Copy
                : DragDropEffects.None;
            e.Handled = true;
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

            if (FrameNumberTextBox.IsKeyboardFocusWithin)
            {
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

        private async Task OpenMediaAsync(string filePath)
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

            TrySelectPaneForShell(string.IsNullOrWhiteSpace(paneId) ? GetFocusedPaneId() : paneId);

            var dialog = new OpenFileDialog
            {
                Title = "Open Video File",
                Filter = GetOpenFileFilter()
            };

            if (dialog.ShowDialog(this) == true)
            {
                await OpenMediaAsync(dialog.FileName);
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

            await StartPlaybackAsync(operationScope);
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
            return StartPlaybackAsync((SynchronizedOperationScope?)null);
        }

        private async Task StartPlaybackAsync(SynchronizedOperationScope? operationScope)
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
            await _workspaceCoordinator.PlayAsync(operationScope ?? GetRequestedOperationScope());
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
            if (!_isMediaLoaded || _isFrameStepInProgress)
            {
                return;
            }

            _isFrameStepInProgress = true;

            try
            {
                await PausePlaybackAsync(logAction: false, operationScope: operationScope);

                FrameStepResult stepResult = null;
                await RunWithCacheStatusAsync(
                    delta < 0 ? "Cache: checking backward window..." : "Cache: refilling forward window...",
                    async () =>
                    {
                        stepResult = delta < 0
                            ? await _workspaceCoordinator.StepBackwardAsync(operationScope ?? GetRequestedOperationScope())
                            : await _workspaceCoordinator.StepForwardAsync(operationScope ?? GetRequestedOperationScope());
                    });

                if (!stepResult.Success)
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
                () => _workspaceCoordinator.SeekToFrameAsync(targetFrameIndex));
            FocusPreferredVideoSurface();
            UpdatePositionDisplay(GetDisplayPosition());
            LogInfo(string.Format(
                CultureInfo.InvariantCulture,
                "Jumped to frame {0} at {1}.",
                GetDisplayedFrameNumber(targetFrameIndex),
                FormatTime(GetDisplayPosition())));
        }

        private void PositionTimer_Tick(object sender, EventArgs e)
        {
            if (!_isMediaLoaded || _isSliderDragActive)
            {
                return;
            }

            UpdatePositionDisplay(GetDisplayPosition());
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
            PositionSlider.IsEnabled = canControl;
            FrameNumberTextBox.IsEnabled = canControl;
            ToggleFullScreenButton.IsEnabled = canControl;
            OverlayToggleFullScreenButton.IsEnabled = canControl;
            FullscreenControlBar.IsEnabled = canControl;
            UpdatePlayPauseToggleVisuals();
            UpdateWorkspacePanePresentation();

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

            if (_isMediaLoaded)
            {
                UpdateCurrentFrameDisplay(currentPosition);

                long currentFrameIndex;
                bool isAbsoluteFrameIndex;
                if (!FrameNumberTextBox.IsKeyboardFocusWithin &&
                    TryGetCurrentEngineFrameIndex(out currentFrameIndex, out isAbsoluteFrameIndex))
                {
                    FrameNumberTextBox.Text = GetDisplayedFrameNumber(currentFrameIndex).ToString(CultureInfo.InvariantCulture);
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

            if (!enginePosition.IsFrameIndexAbsolute)
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
                || extension.Equals(".wmv", StringComparison.OrdinalIgnoreCase)
                || extension.Equals(".ts", StringComparison.OrdinalIgnoreCase);
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
            StatusPanel.Visibility = _isFullScreen ? Visibility.Collapsed : Visibility.Visible;
            FullscreenControlBar.Visibility = _isFullScreen ? Visibility.Visible : Visibility.Collapsed;

            RootGrid.Margin = _isFullScreen ? new Thickness(0) : new Thickness(10);
            VideoPanel.Margin = _isFullScreen ? new Thickness(0) : new Thickness(0, 8, 0, 0);
            VideoPanel.CornerRadius = _isFullScreen ? new CornerRadius(0) : new CornerRadius(12);

            UpdateFullScreenButtonIcon();
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
                return;
            }

            CustomVideoSurface.Source = null;
        }

        private void ResetMediaState(bool clearFilePath, bool clearErrorMessage)
        {
            _isFrameStepInProgress = false;
            _isCacheStatusActive = false;
            _heldFrameStepDirection = 0;
            _frameStepRepeatTimer.Stop();
            _isSliderDragActive = false;
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
    }
}
