using System;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Input;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using FramePlayer.Core.Abstractions;
using FramePlayer.Core.Coordination;
using FramePlayer.Core.Events;
using FramePlayer.Core.Models;
using FramePlayer.Engines.FFmpeg;
using FramePlayer.Desktop.Services;
using FramePlayer.Services;

namespace FramePlayer.Desktop.Views
{
    public sealed partial class MainWindow : Window
    {
        private readonly VideoReviewEngineFactory _engineFactory;
        private readonly DesktopRecentFilesService _recentFilesService;
        private readonly FfmpegReviewEngineOptionsProvider _optionsProvider;
        private readonly IVideoReviewEngine _primaryEngine;
        private IVideoReviewEngine? _compareEngine;
        private DecodedFrameBuffer? _primaryFrameBuffer;
        private DecodedFrameBuffer? _compareFrameBuffer;
        private const int HundredFrameStep = 100;
        private const double MinimumPaneZoomFactor = 1d;
        private const double MaximumPaneZoomFactor = 12d;
        private const double PaneZoomStep = 1.1d;
        private const double PaneWheelZoomStep = 1.08d;
        private const double PaneWheelZoomDeltaLimit = 4d;
        private const double PaneWheelZoomDeltaThreshold = 0.01d;
        private const string PanePrimaryId = "pane-primary";
        private const string PaneCompareId = "pane-compare";
        private const string PanePrimaryKey = "primary";
        private const string PaneCompareKey = "compare";
        private const string PanePrimaryLabel = "Primary";
        private const string PaneCompareLabel = "Compare";
        private const string Mp4Pattern = "*.mp4";
        private const string Mp4Extension = ".mp4";
        private static readonly string[] VideoFilePatterns = new[] { "*.avi", "*.m4v", Mp4Pattern, "*.mkv", "*.wmv", "*.mov" };
        private NativeMenuItem? _nativeRecentFilesMenuItem;
        private NativeMenuItem? _nativePlayPauseMenuItem;
        private NativeMenuItem? _nativeGpuAccelerationMenuItem;
        private NativeMenuItem? _nativeLoopPlaybackMenuItem;
        private LoopPlaybackPaneRangeSnapshot _primaryLoopRange;
        private LoopPlaybackPaneRangeSnapshot _compareLoopRange;
        private bool _isLoopPlaybackEnabled;
        private bool _isLoopRestartInFlight;
        private bool _isUpdatingSliders;
        private Pane _focusedPane = Pane.Primary;
        private TimeSpan _masterTimelineContextTarget = TimeSpan.Zero;
        private TimeSpan _primaryTimelineContextTarget = TimeSpan.Zero;
        private TimeSpan _compareTimelineContextTarget = TimeSpan.Zero;
        private double _primaryZoomFactor = MinimumPaneZoomFactor;
        private double _compareZoomFactor = MinimumPaneZoomFactor;
        private static readonly IBrush PaneChromeBrush = Brush.Parse("#171C22");
        private static readonly IBrush PaneChromeBorderBrush = Brush.Parse("#28313B");
        private static readonly IBrush PaneSelectedBrush = Brush.Parse("#1D2934");
        private static readonly IBrush PaneSelectedBorderBrush = Brush.Parse("#5AA9E6");
        private static KeyModifiers CommandKeyModifier
        {
            get
            {
                return RuntimeInformation.IsOSPlatform(OSPlatform.OSX)
                    ? KeyModifiers.Meta
                    : KeyModifiers.Control;
            }
        }

        private static string CommandKeyLabel
        {
            get
            {
                return RuntimeInformation.IsOSPlatform(OSPlatform.OSX) ? "Cmd" : "Ctrl";
            }
        }

        public MainWindow()
        {
            InitializeComponent();

            if (string.Equals(Environment.GetEnvironmentVariable("FRAMEPLAYER_DESKTOP_SKIP_RUNTIME_BOOTSTRAP"), "1", StringComparison.Ordinal))
            {
                CacheStatusTextBlock.Text = "Runtime: skipped";
            }
            else
            {
                try
                {
                    var configuredRuntime = FfmpegRuntimeBootstrap.ConfigureForCurrentPlatform(AppContext.BaseDirectory);
                    CacheStatusTextBlock.Text = "Runtime: " + configuredRuntime;
                }
                catch (Exception ex)
                {
                    CacheStatusTextBlock.Text = "Runtime unavailable: " + ex.Message;
                }
            }

            _recentFilesService = new DesktopRecentFilesService();
            _optionsProvider = new FfmpegReviewEngineOptionsProvider(new AppPreferencesService());
            _engineFactory = new VideoReviewEngineFactory(_optionsProvider);
            _primaryEngine = _engineFactory.Create(PanePrimaryKey);
            _primaryEngine.StateChanged += PrimaryEngine_StateChanged;
            _primaryEngine.FramePresented += PrimaryEngine_FramePresented;
            _primaryLoopRange = CreateLoopRange(null, null);
            _compareLoopRange = CreateLoopRange(Pane.Compare, null, null);

            ConfigurePaneZoomSurfaces();
            NativeMenu.SetMenu(this, BuildNativeMenu(_optionsProvider.UseGpuAcceleration));
            UpdateRecentFilesMenu();
            UpdatePaneSelectionVisuals();
            UpdateCompareOptionState();
            InstallContextMenus();
            AddHandler(DragDrop.DropEvent, Window_Drop);
            AddHandler(DragDrop.DragOverEvent, Window_DragOver);
            KeyDown += Window_KeyDown;
            AllPanesCheckBox.IsCheckedChanged += AllPanesCheckBox_IsCheckedChanged;
            LinkPaneZoomCheckBox.IsCheckedChanged += LinkPaneZoomCheckBox_IsCheckedChanged;
        }

        private async void OpenVideoMenuItem_Click(object? sender, RoutedEventArgs e)
        {
            await OpenVideoAsync(GetFileOpenTargetPane());
        }

        private async void OpenVideoButton_Click(object? sender, RoutedEventArgs e)
        {
            await OpenVideoAsync(Pane.Primary);
        }

        private async void OpenCompareVideoButton_Click(object? sender, RoutedEventArgs e)
        {
            await OpenVideoAsync(Pane.Compare);
        }

        private async void OpenRecentButton_Click(object? sender, RoutedEventArgs e)
        {
            var recentFiles = _recentFilesService.Load();
            if (recentFiles.Count > 0 && !string.IsNullOrWhiteSpace(recentFiles[0]))
            {
                await OpenRecentPathAsync(recentFiles[0]);
            }
        }

        [SuppressMessage("Major Code Smell", "S1144:Unused private types or members", Justification = "Invoked by the desktop preview parity harness through reflection.")]
        private Task OpenMediaAsync(string filePath)
        {
            return OpenMediaAsync(filePath, PanePrimaryId);
        }

        [SuppressMessage("Major Code Smell", "S1144:Unused private types or members", Justification = "Invoked by the desktop preview parity harness through reflection.")]
        private Task OpenMediaAsync(string filePath, string paneId)
        {
            return OpenPathAsync(filePath, ResolvePane(paneId));
        }

        [SuppressMessage("Major Code Smell", "S1144:Unused private types or members", Justification = "Invoked by the desktop preview parity harness through reflection.")]
        private Task CloseMediaAsync()
        {
            return CloseVideosAsync();
        }

        private async Task OpenVideoAsync(Pane pane)
        {
            SelectPane(pane);
            var topLevel = GetTopLevel(this);
            if (topLevel == null)
            {
                return;
            }

            var files = await topLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = pane == Pane.Compare ? "Open Compare Video" : "Open Video",
                AllowMultiple = false,
                FileTypeFilter = new[]
                {
                    new FilePickerFileType("Video files")
                    {
                        Patterns = VideoFilePatterns
                    }
                }
            });

            var selected = files.Count > 0 ? files[0] : null;
            if (selected == null || string.IsNullOrWhiteSpace(selected.Path.LocalPath))
            {
                return;
            }

            await OpenPathAsync(selected.Path.LocalPath, pane);
        }

        private async Task OpenPathAsync(string filePath, Pane pane)
        {
            if (string.IsNullOrWhiteSpace(filePath))
            {
                return;
            }

            SelectPane(pane);
            var engine = GetEngine(pane);
            SetPaneState(pane, "Opening");
            PlaybackStateTextBlock.Text = "Opening";
            try
            {
                await engine.OpenAsync(filePath);
                _recentFilesService.Add(filePath);
                UpdateRecentFilesMenu();
                ApplyFileLabels(pane, filePath);
                SetPaneZoomFactor(pane, MinimumPaneZoomFactor, synchronizeLinkedPane: false);
                if (pane == Pane.Primary)
                {
                    _primaryLoopRange = CreateLoopRange(Pane.Primary, null, null);
                }
                else
                {
                    _compareLoopRange = CreateLoopRange(Pane.Compare, null, null);
                }

                UpdateLoopUi();
                UpdateCompareOptionState();
                SetPaneState(pane, "Ready");
            }
            catch (Exception ex)
            {
                SetPaneState(pane, "Open failed");
                PlaybackStateTextBlock.Text = "Open failed";
                CacheStatusTextBlock.Text = ex.Message;
            }
        }

        private IVideoReviewEngine GetEngine(Pane pane)
        {
            if (pane == Pane.Primary)
            {
                return _primaryEngine;
            }

            if (_compareEngine == null)
            {
                _compareEngine = _engineFactory.Create(PaneCompareKey);
                _compareEngine.StateChanged += CompareEngine_StateChanged;
                _compareEngine.FramePresented += CompareEngine_FramePresented;
            }

            return _compareEngine;
        }

        private Pane GetFocusedPane()
        {
            return CompareModeCheckBox.IsChecked == true && _focusedPane == Pane.Compare
                ? Pane.Compare
                : Pane.Primary;
        }

        private bool IsCompareModeEnabled
        {
            get { return CompareModeCheckBox.IsChecked == true; }
        }

        private bool IsAllPaneTransportEnabled
        {
            get
            {
                return IsCompareModeEnabled &&
                    AllPanesCheckBox.IsChecked == true &&
                    _primaryEngine.IsMediaOpen &&
                    _compareEngine != null &&
                    _compareEngine.IsMediaOpen;
            }
        }

        private bool IsLinkedPaneZoomEnabled
        {
            get
            {
                return IsCompareModeEnabled &&
                    LinkPaneZoomCheckBox.IsChecked == true;
            }
        }

        private void ConfigurePaneZoomSurfaces()
        {
            CustomVideoSurface.RenderTransformOrigin = new RelativePoint(0.5, 0.5, RelativeUnit.Relative);
            CompareVideoSurface.RenderTransformOrigin = new RelativePoint(0.5, 0.5, RelativeUnit.Relative);
            CustomVideoSurfaceHost.PointerWheelChanged += VideoSurfaceHost_PointerWheelChanged;
            CompareVideoSurfaceHost.PointerWheelChanged += VideoSurfaceHost_PointerWheelChanged;
            CustomVideoSurfaceHost.ScrollGesture += VideoSurfaceHost_ScrollGesture;
            CompareVideoSurfaceHost.ScrollGesture += VideoSurfaceHost_ScrollGesture;
            CustomVideoSurfaceHost.PointerTouchPadGestureMagnify += VideoSurfaceHost_PointerTouchPadGestureMagnify;
            CompareVideoSurfaceHost.PointerTouchPadGestureMagnify += VideoSurfaceHost_PointerTouchPadGestureMagnify;
            CustomVideoSurfaceHost.Pinch += VideoSurfaceHost_Pinch;
            CompareVideoSurfaceHost.Pinch += VideoSurfaceHost_Pinch;
        }

        private void ZoomInFocusedPane()
        {
            AdjustPaneZoom(GetFocusedPane(), PaneZoomStep);
        }

        private void ZoomOutFocusedPane()
        {
            AdjustPaneZoom(GetFocusedPane(), 1d / PaneZoomStep);
        }

        private void ResetZoomForFocusedPane()
        {
            ResetZoomForPane(GetFocusedPane());
        }

        private void ResetZoomForPane(Pane pane)
        {
            if (GetPaneZoomFactor(pane) <= MinimumPaneZoomFactor + 0.0001d)
            {
                CacheStatusTextBlock.Text = ResolvePaneLabel(pane) + " already showing full frame.";
                return;
            }

            SetPaneZoomFactor(pane, MinimumPaneZoomFactor, synchronizeLinkedPane: true);
            CacheStatusTextBlock.Text = ResolvePaneLabel(pane) + " zoom reset.";
        }

        private void AdjustPaneZoom(Pane pane, double scaleMultiplier)
        {
            var currentZoom = GetPaneZoomFactor(pane);
            var targetZoom = Math.Max(
                MinimumPaneZoomFactor,
                Math.Min(MaximumPaneZoomFactor, currentZoom * scaleMultiplier));
            if (targetZoom <= MinimumPaneZoomFactor + 0.0001d)
            {
                targetZoom = MinimumPaneZoomFactor;
            }

            if (Math.Abs(targetZoom - currentZoom) < 0.0001d)
            {
                CacheStatusTextBlock.Text = ResolvePaneLabel(pane) + " is already at " + FormatZoomFactor(targetZoom) + ".";
                return;
            }

            SetPaneZoomFactor(pane, targetZoom, synchronizeLinkedPane: true);
            CacheStatusTextBlock.Text = string.Format(
                CultureInfo.InvariantCulture,
                "{0} zoom: {1}.",
                ResolvePaneLabel(pane),
                FormatZoomFactor(targetZoom));
        }

        private void ApplyPaneWheelZoom(Pane pane, double deltaY)
        {
            if (Math.Abs(deltaY) < PaneWheelZoomDeltaThreshold)
            {
                return;
            }

            var limitedDelta = Math.Max(-PaneWheelZoomDeltaLimit, Math.Min(PaneWheelZoomDeltaLimit, deltaY));
            AdjustPaneZoom(pane, Math.Pow(PaneWheelZoomStep, limitedDelta));
        }

        private void ApplyPaneMagnifyZoom(Pane pane, double magnificationDelta)
        {
            if (Math.Abs(magnificationDelta) < PaneWheelZoomDeltaThreshold)
            {
                return;
            }

            AdjustPaneZoom(pane, Math.Max(0.25d, 1d + magnificationDelta));
        }

        private void VideoSurfaceHost_PointerWheelChanged(object? sender, PointerWheelEventArgs e)
        {
            var pane = ResolvePaneFromSender(sender);
            ApplyPaneWheelZoom(pane, e.Delta.Y);
            e.Handled = true;
        }

        private void VideoSurfaceHost_ScrollGesture(object? sender, ScrollGestureEventArgs e)
        {
            var pane = ResolvePaneFromSender(sender);
            ApplyPaneWheelZoom(pane, e.Delta.Y);
            e.Handled = true;
        }

        private void VideoSurfaceHost_PointerTouchPadGestureMagnify(object? sender, PointerDeltaEventArgs e)
        {
            var pane = ResolvePaneFromSender(sender);
            ApplyPaneMagnifyZoom(pane, Math.Abs(e.Delta.X) >= Math.Abs(e.Delta.Y) ? e.Delta.X : e.Delta.Y);
            e.Handled = true;
        }

        private void VideoSurfaceHost_Pinch(object? sender, PinchEventArgs e)
        {
            var pane = ResolvePaneFromSender(sender);
            ApplyPaneMagnifyZoom(pane, e.Scale - 1d);
            e.Handled = true;
        }

        private void SetPaneZoomFactor(Pane pane, double zoomFactor, bool synchronizeLinkedPane)
        {
            var clampedZoomFactor = Math.Max(MinimumPaneZoomFactor, Math.Min(MaximumPaneZoomFactor, zoomFactor));
            if (pane == Pane.Compare)
            {
                _compareZoomFactor = clampedZoomFactor;
            }
            else
            {
                _primaryZoomFactor = clampedZoomFactor;
            }

            RefreshPaneBitmap(pane);
            if (synchronizeLinkedPane && IsLinkedPaneZoomEnabled)
            {
                var peerPane = pane == Pane.Compare ? Pane.Primary : Pane.Compare;
                SetPaneZoomFactor(peerPane, clampedZoomFactor, synchronizeLinkedPane: false);
            }
        }

        private double GetPaneZoomFactor(Pane pane)
        {
            return pane == Pane.Compare ? _compareZoomFactor : _primaryZoomFactor;
        }

        private static string FormatZoomFactor(double zoomFactor)
        {
            return string.Format(CultureInfo.InvariantCulture, "{0:0}%", zoomFactor * 100d);
        }

        private void RefreshPaneBitmap(Pane pane)
        {
            var frameBuffer = pane == Pane.Compare ? _compareFrameBuffer : _primaryFrameBuffer;
            if (frameBuffer == null)
            {
                return;
            }

            SetPaneBitmap(pane, frameBuffer);
        }

        private void SetPaneBitmap(Pane pane, DecodedFrameBuffer frameBuffer)
        {
            var viewport = BuildPaneViewport(pane, frameBuffer);
            var bitmap = AvaloniaFrameBufferPresenter.CreateBitmap(frameBuffer, viewport);
            if (pane == Pane.Compare)
            {
                _compareFrameBuffer = frameBuffer;
                CompareVideoSurface.Source = bitmap;
                CompareEmptyStateOverlay.IsVisible = false;
            }
            else
            {
                _primaryFrameBuffer = frameBuffer;
                CustomVideoSurface.Source = bitmap;
                PrimaryEmptyStateOverlay.IsVisible = false;
            }
        }

        private Pane GetFileOpenTargetPane()
        {
            return GetFocusedPane();
        }

        private Task OpenRecentPathAsync(string filePath)
        {
            return OpenPathAsync(filePath, GetFileOpenTargetPane());
        }

        private void SelectPane(Pane pane)
        {
            _focusedPane = CompareModeCheckBox.IsChecked == true && pane == Pane.Compare
                ? Pane.Compare
                : Pane.Primary;
            UpdatePaneSelectionVisuals();
        }

        private void UpdatePaneSelectionVisuals()
        {
            var highlightPaneSelection = CompareModeCheckBox.IsChecked == true;
            var compareIsFocused = highlightPaneSelection && _focusedPane == Pane.Compare;
            var primaryIsFocused = highlightPaneSelection && !compareIsFocused;

            PrimaryPaneBorder.BorderBrush = primaryIsFocused ? PaneSelectedBorderBrush : PaneChromeBorderBrush;
            PrimaryPaneBorder.BorderThickness = primaryIsFocused ? new Thickness(2) : new Thickness(1);
            PrimaryPaneHeaderBorder.Background = compareIsFocused ? PaneChromeBrush : PaneSelectedBrush;
            PrimaryPaneHeaderBorder.BorderBrush = primaryIsFocused ? PaneSelectedBorderBrush : PaneChromeBorderBrush;
            PrimaryPaneFooterBorder.Background = compareIsFocused ? PaneChromeBrush : PaneSelectedBrush;
            PrimaryPaneFooterBorder.BorderBrush = primaryIsFocused ? PaneSelectedBorderBrush : PaneChromeBorderBrush;

            ComparePaneBorder.BorderBrush = compareIsFocused ? PaneSelectedBorderBrush : PaneChromeBorderBrush;
            ComparePaneBorder.BorderThickness = compareIsFocused ? new Thickness(2) : new Thickness(1);
            ComparePaneHeaderBorder.Background = compareIsFocused ? PaneSelectedBrush : PaneChromeBrush;
            ComparePaneHeaderBorder.BorderBrush = compareIsFocused ? PaneSelectedBorderBrush : PaneChromeBorderBrush;
            ComparePaneFooterBorder.Background = compareIsFocused ? PaneSelectedBrush : PaneChromeBrush;
            ComparePaneFooterBorder.BorderBrush = compareIsFocused ? PaneSelectedBorderBrush : PaneChromeBorderBrush;
        }

        private static Pane ResolvePane(string? paneId)
        {
            return string.Equals(paneId, PaneCompareId, StringComparison.Ordinal)
                ? Pane.Compare
                : Pane.Primary;
        }

        private static string ResolvePaneId(Pane pane)
        {
            return pane == Pane.Compare ? PaneCompareId : PanePrimaryId;
        }

        private static string ResolvePaneKey(Pane pane)
        {
            return pane == Pane.Compare ? PaneCompareKey : PanePrimaryKey;
        }

        private static string ResolvePaneLabel(Pane pane)
        {
            return pane == Pane.Compare ? PaneCompareLabel : PanePrimaryLabel;
        }

        private async void CloseVideoMenuItem_Click(object? sender, RoutedEventArgs e)
        {
            await CloseVideosAsync();
        }

        private async Task CloseVideosAsync()
        {
            await _primaryEngine.CloseAsync();
            if (_compareEngine != null)
            {
                await _compareEngine.CloseAsync();
            }

            CurrentFileTextBlock.Text = "No file loaded";
            PrimaryPaneFileTextBlock.Text = "No video loaded";
            ComparePaneFileTextBlock.Text = "No video loaded";
            _primaryFrameBuffer = null;
            _compareFrameBuffer = null;
            CustomVideoSurface.Source = null;
            CompareVideoSurface.Source = null;
            SetPaneZoomFactor(Pane.Primary, MinimumPaneZoomFactor, synchronizeLinkedPane: false);
            SetPaneZoomFactor(Pane.Compare, MinimumPaneZoomFactor, synchronizeLinkedPane: false);
            PrimaryEmptyStateOverlay.IsVisible = true;
            CompareEmptyStateOverlay.IsVisible = true;
            PlaybackStateTextBlock.Text = "Ready";
            CurrentFrameTextBlock.Text = "Frame --";
            TimecodeTextBlock.Text = "Timecode --";
            UpdateCompareOptionState();
        }

        private async void PlayPauseButton_Click(object? sender, RoutedEventArgs e)
        {
            if (IsAllPaneTransportEnabled)
            {
                if (_primaryEngine.IsPlaying || (_compareEngine != null && _compareEngine.IsPlaying))
                {
                    await PauseAllPanePlaybackAsync();
                }
                else
                {
                    await StartAllPanePlaybackAsync();
                }
            }
            else
            {
                var engine = GetEngine(GetFocusedPane());
                if (engine.IsPlaying)
                {
                    await engine.PauseAsync();
                }
                else
                {
                    await engine.PlayAsync();
                }
            }
        }

        private async void PlayButton_Click(object? sender, RoutedEventArgs e)
        {
            await StartPlaybackAsync(SynchronizedOperationScope.FocusedPane, ResolvePaneId(GetFocusedPane()));
        }

        private async void PauseButton_Click(object? sender, RoutedEventArgs e)
        {
            await PausePlaybackAsync(logAction: true);
        }

        private async void PanePlayPauseButton_Click(object? sender, RoutedEventArgs e)
        {
            var pane = ResolvePaneFromSender(sender);
            var engine = GetEngine(pane);
            if (engine.IsPlaying)
            {
                await engine.PauseAsync();
            }
            else
            {
                await engine.PlayAsync();
            }
        }

        private async void PaneStepBackButton_Click(object? sender, RoutedEventArgs e)
        {
            await StepFrameAsync(-1, ResolvePaneFromSender(sender));
        }

        private async void PaneStepForwardButton_Click(object? sender, RoutedEventArgs e)
        {
            await StepFrameAsync(1, ResolvePaneFromSender(sender));
        }

        private async void PaneSkipBackHundredFramesButton_Click(object? sender, RoutedEventArgs e)
        {
            await StepFrameAsync(-HundredFrameStep, ResolvePaneFromSender(sender));
        }

        private async void PaneSkipForwardHundredFramesButton_Click(object? sender, RoutedEventArgs e)
        {
            await StepFrameAsync(HundredFrameStep, ResolvePaneFromSender(sender));
        }

        private async void PreviousFrameButton_Click(object? sender, RoutedEventArgs e)
        {
            await StepFrameAsync(-1);
        }

        private async void NextFrameButton_Click(object? sender, RoutedEventArgs e)
        {
            await StepFrameAsync(1);
        }

        private async void RewindButton_Click(object? sender, RoutedEventArgs e)
        {
            await SeekRelativeAsync(TimeSpan.FromSeconds(-5));
        }

        private async void FastForwardButton_Click(object? sender, RoutedEventArgs e)
        {
            await SeekRelativeAsync(TimeSpan.FromSeconds(5));
        }

        private async Task StartPlaybackAsync(SynchronizedOperationScope? operationScope, string? paneId)
        {
            if (ShouldUseAllPaneTransport(operationScope))
            {
                await StartAllPanePlaybackAsync();
                return;
            }

            await GetEngine(ResolvePane(paneId)).PlayAsync();
        }

        [SuppressMessage("Major Code Smell", "S1144:Unused private types or members", Justification = "Preserves the Windows parity harness surface.")]
        private Task PausePlaybackAsync()
        {
            return PausePlaybackAsync(logAction: true);
        }

        private Task PausePlaybackAsync(bool logAction)
        {
            return PausePlaybackAsync(logAction, (SynchronizedOperationScope?)null);
        }

        private async Task PausePlaybackAsync(bool logAction, SynchronizedOperationScope? operationScope)
        {
            _ = logAction;
            if (ShouldUseAllPaneTransport(operationScope))
            {
                await PauseAllPanePlaybackAsync();
                return;
            }

            await GetEngine(GetFocusedPane()).PauseAsync();
        }

        private async Task StepFrameAsync(int delta)
        {
            if (IsAllPaneTransportEnabled)
            {
                await StepFrameAsync(delta, Pane.Primary);
                await StepFrameAsync(delta, Pane.Compare);
                return;
            }

            await StepFrameAsync(delta, GetFocusedPane());
        }

        private async Task StepFrameAsync(int delta, Pane pane)
        {
            if (delta == 0)
            {
                return;
            }

            var engine = GetEngine(pane);
            var steps = Math.Abs(delta);
            for (var index = 0; index < steps; index++)
            {
                if (delta > 0)
                {
                    await engine.StepForwardAsync();
                }
                else
                {
                    await engine.StepBackwardAsync();
                }
            }
        }

        private bool ShouldUseAllPaneTransport(SynchronizedOperationScope? operationScope)
        {
            return operationScope == SynchronizedOperationScope.AllPanes ||
                (operationScope == null && IsAllPaneTransportEnabled);
        }

        private async Task StartAllPanePlaybackAsync()
        {
            if (!_primaryEngine.IsMediaOpen || _compareEngine == null || !_compareEngine.IsMediaOpen)
            {
                return;
            }

            await _primaryEngine.PlayAsync();
            await _compareEngine.PlayAsync();
        }

        private async Task PauseAllPanePlaybackAsync()
        {
            if (_primaryEngine.IsMediaOpen)
            {
                await _primaryEngine.PauseAsync();
            }

            if (_compareEngine != null && _compareEngine.IsMediaOpen)
            {
                await _compareEngine.PauseAsync();
            }
        }

        private async Task SeekRelativeAsync(TimeSpan offset)
        {
            if (IsAllPaneTransportEnabled)
            {
                await SeekRelativeAsync(Pane.Primary, offset);
                await SeekRelativeAsync(Pane.Compare, offset);
                return;
            }

            await SeekRelativeAsync(GetFocusedPane(), offset);
        }

        private async Task SeekRelativeAsync(Pane pane, TimeSpan offset)
        {
            var engine = GetEngine(pane);
            var target = engine.Position.PresentationTime + offset;
            if (target < TimeSpan.Zero)
            {
                target = TimeSpan.Zero;
            }

            await engine.SeekToTimeAsync(target);
        }

        private Pane ResolvePaneFromSender(object? sender)
        {
            if (TryResolvePaneFromSender(sender, out var pane))
            {
                SelectPane(pane);
                return pane;
            }

            return GetFocusedPane();
        }

        private static bool TryResolvePaneFromSender(object? sender, out Pane pane)
        {
            if (sender is Control control &&
                control.Tag is string paneId)
            {
                pane = ResolvePane(paneId);
                return true;
            }

            pane = Pane.Primary;
            return false;
        }

        private void PaneBorder_PointerPressed(object? sender, PointerPressedEventArgs e)
        {
            if (TryResolvePaneFromSender(sender, out var pane))
            {
                SelectPane(pane);
            }

            if (sender is Control control)
            {
                OpenContextMenuOnRightClick(control, e);
            }
        }

        private void TimelineSlider_PointerPressed(object? sender, PointerPressedEventArgs e)
        {
            if (sender is not Slider slider)
            {
                return;
            }

            var pane = slider.Tag is string paneId
                ? ResolvePane(paneId)
                : GetFocusedPane();
            if (slider.Tag is string)
            {
                SelectPane(pane);
            }

            if (e.GetCurrentPoint(slider).Properties.IsRightButtonPressed)
            {
                SetTimelineContextTarget(slider.Tag is string ? pane : null, CalculateTimelineTarget(slider, e));
                OpenContextMenuOnRightClick(slider, e);
            }
        }

        private static void OpenContextMenuOnRightClick(Control control, PointerPressedEventArgs e)
        {
            if (!e.GetCurrentPoint(control).Properties.IsRightButtonPressed ||
                control.ContextMenu == null)
            {
                return;
            }

            control.ContextMenu.Open(control);
            e.Handled = true;
        }

        private static TimeSpan CalculateTimelineTarget(Slider slider, PointerEventArgs e)
        {
            var width = Math.Max(1d, slider.Bounds.Width);
            var position = e.GetCurrentPoint(slider).Position;
            var ratio = Math.Max(0d, Math.Min(1d, position.X / width));
            var seconds = slider.Minimum + ((slider.Maximum - slider.Minimum) * ratio);
            return TimeSpan.FromSeconds(Math.Max(0d, seconds));
        }

        private void SetTimelineContextTarget(Pane? pane, TimeSpan target)
        {
            if (pane == Pane.Primary)
            {
                _primaryTimelineContextTarget = target;
            }
            else if (pane == Pane.Compare)
            {
                _compareTimelineContextTarget = target;
            }
            else
            {
                _masterTimelineContextTarget = target;
            }
        }

        private void InstallContextMenus()
        {
            CustomVideoSurfaceHost.ContextMenu = CreatePaneContextMenu(Pane.Primary);
            CompareVideoSurfaceHost.ContextMenu = CreatePaneContextMenu(Pane.Compare);
            PositionSlider.ContextMenu = CreateTimelineContextMenu(null);
            PrimaryPanePositionSlider.ContextMenu = CreateTimelineContextMenu(Pane.Primary);
            ComparePanePositionSlider.ContextMenu = CreateTimelineContextMenu(Pane.Compare);
        }

        private ContextMenu CreatePaneContextMenu(Pane pane)
        {
            var videoInfoItem = CreateFrameContextMenuItem("Video Info...");
            videoInfoItem.Click += (_, _) =>
            {
                SelectPane(pane);
                ShowVideoInfo(pane);
            };

            var resetZoomItem = CreateFrameContextMenuItem("Reset Zoom");
            resetZoomItem.Click += (_, _) =>
            {
                SelectPane(pane);
                ResetZoomForPane(pane);
            };

            var saveLoopItem = CreateFrameContextMenuItem("Save Loop As Clip...");
            saveLoopItem.Click += async (_, _) =>
            {
                SelectPane(pane);
                await ExportLoopClipAsync(null, ResolvePaneId(pane));
            };

            var compareExportItem = CreateFrameContextMenuItem("Export Side-by-Side Compare...");
            compareExportItem.Click += async (_, _) =>
            {
                SelectPane(pane);
                await ExportSideBySideCompareFromCurrentLoopStateAsync();
            };

            var menu = CreateFrameContextMenu();
            menu.Items.Add(videoInfoItem);
            menu.Items.Add(resetZoomItem);
            menu.Items.Add(saveLoopItem);
            menu.Items.Add(compareExportItem);
            menu.Opened += (_, _) =>
            {
                SelectPane(pane);
                var engine = TryGetExistingEngine(pane);
                videoInfoItem.IsEnabled = engine != null && engine.IsMediaOpen;
                resetZoomItem.IsEnabled = GetPaneZoomFactor(pane) > MinimumPaneZoomFactor + 0.0001d;
                saveLoopItem.IsEnabled = CanExportLoopClip(pane);
                compareExportItem.IsVisible = CompareModeCheckBox.IsChecked == true;
                compareExportItem.IsEnabled = CanExportSideBySideCompare();
            };

            return menu;
        }

        private ContextMenu CreateTimelineContextMenu(Pane? explicitPane)
        {
            var setPositionAItem = CreateFrameContextMenuItem("Set Position A Here");
            setPositionAItem.Click += async (_, _) => await SetTimelineContextMarkerAsync(explicitPane, LoopPlaybackMarkerEndpoint.In);

            var setPositionBItem = CreateFrameContextMenuItem("Set Position B Here");
            setPositionBItem.Click += async (_, _) => await SetTimelineContextMarkerAsync(explicitPane, LoopPlaybackMarkerEndpoint.Out);

            var loopPlaybackItem = CreateFrameContextMenuItem("Loop Playback");
            loopPlaybackItem.ToggleType = MenuItemToggleType.CheckBox;
            loopPlaybackItem.Click += (_, _) => SetLoopPlaybackEnabled(!_isLoopPlaybackEnabled);

            var saveLoopItem = CreateFrameContextMenuItem("Save Loop As Clip...");
            saveLoopItem.Click += async (_, _) =>
            {
                var pane = ResolveTimelineContextPane(explicitPane);
                SelectPane(pane);
                await ExportLoopClipAsync(null, ResolvePaneId(pane));
            };

            var menu = CreateFrameContextMenu();
            menu.Items.Add(setPositionAItem);
            menu.Items.Add(setPositionBItem);
            menu.Items.Add(loopPlaybackItem);
            menu.Items.Add(new Separator());
            menu.Items.Add(saveLoopItem);
            menu.Opened += (_, _) =>
            {
                var pane = ResolveTimelineContextPane(explicitPane);
                SelectPane(pane);
                var target = GetTimelineContextTarget(explicitPane);
                setPositionAItem.IsEnabled = CanSetTimelineLoopMarker(pane, LoopPlaybackMarkerEndpoint.In, target);
                setPositionBItem.IsEnabled = CanSetTimelineLoopMarker(pane, LoopPlaybackMarkerEndpoint.Out, target);
                loopPlaybackItem.IsChecked = _isLoopPlaybackEnabled;
                saveLoopItem.IsEnabled = CanExportLoopClip(pane);
            };

            return menu;
        }

        private static ContextMenu CreateFrameContextMenu()
        {
            var menu = new ContextMenu();
            menu.Classes.Add("frame-context-menu");
            return menu;
        }

        private static MenuItem CreateFrameContextMenuItem(string header)
        {
            var item = new MenuItem { Header = header };
            item.Classes.Add("frame-context-menu-item");
            return item;
        }

        private async Task SetTimelineContextMarkerAsync(Pane? explicitPane, LoopPlaybackMarkerEndpoint endpoint)
        {
            var pane = ResolveTimelineContextPane(explicitPane);
            SelectPane(pane);
            var set = await SetTimelineLoopMarkerAtAsync(ResolvePaneId(pane), endpoint, GetTimelineContextTarget(explicitPane));
            if (!set)
            {
                CacheStatusTextBlock.Text = endpoint == LoopPlaybackMarkerEndpoint.In
                    ? "Could not set position A here."
                    : "Could not set position B here.";
            }
        }

        private Pane ResolveTimelineContextPane(Pane? explicitPane)
        {
            return explicitPane ?? GetFocusedPane();
        }

        private TimeSpan GetTimelineContextTarget(Pane? pane)
        {
            if (pane == Pane.Primary)
            {
                return _primaryTimelineContextTarget;
            }

            if (pane == Pane.Compare)
            {
                return _compareTimelineContextTarget;
            }

            return _masterTimelineContextTarget;
        }

        private bool CanSetTimelineLoopMarker(Pane pane, LoopPlaybackMarkerEndpoint endpoint, TimeSpan target)
        {
            var engine = TryGetExistingEngine(pane);
            if (engine == null || !engine.IsMediaOpen)
            {
                return false;
            }

            var range = GetLoopRange(pane);
            var opposite = endpoint == LoopPlaybackMarkerEndpoint.In ? range.LoopOut : range.LoopIn;
            return opposite == null || IsLoopMarkerTimeOrderValid(endpoint, target, opposite.PresentationTime);
        }

        private bool CanExportLoopClip(Pane pane)
        {
            var engine = TryGetExistingEngine(pane);
            var range = GetLoopRange(pane);
            return engine != null &&
                   engine.IsMediaOpen &&
                   range.HasLoopIn &&
                   range.HasLoopOut &&
                   !range.IsInvalidRange;
        }

        private bool CanExportSideBySideCompare()
        {
            return CompareModeCheckBox.IsChecked == true &&
                   _primaryEngine.IsMediaOpen &&
                   _compareEngine != null &&
                   _compareEngine.IsMediaOpen;
        }

        private IVideoReviewEngine? TryGetExistingEngine(Pane pane)
        {
            return pane == Pane.Primary ? _primaryEngine : _compareEngine;
        }

        [SuppressMessage("Major Code Smell", "S1144:Unused private types or members", Justification = "Invoked by the desktop preview parity harness through reflection.")]
        private async Task CommitSliderSeekAsync(string interactionName, TimeSpan target)
        {
            _ = interactionName;
            await _primaryEngine.SeekToTimeAsync(target);
        }

        private async void PositionSlider_ValueChanged(object? sender, RangeBaseValueChangedEventArgs e)
        {
            if (_isUpdatingSliders || sender != PositionSlider || !_primaryEngine.IsMediaOpen)
            {
                return;
            }

            await _primaryEngine.SeekToTimeAsync(TimeSpan.FromSeconds(PositionSlider.Value));
        }

        private async void PanePositionSlider_ValueChanged(object? sender, RangeBaseValueChangedEventArgs e)
        {
            if (_isUpdatingSliders)
            {
                return;
            }

            if (sender == PrimaryPanePositionSlider && _primaryEngine.IsMediaOpen)
            {
                SelectPane(Pane.Primary);
                await _primaryEngine.SeekToTimeAsync(TimeSpan.FromSeconds(PrimaryPanePositionSlider.Value));
            }
            else if (sender == ComparePanePositionSlider && _compareEngine != null && _compareEngine.IsMediaOpen)
            {
                SelectPane(Pane.Compare);
                await _compareEngine.SeekToTimeAsync(TimeSpan.FromSeconds(ComparePanePositionSlider.Value));
            }
        }

        private async void FrameNumberTextBox_KeyDown(object? sender, KeyEventArgs e)
        {
            if (e.Key != Key.Enter || sender is not TextBox textBox)
            {
                return;
            }

            if (!long.TryParse(textBox.Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var oneBasedFrame) ||
                oneBasedFrame <= 0)
            {
                return;
            }

            var pane = textBox == ComparePaneFrameNumberTextBox ? Pane.Compare : Pane.Primary;
            SelectPane(pane);
            await GetEngine(pane).SeekToFrameAsync(oneBasedFrame - 1);
        }

        private void SetLoopMarker(LoopPlaybackMarkerEndpoint endpoint)
        {
            var pane = GetFocusedPane();
            var engine = GetEngine(pane);
            var currentRange = GetLoopRange(pane);
            var anchor = CreateLoopAnchor(engine, pane);
            if (anchor == null)
            {
                CacheStatusTextBlock.Text = "Open a video before setting loop points.";
                return;
            }

            var loopIn = endpoint == LoopPlaybackMarkerEndpoint.In ? anchor : currentRange.LoopIn;
            var loopOut = endpoint == LoopPlaybackMarkerEndpoint.Out ? anchor : currentRange.LoopOut;
            SetLoopRange(pane, CreateLoopRange(pane, loopIn, loopOut));
            UpdateLoopUi();
        }

        private async Task<bool> SetTimelineLoopMarkerAtAsync(string? paneId, LoopPlaybackMarkerEndpoint endpoint, TimeSpan target)
        {
            var pane = ResolvePane(paneId);
            var engine = GetEngine(pane);
            if (!engine.IsMediaOpen)
            {
                return false;
            }

            var currentRange = GetLoopRange(pane);
            var oppositeAnchor = endpoint == LoopPlaybackMarkerEndpoint.In
                ? currentRange.LoopOut
                : currentRange.LoopIn;
            if (oppositeAnchor != null &&
                !IsLoopMarkerTimeOrderValid(endpoint, target, oppositeAnchor.PresentationTime))
            {
                return false;
            }

            await engine.SeekToTimeAsync(target);
            var anchor = CreateLoopAnchor(engine, pane);
            if (anchor == null)
            {
                return false;
            }

            var loopIn = endpoint == LoopPlaybackMarkerEndpoint.In ? anchor : currentRange.LoopIn;
            var loopOut = endpoint == LoopPlaybackMarkerEndpoint.Out ? anchor : currentRange.LoopOut;
            SetLoopRange(pane, CreateLoopRange(pane, loopIn, loopOut));
            UpdateLoopUi();
            return true;
        }

        private void ClearLoopPoints()
        {
            _primaryLoopRange = CreateLoopRange(null, null);
            _compareLoopRange = CreateLoopRange(Pane.Compare, null, null);
            UpdateLoopUi();
        }

        private void SetLoopPlaybackEnabled(bool isEnabled)
        {
            _isLoopPlaybackEnabled = isEnabled;
            if (_nativeLoopPlaybackMenuItem != null)
            {
                _nativeLoopPlaybackMenuItem.IsChecked = isEnabled;
            }

            UpdateLoopUi();
        }

        private void CompareModeCheckBox_IsCheckedChanged(object? sender, RoutedEventArgs e)
        {
            if (CompareModeCheckBox.IsChecked == true)
            {
                ShowCompareMode();
            }
            else
            {
                HideCompareMode();
            }
        }

        private void ShowCompareMode()
        {
            VideoPaneGrid.ColumnDefinitions[1].Width = new GridLength(1, GridUnitType.Star);
            PrimaryPaneFooterBorder.IsVisible = true;
            ComparePaneBorder.IsVisible = true;
            ComparePaneFooterBorder.IsVisible = true;
            CompareToolbarBorder.IsVisible = true;
            UpdatePaneSelectionVisuals();
            UpdateCompareOptionState();
        }

        private void HideCompareMode()
        {
            _focusedPane = Pane.Primary;
            VideoPaneGrid.ColumnDefinitions[1].Width = new GridLength(0);
            PrimaryPaneFooterBorder.IsVisible = false;
            ComparePaneBorder.IsVisible = false;
            ComparePaneFooterBorder.IsVisible = false;
            CompareToolbarBorder.IsVisible = false;
            UpdatePaneSelectionVisuals();
            UpdateCompareOptionState();
        }

        private void UpdateCompareOptionState()
        {
            var compareModeEnabled = IsCompareModeEnabled;
            var bothPanesLoaded = _primaryEngine.IsMediaOpen &&
                _compareEngine != null &&
                _compareEngine.IsMediaOpen;

            AllPanesCheckBox.IsEnabled = compareModeEnabled && bothPanesLoaded;
            ToolTip.SetTip(
                AllPanesCheckBox,
                bothPanesLoaded
                    ? "Shared transport controls both compare panes."
                    : "Load both compare panes before using shared transport.");
            LinkPaneZoomCheckBox.IsEnabled = compareModeEnabled;
            ToolTip.SetTip(
                LinkPaneZoomCheckBox,
                IsLinkedPaneZoomEnabled
                    ? "Zoom changes are mirrored between compare panes."
                    : "Zoom changes stay on the focused pane.");
        }

        private void AllPanesCheckBox_IsCheckedChanged(object? sender, RoutedEventArgs e)
        {
            UpdateCompareOptionState();
            if (IsCompareModeEnabled)
            {
                CacheStatusTextBlock.Text = AllPanesCheckBox.IsChecked == true
                    ? "Shared transport controls both compare panes."
                    : "Shared transport controls the focused pane.";
            }
        }

        private void LinkPaneZoomCheckBox_IsCheckedChanged(object? sender, RoutedEventArgs e)
        {
            UpdateCompareOptionState();
            if (!IsCompareModeEnabled)
            {
                return;
            }

            if (LinkPaneZoomCheckBox.IsChecked == true)
            {
                SetPaneZoomFactor(GetFocusedPane(), GetPaneZoomFactor(GetFocusedPane()), synchronizeLinkedPane: true);
                CacheStatusTextBlock.Text = "Compare pane zoom is linked.";
            }
            else
            {
                CacheStatusTextBlock.Text = "Compare pane zoom is independent.";
            }
        }

        private async void AlignRightToLeftButton_Click(object? sender, RoutedEventArgs e)
        {
            if (_compareEngine == null || !_primaryEngine.IsMediaOpen || !_compareEngine.IsMediaOpen)
            {
                return;
            }

            if (_primaryEngine.Position.FrameIndex.HasValue && _primaryEngine.Position.IsFrameIndexAbsolute)
            {
                await _compareEngine.SeekToFrameAsync(_primaryEngine.Position.FrameIndex.Value);
            }
            else
            {
                await _compareEngine.SeekToTimeAsync(_primaryEngine.Position.PresentationTime);
            }

            CompareStatusTextBlock.Text = "Compare: synced right to left";
        }

        private async void AlignLeftToRightButton_Click(object? sender, RoutedEventArgs e)
        {
            if (_compareEngine == null || !_primaryEngine.IsMediaOpen || !_compareEngine.IsMediaOpen)
            {
                return;
            }

            if (_compareEngine.Position.FrameIndex.HasValue && _compareEngine.Position.IsFrameIndexAbsolute)
            {
                await _primaryEngine.SeekToFrameAsync(_compareEngine.Position.FrameIndex.Value);
            }
            else
            {
                await _primaryEngine.SeekToTimeAsync(_compareEngine.Position.PresentationTime);
            }

            CompareStatusTextBlock.Text = "Compare: synced left to right";
        }

        private void PrimaryEngine_StateChanged(object? sender, VideoReviewEngineStateChangedEventArgs e)
        {
            RestartLoopPlaybackIfNeeded(e);
            Dispatcher.UIThread.Post(() => ApplyState(Pane.Primary, e));
        }

        private void CompareEngine_StateChanged(object? sender, VideoReviewEngineStateChangedEventArgs e)
        {
            Dispatcher.UIThread.Post(() => ApplyState(Pane.Compare, e));
        }

        private void PrimaryEngine_FramePresented(object? sender, FramePresentedEventArgs e)
        {
            Dispatcher.UIThread.Post(() =>
            {
                SetPaneBitmap(Pane.Primary, e.FrameBuffer);
            });
        }

        private void CompareEngine_FramePresented(object? sender, FramePresentedEventArgs e)
        {
            Dispatcher.UIThread.Post(() =>
            {
                SetPaneBitmap(Pane.Compare, e.FrameBuffer);
            });
        }

        private void ApplyState(Pane pane, VideoReviewEngineStateChangedEventArgs state)
        {
            _isUpdatingSliders = true;
            try
            {
                var durationSeconds = Math.Max(1d, state.MediaInfo.Duration.TotalSeconds);
                var positionSeconds = Math.Max(0d, Math.Min(durationSeconds, state.Position.PresentationTime.TotalSeconds));
                var positionText = FormatTime(state.Position.PresentationTime);
                var durationText = FormatTime(state.MediaInfo.Duration);
                var frameText = state.Position.FrameIndex.HasValue
                    ? "Frame " + (state.Position.FrameIndex.Value + 1).ToString(CultureInfo.InvariantCulture)
                    : "Frame --";

                if (pane == Pane.Primary)
                {
                    ApplyPrimaryState(state, durationSeconds, positionSeconds, positionText, durationText, frameText);
                }
                else
                {
                    ApplyCompareState(state, durationSeconds, positionSeconds, positionText, durationText);
                }

                if (!string.IsNullOrWhiteSpace(state.LastErrorMessage))
                {
                    CacheStatusTextBlock.Text = state.LastErrorMessage;
                }

                UpdateCompareOptionState();
            }
            finally
            {
                _isUpdatingSliders = false;
            }
        }

        private void ApplyPrimaryState(
            VideoReviewEngineStateChangedEventArgs state,
            double durationSeconds,
            double positionSeconds,
            string positionText,
            string durationText,
            string frameText)
        {
            PositionSlider.Maximum = durationSeconds;
            PositionSlider.Value = positionSeconds;
            PrimaryPanePositionSlider.Maximum = durationSeconds;
            PrimaryPanePositionSlider.Value = positionSeconds;
            CurrentPositionTextBlock.Text = positionText;
            PrimaryPaneCurrentPositionTextBlock.Text = positionText;
            DurationTextBlock.Text = durationText;
            PrimaryPaneDurationTextBlock.Text = durationText;
            CurrentFrameTextBlock.Text = frameText;
            TimecodeTextBlock.Text = "Timecode " + positionText;
            FrameNumberTextBox.Text = FormatFrameNumberEntry(state.Position);
            PrimaryPaneFrameNumberTextBox.Text = FrameNumberTextBox.Text;
            PlayPausePlayIcon.IsVisible = !state.IsPlaying;
            PlayPausePauseIcon.IsVisible = state.IsPlaying;
            PrimaryPanePlayPausePlayIcon.IsVisible = !state.IsPlaying;
            PrimaryPanePlayPausePauseIcon.IsVisible = state.IsPlaying;
            if (_nativePlayPauseMenuItem != null)
            {
                _nativePlayPauseMenuItem.Header = state.IsPlaying ? "Pause" : "Play";
            }

            PlaybackStateTextBlock.Text = FormatPlaybackState(state);
        }

        private void ApplyCompareState(
            VideoReviewEngineStateChangedEventArgs state,
            double durationSeconds,
            double positionSeconds,
            string positionText,
            string durationText)
        {
            ComparePanePositionSlider.Maximum = durationSeconds;
            ComparePanePositionSlider.Value = positionSeconds;
            ComparePaneCurrentPositionTextBlock.Text = positionText;
            ComparePaneDurationTextBlock.Text = durationText;
            ComparePaneFrameNumberTextBox.Text = FormatFrameNumberEntry(state.Position);
            ComparePanePlayPausePlayIcon.IsVisible = !state.IsPlaying;
            ComparePanePlayPausePauseIcon.IsVisible = state.IsPlaying;
        }

        private static string FormatFrameNumberEntry(ReviewPosition position)
        {
            return position.FrameIndex.HasValue
                ? (position.FrameIndex.Value + 1).ToString(CultureInfo.InvariantCulture)
                : string.Empty;
        }

        private static string FormatPlaybackState(VideoReviewEngineStateChangedEventArgs state)
        {
            if (state.IsPlaying)
            {
                return "Playing";
            }

            return state.IsMediaOpen ? "Paused" : "Ready";
        }

        private void ApplyFileLabels(Pane pane, string filePath)
        {
            var displayName = Path.GetFileName(filePath);
            if (pane == Pane.Primary)
            {
                CurrentFileTextBlock.Text = filePath;
                PrimaryPaneFileTextBlock.Text = displayName;
            }
            else
            {
                ComparePaneFileTextBlock.Text = displayName;
            }
        }

        private void SetPaneState(Pane pane, string state)
        {
            if (pane == Pane.Primary)
            {
                PrimaryPaneStateTextBlock.Text = state;
            }
            else
            {
                ComparePaneStateTextBlock.Text = state;
            }
        }

        private void UpdateLoopUi()
        {
            var primaryStatus = BuildLoopStatusText(_primaryLoopRange);
            var compareStatus = BuildLoopStatusText(_compareLoopRange);
            LoopStatusTextBlock.Text = primaryStatus;
            PrimaryPaneLoopStatusTextBlock.Text = primaryStatus;
            ComparePaneLoopStatusTextBlock.Text = compareStatus;
        }

        private string BuildLoopStatusText(LoopPlaybackPaneRangeSnapshot range)
        {
            if (range == null || !range.HasAnyMarkers)
            {
                return _isLoopPlaybackEnabled ? "Loop: full media" : "Loop: off";
            }

            var rangeText = FormatTime(range.EffectiveStartTime) + " -> " + FormatTime(range.EffectiveEndTime);
            if (range.IsInvalidRange)
            {
                return "Loop: invalid";
            }

            return _isLoopPlaybackEnabled ? "Loop: " + rangeText : "Loop: off (" + rangeText + ")";
        }

        private void RestartLoopPlaybackIfNeeded(VideoReviewEngineStateChangedEventArgs state)
        {
            if (!_isLoopPlaybackEnabled ||
                _isLoopRestartInFlight ||
                state == null ||
                !state.IsPlaying ||
                !state.IsMediaOpen)
            {
                return;
            }

            var range = _primaryLoopRange;
            if (range != null && range.HasAnyMarkers && range.IsInvalidRange)
            {
                return;
            }

            var endTime = range != null && range.HasAnyMarkers
                ? range.EffectiveEndTime
                : state.MediaInfo.Duration;
            if (endTime <= TimeSpan.Zero)
            {
                return;
            }

            var tolerance = state.MediaInfo.PositionStep > TimeSpan.Zero
                ? state.MediaInfo.PositionStep
                : TimeSpan.FromMilliseconds(50);
            if (state.Position.PresentationTime + tolerance < endTime)
            {
                return;
            }

            _ = Task.Run(async () => await RestartLoopPlaybackAsync(range ?? CreateLoopRange(null, null)));
        }

        private async Task RestartLoopPlaybackAsync(LoopPlaybackPaneRangeSnapshot range)
        {
            _isLoopRestartInFlight = true;
            try
            {
                var restartTime = range != null && range.HasLoopIn ? range.EffectiveStartTime : TimeSpan.Zero;
                await _primaryEngine.PauseAsync();
                await _primaryEngine.SeekToTimeAsync(restartTime);
                await _primaryEngine.PlayAsync();
                CacheStatusTextBlock.Text = range != null && range.HasLoopIn
                    ? "Loop playback restarted from loop-in."
                    : "Loop playback restarted from start.";
            }
            catch (Exception ex)
            {
                CacheStatusTextBlock.Text = "Loop playback restart failed: " + ex.Message;
            }
            finally
            {
                _isLoopRestartInFlight = false;
            }
        }

        private LoopPlaybackPaneRangeSnapshot CreateLoopRange(
            LoopPlaybackAnchorSnapshot? loopIn,
            LoopPlaybackAnchorSnapshot? loopOut)
        {
            return CreateLoopRange(Pane.Primary, loopIn, loopOut);
        }

        private LoopPlaybackPaneRangeSnapshot CreateLoopRange(
            Pane pane,
            LoopPlaybackAnchorSnapshot? loopIn,
            LoopPlaybackAnchorSnapshot? loopOut)
        {
            var engine = GetEngine(pane);
            return new LoopPlaybackPaneRangeSnapshot(
                ResolvePaneId(pane),
                ResolvePaneKey(pane),
                ResolvePaneLabel(pane),
                engine.CurrentFilePath,
                engine.MediaInfo.Duration,
                loopIn,
                loopOut);
        }

        private LoopPlaybackPaneRangeSnapshot GetLoopRange(Pane pane)
        {
            return pane == Pane.Compare ? _compareLoopRange : _primaryLoopRange;
        }

        private void SetLoopRange(Pane pane, LoopPlaybackPaneRangeSnapshot range)
        {
            if (pane == Pane.Compare)
            {
                _compareLoopRange = range;
            }
            else
            {
                _primaryLoopRange = range;
            }
        }

        private static LoopPlaybackAnchorSnapshot? CreateLoopAnchor(IVideoReviewEngine engine, Pane pane)
        {
            if (engine == null || !engine.IsMediaOpen)
            {
                return null;
            }

            var position = engine.Position ?? ReviewPosition.Empty;
            return new LoopPlaybackAnchorSnapshot(
                ResolvePaneId(pane),
                ResolvePaneKey(pane),
                ResolvePaneLabel(pane),
                position.PresentationTime,
                new LoopPlaybackFrameIdentitySnapshot(
                    position.IsFrameIndexAbsolute ? position.FrameIndex : null,
                    position.IsFrameIndexAbsolute,
                    position.PresentationTimestamp,
                    position.DecodeTimestamp));
        }

        private static bool IsLoopMarkerTimeOrderValid(
            LoopPlaybackMarkerEndpoint endpoint,
            TimeSpan target,
            TimeSpan opposite)
        {
            return endpoint == LoopPlaybackMarkerEndpoint.In
                ? target <= opposite
                : target >= opposite;
        }

        private void UpdateRecentFilesMenu()
        {
            if (_nativeRecentFilesMenuItem?.Menu == null)
            {
                return;
            }

            _nativeRecentFilesMenuItem.Menu.Items.Clear();
            var recentFiles = _recentFilesService.Load().ToList();
            if (recentFiles.Count == 0)
            {
                _nativeRecentFilesMenuItem.Menu.Items.Add(new NativeMenuItem("No Recent Files")
                {
                    IsEnabled = false
                });
                return;
            }

            foreach (var filePath in recentFiles)
            {
                var menuItem = new NativeMenuItem(Path.GetFileName(filePath));
                menuItem.Click += async (_, _) => await OpenRecentPathAsync(filePath);
                _nativeRecentFilesMenuItem.Menu.Items.Add(menuItem);
            }
        }

        private async void Window_KeyDown(object? sender, KeyEventArgs e)
        {
            if (e.Key == Key.Space)
            {
                PlayPauseButton_Click(sender, e);
                e.Handled = true;
            }
            else if (e.Key == Key.N && e.KeyModifiers.HasFlag(CommandKeyModifier))
            {
                LaunchNewWindow();
                e.Handled = true;
            }
            else if (e.Key == Key.Left)
            {
                await GetEngine(GetFocusedPane()).StepBackwardAsync();
                e.Handled = true;
            }
            else if (e.Key == Key.Right)
            {
                await GetEngine(GetFocusedPane()).StepForwardAsync();
                e.Handled = true;
            }
            else if (e.Key == Key.O && e.KeyModifiers.HasFlag(CommandKeyModifier))
            {
                await OpenVideoAsync(GetFileOpenTargetPane());
                e.Handled = true;
            }
            else if (e.Key == Key.L)
            {
                SetLoopPlaybackEnabled(!_isLoopPlaybackEnabled);
                e.Handled = true;
            }
            else if (e.Key == Key.OemOpenBrackets)
            {
                SetLoopMarker(LoopPlaybackMarkerEndpoint.In);
                e.Handled = true;
            }
            else if (e.Key == Key.OemCloseBrackets)
            {
                SetLoopMarker(LoopPlaybackMarkerEndpoint.Out);
                e.Handled = true;
            }
            else if (TryHandleZoomShortcut(e.Key))
            {
                e.Handled = true;
            }
        }

        private bool TryHandleZoomShortcut(Key key)
        {
            if (key == Key.OemPlus || key == Key.Add)
            {
                ZoomInFocusedPane();
                return true;
            }

            if (key == Key.OemMinus || key == Key.Subtract)
            {
                ZoomOutFocusedPane();
                return true;
            }

            if (key == Key.D0 || key == Key.NumPad0)
            {
                ResetZoomForFocusedPane();
                return true;
            }

            return false;
        }

        private static void Window_DragOver(object? sender, DragEventArgs e)
        {
            e.DragEffects = e.DataTransfer.Contains(DataFormat.File) ? DragDropEffects.Copy : DragDropEffects.None;
        }

        private async void Window_Drop(object? sender, DragEventArgs e)
        {
            var file = e.DataTransfer.TryGetFiles()?.FirstOrDefault();
            if (file != null && !string.IsNullOrWhiteSpace(file.Path.LocalPath))
            {
                await OpenPathAsync(file.Path.LocalPath, ResolvePaneFromDragSource(e.Source));
            }
        }

        private Pane ResolvePaneFromDragSource(object? source)
        {
            var current = source as StyledElement;
            while (current != null)
            {
                if (ReferenceEquals(current, ComparePaneBorder))
                {
                    return Pane.Compare;
                }

                if (ReferenceEquals(current, PrimaryPaneBorder))
                {
                    return Pane.Primary;
                }

                if (current is Control control &&
                    control.Tag is string paneId)
                {
                    return ResolvePane(paneId);
                }

                current = current.Parent as StyledElement;
            }

            return GetFocusedPane();
        }

        private void MinimizeWindow_Click(object? sender, RoutedEventArgs e)
        {
            WindowState = WindowState.Minimized;
        }

        private void ToggleWindowState_Click(object? sender, RoutedEventArgs e)
        {
            WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
        }

        private void CloseWindow_Click(object? sender, RoutedEventArgs e)
        {
            Close();
        }

        private void TitleBar_PointerPressed(object? sender, PointerPressedEventArgs e)
        {
            if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
            {
                BeginMoveDrag(e);
            }
        }

        private void ExitMenuItem_Click(object? sender, RoutedEventArgs e)
        {
            Close();
        }

        private void ToggleFullScreenMenuItem_Click(object? sender, RoutedEventArgs e)
        {
            ToggleFullScreen();
        }

        private void VideoInfoMenuItem_Click(object? sender, RoutedEventArgs e)
        {
            ShowVideoInfo(GetFocusedPane());
        }

        private void ShowVideoInfo(Pane pane)
        {
            var engine = TryGetExistingEngine(pane);
            var info = engine?.MediaInfo;
            if (info == null || string.IsNullOrWhiteSpace(info.FilePath))
            {
                ShowTextDialog("Video Info", "Video info is unavailable until media is loaded in the selected pane.");
                return;
            }

            ShowTextDialog("Video Info - " + ResolvePaneLabel(pane), BuildVideoInfoText(pane, info, engine!));
        }

        private async void ExportDiagnosticsMenuItem_Click(object? sender, RoutedEventArgs e)
        {
            await ExportDiagnosticsAsync(null);
        }

        private async void SaveLoopAsClipMenuItem_Click(object? sender, RoutedEventArgs e)
        {
            await ExportLoopClipAsync(null, ResolvePaneId(GetFocusedPane()));
        }

        private async void ExportSideBySideCompareMenuItem_Click(object? sender, RoutedEventArgs e)
        {
            await ExportSideBySideCompareFromCurrentLoopStateAsync();
        }

        private async Task<CompareSideBySideExportResult?> ExportSideBySideCompareFromCurrentLoopStateAsync()
        {
            var mode = _primaryLoopRange.HasLoopIn &&
                       _primaryLoopRange.HasLoopOut &&
                       _compareLoopRange.HasLoopIn &&
                       _compareLoopRange.HasLoopOut
                ? CompareSideBySideExportMode.Loop
                : CompareSideBySideExportMode.WholeVideo;
            return await ExportSideBySideCompareAsync(null, mode, CompareSideBySideExportAudioSource.Primary);
        }

        private async void ReplaceAudioTrackMenuItem_Click(object? sender, RoutedEventArgs e)
        {
            await ReplaceAudioTrackAsync();
        }

        private async Task<ClipExportResult?> ExportLoopClipAsync(string? outputPath, string? paneId)
        {
            var pane = ResolvePane(paneId);
            var engine = GetEngine(pane);
            var ffmpegEngine = engine as FfmpegReviewEngine;
            if (ffmpegEngine == null || !engine.IsMediaOpen)
            {
                CacheStatusTextBlock.Text = "Open a video before exporting a loop clip.";
                return null;
            }

            var loopRange = GetLoopRange(pane);
            if (loopRange == null || !loopRange.HasLoopIn || !loopRange.HasLoopOut || loopRange.IsInvalidRange)
            {
                CacheStatusTextBlock.Text = "Set valid loop-in and loop-out points before exporting a loop clip.";
                return null;
            }

            var resolvedOutputPath = outputPath;
            if (string.IsNullOrWhiteSpace(resolvedOutputPath))
            {
                resolvedOutputPath = await PromptForSavePathAsync(
                    "Save Loop As Clip",
                    BuildSuggestedExportFileName(engine.CurrentFilePath, "loop"),
                    "MP4 Video",
                    Mp4Pattern);
                if (string.IsNullOrWhiteSpace(resolvedOutputPath))
                {
                    return null;
                }
            }

            try
            {
                await engine.PauseAsync().ConfigureAwait(false);
                await SetStatusMessageAsync("Exporting loop clip...").ConfigureAwait(false);
                var request = new ClipExportRequest(
                    engine.CurrentFilePath,
                    resolvedOutputPath,
                    ResolvePaneLabel(pane),
                    ResolvePaneId(pane),
                    true,
                    BuildReviewSessionSnapshot(pane, engine),
                    loopRange,
                    ffmpegEngine,
                    BuildPaneViewport(pane, engine.MediaInfo));
                var plan = ClipExportService.CreatePlan(request);
                var result = await ClipExportService.ExportPlanAsync(plan).ConfigureAwait(false);
                await SetStatusMessageAsync(result.Succeeded
                    ? "Clip export completed: " + Path.GetFileName(result.Plan.OutputFilePath)
                    : "Clip export failed: " + result.Message).ConfigureAwait(false);
                return result;
            }
            catch (Exception ex)
            {
                await SetStatusMessageAsync("Clip export failed: " + ex.Message).ConfigureAwait(false);
                return new ClipExportResult(false, null, ex.Message, -1, TimeSpan.Zero, null, string.Empty, string.Empty);
            }
        }

        private async Task<CompareSideBySideExportResult?> ExportSideBySideCompareAsync(
            string? outputPath,
            CompareSideBySideExportMode mode,
            CompareSideBySideExportAudioSource audioSource)
        {
            if (CompareModeCheckBox.IsChecked != true)
            {
                CacheStatusTextBlock.Text = "Enable two-pane compare before exporting side-by-side video.";
                return null;
            }

            var primaryEngine = _primaryEngine as FfmpegReviewEngine;
            var compareEngine = _compareEngine as FfmpegReviewEngine;
            if (primaryEngine == null || compareEngine == null || !primaryEngine.IsMediaOpen || !compareEngine.IsMediaOpen)
            {
                CacheStatusTextBlock.Text = "Load both compare panes before exporting side-by-side video.";
                return null;
            }

            if (mode == CompareSideBySideExportMode.Loop &&
                (!_primaryLoopRange.HasLoopIn ||
                 !_primaryLoopRange.HasLoopOut ||
                 !_compareLoopRange.HasLoopIn ||
                 !_compareLoopRange.HasLoopOut ||
                 _primaryLoopRange.IsInvalidRange ||
                 _compareLoopRange.IsInvalidRange))
            {
                CacheStatusTextBlock.Text = "Loop compare export requires valid loop points on both panes.";
                return null;
            }

            var resolvedOutputPath = outputPath;
            if (string.IsNullOrWhiteSpace(resolvedOutputPath))
            {
                resolvedOutputPath = await PromptForSavePathAsync(
                    "Export Side-by-Side Compare",
                    BuildSuggestedExportFileName(primaryEngine.CurrentFilePath, mode == CompareSideBySideExportMode.Loop ? "compare-loop" : PaneCompareKey),
                    "MP4 Video",
                    Mp4Pattern);
                if (string.IsNullOrWhiteSpace(resolvedOutputPath))
                {
                    return null;
                }
            }

            try
            {
                await primaryEngine.PauseAsync().ConfigureAwait(false);
                await compareEngine.PauseAsync().ConfigureAwait(false);
                await SetStatusMessageAsync("Exporting side-by-side compare...").ConfigureAwait(false);
                var request = new CompareSideBySideExportRequest
                {
                    OutputFilePath = resolvedOutputPath,
                    Mode = mode,
                    AudioSource = audioSource,
                    PrimarySessionSnapshot = BuildReviewSessionSnapshot(Pane.Primary, primaryEngine),
                    CompareSessionSnapshot = BuildReviewSessionSnapshot(Pane.Compare, compareEngine),
                    PrimaryViewportSnapshot = BuildPaneViewport(Pane.Primary, primaryEngine.MediaInfo),
                    CompareViewportSnapshot = BuildPaneViewport(Pane.Compare, compareEngine.MediaInfo),
                    PrimaryLoopRange = _primaryLoopRange,
                    CompareLoopRange = _compareLoopRange,
                    PrimaryEngine = primaryEngine,
                    CompareEngine = compareEngine
                };
                var plan = CompareSideBySideExportService.CreatePlan(request);
                var result = await CompareSideBySideExportService.ExportPlanAsync(plan).ConfigureAwait(false);
                await SetStatusMessageAsync(result.Succeeded
                    ? "Side-by-side compare export completed: " + Path.GetFileName(result.Plan.OutputFilePath)
                    : "Side-by-side compare export failed: " + result.Message).ConfigureAwait(false);
                return result;
            }
            catch (Exception ex)
            {
                await SetStatusMessageAsync("Side-by-side compare export failed: " + ex.Message).ConfigureAwait(false);
                return new CompareSideBySideExportResult
                {
                    Succeeded = false,
                    Message = ex.Message,
                    ExitCode = -1,
                    Elapsed = TimeSpan.Zero
                };
            }
        }

        private async Task<AudioInsertionResult?> ReplaceAudioTrackAsync()
        {
            var replacementAudioPath = await PromptForOpenPathAsync(
                "Select Replacement Audio",
                "Audio Files",
                "*.wav",
                "*.mp3");
            if (string.IsNullOrWhiteSpace(replacementAudioPath))
            {
                return null;
            }

            var outputPath = await PromptForSavePathAsync(
                "Replace Audio Track",
                BuildSuggestedExportFileName(_primaryEngine.CurrentFilePath, "audio-inserted"),
                "MP4 Video",
                Mp4Pattern);
            if (string.IsNullOrWhiteSpace(outputPath))
            {
                return null;
            }

            return await ReplaceAudioTrackAsync(replacementAudioPath, outputPath);
        }

        private async Task<AudioInsertionResult?> ReplaceAudioTrackAsync(string replacementAudioFilePath, string outputPath)
        {
            var ffmpegEngine = _primaryEngine as FfmpegReviewEngine;
            if (ffmpegEngine == null || !_primaryEngine.IsMediaOpen)
            {
                CacheStatusTextBlock.Text = "Load a primary MP4 before replacing audio.";
                return null;
            }

            try
            {
                await _primaryEngine.PauseAsync().ConfigureAwait(false);
                await SetStatusMessageAsync("Replacing audio track...").ConfigureAwait(false);
                var request = new AudioInsertionRequest(
                    _primaryEngine.CurrentFilePath,
                    replacementAudioFilePath,
                    outputPath,
                    PanePrimaryLabel,
                    BuildReviewSessionSnapshot(Pane.Primary, _primaryEngine));
                var plan = AudioInsertionService.CreatePlan(request);
                var result = await AudioInsertionService.InsertPlanAsync(plan).ConfigureAwait(false);
                await SetStatusMessageAsync(result.Succeeded
                    ? "Audio track replaced: " + Path.GetFileName(result.Plan.OutputFilePath)
                    : "Audio insertion failed: " + result.Message).ConfigureAwait(false);
                return result;
            }
            catch (Exception ex)
            {
                await SetStatusMessageAsync("Audio insertion failed: " + ex.Message).ConfigureAwait(false);
                return new AudioInsertionResult(false, null, ex.Message, -1, TimeSpan.Zero, null, null, string.Empty, string.Empty);
            }
        }

        private async Task<string?> ExportDiagnosticsAsync(string? outputPath)
        {
            var resolvedOutputPath = outputPath;
            if (string.IsNullOrWhiteSpace(resolvedOutputPath))
            {
                resolvedOutputPath = await PromptForSavePathAsync(
                    "Export Diagnostic Report",
                    "frame-player-macos-diagnostics.txt",
                    "Text File",
                    "*.txt");
                if (string.IsNullOrWhiteSpace(resolvedOutputPath))
                {
                    return null;
                }
            }

            var report = BuildDiagnosticsReport();
            await File.WriteAllTextAsync(resolvedOutputPath, report, new UTF8Encoding(false)).ConfigureAwait(false);
            await SetStatusMessageAsync("Diagnostic report exported: " + Path.GetFileName(resolvedOutputPath)).ConfigureAwait(false);
            return resolvedOutputPath;
        }

        private Task SetStatusMessageAsync(string message)
        {
            var dispatcher = CacheStatusTextBlock.Dispatcher;
            try
            {
                if (dispatcher.CheckAccess())
                {
                    CacheStatusTextBlock.Text = message;
                    return Task.CompletedTask;
                }
            }
            catch
            {
                // In headless tests the global dispatcher and control owner can disagree briefly.
            }

            dispatcher.Post(() =>
            {
                try
                {
                    CacheStatusTextBlock.Text = message;
                }
                catch
                {
                    // Status text is informational; export/test completion must not depend on it.
                }
            });
            return Task.CompletedTask;
        }

        private string BuildDiagnosticsReport()
        {
            var builder = new StringBuilder();
            builder.AppendLine("Frame Player desktop preview diagnostics");
            builder.AppendLine("Generated: " + DateTimeOffset.Now.ToString("O", CultureInfo.InvariantCulture));
            AppendEngineDiagnostics(builder, PanePrimaryLabel, _primaryEngine);
            if (_compareEngine != null)
            {
                AppendEngineDiagnostics(builder, PaneCompareLabel, _compareEngine);
            }

            builder.AppendLine("Loop primary: " + BuildLoopStatusText(_primaryLoopRange));
            builder.AppendLine("Loop compare: " + BuildLoopStatusText(_compareLoopRange));
            builder.AppendLine("Runtime base: " + AppContext.BaseDirectory);
            return builder.ToString();
        }

        private static void AppendEngineDiagnostics(StringBuilder builder, string label, IVideoReviewEngine engine)
        {
            builder.AppendLine(label + ":");
            builder.AppendLine("  File: " + (engine.CurrentFilePath ?? string.Empty));
            builder.AppendLine("  Open: " + engine.IsMediaOpen.ToString(CultureInfo.InvariantCulture));
            builder.AppendLine("  Playing: " + engine.IsPlaying.ToString(CultureInfo.InvariantCulture));
            builder.AppendLine("  Position: " + FormatTime(engine.Position.PresentationTime));
            builder.AppendLine("  Duration: " + FormatTime(engine.MediaInfo.Duration));
            builder.AppendLine("  Video: " + engine.MediaInfo.PixelWidth.ToString(CultureInfo.InvariantCulture) + "x" + engine.MediaInfo.PixelHeight.ToString(CultureInfo.InvariantCulture) + " " + engine.MediaInfo.VideoCodecName);
            builder.AppendLine("  Audio: " + (engine.MediaInfo.HasAudioStream ? engine.MediaInfo.AudioCodecName : "none"));
        }

        private static ReviewSessionSnapshot BuildReviewSessionSnapshot(Pane pane, IVideoReviewEngine engine)
        {
            return new ReviewSessionSnapshot(
                ResolvePaneKey(pane),
                ResolvePaneLabel(pane),
                ReviewSessionSnapshot.FromEngineState(engine.IsMediaOpen, engine.IsPlaying),
                engine.CurrentFilePath,
                engine.MediaInfo,
                engine.Position);
        }

        private PaneViewportSnapshot BuildPaneViewport(Pane pane, VideoMediaInfo mediaInfo)
        {
            return BuildPaneViewport(pane, Math.Max(1, mediaInfo.PixelWidth), Math.Max(1, mediaInfo.PixelHeight));
        }

        private PaneViewportSnapshot BuildPaneViewport(Pane pane, DecodedFrameBuffer frameBuffer)
        {
            var descriptor = frameBuffer.Descriptor;
            return BuildPaneViewport(pane, Math.Max(1, descriptor.PixelWidth), Math.Max(1, descriptor.PixelHeight));
        }

        private PaneViewportSnapshot BuildPaneViewport(Pane pane, int sourceWidth, int sourceHeight)
        {
            var zoomFactor = GetPaneZoomFactor(pane);
            if (zoomFactor <= MinimumPaneZoomFactor + 0.0001d)
            {
                return PaneViewportSnapshot.CreateFullFrame(sourceWidth, sourceHeight);
            }

            var cropWidth = Math.Max(1, (int)Math.Round(sourceWidth / zoomFactor));
            var cropHeight = Math.Max(1, (int)Math.Round(sourceHeight / zoomFactor));
            var cropX = Math.Max(0, (sourceWidth - cropWidth) / 2);
            var cropY = Math.Max(0, (sourceHeight - cropHeight) / 2);
            return new PaneViewportSnapshot(
                zoomFactor,
                0.5d,
                0.5d,
                sourceWidth,
                sourceHeight,
                cropX,
                cropY,
                cropWidth,
                cropHeight);
        }

        private static string BuildSuggestedExportFileName(string sourceFilePath, string suffix)
        {
            var baseName = string.IsNullOrWhiteSpace(sourceFilePath)
                ? "video"
                : Path.GetFileNameWithoutExtension(sourceFilePath);
            foreach (var invalid in Path.GetInvalidFileNameChars())
            {
                baseName = baseName.Replace(invalid, '-');
            }

            return string.IsNullOrWhiteSpace(baseName)
                ? "video-" + suffix + Mp4Extension
                : baseName + "-" + suffix + Mp4Extension;
        }

        private async Task<string?> PromptForSavePathAsync(string title, string suggestedFileName, string fileTypeName, params string[] patterns)
        {
            var topLevel = GetTopLevel(this);
            if (topLevel == null)
            {
                return null;
            }

            var file = await topLevel.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
            {
                Title = title,
                SuggestedFileName = suggestedFileName,
                FileTypeChoices = new[]
                {
                    new FilePickerFileType(fileTypeName)
                    {
                        Patterns = patterns
                    }
                }
            });
            return file?.Path.LocalPath;
        }

        private async Task<string?> PromptForOpenPathAsync(string title, string fileTypeName, params string[] patterns)
        {
            var topLevel = GetTopLevel(this);
            if (topLevel == null)
            {
                return null;
            }

            var files = await topLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = title,
                AllowMultiple = false,
                FileTypeFilter = new[]
                {
                    new FilePickerFileType(fileTypeName)
                    {
                        Patterns = patterns
                    }
                }
            });
            return files.Count > 0 ? files[0].Path.LocalPath : null;
        }

        private void HelpMenuItem_Click(object? sender, RoutedEventArgs e)
        {
            ShowTextDialog("Controls and Shortcuts", BuildHelpText());
        }

        private void AboutMenuItem_Click(object? sender, RoutedEventArgs e)
        {
            ShowTextDialog("About Frame Player", BuildAboutText());
        }

        private void SetGpuAccelerationPreference(bool useGpuAcceleration)
        {
            if (_nativeGpuAccelerationMenuItem != null)
            {
                _nativeGpuAccelerationMenuItem.IsChecked = useGpuAcceleration;
            }

            if (_optionsProvider.UseGpuAcceleration != useGpuAcceleration)
            {
                _optionsProvider.SetUseGpuAcceleration(useGpuAcceleration);
            }

            CacheStatusTextBlock.Text = useGpuAcceleration
                ? "GPU acceleration enabled for newly opened media."
                : "GPU acceleration disabled for newly opened media.";
        }

        private void ShowTextDialog(string title, string body)
        {
            var dialog = new Window
            {
                Title = title,
                Width = 560,
                Height = 420,
                MinWidth = 420,
                MinHeight = 260,
                Background = Brush.Parse("#101418"),
                WindowStartupLocation = WindowStartupLocation.CenterOwner
            };

            var closeButton = new Button
            {
                Content = "Close",
                HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right,
                MinWidth = 88
            };
            closeButton.Click += (_, _) => dialog.Close();

            dialog.Content = new Border
            {
                Padding = new Thickness(18),
                Child = new Grid
                {
                    RowDefinitions = new RowDefinitions("*,Auto"),
                    Children =
                    {
                        new ScrollViewer
                        {
                            Content = new TextBlock
                            {
                                Text = body,
                                Foreground = Brush.Parse("#F3F4F6"),
                                TextWrapping = TextWrapping.Wrap,
                                FontSize = 13,
                                LineHeight = 20
                            }
                        },
                        closeButton
                    }
                }
            };
            Grid.SetRow(closeButton, 1);

            dialog.Show(this);
        }

        private static string BuildVideoInfoText(Pane pane, VideoMediaInfo info, IVideoReviewEngine engine)
        {
            var builder = new StringBuilder();
            builder.AppendLine(ResolvePaneLabel(pane));
            builder.AppendLine();
            builder.AppendLine("File: " + info.FilePath);
            builder.AppendLine("Duration: " + FormatTime(info.Duration));
            builder.AppendLine("Position: " + FormatTime(engine.Position.PresentationTime));
            builder.AppendLine(string.Format(CultureInfo.InvariantCulture, "Resolution: {0} x {1}", info.PixelWidth, info.PixelHeight));
            builder.AppendLine(string.Format(CultureInfo.InvariantCulture, "Frame rate: {0:0.###} fps", info.FramesPerSecond));
            builder.AppendLine("Video codec: " + FormatDialogValue(info.VideoCodecName));
            builder.AppendLine("Pixel format: " + FormatDialogValue(info.SourcePixelFormatName));
            builder.AppendLine("Audio: " + (info.HasAudioStream ? FormatDialogValue(info.AudioCodecName) : "none"));
            if (info.HasAudioStream)
            {
                builder.AppendLine(string.Format(CultureInfo.InvariantCulture, "Audio sample rate: {0} Hz", info.AudioSampleRate));
                builder.AppendLine(string.Format(CultureInfo.InvariantCulture, "Audio channels: {0}", info.AudioChannelCount));
                builder.AppendLine("Audio playback: " + (info.IsAudioPlaybackAvailable ? "available" : "unavailable"));
            }

            builder.AppendLine("Video stream index: " + info.VideoStreamIndex.ToString(CultureInfo.InvariantCulture));
            builder.AppendLine(string.Format(
                CultureInfo.InvariantCulture,
                "Nominal frame rate: {0}/{1}",
                info.NominalFrameRateNumerator,
                info.NominalFrameRateDenominator));
            builder.AppendLine(string.Format(
                CultureInfo.InvariantCulture,
                "Stream time base: {0}/{1}",
                info.StreamTimeBaseNumerator,
                info.StreamTimeBaseDenominator));
            return builder.ToString();
        }

        private static string BuildHelpText()
        {
            return string.Join(
                Environment.NewLine,
                "Space: play or pause",
                "Left / Right: previous or next frame",
                CommandKeyLabel + "+O: open video",
                CommandKeyLabel + "+N: new window",
                "L: loop playback",
                "[ / ]: set loop in or loop out",
                "Playback menu: zoom in, zoom out, reset zoom",
                "F11: full screen",
                "Right-click video: video info, reset zoom, loop export, compare export",
                "Right-click timeline: set loop markers and export loop");
        }

        private static string BuildAboutText()
        {
            var version = typeof(MainWindow).Assembly.GetName().Version?.ToString() ?? "unknown";
            return string.Join(
                Environment.NewLine,
                "Frame Player",
                "Avalonia Desktop Preview",
                "Version: " + version,
                "",
                "Windows WPF v1.8.4 and macOS Preview 0.1.0 remain the protected release tracks. This build is a separate Avalonia desktop preview.");
        }

        private static string FormatDialogValue(string? value)
        {
            return string.IsNullOrWhiteSpace(value) ? "unknown" : value.Trim();
        }

        private NativeMenu BuildNativeMenu(bool useGpuAcceleration)
        {
            _nativeRecentFilesMenuItem = new NativeMenuItem("Open Recent")
            {
                Menu = new NativeMenu()
            };

            var fileMenu = CreateTopLevelMenu(
                "File",
                CreateMenuItem("New Window", (_, _) => LaunchNewWindow(), new KeyGesture(Key.N, CommandKeyModifier)),
                new NativeMenuItemSeparator(),
                CreateMenuItem("Open Video...", async (_, _) => await OpenVideoAsync(GetFileOpenTargetPane()), new KeyGesture(Key.O, CommandKeyModifier)),
                _nativeRecentFilesMenuItem,
                new NativeMenuItemSeparator(),
                CreateMenuItem("Close Video", async (_, _) => await CloseVideosAsync(), new KeyGesture(Key.W, CommandKeyModifier)),
                CreateMenuItem("Video Info...", (sender, _) => VideoInfoMenuItem_Click(sender, new RoutedEventArgs())),
                new NativeMenuItemSeparator(),
                CreateMenuItem("Export Diagnostic Report...", (sender, _) => ExportDiagnosticsMenuItem_Click(sender, new RoutedEventArgs())),
                new NativeMenuItemSeparator(),
                CreateMenuItem("Exit", (_, _) => Close()));

            _nativePlayPauseMenuItem = CreateMenuItem("Play", (sender, _) => PlayPauseButton_Click(sender, new RoutedEventArgs()), new KeyGesture(Key.Space));
            _nativeGpuAccelerationMenuItem = CreateMenuItem("Use GPU Acceleration", (_, _) =>
            {
                SetGpuAccelerationPreference(_nativeGpuAccelerationMenuItem?.IsChecked != true);
            });
            _nativeGpuAccelerationMenuItem.ToggleType = MenuItemToggleType.CheckBox;
            _nativeGpuAccelerationMenuItem.IsChecked = useGpuAcceleration;

            _nativeLoopPlaybackMenuItem = CreateToggleMenuItem("Loop Playback", new KeyGesture(Key.L));
            _nativeLoopPlaybackMenuItem.Click += (_, _) => SetLoopPlaybackEnabled(!_isLoopPlaybackEnabled);

            var playbackMenu = CreateTopLevelMenu(
                "Playback",
                _nativePlayPauseMenuItem,
                new NativeMenuItemSeparator(),
                CreateMenuItem("Rewind 5s", (sender, _) => RewindButton_Click(sender, new RoutedEventArgs())),
                CreateMenuItem("Fast Forward 5s", (sender, _) => FastForwardButton_Click(sender, new RoutedEventArgs())),
                new NativeMenuItemSeparator(),
                CreateMenuItem("Previous Frame", (sender, _) => PreviousFrameButton_Click(sender, new RoutedEventArgs()), new KeyGesture(Key.Left)),
                CreateMenuItem("Next Frame", (sender, _) => NextFrameButton_Click(sender, new RoutedEventArgs()), new KeyGesture(Key.Right)),
                new NativeMenuItemSeparator(),
                _nativeLoopPlaybackMenuItem,
                CreateMenuItem("Set Loop In", (_, _) => SetLoopMarker(LoopPlaybackMarkerEndpoint.In)),
                CreateMenuItem("Set Loop Out", (_, _) => SetLoopMarker(LoopPlaybackMarkerEndpoint.Out)),
                CreateMenuItem("Clear Loop Points", (_, _) => ClearLoopPoints()),
                CreateMenuItem("Save Loop As Clip...", (sender, _) => SaveLoopAsClipMenuItem_Click(sender, new RoutedEventArgs())),
                CreateMenuItem("Export Side-by-Side Compare...", (sender, _) => ExportSideBySideCompareMenuItem_Click(sender, new RoutedEventArgs())),
                new NativeMenuItemSeparator(),
                CreateCommandMenuItem("Zoom In", ZoomInFocusedPane, new KeyGesture(Key.OemPlus)),
                CreateCommandMenuItem("Zoom Out", ZoomOutFocusedPane, new KeyGesture(Key.OemMinus)),
                CreateCommandMenuItem("Reset Zoom", ResetZoomForFocusedPane, new KeyGesture(Key.D0)),
                new NativeMenuItemSeparator(),
                _nativeGpuAccelerationMenuItem,
                CreateMenuItem("Toggle Full Screen", (_, _) => ToggleFullScreen(), new KeyGesture(Key.F11)));

            var audioMenu = CreateTopLevelMenu(
                "Audio Insertion",
                CreateMenuItem("Replace Audio Track...", (sender, _) => ReplaceAudioTrackMenuItem_Click(sender, new RoutedEventArgs())));

            var helpMenu = CreateTopLevelMenu(
                "Help",
                CreateMenuItem("Controls and Shortcuts...", (sender, _) => HelpMenuItem_Click(sender, new RoutedEventArgs())),
                new NativeMenuItemSeparator(),
                CreateMenuItem("About Frame Player", (sender, _) => AboutMenuItem_Click(sender, new RoutedEventArgs())));

            var menu = new NativeMenu();
            menu.Items.Add(fileMenu);
            menu.Items.Add(playbackMenu);
            menu.Items.Add(audioMenu);
            menu.Items.Add(helpMenu);
            return menu;
        }

        private static NativeMenuItem CreateTopLevelMenu(string header, params NativeMenuItemBase[] items)
        {
            var menuItem = new NativeMenuItem(header)
            {
                Menu = new NativeMenu()
            };

            foreach (var item in items)
            {
                menuItem.Menu.Items.Add(item);
            }

            return menuItem;
        }

        private static NativeMenuItem CreateMenuItem(string header, EventHandler click, KeyGesture? gesture = null)
        {
            var menuItem = new NativeMenuItem(header);
            if (gesture != null)
            {
                menuItem.Gesture = gesture;
            }

            menuItem.Click += click;
            return menuItem;
        }

        private static NativeMenuItem CreateCommandMenuItem(string header, Action execute, KeyGesture? gesture = null)
        {
            var menuItem = new NativeMenuItem(header)
            {
                Command = new ActionCommand(execute)
            };
            if (gesture != null)
            {
                menuItem.Gesture = gesture;
            }

            return menuItem;
        }

        private static NativeMenuItem CreateToggleMenuItem(string header, KeyGesture? gesture = null)
        {
            var menuItem = new NativeMenuItem(header)
            {
                ToggleType = MenuItemToggleType.CheckBox
            };
            if (gesture != null)
            {
                menuItem.Gesture = gesture;
            }

            return menuItem;
        }

        private sealed class ActionCommand : ICommand
        {
            private readonly Action _execute;
            private EventHandler? _canExecuteChanged;

            public ActionCommand(Action execute)
            {
                _execute = execute ?? throw new ArgumentNullException(nameof(execute));
            }

            public event EventHandler? CanExecuteChanged
            {
                add { _canExecuteChanged += value; }
                remove { _canExecuteChanged -= value; }
            }

            public bool CanExecute(object? parameter)
            {
                return true;
            }

            public void Execute(object? parameter)
            {
                _execute();
                _canExecuteChanged?.Invoke(this, EventArgs.Empty);
            }
        }

        private void ToggleFullScreen()
        {
            WindowState = WindowState == WindowState.FullScreen ? WindowState.Normal : WindowState.FullScreen;
        }

        private void LaunchNewWindow()
        {
            try
            {
                AppInstanceLauncher.LaunchNewInstance();
                CacheStatusTextBlock.Text = "Opened a new Frame Player window.";
            }
            catch (Exception ex)
            {
                CacheStatusTextBlock.Text = "New window failed: " + ex.Message;
            }
        }

        private static string FormatTime(TimeSpan time)
        {
            if (time < TimeSpan.Zero)
            {
                time = TimeSpan.Zero;
            }

            return string.Format(
                CultureInfo.InvariantCulture,
                "{0:00}:{1:00}:{2:00}.{3:000}",
                (int)time.TotalHours,
                time.Minutes,
                time.Seconds,
                time.Milliseconds);
        }

        protected override void OnClosed(EventArgs e)
        {
            _primaryEngine.Dispose();
            _compareEngine?.Dispose();
            base.OnClosed(e);
        }

        private enum Pane
        {
            Primary,
            Compare
        }
    }
}
