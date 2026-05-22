using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using FramePlayer.Core.Abstractions;
using FramePlayer.Core.Coordination;
using FramePlayer.Core.Events;
using FramePlayer.Core.Models;
using FramePlayer.Engines.FFmpeg;
using FramePlayer.Avalonia.Services;
using FramePlayer.Services;

namespace FramePlayer.Avalonia.Views
{
    public sealed partial class MainWindow : Window
    {
        private readonly VideoReviewEngineFactory _engineFactory;
        private readonly UnifiedRecentFilesService _recentFilesService;
        private readonly FfmpegReviewEngineOptionsProvider _optionsProvider;
        private readonly DiagnosticLogService _diagnosticLogService;
        private readonly IVideoReviewEngine _primaryEngine;
        private IVideoReviewEngine? _compareEngine;
        private DecodedFrameBuffer? _primaryFrameBuffer;
        private DecodedFrameBuffer? _compareFrameBuffer;
        private WriteableBitmap? _primaryReusableBitmap;
        private WriteableBitmap? _compareReusableBitmap;
        private readonly object _primaryFramePresentationLock = new();
        private readonly object _compareFramePresentationLock = new();
        private DecodedFrameBuffer? _pendingPrimaryFrameBuffer;
        private DecodedFrameBuffer? _pendingCompareFrameBuffer;
        private bool _primaryFramePresentationQueued;
        private bool _compareFramePresentationQueued;
        private const int ControlModifiedFrameStep = 10;
        private const int HundredFrameStep = 100;
        private const double MinimumPaneZoomFactor = 1d;
        private const double MaximumPaneZoomFactor = 12d;
        private const double PaneZoomStep = 1.1d;
        private const double PaneWheelZoomStep = 1.08d;
        private const double PaneWheelZoomDeltaLimit = 4d;
        private const double PaneWheelZoomDeltaThreshold = 0.01d;
        private const double PaneHeaderHeight = 46d;
        private const double PaneFooterHeight = 118d;
        private const string PanePrimaryId = "pane-primary";
        private const string PaneCompareId = "pane-compare";
        private const string PanePrimaryKey = "primary";
        private const string PaneCompareKey = "compare";
        private const string PanePrimaryLabel = "Primary";
        private const string PaneCompareLabel = "Compare";
        private const string Mp4Pattern = "*.mp4";
        private const string Mp4Extension = ".mp4";
        private const string UnknownRawValue = "unknown";
        private const string CompareExportSecondaryTextColor = "#B7BDC6";
        private static readonly string[] VideoFilePatterns = new[] { "*.avi", "*.m4v", Mp4Pattern, "*.mkv", "*.wmv", "*.mov" };
        private NativeMenuItem? _nativeRecentFilesMenuItem;
        private NativeMenuItem? _nativePlayPauseMenuItem;
        private NativeMenuItem? _nativeGpuAccelerationMenuItem;
        private NativeMenuItem? _nativeLoopPlaybackMenuItem;
        private NativeMenuItem? _nativeCloseVideoMenuItem;
        private NativeMenuItem? _nativeVideoInfoMenuItem;
        private NativeMenuItem? _nativeRewindMenuItem;
        private NativeMenuItem? _nativeFastForwardMenuItem;
        private NativeMenuItem? _nativePreviousFrameMenuItem;
        private NativeMenuItem? _nativeNextFrameMenuItem;
        private NativeMenuItem? _nativeSetLoopInMenuItem;
        private NativeMenuItem? _nativeSetLoopOutMenuItem;
        private NativeMenuItem? _nativeClearLoopPointsMenuItem;
        private NativeMenuItem? _nativeSaveLoopAsClipMenuItem;
        private NativeMenuItem? _nativeExportSideBySideCompareMenuItem;
        private NativeMenuItem? _nativeZoomInMenuItem;
        private NativeMenuItem? _nativeZoomOutMenuItem;
        private NativeMenuItem? _nativeResetZoomMenuItem;
        private NativeMenuItem? _nativeReplaceAudioTrackMenuItem;
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
        private static readonly TimeSpan SliderScrubThrottleInterval = TimeSpan.FromMilliseconds(75);
        private readonly DispatcherTimer _sliderScrubTimer;
        private readonly DispatcherTimer _paneSliderScrubTimer;
        private bool _isSliderScrubSeekInFlight;
        private bool _hasPendingSliderScrubTarget;
        private TimeSpan _pendingSliderScrubTarget;
        private bool _isPaneSliderScrubSeekInFlight;
        private bool _hasPendingPaneSliderScrubTarget;
        private TimeSpan _pendingPaneSliderScrubTarget;
        private Pane _pendingPaneSliderScrubPane;
        private CancellationTokenSource? _sliderScrubCts;
        private CancellationTokenSource? _paneSliderScrubCts;
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

        private static KeyModifiers CommandShiftKeyModifier
        {
            get
            {
                return CommandKeyModifier | KeyModifiers.Shift;
            }
        }

        public MainWindow()
        {
            InitializeComponent();
            ConfigurePlatformChrome();
            TryApplyWindowIcon();

            if (string.Equals(Environment.GetEnvironmentVariable("FRAMEPLAYER_AVALONIA_SKIP_RUNTIME_BOOTSTRAP"), "1", StringComparison.Ordinal))
            {
                CacheStatusTextBlock.Text = "Cache: idle";
            }
            else
            {
                try
                {
                    FfmpegRuntimeBootstrap.ConfigureForCurrentPlatform(AppContext.BaseDirectory);
                    CacheStatusTextBlock.Text = "Cache: idle";
                }
                catch (Exception ex)
                {
                    CacheStatusTextBlock.Text = "Cache: runtime unavailable";
                    ToolTip.SetTip(CacheStatusTextBlock, ex.Message);
                }
            }

            _recentFilesService = new UnifiedRecentFilesService();
            _optionsProvider = new FfmpegReviewEngineOptionsProvider(new AppPreferencesService());
            _engineFactory = new VideoReviewEngineFactory(_optionsProvider);
            _diagnosticLogService = new DiagnosticLogService();
            _primaryEngine = _engineFactory.Create(PanePrimaryKey);
            _primaryEngine.StateChanged += PrimaryEngine_StateChanged;
            _primaryEngine.FramePresented += PrimaryEngine_FramePresented;
            _primaryLoopRange = CreateLoopRange(null, null);
            _compareLoopRange = CreateLoopRange(Pane.Compare, null, null);

            ConfigurePaneZoomSurfaces();
            NativeMenu.SetMenu(this, BuildNativeMenu(_optionsProvider.UseGpuAcceleration));
            UseGpuAccelerationMenuItem.IsChecked = _optionsProvider.UseGpuAcceleration;
            UpdateRecentFilesMenu();
            UpdatePaneSelectionVisuals();
            UpdateCompareOptionState();
            UpdateCommandStates();
            InstallContextMenus();
            AddHandler(DragDrop.DropEvent, Window_Drop);
            AddHandler(DragDrop.DragOverEvent, Window_DragOver);
            KeyDown += Window_KeyDown;
            AllPanesCheckBox.IsCheckedChanged += AllPanesCheckBox_IsCheckedChanged;
            LinkPaneZoomCheckBox.IsCheckedChanged += LinkPaneZoomCheckBox_IsCheckedChanged;
            _sliderScrubTimer = new DispatcherTimer { Interval = SliderScrubThrottleInterval };
            _sliderScrubTimer.Tick += SliderScrubTimer_Tick;
            _paneSliderScrubTimer = new DispatcherTimer { Interval = SliderScrubThrottleInterval };
            _paneSliderScrubTimer.Tick += PaneSliderScrubTimer_Tick;

            _diagnosticLogService.Info("Application started");
        }

        private void ConfigurePlatformChrome()
        {
            if (!RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            {
                WindowDecorations = WindowDecorations.None;
                MenuPanel.IsVisible = true;
                return;
            }

            WindowDecorations = WindowDecorations.Full;
            MenuPanel.IsVisible = false;
            if (RootGrid.RowDefinitions.Count > 0)
            {
                RootGrid.RowDefinitions[0].Height = new GridLength(0);
            }
        }

        [SuppressMessage("Major Code Smell", "S1075:URIs should not be hardcoded", Justification = "Avalonia embedded assets are addressed by avares resource URI.")]
        private void TryApplyWindowIcon()
        {
            var outputIconPath = Path.Combine(AppContext.BaseDirectory, "Assets", "FramePlayer.ico");
            if (File.Exists(outputIconPath))
            {
                Icon = new WindowIcon(outputIconPath);
                return;
            }

            var iconUri = new Uri("avares://FramePlayer.Avalonia/Assets/FramePlayer.ico");
            if (AssetLoader.Exists(iconUri))
            {
                using var iconStream = AssetLoader.Open(iconUri);
                Icon = new WindowIcon(iconStream);
            }
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

        [SuppressMessage("Major Code Smell", "S1144:Unused private types or members", Justification = "Invoked by the desktop parity harness through reflection.")]
        private Task OpenMediaAsync(string filePath)
        {
            return OpenMediaAsync(filePath, PanePrimaryId);
        }

        [SuppressMessage("Major Code Smell", "S1144:Unused private types or members", Justification = "Invoked by the desktop parity harness through reflection.")]
        private Task OpenMediaAsync(string filePath, string paneId)
        {
            return OpenPathAsync(filePath, ResolvePane(paneId));
        }

        [SuppressMessage("Major Code Smell", "S1144:Unused private types or members", Justification = "Invoked by the desktop parity harness through reflection.")]
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
                _diagnosticLogService.Info("File opened: " + BuildDiagnosticFileIdentifier(filePath));
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

            UpdateCommandStates();
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
            var retainedFrameBuffer = frameBuffer.Retain();
            if (pane == Pane.Compare)
            {
                var previousFrameBuffer = _compareFrameBuffer;
                _compareFrameBuffer = retainedFrameBuffer;
                previousFrameBuffer?.Dispose();
                var bitmap = AvaloniaFrameBufferPresenter.PresentBitmap(frameBuffer, viewport, ref _compareReusableBitmap);
                CompareVideoSurface.Source = bitmap;
                CompareEmptyStateOverlay.IsVisible = false;
            }
            else
            {
                var previousFrameBuffer = _primaryFrameBuffer;
                _primaryFrameBuffer = retainedFrameBuffer;
                previousFrameBuffer?.Dispose();
                var bitmap = AvaloniaFrameBufferPresenter.PresentBitmap(frameBuffer, viewport, ref _primaryReusableBitmap);
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
            UpdateCacheStatusFromEngine();
        }

        private void UpdatePaneSelectionVisuals()
        {
            var highlightPaneSelection = CompareModeCheckBox.IsChecked == true;
            var compareIsFocused = highlightPaneSelection && _focusedPane == Pane.Compare;
            var primaryIsFocused = highlightPaneSelection && !compareIsFocused;

            PrimaryPaneBorder.BorderBrush = primaryIsFocused ? PaneSelectedBorderBrush : PaneChromeBorderBrush;
            PrimaryPaneBorder.BorderThickness = new Thickness(1);
            PrimaryPaneHeaderBorder.Background = compareIsFocused ? PaneChromeBrush : PaneSelectedBrush;
            PrimaryPaneHeaderBorder.BorderBrush = primaryIsFocused ? PaneSelectedBorderBrush : PaneChromeBorderBrush;
            PrimaryPaneFooterBorder.Background = compareIsFocused ? PaneChromeBrush : PaneSelectedBrush;
            PrimaryPaneFooterBorder.BorderBrush = primaryIsFocused ? PaneSelectedBorderBrush : PaneChromeBorderBrush;

            ComparePaneBorder.BorderBrush = compareIsFocused ? PaneSelectedBorderBrush : PaneChromeBorderBrush;
            ComparePaneBorder.BorderThickness = new Thickness(1);
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

            ClearPendingFramePresentations();
            CurrentFileTextBlock.Text = "No file loaded";
            PrimaryPaneFileTextBlock.Text = "No video loaded";
            ComparePaneFileTextBlock.Text = "No video loaded";
            _primaryFrameBuffer?.Dispose();
            _compareFrameBuffer?.Dispose();
            _primaryFrameBuffer = null;
            _compareFrameBuffer = null;
            CustomVideoSurface.Source = null;
            CompareVideoSurface.Source = null;
            DisposeReusablePaneBitmaps();
            SetPaneZoomFactor(Pane.Primary, MinimumPaneZoomFactor, synchronizeLinkedPane: false);
            SetPaneZoomFactor(Pane.Compare, MinimumPaneZoomFactor, synchronizeLinkedPane: false);
            PrimaryEmptyStateOverlay.IsVisible = true;
            CompareEmptyStateOverlay.IsVisible = true;
            PlaybackStateTextBlock.Text = "A/V playback + frame review";
            CurrentFrameTextBlock.Text = "Frame --";
            TimecodeTextBlock.Text = "--:--:--.--- / --:--:--.---";
            UpdateCompareOptionState();
        }

        private async void PlayPauseButton_Click(object? sender, RoutedEventArgs e)
        {
            await TogglePlaybackAsync();
        }

        private async Task TogglePlaybackAsync()
        {
            if (IsAllPaneTransportEnabled)
            {
                await ToggleAllPanePlaybackAsync();
                return;
            }

            await ToggleFocusedPanePlaybackAsync();
        }

        private async Task ToggleAllPanePlaybackAsync()
        {
            if (!CanStartAllPanePlayback())
            {
                return;
            }

            if (ShouldPauseAllPanePlayback())
            {
                await PauseAllPanePlaybackAsync();
                return;
            }

            await StartAllPanePlaybackAsync();
        }

        private async Task ToggleFocusedPanePlaybackAsync()
        {
            var engine = GetEngine(GetFocusedPane());
            if (engine.IsPlaying)
            {
                await engine.PauseAsync();
                return;
            }

            if (!CanStartPlayback(engine))
            {
                return;
            }

            await engine.PlayAsync();
        }

        private async void PlayButton_Click(object? sender, RoutedEventArgs e)
        {
            await StartPlaybackAsync(null, null);
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
                if (!CanStartPlayback(engine))
                {
                    return;
                }

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

            var engine = GetEngine(ResolvePane(paneId));
            if (!CanStartPlayback(engine))
            {
                return;
            }

            await engine.PlayAsync();
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
            CancelQueuedSliderScrubs();
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

            CancelQueuedSliderScrubs();
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
            var compareEngine = _compareEngine;
            if (!_primaryEngine.IsMediaOpen || compareEngine == null || !compareEngine.IsMediaOpen)
            {
                return;
            }

            if (!CanStartAllPanePlayback())
            {
                return;
            }

            await Task.WhenAll(
                Task.Run(() => _primaryEngine.PlayAsync()),
                Task.Run(() => compareEngine.PlayAsync()));

            UpdateCommandStates();
        }

        private async Task PauseAllPanePlaybackAsync()
        {
            var pauseTasks = new List<Task>(2);
            if (_primaryEngine.IsMediaOpen)
            {
                pauseTasks.Add(Task.Run(() => _primaryEngine.PauseAsync()));
            }

            if (_compareEngine != null && _compareEngine.IsMediaOpen)
            {
                pauseTasks.Add(Task.Run(() => _compareEngine.PauseAsync()));
            }

            if (pauseTasks.Count > 0)
            {
                await Task.WhenAll(pauseTasks);
            }
        }

        private async Task SeekRelativeAsync(TimeSpan offset)
        {
            CancelQueuedSliderScrubs();
            if (IsAllPaneTransportEnabled)
            {
                await SeekAllPaneRelativePreservingPlaybackAsync(offset);
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

            await SeekToTimePreservingPlaybackAsync(engine, target);
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
                if (IsSharedTimelineContextDisabled(explicitPane))
                {
                    CacheStatusTextBlock.Text = "Use a pane-local timeline before exporting compare loop clips.";
                    return;
                }

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
                if (IsSharedTimelineContextDisabled(explicitPane))
                {
                    setPositionAItem.IsEnabled = false;
                    setPositionBItem.IsEnabled = false;
                    loopPlaybackItem.IsEnabled = false;
                    loopPlaybackItem.IsChecked = _isLoopPlaybackEnabled;
                    saveLoopItem.IsEnabled = false;
                    return;
                }

                var pane = ResolveTimelineContextPane(explicitPane);
                SelectPane(pane);
                var target = GetTimelineContextTarget(explicitPane);
                setPositionAItem.IsEnabled = CanSetTimelineLoopMarker(pane, LoopPlaybackMarkerEndpoint.In, target);
                setPositionBItem.IsEnabled = CanSetTimelineLoopMarker(pane, LoopPlaybackMarkerEndpoint.Out, target);
                loopPlaybackItem.IsEnabled = true;
                loopPlaybackItem.IsChecked = _isLoopPlaybackEnabled;
                saveLoopItem.IsEnabled = CanExportLoopClip(pane);
            };

            return menu;
        }

        private bool IsSharedTimelineContextDisabled(Pane? explicitPane)
        {
            return explicitPane == null && IsCompareModeEnabled;
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
            if (IsSharedTimelineContextDisabled(explicitPane))
            {
                CacheStatusTextBlock.Text = "Use the primary or compare pane timeline to set compare loop points.";
                return;
            }

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
            return ClipExportService.IsBundledRuntimeAvailable &&
                   engine != null &&
                   engine.IsMediaOpen &&
                   range.HasLoopIn &&
                   range.HasLoopOut &&
                   !range.HasPendingMarkers &&
                   !range.IsInvalidRange;
        }

        private bool CanExportSideBySideCompare()
        {
            return CompareSideBySideExportService.IsBundledRuntimeAvailable &&
                   CompareModeCheckBox.IsChecked == true &&
                   _primaryEngine.IsMediaOpen &&
                   _compareEngine != null &&
                   _compareEngine.IsMediaOpen;
        }

        private IVideoReviewEngine? TryGetExistingEngine(Pane pane)
        {
            return pane == Pane.Primary ? _primaryEngine : _compareEngine;
        }

        [SuppressMessage("Major Code Smell", "S1144:Unused private types or members", Justification = "Invoked by the desktop parity harness through reflection.")]
        private async Task CommitSliderSeekAsync(string interactionName, TimeSpan target)
        {
            _ = interactionName;
            CancelQueuedSliderScrubs();
            await SeekMasterTimelineAsync(target);
        }

        private void PositionSlider_ValueChanged(object? sender, RangeBaseValueChangedEventArgs e)
        {
            if (_isUpdatingSliders || sender != PositionSlider || !_primaryEngine.IsMediaOpen)
            {
                return;
            }

            QueueSliderScrub(TimeSpan.FromSeconds(PositionSlider.Value));
        }

        private void QueueSliderScrub(TimeSpan target)
        {
            _pendingSliderScrubTarget = target;
            _hasPendingSliderScrubTarget = true;
            if (!_isSliderScrubSeekInFlight)
            {
                _sliderScrubTimer.Start();
            }
        }

        private async void SliderScrubTimer_Tick(object? sender, EventArgs e)
        {
            _sliderScrubTimer.Stop();
            if (!_hasPendingSliderScrubTarget)
            {
                return;
            }

            _hasPendingSliderScrubTarget = false;
            _isSliderScrubSeekInFlight = true;
            _sliderScrubCts?.Dispose();
            _sliderScrubCts = new CancellationTokenSource();
            try
            {
                await SeekMasterTimelineAsync(_pendingSliderScrubTarget, _sliderScrubCts.Token);
            }
            catch (OperationCanceledException)
            {
                // Ignored
            }
            finally
            {
                _isSliderScrubSeekInFlight = false;
                if (_hasPendingSliderScrubTarget)
                {
                    _sliderScrubTimer.Start();
                }
            }
        }

        private void PanePositionSlider_ValueChanged(object? sender, RangeBaseValueChangedEventArgs e)
        {
            if (_isUpdatingSliders)
            {
                return;
            }

            if (sender == PrimaryPanePositionSlider && _primaryEngine.IsMediaOpen)
            {
                SelectPane(Pane.Primary);
                QueuePaneSliderScrub(Pane.Primary, TimeSpan.FromSeconds(PrimaryPanePositionSlider.Value));
            }
            else if (sender == ComparePanePositionSlider && _compareEngine != null && _compareEngine.IsMediaOpen)
            {
                SelectPane(Pane.Compare);
                QueuePaneSliderScrub(Pane.Compare, TimeSpan.FromSeconds(ComparePanePositionSlider.Value));
            }
        }

        private void QueuePaneSliderScrub(Pane pane, TimeSpan target)
        {
            _pendingPaneSliderScrubTarget = target;
            _pendingPaneSliderScrubPane = pane;
            _hasPendingPaneSliderScrubTarget = true;
            if (!_isPaneSliderScrubSeekInFlight)
            {
                _paneSliderScrubTimer.Start();
            }
        }

        private void CancelQueuedSliderScrubs()
        {
            _hasPendingSliderScrubTarget = false;
            _hasPendingPaneSliderScrubTarget = false;
            _sliderScrubTimer.Stop();
            _paneSliderScrubTimer.Stop();
            _sliderScrubCts?.Cancel();
            _sliderScrubCts?.Dispose();
            _sliderScrubCts = null;
            _paneSliderScrubCts?.Cancel();
            _paneSliderScrubCts?.Dispose();
            _paneSliderScrubCts = null;
        }

        private async void PaneSliderScrubTimer_Tick(object? sender, EventArgs e)
        {
            _paneSliderScrubTimer.Stop();
            if (!_hasPendingPaneSliderScrubTarget)
            {
                return;
            }

            _hasPendingPaneSliderScrubTarget = false;
            _isPaneSliderScrubSeekInFlight = true;
            var pane = _pendingPaneSliderScrubPane;
            var target = _pendingPaneSliderScrubTarget;
            _paneSliderScrubCts?.Dispose();
            _paneSliderScrubCts = new CancellationTokenSource();
            try
            {
                var engine = TryGetExistingEngine(pane);
                if (engine != null && engine.IsMediaOpen)
                {
                    await SeekToTimePreservingPlaybackAsync(engine, target, _paneSliderScrubCts.Token);
                }
            }
            catch (OperationCanceledException)
            {
                // Ignored
            }
            finally
            {
                _isPaneSliderScrubSeekInFlight = false;
                if (_hasPendingPaneSliderScrubTarget)
                {
                    _paneSliderScrubTimer.Start();
                }
            }
        }

        private static async Task SeekToTimePreservingPlaybackAsync(IVideoReviewEngine engine, TimeSpan target, CancellationToken cancellationToken = default)
        {
            var resumePlayback = engine.IsPlaying;
            await engine.SeekToTimeAsync(target, cancellationToken);
            if (resumePlayback && CanStartPlayback(engine))
            {
                await engine.PlayAsync();
            }
        }

        private async Task SeekMasterTimelineAsync(TimeSpan target, CancellationToken cancellationToken = default)
        {
            if (IsAllPaneTransportEnabled)
            {
                await SeekAllPaneToTimePreservingPlaybackAsync(target, cancellationToken);
                return;
            }

            await SeekToTimePreservingPlaybackAsync(_primaryEngine, target, cancellationToken);
        }

        private async Task SeekAllPaneRelativePreservingPlaybackAsync(TimeSpan offset)
        {
            var compareEngine = _compareEngine;
            if (!_primaryEngine.IsMediaOpen || compareEngine == null || !compareEngine.IsMediaOpen)
            {
                return;
            }

            var primaryTarget = ClampSeekTarget(_primaryEngine.Position.PresentationTime + offset);
            var compareTarget = ClampSeekTarget(compareEngine.Position.PresentationTime + offset);
            await SeekAllPaneToTimesPreservingPlaybackAsync(primaryTarget, compareTarget);
        }

        private async Task SeekAllPaneToTimePreservingPlaybackAsync(TimeSpan target, CancellationToken cancellationToken = default)
        {
            await SeekAllPaneToTimesPreservingPlaybackAsync(target, target, cancellationToken);
        }

        private async Task SeekAllPaneToTimesPreservingPlaybackAsync(TimeSpan primaryTarget, TimeSpan compareTarget, CancellationToken cancellationToken = default)
        {
            var compareEngine = _compareEngine;
            if (!_primaryEngine.IsMediaOpen || compareEngine == null || !compareEngine.IsMediaOpen)
            {
                return;
            }

            var resumePrimaryPlayback = _primaryEngine.IsPlaying;
            var resumeComparePlayback = compareEngine.IsPlaying;
            if (resumePrimaryPlayback || resumeComparePlayback)
            {
                await PauseAllPanePlaybackAsync();
            }

            await Task.WhenAll(
                Task.Run(() => _primaryEngine.SeekToTimeAsync(primaryTarget, cancellationToken), cancellationToken),
                Task.Run(() => compareEngine.SeekToTimeAsync(compareTarget, cancellationToken), cancellationToken));

            var resumeTasks = new List<Task>(2);
            if (resumePrimaryPlayback && CanStartPlayback(_primaryEngine))
            {
                resumeTasks.Add(Task.Run(() => _primaryEngine.PlayAsync(), cancellationToken));
            }

            if (resumeComparePlayback && CanStartPlayback(compareEngine))
            {
                resumeTasks.Add(Task.Run(() => compareEngine.PlayAsync(), cancellationToken));
            }

            if (resumeTasks.Count > 0)
            {
                await Task.WhenAll(resumeTasks);
            }

            UpdateCommandStates();
        }

        private static TimeSpan ClampSeekTarget(TimeSpan target)
        {
            return target < TimeSpan.Zero ? TimeSpan.Zero : target;
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

            CancelQueuedSliderScrubs();
            if (textBox == FrameNumberTextBox && IsAllPaneTransportEnabled)
            {
                await SeekAllPaneToFramePreservingPlaybackAsync(oneBasedFrame - 1);
                return;
            }

            var pane = textBox == ComparePaneFrameNumberTextBox ? Pane.Compare : Pane.Primary;
            SelectPane(pane);
            await GetEngine(pane).SeekToFrameAsync(oneBasedFrame - 1);
        }

        private static void FrameNumberTextBox_GotFocus(object? sender, RoutedEventArgs e)
        {
            if (sender is TextBox textBox && !string.IsNullOrEmpty(textBox.Text))
            {
                textBox.SelectAll();
            }
        }

        private async Task SeekAllPaneToFramePreservingPlaybackAsync(long frameIndex)
        {
            var compareEngine = _compareEngine;
            if (!_primaryEngine.IsMediaOpen || compareEngine == null || !compareEngine.IsMediaOpen)
            {
                return;
            }

            var targetFrameIndex = Math.Max(0L, frameIndex);
            var resumePrimaryPlayback = _primaryEngine.IsPlaying;
            var resumeComparePlayback = compareEngine.IsPlaying;
            if (resumePrimaryPlayback || resumeComparePlayback)
            {
                await PauseAllPanePlaybackAsync();
            }

            await Task.WhenAll(
                Task.Run(() => _primaryEngine.SeekToFrameAsync(targetFrameIndex)),
                Task.Run(() => compareEngine.SeekToFrameAsync(targetFrameIndex)));

            var resumeTasks = new List<Task>(2);
            if (resumePrimaryPlayback && CanStartPlayback(_primaryEngine))
            {
                resumeTasks.Add(Task.Run(() => _primaryEngine.PlayAsync()));
            }

            if (resumeComparePlayback && CanStartPlayback(compareEngine))
            {
                resumeTasks.Add(Task.Run(() => compareEngine.PlayAsync()));
            }

            if (resumeTasks.Count > 0)
            {
                await Task.WhenAll(resumeTasks);
            }

            UpdateCommandStates();
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

            CancelQueuedSliderScrubs();
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

            LoopPlaybackMenuItem.IsChecked = isEnabled;
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
            VideoPaneGrid.ColumnSpacing = 12;
            SetPrimaryPaneLocalChromeVisible(true);
            ComparePaneBorder.IsVisible = true;
            ComparePaneFooterBorder.IsVisible = true;
            CompareToolbarBorder.IsVisible = true;
            UpdatePaneSelectionVisuals();
            UpdateCompareOptionState();
            UpdateCacheStatusFromEngine();
        }

        private void HideCompareMode()
        {
            _focusedPane = Pane.Primary;
            VideoPaneGrid.ColumnDefinitions[1].Width = new GridLength(0);
            VideoPaneGrid.ColumnSpacing = 0;
            SetPrimaryPaneLocalChromeVisible(false);
            ComparePaneBorder.IsVisible = false;
            ComparePaneFooterBorder.IsVisible = false;
            CompareToolbarBorder.IsVisible = false;
            UpdatePaneSelectionVisuals();
            UpdateCompareOptionState();
            UpdateCacheStatusFromEngine();
            _ = PauseHiddenComparePlaybackAsync();
        }

        private async Task PauseHiddenComparePlaybackAsync()
        {
            var compareEngine = _compareEngine;
            if (compareEngine == null ||
                !compareEngine.IsMediaOpen ||
                !compareEngine.IsPlaying)
            {
                return;
            }

            try
            {
                if (compareEngine.IsMediaOpen && compareEngine.IsPlaying)
                {
                    await compareEngine.PauseAsync();
                }
            }
            catch (Exception ex)
            {
                Trace.TraceWarning("Compare playback pause failed: " + ex.Message);
                CacheStatusTextBlock.Text = "Compare playback pause failed: " + ex.Message;
            }
        }

        private void SetPrimaryPaneLocalChromeVisible(bool isVisible)
        {
            PrimaryPaneLayoutGrid.RowDefinitions[0].Height = new GridLength(isVisible ? PaneHeaderHeight : 0, GridUnitType.Pixel);
            PrimaryPaneLayoutGrid.RowDefinitions[2].Height = new GridLength(isVisible ? PaneFooterHeight : 0, GridUnitType.Pixel);
            PrimaryPaneHeaderBorder.IsVisible = isVisible;
            PrimaryPaneFooterBorder.IsVisible = isVisible;
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
            UpdateCommandStates();
        }

        private void UpdateCommandStates()
        {
            var focusedPane = GetFocusedPane();
            var focusedEngine = TryGetExistingEngine(focusedPane);
            var focusedPaneLoaded = focusedEngine != null && focusedEngine.IsMediaOpen;
            var primaryLoaded = _primaryEngine.IsMediaOpen;
            var compareLoaded = _compareEngine != null && _compareEngine.IsMediaOpen;
            var anyMediaLoaded = primaryLoaded || compareLoaded;
            var focusedPaneCanZoom = focusedPaneLoaded && focusedEngine != null && !focusedEngine.IsPlaying;
            var focusedPaneCanTogglePlayback = CanTogglePlayback(focusedEngine);
            var mainPlayPauseCanToggle = IsAllPaneTransportEnabled
                ? CanToggleAllPanePlayback()
                : focusedPaneCanTogglePlayback;
            var focusedLoopRange = GetLoopRange(focusedPane);
            var canClearLoopPoints = focusedLoopRange != null && focusedLoopRange.HasAnyMarkers;
            var canReplaceAudioTrack = CanReplaceAudioTrack(out _);
            var canExportLoopClip = CanExportLoopClip(focusedPane);
            var canExportSideBySideCompare = CanExportSideBySideCompare();
            var canZoomIn = focusedPaneCanZoom && GetPaneZoomFactor(focusedPane) < MaximumPaneZoomFactor - 0.0001d;
            var canZoomOut = focusedPaneCanZoom && GetPaneZoomFactor(focusedPane) > MinimumPaneZoomFactor + 0.0001d;

            SetNativeMenuEnabled(_nativePlayPauseMenuItem, mainPlayPauseCanToggle);
            SetNativeMenuEnabled(_nativeCloseVideoMenuItem, anyMediaLoaded);
            SetNativeMenuEnabled(_nativeVideoInfoMenuItem, focusedPaneLoaded);
            SetNativeMenuEnabled(_nativeRewindMenuItem, focusedPaneLoaded);
            SetNativeMenuEnabled(_nativeFastForwardMenuItem, focusedPaneLoaded);
            SetNativeMenuEnabled(_nativePreviousFrameMenuItem, focusedPaneLoaded);
            SetNativeMenuEnabled(_nativeNextFrameMenuItem, focusedPaneLoaded);
            SetNativeMenuEnabled(_nativeLoopPlaybackMenuItem, focusedPaneLoaded);
            SetNativeMenuEnabled(_nativeSetLoopInMenuItem, focusedPaneLoaded);
            SetNativeMenuEnabled(_nativeSetLoopOutMenuItem, focusedPaneLoaded);
            SetNativeMenuEnabled(_nativeClearLoopPointsMenuItem, canClearLoopPoints);
            SetNativeMenuEnabled(_nativeSaveLoopAsClipMenuItem, canExportLoopClip);
            SetNativeMenuEnabled(_nativeExportSideBySideCompareMenuItem, canExportSideBySideCompare);
            SetNativeMenuEnabled(_nativeZoomInMenuItem, canZoomIn);
            SetNativeMenuEnabled(_nativeZoomOutMenuItem, canZoomOut);
            SetNativeMenuEnabled(_nativeResetZoomMenuItem, canZoomOut);
            SetNativeMenuEnabled(_nativeReplaceAudioTrackMenuItem, canReplaceAudioTrack);

            SetControlEnabled(PlayPauseMenuItem, mainPlayPauseCanToggle);
            SetControlEnabled(CloseVideoMenuItem, anyMediaLoaded);
            SetControlEnabled(VideoInfoMenuItem, focusedPaneLoaded);
            SetControlEnabled(RewindMenuItem, focusedPaneLoaded);
            SetControlEnabled(FastForwardMenuItem, focusedPaneLoaded);
            SetControlEnabled(PreviousFrameMenuItem, focusedPaneLoaded);
            SetControlEnabled(NextFrameMenuItem, focusedPaneLoaded);
            SetControlEnabled(LoopPlaybackMenuItem, focusedPaneLoaded);
            SetControlEnabled(SetLoopInMenuItem, focusedPaneLoaded);
            SetControlEnabled(SetLoopOutMenuItem, focusedPaneLoaded);
            SetControlEnabled(ClearLoopPointsMenuItem, canClearLoopPoints);
            SetControlEnabled(SaveLoopAsClipMenuItem, canExportLoopClip);
            SetControlEnabled(ExportSideBySideCompareMenuItem, canExportSideBySideCompare);
            SetControlEnabled(ZoomInMenuItem, canZoomIn);
            SetControlEnabled(ZoomOutMenuItem, canZoomOut);
            SetControlEnabled(ResetZoomMenuItem, canZoomOut);
            SetControlEnabled(ReplaceAudioTrackMenuItem, canReplaceAudioTrack);

            SetControlEnabled(PositionSlider, primaryLoaded);
            SetControlEnabled(FrameNumberTextBox, primaryLoaded);
            SetControlEnabled(PreviousFrameButton, focusedPaneLoaded);
            SetControlEnabled(RewindButton, focusedPaneLoaded);
            SetControlEnabled(PlayPauseButton, mainPlayPauseCanToggle);
            SetControlEnabled(FastForwardButton, focusedPaneLoaded);
            SetControlEnabled(NextFrameButton, focusedPaneLoaded);
            SetControlEnabled(ToggleFullScreenButton, anyMediaLoaded);
            SetPaneTransportEnabled(Pane.Primary, primaryLoaded, CanTogglePlayback(_primaryEngine));
            SetPaneTransportEnabled(Pane.Compare, compareLoaded, CanTogglePlayback(_compareEngine));
            UpdateMainPlayPauseVisual();
            AlignRightToLeftButton.IsEnabled = primaryLoaded && compareLoaded;
            AlignLeftToRightButton.IsEnabled = primaryLoaded && compareLoaded;
            ToolTip.SetTip(
                AlignRightToLeftButton,
                primaryLoaded && compareLoaded
                    ? "Syncs the right pane to the left pane with exact frame identity when available."
                    : "Load both compare panes before syncing.");
            ToolTip.SetTip(
                AlignLeftToRightButton,
                primaryLoaded && compareLoaded
                    ? "Syncs the left pane to the right pane with exact frame identity when available."
                    : "Load both compare panes before syncing.");
            UpdatePlaybackAvailabilityToolTips(focusedEngine);
        }

        private static void SetNativeMenuEnabled(NativeMenuItem? item, bool isEnabled)
        {
            if (item != null)
            {
                item.IsEnabled = isEnabled;
            }
        }

        private static void SetControlEnabled(Control control, bool isEnabled)
        {
            control.IsEnabled = isEnabled;
        }

        private void SetPaneTransportEnabled(Pane pane, bool isEnabled, bool canTogglePlayback)
        {
            if (pane == Pane.Compare)
            {
                SetControlEnabled(ComparePanePositionSlider, isEnabled);
                SetControlEnabled(ComparePaneFrameNumberTextBox, isEnabled);
                SetControlEnabled(ComparePaneStepBackButton, isEnabled);
                SetControlEnabled(ComparePaneSkipBackHundredFramesButton, isEnabled);
                SetControlEnabled(ComparePanePlayPauseButton, canTogglePlayback);
                SetControlEnabled(ComparePaneSkipForwardHundredFramesButton, isEnabled);
                SetControlEnabled(ComparePaneStepForwardButton, isEnabled);
                return;
            }

            SetControlEnabled(PrimaryPanePositionSlider, isEnabled);
            SetControlEnabled(PrimaryPaneFrameNumberTextBox, isEnabled);
            SetControlEnabled(PrimaryPaneStepBackButton, isEnabled);
            SetControlEnabled(PrimaryPaneSkipBackHundredFramesButton, isEnabled);
            SetControlEnabled(PrimaryPanePlayPauseButton, canTogglePlayback);
            SetControlEnabled(PrimaryPaneSkipForwardHundredFramesButton, isEnabled);
            SetControlEnabled(PrimaryPaneStepForwardButton, isEnabled);
        }

        private static bool CanStartPlayback(IVideoReviewEngine? engine)
        {
            return engine != null &&
                engine.IsMediaOpen;
        }

        private static bool CanTogglePlayback(IVideoReviewEngine? engine)
        {
            return engine != null &&
                engine.IsMediaOpen &&
                (engine.IsPlaying || CanStartPlayback(engine));
        }

        private bool CanStartAllPanePlayback()
        {
            return _primaryEngine.IsMediaOpen &&
                _compareEngine != null &&
                _compareEngine.IsMediaOpen &&
                CanStartPlayback(_primaryEngine) &&
                CanStartPlayback(_compareEngine);
        }

        private bool ShouldPauseAllPanePlayback()
        {
            return ShouldPauseAllPanePlayback(_primaryEngine, _compareEngine);
        }

        private static bool ShouldPauseAllPanePlayback(IVideoReviewEngine primaryEngine, IVideoReviewEngine? compareEngine)
        {
            return primaryEngine.IsMediaOpen &&
                compareEngine != null &&
                compareEngine.IsMediaOpen &&
                primaryEngine.IsPlaying &&
                compareEngine.IsPlaying;
        }

        private bool CanToggleAllPanePlayback()
        {
            return _primaryEngine.IsMediaOpen &&
                _compareEngine != null &&
                _compareEngine.IsMediaOpen &&
                (_primaryEngine.IsPlaying || _compareEngine.IsPlaying || CanStartAllPanePlayback());
        }

        private void UpdatePlaybackAvailabilityToolTips(IVideoReviewEngine? focusedEngine)
        {
            _ = focusedEngine;
            var mainPlaybackAction = ShouldShowMainPauseAction() ? "Pause" : "Play";
            ToolTip.SetTip(PlayPauseButton, mainPlaybackAction);
            ToolTip.SetTip(PlayPauseMenuItem, mainPlaybackAction);
            ToolTip.SetTip(PrimaryPanePlayPauseButton, "Play");
            ToolTip.SetTip(ComparePanePlayPauseButton, "Play");
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

            CancelQueuedSliderScrubs();
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

            CancelQueuedSliderScrubs();
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
            RestartLoopPlaybackIfNeeded(Pane.Primary, e);
            Dispatcher.UIThread.Post(() =>
            {
                ApplyState(Pane.Primary, e);
                UpdateCacheStatusFromEngine();
            });
        }

        private void CompareEngine_StateChanged(object? sender, VideoReviewEngineStateChangedEventArgs e)
        {
            RestartLoopPlaybackIfNeeded(Pane.Compare, e);
            Dispatcher.UIThread.Post(() =>
            {
                ApplyState(Pane.Compare, e);
                UpdateCacheStatusFromEngine();
            });
        }

        private void PrimaryEngine_FramePresented(object? sender, FramePresentedEventArgs e)
        {
            QueueFramePresentation(Pane.Primary, e.FrameBuffer);
        }

        private void CompareEngine_FramePresented(object? sender, FramePresentedEventArgs e)
        {
            QueueFramePresentation(Pane.Compare, e.FrameBuffer);
        }

        private void QueueFramePresentation(Pane pane, DecodedFrameBuffer sourceFrameBuffer)
        {
            var retainedFrameBuffer = sourceFrameBuffer.Retain();
            var shouldPost = pane == Pane.Compare
                ? StorePendingCompareFrame(retainedFrameBuffer)
                : StorePendingPrimaryFrame(retainedFrameBuffer);
            if (!shouldPost)
            {
                return;
            }

            Dispatcher.UIThread.Post(() => PresentPendingFrame(pane));
        }

        private bool StorePendingPrimaryFrame(DecodedFrameBuffer retainedFrameBuffer)
        {
            DecodedFrameBuffer? replacedFrameBuffer;
            var shouldPost = false;
            lock (_primaryFramePresentationLock)
            {
                replacedFrameBuffer = _pendingPrimaryFrameBuffer;
                _pendingPrimaryFrameBuffer = retainedFrameBuffer;
                if (!_primaryFramePresentationQueued)
                {
                    _primaryFramePresentationQueued = true;
                    shouldPost = true;
                }
            }

            replacedFrameBuffer?.Dispose();
            return shouldPost;
        }

        private bool StorePendingCompareFrame(DecodedFrameBuffer retainedFrameBuffer)
        {
            DecodedFrameBuffer? replacedFrameBuffer;
            var shouldPost = false;
            lock (_compareFramePresentationLock)
            {
                replacedFrameBuffer = _pendingCompareFrameBuffer;
                _pendingCompareFrameBuffer = retainedFrameBuffer;
                if (!_compareFramePresentationQueued)
                {
                    _compareFramePresentationQueued = true;
                    shouldPost = true;
                }
            }

            replacedFrameBuffer?.Dispose();
            return shouldPost;
        }

        private void PresentPendingFrame(Pane pane)
        {
            var frameBuffer = TakePendingFramePresentation(pane);
            if (frameBuffer == null)
            {
                return;
            }

            try
            {
                if (pane == Pane.Compare && !IsCompareModeEnabled)
                {
                    return;
                }

                SetPaneBitmap(pane, frameBuffer);
            }
            catch (Exception ex)
            {
                CacheStatusTextBlock.Text = pane == Pane.Compare
                    ? "Compare frame presentation failed: " + ex.Message
                    : "Frame presentation failed: " + ex.Message;
            }
            finally
            {
                frameBuffer.Dispose();
            }
        }

        private DecodedFrameBuffer? TakePendingFramePresentation(Pane pane)
        {
            var gate = pane == Pane.Compare ? _compareFramePresentationLock : _primaryFramePresentationLock;
            lock (gate)
            {
                if (pane == Pane.Compare)
                {
                    var frameBuffer = _pendingCompareFrameBuffer;
                    _pendingCompareFrameBuffer = null;
                    _compareFramePresentationQueued = false;
                    return frameBuffer;
                }

                var primaryFrameBuffer = _pendingPrimaryFrameBuffer;
                _pendingPrimaryFrameBuffer = null;
                _primaryFramePresentationQueued = false;
                return primaryFrameBuffer;
            }
        }

        private void ClearPendingFramePresentations()
        {
            DecodedFrameBuffer? primaryFrameBuffer;
            lock (_primaryFramePresentationLock)
            {
                primaryFrameBuffer = _pendingPrimaryFrameBuffer;
                _pendingPrimaryFrameBuffer = null;
                _primaryFramePresentationQueued = false;
            }

            DecodedFrameBuffer? compareFrameBuffer;
            lock (_compareFramePresentationLock)
            {
                compareFrameBuffer = _pendingCompareFrameBuffer;
                _pendingCompareFrameBuffer = null;
                _compareFramePresentationQueued = false;
            }

            primaryFrameBuffer?.Dispose();
            compareFrameBuffer?.Dispose();
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
            TimecodeTextBlock.Text = positionText + " / " + durationText;
            FrameNumberTextBox.Text = FormatFrameNumberEntry(state.Position);
            PrimaryPaneFrameNumberTextBox.Text = FrameNumberTextBox.Text;
            PrimaryPanePlayPausePlayIcon.IsVisible = !state.IsPlaying;
            PrimaryPanePlayPausePauseIcon.IsVisible = state.IsPlaying;
            UpdateMainPlayPauseVisual();
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
            UpdateMainPlayPauseVisual();
        }

        private void UpdateMainPlayPauseVisual()
        {
            var shouldShowPauseAction = ShouldShowMainPauseAction();
            PlayPausePlayIcon.IsVisible = !shouldShowPauseAction;
            PlayPausePauseIcon.IsVisible = shouldShowPauseAction;
            if (_nativePlayPauseMenuItem != null)
            {
                _nativePlayPauseMenuItem.Header = shouldShowPauseAction ? "Pause" : "Play";
            }

            PlayPauseMenuItem.Header = shouldShowPauseAction ? "Pause" : "Play";
        }

        private bool ShouldShowMainPauseAction()
        {
            if (IsAllPaneTransportEnabled)
            {
                return ShouldPauseAllPanePlayback();
            }

            var focusedEngine = TryGetExistingEngine(GetFocusedPane());
            return focusedEngine != null && focusedEngine.IsPlaying;
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

        private void RestartLoopPlaybackIfNeeded(Pane pane, VideoReviewEngineStateChangedEventArgs state)
        {
            if (!_isLoopPlaybackEnabled ||
                _isLoopRestartInFlight ||
                state == null ||
                !state.IsPlaying ||
                !state.IsMediaOpen)
            {
                return;
            }

            var range = GetLoopRange(pane);
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

            var engine = TryGetExistingEngine(pane);
            if (engine == null)
            {
                return;
            }

            var restartRange = range ?? CreateLoopRange(pane, null, null);
            _isLoopRestartInFlight = true;
            _ = Task.Run(() => RestartLoopPlaybackAsync(engine, restartRange));
        }

        private async Task RestartLoopPlaybackAsync(IVideoReviewEngine engine, LoopPlaybackPaneRangeSnapshot range)
        {
            try
            {
                var restartTime = range != null && range.HasLoopIn ? range.EffectiveStartTime : TimeSpan.Zero;
                await engine.PauseAsync();
                await engine.SeekToTimeAsync(restartTime);
                await engine.PlayAsync();
                await SetStatusMessageAsync(range != null && range.HasLoopIn
                    ? "Loop playback restarted from loop-in."
                    : "Loop playback restarted from start.").ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                Trace.TraceWarning("Loop playback restart failed: " + ex.Message);
                await SetStatusMessageAsync("Loop playback restart failed: " + ex.Message).ConfigureAwait(false);
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

            UpdateCommandStates();
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
            var recentFiles = _recentFilesService.Load().ToList();
            if (_nativeRecentFilesMenuItem?.Menu != null)
            {
                _nativeRecentFilesMenuItem.Menu.Items.Clear();
            }

            RecentFilesMenuItem.Items.Clear();
            if (recentFiles.Count == 0)
            {
                if (_nativeRecentFilesMenuItem?.Menu != null)
                {
                    _nativeRecentFilesMenuItem.Menu.Items.Add(new NativeMenuItem("No Recent Files")
                    {
                        IsEnabled = false
                    });
                }

                RecentFilesMenuItem.Items.Add(new MenuItem
                {
                    Header = "No Recent Files",
                    IsEnabled = false
                });
                return;
            }

            foreach (var filePath in recentFiles)
            {
                if (_nativeRecentFilesMenuItem?.Menu != null)
                {
                    var nativeMenuItem = new NativeMenuItem(Path.GetFileName(filePath));
                    nativeMenuItem.Click += async (_, _) => await OpenRecentPathAsync(filePath);
                    _nativeRecentFilesMenuItem.Menu.Items.Add(nativeMenuItem);
                }

                var menuItem = new MenuItem
                {
                    Header = Path.GetFileName(filePath)
                };
                menuItem.Click += async (_, _) => await OpenRecentPathAsync(filePath);
                RecentFilesMenuItem.Items.Add(menuItem);
            }
        }

        private async void Window_KeyDown(object? sender, KeyEventArgs e)
        {
            if (await TryHandleWindowCommandShortcutAsync(sender, e))
            {
                e.Handled = true;
                return;
            }

            if (IsTextEntryTarget(e))
            {
                return;
            }

            if (await TryHandleReviewShortcutAsync(e))
            {
                e.Handled = true;
            }
        }

        private async Task<bool> TryHandleWindowCommandShortcutAsync(object? sender, KeyEventArgs e)
        {
            if (e.Key == Key.N && HasExactModifiers(e, CommandKeyModifier))
            {
                LaunchNewWindow();
                return true;
            }

            if (e.Key == Key.O && HasExactModifiers(e, CommandKeyModifier))
            {
                await OpenVideoAsync(GetFileOpenTargetPane());
                return true;
            }

            if (e.Key == Key.W && HasExactModifiers(e, CommandKeyModifier))
            {
                await CloseVideosAsync();
                return true;
            }

            if (e.Key == Key.E && HasExactModifiers(e, CommandShiftKeyModifier))
            {
                await ExportDiagnosticsAsync(null);
                return true;
            }

            if (e.Key == Key.F1)
            {
                HelpMenuItem_Click(sender, e);
                return true;
            }

            if (ShouldToggleFullScreen(e))
            {
                ToggleFullScreen();
                return true;
            }

            return false;
        }

        private async Task<bool> TryHandleReviewShortcutAsync(KeyEventArgs e)
        {
            if (e.Key == Key.Space)
            {
                await TogglePlaybackAsync();
                return true;
            }

            if (e.Key == Key.Left)
            {
                await StepFrameAsync(-ResolveFrameStepCount(e.KeyModifiers));
                return true;
            }

            if (e.Key == Key.Right)
            {
                await StepFrameAsync(ResolveFrameStepCount(e.KeyModifiers));
                return true;
            }

            if (e.Key == Key.L)
            {
                SetLoopPlaybackEnabled(!_isLoopPlaybackEnabled);
                return true;
            }

            if (e.Key == Key.OemOpenBrackets)
            {
                SetLoopMarker(LoopPlaybackMarkerEndpoint.In);
                return true;
            }

            if (e.Key == Key.OemCloseBrackets)
            {
                SetLoopMarker(LoopPlaybackMarkerEndpoint.Out);
                return true;
            }

            if (e.Key == Key.J || e.Key == Key.OemComma)
            {
                await SeekRelativeAsync(TimeSpan.FromSeconds(-5));
                return true;
            }

            if (e.Key == Key.OemPeriod)
            {
                await SeekRelativeAsync(TimeSpan.FromSeconds(5));
                return true;
            }

            if (TryHandleZoomShortcut(e))
            {
                return true;
            }

            return false;
        }

        private static bool HasExactModifiers(KeyEventArgs e, KeyModifiers modifiers)
        {
            return e.KeyModifiers == modifiers;
        }

        private bool ShouldToggleFullScreen(KeyEventArgs e)
        {
            return e.Key == Key.F11 ||
                (e.Key == Key.Enter && HasExactModifiers(e, KeyModifiers.Alt)) ||
                (e.Key == Key.Escape && WindowState == WindowState.FullScreen);
        }

        private static bool IsTextEntryTarget(KeyEventArgs e)
        {
            return e.Source is TextBox;
        }

        private static int ResolveFrameStepCount(KeyModifiers modifiers)
        {
            if (modifiers == KeyModifiers.Control)
            {
                return ControlModifiedFrameStep;
            }

            if (modifiers == KeyModifiers.Shift)
            {
                return HundredFrameStep;
            }

            return 1;
        }

        private bool TryHandleZoomShortcut(KeyEventArgs e)
        {
            if ((e.KeyModifiers == KeyModifiers.None || e.KeyModifiers == KeyModifiers.Shift) &&
                (e.Key == Key.OemPlus || e.Key == Key.Add))
            {
                ZoomInFocusedPane();
                return true;
            }

            if ((e.KeyModifiers == KeyModifiers.None || e.KeyModifiers == KeyModifiers.Shift) &&
                (e.Key == Key.OemMinus || e.Key == Key.Subtract))
            {
                ZoomOutFocusedPane();
                return true;
            }

            if (e.KeyModifiers == KeyModifiers.None &&
                (e.Key == Key.D0 || e.Key == Key.NumPad0))
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
            WindowStateButton.Content = WindowState == WindowState.Maximized ? "\uE923" : "\uE922";
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

        private void NewWindowMenuItem_Click(object? sender, RoutedEventArgs e)
        {
            LaunchNewWindow();
        }

        private void ExitMenuItem_Click(object? sender, RoutedEventArgs e)
        {
            Close();
        }

        private void ToggleFullScreenMenuItem_Click(object? sender, RoutedEventArgs e)
        {
            ToggleFullScreen();
        }

        private void LoopPlaybackMenuItem_Click(object? sender, RoutedEventArgs e)
        {
            SetLoopPlaybackEnabled(!_isLoopPlaybackEnabled);
        }

        private void SetLoopInMenuItem_Click(object? sender, RoutedEventArgs e)
        {
            SetLoopMarker(LoopPlaybackMarkerEndpoint.In);
        }

        private void SetLoopOutMenuItem_Click(object? sender, RoutedEventArgs e)
        {
            SetLoopMarker(LoopPlaybackMarkerEndpoint.Out);
        }

        private void ClearLoopPointsMenuItem_Click(object? sender, RoutedEventArgs e)
        {
            ClearLoopPoints();
        }

        private void ZoomInMenuItem_Click(object? sender, RoutedEventArgs e)
        {
            ZoomInFocusedPane();
        }

        private void ZoomOutMenuItem_Click(object? sender, RoutedEventArgs e)
        {
            ZoomOutFocusedPane();
        }

        private void ResetZoomMenuItem_Click(object? sender, RoutedEventArgs e)
        {
            ResetZoomForFocusedPane();
        }

        private void UseGpuAccelerationMenuItem_Click(object? sender, RoutedEventArgs e)
        {
            SetGpuAccelerationPreference(!_optionsProvider.UseGpuAcceleration);
        }

        private void VideoInfoMenuItem_Click(object? sender, RoutedEventArgs e)
        {
            ShowVideoInfo(GetFocusedPane());
        }

        private void ShowVideoInfo(Pane pane)
        {
            var engine = TryGetExistingEngine(pane);
            if (engine == null || string.IsNullOrWhiteSpace(engine.MediaInfo.FilePath))
            {
                ShowTextDialog("Video Info", "Video info is unavailable until media is loaded in the selected pane.");
                return;
            }

            var info = engine.MediaInfo;
            var snapshot = BuildVideoInfoSnapshot(ResolvePaneLabel(pane), info, engine.Position);
            var infoWindow = new VideoInfoWindow(snapshot);
            infoWindow.Show(this);
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
            if (!CompareSideBySideExportService.IsBundledRuntimeAvailable)
            {
                CacheStatusTextBlock.Text = CompareSideBySideExportService.GetRuntimeAvailabilityMessage();
                return null;
            }

            var selection = await PromptForCompareSideBySideExportOptionsAsync();
            if (selection == null)
            {
                return null;
            }

            return await ExportSideBySideCompareAsync(null, selection.Mode, selection.AudioSource);
        }

        private async void ReplaceAudioTrackMenuItem_Click(object? sender, RoutedEventArgs e)
        {
            await ReplaceAudioTrackAsync();
        }

        private async Task<ClipExportResult?> ExportLoopClipAsync(string? outputPath, string? paneId)
        {
            if (!ClipExportService.IsBundledRuntimeAvailable)
            {
                CacheStatusTextBlock.Text = ClipExportService.GetRuntimeAvailabilityMessage();
                return null;
            }

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
            if (!CompareSideBySideExportService.IsBundledRuntimeAvailable)
            {
                CacheStatusTextBlock.Text = CompareSideBySideExportService.GetRuntimeAvailabilityMessage();
                return null;
            }

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

        private async Task<CompareSideBySideExportDialogSelection?> PromptForCompareSideBySideExportOptionsAsync()
        {
            if (CompareModeCheckBox.IsChecked != true ||
                !_primaryEngine.IsMediaOpen ||
                _compareEngine == null ||
                !_compareEngine.IsMediaOpen)
            {
                CacheStatusTextBlock.Text = "Load both compare panes before exporting side-by-side video.";
                return null;
            }

            var loopModeAvailable = IsCompareLoopModeAvailable();
            var initialMode = loopModeAvailable
                ? CompareSideBySideExportMode.Loop
                : CompareSideBySideExportMode.WholeVideo;
            var primaryHasAudio = _primaryEngine.MediaInfo.HasAudioStream;
            var compareHasAudio = _compareEngine.MediaInfo.HasAudioStream;

            var dialog = new Window
            {
                Title = "Export Side-by-Side Compare",
                Width = 560,
                Height = 420,
                MinWidth = 480,
                MinHeight = 360,
                Background = Brush.Parse("#101418"),
                WindowStartupLocation = WindowStartupLocation.CenterOwner
            };

            var loopModeRadio = new RadioButton
            {
                Content = "Loop",
                GroupName = "CompareExportMode",
                IsEnabled = loopModeAvailable,
                IsChecked = initialMode == CompareSideBySideExportMode.Loop
            };
            var wholeVideoModeRadio = new RadioButton
            {
                Content = "Whole Video",
                GroupName = "CompareExportMode",
                IsChecked = initialMode == CompareSideBySideExportMode.WholeVideo
            };
            var primaryAudioRadio = new RadioButton
            {
                Content = BuildCompareExportAudioOptionLabel(Pane.Primary, primaryHasAudio),
                GroupName = "CompareExportAudio",
                IsChecked = true
            };
            var compareAudioRadio = new RadioButton
            {
                Content = BuildCompareExportAudioOptionLabel(Pane.Compare, compareHasAudio),
                GroupName = "CompareExportAudio"
            };

            var cancelButton = new Button
            {
                Content = "Cancel",
                MinWidth = 88
            };
            cancelButton.Click += (_, _) => dialog.Close(null);

            var continueButton = new Button
            {
                Content = "Continue",
                MinWidth = 96
            };
            continueButton.Click += (_, _) =>
            {
                var mode = loopModeRadio.IsChecked == true
                    ? CompareSideBySideExportMode.Loop
                    : CompareSideBySideExportMode.WholeVideo;
                var audioSource = compareAudioRadio.IsChecked == true
                    ? CompareSideBySideExportAudioSource.Compare
                    : CompareSideBySideExportAudioSource.Primary;
                dialog.Close(new CompareSideBySideExportDialogSelection(mode, audioSource));
            };

            var summaryGrid = new Grid
            {
                ColumnDefinitions = new ColumnDefinitions("*,*"),
                Margin = new Thickness(0, 12, 0, 0)
            };
            var primarySummary = BuildCompareExportPaneSummary(Pane.Primary, _primaryEngine);
            var compareSummary = BuildCompareExportPaneSummary(Pane.Compare, _compareEngine);
            Grid.SetColumn(compareSummary, 1);
            summaryGrid.Children.Add(primarySummary);
            summaryGrid.Children.Add(compareSummary);

            var choicesGrid = new Grid
            {
                ColumnDefinitions = new ColumnDefinitions("*,*"),
                Margin = new Thickness(0, 16, 0, 0)
            };
            var modePanel = new StackPanel
            {
                Spacing = 8,
                Children =
                {
                    new TextBlock { Text = "Export Mode", FontWeight = FontWeight.SemiBold },
                    loopModeRadio,
                    wholeVideoModeRadio,
                    new TextBlock
                    {
                        Text = loopModeAvailable
                            ? "Loop uses each pane's A/B range. Whole Video exports the aligned sources."
                            : "Loop export requires valid A/B ranges on both panes.",
                        Foreground = Brush.Parse(CompareExportSecondaryTextColor),
                        TextWrapping = TextWrapping.Wrap
                    }
                }
            };
            var audioPanel = new StackPanel
            {
                Spacing = 8,
                Children =
                {
                    new TextBlock { Text = "Audio Source", FontWeight = FontWeight.SemiBold },
                    primaryAudioRadio,
                    compareAudioRadio,
                    new TextBlock
                    {
                        Text = "If the selected pane has no audio stream, the side-by-side export will be silent.",
                        Foreground = Brush.Parse(CompareExportSecondaryTextColor),
                        TextWrapping = TextWrapping.Wrap
                    }
                }
            };
            Grid.SetColumn(audioPanel, 1);
            choicesGrid.Children.Add(modePanel);
            choicesGrid.Children.Add(audioPanel);

            var buttons = new StackPanel
            {
                Orientation = global::Avalonia.Layout.Orientation.Horizontal,
                HorizontalAlignment = global::Avalonia.Layout.HorizontalAlignment.Right,
                Spacing = 8,
                Children =
                {
                    cancelButton,
                    continueButton
                }
            };

            var layout = new Grid
            {
                RowDefinitions = new RowDefinitions("Auto,Auto,*,Auto")
            };
            layout.Children.Add(new StackPanel
            {
                Children =
                {
                    new TextBlock
                    {
                        Text = "Export Side-by-Side Compare",
                        FontSize = 20,
                        FontWeight = FontWeight.SemiBold
                    },
                    new TextBlock
                    {
                        Text = "Choose the compare export range and which pane supplies audio.",
                        Foreground = Brush.Parse(CompareExportSecondaryTextColor),
                        TextWrapping = TextWrapping.Wrap,
                        Margin = new Thickness(0, 6, 0, 0)
                    }
                }
            });
            Grid.SetRow(summaryGrid, 1);
            layout.Children.Add(summaryGrid);
            Grid.SetRow(choicesGrid, 2);
            layout.Children.Add(choicesGrid);
            Grid.SetRow(buttons, 3);
            layout.Children.Add(buttons);

            dialog.Content = new Border
            {
                Padding = new Thickness(18),
                Child = layout
            };

            return await dialog.ShowDialog<CompareSideBySideExportDialogSelection?>(this);
        }

        private bool IsCompareLoopModeAvailable()
        {
            return _primaryLoopRange.HasLoopIn &&
                   _primaryLoopRange.HasLoopOut &&
                   _compareLoopRange.HasLoopIn &&
                   _compareLoopRange.HasLoopOut &&
                   !_primaryLoopRange.HasPendingMarkers &&
                   !_compareLoopRange.HasPendingMarkers &&
                   !_primaryLoopRange.IsInvalidRange &&
                   !_compareLoopRange.IsInvalidRange;
        }

        private static string BuildCompareExportAudioOptionLabel(Pane pane, bool hasAudio)
        {
            return ResolvePaneLabel(pane) + (hasAudio ? " pane" : " pane (silent)");
        }

        private static StackPanel BuildCompareExportPaneSummary(Pane pane, IVideoReviewEngine engine)
        {
            var mediaInfo = engine.MediaInfo;
            return new StackPanel
            {
                Margin = pane == Pane.Primary ? new Thickness(0, 0, 10, 0) : new Thickness(10, 0, 0, 0),
                Children =
                {
                    new TextBlock
                    {
                        Text = ResolvePaneLabel(pane),
                        FontWeight = FontWeight.SemiBold
                    },
                    new TextBlock
                    {
                        Text = Path.GetFileName(engine.CurrentFilePath),
                        Foreground = Brush.Parse("#F3F4F6"),
                        TextWrapping = TextWrapping.Wrap,
                        Margin = new Thickness(0, 4, 0, 0)
                    },
                    new TextBlock
                    {
                        Text = FormatCompareExportMediaSummary(mediaInfo),
                        Foreground = Brush.Parse(CompareExportSecondaryTextColor),
                        TextWrapping = TextWrapping.Wrap,
                        Margin = new Thickness(0, 4, 0, 0)
                    }
                }
            };
        }

        private static string FormatCompareExportMediaSummary(VideoMediaInfo mediaInfo)
        {
            var resolution = string.Format(
                CultureInfo.InvariantCulture,
                "{0}x{1}",
                mediaInfo.PixelWidth,
                mediaInfo.PixelHeight);
            var audio = mediaInfo.HasAudioStream
                ? "audio: " + FormatDialogValue(mediaInfo.AudioCodecName)
                : "audio: none";
            return resolution + " | " + audio;
        }

        private async Task<AudioInsertionResult?> ReplaceAudioTrackAsync()
        {
            if (!CanReplaceAudioTrack(out var failureMessage))
            {
                CacheStatusTextBlock.Text = failureMessage;
                return null;
            }

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

        private bool CanReplaceAudioTrack(out string status)
        {
            if (!AudioInsertionService.IsBundledRuntimeAvailable)
            {
                status = AudioInsertionService.GetRuntimeAvailabilityMessage();
                return false;
            }

            if (IsCompareModeEnabled)
            {
                status = "Audio insertion is unavailable while two-pane compare mode is enabled.";
                return false;
            }

            if (!_primaryEngine.IsMediaOpen || string.IsNullOrWhiteSpace(_primaryEngine.CurrentFilePath))
            {
                status = "Load a single-pane H.264 MP4 before replacing the audio track.";
                return false;
            }

            if (!string.Equals(Path.GetExtension(_primaryEngine.CurrentFilePath), Mp4Extension, StringComparison.OrdinalIgnoreCase))
            {
                status = "Audio insertion is available only for loaded H.264 MP4 sources.";
                return false;
            }

            if (string.IsNullOrWhiteSpace(_primaryEngine.MediaInfo.VideoCodecName) ||
                !IsH264Codec(_primaryEngine.MediaInfo.VideoCodecName))
            {
                status = "Audio insertion is available only for loaded MP4 sources with H.264 video.";
                return false;
            }

            status = "Replace the reviewed source audio with a WAV or MP3 track and write a new MP4 copy.";
            return true;
        }

        private static bool IsH264Codec(string codecName)
        {
            return codecName.Contains("h264", StringComparison.OrdinalIgnoreCase) ||
                codecName.Contains("avc", StringComparison.OrdinalIgnoreCase);
        }

        private async Task<string?> ExportDiagnosticsAsync(string? outputPath)
        {
            var resolvedOutputPath = outputPath;
            if (string.IsNullOrWhiteSpace(resolvedOutputPath))
            {
                resolvedOutputPath = await PromptForSavePathAsync(
                    "Export Diagnostic Report",
                    "frame-player-desktop-diagnostics.txt",
                    "Text File",
                    "*.txt");
                if (string.IsNullOrWhiteSpace(resolvedOutputPath))
                {
                    return null;
                }
            }

            var report = _diagnosticLogService.BuildReport(BuildDiagnosticsHeader());
            await File.WriteAllTextAsync(resolvedOutputPath, report, new UTF8Encoding(false)).ConfigureAwait(false);
            await SetStatusMessageAsync("Diagnostic report exported: " + Path.GetFileName(resolvedOutputPath)).ConfigureAwait(false);
            return resolvedOutputPath;
        }

        private void UpdateCacheStatusFromEngine()
        {
            var focusedEngine = TryGetExistingEngine(GetFocusedPane());
            if (focusedEngine == null || !focusedEngine.IsMediaOpen)
            {
                SetCacheStatus("Cache: idle", "Open media to populate the decoded-frame cache.");
                return;
            }

            var ffmpegEngine = focusedEngine as FfmpegReviewEngine;
            if (ffmpegEngine == null)
            {
                SetCacheStatus("Cache: unavailable", "Cache details are not available for this engine.");
                return;
            }

            var forwardCount = Math.Max(0, ffmpegEngine.ForwardCachedFrameCount);
            var previousCount = Math.Max(0, ffmpegEngine.PreviousCachedFrameCount);
            var approximateCacheMegabytes = ffmpegEngine.ApproximateCachedFrameBytes / 1048576d;
            var cacheBudgetMegabytes = ffmpegEngine.DecodedFrameCacheBudgetBytes / 1048576d;
            var positionIdentity = focusedEngine.Position != null &&
                                   focusedEngine.Position.IsFrameIndexAbsolute
                ? "absolute frame ready"
                : "frame number pending index";
            var cacheModeLabel = ffmpegEngine.IsGpuActive ? "GPU" : "CPU";
            var completeCacheFrameCount = Math.Max(0, ffmpegEngine.CompleteDecodedCacheFrameCount);
            var completeCacheStatus = FormatCompleteDecodedCacheStatus(ffmpegEngine, completeCacheFrameCount);
            var message = FormatCacheStatusMessage(
                ffmpegEngine,
                previousCount,
                forwardCount,
                completeCacheFrameCount,
                cacheModeLabel);
            var tooltip = string.Format(
                CultureInfo.InvariantCulture,
                "Backend: {0}. GPU status: {1}. Fallback: {2}. Queue depth: {3}. Index: {4}. Frame identity: {5}. Complete cache: {6}. Review cache budget is {7:0.0} MiB and currently uses about {8:0.0} MiB with {9} prior and {10} forward decoded frames at the current position, up to {11} prior and {12} forward. Last refill: {13} ({14:0.0} ms, {15}). Timeline seeks show the landed frame first.",
                string.IsNullOrWhiteSpace(ffmpegEngine.ActiveDecodeBackend) ? "(unknown)" : ffmpegEngine.ActiveDecodeBackend,
                string.IsNullOrWhiteSpace(ffmpegEngine.GpuCapabilityStatus) ? "none" : ffmpegEngine.GpuCapabilityStatus,
                string.IsNullOrWhiteSpace(ffmpegEngine.GpuFallbackReason) ? "none" : ffmpegEngine.GpuFallbackReason,
                ffmpegEngine.OperationalQueueDepth,
                ffmpegEngine.GlobalFrameIndexStatus,
                positionIdentity,
                completeCacheStatus,
                cacheBudgetMegabytes,
                approximateCacheMegabytes,
                previousCount,
                forwardCount,
                ffmpegEngine.MaxPreviousCachedFrameCount,
                ffmpegEngine.MaxForwardCachedFrameCount,
                string.IsNullOrWhiteSpace(ffmpegEngine.LastCacheRefillReason) ? "none" : ffmpegEngine.LastCacheRefillReason,
                ffmpegEngine.LastCacheRefillMilliseconds,
                string.IsNullOrWhiteSpace(ffmpegEngine.LastCacheRefillMode) ? "none" : ffmpegEngine.LastCacheRefillMode);
            SetCacheStatus(message, tooltip);
        }

        private static string FormatCompleteDecodedCacheStatus(
            FfmpegReviewEngine ffmpegEngine,
            int completeCacheFrameCount)
        {
            if (ffmpegEngine.IsCompleteDecodedCacheLoaded)
            {
                return string.Format(
                    CultureInfo.InvariantCulture,
                    "complete decoded cache loaded for {0} frames",
                    completeCacheFrameCount);
            }

            if (ffmpegEngine.IsCompleteDecodedCacheEligible)
            {
                return "complete decoded cache eligible; loads on the next indexed seek";
            }

            return "complete decoded cache not eligible";
        }

        private static string FormatCacheStatusMessage(
            FfmpegReviewEngine ffmpegEngine,
            int previousCount,
            int forwardCount,
            int completeCacheFrameCount,
            string cacheModeLabel)
        {
            if (ffmpegEngine.IsCompleteDecodedCacheLoaded)
            {
                return string.Format(
                    CultureInfo.InvariantCulture,
                    "Cache: complete ({0} frames, {1})",
                    completeCacheFrameCount,
                    cacheModeLabel);
            }

            var statusFormat = ffmpegEngine.IsGlobalFrameIndexBuildInProgress
                ? "Cache: indexing ({0} back / {1} ahead, {2})"
                : "Cache: ready ({0} back / {1} ahead, {2})";
            return string.Format(
                CultureInfo.InvariantCulture,
                statusFormat,
                previousCount,
                forwardCount,
                cacheModeLabel);
        }

        private void SetCacheStatus(string message, string tooltip)
        {
            var dispatcher = CacheStatusTextBlock.Dispatcher;
            if (dispatcher.CheckAccess())
            {
                ToolTip.SetTip(CacheStatusTextBlock, tooltip);
                CacheStatusTextBlock.Text = message;
            }
            else
            {
                dispatcher.Post(() =>
                {
                    ToolTip.SetTip(CacheStatusTextBlock, tooltip);
                    CacheStatusTextBlock.Text = message;
                });
            }
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
            catch (Exception dispatcherEx)
            {
                // In headless tests the global dispatcher and control owner can disagree briefly.
                Trace.TraceWarning("SetStatusMessageAsync dispatcher check failed: " + dispatcherEx.Message);
            }

            dispatcher.Post(() =>
            {
                try
                {
                    CacheStatusTextBlock.Text = message;
                }
                catch (Exception postEx)
                {
                    // Status text is informational; export/test completion must not depend on it.
                    Trace.TraceWarning("SetStatusMessageAsync post failed: " + postEx.Message);
                }
            });
            return Task.CompletedTask;
        }

        private List<string> BuildDiagnosticsHeader()
        {
            var header = new List<string>
            {
                "Frame Player desktop diagnostics",
                "Generated: " + DateTimeOffset.Now.ToString("O", CultureInfo.InvariantCulture),
                "Version: " + (typeof(MainWindow).Assembly.GetName().Version?.ToString() ?? UnknownRawValue),
                "OS: " + RuntimeInformation.OSDescription,
                ".NET: " + RuntimeInformation.FrameworkDescription
            };

            var builder = new StringBuilder();
            AppendEngineDiagnostics(builder, PanePrimaryLabel, _primaryEngine);
            if (_compareEngine != null)
            {
                AppendEngineDiagnostics(builder, PaneCompareLabel, _compareEngine);
            }

            builder.AppendLine("Loop primary: " + BuildLoopStatusText(_primaryLoopRange));
            builder.AppendLine("Loop compare: " + BuildLoopStatusText(_compareLoopRange));
            builder.AppendLine("Runtime base: " + AppContext.BaseDirectory);
            builder.AppendLine("Export runtime: " + (ExportHostClient.IsBundledRuntimeAvailable
                ? "available"
                : ExportHostClient.GetRuntimeAvailabilityMessage()));

            header.AddRange(builder.ToString().Split(new[] { Environment.NewLine }, StringSplitOptions.None));
            return header;
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

            UseGpuAccelerationMenuItem.IsChecked = useGpuAcceleration;
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
                HorizontalAlignment = global::Avalonia.Layout.HorizontalAlignment.Right,
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

        private const string UnknownDisplayLabel = "—";

        private static VideoInfoSnapshot BuildVideoInfoSnapshot(
            string paneLabel,
            VideoMediaInfo mediaInfo,
            ReviewPosition position)
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
                new VideoInfoField("Current position", FormatInspectorDuration(position.PresentationTime)),
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
                    new VideoInfoField("Bitrate", FormatInspectorBitRate(mediaInfo.AudioBitRate)),
                    new VideoInfoField("Playback", mediaInfo.IsAudioPlaybackAvailable ? "available" : "unavailable")
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

            return new VideoInfoSnapshot
            {
                WindowTitle = "Video Info - " + paneLabel,
                PaneLabel = paneLabel,
                FileName = fileName,
                FilePath = filePath,
                SummarySection = new VideoInfoSection(summaryFields),
                VideoSection = new VideoInfoSection(videoFields),
                AudioSection = audioSection,
                AdvancedSection = new VideoInfoSection(advancedFields)
            };
        }

        private static string FormatInspectorDuration(TimeSpan duration)
        {
            return duration >= TimeSpan.Zero
                ? FormatTime(duration)
                : UnknownDisplayLabel;
        }

        private static void AddInspectorFieldIfKnown(List<VideoInfoField> fields, string label, string? value)
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
                : UnknownDisplayLabel;
        }

        private static string FormatInspectorResolution(int width, int height)
        {
            return width > 0 && height > 0
                ? string.Format(CultureInfo.InvariantCulture, "{0} x {1}", width, height)
                : UnknownDisplayLabel;
        }

        private static string FormatInspectorResolution(int? width, int? height)
        {
            return width.HasValue && height.HasValue
                ? FormatInspectorResolution(width.Value, height.Value)
                : UnknownDisplayLabel;
        }

        private static string FormatInspectorRatio(int? numerator, int? denominator)
        {
            return numerator.HasValue &&
                   denominator.HasValue &&
                   numerator.Value > 0 &&
                   denominator.Value > 0
                ? string.Format(CultureInfo.InvariantCulture, "{0}:{1}", numerator.Value, denominator.Value)
                : UnknownDisplayLabel;
        }

        private static string FormatInspectorText(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return UnknownDisplayLabel;
            }

            var trimmedValue = value.Trim();
            return trimmedValue.Equals(UnknownRawValue, StringComparison.OrdinalIgnoreCase) ||
                   trimmedValue.Equals("unspecified", StringComparison.OrdinalIgnoreCase) ||
                   trimmedValue.StartsWith("reserved", StringComparison.OrdinalIgnoreCase)
                ? UnknownDisplayLabel
                : trimmedValue;
        }

        private static string FormatInspectorBitDepth(int? bitDepth)
        {
            return bitDepth.HasValue && bitDepth.Value > 0
                ? string.Format(CultureInfo.InvariantCulture, "{0}-bit", bitDepth.Value)
                : UnknownDisplayLabel;
        }

        private static string FormatInspectorBitRate(long? bitsPerSecond)
        {
            if (!bitsPerSecond.HasValue || bitsPerSecond.Value <= 0L)
            {
                return UnknownDisplayLabel;
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
                : UnknownDisplayLabel;
        }

        private static string FormatInspectorChannelCount(int channelCount)
        {
            return channelCount > 0
                ? string.Format(CultureInfo.InvariantCulture, "{0} channel(s)", channelCount)
                : UnknownDisplayLabel;
        }

        private static string FormatInspectorIndex(int streamIndex)
        {
            return streamIndex >= 0
                ? streamIndex.ToString(CultureInfo.InvariantCulture)
                : UnknownDisplayLabel;
        }

        private static string FormatInspectorFraction(int numerator, int denominator)
        {
            return numerator > 0 && denominator > 0
                ? string.Format(CultureInfo.InvariantCulture, "{0}/{1}", numerator, denominator)
                : UnknownDisplayLabel;
        }

        private static string BuildHelpText()
        {
            return string.Join(
                Environment.NewLine,
                "Space: play or pause",
                "Left / Right: previous or next frame",
                "Ctrl+Left / Ctrl+Right: previous or next 10 frames",
                "Shift+Left / Shift+Right: previous or next 100 frames",
                ", / .: rewind or fast forward 5 seconds",
                CommandKeyLabel + "+O: open video",
                CommandKeyLabel + "+N: new window",
                CommandKeyLabel + "+W: close video",
                CommandKeyLabel + "+Shift+E: export diagnostic report",
                "L: loop playback",
                "[ / ]: set loop in or loop out",
                "Playback menu: zoom in, zoom out, reset zoom",
                "F1: controls and shortcuts",
                "F11 / Alt+Enter: full screen",
                "Escape: exit full screen",
                "Right-click video: video info, reset zoom, loop export, compare export",
                "Right-click timeline: set loop markers and export loop");
        }

        private static string BuildAboutText()
        {
            var version = typeof(MainWindow).Assembly.GetName().Version?.ToString() ?? UnknownRawValue;
            return string.Join(
                Environment.NewLine,
                "Frame Player",
                "Version: " + version,
                "",
                "A/V playback and frame-accurate review for Windows and macOS.",
                "Built with Avalonia UI.");
        }

        private static string FormatDialogValue(string? value)
        {
            return string.IsNullOrWhiteSpace(value) ? UnknownRawValue : value.Trim();
        }

        private static string BuildDiagnosticFileIdentifier(string filePath)
        {
            var normalizedPath = string.IsNullOrWhiteSpace(filePath)
                ? string.Empty
                : filePath.Trim();
            var hashBytes = SHA256.HashData(Encoding.UTF8.GetBytes(normalizedPath));
            return "path-hash:" + Convert.ToHexString(hashBytes.AsSpan(0, 6));
        }

        private NativeMenu BuildNativeMenu(bool useGpuAcceleration)
        {
            _nativeRecentFilesMenuItem = new NativeMenuItem("Open Recent")
            {
                Menu = new NativeMenu()
            };
            _nativeCloseVideoMenuItem = CreateMenuItem("Close Video", async (_, _) => await CloseVideosAsync(), new KeyGesture(Key.W, CommandKeyModifier));
            _nativeVideoInfoMenuItem = CreateMenuItem("Video Info...", (sender, _) => VideoInfoMenuItem_Click(sender, new RoutedEventArgs()));

            var fileMenu = CreateTopLevelMenu(
                "File",
                CreateMenuItem("New Window", (_, _) => LaunchNewWindow(), new KeyGesture(Key.N, CommandKeyModifier)),
                new NativeMenuItemSeparator(),
                CreateMenuItem("Open Video...", async (_, _) => await OpenVideoAsync(GetFileOpenTargetPane()), new KeyGesture(Key.O, CommandKeyModifier)),
                _nativeRecentFilesMenuItem,
                new NativeMenuItemSeparator(),
                _nativeCloseVideoMenuItem,
                _nativeVideoInfoMenuItem,
                new NativeMenuItemSeparator(),
                CreateMenuItem("Export Diagnostic Report...", (sender, _) => ExportDiagnosticsMenuItem_Click(sender, new RoutedEventArgs()), new KeyGesture(Key.E, CommandShiftKeyModifier)),
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
            _nativeRewindMenuItem = CreateMenuItem("Rewind 5s", (sender, _) => RewindButton_Click(sender, new RoutedEventArgs()), new KeyGesture(Key.OemComma));
            _nativeFastForwardMenuItem = CreateMenuItem("Fast Forward 5s", (sender, _) => FastForwardButton_Click(sender, new RoutedEventArgs()), new KeyGesture(Key.OemPeriod));
            _nativePreviousFrameMenuItem = CreateMenuItem("Previous Frame", (sender, _) => PreviousFrameButton_Click(sender, new RoutedEventArgs()), new KeyGesture(Key.Left));
            _nativeNextFrameMenuItem = CreateMenuItem("Next Frame", (sender, _) => NextFrameButton_Click(sender, new RoutedEventArgs()), new KeyGesture(Key.Right));
            _nativeSetLoopInMenuItem = CreateMenuItem("Set Loop In", (_, _) => SetLoopMarker(LoopPlaybackMarkerEndpoint.In), new KeyGesture(Key.OemOpenBrackets));
            _nativeSetLoopOutMenuItem = CreateMenuItem("Set Loop Out", (_, _) => SetLoopMarker(LoopPlaybackMarkerEndpoint.Out), new KeyGesture(Key.OemCloseBrackets));
            _nativeClearLoopPointsMenuItem = CreateMenuItem("Clear Loop Points", (_, _) => ClearLoopPoints());
            _nativeSaveLoopAsClipMenuItem = CreateMenuItem("Save Loop As Clip...", (sender, _) => SaveLoopAsClipMenuItem_Click(sender, new RoutedEventArgs()));
            _nativeExportSideBySideCompareMenuItem = CreateMenuItem("Export Side-by-Side Compare...", (sender, _) => ExportSideBySideCompareMenuItem_Click(sender, new RoutedEventArgs()));
            _nativeZoomInMenuItem = CreateCommandMenuItem("Zoom In", ZoomInFocusedPane, new KeyGesture(Key.OemPlus));
            _nativeZoomOutMenuItem = CreateCommandMenuItem("Zoom Out", ZoomOutFocusedPane, new KeyGesture(Key.OemMinus));
            _nativeResetZoomMenuItem = CreateCommandMenuItem("Reset Zoom", ResetZoomForFocusedPane, new KeyGesture(Key.D0));

            var playbackMenu = CreateTopLevelMenu(
                "Playback",
                _nativePlayPauseMenuItem,
                new NativeMenuItemSeparator(),
                _nativeRewindMenuItem,
                _nativeFastForwardMenuItem,
                new NativeMenuItemSeparator(),
                _nativePreviousFrameMenuItem,
                _nativeNextFrameMenuItem,
                new NativeMenuItemSeparator(),
                _nativeLoopPlaybackMenuItem,
                _nativeSetLoopInMenuItem,
                _nativeSetLoopOutMenuItem,
                _nativeClearLoopPointsMenuItem,
                _nativeSaveLoopAsClipMenuItem,
                _nativeExportSideBySideCompareMenuItem,
                new NativeMenuItemSeparator(),
                _nativeZoomInMenuItem,
                _nativeZoomOutMenuItem,
                _nativeResetZoomMenuItem,
                new NativeMenuItemSeparator(),
                _nativeGpuAccelerationMenuItem,
                CreateMenuItem("Toggle Full Screen", (_, _) => ToggleFullScreen(), new KeyGesture(Key.F11)));

            _nativeReplaceAudioTrackMenuItem = CreateMenuItem("Replace Audio Track...", (sender, _) => ReplaceAudioTrackMenuItem_Click(sender, new RoutedEventArgs()));
            var audioMenu = CreateTopLevelMenu(
                "Audio Insertion",
                _nativeReplaceAudioTrackMenuItem);

            var helpMenu = CreateTopLevelMenu(
                "Help",
                CreateMenuItem("Controls and Shortcuts...", (sender, _) => HelpMenuItem_Click(sender, new RoutedEventArgs()), new KeyGesture(Key.F1)),
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

        private sealed class CompareSideBySideExportDialogSelection
        {
            public CompareSideBySideExportDialogSelection(
                CompareSideBySideExportMode mode,
                CompareSideBySideExportAudioSource audioSource)
            {
                Mode = mode;
                AudioSource = audioSource;
            }

            public CompareSideBySideExportMode Mode { get; }

            public CompareSideBySideExportAudioSource AudioSource { get; }
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
            CancelQueuedSliderScrubs();
            ClearPendingFramePresentations();
            _primaryFrameBuffer?.Dispose();
            _compareFrameBuffer?.Dispose();
            _primaryFrameBuffer = null;
            _compareFrameBuffer = null;
            DisposeReusablePaneBitmaps();
            _primaryEngine.Dispose();
            _compareEngine?.Dispose();
            base.OnClosed(e);
        }

        private void DisposeReusablePaneBitmaps()
        {
            _primaryReusableBitmap?.Dispose();
            _compareReusableBitmap?.Dispose();
            _primaryReusableBitmap = null;
            _compareReusableBitmap = null;
        }

        private enum Pane
        {
            Primary,
            Compare
        }
    }
}
