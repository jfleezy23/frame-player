using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using FramePlayer.Core.Abstractions;
using FramePlayer.Core.Coordination;
using FramePlayer.Core.Events;
using FramePlayer.Core.Models;
using Xunit;

namespace FramePlayer.Core.Tests
{
    public sealed class ReviewWorkspaceCoordinatorTests
    {
        [Fact]
        public void WorkspaceModels_NormalizeAndAggregatePaneState()
        {
            var position = new ReviewPosition(
                TimeSpan.FromSeconds(2),
                60L,
                isFrameAccurate: true,
                isFrameIndexAbsolute: true,
                presentationTimestamp: 180_000L,
                decodeTimestamp: 179_000L);
            var mediaInfo = CreateMediaInfo("/video.mov", TimeSpan.FromSeconds(10));
            var session = new ReviewSessionSnapshot(
                "secondary",
                "Secondary",
                ReviewPlaybackState.Paused,
                "/video.mov",
                mediaInfo,
                position);
            var frameIdentity = new LoopPlaybackFrameIdentitySnapshot(60L, true, 180_000L, 179_000L);
            var loopIn = new LoopPlaybackAnchorSnapshot(
                "pane-secondary",
                "secondary",
                "Secondary",
                TimeSpan.FromSeconds(2),
                frameIdentity);
            var loopOut = new LoopPlaybackAnchorSnapshot(
                "pane-secondary",
                "secondary",
                "Secondary",
                TimeSpan.FromSeconds(12),
                null);
            var paneRange = new LoopPlaybackPaneRangeSnapshot(
                "pane-secondary",
                "secondary",
                "Secondary",
                "/video.mov",
                TimeSpan.FromSeconds(10),
                loopIn,
                loopOut);
            var range = new LoopPlaybackRangeSnapshot(
                LoopPlaybackTargetKind.PaneLocal,
                new[] { paneRange, null });
            var pane = new ReviewPaneState(
                "pane-secondary",
                "Secondary",
                string.Empty,
                session,
                TimeSpan.FromMilliseconds(50),
                isFocused: true,
                isActive: true,
                isPrimary: false,
                paneRange);
            var workspace = new MultiVideoWorkspaceState(
                position.PresentationTime,
                TimelineSynchronizationMode.Independent,
                SynchronizedOperationScope.AllPanes,
                "pane-primary",
                pane.PaneId,
                pane.PaneId,
                range,
                new[] { pane });

            Assert.True(session.IsMediaOpen);
            Assert.True(session.HasAbsoluteFrameIdentity);
            Assert.Equal(ReviewPlaybackState.Closed, ReviewSessionSnapshot.FromEngineState(false, true));
            Assert.Equal(ReviewPlaybackState.Playing, ReviewSessionSnapshot.FromEngineState(true, true));
            Assert.Equal(ReviewPlaybackState.Paused, ReviewSessionSnapshot.FromEngineState(true, false));
            Assert.Equal(60L, loopIn.AbsoluteFrameIndex);
            Assert.True(loopIn.HasAbsoluteFrameIdentity);
            Assert.False(loopIn.IsPending);
            Assert.True(loopOut.IsPending);
            Assert.True(paneRange.HasAnyMarkers);
            Assert.True(paneRange.HasPendingMarkers);
            Assert.False(paneRange.IsInvalidRange);
            Assert.Equal(TimeSpan.FromSeconds(2), paneRange.EffectiveStartTime);
            Assert.Equal(TimeSpan.FromSeconds(10), paneRange.EffectiveEndTime);
            Assert.True(paneRange.TryGetRestartFrameIndex(out var restartFrameIndex));
            Assert.Equal(60L, restartFrameIndex);
            Assert.True(range.HasMarkers);
            Assert.True(range.TryGetPaneRange("pane-secondary", out var selectedRange));
            Assert.Same(paneRange, selectedRange);
            Assert.False(range.TryGetPaneRange("missing", out _));
            Assert.Equal(1, workspace.PaneCount);
            Assert.Same(pane, workspace.FocusedPane);
            Assert.Same(pane, workspace.ActivePane);
            Assert.Same(session, workspace.FocusedSession);
            Assert.Same(session, workspace.ActiveSession);
            Assert.Same(pane, workspace.PrimaryPane);
            Assert.True(workspace.TryGetPane(pane.PaneId, out var selectedPane));
            Assert.Same(pane, selectedPane);

            var paneSnapshot = new ReviewWorkspacePaneSnapshot(
                pane.PaneId,
                session.SessionId,
                pane.DisplayLabel,
                isBound: true,
                isPrimary: false,
                isFocused: true,
                isActive: true,
                pane.TimelineOffset,
                session.PlaybackState,
                session.CurrentFilePath,
                position,
                paneRange);
            var workspaceSnapshot = new ReviewWorkspaceSnapshot(
                position.PresentationTime,
                workspace.SynchronizationMode,
                workspace.DefaultOperationScope,
                workspace.PrimaryPaneId,
                workspace.ActivePaneId,
                workspace.FocusedPaneId,
                range,
                new[] { paneSnapshot });

            Assert.Equal(position.PresentationTime, paneSnapshot.PresentationTime);
            Assert.Equal(position.FrameIndex, paneSnapshot.FrameIndex);
            Assert.True(paneSnapshot.HasAbsoluteFrameIdentity);
            Assert.Equal(1, workspaceSnapshot.PaneCount);
            Assert.Same(paneSnapshot, workspaceSnapshot.FocusedPane);
            Assert.Same(paneSnapshot, workspaceSnapshot.ActivePane);
            Assert.True(workspaceSnapshot.TryGetPane(pane.PaneId, out var selectedSnapshot));
            Assert.Same(paneSnapshot, selectedSnapshot);
            Assert.False(workspaceSnapshot.TryGetPane("missing", out _));

            var step = FrameStepResult.Succeeded(1, position, wasCacheHit: true, requiredReconstruction: true, "decoded");
            var succeeded = CreatePaneOperationResult(
                "pane-primary",
                ReviewWorkspacePaneOperationOutcome.Succeeded,
                session,
                step,
                null);
            var failure = new InvalidOperationException("pane failed");
            var failed = CreatePaneOperationResult(
                "pane-secondary",
                ReviewWorkspacePaneOperationOutcome.Failed,
                session,
                FrameStepResult.Failed(-1, position, failure.Message, wasCacheHit: false, requiredReconstruction: true),
                failure);
            var skipped = CreatePaneOperationResult(
                "pane-tertiary",
                ReviewWorkspacePaneOperationOutcome.Skipped,
                session,
                null,
                null,
                wasAttempted: false);
            var operation = new ReviewWorkspaceOperationResult(
                "step",
                SynchronizedOperationScope.AllPanes,
                "pane-secondary",
                new[] { succeeded, failed, skipped, null });

            Assert.Equal(3, operation.PaneCount);
            Assert.False(operation.Succeeded);
            Assert.Equal(2, operation.AttemptedPaneCount);
            Assert.Equal(1, operation.SucceededPaneCount);
            Assert.Equal(1, operation.FailedPaneCount);
            Assert.Equal(1, operation.SkippedPaneCount);
            Assert.True(operation.HasFailures);
            Assert.True(operation.HasExceptionalFailures);
            Assert.Same(failed, operation.FocusedPaneResult);
            Assert.Same(failed, operation.FirstFailedPaneResult);
            Assert.Same(failed, operation.FirstExceptionalFailurePaneResult);
            Assert.True(operation.TryGetPaneResult("pane-primary", out var selectedResult));
            Assert.Same(succeeded, selectedResult);
            Assert.False(operation.TryGetPaneResult("missing", out _));
            Assert.True(succeeded.Succeeded);
            Assert.False(succeeded.Failed);
            Assert.False(succeeded.Skipped);
            Assert.Same(position, succeeded.Position);
            Assert.True(step.WasCacheHit);
            Assert.True(step.RequiredReconstruction);

            var idlePreparation = new ReviewWorkspacePreparationState(ReviewWorkspacePreparationPhase.Idle, null);
            var openingPreparation = new ReviewWorkspacePreparationState(
                ReviewWorkspacePreparationPhase.Opening,
                "/video.mov");
            var preparingFirstFrame = new ReviewWorkspacePreparationState(
                ReviewWorkspacePreparationPhase.PreparingFirstFrame,
                "/video.mov");
            Assert.False(idlePreparation.IsActive);
            Assert.True(openingPreparation.IsActive);
            Assert.True(preparingFirstFrame.IsActive);

            var pendingRange = new LoopPlaybackPaneRangeSnapshot(
                "pane-secondary",
                "secondary",
                "Secondary",
                "/video.mov",
                TimeSpan.Zero,
                new LoopPlaybackAnchorSnapshot("pane-secondary", "secondary", "Secondary", TimeSpan.Zero, null),
                null);
            Assert.False(pendingRange.TryGetRestartFrameIndex(out var pendingRestartIndex));
            Assert.Equal(0L, pendingRestartIndex);
            Assert.True(new LoopPlaybackPaneRangeSnapshot(
                "pane-secondary",
                "secondary",
                "Secondary",
                "/video.mov",
                TimeSpan.FromSeconds(10),
                loopOut,
                loopIn).IsInvalidRange);
        }

        [Fact]
        public void Coordinator_BindsSelectsAndSnapshotsPanes()
        {
            var primaryEngine = new TestVideoReviewEngine();
            using var primarySession = new ReviewSessionCoordinator(primaryEngine);
            using var coordinator = new ReviewWorkspaceCoordinator(primaryEngine, primarySession);
            var workspaceChangedCount = 0;
            coordinator.WorkspaceChanged += (_, _) => workspaceChangedCount++;

            Assert.Equal(1, coordinator.PaneBindingCount);
            Assert.Single(coordinator.GetBoundPanes());
            Assert.True(coordinator.TryGetBoundPane("pane-primary", out var primaryPane));
            Assert.True(primaryPane.IsPrimary);
            Assert.True(coordinator.TryGetPaneSnapshot("pane-primary", out var primarySnapshot));
            Assert.True(primarySnapshot.IsPrimary);
            Assert.False(coordinator.TrySetActivePane("missing"));
            Assert.False(coordinator.TrySetFocusedPane(string.Empty));

            var secondaryEngine = new TestVideoReviewEngine();
            using var secondarySession = new ReviewSessionCoordinator(
                secondaryEngine,
                "secondary",
                "Secondary");
            Assert.True(coordinator.TryBindPane(
                "pane-secondary",
                secondarySession,
                "Second view",
                TimeSpan.FromMilliseconds(25),
                makeActive: true,
                makeFocused: true));
            Assert.False(coordinator.TryBindPane("pane-secondary", secondarySession));
            Assert.Equal(2, coordinator.PaneBindingCount);
            Assert.True(coordinator.TrySetActivePane("pane-primary"));
            Assert.True(coordinator.TrySetFocusedPane("pane-secondary"));
            Assert.True(coordinator.TrySetActiveAndFocusedPane("pane-primary"));
            Assert.True(coordinator.TrySelectPane("pane-secondary", WorkspacePaneSelectionMode.Active));
            Assert.True(coordinator.TrySelectPane("pane-primary", WorkspacePaneSelectionMode.Focused));

            var snapshot = coordinator.GetWorkspaceSnapshot();
            Assert.Equal(2, snapshot.PaneCount);
            Assert.Equal("pane-secondary", snapshot.ActivePaneId);
            Assert.Equal("pane-primary", snapshot.FocusedPaneId);
            Assert.Equal("pane-primary", snapshot.PrimaryPaneId);
            Assert.NotNull(snapshot.ActivePane);
            Assert.NotNull(snapshot.FocusedPane);
            Assert.NotNull(snapshot.PrimaryPane);
            Assert.True(workspaceChangedCount > 0);

            var refreshed = coordinator.RefreshWorkspaceFromEngines();
            Assert.Equal(2, refreshed.PaneCount);
            Assert.Same(primarySession.CurrentSession, coordinator.RefreshFromEngine());
            Assert.Equal("reset.mov", coordinator.Reset("reset.mov").CurrentFilePath);
        }

        [Fact]
        public async Task Coordinator_OpensClosesAndReportsPreparationState()
        {
            var engine = new TestVideoReviewEngine();
            engine.SetOpenState("/old.mov", TimeSpan.FromSeconds(1), 30L);
            using var session = new ReviewSessionCoordinator(engine);
            using var coordinator = new ReviewWorkspaceCoordinator(session);
            var phases = new List<ReviewWorkspacePreparationPhase>();
            coordinator.PreparationStateChanged += (_, args) => phases.Add(args.CurrentState.Phase);

            await coordinator.OpenAsync("/new.mov");

            Assert.Equal(1, engine.CloseCallCount);
            Assert.Equal(1, engine.OpenCallCount);
            Assert.Equal(1, engine.PauseCallCount);
            Assert.Equal(TimeSpan.Zero, Assert.Single(engine.SeekTimeRequests));
            Assert.Equal(ReviewWorkspacePreparationPhase.Ready, coordinator.CurrentPreparationState.Phase);
            Assert.Equal(
                new[]
                {
                    ReviewWorkspacePreparationPhase.Opening,
                    ReviewWorkspacePreparationPhase.PreparingFirstFrame,
                    ReviewWorkspacePreparationPhase.Ready
                },
                phases);

            engine.OpenException = new InvalidOperationException("open failed");
            await Assert.ThrowsAsync<InvalidOperationException>(() => coordinator.OpenAsync("/bad.mov"));
            Assert.Equal(ReviewWorkspacePreparationPhase.Failed, coordinator.CurrentPreparationState.Phase);
            Assert.Equal("/bad.mov", coordinator.CurrentPreparationState.TargetFilePath);

            engine.OpenException = null;
            await coordinator.CloseAsync();
            Assert.False(engine.IsMediaOpen);
            Assert.Equal(ReviewWorkspacePreparationPhase.Idle, coordinator.CurrentPreparationState.Phase);
        }

        [Fact]
        public async Task Coordinator_ExecutesScopedTransportAndPreservesPerPaneFailures()
        {
            var primaryEngine = new TestVideoReviewEngine();
            primaryEngine.SetOpenState("/primary.mov", TimeSpan.FromSeconds(1), 30L);
            var secondaryEngine = new TestVideoReviewEngine();
            secondaryEngine.SetOpenState("/secondary.mov", TimeSpan.FromSeconds(2), 60L);
            using var primarySession = new ReviewSessionCoordinator(primaryEngine);
            using var secondarySession = new ReviewSessionCoordinator(secondaryEngine, "secondary", "Secondary");
            using var coordinator = new ReviewWorkspaceCoordinator(primarySession);
            Assert.True(coordinator.TryBindPane("pane-secondary", secondarySession));

            var play = await coordinator.PlayWithPaneResultsAsync(SynchronizedOperationScope.AllPanes);
            Assert.True(play.Succeeded);
            Assert.Equal(2, play.SucceededPaneCount);
            Assert.Equal(1, primaryEngine.PlayCallCount);
            Assert.Equal(1, secondaryEngine.PlayCallCount);

            var pause = await coordinator.PauseWithPaneResultsAsync(SynchronizedOperationScope.AllPanes);
            Assert.True(pause.Succeeded);
            await coordinator.SeekToTimeAsync(TimeSpan.FromSeconds(3), SynchronizedOperationScope.AllPanes);
            await coordinator.SeekToFrameAsync(90L, SynchronizedOperationScope.AllPanes);
            Assert.Equal(2, primaryEngine.SeekTimeRequests.Count + secondaryEngine.SeekTimeRequests.Count);
            Assert.Equal(2, primaryEngine.SeekFrameRequests.Count + secondaryEngine.SeekFrameRequests.Count);

            var forward = await coordinator.StepForwardWithPaneResultsAsync(SynchronizedOperationScope.AllPanes);
            var backward = await coordinator.StepBackwardWithPaneResultsAsync(SynchronizedOperationScope.AllPanes);
            Assert.True(forward.Succeeded);
            Assert.True(backward.Succeeded);
            Assert.True((await coordinator.StepForwardAsync()).Success);
            Assert.True((await coordinator.StepBackwardAsync()).Success);

            await coordinator.PlayPaneAsync("pane-secondary");
            await coordinator.PausePaneAsync("pane-secondary");
            await coordinator.SeekPaneToTimeAsync("pane-secondary", TimeSpan.FromSeconds(4));
            await coordinator.SeekPaneToFrameAsync("pane-secondary", 120L);
            await Assert.ThrowsAsync<InvalidOperationException>(() => coordinator.PlayPaneAsync("missing"));

            await coordinator.SeekToPaneFramesAsync(new Dictionary<string, long>
            {
                ["pane-primary"] = -5L,
                ["pane-secondary"] = 125L
            });
            await coordinator.SeekToPaneFramesAsync(new Dictionary<string, long>());
            Assert.Contains(0L, primaryEngine.SeekFrameRequests);
            Assert.Contains(125L, secondaryEngine.SeekFrameRequests);

            await coordinator.SeekToPaneTimesAsync(new Dictionary<string, TimeSpan>
            {
                ["pane-primary"] = TimeSpan.FromSeconds(-1),
                ["pane-secondary"] = TimeSpan.FromSeconds(5)
            });
            await coordinator.SeekToPaneTimesAsync(new Dictionary<string, TimeSpan>());
            Assert.Contains(TimeSpan.Zero, primaryEngine.SeekTimeRequests);
            Assert.Contains(TimeSpan.FromSeconds(5), secondaryEngine.SeekTimeRequests);

            secondaryEngine.PlayException = new InvalidOperationException("secondary play failed");
            var failedPlay = await coordinator.PlayWithPaneResultsAsync(SynchronizedOperationScope.AllPanes);
            Assert.False(failedPlay.Succeeded);
            Assert.Equal(1, failedPlay.FailedPaneCount);
            Assert.True(failedPlay.HasExceptionalFailures);
            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                coordinator.PlayAsync(SynchronizedOperationScope.AllPanes));
            secondaryEngine.PlayException = null;

            secondaryEngine.SeekFrameException = new InvalidOperationException("secondary seek failed");
            await Assert.ThrowsAsync<InvalidOperationException>(() => coordinator.SeekToPaneFramesAsync(
                new Dictionary<string, long> { ["pane-secondary"] = 10L }));
            secondaryEngine.SeekFrameException = null;
            secondaryEngine.SeekTimeException = new InvalidOperationException("secondary time failed");
            await Assert.ThrowsAsync<InvalidOperationException>(() => coordinator.SeekToPaneTimesAsync(
                new Dictionary<string, TimeSpan> { ["pane-secondary"] = TimeSpan.FromSeconds(1) }));
            secondaryEngine.SeekTimeException = null;

            Assert.True(coordinator.TrySetFocusedPane("pane-secondary"));
            secondaryEngine.ForwardResult = FrameStepResult.Failed(1, secondaryEngine.Position, "no next frame");
            var failedStep = await coordinator.StepForwardAsync();
            Assert.False(failedStep.Success);
            Assert.Equal("no next frame", failedStep.Message);
        }

        [Fact]
        public void Coordinator_CapturesAndClearsSharedAndPaneLoopRanges()
        {
            var primaryEngine = new TestVideoReviewEngine();
            primaryEngine.SetOpenState("/primary.mov", TimeSpan.FromSeconds(2), 60L);
            var secondaryEngine = new TestVideoReviewEngine();
            secondaryEngine.SetOpenState("/secondary.mov", TimeSpan.FromSeconds(3), 90L);
            using var primarySession = new ReviewSessionCoordinator(primaryEngine);
            using var secondarySession = new ReviewSessionCoordinator(secondaryEngine, "secondary", "Secondary");
            using var coordinator = new ReviewWorkspaceCoordinator(primarySession);
            Assert.True(coordinator.TryBindPane("pane-secondary", secondarySession));

            var sharedIn = coordinator.SetSharedLoopMarker(
                LoopPlaybackMarkerEndpoint.In,
                SynchronizedOperationScope.AllPanes);
            Assert.Equal(2, sharedIn.TargetPaneCount);
            Assert.True(sharedIn.HasMarkers);

            primaryEngine.SetOpenState("/primary.mov", TimeSpan.FromSeconds(5), 150L);
            secondaryEngine.SetOpenState("/secondary.mov", TimeSpan.FromSeconds(6), 180L);
            var sharedRange = coordinator.SetSharedLoopMarker(
                LoopPlaybackMarkerEndpoint.Out,
                SynchronizedOperationScope.AllPanes);
            Assert.True(sharedRange.TryGetPaneRange("pane-primary", out var primaryRange));
            Assert.True(sharedRange.TryGetPaneRange("pane-secondary", out var secondaryRange));
            Assert.Equal(TimeSpan.FromSeconds(5), primaryRange.LoopOut.PresentationTime);
            Assert.Equal(TimeSpan.FromSeconds(6), secondaryRange.LoopOut.PresentationTime);

            var localIn = coordinator.SetPaneLoopMarker("pane-secondary", LoopPlaybackMarkerEndpoint.In);
            Assert.NotNull(localIn);
            secondaryEngine.SetOpenState("/secondary.mov", TimeSpan.FromSeconds(8), 240L);
            var localRange = coordinator.SetPaneLoopMarker("pane-secondary", LoopPlaybackMarkerEndpoint.Out);
            Assert.NotNull(localRange);
            Assert.Equal(TimeSpan.FromSeconds(8), localRange.LoopOut.PresentationTime);
            Assert.Null(coordinator.SetPaneLoopMarker("missing", LoopPlaybackMarkerEndpoint.In));

            coordinator.ClearPaneLoopRange("pane-secondary");
            Assert.Null(coordinator.GetWorkspaceSnapshot().Panes[1].LoopRange);
            coordinator.ClearPaneLoopRange("pane-secondary");
            coordinator.ClearPaneLoopRange(string.Empty);
            coordinator.ClearSharedLoopRange();
            Assert.False(coordinator.CurrentWorkspace.SharedLoopRange.HasMarkers);
            coordinator.ClearSharedLoopRange();
        }

        private static ReviewWorkspacePaneOperationResult CreatePaneOperationResult(
            string paneId,
            ReviewWorkspacePaneOperationOutcome outcome,
            ReviewSessionSnapshot session,
            FrameStepResult? frameStepResult,
            Exception? failureException,
            bool wasAttempted = true)
        {
            return new ReviewWorkspacePaneOperationResult(
                paneId,
                session.SessionId,
                session.DisplayLabel,
                wasTargeted: true,
                wasAttempted,
                outcome,
                session,
                failureException?.Message ?? string.Empty,
                failureException?.GetType().FullName ?? string.Empty,
                frameStepResult,
                failureException);
        }

        private static VideoMediaInfo CreateMediaInfo(string filePath, TimeSpan duration)
        {
            return new VideoMediaInfo(
                filePath,
                duration,
                TimeSpan.FromSeconds(1d / 30d),
                30d,
                1920,
                1080,
                "h264",
                0,
                30,
                1,
                1,
                90_000);
        }

        private sealed class TestVideoReviewEngine : IVideoReviewEngine
        {
            public bool IsMediaOpen { get; private set; }

            public bool IsPlaying { get; private set; }

            public string CurrentFilePath { get; private set; } = string.Empty;

            public string LastErrorMessage { get; private set; } = string.Empty;

            public VideoMediaInfo MediaInfo { get; private set; } = VideoMediaInfo.Empty;

            public ReviewPosition Position { get; private set; } = ReviewPosition.Empty;

            public int OpenCallCount { get; private set; }

            public int CloseCallCount { get; private set; }

            public int PlayCallCount { get; private set; }

            public int PauseCallCount { get; private set; }

            public List<TimeSpan> SeekTimeRequests { get; } = new List<TimeSpan>();

            public List<long> SeekFrameRequests { get; } = new List<long>();

            public Exception? OpenException { get; set; }

            public Exception? PlayException { get; set; }

            public Exception? SeekTimeException { get; set; }

            public Exception? SeekFrameException { get; set; }

            public FrameStepResult? ForwardResult { get; set; }

            public FrameStepResult? BackwardResult { get; set; }

            public event EventHandler<VideoReviewEngineStateChangedEventArgs> StateChanged
            {
                add { _stateChanged += value; }
                remove { _stateChanged -= value; }
            }

            public event EventHandler<FramePresentedEventArgs> FramePresented
            {
                add { }
                remove { }
            }

            private event EventHandler<VideoReviewEngineStateChangedEventArgs>? _stateChanged;

            public Task OpenAsync(string filePath, CancellationToken cancellationToken = default)
            {
                cancellationToken.ThrowIfCancellationRequested();
                OpenCallCount++;
                if (OpenException != null)
                {
                    throw OpenException;
                }

                SetOpenState(filePath, TimeSpan.Zero, 0L);
                return Task.CompletedTask;
            }

            public Task CloseAsync()
            {
                CloseCallCount++;
                IsMediaOpen = false;
                IsPlaying = false;
                CurrentFilePath = string.Empty;
                MediaInfo = VideoMediaInfo.Empty;
                Position = ReviewPosition.Empty;
                RaiseStateChanged();
                return Task.CompletedTask;
            }

            public Task PlayAsync()
            {
                PlayCallCount++;
                if (PlayException != null)
                {
                    throw PlayException;
                }

                IsPlaying = true;
                RaiseStateChanged();
                return Task.CompletedTask;
            }

            public Task PauseAsync()
            {
                PauseCallCount++;
                IsPlaying = false;
                RaiseStateChanged();
                return Task.CompletedTask;
            }

            public Task<FrameStepResult> StepForwardAsync(CancellationToken cancellationToken = default)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var result = ForwardResult ?? FrameStepResult.Succeeded(1, MoveByFrames(1L));
                Position = result.Position;
                RaiseStateChanged();
                return Task.FromResult(result);
            }

            public Task<FrameStepResult> StepBackwardAsync(CancellationToken cancellationToken = default)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var result = BackwardResult ?? FrameStepResult.Succeeded(-1, MoveByFrames(-1L));
                Position = result.Position;
                RaiseStateChanged();
                return Task.FromResult(result);
            }

            public Task SeekToTimeAsync(TimeSpan position, CancellationToken cancellationToken = default)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (SeekTimeException != null)
                {
                    throw SeekTimeException;
                }

                SeekTimeRequests.Add(position);
                var frameIndex = Math.Max(0L, (long)Math.Round(position.TotalSeconds * 30d));
                Position = CreatePosition(position, frameIndex);
                RaiseStateChanged();
                return Task.CompletedTask;
            }

            public Task SeekToFrameAsync(long frameIndex, CancellationToken cancellationToken = default)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (SeekFrameException != null)
                {
                    throw SeekFrameException;
                }

                SeekFrameRequests.Add(frameIndex);
                Position = CreatePosition(TimeSpan.FromSeconds(frameIndex / 30d), frameIndex);
                RaiseStateChanged();
                return Task.CompletedTask;
            }

            public void SetOpenState(string filePath, TimeSpan position, long frameIndex)
            {
                IsMediaOpen = true;
                CurrentFilePath = filePath;
                MediaInfo = CreateMediaInfo(filePath, TimeSpan.FromSeconds(30));
                Position = CreatePosition(position, frameIndex);
                RaiseStateChanged();
            }

            public void Dispose()
            {
            }

            private ReviewPosition MoveByFrames(long delta)
            {
                var frameIndex = Math.Max(0L, Position.FrameIndex.GetValueOrDefault() + delta);
                return CreatePosition(TimeSpan.FromSeconds(frameIndex / 30d), frameIndex);
            }

            private static ReviewPosition CreatePosition(TimeSpan position, long frameIndex)
            {
                return new ReviewPosition(
                    position,
                    frameIndex,
                    isFrameAccurate: true,
                    isFrameIndexAbsolute: true,
                    presentationTimestamp: frameIndex * 3_000L,
                    decodeTimestamp: frameIndex * 3_000L);
            }

            private void RaiseStateChanged()
            {
                _stateChanged?.Invoke(
                    this,
                    new VideoReviewEngineStateChangedEventArgs(
                        IsMediaOpen,
                        IsPlaying,
                        CurrentFilePath,
                        LastErrorMessage,
                        MediaInfo,
                        Position));
            }
        }
    }
}
