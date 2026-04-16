using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FramePlayer.Core.Abstractions;
using FramePlayer.Core.Coordination;
using FramePlayer.Core.Models;

namespace FramePlayer.Core.Hosting
{
    public sealed class ReviewWorkspaceHostController : IDisposable
    {
        private readonly ReviewWorkspaceCoordinator _workspaceCoordinator;
        private readonly IRecentFilesCatalog _recentFilesCatalog;
        private ReviewHostCapabilities _capabilities;
        private string _lastMediaErrorMessage;
        private string _startupOpenFilePath;

        public ReviewWorkspaceHostController(
            ReviewWorkspaceCoordinator workspaceCoordinator,
            ReviewHostCapabilities capabilities = null,
            IRecentFilesCatalog recentFilesCatalog = null)
        {
            _workspaceCoordinator = workspaceCoordinator ?? throw new ArgumentNullException(nameof(workspaceCoordinator));
            _capabilities = capabilities ?? ReviewHostCapabilities.Default;
            _recentFilesCatalog = recentFilesCatalog;
            _workspaceCoordinator.WorkspaceChanged += WorkspaceCoordinator_WorkspaceChanged;
            _workspaceCoordinator.PreparationStateChanged += WorkspaceCoordinator_PreparationStateChanged;
            CurrentViewState = BuildViewState();
        }

        public ReviewWorkspaceViewState CurrentViewState { get; private set; }

        public ReviewHostCapabilities Capabilities
        {
            get { return _capabilities; }
        }

        public event EventHandler<ReviewWorkspaceViewStateChangedEventArgs> ViewStateChanged;

        public void Dispose()
        {
            _workspaceCoordinator.WorkspaceChanged -= WorkspaceCoordinator_WorkspaceChanged;
            _workspaceCoordinator.PreparationStateChanged -= WorkspaceCoordinator_PreparationStateChanged;
        }

        public void UpdateCapabilities(ReviewHostCapabilities capabilities)
        {
            _capabilities = capabilities ?? ReviewHostCapabilities.Default;
            Refresh();
        }

        public void SetLastMediaErrorMessage(string message)
        {
            _lastMediaErrorMessage = message ?? string.Empty;
            Refresh();
        }

        public ReviewWorkspaceViewState Refresh()
        {
            var previous = CurrentViewState ?? ReviewWorkspaceViewState.Empty;
            var current = BuildViewState();
            CurrentViewState = current;
            ViewStateChanged?.Invoke(this, new ReviewWorkspaceViewStateChangedEventArgs(previous, current));
            return current;
        }

        public Task OpenAsync(string filePath, CancellationToken cancellationToken = default(CancellationToken))
        {
            return OpenInternalAsync(filePath, cancellationToken);
        }

        public Task CloseAsync()
        {
            return _workspaceCoordinator.CloseAsync();
        }

        public Task PlayAsync(SynchronizedOperationScope operationScope = SynchronizedOperationScope.FocusedPane)
        {
            return _workspaceCoordinator.PlayAsync(operationScope);
        }

        public Task PauseAsync(SynchronizedOperationScope operationScope = SynchronizedOperationScope.FocusedPane)
        {
            return _workspaceCoordinator.PauseAsync(operationScope);
        }

        public Task<FrameStepResult> StepForwardAsync(
            SynchronizedOperationScope operationScope = SynchronizedOperationScope.FocusedPane,
            CancellationToken cancellationToken = default(CancellationToken))
        {
            return _workspaceCoordinator.StepForwardAsync(operationScope, cancellationToken);
        }

        public Task<FrameStepResult> StepBackwardAsync(
            SynchronizedOperationScope operationScope = SynchronizedOperationScope.FocusedPane,
            CancellationToken cancellationToken = default(CancellationToken))
        {
            return _workspaceCoordinator.StepBackwardAsync(operationScope, cancellationToken);
        }

        public Task SeekToTimeAsync(
            TimeSpan position,
            SynchronizedOperationScope operationScope = SynchronizedOperationScope.FocusedPane,
            CancellationToken cancellationToken = default(CancellationToken))
        {
            return _workspaceCoordinator.SeekToTimeAsync(position, operationScope, cancellationToken);
        }

        public Task SeekToFrameAsync(
            long frameIndex,
            SynchronizedOperationScope operationScope = SynchronizedOperationScope.FocusedPane,
            CancellationToken cancellationToken = default(CancellationToken))
        {
            return _workspaceCoordinator.SeekToFrameAsync(frameIndex, operationScope, cancellationToken);
        }

        public LoopPlaybackRangeSnapshot SetSharedLoopMarker(
            LoopPlaybackMarkerEndpoint endpoint,
            SynchronizedOperationScope operationScope)
        {
            var range = _workspaceCoordinator.SetSharedLoopMarker(endpoint, operationScope);
            Refresh();
            return range;
        }

        public void ClearSharedLoopRange()
        {
            _workspaceCoordinator.ClearSharedLoopRange();
            Refresh();
        }

        public LoopPlaybackPaneRangeSnapshot SetPaneLoopMarker(string paneId, LoopPlaybackMarkerEndpoint endpoint)
        {
            var range = _workspaceCoordinator.SetPaneLoopMarker(paneId, endpoint);
            Refresh();
            return range;
        }

        public void ClearPaneLoopRange(string paneId)
        {
            _workspaceCoordinator.ClearPaneLoopRange(paneId);
            Refresh();
        }

        public void SetStartupOpenFilePath(string filePath)
        {
            _startupOpenFilePath = NormalizeExistingPath(filePath);
        }

        public bool TryConsumeStartupOpenFilePath(out string filePath)
        {
            filePath = NormalizeExistingPath(_startupOpenFilePath);
            _startupOpenFilePath = string.Empty;
            return !string.IsNullOrWhiteSpace(filePath);
        }

        public void RemoveRecentFile(string filePath)
        {
            if (_recentFilesCatalog == null)
            {
                return;
            }

            _recentFilesCatalog.Remove(filePath);
            Refresh();
        }

        public void ClearRecentFiles()
        {
            if (_recentFilesCatalog == null)
            {
                return;
            }

            _recentFilesCatalog.Clear();
            Refresh();
        }

        private void WorkspaceCoordinator_WorkspaceChanged(object sender, Events.ReviewWorkspaceChangedEventArgs e)
        {
            Refresh();
        }

        private void WorkspaceCoordinator_PreparationStateChanged(object sender, Events.ReviewWorkspacePreparationChangedEventArgs e)
        {
            Refresh();
        }

        private ReviewWorkspaceViewState BuildViewState()
        {
            var workspaceState = _workspaceCoordinator.CurrentWorkspace ?? MultiVideoWorkspaceState.Empty;
            var workspaceSnapshot = _workspaceCoordinator.GetWorkspaceSnapshot();
            var session = _workspaceCoordinator.CurrentSession ?? ReviewSessionSnapshot.Empty;
            var focusedPane = workspaceState.FocusedPane ?? workspaceState.ActivePane ?? workspaceState.PrimaryPane;
            var canControl = session.IsMediaOpen;
            var canTogglePlayPause = canControl && _capabilities.SupportsTimedPlayback;

            var transportState = new TransportCommandState
            {
                CanControlTransport = canControl,
                CanTogglePlayPause = canTogglePlayPause,
                CanStepBackward = canControl,
                CanStepForward = canControl,
                CanSeek = canControl,
                CanCloseMedia = canControl,
                CanInspectMedia = canControl,
                IsPlaying = session.PlaybackState == ReviewPlaybackState.Playing
            };

            var focusedLoopRange = focusedPane != null && focusedPane.LoopRange != null
                ? focusedPane.LoopRange
                : ResolveSharedPaneLoopRange(workspaceSnapshot);
            var loopState = BuildLoopState(focusedLoopRange, canControl);
            var exportState = BuildExportState(loopState);
            var recentFilesState = BuildRecentFilesState();

            var panes = workspaceState.Panes
                .Select(BuildPaneState)
                .ToArray();

            return new ReviewWorkspaceViewState
            {
                PrimaryPaneId = workspaceSnapshot.PrimaryPaneId ?? string.Empty,
                ActivePaneId = workspaceSnapshot.ActivePaneId ?? string.Empty,
                FocusedPaneId = workspaceSnapshot.FocusedPaneId ?? string.Empty,
                CurrentFilePath = session.CurrentFilePath ?? string.Empty,
                PlaybackMessage = BuildPlaybackMessage(session),
                MediaSummary = BuildMediaSummary(session),
                Transport = transportState,
                Loop = loopState,
                Export = exportState,
                RecentFiles = recentFilesState,
                Panes = panes
            };
        }

        private static PaneViewState BuildPaneState(ReviewPaneState pane)
        {
            var session = pane != null ? pane.Session ?? ReviewSessionSnapshot.Empty : ReviewSessionSnapshot.Empty;
            var loopState = BuildLoopState(pane != null ? pane.LoopRange : null, session.IsMediaOpen);
            return new PaneViewState
            {
                PaneId = pane != null ? pane.PaneId ?? string.Empty : string.Empty,
                DisplayLabel = pane != null ? pane.DisplayLabel ?? string.Empty : string.Empty,
                IsPrimary = pane != null && pane.IsPrimary,
                IsActive = pane != null && pane.IsActive,
                IsFocused = pane != null && pane.IsFocused,
                IsMediaOpen = session.IsMediaOpen,
                CurrentFilePath = session.CurrentFilePath ?? string.Empty,
                PlaybackState = session.PlaybackState,
                CurrentPosition = session.Position.PresentationTime < TimeSpan.Zero
                    ? TimeSpan.Zero
                    : session.Position.PresentationTime,
                Duration = session.MediaInfo.Duration > TimeSpan.Zero
                    ? session.MediaInfo.Duration
                    : TimeSpan.Zero,
                FrameIndex = session.Position.FrameIndex,
                IsFrameIndexAbsolute = session.Position.IsFrameIndexAbsolute,
                Loop = loopState
            };
        }

        private ExportCommandState BuildExportState(LoopCommandState loopState)
        {
            if (!_capabilities.ExportToolingAvailable)
            {
                return new ExportCommandState(
                    false,
                    false,
                    string.IsNullOrWhiteSpace(_capabilities.ExportToolingStatusText)
                        ? "Clip export tooling is unavailable."
                        : _capabilities.ExportToolingStatusText);
            }

            if (loopState == null || !loopState.HasReadyRange)
            {
                return new ExportCommandState(
                    true,
                    false,
                    "Set exact A/B markers before exporting a reviewed clip.");
            }

            return new ExportCommandState(true, true, "The current loop is ready to export.");
        }

        private RecentFilesCommandState BuildRecentFilesState()
        {
            if (_recentFilesCatalog == null)
            {
                return RecentFilesCommandState.Empty;
            }

            var entries = _recentFilesCatalog.Load()
                .Select(NormalizePath)
                .Where(path => !string.IsNullOrWhiteSpace(path))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Select(
                    (path, index) => new RecentFileViewState(
                        path,
                        string.Format(
                            CultureInfo.InvariantCulture,
                            "_{0} {1}",
                            index + 1,
                            Path.GetFileName(path)),
                        path,
                        File.Exists(path)))
                .ToArray();

            return entries.Length == 0
                ? RecentFilesCommandState.Empty
                : new RecentFilesCommandState(entries, true, "Recent files are ready.");
        }

        private static LoopCommandState BuildLoopState(LoopPlaybackPaneRangeSnapshot loopRange, bool canControl)
        {
            if (loopRange == null || !loopRange.HasAnyMarkers)
            {
                return new LoopCommandState
                {
                    CanSetMarkers = canControl
                };
            }

            if (loopRange.IsInvalidRange)
            {
                return new LoopCommandState
                {
                    CanSetMarkers = canControl,
                    CanClearMarkers = true,
                    HasAnyMarkers = true,
                    HasPendingMarkers = loopRange.HasPendingMarkers,
                    IsInvalidRange = true,
                    StatusText = "Loop: invalid",
                    ToolTip = "Loop-out currently lands before loop-in."
                };
            }

            if (loopRange.HasPendingMarkers)
            {
                return new LoopCommandState
                {
                    CanSetMarkers = canControl,
                    CanClearMarkers = true,
                    HasAnyMarkers = true,
                    HasPendingMarkers = true,
                    StatusText = string.Format(
                        CultureInfo.InvariantCulture,
                        "Loop: pending ({0} -> {1})",
                        FormatTime(loopRange.EffectiveStartTime),
                        FormatTime(loopRange.EffectiveEndTime)),
                    ToolTip = "Loop markers are time-bounded now and will upgrade to exact frame identity when indexing completes."
                };
            }

            return new LoopCommandState
            {
                CanSetMarkers = canControl,
                CanClearMarkers = true,
                HasAnyMarkers = true,
                HasReadyRange = loopRange.HasLoopIn && loopRange.HasLoopOut,
                StatusText = string.Format(
                    CultureInfo.InvariantCulture,
                    "Loop: {0} -> {1}",
                    FormatTime(loopRange.EffectiveStartTime),
                    FormatTime(loopRange.EffectiveEndTime)),
                ToolTip = "Loop markers are ready for exact reviewed playback and export."
            };
        }

        private string BuildPlaybackMessage(ReviewSessionSnapshot session)
        {
            if (session == null || !session.IsMediaOpen)
            {
                if (!string.IsNullOrWhiteSpace(_lastMediaErrorMessage))
                {
                    return "The last action did not complete.";
                }

                return _capabilities.HasBundledRuntime
                    ? _capabilities.IdleStatusText
                    : _capabilities.RuntimeMissingStatusText;
            }

            if (!_capabilities.SupportsTimedPlayback)
            {
                return _capabilities.TimedPlaybackCapabilityText;
            }

            return session.PlaybackState == ReviewPlaybackState.Playing
                ? "Playing"
                : "Paused";
        }

        private static string BuildMediaSummary(ReviewSessionSnapshot session)
        {
            if (session == null || !session.IsMediaOpen)
            {
                return string.Empty;
            }

            var framesPerSecond = session.MediaInfo.FramesPerSecond > 0d
                ? session.MediaInfo.FramesPerSecond
                : 0d;
            var positionStep = session.MediaInfo.PositionStep;
            return framesPerSecond > 0d && positionStep > TimeSpan.Zero
                ? string.Format(
                    CultureInfo.InvariantCulture,
                    "Sample rate {0:0.###} fps | Interval {1}",
                    framesPerSecond,
                    FormatStepDuration(positionStep))
                : string.Empty;
        }

        private static LoopPlaybackPaneRangeSnapshot ResolveSharedPaneLoopRange(ReviewWorkspaceSnapshot snapshot)
        {
            if (snapshot == null || snapshot.SharedLoopRange == null || !snapshot.SharedLoopRange.HasMarkers)
            {
                return null;
            }

            var focusedPaneId = !string.IsNullOrWhiteSpace(snapshot.FocusedPaneId)
                ? snapshot.FocusedPaneId
                : snapshot.ActivePaneId;
            LoopPlaybackPaneRangeSnapshot paneRange;
            return !string.IsNullOrWhiteSpace(focusedPaneId) &&
                   snapshot.SharedLoopRange.TryGetPaneRange(focusedPaneId, out paneRange)
                ? paneRange
                : snapshot.SharedLoopRange.PaneRanges.FirstOrDefault();
        }

        private static string FormatStepDuration(TimeSpan value)
        {
            return value > TimeSpan.Zero
                ? string.Format(
                    CultureInfo.InvariantCulture,
                    "{0:0.###} ms",
                    value.TotalMilliseconds)
                : "0 ms";
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

        private async Task OpenInternalAsync(string filePath, CancellationToken cancellationToken)
        {
            await _workspaceCoordinator.OpenAsync(filePath, cancellationToken).ConfigureAwait(false);

            if (_recentFilesCatalog == null)
            {
                Refresh();
                return;
            }

            var session = _workspaceCoordinator.CurrentSession ?? ReviewSessionSnapshot.Empty;
            var requestedPath = NormalizePath(filePath);
            var currentPath = NormalizePath(session.CurrentFilePath);
            if (session.IsMediaOpen &&
                !string.IsNullOrWhiteSpace(requestedPath) &&
                string.Equals(requestedPath, currentPath, StringComparison.OrdinalIgnoreCase))
            {
                _recentFilesCatalog.Add(requestedPath);
            }

            Refresh();
        }

        private static string NormalizeExistingPath(string filePath)
        {
            var normalizedPath = NormalizePath(filePath);
            return !string.IsNullOrWhiteSpace(normalizedPath) && File.Exists(normalizedPath)
                ? normalizedPath
                : string.Empty;
        }

        private static string NormalizePath(string filePath)
        {
            if (string.IsNullOrWhiteSpace(filePath))
            {
                return string.Empty;
            }

            try
            {
                return Path.GetFullPath(filePath.Trim());
            }
            catch
            {
                return string.Empty;
            }
        }
    }
}
