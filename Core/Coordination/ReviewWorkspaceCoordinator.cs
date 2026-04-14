using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.ExceptionServices;
using System.Threading;
using System.Threading.Tasks;
using FramePlayer.Core.Abstractions;
using FramePlayer.Core.Events;
using FramePlayer.Core.Models;

namespace FramePlayer.Core.Coordination
{
    // Workspace-level single-session coordinator that can later grow into multi-pane composition.
    public sealed class ReviewWorkspaceCoordinator : IDisposable
    {
        private const string PrimaryPaneId = "pane-primary";

        private readonly List<WorkspacePaneBinding> _paneBindings;
        private readonly string _primaryPaneId;
        private readonly Dictionary<string, LoopPlaybackRangeSnapshot> _paneLoopRanges =
            new Dictionary<string, LoopPlaybackRangeSnapshot>(StringComparer.Ordinal);
        private LoopPlaybackRangeSnapshot _sharedLoopRange = LoopPlaybackRangeSnapshot.Empty;
        private string _activePaneId;
        private string _focusedPaneId;

        public ReviewWorkspaceCoordinator(
            IVideoReviewEngine engine,
            ReviewSessionCoordinator sessionCoordinator)
            : this(sessionCoordinator)
        {
            if (engine == null)
            {
                throw new ArgumentNullException(nameof(engine));
            }
        }

        public ReviewWorkspaceCoordinator(ReviewSessionCoordinator sessionCoordinator)
        {
            SessionCoordinator = sessionCoordinator ?? throw new ArgumentNullException(nameof(sessionCoordinator));
            _paneBindings = new List<WorkspacePaneBinding>();
            var primaryBinding = new WorkspacePaneBinding(
                PrimaryPaneId,
                "Primary",
                TimeSpan.Zero,
                true,
                SessionCoordinator);
            _paneBindings.Add(primaryBinding);
            SessionCoordinator.SessionChanged += SessionCoordinator_SessionChanged;
            _primaryPaneId = primaryBinding.PaneId;
            _activePaneId = _primaryPaneId;
            _focusedPaneId = _primaryPaneId;
            CurrentWorkspace = BuildWorkspace();
        }

        public ReviewSessionCoordinator SessionCoordinator { get; }

        public ReviewSessionSnapshot CurrentSession
        {
            get
            {
                return GetFocusedBinding().SessionCoordinator.CurrentSession;
            }
        }

        public MultiVideoWorkspaceState CurrentWorkspace { get; private set; }

        public ReviewWorkspacePreparationState CurrentPreparationState { get; private set; } =
            ReviewWorkspacePreparationState.Idle;

        public event EventHandler<ReviewWorkspaceChangedEventArgs> WorkspaceChanged;

        public event EventHandler<ReviewWorkspacePreparationChangedEventArgs> PreparationStateChanged;

        public int PaneBindingCount
        {
            get { return _paneBindings.Count; }
        }

        public IReadOnlyList<ReviewPaneState> GetBoundPanes()
        {
            return CurrentWorkspace.Panes;
        }

        public ReviewWorkspaceSnapshot GetWorkspaceSnapshot()
        {
            return BuildWorkspaceSnapshot(CurrentWorkspace ?? MultiVideoWorkspaceState.Empty);
        }

        public bool TryGetBoundPane(string paneId, out ReviewPaneState pane)
        {
            return CurrentWorkspace.TryGetPane(paneId, out pane);
        }

        public bool TryGetPaneSnapshot(string paneId, out ReviewWorkspacePaneSnapshot paneSnapshot)
        {
            return GetWorkspaceSnapshot().TryGetPane(paneId, out paneSnapshot);
        }

        public LoopPlaybackRangeSnapshot SetSharedLoopMarker(
            LoopPlaybackMarkerEndpoint endpoint,
            SynchronizedOperationScope operationScope)
        {
            var workspace = CurrentWorkspace ?? BuildWorkspace();
            var targetPanes = GetMarkerTargetPanes(workspace, operationScope);
            if (targetPanes.Length == 0)
            {
                return _sharedLoopRange;
            }

            var baselineRange = TargetsMatchRange(_sharedLoopRange, targetPanes)
                ? _sharedLoopRange
                : LoopPlaybackRangeSnapshot.Empty;
            _sharedLoopRange = ResolveSharedLoopRange(CaptureSharedLoopMarker(baselineRange, endpoint, targetPanes), targetPanes);
            RefreshWorkspace();
            return _sharedLoopRange;
        }

        public LoopPlaybackPaneRangeSnapshot SetPaneLoopMarker(
            string paneId,
            LoopPlaybackMarkerEndpoint endpoint)
        {
            var workspace = CurrentWorkspace ?? BuildWorkspace();
            ReviewPaneState targetPane;
            if (!workspace.TryGetPane(paneId, out targetPane) ||
                targetPane == null ||
                !targetPane.Session.IsMediaOpen)
            {
                return null;
            }

            LoopPlaybackRangeSnapshot existingRange;
            _paneLoopRanges.TryGetValue(targetPane.PaneId, out existingRange);
            var nextRange = ResolvePaneLocalLoopRange(
                CapturePaneLoopMarker(existingRange, endpoint, targetPane),
                targetPane);
            if (nextRange != null && nextRange.HasMarkers)
            {
                _paneLoopRanges[targetPane.PaneId] = nextRange;
            }
            else
            {
                _paneLoopRanges.Remove(targetPane.PaneId);
            }

            RefreshWorkspace();

            ReviewPaneState refreshedPane;
            return CurrentWorkspace != null &&
                   CurrentWorkspace.TryGetPane(targetPane.PaneId, out refreshedPane) &&
                   refreshedPane != null
                ? refreshedPane.LoopRange
                : null;
        }

        public void ClearSharedLoopRange()
        {
            if (!_sharedLoopRange.HasMarkers)
            {
                return;
            }

            _sharedLoopRange = LoopPlaybackRangeSnapshot.Empty;
            RefreshWorkspace();
        }

        public void ClearPaneLoopRange(string paneId)
        {
            if (string.IsNullOrWhiteSpace(paneId) || !_paneLoopRanges.Remove(paneId))
            {
                return;
            }

            RefreshWorkspace();
        }

        public bool TrySelectPane(
            string paneId,
            WorkspacePaneSelectionMode selectionMode = WorkspacePaneSelectionMode.ActiveAndFocused)
        {
            var normalizedPaneId = NormalizeExistingPaneId(paneId);
            if (string.IsNullOrWhiteSpace(normalizedPaneId))
            {
                return false;
            }

            var changed = false;
            switch (selectionMode)
            {
                case WorkspacePaneSelectionMode.Active:
                    changed = !string.Equals(_activePaneId, normalizedPaneId, StringComparison.Ordinal);
                    _activePaneId = normalizedPaneId;
                    break;
                case WorkspacePaneSelectionMode.Focused:
                    changed = !string.Equals(_focusedPaneId, normalizedPaneId, StringComparison.Ordinal);
                    _focusedPaneId = normalizedPaneId;
                    break;
                case WorkspacePaneSelectionMode.ActiveAndFocused:
                default:
                    changed = !string.Equals(_activePaneId, normalizedPaneId, StringComparison.Ordinal) ||
                              !string.Equals(_focusedPaneId, normalizedPaneId, StringComparison.Ordinal);
                    _activePaneId = normalizedPaneId;
                    _focusedPaneId = normalizedPaneId;
                    break;
            }

            NormalizePaneSelection();
            if (changed)
            {
                RefreshWorkspace();
            }

            return true;
        }

        public bool TrySetActivePane(string paneId)
        {
            return TrySelectPane(paneId, WorkspacePaneSelectionMode.Active);
        }

        public bool TrySetFocusedPane(string paneId)
        {
            return TrySelectPane(paneId, WorkspacePaneSelectionMode.Focused);
        }

        public bool TrySetActiveAndFocusedPane(string paneId)
        {
            return TrySelectPane(paneId, WorkspacePaneSelectionMode.ActiveAndFocused);
        }

        public Task OpenAsync(string filePath, CancellationToken cancellationToken = default(CancellationToken))
        {
            return OpenPreparedAsync(filePath, cancellationToken);
        }

        public Task CloseAsync()
        {
            return CloseInternalAsync();
        }

        public Task PlayAsync()
        {
            return PlayAsync(SynchronizedOperationScope.FocusedPane);
        }

        public Task PlayAsync(SynchronizedOperationScope operationScope)
        {
            return ExecuteScopedActionAsync(
                "play",
                operationScope,
                CancellationToken.None,
                (binding, cancellationToken) => binding.SessionCoordinator.Engine.PlayAsync());
        }

        public Task<ReviewWorkspaceOperationResult> PlayWithPaneResultsAsync(
            SynchronizedOperationScope operationScope)
        {
            return ExecuteScopedOperationAsync(
                "play",
                operationScope,
                CancellationToken.None,
                (binding, cancellationToken) => binding.SessionCoordinator.Engine.PlayAsync());
        }

        public Task PauseAsync()
        {
            return PauseAsync(SynchronizedOperationScope.FocusedPane);
        }

        public Task PauseAsync(SynchronizedOperationScope operationScope)
        {
            return ExecuteScopedActionAsync(
                "pause",
                operationScope,
                CancellationToken.None,
                (binding, cancellationToken) => binding.SessionCoordinator.Engine.PauseAsync());
        }

        public Task<ReviewWorkspaceOperationResult> PauseWithPaneResultsAsync(
            SynchronizedOperationScope operationScope)
        {
            return ExecuteScopedOperationAsync(
                "pause",
                operationScope,
                CancellationToken.None,
                (binding, cancellationToken) => binding.SessionCoordinator.Engine.PauseAsync());
        }

        public Task PlayPaneAsync(string paneId)
        {
            var binding = GetRequiredBinding(paneId);
            return binding.SessionCoordinator.Engine.PlayAsync();
        }

        public Task PausePaneAsync(string paneId)
        {
            var binding = GetRequiredBinding(paneId);
            return binding.SessionCoordinator.Engine.PauseAsync();
        }

        public Task SeekToTimeAsync(TimeSpan position, CancellationToken cancellationToken = default(CancellationToken))
        {
            return SeekToTimeAsync(position, SynchronizedOperationScope.FocusedPane, cancellationToken);
        }

        public Task SeekToTimeAsync(
            TimeSpan position,
            SynchronizedOperationScope operationScope,
            CancellationToken cancellationToken = default(CancellationToken))
        {
            return ExecuteScopedActionAsync(
                "seek-to-time",
                operationScope,
                cancellationToken,
                (binding, token) => binding.SessionCoordinator.Engine.SeekToTimeAsync(position, token));
        }

        public Task SeekPaneToTimeAsync(
            string paneId,
            TimeSpan position,
            CancellationToken cancellationToken = default(CancellationToken))
        {
            var binding = GetRequiredBinding(paneId);
            return binding.SessionCoordinator.Engine.SeekToTimeAsync(position, cancellationToken);
        }

        public Task<ReviewWorkspaceOperationResult> SeekToTimeWithPaneResultsAsync(
            TimeSpan position,
            SynchronizedOperationScope operationScope,
            CancellationToken cancellationToken = default(CancellationToken))
        {
            return ExecuteScopedOperationAsync(
                "seek-to-time",
                operationScope,
                cancellationToken,
                (binding, token) => binding.SessionCoordinator.Engine.SeekToTimeAsync(position, token));
        }

        public Task SeekToFrameAsync(long frameIndex, CancellationToken cancellationToken = default(CancellationToken))
        {
            return SeekToFrameAsync(frameIndex, SynchronizedOperationScope.FocusedPane, cancellationToken);
        }

        public Task SeekToFrameAsync(
            long frameIndex,
            SynchronizedOperationScope operationScope,
            CancellationToken cancellationToken = default(CancellationToken))
        {
            return ExecuteScopedActionAsync(
                "seek-to-frame",
                operationScope,
                cancellationToken,
                (binding, token) => binding.SessionCoordinator.Engine.SeekToFrameAsync(frameIndex, token));
        }

        public Task SeekPaneToFrameAsync(
            string paneId,
            long frameIndex,
            CancellationToken cancellationToken = default(CancellationToken))
        {
            var binding = GetRequiredBinding(paneId);
            return binding.SessionCoordinator.Engine.SeekToFrameAsync(frameIndex, cancellationToken);
        }

        public Task<FrameStepResult> StepBackwardAsync(CancellationToken cancellationToken = default(CancellationToken))
        {
            return StepBackwardAsync(SynchronizedOperationScope.FocusedPane, cancellationToken);
        }

        public Task<FrameStepResult> StepBackwardAsync(
            SynchronizedOperationScope operationScope,
            CancellationToken cancellationToken = default(CancellationToken))
        {
            return ExecuteScopedStepActionAsync(
                "step-backward",
                operationScope,
                cancellationToken,
                (binding, token) => binding.SessionCoordinator.Engine.StepBackwardAsync(token));
        }

        public Task<ReviewWorkspaceOperationResult> StepBackwardWithPaneResultsAsync(
            SynchronizedOperationScope operationScope,
            CancellationToken cancellationToken = default(CancellationToken))
        {
            return ExecuteScopedOperationAsync(
                "step-backward",
                operationScope,
                cancellationToken,
                (binding, token) => binding.SessionCoordinator.Engine.StepBackwardAsync(token));
        }

        public Task<FrameStepResult> StepForwardAsync(CancellationToken cancellationToken = default(CancellationToken))
        {
            return StepForwardAsync(SynchronizedOperationScope.FocusedPane, cancellationToken);
        }

        public Task<FrameStepResult> StepForwardAsync(
            SynchronizedOperationScope operationScope,
            CancellationToken cancellationToken = default(CancellationToken))
        {
            return ExecuteScopedStepActionAsync(
                "step-forward",
                operationScope,
                cancellationToken,
                (binding, token) => binding.SessionCoordinator.Engine.StepForwardAsync(token));
        }

        public Task<ReviewWorkspaceOperationResult> StepForwardWithPaneResultsAsync(
            SynchronizedOperationScope operationScope,
            CancellationToken cancellationToken = default(CancellationToken))
        {
            return ExecuteScopedOperationAsync(
                "step-forward",
                operationScope,
                cancellationToken,
                (binding, token) => binding.SessionCoordinator.Engine.StepForwardAsync(token));
        }

        public async Task SeekToPaneFramesAsync(
            IReadOnlyDictionary<string, long> paneFrameIndices,
            CancellationToken cancellationToken = default(CancellationToken))
        {
            if (paneFrameIndices == null)
            {
                throw new ArgumentNullException(nameof(paneFrameIndices));
            }

            if (paneFrameIndices.Count == 0)
            {
                return;
            }

            var focusedBinding = GetFocusedBinding();
            var paneResults = new List<ReviewWorkspacePaneOperationResult>();
            for (var index = 0; index < _paneBindings.Count; index++)
            {
                var binding = _paneBindings[index];
                long frameIndex;
                if (!paneFrameIndices.TryGetValue(binding.PaneId, out frameIndex))
                {
                    continue;
                }

                try
                {
                    await binding.SessionCoordinator.Engine
                        .SeekToFrameAsync(Math.Max(0L, frameIndex), cancellationToken)
                        .ConfigureAwait(false);
                    paneResults.Add(CreateSucceededPaneOperationResult(
                        binding,
                        binding.SessionCoordinator.CurrentSession ?? ReviewSessionSnapshot.Empty,
                        null));
                }
                catch (Exception exception)
                {
                    paneResults.Add(CreateFailedPaneOperationResult(
                        binding,
                        binding.SessionCoordinator.CurrentSession ?? ReviewSessionSnapshot.Empty,
                        exception.Message,
                        exception,
                        null));
                }
            }

            ThrowIfOperationFailed(new ReviewWorkspaceOperationResult(
                "seek-pane-frames",
                SynchronizedOperationScope.AllPanes,
                focusedBinding.PaneId,
                paneResults));
        }

        public ReviewSessionSnapshot RefreshFromEngine()
        {
            return GetFocusedBinding().SessionCoordinator.RefreshFromEngine();
        }

        public ReviewSessionSnapshot Reset(string currentFilePath = "")
        {
            SetPreparationState(ReviewWorkspacePreparationPhase.Idle, currentFilePath);
            return GetFocusedBinding().SessionCoordinator.Reset(currentFilePath);
        }

        public bool TryBindPane(
            string paneId,
            ReviewSessionCoordinator sessionCoordinator,
            string displayLabel = "",
            TimeSpan timelineOffset = default(TimeSpan),
            bool makeActive = false,
            bool makeFocused = false)
        {
            if (sessionCoordinator == null)
            {
                throw new ArgumentNullException(nameof(sessionCoordinator));
            }

            var normalizedPaneId = NormalizePaneId(paneId, sessionCoordinator);
            if (_paneBindings.Any(binding => string.Equals(binding.PaneId, normalizedPaneId, StringComparison.Ordinal)))
            {
                return false;
            }

            var binding = new WorkspacePaneBinding(
                normalizedPaneId,
                string.IsNullOrWhiteSpace(displayLabel)
                    ? sessionCoordinator.CurrentSession.DisplayLabel
                    : displayLabel,
                timelineOffset,
                false,
                sessionCoordinator);
            _paneBindings.Add(binding);
            sessionCoordinator.SessionChanged += SessionCoordinator_SessionChanged;
            if (makeActive)
            {
                _activePaneId = normalizedPaneId;
            }

            if (makeFocused)
            {
                _focusedPaneId = normalizedPaneId;
            }

            NormalizePaneSelection();
            RefreshWorkspace();
            return true;
        }

        public void Dispose()
        {
            foreach (var binding in _paneBindings)
            {
                binding.SessionCoordinator.SessionChanged -= SessionCoordinator_SessionChanged;
            }
        }

        private async Task OpenPreparedAsync(string filePath, CancellationToken cancellationToken)
        {
            var activeBinding = GetActiveBinding();
            try
            {
                SetPreparationState(ReviewWorkspacePreparationPhase.Opening, filePath);
                if (activeBinding.SessionCoordinator.CurrentSession.IsMediaOpen)
                {
                    await activeBinding.SessionCoordinator.Engine.CloseAsync().ConfigureAwait(false);
                }

                await activeBinding.SessionCoordinator.Engine.OpenAsync(filePath, cancellationToken).ConfigureAwait(false);

                SetPreparationState(ReviewWorkspacePreparationPhase.PreparingFirstFrame, filePath);
                await activeBinding.SessionCoordinator.Engine.PauseAsync().ConfigureAwait(false);
                await activeBinding.SessionCoordinator.Engine.SeekToTimeAsync(TimeSpan.Zero, cancellationToken).ConfigureAwait(false);
                activeBinding.SessionCoordinator.RefreshFromEngine();
                SetPreparationState(ReviewWorkspacePreparationPhase.Ready, filePath);
            }
            catch
            {
                SetPreparationState(ReviewWorkspacePreparationPhase.Failed, filePath);
                throw;
            }
        }

        private async Task CloseInternalAsync()
        {
            var activeBinding = GetActiveBinding();
            await activeBinding.SessionCoordinator.Engine.CloseAsync().ConfigureAwait(false);
            activeBinding.SessionCoordinator.RefreshFromEngine();
            SetPreparationState(ReviewWorkspacePreparationPhase.Idle, string.Empty);
        }

        private void SessionCoordinator_SessionChanged(object sender, ReviewSessionChangedEventArgs e)
        {
            var previousWorkspace = CurrentWorkspace ?? MultiVideoWorkspaceState.Empty;
            var currentWorkspace = BuildWorkspace();
            if (WorkspaceStatesEqual(previousWorkspace, currentWorkspace))
            {
                return;
            }

            CurrentWorkspace = currentWorkspace;
            WorkspaceChanged?.Invoke(
                this,
                new ReviewWorkspaceChangedEventArgs(previousWorkspace, currentWorkspace));
        }

        private void SetPreparationState(ReviewWorkspacePreparationPhase phase, string targetFilePath)
        {
            var nextState = new ReviewWorkspacePreparationState(phase, targetFilePath);
            var previousState = CurrentPreparationState ?? ReviewWorkspacePreparationState.Idle;
            if (previousState.Phase == nextState.Phase &&
                string.Equals(previousState.TargetFilePath, nextState.TargetFilePath, StringComparison.Ordinal))
            {
                return;
            }

            CurrentPreparationState = nextState;
            PreparationStateChanged?.Invoke(
                this,
                new ReviewWorkspacePreparationChangedEventArgs(previousState, nextState));
        }

        private MultiVideoWorkspaceState BuildWorkspace()
        {
            NormalizePaneSelection();
            var paneStates = new ReviewPaneState[_paneBindings.Count];
            for (var index = 0; index < _paneBindings.Count; index++)
            {
                var binding = _paneBindings[index];
                var paneIsFocused = string.Equals(binding.PaneId, _focusedPaneId, StringComparison.Ordinal);
                var paneIsActive = string.Equals(binding.PaneId, _activePaneId, StringComparison.Ordinal);
                paneStates[index] = new ReviewPaneState(
                    binding.PaneId,
                    string.IsNullOrWhiteSpace(binding.DisplayLabel)
                        ? binding.SessionCoordinator.CurrentSession.DisplayLabel
                        : binding.DisplayLabel,
                    binding.SessionCoordinator.CurrentSession.SessionId,
                    binding.SessionCoordinator.CurrentSession,
                    binding.TimelineOffset,
                    paneIsFocused,
                    paneIsActive,
                    binding.IsPrimary);
            }

            var activePane = ResolvePane(paneStates, _activePaneId, pane => pane.IsActive) ??
                             ResolvePane(paneStates, _focusedPaneId, pane => pane.IsFocused) ??
                             ResolvePane(paneStates, _primaryPaneId, pane => pane.IsPrimary);
            _sharedLoopRange = ResolveSharedLoopRange(_sharedLoopRange, paneStates);
            var resolvedPaneLoopRanges = new Dictionary<string, LoopPlaybackRangeSnapshot>(StringComparer.Ordinal);
            for (var index = 0; index < paneStates.Length; index++)
            {
                var paneState = paneStates[index];
                if (paneState == null)
                {
                    continue;
                }

                LoopPlaybackRangeSnapshot paneLoopRange;
                _paneLoopRanges.TryGetValue(paneState.PaneId, out paneLoopRange);
                var resolvedPaneLoopRange = ResolvePaneLocalLoopRange(paneLoopRange, paneState);
                if (resolvedPaneLoopRange != null && resolvedPaneLoopRange.HasMarkers)
                {
                    resolvedPaneLoopRanges[paneState.PaneId] = resolvedPaneLoopRange;
                }

                LoopPlaybackPaneRangeSnapshot resolvedPaneRange = null;
                if (resolvedPaneLoopRange != null)
                {
                    resolvedPaneLoopRange.TryGetPaneRange(paneState.PaneId, out resolvedPaneRange);
                }

                paneStates[index] = new ReviewPaneState(
                    paneState.PaneId,
                    paneState.DisplayLabel,
                    paneState.SessionId,
                    paneState.Session,
                    paneState.TimelineOffset,
                    paneState.IsFocused,
                    paneState.IsActive,
                    paneState.IsPrimary,
                    resolvedPaneRange);
            }

            _paneLoopRanges.Clear();
            foreach (var entry in resolvedPaneLoopRanges)
            {
                _paneLoopRanges[entry.Key] = entry.Value;
            }

            return new MultiVideoWorkspaceState(
                activePane != null
                    ? activePane.Session.Position.PresentationTime
                    : TimeSpan.Zero,
                TimelineSynchronizationMode.Independent,
                SynchronizedOperationScope.FocusedPane,
                _primaryPaneId,
                _activePaneId,
                _focusedPaneId,
                _sharedLoopRange,
                paneStates);
        }

        private void RefreshWorkspace()
        {
            var previousWorkspace = CurrentWorkspace ?? MultiVideoWorkspaceState.Empty;
            var currentWorkspace = BuildWorkspace();
            if (WorkspaceStatesEqual(previousWorkspace, currentWorkspace))
            {
                return;
            }

            CurrentWorkspace = currentWorkspace;
            WorkspaceChanged?.Invoke(
                this,
                new ReviewWorkspaceChangedEventArgs(previousWorkspace, currentWorkspace));
        }

        private static ReviewWorkspaceSnapshot BuildWorkspaceSnapshot(MultiVideoWorkspaceState workspaceState)
        {
            if (workspaceState == null)
            {
                return ReviewWorkspaceSnapshot.Empty;
            }

            var paneStates = workspaceState.Panes;
            var paneSnapshots = new ReviewWorkspacePaneSnapshot[paneStates.Count];
            for (var index = 0; index < paneStates.Count; index++)
            {
                paneSnapshots[index] = BuildPaneSnapshot(paneStates[index]);
            }

            return new ReviewWorkspaceSnapshot(
                workspaceState.MasterTimelinePosition,
                workspaceState.SynchronizationMode,
                workspaceState.DefaultOperationScope,
                workspaceState.PrimaryPaneId,
                workspaceState.ActivePaneId,
                workspaceState.FocusedPaneId,
                workspaceState.SharedLoopRange,
                paneSnapshots);
        }

        private static ReviewWorkspacePaneSnapshot BuildPaneSnapshot(ReviewPaneState paneState)
        {
            if (paneState == null)
            {
                return new ReviewWorkspacePaneSnapshot(
                    string.Empty,
                    string.Empty,
                    string.Empty,
                    false,
                    false,
                    false,
                    false,
                    TimeSpan.Zero,
                    ReviewPlaybackState.Closed,
                    string.Empty,
                    ReviewPosition.Empty,
                    null);
            }

            var session = paneState.Session ?? ReviewSessionSnapshot.Empty;
            var isBound = !string.IsNullOrWhiteSpace(paneState.SessionId) || session.IsMediaOpen;
            return new ReviewWorkspacePaneSnapshot(
                paneState.PaneId,
                paneState.SessionId,
                paneState.DisplayLabel,
                isBound,
                paneState.IsPrimary,
                paneState.IsFocused,
                paneState.IsActive,
                paneState.TimelineOffset,
                session.PlaybackState,
                session.CurrentFilePath,
                session.Position,
                paneState.LoopRange);
        }

        private static bool WorkspaceStatesEqual(MultiVideoWorkspaceState left, MultiVideoWorkspaceState right)
        {
            if (ReferenceEquals(left, right))
            {
                return true;
            }

            if (left == null || right == null)
            {
                return false;
            }

            return left.MasterTimelinePosition == right.MasterTimelinePosition &&
                   left.SynchronizationMode == right.SynchronizationMode &&
                   left.DefaultOperationScope == right.DefaultOperationScope &&
                   string.Equals(left.PrimaryPaneId, right.PrimaryPaneId, StringComparison.Ordinal) &&
                   string.Equals(left.ActivePaneId, right.ActivePaneId, StringComparison.Ordinal) &&
                   string.Equals(left.FocusedPaneId, right.FocusedPaneId, StringComparison.Ordinal) &&
                   LoopRangesEqual(left.SharedLoopRange, right.SharedLoopRange) &&
                   PaneCollectionsEqual(left.Panes, right.Panes);
        }

        private static bool PaneCollectionsEqual(
            System.Collections.Generic.IReadOnlyList<ReviewPaneState> left,
            System.Collections.Generic.IReadOnlyList<ReviewPaneState> right)
        {
            if (ReferenceEquals(left, right))
            {
                return true;
            }

            if (left == null || right == null || left.Count != right.Count)
            {
                return false;
            }

            for (var index = 0; index < left.Count; index++)
            {
                if (!PaneStatesEqual(left[index], right[index]))
                {
                    return false;
                }
            }

            return true;
        }

        private static bool PaneStatesEqual(ReviewPaneState left, ReviewPaneState right)
        {
            if (ReferenceEquals(left, right))
            {
                return true;
            }

            if (left == null || right == null)
            {
                return false;
            }

            return string.Equals(left.PaneId, right.PaneId, StringComparison.Ordinal) &&
                   string.Equals(left.DisplayLabel, right.DisplayLabel, StringComparison.Ordinal) &&
                   string.Equals(left.SessionId, right.SessionId, StringComparison.Ordinal) &&
                   left.TimelineOffset == right.TimelineOffset &&
                   left.IsFocused == right.IsFocused &&
                   left.IsActive == right.IsActive &&
                   left.IsPrimary == right.IsPrimary &&
                   PaneRangesEqual(left.LoopRange, right.LoopRange) &&
                   SessionsEqual(left.Session, right.Session);
        }

        private static bool LoopRangesEqual(LoopPlaybackRangeSnapshot left, LoopPlaybackRangeSnapshot right)
        {
            if (ReferenceEquals(left, right))
            {
                return true;
            }

            if (left == null || right == null)
            {
                return false;
            }

            if (left.TargetKind != right.TargetKind || left.PaneRanges.Count != right.PaneRanges.Count)
            {
                return false;
            }

            for (var index = 0; index < left.PaneRanges.Count; index++)
            {
                if (!PaneRangesEqual(left.PaneRanges[index], right.PaneRanges[index]))
                {
                    return false;
                }
            }

            return true;
        }

        private static bool PaneRangesEqual(LoopPlaybackPaneRangeSnapshot left, LoopPlaybackPaneRangeSnapshot right)
        {
            if (ReferenceEquals(left, right))
            {
                return true;
            }

            if (left == null || right == null)
            {
                return false;
            }

            return string.Equals(left.PaneId, right.PaneId, StringComparison.Ordinal) &&
                   string.Equals(left.SessionId, right.SessionId, StringComparison.Ordinal) &&
                   string.Equals(left.DisplayLabel, right.DisplayLabel, StringComparison.Ordinal) &&
                   string.Equals(left.CurrentFilePath, right.CurrentFilePath, StringComparison.Ordinal) &&
                   left.Duration == right.Duration &&
                   AnchorsEqual(left.LoopIn, right.LoopIn) &&
                   AnchorsEqual(left.LoopOut, right.LoopOut);
        }

        private static bool AnchorsEqual(LoopPlaybackAnchorSnapshot left, LoopPlaybackAnchorSnapshot right)
        {
            if (ReferenceEquals(left, right))
            {
                return true;
            }

            if (left == null || right == null)
            {
                return false;
            }

            return string.Equals(left.PaneId, right.PaneId, StringComparison.Ordinal) &&
                   string.Equals(left.SessionId, right.SessionId, StringComparison.Ordinal) &&
                   string.Equals(left.DisplayLabel, right.DisplayLabel, StringComparison.Ordinal) &&
                   left.PresentationTime == right.PresentationTime &&
                   left.AbsoluteFrameIndex == right.AbsoluteFrameIndex &&
                   left.IsFrameIndexAbsolute == right.IsFrameIndexAbsolute;
        }

        private static bool SessionsEqual(ReviewSessionSnapshot left, ReviewSessionSnapshot right)
        {
            if (ReferenceEquals(left, right))
            {
                return true;
            }

            if (left == null || right == null)
            {
                return false;
            }

            return left.PlaybackState == right.PlaybackState &&
                   string.Equals(left.CurrentFilePath, right.CurrentFilePath, StringComparison.Ordinal) &&
                   left.Position.PresentationTime == right.Position.PresentationTime &&
                   left.Position.FrameIndex == right.Position.FrameIndex &&
                   left.Position.IsFrameAccurate == right.Position.IsFrameAccurate &&
                   left.Position.IsFrameIndexAbsolute == right.Position.IsFrameIndexAbsolute &&
                   left.MediaInfo.Duration == right.MediaInfo.Duration &&
                   left.MediaInfo.PositionStep == right.MediaInfo.PositionStep &&
                   Math.Abs(left.MediaInfo.FramesPerSecond - right.MediaInfo.FramesPerSecond) < 0.0001d;
        }

        private static ReviewPaneState[] GetMarkerTargetPanes(
            MultiVideoWorkspaceState workspace,
            SynchronizedOperationScope operationScope)
        {
            if (workspace == null)
            {
                return Array.Empty<ReviewPaneState>();
            }

            if (operationScope == SynchronizedOperationScope.AllPanes)
            {
                return workspace.Panes
                    .Where(pane => pane != null && pane.Session.IsMediaOpen)
                    .ToArray();
            }

            var focusedPane = workspace.FocusedPane;
            return focusedPane != null && focusedPane.Session.IsMediaOpen
                ? new[] { focusedPane }
                : Array.Empty<ReviewPaneState>();
        }

        private static LoopPlaybackRangeSnapshot CapturePaneLoopMarker(
            LoopPlaybackRangeSnapshot existingRange,
            LoopPlaybackMarkerEndpoint endpoint,
            ReviewPaneState targetPane)
        {
            if (targetPane == null)
            {
                return LoopPlaybackRangeSnapshot.Empty;
            }

            var session = targetPane.Session ?? ReviewSessionSnapshot.Empty;
            if (!session.IsMediaOpen)
            {
                return LoopPlaybackRangeSnapshot.Empty;
            }

            LoopPlaybackPaneRangeSnapshot existingPaneRange = null;
            if (existingRange != null)
            {
                existingRange.TryGetPaneRange(targetPane.PaneId, out existingPaneRange);
            }

            var nextAnchor = CreateAnchorSnapshot(targetPane, session);
            return new LoopPlaybackRangeSnapshot(
                LoopPlaybackTargetKind.PaneLocal,
                new[]
                {
                    new LoopPlaybackPaneRangeSnapshot(
                        targetPane.PaneId,
                        session.SessionId,
                        targetPane.DisplayLabel,
                        session.CurrentFilePath,
                        session.MediaInfo.Duration,
                        endpoint == LoopPlaybackMarkerEndpoint.In ? nextAnchor : (existingPaneRange != null ? existingPaneRange.LoopIn : null),
                        endpoint == LoopPlaybackMarkerEndpoint.Out ? nextAnchor : (existingPaneRange != null ? existingPaneRange.LoopOut : null))
                });
        }

        private static bool TargetsMatchRange(
            LoopPlaybackRangeSnapshot existingRange,
            ReviewPaneState[] targetPanes)
        {
            if (existingRange == null || existingRange.PaneRanges.Count != targetPanes.Length)
            {
                return false;
            }

            for (var index = 0; index < targetPanes.Length; index++)
            {
                var targetPane = targetPanes[index];
                var existingPaneRange = existingRange.PaneRanges[index];
                if (targetPane == null ||
                    existingPaneRange == null ||
                    !string.Equals(targetPane.PaneId, existingPaneRange.PaneId, StringComparison.Ordinal))
                {
                    return false;
                }
            }

            return true;
        }

        private static LoopPlaybackRangeSnapshot CaptureSharedLoopMarker(
            LoopPlaybackRangeSnapshot existingRange,
            LoopPlaybackMarkerEndpoint endpoint,
            ReviewPaneState[] targetPanes)
        {
            var paneRanges = new LoopPlaybackPaneRangeSnapshot[targetPanes.Length];
            for (var index = 0; index < targetPanes.Length; index++)
            {
                var pane = targetPanes[index];
                var session = pane != null ? pane.Session ?? ReviewSessionSnapshot.Empty : ReviewSessionSnapshot.Empty;
                LoopPlaybackPaneRangeSnapshot existingPaneRange = null;
                if (existingRange != null)
                {
                    existingRange.TryGetPaneRange(pane != null ? pane.PaneId : string.Empty, out existingPaneRange);
                }

                var nextAnchor = pane != null && session.IsMediaOpen
                    ? CreateAnchorSnapshot(pane, session)
                    : null;
                paneRanges[index] = new LoopPlaybackPaneRangeSnapshot(
                    pane != null ? pane.PaneId : string.Empty,
                    session.SessionId,
                    pane != null ? pane.DisplayLabel : string.Empty,
                    session.CurrentFilePath,
                    session.MediaInfo.Duration,
                    endpoint == LoopPlaybackMarkerEndpoint.In ? nextAnchor : (existingPaneRange != null ? existingPaneRange.LoopIn : null),
                    endpoint == LoopPlaybackMarkerEndpoint.Out ? nextAnchor : (existingPaneRange != null ? existingPaneRange.LoopOut : null));
            }

            return new LoopPlaybackRangeSnapshot(LoopPlaybackTargetKind.SharedWorkspace, paneRanges);
        }

        private static LoopPlaybackRangeSnapshot ResolvePaneLocalLoopRange(
            LoopPlaybackRangeSnapshot range,
            ReviewPaneState currentPane)
        {
            if (range == null || !range.HasMarkers || currentPane == null || !currentPane.Session.IsMediaOpen)
            {
                return LoopPlaybackRangeSnapshot.Empty;
            }

            LoopPlaybackPaneRangeSnapshot paneRange;
            if (!range.TryGetPaneRange(currentPane.PaneId, out paneRange) || paneRange == null)
            {
                return LoopPlaybackRangeSnapshot.Empty;
            }

            if (!string.Equals(currentPane.Session.CurrentFilePath, paneRange.CurrentFilePath, StringComparison.Ordinal))
            {
                return LoopPlaybackRangeSnapshot.Empty;
            }

            return new LoopPlaybackRangeSnapshot(
                LoopPlaybackTargetKind.PaneLocal,
                new[]
                {
                    new LoopPlaybackPaneRangeSnapshot(
                        currentPane.PaneId,
                        currentPane.Session.SessionId,
                        currentPane.DisplayLabel,
                        currentPane.Session.CurrentFilePath,
                        currentPane.Session.MediaInfo.Duration,
                        ResolveAnchorSnapshot(paneRange.LoopIn, currentPane),
                        ResolveAnchorSnapshot(paneRange.LoopOut, currentPane))
                });
        }

        private static LoopPlaybackRangeSnapshot ResolveSharedLoopRange(
            LoopPlaybackRangeSnapshot range,
            ReviewPaneState[] currentPanes)
        {
            if (range == null || !range.HasMarkers)
            {
                return LoopPlaybackRangeSnapshot.Empty;
            }

            var resolvedPaneRanges = new LoopPlaybackPaneRangeSnapshot[range.PaneRanges.Count];
            for (var index = 0; index < range.PaneRanges.Count; index++)
            {
                var paneRange = range.PaneRanges[index];
                if (paneRange == null)
                {
                    return LoopPlaybackRangeSnapshot.Empty;
                }

                var currentPane = currentPanes != null
                    ? currentPanes.FirstOrDefault(pane => pane != null && string.Equals(pane.PaneId, paneRange.PaneId, StringComparison.Ordinal))
                    : null;
                if (currentPane == null ||
                    !currentPane.Session.IsMediaOpen ||
                    !string.Equals(currentPane.Session.CurrentFilePath, paneRange.CurrentFilePath, StringComparison.Ordinal))
                {
                    return LoopPlaybackRangeSnapshot.Empty;
                }

                resolvedPaneRanges[index] = new LoopPlaybackPaneRangeSnapshot(
                    currentPane.PaneId,
                    currentPane.Session.SessionId,
                    currentPane.DisplayLabel,
                    currentPane.Session.CurrentFilePath,
                    currentPane.Session.MediaInfo.Duration,
                    ResolveAnchorSnapshot(paneRange.LoopIn, currentPane),
                    ResolveAnchorSnapshot(paneRange.LoopOut, currentPane));
            }

            return new LoopPlaybackRangeSnapshot(range.TargetKind, resolvedPaneRanges);
        }

        private static LoopPlaybackAnchorSnapshot ResolveAnchorSnapshot(
            LoopPlaybackAnchorSnapshot anchor,
            ReviewPaneState pane)
        {
            if (anchor == null || pane == null)
            {
                return anchor;
            }

            var session = pane.Session ?? ReviewSessionSnapshot.Empty;
            if (!session.IsMediaOpen)
            {
                return anchor;
            }

            if (!anchor.HasAbsoluteFrameIdentity &&
                session.HasAbsoluteFrameIdentity &&
                ArePresentationTimesEquivalent(anchor.PresentationTime, session.Position.PresentationTime, session.MediaInfo.PositionStep))
            {
                return CreateAnchorSnapshot(pane, session);
            }

            return new LoopPlaybackAnchorSnapshot(
                pane.PaneId,
                session.SessionId,
                pane.DisplayLabel,
                anchor.PresentationTime,
                anchor.AbsoluteFrameIndex,
                anchor.IsFrameIndexAbsolute,
                anchor.PresentationTimestamp,
                anchor.DecodeTimestamp);
        }

        private static LoopPlaybackAnchorSnapshot CreateAnchorSnapshot(
            ReviewPaneState pane,
            ReviewSessionSnapshot session)
        {
            return new LoopPlaybackAnchorSnapshot(
                pane.PaneId,
                session.SessionId,
                pane.DisplayLabel,
                session.Position.PresentationTime,
                session.HasAbsoluteFrameIdentity
                    ? (long?)Math.Max(0L, session.Position.FrameIndex.GetValueOrDefault())
                    : null,
                session.HasAbsoluteFrameIdentity,
                session.Position.PresentationTimestamp,
                session.Position.DecodeTimestamp);
        }

        private static bool ArePresentationTimesEquivalent(
            TimeSpan expected,
            TimeSpan actual,
            TimeSpan positionStep)
        {
            var toleranceTicks = positionStep > TimeSpan.Zero
                ? Math.Max(1L, positionStep.Ticks / 2L)
                : TimeSpan.FromMilliseconds(2).Ticks;
            var difference = expected > actual ? expected - actual : actual - expected;
            return difference.Ticks <= toleranceTicks;
        }

        private static ReviewPaneState ResolvePane(
            ReviewPaneState[] panes,
            string paneId,
            Func<ReviewPaneState, bool> fallbackPredicate)
        {
            if (panes == null || panes.Length == 0)
            {
                return null;
            }

            for (var index = 0; index < panes.Length; index++)
            {
                var pane = panes[index];
                if (pane != null && string.Equals(pane.PaneId, paneId, StringComparison.Ordinal))
                {
                    return pane;
                }
            }

            if (fallbackPredicate != null)
            {
                for (var index = 0; index < panes.Length; index++)
                {
                    var pane = panes[index];
                    if (pane != null && fallbackPredicate(pane))
                    {
                        return pane;
                    }
                }
            }

            return panes[0];
        }

        private void NormalizePaneSelection()
        {
            _activePaneId = NormalizeExistingPaneId(_activePaneId) ?? _primaryPaneId;
            _focusedPaneId = NormalizeExistingPaneId(_focusedPaneId) ?? _activePaneId;
        }

        private string NormalizeExistingPaneId(string paneId)
        {
            return ResolveBinding(paneId) != null
                ? paneId
                : null;
        }

        private WorkspacePaneBinding GetActiveBinding()
        {
            var binding = ResolveBinding(_activePaneId) ??
                          ResolveBinding(_focusedPaneId) ??
                          ResolveBinding(_primaryPaneId) ??
                          _paneBindings.FirstOrDefault();
            if (binding == null)
            {
                throw new InvalidOperationException("The workspace does not contain any pane bindings.");
            }

            return binding;
        }

        private WorkspacePaneBinding GetFocusedBinding()
        {
            var binding = ResolveBinding(_focusedPaneId) ??
                          ResolveBinding(_activePaneId) ??
                          ResolveBinding(_primaryPaneId) ??
                          _paneBindings.FirstOrDefault();
            if (binding == null)
            {
                throw new InvalidOperationException("The workspace does not contain any pane bindings.");
            }

            return binding;
        }

        private WorkspacePaneBinding[] GetOperationBindings(SynchronizedOperationScope operationScope)
        {
            if (operationScope != SynchronizedOperationScope.AllPanes)
            {
                return new[] { GetFocusedBinding() };
            }

            var focusedBinding = GetFocusedBinding();
            var bindings = new List<WorkspacePaneBinding>(_paneBindings.Count);
            bindings.Add(focusedBinding);
            for (var index = 0; index < _paneBindings.Count; index++)
            {
                var binding = _paneBindings[index];
                if (!string.Equals(binding.PaneId, focusedBinding.PaneId, StringComparison.Ordinal))
                {
                    bindings.Add(binding);
                }
            }

            return bindings.ToArray();
        }

        private Task ExecuteScopedActionAsync(
            string operationName,
            SynchronizedOperationScope operationScope,
            CancellationToken cancellationToken,
            Func<WorkspacePaneBinding, CancellationToken, Task> operation)
        {
            if (operation == null)
            {
                throw new ArgumentNullException(nameof(operation));
            }

            return ExecuteScopedActionCoreAsync(operationName, operationScope, cancellationToken, operation);
        }

        private async Task ExecuteScopedActionCoreAsync(
            string operationName,
            SynchronizedOperationScope operationScope,
            CancellationToken cancellationToken,
            Func<WorkspacePaneBinding, CancellationToken, Task> operation)
        {
            var result = await ExecuteScopedOperationAsync(
                    operationName,
                    operationScope,
                    cancellationToken,
                    async (binding, token) =>
                    {
                        await operation(binding, token).ConfigureAwait(false);
                        return null;
                    })
                .ConfigureAwait(false);
            ThrowIfOperationFailed(result);
        }

        private async Task<ReviewWorkspaceOperationResult> ExecuteScopedOperationAsync(
            string operationName,
            SynchronizedOperationScope operationScope,
            CancellationToken cancellationToken,
            Func<WorkspacePaneBinding, CancellationToken, Task> operation)
        {
            if (operation == null)
            {
                throw new ArgumentNullException(nameof(operation));
            }

            return await ExecuteScopedOperationAsync(
                    operationName,
                    operationScope,
                    cancellationToken,
                    async (binding, token) =>
                    {
                        await operation(binding, token).ConfigureAwait(false);
                        return null;
                    })
                .ConfigureAwait(false);
        }

        private async Task<ReviewWorkspaceOperationResult> ExecuteScopedOperationAsync(
            string operationName,
            SynchronizedOperationScope operationScope,
            CancellationToken cancellationToken,
            Func<WorkspacePaneBinding, CancellationToken, Task<FrameStepResult>> operation)
        {
            if (operation == null)
            {
                throw new ArgumentNullException(nameof(operation));
            }

            var focusedBinding = GetFocusedBinding();
            var bindings = GetOperationBindings(operationScope);
            var paneResults = new ReviewWorkspacePaneOperationResult[bindings.Length];
            for (var index = 0; index < bindings.Length; index++)
            {
                var binding = bindings[index];
                paneResults[index] = await ExecutePaneOperationAsync(
                        binding,
                        cancellationToken,
                        operation)
                    .ConfigureAwait(false);
            }

            return new ReviewWorkspaceOperationResult(
                operationName,
                operationScope,
                focusedBinding.PaneId,
                paneResults);
        }

        private async Task<FrameStepResult> ExecuteScopedStepActionAsync(
            string operationName,
            SynchronizedOperationScope operationScope,
            CancellationToken cancellationToken,
            Func<WorkspacePaneBinding, CancellationToken, Task<FrameStepResult>> operation)
        {
            var result = await ExecuteScopedOperationAsync(
                    operationName,
                    operationScope,
                    cancellationToken,
                    operation)
                .ConfigureAwait(false);
            ThrowIfOperationFailed(result);

            var focusedPaneResult = result.FocusedPaneResult;
            if (focusedPaneResult != null && focusedPaneResult.FrameStepResult != null)
            {
                return focusedPaneResult.FrameStepResult;
            }

            var focusedBinding = GetFocusedBinding();
            return FrameStepResult.Failed(
                0,
                focusedBinding.SessionCoordinator.CurrentSession.Position,
                "The workspace did not produce a frame-step result for the focused pane.");
        }

        private async Task<ReviewWorkspacePaneOperationResult> ExecutePaneOperationAsync(
            WorkspacePaneBinding binding,
            CancellationToken cancellationToken,
            Func<WorkspacePaneBinding, CancellationToken, Task<FrameStepResult>> operation)
        {
            if (binding == null)
            {
                throw new ArgumentNullException(nameof(binding));
            }

            try
            {
                var frameStepResult = await operation(binding, cancellationToken).ConfigureAwait(false);
                var session = binding.SessionCoordinator.CurrentSession ?? ReviewSessionSnapshot.Empty;
                if (frameStepResult != null && !frameStepResult.Success)
                {
                    return CreateFailedPaneOperationResult(
                        binding,
                        session,
                        frameStepResult.Message,
                        null,
                        frameStepResult);
                }

                return CreateSucceededPaneOperationResult(binding, session, frameStepResult);
            }
            catch (Exception exception)
            {
                var session = binding.SessionCoordinator.CurrentSession ?? ReviewSessionSnapshot.Empty;
                return CreateFailedPaneOperationResult(
                    binding,
                    session,
                    exception.Message,
                    exception,
                    null);
            }
        }

        private static void ThrowIfOperationFailed(ReviewWorkspaceOperationResult result)
        {
            if (result == null || !result.HasExceptionalFailures)
            {
                return;
            }

            var failedPaneResult = result.FirstExceptionalFailurePaneResult;
            if (failedPaneResult != null && failedPaneResult.FailureException != null)
            {
                ExceptionDispatchInfo.Capture(failedPaneResult.FailureException).Throw();
            }

            throw new InvalidOperationException(
                failedPaneResult != null && !string.IsNullOrWhiteSpace(failedPaneResult.FailureDetail)
                    ? failedPaneResult.FailureDetail
                    : "The workspace operation failed.");
        }

        private static ReviewWorkspacePaneOperationResult CreateSucceededPaneOperationResult(
            WorkspacePaneBinding binding,
            ReviewSessionSnapshot session,
            FrameStepResult frameStepResult)
        {
            return new ReviewWorkspacePaneOperationResult(
                binding.PaneId,
                session != null ? session.SessionId : string.Empty,
                ResolveDisplayLabel(binding, session),
                true,
                true,
                ReviewWorkspacePaneOperationOutcome.Succeeded,
                session,
                string.Empty,
                string.Empty,
                frameStepResult,
                null);
        }

        private static ReviewWorkspacePaneOperationResult CreateFailedPaneOperationResult(
            WorkspacePaneBinding binding,
            ReviewSessionSnapshot session,
            string failureDetail,
            Exception exception,
            FrameStepResult frameStepResult)
        {
            return new ReviewWorkspacePaneOperationResult(
                binding.PaneId,
                session != null ? session.SessionId : string.Empty,
                ResolveDisplayLabel(binding, session),
                true,
                true,
                ReviewWorkspacePaneOperationOutcome.Failed,
                session,
                failureDetail,
                exception != null ? exception.GetType().FullName : string.Empty,
                frameStepResult,
                exception);
        }

        private static string ResolveDisplayLabel(
            WorkspacePaneBinding binding,
            ReviewSessionSnapshot session)
        {
            if (binding == null)
            {
                return string.Empty;
            }

            if (!string.IsNullOrWhiteSpace(binding.DisplayLabel))
            {
                return binding.DisplayLabel;
            }

            return session != null
                ? session.DisplayLabel
                : string.Empty;
        }

        private WorkspacePaneBinding ResolveBinding(string paneId)
        {
            for (var index = 0; index < _paneBindings.Count; index++)
            {
                var binding = _paneBindings[index];
                if (string.Equals(binding.PaneId, paneId, StringComparison.Ordinal))
                {
                    return binding;
                }
            }

            return null;
        }

        private WorkspacePaneBinding GetRequiredBinding(string paneId)
        {
            var binding = ResolveBinding(paneId);
            if (binding == null)
            {
                throw new InvalidOperationException("The requested workspace pane is not available.");
            }

            return binding;
        }

        private static string NormalizePaneId(string paneId, ReviewSessionCoordinator sessionCoordinator)
        {
            if (!string.IsNullOrWhiteSpace(paneId))
            {
                return paneId;
            }

            var sessionId = sessionCoordinator != null
                ? sessionCoordinator.CurrentSession.SessionId
                : string.Empty;
            if (!string.IsNullOrWhiteSpace(sessionId))
            {
                return "pane-" + sessionId;
            }

            return Guid.NewGuid().ToString("N");
        }

        private sealed class WorkspacePaneBinding
        {
            public WorkspacePaneBinding(
                string paneId,
                string displayLabel,
                TimeSpan timelineOffset,
                bool isPrimary,
                ReviewSessionCoordinator sessionCoordinator)
            {
                PaneId = string.IsNullOrWhiteSpace(paneId) ? PrimaryPaneId : paneId;
                DisplayLabel = displayLabel ?? string.Empty;
                TimelineOffset = timelineOffset;
                IsPrimary = isPrimary;
                SessionCoordinator = sessionCoordinator ?? throw new ArgumentNullException(nameof(sessionCoordinator));
            }

            public string PaneId { get; }

            public string DisplayLabel { get; }

            public TimeSpan TimelineOffset { get; }

            public bool IsPrimary { get; }

            public ReviewSessionCoordinator SessionCoordinator { get; }
        }
    }
}
