using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.Win32;
using Rpcs3VideoPlayer.Services;
using Unosquare.FFME.Common;

namespace Rpcs3VideoPlayer
{
    public partial class MainWindow : Window
    {
        private const double DefaultFramesPerSecond = 30.0;
        private static readonly TimeSpan SeekJump = TimeSpan.FromSeconds(5);
        private static readonly TimeSpan FrameStepInitialDelay = TimeSpan.FromMilliseconds(550);
        private static readonly TimeSpan FrameStepRepeatInterval = TimeSpan.FromMilliseconds(60);

        private readonly DispatcherTimer _positionTimer;
        private readonly DispatcherTimer _frameStepRepeatTimer;
        private readonly DiagnosticLogService _diagnosticLogService;
        private readonly RecentFilesService _recentFilesService;

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

        public MainWindow()
        {
            InitializeComponent();

            _diagnosticLogService = new DiagnosticLogService();
            _recentFilesService = new RecentFilesService();
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
                "Session started. Version {0}. Runtime available: {1}. Runtime source: {2}.",
                GetApplicationVersion(),
                App.HasBundledFfmpegRuntime ? "Yes" : "No",
                App.HasBundledFfmpegRuntime
                    ? "Bundled runtime verified."
                    : GetRuntimeStatusMessage()));
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
                Filter = "Supported Video Files|*.avi;*.mov;*.m4v;*.mp4|AVI Files|*.avi|MOV Files|*.mov|M4V Files|*.m4v|MP4 Files|*.mp4|All Files|*.*"
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

        private async void PlayButton_Click(object sender, RoutedEventArgs e)
        {
            if (!_isMediaLoaded)
            {
                return;
            }

            var playing = await Media.Play();
            _isPlaying = playing;
            if (!playing)
            {
                _lastMediaErrorMessage = "Playback did not start.";
                SetMediaSummary(_lastMediaErrorMessage);
                LogWarning("Playback did not start.");
            }
            else
            {
                LogInfo("Playback started at " + FormatTime(GetDisplayPosition()) + ".");
            }

            UpdateTransportState();
        }

        private async void PauseButton_Click(object sender, RoutedEventArgs e)
        {
            await PausePlaybackAsync();
        }

        private void RewindButton_Click(object sender, RoutedEventArgs e)
        {
            SeekRelative(-SeekJump);
        }

        private void FastForwardButton_Click(object sender, RoutedEventArgs e)
        {
            SeekRelative(SeekJump);
        }

        private async void PreviousFrameButton_Click(object sender, RoutedEventArgs e)
        {
            await StepFrameAsync(-1);
        }

        private async void NextFrameButton_Click(object sender, RoutedEventArgs e)
        {
            await StepFrameAsync(1);
        }

        private async void JumpToFrameButton_Click(object sender, RoutedEventArgs e)
        {
            await JumpToFrameFromInputAsync();
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

        private void PositionSlider_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            _isSliderDragActive = true;
        }

        private void PositionSlider_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (!_isMediaLoaded)
            {
                _isSliderDragActive = false;
                return;
            }

            _isSliderDragActive = false;
            SeekTo(TimeSpan.FromSeconds(PositionSlider.Value));
        }

        private void PositionSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_suppressSliderUpdate || !_isSliderDragActive)
            {
                return;
            }

            var previewPosition = TimeSpan.FromSeconds(PositionSlider.Value);
            CurrentPositionTextBlock.Text = FormatTime(previewPosition);
            UpdateCurrentFrameDisplay(previewPosition);
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
                MessageBox.Show(this, "Drop an AVI, MOV, M4V, or MP4 file.", "Unsupported File", MessageBoxButton.OK, MessageBoxImage.Information);
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

        private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (Keyboard.Modifiers == ModifierKeys.Control && e.Key == Key.O)
            {
                OpenFileButton_Click(sender, e);
                e.Handled = true;
                return;
            }

            if (Keyboard.Modifiers == ModifierKeys.Control && e.Key == Key.W)
            {
                _ = CloseMediaAsync();
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
                    _ = TogglePlaybackAsync();
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
                    SeekRelative(-SeekJump);
                    e.Handled = true;
                    break;
                case Key.L:
                    SeekRelative(SeekJump);
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
            Media.Dispose();
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
                MessageBox.Show(this, "Supported file types are AVI, MOV, M4V, and MP4.", "Unsupported File", MessageBoxButton.OK, MessageBoxImage.Information);
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
                    await Media.Close();
                }

                var opened = await Media.Open(new Uri(filePath));
                if (!opened)
                {
                    throw new InvalidOperationException("The bundled playback runtime could not open this video.");
                }

                _currentFilePath = filePath;
                _isMediaLoaded = true;
                _isPlaying = false;

                await Media.Pause();
                RefreshMediaMetricsFromElement();
                await Media.Seek(TimeSpan.Zero);

                _recentFilesService.Add(filePath);
                RefreshRecentFilesMenu();
                UpdateHeader();
                UpdatePositionDisplay(GetDisplayPosition());
                UpdateTransportState();
                Activate();
                Focus();
                Keyboard.Focus(Media);

                LogInfo(string.Format(
                    CultureInfo.InvariantCulture,
                    "Media opened: {0} | FPS {1:0.###} | Step {2} | Duration {3}.",
                    GetSafeFileDisplay(filePath),
                    _framesPerSecond,
                    FormatStepDuration(_positionStep),
                    FormatTime(_mediaDuration)));
            }
            catch (Exception ex)
            {
                _lastMediaErrorMessage = SanitizeSensitiveText(ex.Message);
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

            if (_isPlaying)
            {
                await PausePlaybackAsync();
                return;
            }

            var playing = await Media.Play();
            _isPlaying = playing;
            if (!playing)
            {
                _lastMediaErrorMessage = "Playback did not start.";
                SetMediaSummary(_lastMediaErrorMessage);
                LogWarning("Playback did not start.");
            }
            else
            {
                LogInfo("Playback started at " + FormatTime(GetDisplayPosition()) + ".");
            }

            UpdateTransportState();
        }

        private async Task CloseMediaAsync()
        {
            EndHeldFrameStep();

            if (_isMediaLoaded)
            {
                LogInfo("Closing media: " + GetSafeFileDisplay(_currentFilePath));
                await Media.Close();
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
            await Media.Pause();
            _isPlaying = false;
            UpdateTransportState();

            if (logAction && wasPlaying)
            {
                LogInfo("Playback paused at " + FormatTime(positionBeforePause) + ".");
            }
        }

        private void SeekRelative(TimeSpan offset)
        {
            EndHeldFrameStep();

            if (!_isMediaLoaded)
            {
                return;
            }

            var target = ClampPosition(Media.Position + offset);
            Media.Position = target;
            UpdatePositionDisplay(target);
            LogInfo(string.Format(
                CultureInfo.InvariantCulture,
                "Seeked {0}{1} to {2}.",
                offset < TimeSpan.Zero ? "-" : "+",
                FormatTime(offset.Duration()),
                FormatTime(target)));
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

                bool stepped;
                if (delta < 0)
                {
                    stepped = await Media.StepBackward();
                }
                else
                {
                    stepped = await Media.StepForward();
                }

                if (!stepped)
                {
                    SetPlaybackMessage("Frame step did not advance. This media may not support precise stepping at the current position.");
                    LogWarning("Frame step did not advance.");
                }

                UpdatePositionDisplay(GetDisplayPosition());
            }
            finally
            {
                _isFrameStepInProgress = false;
            }
        }

        private void SeekTo(TimeSpan target)
        {
            EndHeldFrameStep();

            if (!_isMediaLoaded)
            {
                return;
            }

            var clampedTarget = ClampPosition(target);
            Media.Position = clampedTarget;
            UpdatePositionDisplay(clampedTarget);
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

            requestedFrameNumber = Math.Max(1L, requestedFrameNumber);
            var maxFrameIndex = GetMaxFrameIndex();
            var targetFrameIndex = requestedFrameNumber - 1L;
            if (maxFrameIndex >= 0 && targetFrameIndex > maxFrameIndex)
            {
                targetFrameIndex = maxFrameIndex;
            }

            await PausePlaybackAsync();

            var targetPosition = GetFramePosition(targetFrameIndex);
            Media.Position = ClampPosition(targetPosition);
            UpdatePositionDisplay(GetDisplayPosition());
            LogInfo(string.Format(
                CultureInfo.InvariantCulture,
                "Jumped to frame {0} at {1}.",
                targetFrameIndex + 1L,
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

        private void RefreshMediaMetricsFromElement()
        {
            _positionStep = Media.PositionStep > TimeSpan.Zero
                ? Media.PositionStep
                : TimeSpan.FromSeconds(1d / DefaultFramesPerSecond);
            _framesPerSecond = _positionStep > TimeSpan.Zero
                ? 1.0 / _positionStep.TotalSeconds
                : DefaultFramesPerSecond;

            if (Media.NaturalDuration.HasValue && Media.NaturalDuration.Value > TimeSpan.Zero)
            {
                _mediaDuration = Media.NaturalDuration.Value;
            }
            else if (Media.MediaInfo != null && Media.MediaInfo.Duration > TimeSpan.Zero)
            {
                _mediaDuration = Media.MediaInfo.Duration;
            }
            else
            {
                _mediaDuration = TimeSpan.Zero;
            }
        }

        private void UpdateHeader()
        {
            if (string.IsNullOrWhiteSpace(_currentFilePath))
            {
                CurrentFileTextBlock.Text = "No file loaded";
                CurrentFileTextBlock.ToolTip = null;
                Title = "Frame Player";
            }
            else
            {
                CurrentFileTextBlock.Text = Path.GetFileName(_currentFilePath);
                CurrentFileTextBlock.ToolTip = _currentFilePath;
                Title = "Frame Player - " + Path.GetFileName(_currentFilePath);
            }
        }

        private void UpdateTransportState()
        {
            var canControl = _isMediaLoaded;

            PlayButton.IsEnabled = canControl && !_isPlaying;
            PauseButton.IsEnabled = canControl && _isPlaying;
            RewindButton.IsEnabled = canControl;
            FastForwardButton.IsEnabled = canControl;
            PreviousFrameButton.IsEnabled = canControl;
            NextFrameButton.IsEnabled = canControl;
            CloseVideoMenuItem.IsEnabled = canControl;
            PositionSlider.IsEnabled = canControl;
            FrameNumberTextBox.IsEnabled = canControl;
            JumpToFrameButton.IsEnabled = canControl;
            ToggleFullScreenButton.IsEnabled = canControl;
            OverlayToggleFullScreenButton.IsEnabled = canControl;
            FullscreenControlBar.IsEnabled = canControl;
            EmptyStateOverlay.Visibility = canControl ? Visibility.Collapsed : Visibility.Visible;
            UpdateEmptyState();

            if (!_isMediaLoaded)
            {
                SetPlaybackMessage(
                    !string.IsNullOrWhiteSpace(_lastMediaErrorMessage)
                        ? "The last action did not complete."
                        : App.HasBundledFfmpegRuntime
                            ? "Ready. Open a video to begin."
                            : "Bundled playback runtime is missing.");
                SetMediaSummary(string.IsNullOrWhiteSpace(_lastMediaErrorMessage)
                    ? "AVI | MOV | M4V | MP4"
                    : _lastMediaErrorMessage);
                CurrentFrameTextBlock.Text = "Frame --";
                CurrentFrameTextBlock.ToolTip = null;
                TimecodeTextBlock.Text = "TC --:--:--:-- | --:--:--.---";
                TimecodeTextBlock.ToolTip = null;
                TotalFramesTextBlock.Text = "--";
                TotalFramesTextBlock.ToolTip = null;
                FrameNumberTextBox.Text = string.Empty;
                FrameNumberTextBox.ToolTip = "Type a frame number and press Enter.";
                KeyboardHintTextBlock.Text = "Ctrl+O open   drag and drop supported   F11 full screen";
                UpdateFullScreenButtonIcon();
                return;
            }

            SetPlaybackMessage(_isPlaying
                ? "Playing. Pause to use Left or Right for frame stepping."
                : "Paused. Press Play or Space to begin. Left and Right step a single frame.");
            KeyboardHintTextBlock.Text = _isPlaying
                ? "Space pause   J/L seek   F11 full screen"
                : "Left/Right frame step or hold   J/L seek   Space play   F11 full screen";

            SetMediaSummary(string.Format(
                CultureInfo.InvariantCulture,
                "FPS: {0:0.###} | Step: {1} | Duration: {2}",
                _framesPerSecond,
                FormatStepDuration(_positionStep),
                FormatTime(_mediaDuration)));
            MediaSummaryTextBlock.ToolTip = string.Format(
                CultureInfo.InvariantCulture,
                "Frame rate {0:0.###} fps, frame step {1}, duration {2}.",
                _framesPerSecond,
                FormatStepDuration(_positionStep),
                FormatTime(_mediaDuration));

            UpdateCurrentFrameDisplay(GetDisplayPosition());
            UpdateFullScreenButtonIcon();
        }

        private void UpdatePositionDisplay(TimeSpan currentPosition)
        {
            CurrentPositionTextBlock.Text = FormatTime(currentPosition);
            DurationTextBlock.Text = FormatTime(_mediaDuration);

            _suppressSliderUpdate = true;
            try
            {
                PositionSlider.Maximum = _mediaDuration > TimeSpan.Zero ? _mediaDuration.TotalSeconds : 1.0;
                PositionSlider.Value = Math.Min(currentPosition.TotalSeconds, PositionSlider.Maximum);
            }
            finally
            {
                _suppressSliderUpdate = false;
            }

            if (_isMediaLoaded)
            {
                UpdateCurrentFrameDisplay(currentPosition);

                if (!FrameNumberTextBox.IsKeyboardFocusWithin)
                {
                    FrameNumberTextBox.Text = (GetNearestFrameIndex(currentPosition) + 1L).ToString(CultureInfo.InvariantCulture);
                }
            }
        }

        private void UpdateCurrentFrameDisplay(TimeSpan currentPosition)
        {
            if (!_isMediaLoaded)
            {
                CurrentFrameTextBlock.Text = "Frame --";
                CurrentFrameTextBlock.ToolTip = null;
                TimecodeTextBlock.Text = "TC --:--:--:-- | --:--:--.---";
                TimecodeTextBlock.ToolTip = null;
                TotalFramesTextBlock.Text = "--";
                TotalFramesTextBlock.ToolTip = null;
                FrameNumberTextBox.ToolTip = "Type a frame number and press Enter.";
                return;
            }

            var currentFrameIndex = GetNearestFrameIndex(currentPosition);
            var currentFrame = currentFrameIndex + 1L;
            var totalFrames = GetMaxFrameIndex() + 1L;
            var frameDigits = Math.Max(
                4,
                totalFrames > 0
                    ? totalFrames.ToString(CultureInfo.InvariantCulture).Length
                    : currentFrame.ToString(CultureInfo.InvariantCulture).Length);
            var frameFormat = "D" + frameDigits.ToString(CultureInfo.InvariantCulture);
            var timecode = FormatTimecode(currentFrameIndex);

            CurrentFrameTextBlock.Text = totalFrames > 0
                ? string.Format(
                    CultureInfo.InvariantCulture,
                    "Frame {0} / {1}",
                    currentFrame.ToString(frameFormat, CultureInfo.InvariantCulture),
                    totalFrames.ToString(frameFormat, CultureInfo.InvariantCulture))
                : string.Format(
                    CultureInfo.InvariantCulture,
                    "Frame {0}",
                    currentFrame.ToString(frameFormat, CultureInfo.InvariantCulture));
            CurrentFrameTextBlock.ToolTip = totalFrames > 0
                ? string.Format(CultureInfo.InvariantCulture, "Current frame {0} of {1}.", currentFrame, totalFrames)
                : string.Format(CultureInfo.InvariantCulture, "Current frame {0}.", currentFrame);

            TotalFramesTextBlock.Text = totalFrames > 0
                ? totalFrames.ToString(frameFormat, CultureInfo.InvariantCulture)
                : "--";
            TotalFramesTextBlock.ToolTip = totalFrames > 0
                ? string.Format(CultureInfo.InvariantCulture, "Total available frames: {0}.", totalFrames)
                : "Total available frames are not known.";
            FrameNumberTextBox.ToolTip = totalFrames > 0
                ? string.Format(CultureInfo.InvariantCulture, "Current / total frames: {0} / {1}. Type a frame number and press Enter.", currentFrame, totalFrames)
                : string.Format(CultureInfo.InvariantCulture, "Current frame: {0}. Type a frame number and press Enter.", currentFrame);

            TimecodeTextBlock.Text = string.Format(
                CultureInfo.InvariantCulture,
                "TC {0} | {1}",
                timecode,
                FormatTime(currentPosition));
            TimecodeTextBlock.ToolTip = string.Format(
                CultureInfo.InvariantCulture,
                "Frame-derived timecode using nominal {0} fps buckets for {1:0.###} fps media.",
                GetNominalTimecodeFramesPerSecond(),
                _framesPerSecond);
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

        private long GetNearestFrameIndex(TimeSpan position)
        {
            if (_positionStep <= TimeSpan.Zero)
            {
                return 0L;
            }

            return Math.Max(0L, position.Ticks / _positionStep.Ticks);
        }

        private long GetMaxFrameIndex()
        {
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
                || extension.Equals(".mp4", StringComparison.OrdinalIgnoreCase);
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

        private void Media_MediaOpened(object sender, MediaOpenedEventArgs e)
        {
            RefreshMediaMetricsFromElement();
            UpdatePositionDisplay(GetDisplayPosition());
            UpdateTransportState();
            Activate();
            Keyboard.Focus(Media);
        }

        private void Media_MediaFailed(object sender, MediaFailedEventArgs e)
        {
            EndHeldFrameStep();
            _lastMediaErrorMessage = e.ErrorException != null
                ? SanitizeSensitiveText(e.ErrorException.Message)
                : "An unknown media error occurred.";
            ResetMediaState(clearFilePath: false, clearErrorMessage: false);
            SetPlaybackMessage("Playback failed.");
            SetMediaSummary(_lastMediaErrorMessage);
            LogError("Playback failed: " + _lastMediaErrorMessage);
        }

        private void Media_MediaStateChanged(object sender, MediaStateChangedEventArgs e)
        {
            _isPlaying = Media.IsPlaying;
            if (_isPlaying)
            {
                EndHeldFrameStep();
            }

            UpdateTransportState();
        }

        private void Media_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (_isMediaLoaded)
            {
                ToggleFullScreen();
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
                ? "Open an AVI, MOV, M4V, or MP4 file, or press Ctrl+O. When paused, Left and Right step one frame at a time."
                : _lastMediaErrorMessage;
        }

        private void ResetMediaState(bool clearFilePath, bool clearErrorMessage)
        {
            _isMediaLoaded = false;
            _isPlaying = false;
            _isFrameStepInProgress = false;
            _heldFrameStepDirection = 0;
            _frameStepRepeatTimer.Stop();
            _isSliderDragActive = false;
            _framesPerSecond = DefaultFramesPerSecond;
            _positionStep = TimeSpan.FromSeconds(1d / DefaultFramesPerSecond);
            _mediaDuration = TimeSpan.Zero;

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
                var currentFrameIndex = _isMediaLoaded ? GetNearestFrameIndex(currentPosition) : -1L;
                var totalFrames = GetMaxFrameIndex();

                var report = _diagnosticLogService.BuildReport(new[]
                {
                    "Frame Player Diagnostics",
                    string.Format(CultureInfo.InvariantCulture, "Generated: {0:yyyy-MM-dd HH:mm:ss.fff zzz}", DateTime.Now),
                    string.Format(CultureInfo.InvariantCulture, "Session started: {0:yyyy-MM-dd HH:mm:ss.fff zzz}", _diagnosticLogService.SessionStarted),
                    "Version: " + GetApplicationVersion(),
                    "OS: " + Environment.OSVersion,
                    ".NET: " + Environment.Version,
                    "Runtime available: " + (App.HasBundledFfmpegRuntime ? "Yes" : "No"),
                    "Runtime status: " + GetRuntimeStatusMessage(),
                    "Latest session log: " + GetSafeFileDisplay(_diagnosticLogService.LatestLogPath),
                    "Current file: " + GetSafeFileDisplay(_currentFilePath),
                    "Media loaded: " + (_isMediaLoaded ? "Yes" : "No"),
                    "Playback state: " + (_isPlaying ? "Playing" : "Paused/Idle"),
                    "Full screen: " + (_isFullScreen ? "Yes" : "No"),
                    string.Format(CultureInfo.InvariantCulture, "Clock position: {0} / {1}", FormatTime(currentPosition), FormatTime(_mediaDuration)),
                    string.Format(CultureInfo.InvariantCulture, "Timecode: {0}", _isMediaLoaded ? FormatTimecode(currentFrameIndex) : "--:--:--:--"),
                    string.Format(
                        CultureInfo.InvariantCulture,
                        "Frame: {0}{1}",
                        _isMediaLoaded ? (currentFrameIndex + 1L).ToString(CultureInfo.InvariantCulture) : "--",
                        totalFrames >= 0 ? " / " + (totalFrames + 1L).ToString(CultureInfo.InvariantCulture) : string.Empty),
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
            var version = Assembly.GetExecutingAssembly().GetName().Version;
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

        private TimeSpan GetDisplayPosition()
        {
            if (!_isMediaLoaded)
            {
                return TimeSpan.Zero;
            }

            return _isPlaying ? Media.Position : Media.FramePosition;
        }

        private TimeSpan GetFramePosition(long frameIndex)
        {
            if (frameIndex <= 0 || _positionStep <= TimeSpan.Zero)
            {
                return TimeSpan.Zero;
            }

            return TimeSpan.FromTicks(frameIndex * _positionStep.Ticks);
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
            LogInfo("Held frame stepping started: " + (_heldFrameStepDirection < 0 ? "backward" : "forward") + ".");
            _ = StepFrameAsync(_heldFrameStepDirection);
        }

        private void EndHeldFrameStep()
        {
            var hadActiveStep = _heldFrameStepDirection != 0;
            _heldFrameStepDirection = 0;
            _frameStepRepeatTimer.Stop();
            _frameStepRepeatTimer.Interval = FrameStepInitialDelay;

            if (hadActiveStep)
            {
                LogInfo("Held frame stepping stopped.");
            }
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
