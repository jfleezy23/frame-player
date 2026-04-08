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
        private static readonly TimeSpan SeekJump = TimeSpan.FromSeconds(5);
        private static readonly TimeSpan FrameStepInitialDelay = TimeSpan.FromMilliseconds(550);
        private static readonly TimeSpan FrameStepRepeatInterval = TimeSpan.FromMilliseconds(60);

        private readonly DispatcherTimer _positionTimer;
        private readonly DispatcherTimer _frameStepRepeatTimer;
        private readonly BuildVariantInfo _buildVariant;
        private readonly DiagnosticLogService _diagnosticLogService;
        private readonly RecentFilesService _recentFilesService;
        private readonly IVideoReviewEngine _videoReviewEngine;

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

            _diagnosticLogService = new DiagnosticLogService();
            _recentFilesService = new RecentFilesService();
            _videoReviewEngine = CreateVideoReviewEngine();
            _videoReviewEngine.StateChanged += VideoReviewEngine_StateChanged;
            _videoReviewEngine.FramePresented += VideoReviewEngine_FramePresented;
            _framesPerSecond = DefaultFramesPerSecond;
            _positionStep = TimeSpan.FromSeconds(1d / DefaultFramesPerSecond);
            _mediaDuration = TimeSpan.Zero;
            _currentFilePath = string.Empty;
            _lastMediaErrorMessage = string.Empty;

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
            UpdateHeader();
            UpdatePositionDisplay(TimeSpan.Zero);
            UpdateFullScreenVisualState();
            UpdateTransportState();

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
            return new FfmpegReviewEngine();
        }

        private void FocusPreferredVideoSurface()
        {
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

        private async void OpenFileButton_Click(object sender, RoutedEventArgs e)
        {
            if (!EnsureRuntimeAvailable())
            {
                return;
            }

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
            _videoReviewEngine.StateChanged -= VideoReviewEngine_StateChanged;
            _videoReviewEngine.FramePresented -= VideoReviewEngine_FramePresented;
            _videoReviewEngine.Dispose();
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
                Mouse.OverrideCursor = Cursors.Wait;
                _lastMediaErrorMessage = string.Empty;
                SetPlaybackMessage("Opening media...");
                LogInfo("Opening media: " + GetSafeFileDisplay(filePath));

                if (_isMediaLoaded)
                {
                    LogInfo("Closing current media before opening the new file.");
                    await _videoReviewEngine.CloseAsync();
                }

                await RunWithCacheStatusAsync(
                    "Cache: indexing and warming...",
                    () => _videoReviewEngine.OpenAsync(filePath));
                _currentFilePath = filePath;
                _isMediaLoaded = _videoReviewEngine.IsMediaOpen;
                _isPlaying = _videoReviewEngine.IsPlaying;

                await _videoReviewEngine.PauseAsync();
                RefreshMediaMetricsFromEngine();
                await RunWithCacheStatusAsync(
                    "Cache: warming first frame...",
                    () => _videoReviewEngine.SeekToTimeAsync(TimeSpan.Zero));

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
                var ffmpegEngine = _videoReviewEngine as FfmpegReviewEngine;
                if (ffmpegEngine != null)
                {
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
                _lastMediaErrorMessage = !string.IsNullOrWhiteSpace(_videoReviewEngine.LastErrorMessage)
                    ? SanitizeSensitiveText(_videoReviewEngine.LastErrorMessage)
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

        private async Task TogglePlaybackAsync()
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
                await PausePlaybackAsync();
                return;
            }

            await StartPlaybackAsync();
        }

        private async Task CloseMediaAsync()
        {
            EndHeldFrameStep();

            if (_isMediaLoaded)
            {
                LogInfo("Closing media: " + GetSafeFileDisplay(_currentFilePath));
                await _videoReviewEngine.CloseAsync();
            }

            ResetMediaState(clearFilePath: true, clearErrorMessage: true);
        }

        private async Task PausePlaybackAsync(bool logAction = true)
        {
            if (!_isMediaLoaded)
            {
                return;
            }

            var positionBeforePause = GetDisplayPosition();
            var wasPlaying = _isPlaying;
            await _videoReviewEngine.PauseAsync();
            _isPlaying = _videoReviewEngine.IsPlaying;
            UpdateTransportState();

            if (logAction && wasPlaying)
            {
                LogInfo("Playback paused at " + FormatTime(positionBeforePause) + ".");
            }
        }

        private async Task StartPlaybackAsync()
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

            await _videoReviewEngine.PlayAsync();
            _isPlaying = _videoReviewEngine.IsPlaying;
            if (!_isPlaying)
            {
                _lastMediaErrorMessage = !string.IsNullOrWhiteSpace(_videoReviewEngine.LastErrorMessage)
                    ? SanitizeSensitiveText(_videoReviewEngine.LastErrorMessage)
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

        private async Task SeekRelativeAsync(TimeSpan offset)
        {
            EndHeldFrameStep();

            if (!_isMediaLoaded)
            {
                return;
            }

            var target = ClampPosition(GetDisplayPosition() + offset);
            await RunWithCacheStatusAsync(
                "Cache: seeking and warming...",
                () => _videoReviewEngine.SeekToTimeAsync(target));
            UpdatePositionDisplay(GetDisplayPosition());
            LogInfo(string.Format(
                CultureInfo.InvariantCulture,
                "Seeked {0}{1} to {2}; landed at {3}.",
                offset < TimeSpan.Zero ? "-" : "+",
                FormatTime(offset.Duration()),
                FormatTime(target),
                FormatTime(GetDisplayPosition())));
        }

        private async Task StepFrameAsync(int delta)
        {
            if (!_isMediaLoaded || _isFrameStepInProgress)
            {
                return;
            }

            _isFrameStepInProgress = true;

            try
            {
                await PausePlaybackAsync(logAction: false);

                FrameStepResult stepResult = null;
                await RunWithCacheStatusAsync(
                    delta < 0 ? "Cache: checking backward window..." : "Cache: refilling forward window...",
                    async () =>
                    {
                        stepResult = delta < 0
                            ? await _videoReviewEngine.StepBackwardAsync()
                            : await _videoReviewEngine.StepForwardAsync();
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

        private async Task SeekToAsync(TimeSpan target, string diagnosticSource = null)
        {
            EndHeldFrameStep();

            if (!_isMediaLoaded)
            {
                return;
            }

            var clampedTarget = ClampPosition(target);
            await RunWithCacheStatusAsync(
                "Cache: seeking...",
                () => _videoReviewEngine.SeekToTimeAsync(clampedTarget));
            UpdatePositionDisplay(GetDisplayPosition());

            if (!string.IsNullOrWhiteSpace(diagnosticSource))
            {
                LogSeekResult(diagnosticSource, target, clampedTarget);
            }
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
                () => _videoReviewEngine.SeekToFrameAsync(targetFrameIndex));
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

        private void RefreshMediaMetricsFromEngine()
        {
            var mediaInfo = _videoReviewEngine.MediaInfo;
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
            EmptyStateOverlay.Visibility = canControl ? Visibility.Collapsed : Visibility.Visible;
            UpdateEmptyState();

            if (!_isMediaLoaded)
            {
                SetPlaybackMessage(
                    !string.IsNullOrWhiteSpace(_lastMediaErrorMessage)
                        ? "The last action did not complete."
                        : App.HasBundledFfmpegRuntime
                            ? _buildVariant.IsComparisonBuild
                                ? _buildVariant.StatusText
                                : "Ready. Open a video to begin."
                            : "Bundled playback runtime is missing.");
                SetMediaSummary(string.IsNullOrWhiteSpace(_lastMediaErrorMessage)
                    ? GetSupportedVideoExtensionsDescription().Replace(", ", " | ")
                    : _lastMediaErrorMessage);
                CurrentFrameTextBlock.Text = "Frame --";
                CurrentFrameTextBlock.ToolTip = null;
                TimecodeTextBlock.Text = "Timestamp --:--:--.--- | Duration --:--:--.---";
                TimecodeTextBlock.ToolTip = null;
                FrameNumberTextBox.Text = string.Empty;
                FrameNumberTextBox.ToolTip = GetFrameNumberInputToolTip();
                KeyboardHintTextBlock.Text = _buildVariant.SupportsTimedPlayback
                    ? "Ctrl+O open   drag and drop supported   F11 full screen"
                    : "Ctrl+O open   seek and frame-step ready   F11 full screen";
                UpdateCacheStatusFromEngine();
                UpdateFullScreenButtonIcon();
                return;
            }

            if (_buildVariant.SupportsTimedPlayback)
            {
                SetPlaybackMessage(_isPlaying
                    ? "Playing. Pause to use Left or Right for frame stepping."
                    : "Paused. Press Play or Space to begin. Left and Right step a single frame.");
                KeyboardHintTextBlock.Text = _isPlaying
                    ? "Space pause   J/L seek   F11 full screen"
                    : "Left/Right frame step or hold   J/L seek   Space play   F11 full screen";
            }
            else
            {
                SetPlaybackMessage(_buildVariant.PlaybackCapabilityText);
                KeyboardHintTextBlock.Text = "Left/Right frame step or hold   J/L seek   F11 full screen";
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
                GetAudioTooltipText(_videoReviewEngine.MediaInfo));

            UpdateCurrentFrameDisplay(GetDisplayPosition());
            UpdateCacheStatusFromEngine();
            UpdateFullScreenButtonIcon();
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
                TimecodeTextBlock.Text = "Timestamp --:--:--.--- | Duration --:--:--.---";
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
                TimecodeTextBlock.Text = string.Format(CultureInfo.InvariantCulture, "Timestamp {0} | Duration {1}", FormatTime(currentPosition), FormatTime(_mediaDuration));
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
                "Timestamp {0} | Duration {1}",
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

            var enginePosition = _videoReviewEngine.Position;
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
            var ffmpegEngine = _videoReviewEngine as FfmpegReviewEngine;
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
            var ffmpegEngine = _videoReviewEngine as FfmpegReviewEngine;
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
            var ffmpegEngine = _videoReviewEngine as FfmpegReviewEngine;
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

        private void VideoReviewEngine_StateChanged(object sender, VideoReviewEngineStateChangedEventArgs e)
        {
            if (!Dispatcher.CheckAccess())
            {
                Dispatcher.BeginInvoke(new Action<object, VideoReviewEngineStateChangedEventArgs>(VideoReviewEngine_StateChanged), sender, e);
                return;
            }

            var wasMediaLoaded = _isMediaLoaded;
            if (!string.IsNullOrWhiteSpace(e.LastErrorMessage) && !e.IsMediaOpen)
            {
                EndHeldFrameStep();
                _lastMediaErrorMessage = SanitizeSensitiveText(e.LastErrorMessage);
                ResetMediaState(clearFilePath: false, clearErrorMessage: false);
                SetPlaybackMessage("Playback failed.");
                SetMediaSummary(_lastMediaErrorMessage);
                if (wasMediaLoaded)
                {
                    LogError("Playback failed: " + _lastMediaErrorMessage);
                }
                return;
            }

            _isMediaLoaded = e.IsMediaOpen;
            _isPlaying = e.IsPlaying;

            if (_isPlaying)
            {
                EndHeldFrameStep();
            }

            if (!_isMediaLoaded)
            {
                UpdateTransportState();
                return;
            }

            _currentFilePath = e.CurrentFilePath;
            _lastMediaErrorMessage = string.Empty;
            RefreshMediaMetricsFromEngine();
            UpdatePositionDisplay(e.Position.PresentationTime);
                UpdateTransportState();

            if (!wasMediaLoaded)
            {
                Activate();
                FocusPreferredVideoSurface();
            }
        }

        private void VideoReviewEngine_FramePresented(object sender, FramePresentedEventArgs e)
        {
            if (!Dispatcher.CheckAccess())
            {
                Dispatcher.BeginInvoke(new Action<object, FramePresentedEventArgs>(VideoReviewEngine_FramePresented), sender, e);
                return;
            }

            CustomVideoSurface.Source = e != null && e.Frame != null ? e.Frame.BitmapSource : null;
        }

        private void CustomVideoSurfaceHost_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
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

        private void UpdateEmptyState()
        {
            if (_isMediaLoaded)
            {
                EmptyStateTitleTextBlock.Text = string.Empty;
                EmptyStateBodyTextBlock.Text = string.Empty;
                return;
            }

            if (!App.HasBundledFfmpegRuntime)
            {
                EmptyStateTitleTextBlock.Text = "Playback runtime missing";
                EmptyStateBodyTextBlock.Text = GetRuntimeStatusMessage();
                return;
            }

            EmptyStateTitleTextBlock.Text = "Drop a video here";
            EmptyStateBodyTextBlock.Text = string.IsNullOrWhiteSpace(_lastMediaErrorMessage)
                ? _buildVariant.SupportsTimedPlayback
                    ? "Open a supported video file, or press Ctrl+O. When paused, Left and Right step one frame at a time."
                    : "Open a supported video file, or press Ctrl+O. Seek and frame stepping are available, but timed playback is not."
                : _lastMediaErrorMessage;
        }

        private void ResetMediaState(bool clearFilePath, bool clearErrorMessage)
        {
            _isMediaLoaded = false;
            _isPlaying = false;
            _isFrameStepInProgress = false;
            _isCacheStatusActive = false;
            _heldFrameStepDirection = 0;
            _frameStepRepeatTimer.Stop();
            _isSliderDragActive = false;
            _framesPerSecond = DefaultFramesPerSecond;
            _positionStep = TimeSpan.FromSeconds(1d / DefaultFramesPerSecond);
            _mediaDuration = TimeSpan.Zero;
            CustomVideoSurface.Source = null;

            if (clearFilePath)
            {
                _currentFilePath = string.Empty;
            }

            if (clearErrorMessage)
            {
                _lastMediaErrorMessage = string.Empty;
            }

            UpdateHeader();
            UpdatePositionDisplay(TimeSpan.Zero);
            UpdateTransportState();
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

            var ffmpegEngine = _videoReviewEngine as FfmpegReviewEngine;
            if (ffmpegEngine == null)
            {
                SetCacheStatus("Cache: unavailable", "Cache details are not available for this engine.", false);
                return;
            }

            var forwardCount = Math.Max(0, ffmpegEngine.ForwardCachedFrameCount);
            var previousCount = Math.Max(0, ffmpegEngine.PreviousCachedFrameCount);
            var approximateCacheMegabytes = ffmpegEngine.ApproximateCachedFrameBytes / 1048576d;
            var positionIdentity = _videoReviewEngine.Position != null && _videoReviewEngine.Position.IsFrameIndexAbsolute
                ? "absolute frame ready"
                : "frame number pending index";
            var message = string.Format(
                CultureInfo.InvariantCulture,
                ffmpegEngine.IsGlobalFrameIndexBuildInProgress
                    ? "Cache: indexing ({0} back / {1} ahead)"
                    : "Cache: ready ({0} back / {1} ahead)",
                previousCount,
                forwardCount);
            var tooltip = string.Format(
                CultureInfo.InvariantCulture,
                "Index: {0}. Frame identity: {1}. Review cache holds up to {2} prior and {3} forward decoded frames and is currently using about {4:0.0} MiB. Last refill: {5} ({6:0.0} ms, {7}). Timeline seeks show the landed frame first.",
                ffmpegEngine.GlobalFrameIndexStatus,
                positionIdentity,
                ffmpegEngine.MaxPreviousCachedFrameCount,
                ffmpegEngine.MaxForwardCachedFrameCount,
                approximateCacheMegabytes,
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
                var ffmpegEngine = _videoReviewEngine as FfmpegReviewEngine;

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
                    "Audio stream: " + (_videoReviewEngine.MediaInfo.HasAudioStream ? "Yes" : "No"),
                    "Audio playback available: " + (_videoReviewEngine.MediaInfo.IsAudioPlaybackAvailable ? "Yes" : "No"),
                    "Audio codec: " + (string.IsNullOrWhiteSpace(_videoReviewEngine.MediaInfo.AudioCodecName) ? "(none)" : _videoReviewEngine.MediaInfo.AudioCodecName),
                    "Audio details: " + GetAudioTooltipText(_videoReviewEngine.MediaInfo),
                    "Frame index status: " + (ffmpegEngine != null ? ffmpegEngine.GlobalFrameIndexStatus : "(unavailable)"),
                    "Frame index available: " + (ffmpegEngine != null && ffmpegEngine.IsGlobalFrameIndexAvailable ? "Yes" : "No"),
                    "Indexed frame count: " + (ffmpegEngine != null ? ffmpegEngine.IndexedFrameCount.ToString(CultureInfo.InvariantCulture) : "(unavailable)"),
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
            var enginePosition = _videoReviewEngine.Position;
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
            var ffmpegEngine = _videoReviewEngine as FfmpegReviewEngine;
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

            return _videoReviewEngine.Position.PresentationTime;
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
