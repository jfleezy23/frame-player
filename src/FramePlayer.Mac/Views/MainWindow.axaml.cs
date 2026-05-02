using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
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
using FramePlayer.Mac.Services;
using FramePlayer.Services;

namespace FramePlayer.Mac.Views
{
    public sealed partial class MainWindow : Window
    {
        private readonly VideoReviewEngineFactory _engineFactory;
        private readonly MacRecentFilesService _recentFilesService;
        private readonly IVideoReviewEngine _primaryEngine;
        private IVideoReviewEngine? _compareEngine;
        private const int HundredFrameStep = 100;
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
        private static readonly IBrush PaneChromeBrush = Brush.Parse("#171C22");
        private static readonly IBrush PaneChromeBorderBrush = Brush.Parse("#28313B");
        private static readonly IBrush PaneSelectedBrush = Brush.Parse("#1D2934");
        private static readonly IBrush PaneSelectedBorderBrush = Brush.Parse("#5AA9E6");

        public MainWindow()
        {
            InitializeComponent();

            if (string.Equals(Environment.GetEnvironmentVariable("FRAMEPLAYER_MAC_SKIP_RUNTIME_BOOTSTRAP"), "1", StringComparison.Ordinal))
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

            _recentFilesService = new MacRecentFilesService();
            var optionsProvider = new FfmpegReviewEngineOptionsProvider(new AppPreferencesService());
            _engineFactory = new VideoReviewEngineFactory(optionsProvider);
            _primaryEngine = _engineFactory.Create("primary");
            _primaryEngine.StateChanged += PrimaryEngine_StateChanged;
            _primaryEngine.FramePresented += PrimaryEngine_FramePresented;
            _primaryLoopRange = CreateLoopRange(null, null);
            _compareLoopRange = CreateLoopRange(Pane.Compare, null, null);

            NativeMenu.SetMenu(this, BuildNativeMenu(optionsProvider.UseGpuAcceleration));
            UpdateRecentFilesMenu();
            UpdatePaneSelectionVisuals();
            InstallContextMenus();
            AddHandler(DragDrop.DropEvent, Window_Drop);
            AddHandler(DragDrop.DragOverEvent, Window_DragOver);
            KeyDown += Window_KeyDown;
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
            var firstRecent = _recentFilesService.Load().FirstOrDefault();
            if (!string.IsNullOrWhiteSpace(firstRecent))
            {
                await OpenRecentPathAsync(firstRecent);
            }
        }

        private Task OpenMediaAsync(string filePath)
        {
            return OpenMediaAsync(filePath, "pane-primary");
        }

        private Task OpenMediaAsync(string filePath, string paneId)
        {
            return OpenPathAsync(filePath, ResolvePane(paneId));
        }

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
                        Patterns = new[] { "*.avi", "*.m4v", "*.mp4", "*.mkv", "*.wmv", "*.mov" }
                    }
                }
            });

            var selected = files.FirstOrDefault();
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
                if (pane == Pane.Primary)
                {
                    _primaryLoopRange = CreateLoopRange(Pane.Primary, null, null);
                }
                else
                {
                    _compareLoopRange = CreateLoopRange(Pane.Compare, null, null);
                }

                UpdateLoopUi();
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
                _compareEngine = _engineFactory.Create("compare");
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
            return string.Equals(paneId, "pane-compare", StringComparison.Ordinal)
                ? Pane.Compare
                : Pane.Primary;
        }

        private static string ResolvePaneId(Pane pane)
        {
            return pane == Pane.Compare ? "pane-compare" : "pane-primary";
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
            CustomVideoSurface.Source = null;
            CompareVideoSurface.Source = null;
            PrimaryEmptyStateOverlay.IsVisible = true;
            CompareEmptyStateOverlay.IsVisible = true;
            PlaybackStateTextBlock.Text = "Ready";
            CurrentFrameTextBlock.Text = "Frame --";
            TimecodeTextBlock.Text = "Timecode --";
        }

        private async void PlayPauseButton_Click(object? sender, RoutedEventArgs e)
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
            var engine = GetEngine(GetFocusedPane());
            var target = engine.Position.PresentationTime - TimeSpan.FromSeconds(5);
            await engine.SeekToTimeAsync(target < TimeSpan.Zero ? TimeSpan.Zero : target);
        }

        private async void FastForwardButton_Click(object? sender, RoutedEventArgs e)
        {
            var engine = GetEngine(GetFocusedPane());
            await engine.SeekToTimeAsync(engine.Position.PresentationTime + TimeSpan.FromSeconds(5));
        }

        private async Task StartPlaybackAsync(SynchronizedOperationScope? operationScope, string? paneId)
        {
            await GetEngine(ResolvePane(paneId)).PlayAsync();
        }

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
            await GetEngine(GetFocusedPane()).PauseAsync();
        }

        private async Task StepFrameAsync(int delta)
        {
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
                CacheStatusTextBlock.Text = "Zoom is already reset.";
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
                resetZoomItem.IsEnabled = true;
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

        private async Task CommitSliderSeekAsync(string interactionName, TimeSpan target)
        {
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
                CustomVideoSurface.Source = AvaloniaFrameBufferPresenter.CreateBitmap(e.FrameBuffer);
                PrimaryEmptyStateOverlay.IsVisible = false;
            });
        }

        private void CompareEngine_FramePresented(object? sender, FramePresentedEventArgs e)
        {
            Dispatcher.UIThread.Post(() =>
            {
                CompareVideoSurface.Source = AvaloniaFrameBufferPresenter.CreateBitmap(e.FrameBuffer);
                CompareEmptyStateOverlay.IsVisible = false;
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
                    FrameNumberTextBox.Text = state.Position.FrameIndex.HasValue
                        ? (state.Position.FrameIndex.Value + 1).ToString(CultureInfo.InvariantCulture)
                        : string.Empty;
                    PrimaryPaneFrameNumberTextBox.Text = FrameNumberTextBox.Text;
                    PlayPausePlayIcon.IsVisible = !state.IsPlaying;
                    PlayPausePauseIcon.IsVisible = state.IsPlaying;
                    PrimaryPanePlayPausePlayIcon.IsVisible = !state.IsPlaying;
                    PrimaryPanePlayPausePauseIcon.IsVisible = state.IsPlaying;
                    if (_nativePlayPauseMenuItem != null)
                    {
                        _nativePlayPauseMenuItem.Header = state.IsPlaying ? "Pause" : "Play";
                    }

                    PlaybackStateTextBlock.Text = state.IsPlaying ? "Playing" : state.IsMediaOpen ? "Paused" : "Ready";
                }
                else
                {
                    ComparePanePositionSlider.Maximum = durationSeconds;
                    ComparePanePositionSlider.Value = positionSeconds;
                    ComparePaneCurrentPositionTextBlock.Text = positionText;
                    ComparePaneDurationTextBlock.Text = durationText;
                    ComparePaneFrameNumberTextBox.Text = state.Position.FrameIndex.HasValue
                        ? (state.Position.FrameIndex.Value + 1).ToString(CultureInfo.InvariantCulture)
                        : string.Empty;
                    ComparePanePlayPausePlayIcon.IsVisible = !state.IsPlaying;
                    ComparePanePlayPausePauseIcon.IsVisible = state.IsPlaying;
                }

                if (!string.IsNullOrWhiteSpace(state.LastErrorMessage))
                {
                    CacheStatusTextBlock.Text = state.LastErrorMessage;
                }
            }
            finally
            {
                _isUpdatingSliders = false;
            }
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
                pane == Pane.Compare ? "compare" : "primary",
                pane == Pane.Compare ? "Compare" : "Primary",
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
                pane == Pane.Compare ? "compare" : "primary",
                pane == Pane.Compare ? "Compare" : "Primary",
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
            else if (e.Key == Key.N && e.KeyModifiers.HasFlag(KeyModifiers.Meta))
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
            else if (e.Key == Key.O && e.KeyModifiers.HasFlag(KeyModifiers.Meta))
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
        }

        private void Window_DragOver(object? sender, DragEventArgs e)
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
            CacheStatusTextBlock.Text = info == null || string.IsNullOrWhiteSpace(info.FilePath)
                ? "Video info unavailable"
                : string.Format(
                    CultureInfo.InvariantCulture,
                    "{0}: {1}x{2} {3:0.###} fps {4}",
                    pane == Pane.Compare ? "Compare" : "Primary",
                    info.PixelWidth,
                    info.PixelHeight,
                    info.FramesPerSecond,
                    info.VideoCodecName);
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
                    "*.mp4");
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
                    pane == Pane.Compare ? "Compare" : "Primary",
                    ResolvePaneId(pane),
                    true,
                    BuildReviewSessionSnapshot(pane, engine),
                    loopRange,
                    ffmpegEngine,
                    BuildFullFrameViewport(engine.MediaInfo));
                var plan = ClipExportService.CreatePlan(request);
                var result = await NativeClipExportService.ExportAsync(plan).ConfigureAwait(false);
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
                    BuildSuggestedExportFileName(primaryEngine.CurrentFilePath, mode == CompareSideBySideExportMode.Loop ? "compare-loop" : "compare"),
                    "MP4 Video",
                    "*.mp4");
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
                    PrimaryViewportSnapshot = BuildFullFrameViewport(primaryEngine.MediaInfo),
                    CompareViewportSnapshot = BuildFullFrameViewport(compareEngine.MediaInfo),
                    PrimaryLoopRange = _primaryLoopRange,
                    CompareLoopRange = _compareLoopRange,
                    PrimaryEngine = primaryEngine,
                    CompareEngine = compareEngine
                };
                var plan = CompareSideBySideExportService.CreatePlan(request);
                var result = await NativeCompareSideBySideExportService.ExportAsync(plan).ConfigureAwait(false);
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
                "*.mp4");
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
                    "Primary",
                    BuildReviewSessionSnapshot(Pane.Primary, _primaryEngine));
                var plan = AudioInsertionService.CreatePlan(request);
                var result = await NativeAudioInsertionService.InsertAsync(plan).ConfigureAwait(false);
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
            builder.AppendLine("Frame Player macOS diagnostics");
            builder.AppendLine("Generated: " + DateTimeOffset.Now.ToString("O", CultureInfo.InvariantCulture));
            AppendEngineDiagnostics(builder, "Primary", _primaryEngine);
            if (_compareEngine != null)
            {
                AppendEngineDiagnostics(builder, "Compare", _compareEngine);
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
                pane == Pane.Compare ? "compare" : "primary",
                pane == Pane.Compare ? "Compare" : "Primary",
                ReviewSessionSnapshot.FromEngineState(engine.IsMediaOpen, engine.IsPlaying),
                engine.CurrentFilePath,
                engine.MediaInfo,
                engine.Position);
        }

        private static PaneViewportSnapshot BuildFullFrameViewport(VideoMediaInfo mediaInfo)
        {
            return PaneViewportSnapshot.CreateFullFrame(mediaInfo.PixelWidth, mediaInfo.PixelHeight);
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
                ? "video-" + suffix + ".mp4"
                : baseName + "-" + suffix + ".mp4";
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
            return files.FirstOrDefault()?.Path.LocalPath;
        }

        private void HelpMenuItem_Click(object? sender, RoutedEventArgs e)
        {
            CacheStatusTextBlock.Text = "Shortcuts: Space play/pause, Left/Right frame step, comma/period seek 5s, L loop, F11 full screen.";
        }

        private void AboutMenuItem_Click(object? sender, RoutedEventArgs e)
        {
            CacheStatusTextBlock.Text = "Frame Player macOS Avalonia port";
        }

        private NativeMenu BuildNativeMenu(bool useGpuAcceleration)
        {
            var fileMenu = CreateTopLevelMenu(
                "File",
                CreateMenuItem("New Window", (_, _) => LaunchNewWindow(), new KeyGesture(Key.N, KeyModifiers.Meta)),
                new NativeMenuItemSeparator(),
                CreateMenuItem("Open Video...", async (_, _) => await OpenVideoAsync(GetFileOpenTargetPane()), new KeyGesture(Key.O, KeyModifiers.Meta)),
                _nativeRecentFilesMenuItem = new NativeMenuItem("Open Recent")
                {
                    Menu = new NativeMenu()
                },
                new NativeMenuItemSeparator(),
                CreateMenuItem("Close Video", async (_, _) => await CloseVideosAsync(), new KeyGesture(Key.W, KeyModifiers.Meta)),
                CreateMenuItem("Video Info...", (sender, _) => VideoInfoMenuItem_Click(sender, new RoutedEventArgs())),
                new NativeMenuItemSeparator(),
                CreateMenuItem("Export Diagnostic Report...", (sender, _) => ExportDiagnosticsMenuItem_Click(sender, new RoutedEventArgs())),
                new NativeMenuItemSeparator(),
                CreateMenuItem("Exit", (_, _) => Close()));

            _nativePlayPauseMenuItem = CreateMenuItem("Play", (sender, _) => PlayPauseButton_Click(sender, new RoutedEventArgs()), new KeyGesture(Key.Space));
            _nativeGpuAccelerationMenuItem = CreateMenuItem("Use GPU Acceleration", (_, _) =>
            {
                if (_nativeGpuAccelerationMenuItem != null)
                {
                    _nativeGpuAccelerationMenuItem.IsChecked = !_nativeGpuAccelerationMenuItem.IsChecked;
                }

                CacheStatusTextBlock.Text = "GPU acceleration preference will apply when the macOS FFmpeg runtime supports it.";
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
                CreateMenuItem("Zoom In", (_, _) => CacheStatusTextBlock.Text = "Zoom in will be wired with the pane interaction pass."),
                CreateMenuItem("Zoom Out", (_, _) => CacheStatusTextBlock.Text = "Zoom out will be wired with the pane interaction pass."),
                CreateMenuItem("Reset Zoom", (_, _) => CacheStatusTextBlock.Text = "Zoom reset will be wired with the pane interaction pass."),
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
