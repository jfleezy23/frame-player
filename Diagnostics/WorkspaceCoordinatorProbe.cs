using System;
using System.Diagnostics.CodeAnalysis;
using System.Threading;
using System.Threading.Tasks;
using FramePlayer.Core.Abstractions;
using FramePlayer.Core.Coordination;
using FramePlayer.Core.Events;
using FramePlayer.Core.Models;

namespace FramePlayer.Diagnostics
{
    public static class WorkspaceCoordinatorProbe
    {
        private const string PrimaryPaneId = "pane-primary";
        private const string ComparePaneId = "pane-compare-a";

        [SuppressMessage(
            "Major Code Smell",
            "S3776:Cognitive Complexity of methods should not be too high",
            Justification = "This is a diagnostics-only workspace probe that intentionally exercises many coordination branches in one place to keep the synthetic scenario self-contained.")]
        public static WorkspaceCoordinatorProbeReport Run()
        {
            ReviewWorkspacePaneOperationResult GetPaneResult(
                ReviewWorkspaceOperationResult operationResult,
                string paneId)
            {
                if (operationResult != null &&
                    operationResult.TryGetPaneResult(paneId, out var paneResult))
                {
                    return paneResult;
                }

                return null;
            }

            string GetPaneIdAt(ReviewWorkspaceOperationResult operationResult, int index)
            {
                return operationResult != null &&
                       operationResult.PaneResults != null &&
                       index >= 0 &&
                       index < operationResult.PaneResults.Count &&
                       operationResult.PaneResults[index] != null
                    ? operationResult.PaneResults[index].PaneId
                    : string.Empty;
            }

            bool HasNoStepPayloads(ReviewWorkspaceOperationResult operationResult)
            {
                if (operationResult == null || operationResult.PaneResults == null)
                {
                    return false;
                }

                for (var index = 0; index < operationResult.PaneResults.Count; index++)
                {
                    var paneResult = operationResult.PaneResults[index];
                    if (paneResult == null || paneResult.FrameStepResult != null)
                    {
                        return false;
                    }
                }

                return true;
            }

            bool HasStepPayloads(ReviewWorkspaceOperationResult operationResult)
            {
                if (operationResult == null || operationResult.PaneResults == null)
                {
                    return false;
                }

                for (var index = 0; index < operationResult.PaneResults.Count; index++)
                {
                    var paneResult = operationResult.PaneResults[index];
                    if (paneResult == null || paneResult.FrameStepResult == null)
                    {
                        return false;
                    }
                }

                return true;
            }

            bool AllAttemptedWithOutcome(
                ReviewWorkspaceOperationResult operationResult,
                ReviewWorkspacePaneOperationOutcome expectedOutcome)
            {
                if (operationResult == null || operationResult.PaneResults == null)
                {
                    return false;
                }

                for (var index = 0; index < operationResult.PaneResults.Count; index++)
                {
                    var paneResult = operationResult.PaneResults[index];
                    if (paneResult == null ||
                        !paneResult.WasTargeted ||
                        !paneResult.WasAttempted ||
                        paneResult.OutcomeStatus != expectedOutcome)
                    {
                        return false;
                    }
                }

                return true;
            }

            using (var primaryEngine = new ProbeVideoReviewEngine(
                "probe-primary.mp4",
                new ReviewPosition(TimeSpan.FromSeconds(1d), 30L, true, true, null, null)))
            using (var compareEngine = new ProbeVideoReviewEngine(
                "probe-compare-a.mp4",
                new ReviewPosition(TimeSpan.FromSeconds(2d), 60L, true, true, null, null)))
            using (var primarySessionCoordinator = new ReviewSessionCoordinator(
                primaryEngine,
                "session-primary",
                "Primary"))
            using (var compareSessionCoordinator = new ReviewSessionCoordinator(
                compareEngine,
                "session-compare-a",
                "Compare A"))
            {
                var workspaceCoordinator = new ReviewWorkspaceCoordinator(primaryEngine, primarySessionCoordinator);
                try
                {
                    primarySessionCoordinator.RefreshFromEngine();
                    compareSessionCoordinator.RefreshFromEngine();

                    var secondPaneBound = workspaceCoordinator.TryBindPane(
                        ComparePaneId,
                        compareSessionCoordinator,
                        "Compare A",
                        TimeSpan.FromMilliseconds(120d));

                    var beforeSelectionPanes = workspaceCoordinator.GetBoundPanes();
                    var workspaceSnapshotBeforeSelection = workspaceCoordinator.GetWorkspaceSnapshot();
                    var secondPaneLookupBeforeSelection = workspaceCoordinator.TryGetBoundPane(ComparePaneId, out var comparePaneBeforeSelection);
                    var snapshotPrimaryLookupBeforeSelection = workspaceCoordinator.TryGetPaneSnapshot(PrimaryPaneId, out var primarySnapshotBeforeSelection);
                    var snapshotSecondaryLookupBeforeSelection = workspaceCoordinator.TryGetPaneSnapshot(ComparePaneId, out var secondarySnapshotBeforeSelection);
                    var snapshotMissingPaneLookupFails = !workspaceCoordinator.TryGetPaneSnapshot("pane-missing", out _);
                    var focusedSecondary = workspaceCoordinator.TrySelectPane(ComparePaneId, WorkspacePaneSelectionMode.Focused);
                    var focusedPlayResult = workspaceCoordinator
                        .PlayWithPaneResultsAsync(SynchronizedOperationScope.FocusedPane)
                        .GetAwaiter()
                        .GetResult();
                    var focusedPauseResult = workspaceCoordinator
                        .PauseWithPaneResultsAsync(SynchronizedOperationScope.FocusedPane)
                        .GetAwaiter()
                        .GetResult();
                    var focusedSeekResult = workspaceCoordinator
                        .SeekToTimeWithPaneResultsAsync(TimeSpan.FromSeconds(3d), SynchronizedOperationScope.FocusedPane)
                        .GetAwaiter()
                        .GetResult();
                    var focusedStepBackwardResult = workspaceCoordinator
                        .StepBackwardWithPaneResultsAsync(SynchronizedOperationScope.FocusedPane)
                        .GetAwaiter()
                        .GetResult();
                    var focusedStepForwardResult = workspaceCoordinator
                        .StepForwardWithPaneResultsAsync(SynchronizedOperationScope.FocusedPane)
                        .GetAwaiter()
                        .GetResult();
                    var focusedPlayPaneResult = focusedPlayResult != null ? focusedPlayResult.FocusedPaneResult : null;
                    var workspaceWhileSecondaryFocused = workspaceCoordinator.CurrentWorkspace;
                    var workspaceSnapshotWhileSecondaryFocused = workspaceCoordinator.GetWorkspaceSnapshot();
                    var snapshotSecondaryLookupWhileSecondaryFocused = workspaceCoordinator.TryGetPaneSnapshot(ComparePaneId, out var secondarySnapshotWhileSecondaryFocused);
                    var primaryPlayCallsAfterFocusedSecondary = primaryEngine.PlayCallCount;
                    var primaryPauseCallsAfterFocusedSecondary = primaryEngine.PauseCallCount;
                    var primarySeekToTimeCallsAfterFocusedSecondary = primaryEngine.SeekToTimeCallCount;
                    var primaryStepBackwardCallsAfterFocusedSecondary = primaryEngine.StepBackwardCallCount;
                    var primaryStepForwardCallsAfterFocusedSecondary = primaryEngine.StepForwardCallCount;
                    var secondaryPlayCallsAfterFocusedSecondary = compareEngine.PlayCallCount;
                    var secondaryPauseCallsAfterFocusedSecondary = compareEngine.PauseCallCount;
                    var secondarySeekToTimeCallsAfterFocusedSecondary = compareEngine.SeekToTimeCallCount;
                    var secondaryStepBackwardCallsAfterFocusedSecondary = compareEngine.StepBackwardCallCount;
                    var secondaryStepForwardCallsAfterFocusedSecondary = compareEngine.StepForwardCallCount;

                    var allPanePlayResult = workspaceCoordinator
                        .PlayWithPaneResultsAsync(SynchronizedOperationScope.AllPanes)
                        .GetAwaiter()
                        .GetResult();
                    var allPanePauseResult = workspaceCoordinator
                        .PauseWithPaneResultsAsync(SynchronizedOperationScope.AllPanes)
                        .GetAwaiter()
                        .GetResult();
                    var allPaneSeekResult = workspaceCoordinator
                        .SeekToTimeWithPaneResultsAsync(TimeSpan.FromSeconds(4d), SynchronizedOperationScope.AllPanes)
                        .GetAwaiter()
                        .GetResult();
                    var allPaneStepBackwardResult = workspaceCoordinator
                        .StepBackwardWithPaneResultsAsync(SynchronizedOperationScope.AllPanes)
                        .GetAwaiter()
                        .GetResult();
                    var allPaneStepForwardResult = workspaceCoordinator
                        .StepForwardWithPaneResultsAsync(SynchronizedOperationScope.AllPanes)
                        .GetAwaiter()
                        .GetResult();
                    var primaryPlayCallsAfterAllPanes = primaryEngine.PlayCallCount;
                    var primaryPauseCallsAfterAllPanes = primaryEngine.PauseCallCount;
                    var primarySeekToTimeCallsAfterAllPanes = primaryEngine.SeekToTimeCallCount;
                    var primaryStepBackwardCallsAfterAllPanes = primaryEngine.StepBackwardCallCount;
                    var primaryStepForwardCallsAfterAllPanes = primaryEngine.StepForwardCallCount;
                    var secondaryPlayCallsAfterAllPanes = compareEngine.PlayCallCount;
                    var secondaryPauseCallsAfterAllPanes = compareEngine.PauseCallCount;
                    var secondarySeekToTimeCallsAfterAllPanes = compareEngine.SeekToTimeCallCount;
                    var secondaryStepBackwardCallsAfterAllPanes = compareEngine.StepBackwardCallCount;
                    var secondaryStepForwardCallsAfterAllPanes = compareEngine.StepForwardCallCount;

                    var allPanePlayPrimaryResult = GetPaneResult(allPanePlayResult, PrimaryPaneId);
                    var allPanePlaySecondaryResult = GetPaneResult(allPanePlayResult, ComparePaneId);
                    var allPaneSeekPrimaryResult = GetPaneResult(allPaneSeekResult, PrimaryPaneId);
                    var allPaneSeekSecondaryResult = GetPaneResult(allPaneSeekResult, ComparePaneId);
                    var allPaneStepBackwardPrimaryResult = GetPaneResult(allPaneStepBackwardResult, PrimaryPaneId);
                    var allPaneStepBackwardSecondaryResult = GetPaneResult(allPaneStepBackwardResult, ComparePaneId);
                    var allPaneStepForwardPrimaryResult = GetPaneResult(allPaneStepForwardResult, PrimaryPaneId);
                    var allPaneStepForwardSecondaryResult = GetPaneResult(allPaneStepForwardResult, ComparePaneId);
                    var snapshotPrimaryLookupAfterAllPaneOperations = workspaceCoordinator.TryGetPaneSnapshot(PrimaryPaneId, out var primarySnapshotAfterAllPaneOperations);
                    var snapshotSecondaryLookupAfterAllPaneOperations = workspaceCoordinator.TryGetPaneSnapshot(ComparePaneId, out var secondarySnapshotAfterAllPaneOperations);

                    bool failureSecondPaneBound;
                    ReviewWorkspaceOperationResult failureAllPanePlayResult;
                    int failurePrimaryPlayCalls;
                    int failureSecondaryPlayCalls;
                    using (var failurePrimaryEngine = new ProbeVideoReviewEngine(
                        "probe-failure-primary.mp4",
                        new ReviewPosition(TimeSpan.FromSeconds(1d), 30L, true, true, null, null)))
                    using (var failureCompareEngine = new ProbeVideoReviewEngine(
                        "probe-failure-compare-a.mp4",
                        new ReviewPosition(TimeSpan.FromSeconds(2d), 60L, true, true, null, null),
                        throwOnPlay: true))
                    using (var failurePrimarySessionCoordinator = new ReviewSessionCoordinator(
                        failurePrimaryEngine,
                        "session-failure-primary",
                        "Failure Primary"))
                    using (var failureCompareSessionCoordinator = new ReviewSessionCoordinator(
                        failureCompareEngine,
                        "session-failure-compare-a",
                        "Failure Compare A"))
                    {
                        var failureWorkspaceCoordinator = new ReviewWorkspaceCoordinator(
                            failurePrimaryEngine,
                            failurePrimarySessionCoordinator);
                        try
                        {
                            failurePrimarySessionCoordinator.RefreshFromEngine();
                            failureCompareSessionCoordinator.RefreshFromEngine();
                            failureSecondPaneBound = failureWorkspaceCoordinator.TryBindPane(
                                "pane-failure-compare-a",
                                failureCompareSessionCoordinator,
                                "Failure Compare A",
                                TimeSpan.Zero);
                            failureWorkspaceCoordinator.TrySelectPane(
                                "pane-failure-compare-a",
                                WorkspacePaneSelectionMode.Focused);
                            failureAllPanePlayResult = failureWorkspaceCoordinator
                                .PlayWithPaneResultsAsync(SynchronizedOperationScope.AllPanes)
                                .GetAwaiter()
                                .GetResult();
                            failurePrimaryPlayCalls = failurePrimaryEngine.PlayCallCount;
                            failureSecondaryPlayCalls = failureCompareEngine.PlayCallCount;
                        }
                        finally
                        {
                            failureWorkspaceCoordinator.Dispose();
                        }
                    }
                    var failureFirstPaneResult = failureAllPanePlayResult != null &&
                                                 failureAllPanePlayResult.PaneResults != null &&
                                                 failureAllPanePlayResult.PaneResults.Count > 0
                        ? failureAllPanePlayResult.PaneResults[0]
                        : null;
                    var failureSecondPaneResult = failureAllPanePlayResult != null &&
                                                  failureAllPanePlayResult.PaneResults != null &&
                                                  failureAllPanePlayResult.PaneResults.Count > 1
                        ? failureAllPanePlayResult.PaneResults[1]
                        : null;

                    var focusedPrimaryAgain = workspaceCoordinator.TrySelectPane(PrimaryPaneId, WorkspacePaneSelectionMode.Focused);
                    workspaceCoordinator.PlayAsync(SynchronizedOperationScope.FocusedPane).GetAwaiter().GetResult();
                    workspaceCoordinator.PauseAsync(SynchronizedOperationScope.FocusedPane).GetAwaiter().GetResult();
                    workspaceCoordinator.SeekToTimeAsync(TimeSpan.FromSeconds(5d), SynchronizedOperationScope.FocusedPane).GetAwaiter().GetResult();
                    workspaceCoordinator.StepBackwardAsync(SynchronizedOperationScope.FocusedPane).GetAwaiter().GetResult();
                    workspaceCoordinator.StepForwardAsync(SynchronizedOperationScope.FocusedPane).GetAwaiter().GetResult();

                    var workspace = workspaceCoordinator.CurrentWorkspace;
                    return new WorkspaceCoordinatorProbeReport
                    {
                        PaneCount = workspace.PaneCount,
                        WorkspacePrimaryPaneId = workspace.PrimaryPaneId,
                        ActivePaneId = workspace.ActivePaneId,
                        FocusedPaneId = workspace.FocusedPaneId,
                        PaneIds = workspace.Panes != null
                            ? Array.ConvertAll(
                                new System.Collections.Generic.List<ReviewPaneState>(workspace.Panes).ToArray(),
                                pane => pane != null ? pane.PaneId : string.Empty)
                            : new string[0],
                        BoundPaneCountBeforeSelection = beforeSelectionPanes != null ? beforeSelectionPanes.Count : 0,
                        WorkspaceSnapshotPaneCount = workspaceSnapshotBeforeSelection != null ? workspaceSnapshotBeforeSelection.PaneCount : 0,
                        AddedSecondPane = secondPaneBound,
                        SecondPaneLookupBeforeSelection = secondPaneLookupBeforeSelection,
                        SecondPaneDisplayLabelBeforeSelection = comparePaneBeforeSelection != null ? comparePaneBeforeSelection.DisplayLabel : string.Empty,
                        SecondPanePresent = workspace.TryGetPane(ComparePaneId, out var comparePane),
                        SnapshotPrimaryLookupBeforeSelection = snapshotPrimaryLookupBeforeSelection,
                        SnapshotSecondaryLookupBeforeSelection = snapshotSecondaryLookupBeforeSelection,
                        SnapshotMissingPaneLookupFails = snapshotMissingPaneLookupFails,
                        SnapshotPrimarySessionIdBeforeSelection = primarySnapshotBeforeSelection != null ? primarySnapshotBeforeSelection.SessionId : string.Empty,
                        SnapshotSecondarySessionIdBeforeSelection = secondarySnapshotBeforeSelection != null ? secondarySnapshotBeforeSelection.SessionId : string.Empty,
                        SnapshotPrimaryIsBoundBeforeSelection = primarySnapshotBeforeSelection != null && primarySnapshotBeforeSelection.IsBound,
                        SnapshotSecondaryIsBoundBeforeSelection = secondarySnapshotBeforeSelection != null && secondarySnapshotBeforeSelection.IsBound,
                        SnapshotPrimaryIsPrimaryBeforeSelection = primarySnapshotBeforeSelection != null && primarySnapshotBeforeSelection.IsPrimary,
                        SnapshotSecondaryIsPrimaryBeforeSelection = secondarySnapshotBeforeSelection != null && secondarySnapshotBeforeSelection.IsPrimary,
                        FocusedSecondary = focusedSecondary,
                        FocusedPrimaryAgain = focusedPrimaryAgain,
                        FocusedPlayResultPaneCount = focusedPlayResult != null ? focusedPlayResult.PaneCount : 0,
                        FocusedPlayResultTargetPaneId = focusedPlayResult != null && focusedPlayResult.FocusedPaneResult != null
                            ? focusedPlayResult.FocusedPaneResult.PaneId
                            : string.Empty,
                        FocusedPlayAttemptedAndSucceeded = focusedPlayPaneResult != null &&
                                                           focusedPlayPaneResult.WasTargeted &&
                                                           focusedPlayPaneResult.WasAttempted &&
                                                           focusedPlayPaneResult.OutcomeStatus == ReviewWorkspacePaneOperationOutcome.Succeeded,
                        FocusedPlayOutcomeStatus = focusedPlayPaneResult != null
                            ? focusedPlayPaneResult.OutcomeStatus.ToString()
                            : string.Empty,
                        FocusedPlayHadStepPayload = focusedPlayPaneResult != null &&
                                                    focusedPlayPaneResult.FrameStepResult != null,
                        ActivePaneIdWhileSecondaryFocused = workspaceWhileSecondaryFocused.ActivePaneId,
                        FocusedPaneIdWhileSecondaryFocused = workspaceWhileSecondaryFocused.FocusedPaneId,
                        SnapshotActivePaneIdWhileSecondaryFocused = workspaceSnapshotWhileSecondaryFocused != null ? workspaceSnapshotWhileSecondaryFocused.ActivePaneId : string.Empty,
                        SnapshotFocusedPaneIdWhileSecondaryFocused = workspaceSnapshotWhileSecondaryFocused != null ? workspaceSnapshotWhileSecondaryFocused.FocusedPaneId : string.Empty,
                        SnapshotSecondaryLookupWhileSecondaryFocused = snapshotSecondaryLookupWhileSecondaryFocused,
                        SnapshotSecondaryIsFocusedWhileSecondaryFocused = secondarySnapshotWhileSecondaryFocused != null && secondarySnapshotWhileSecondaryFocused.IsFocused,
                        SnapshotSecondaryFrameIndexWhileSecondaryFocused = secondarySnapshotWhileSecondaryFocused != null
                            ? secondarySnapshotWhileSecondaryFocused.FrameIndex
                            : null,
                        SnapshotSecondaryPresentationTimeTicksWhileSecondaryFocused = secondarySnapshotWhileSecondaryFocused != null
                            ? secondarySnapshotWhileSecondaryFocused.PresentationTime.Ticks
                            : 0L,
                        SnapshotSecondaryPlaybackStateWhileSecondaryFocused = secondarySnapshotWhileSecondaryFocused != null
                            ? secondarySnapshotWhileSecondaryFocused.PlaybackState.ToString()
                            : string.Empty,
                        PrimarySessionId = workspace.PrimaryPane != null ? workspace.PrimaryPane.SessionId : string.Empty,
                        ActiveSessionId = workspace.ActivePane != null ? workspace.ActivePane.SessionId : string.Empty,
                        FocusedSessionId = workspace.FocusedPane != null ? workspace.FocusedPane.SessionId : string.Empty,
                        SecondPaneSessionId = comparePane != null ? comparePane.SessionId : string.Empty,
                        PrimaryFilePath = workspace.PrimaryPane != null ? workspace.PrimaryPane.Session.CurrentFilePath : string.Empty,
                        SecondPaneFilePath = comparePane != null ? comparePane.Session.CurrentFilePath : string.Empty,
                        PrimaryPlayCalls = primaryEngine.PlayCallCount,
                        PrimaryPauseCalls = primaryEngine.PauseCallCount,
                        PrimarySeekToTimeCalls = primaryEngine.SeekToTimeCallCount,
                        PrimaryStepBackwardCalls = primaryEngine.StepBackwardCallCount,
                        PrimaryStepForwardCalls = primaryEngine.StepForwardCallCount,
                        SecondaryPlayCalls = compareEngine.PlayCallCount,
                        SecondaryPauseCalls = compareEngine.PauseCallCount,
                        SecondarySeekToTimeCalls = compareEngine.SeekToTimeCallCount,
                        SecondaryStepBackwardCalls = compareEngine.StepBackwardCallCount,
                        SecondaryStepForwardCalls = compareEngine.StepForwardCallCount,
                        FocusedCommandsRoutedToSecondary = primaryPlayCallsAfterFocusedSecondary == 0 &&
                                                           primaryPauseCallsAfterFocusedSecondary == 0 &&
                                                           primarySeekToTimeCallsAfterFocusedSecondary == 0 &&
                                                           primaryStepBackwardCallsAfterFocusedSecondary == 0 &&
                                                           primaryStepForwardCallsAfterFocusedSecondary == 0 &&
                                                           secondaryPlayCallsAfterFocusedSecondary == 1 &&
                                                           secondaryPauseCallsAfterFocusedSecondary == 1 &&
                                                           secondarySeekToTimeCallsAfterFocusedSecondary == 1 &&
                                                           secondaryStepBackwardCallsAfterFocusedSecondary == 1 &&
                                                           secondaryStepForwardCallsAfterFocusedSecondary == 1,
                        FocusedRoutingIgnoredPrimaryActivePane = string.Equals(
                                                                    workspaceWhileSecondaryFocused.ActivePaneId,
                                                                    PrimaryPaneId,
                                                                    StringComparison.Ordinal) &&
                                                                string.Equals(
                                                                    workspaceWhileSecondaryFocused.FocusedPaneId,
                                                                    ComparePaneId,
                                                                    StringComparison.Ordinal) &&
                                                                secondaryPlayCallsAfterFocusedSecondary == 1 &&
                                                                primaryPlayCallsAfterFocusedSecondary == 0,
                        AllPanePlayReachedBoth = primaryPlayCallsAfterAllPanes == 1 &&
                                                 secondaryPlayCallsAfterAllPanes == 2,
                        AllPanePauseReachedBoth = primaryPauseCallsAfterAllPanes == 1 &&
                                                  secondaryPauseCallsAfterAllPanes == 2,
                        AllPaneSeekReachedBoth = primarySeekToTimeCallsAfterAllPanes == 1 &&
                                                 secondarySeekToTimeCallsAfterAllPanes == 2,
                        AllPaneStepBackwardReachedBoth = primaryStepBackwardCallsAfterAllPanes == 1 &&
                                                         secondaryStepBackwardCallsAfterAllPanes == 2,
                        AllPaneStepForwardReachedBoth = primaryStepForwardCallsAfterAllPanes == 1 &&
                                                        secondaryStepForwardCallsAfterAllPanes == 2,
                        AllPaneOrderingFocusedFirst = string.Equals(GetPaneIdAt(allPanePlayResult, 0), ComparePaneId, StringComparison.Ordinal) &&
                                                     string.Equals(GetPaneIdAt(allPanePlayResult, 1), PrimaryPaneId, StringComparison.Ordinal),
                        AllPanePlayResultPaneCount = allPanePlayResult != null ? allPanePlayResult.PaneCount : 0,
                        AllPanePauseResultPaneCount = allPanePauseResult != null ? allPanePauseResult.PaneCount : 0,
                        AllPaneSeekResultPaneCount = allPaneSeekResult != null ? allPaneSeekResult.PaneCount : 0,
                        AllPaneStepBackwardResultPaneCount = allPaneStepBackwardResult != null ? allPaneStepBackwardResult.PaneCount : 0,
                        AllPaneStepForwardResultPaneCount = allPaneStepForwardResult != null ? allPaneStepForwardResult.PaneCount : 0,
                        AllPaneResultSurfaceSucceeded = allPanePlayResult != null &&
                                                       allPanePauseResult != null &&
                                                       allPaneSeekResult != null &&
                                                       allPaneStepBackwardResult != null &&
                                                       allPaneStepForwardResult != null &&
                                                       allPanePlayResult.Succeeded &&
                                                       allPanePauseResult.Succeeded &&
                                                       allPaneSeekResult.Succeeded &&
                                                       allPaneStepBackwardResult.Succeeded &&
                                                       allPaneStepForwardResult.Succeeded,
                        AllPaneResultsMarkedAttemptedAndSucceeded = AllAttemptedWithOutcome(allPanePlayResult, ReviewWorkspacePaneOperationOutcome.Succeeded) &&
                                                                    AllAttemptedWithOutcome(allPanePauseResult, ReviewWorkspacePaneOperationOutcome.Succeeded) &&
                                                                    AllAttemptedWithOutcome(allPaneSeekResult, ReviewWorkspacePaneOperationOutcome.Succeeded) &&
                                                                    AllAttemptedWithOutcome(allPaneStepBackwardResult, ReviewWorkspacePaneOperationOutcome.Succeeded) &&
                                                                    AllAttemptedWithOutcome(allPaneStepForwardResult, ReviewWorkspacePaneOperationOutcome.Succeeded),
                        NonStepResultsHaveNoStepPayload = HasNoStepPayloads(focusedPlayResult) &&
                                                          HasNoStepPayloads(focusedPauseResult) &&
                                                          HasNoStepPayloads(focusedSeekResult) &&
                                                          HasNoStepPayloads(allPanePlayResult) &&
                                                          HasNoStepPayloads(allPanePauseResult) &&
                                                          HasNoStepPayloads(allPaneSeekResult),
                        StepResultsCarryStepPayload = HasStepPayloads(focusedStepBackwardResult) &&
                                                      HasStepPayloads(focusedStepForwardResult) &&
                                                      HasStepPayloads(allPaneStepBackwardResult) &&
                                                      HasStepPayloads(allPaneStepForwardResult),
                        AllOperationsUsedNormalizedResults = focusedPlayResult != null &&
                                                             focusedPauseResult != null &&
                                                             focusedSeekResult != null &&
                                                             focusedStepBackwardResult != null &&
                                                             focusedStepForwardResult != null &&
                                                             focusedPlayResult.AttemptedPaneCount == 1 &&
                                                             focusedPauseResult.AttemptedPaneCount == 1 &&
                                                             focusedSeekResult.AttemptedPaneCount == 1 &&
                                                             focusedStepBackwardResult.AttemptedPaneCount == 1 &&
                                                             focusedStepForwardResult.AttemptedPaneCount == 1 &&
                                                             allPanePlayResult != null &&
                                                             allPanePauseResult != null &&
                                                             allPaneSeekResult != null &&
                                                             allPaneStepBackwardResult != null &&
                                                             allPaneStepForwardResult != null &&
                                                             allPanePlayResult.AttemptedPaneCount == 2 &&
                                                             allPanePauseResult.AttemptedPaneCount == 2 &&
                                                             allPaneSeekResult.AttemptedPaneCount == 2 &&
                                                             allPaneStepBackwardResult.AttemptedPaneCount == 2 &&
                                                             allPaneStepForwardResult.AttemptedPaneCount == 2,
                        AllPanePlayResultIncludedPrimary = allPanePlayPrimaryResult != null,
                        AllPanePlayResultIncludedSecondary = allPanePlaySecondaryResult != null,
                        SnapshotPrimaryLookupAfterAllPaneOperations = snapshotPrimaryLookupAfterAllPaneOperations,
                        SnapshotSecondaryLookupAfterAllPaneOperations = snapshotSecondaryLookupAfterAllPaneOperations,
                        SnapshotPrimaryFrameIndexAfterAllPaneOperations = primarySnapshotAfterAllPaneOperations != null
                            ? primarySnapshotAfterAllPaneOperations.FrameIndex
                            : null,
                        SnapshotSecondaryFrameIndexAfterAllPaneOperations = secondarySnapshotAfterAllPaneOperations != null
                            ? secondarySnapshotAfterAllPaneOperations.FrameIndex
                            : null,
                        SnapshotPrimaryPresentationTimeTicksAfterAllPaneOperations = primarySnapshotAfterAllPaneOperations != null
                            ? primarySnapshotAfterAllPaneOperations.PresentationTime.Ticks
                            : 0L,
                        SnapshotSecondaryPresentationTimeTicksAfterAllPaneOperations = secondarySnapshotAfterAllPaneOperations != null
                            ? secondarySnapshotAfterAllPaneOperations.PresentationTime.Ticks
                            : 0L,
                        AllPaneSeekPrimaryFrameIndex = allPaneSeekPrimaryResult != null
                            ? allPaneSeekPrimaryResult.Position.FrameIndex
                            : null,
                        AllPaneSeekSecondaryFrameIndex = allPaneSeekSecondaryResult != null
                            ? allPaneSeekSecondaryResult.Position.FrameIndex
                            : null,
                        AllPaneStepBackwardPrimaryFrameIndex = allPaneStepBackwardPrimaryResult != null
                            ? allPaneStepBackwardPrimaryResult.Position.FrameIndex
                            : null,
                        AllPaneStepBackwardSecondaryFrameIndex = allPaneStepBackwardSecondaryResult != null
                            ? allPaneStepBackwardSecondaryResult.Position.FrameIndex
                            : null,
                        AllPaneStepForwardPrimaryFrameIndex = allPaneStepForwardPrimaryResult != null
                            ? allPaneStepForwardPrimaryResult.Position.FrameIndex
                            : null,
                        AllPaneStepForwardSecondaryFrameIndex = allPaneStepForwardSecondaryResult != null
                            ? allPaneStepForwardSecondaryResult.Position.FrameIndex
                            : null,
                        FailureSecondPaneBound = failureSecondPaneBound,
                        FailureAllPaneCollectedBothResults = failureAllPanePlayResult != null &&
                                                             failureAllPanePlayResult.PaneCount == 2,
                        FailureAllPanePreservedOrder = string.Equals(GetPaneIdAt(failureAllPanePlayResult, 0), "pane-failure-compare-a", StringComparison.Ordinal) &&
                                                       string.Equals(GetPaneIdAt(failureAllPanePlayResult, 1), PrimaryPaneId, StringComparison.Ordinal),
                        FailureAggregateInspectable = failureAllPanePlayResult != null &&
                                                     failureAllPanePlayResult.HasFailures &&
                                                     failureAllPanePlayResult.FailedPaneCount == 1 &&
                                                     failureAllPanePlayResult.SucceededPaneCount == 1,
                        FailureSecondaryMarkedFailed = failureFirstPaneResult != null &&
                                                       failureFirstPaneResult.WasAttempted &&
                                                       failureFirstPaneResult.OutcomeStatus == ReviewWorkspacePaneOperationOutcome.Failed,
                        FailureSecondaryExceptionType = failureFirstPaneResult != null
                            ? failureFirstPaneResult.FailureExceptionType
                            : string.Empty,
                        FailurePrimaryStillSucceeded = failureSecondPaneResult != null &&
                                                       failureSecondPaneResult.WasAttempted &&
                                                       failureSecondPaneResult.OutcomeStatus == ReviewWorkspacePaneOperationOutcome.Succeeded,
                        FailureLaterPaneStillAttempted = failurePrimaryPlayCalls == 1 &&
                                                         failureSecondaryPlayCalls == 1,
                        CommandsReturnedToPrimary = primaryEngine.PlayCallCount == 2 &&
                                                    primaryEngine.PauseCallCount == 2 &&
                                                    primaryEngine.SeekToTimeCallCount == 2 &&
                                                    primaryEngine.StepBackwardCallCount == 2 &&
                                                    primaryEngine.StepForwardCallCount == 2 &&
                                                    compareEngine.PlayCallCount == 2 &&
                                                    compareEngine.PauseCallCount == 2 &&
                                                    compareEngine.SeekToTimeCallCount == 2 &&
                                                    compareEngine.StepBackwardCallCount == 2 &&
                                                    compareEngine.StepForwardCallCount == 2
                    };
                }
                finally
                {
                    workspaceCoordinator.Dispose();
                }
            }
        }

        public sealed class WorkspaceCoordinatorProbeReport
        {
            public int PaneCount { get; set; }

            public string WorkspacePrimaryPaneId { get; set; }

            public string ActivePaneId { get; set; }

            public string FocusedPaneId { get; set; }

            public string[] PaneIds { get; set; }

            public bool AddedSecondPane { get; set; }

            public int BoundPaneCountBeforeSelection { get; set; }

            public int WorkspaceSnapshotPaneCount { get; set; }

            public bool SecondPaneLookupBeforeSelection { get; set; }

            public string SecondPaneDisplayLabelBeforeSelection { get; set; }

            public bool SecondPanePresent { get; set; }

            public bool SnapshotPrimaryLookupBeforeSelection { get; set; }

            public bool SnapshotSecondaryLookupBeforeSelection { get; set; }

            public bool SnapshotMissingPaneLookupFails { get; set; }

            public string SnapshotPrimarySessionIdBeforeSelection { get; set; }

            public string SnapshotSecondarySessionIdBeforeSelection { get; set; }

            public bool SnapshotPrimaryIsBoundBeforeSelection { get; set; }

            public bool SnapshotSecondaryIsBoundBeforeSelection { get; set; }

            public bool SnapshotPrimaryIsPrimaryBeforeSelection { get; set; }

            public bool SnapshotSecondaryIsPrimaryBeforeSelection { get; set; }

            public bool FocusedSecondary { get; set; }

            public bool FocusedPrimaryAgain { get; set; }

            public int FocusedPlayResultPaneCount { get; set; }

            public string FocusedPlayResultTargetPaneId { get; set; }

            public bool FocusedPlayAttemptedAndSucceeded { get; set; }

            public string FocusedPlayOutcomeStatus { get; set; }

            public bool FocusedPlayHadStepPayload { get; set; }

            public string ActivePaneIdWhileSecondaryFocused { get; set; }

            public string FocusedPaneIdWhileSecondaryFocused { get; set; }

            public string SnapshotActivePaneIdWhileSecondaryFocused { get; set; }

            public string SnapshotFocusedPaneIdWhileSecondaryFocused { get; set; }

            public bool SnapshotSecondaryLookupWhileSecondaryFocused { get; set; }

            public bool SnapshotSecondaryIsFocusedWhileSecondaryFocused { get; set; }

            public long? SnapshotSecondaryFrameIndexWhileSecondaryFocused { get; set; }

            public long SnapshotSecondaryPresentationTimeTicksWhileSecondaryFocused { get; set; }

            public string SnapshotSecondaryPlaybackStateWhileSecondaryFocused { get; set; }

            public string PrimarySessionId { get; set; }

            public string ActiveSessionId { get; set; }

            public string FocusedSessionId { get; set; }

            public string SecondPaneSessionId { get; set; }

            public string PrimaryFilePath { get; set; }

            public string SecondPaneFilePath { get; set; }

            public int PrimaryPlayCalls { get; set; }

            public int PrimaryPauseCalls { get; set; }

            public int PrimarySeekToTimeCalls { get; set; }

            public int PrimaryStepBackwardCalls { get; set; }

            public int PrimaryStepForwardCalls { get; set; }

            public int SecondaryPlayCalls { get; set; }

            public int SecondaryPauseCalls { get; set; }

            public int SecondarySeekToTimeCalls { get; set; }

            public int SecondaryStepBackwardCalls { get; set; }

            public int SecondaryStepForwardCalls { get; set; }

            public bool FocusedCommandsRoutedToSecondary { get; set; }

            public bool FocusedRoutingIgnoredPrimaryActivePane { get; set; }

            public bool AllPanePlayReachedBoth { get; set; }

            public bool AllPanePauseReachedBoth { get; set; }

            public bool AllPaneSeekReachedBoth { get; set; }

            public bool AllPaneStepBackwardReachedBoth { get; set; }

            public bool AllPaneStepForwardReachedBoth { get; set; }

            public bool AllPaneOrderingFocusedFirst { get; set; }

            public int AllPanePlayResultPaneCount { get; set; }

            public int AllPanePauseResultPaneCount { get; set; }

            public int AllPaneSeekResultPaneCount { get; set; }

            public int AllPaneStepBackwardResultPaneCount { get; set; }

            public int AllPaneStepForwardResultPaneCount { get; set; }

            public bool AllPaneResultSurfaceSucceeded { get; set; }

            public bool AllPaneResultsMarkedAttemptedAndSucceeded { get; set; }

            public bool NonStepResultsHaveNoStepPayload { get; set; }

            public bool StepResultsCarryStepPayload { get; set; }

            public bool AllOperationsUsedNormalizedResults { get; set; }

            public bool AllPanePlayResultIncludedPrimary { get; set; }

            public bool AllPanePlayResultIncludedSecondary { get; set; }

            public bool SnapshotPrimaryLookupAfterAllPaneOperations { get; set; }

            public bool SnapshotSecondaryLookupAfterAllPaneOperations { get; set; }

            public long? SnapshotPrimaryFrameIndexAfterAllPaneOperations { get; set; }

            public long? SnapshotSecondaryFrameIndexAfterAllPaneOperations { get; set; }

            public long SnapshotPrimaryPresentationTimeTicksAfterAllPaneOperations { get; set; }

            public long SnapshotSecondaryPresentationTimeTicksAfterAllPaneOperations { get; set; }

            public long? AllPaneSeekPrimaryFrameIndex { get; set; }

            public long? AllPaneSeekSecondaryFrameIndex { get; set; }

            public long? AllPaneStepBackwardPrimaryFrameIndex { get; set; }

            public long? AllPaneStepBackwardSecondaryFrameIndex { get; set; }

            public long? AllPaneStepForwardPrimaryFrameIndex { get; set; }

            public long? AllPaneStepForwardSecondaryFrameIndex { get; set; }

            public bool FailureSecondPaneBound { get; set; }

            public bool FailureAllPaneCollectedBothResults { get; set; }

            public bool FailureAllPanePreservedOrder { get; set; }

            public bool FailureAggregateInspectable { get; set; }

            public bool FailureSecondaryMarkedFailed { get; set; }

            public string FailureSecondaryExceptionType { get; set; }

            public bool FailurePrimaryStillSucceeded { get; set; }

            public bool FailureLaterPaneStillAttempted { get; set; }

            public bool CommandsReturnedToPrimary { get; set; }
        }

        private sealed class ProbeVideoReviewEngine : IVideoReviewEngine
        {
            private readonly TimeSpan _positionStep = TimeSpan.FromMilliseconds(1000d / 30d);
            private readonly VideoMediaInfo _mediaInfo;

            private readonly bool _throwOnPlay;
            private readonly bool _throwOnPause;
            private readonly bool _throwOnSeekToTime;
            private readonly bool _throwOnStepBackward;
            private readonly bool _throwOnStepForward;

            public ProbeVideoReviewEngine(
                string currentFilePath,
                ReviewPosition initialPosition,
                bool throwOnPlay = false,
                bool throwOnPause = false,
                bool throwOnSeekToTime = false,
                bool throwOnStepBackward = false,
                bool throwOnStepForward = false)
            {
                CurrentFilePath = currentFilePath ?? string.Empty;
                Position = initialPosition ?? ReviewPosition.Empty;
                IsMediaOpen = !string.IsNullOrWhiteSpace(CurrentFilePath);
                _throwOnPlay = throwOnPlay;
                _throwOnPause = throwOnPause;
                _throwOnSeekToTime = throwOnSeekToTime;
                _throwOnStepBackward = throwOnStepBackward;
                _throwOnStepForward = throwOnStepForward;
                _mediaInfo = new VideoMediaInfo(
                    "probe-video",
                    TimeSpan.FromSeconds(10d),
                    _positionStep,
                    30d,
                    1920,
                    1080,
                    "probe-video",
                    0,
                    30,
                    1,
                    1,
                    1000);
            }

            public bool IsMediaOpen { get; private set; }

            public bool IsPlaying { get; private set; }

            public string CurrentFilePath { get; private set; }

            public string LastErrorMessage { get; } = string.Empty;

            public VideoMediaInfo MediaInfo
            {
                get { return _mediaInfo; }
            }

            public ReviewPosition Position { get; private set; }

            public int PlayCallCount { get; private set; }

            public int PauseCallCount { get; private set; }

            public int SeekToTimeCallCount { get; private set; }

            public int SeekToFrameCallCount { get; private set; }

            public int StepBackwardCallCount { get; private set; }

            public int StepForwardCallCount { get; private set; }

            public event EventHandler<VideoReviewEngineStateChangedEventArgs> StateChanged;

#pragma warning disable 0067
            public event EventHandler<FramePresentedEventArgs> FramePresented;
#pragma warning restore 0067

            public Task OpenAsync(string filePath, CancellationToken cancellationToken = default(CancellationToken))
            {
                CurrentFilePath = filePath ?? string.Empty;
                IsMediaOpen = !string.IsNullOrWhiteSpace(CurrentFilePath);
                RaiseStateChanged();
                return Task.CompletedTask;
            }

            public Task CloseAsync()
            {
                IsMediaOpen = false;
                IsPlaying = false;
                CurrentFilePath = string.Empty;
                Position = ReviewPosition.Empty;
                RaiseStateChanged();
                return Task.CompletedTask;
            }

            public Task PlayAsync()
            {
                PlayCallCount++;
                if (_throwOnPlay)
                {
                    throw new InvalidOperationException("Probe play failure.");
                }

                IsPlaying = true;
                RaiseStateChanged();
                return Task.CompletedTask;
            }

            public Task PauseAsync()
            {
                PauseCallCount++;
                if (_throwOnPause)
                {
                    throw new InvalidOperationException("Probe pause failure.");
                }

                IsPlaying = false;
                RaiseStateChanged();
                return Task.CompletedTask;
            }

            public Task<FrameStepResult> StepForwardAsync(CancellationToken cancellationToken = default(CancellationToken))
            {
                StepForwardCallCount++;
                if (_throwOnStepForward)
                {
                    throw new InvalidOperationException("Probe step-forward failure.");
                }

                var nextFrameIndex = (Position != null && Position.FrameIndex.HasValue ? Position.FrameIndex.Value : 0L) + 1L;
                Position = new ReviewPosition(
                    TimeSpan.FromTicks(_positionStep.Ticks * nextFrameIndex),
                    nextFrameIndex,
                    true,
                    true,
                    null,
                    null);
                RaiseStateChanged();
                return Task.FromResult(FrameStepResult.Succeeded(1, Position));
            }

            public Task<FrameStepResult> StepBackwardAsync(CancellationToken cancellationToken = default(CancellationToken))
            {
                StepBackwardCallCount++;
                if (_throwOnStepBackward)
                {
                    throw new InvalidOperationException("Probe step-backward failure.");
                }

                var nextFrameIndex = Math.Max(0L, (Position != null && Position.FrameIndex.HasValue ? Position.FrameIndex.Value : 0L) - 1L);
                Position = new ReviewPosition(
                    TimeSpan.FromTicks(_positionStep.Ticks * nextFrameIndex),
                    nextFrameIndex,
                    true,
                    true,
                    null,
                    null);
                RaiseStateChanged();
                return Task.FromResult(FrameStepResult.Succeeded(-1, Position));
            }

            public Task SeekToTimeAsync(TimeSpan position, CancellationToken cancellationToken = default(CancellationToken))
            {
                SeekToTimeCallCount++;
                if (_throwOnSeekToTime)
                {
                    throw new InvalidOperationException("Probe seek-to-time failure.");
                }

                var frameIndex = position <= TimeSpan.Zero
                    ? 0L
                    : (long)Math.Round(position.Ticks / (double)_positionStep.Ticks, MidpointRounding.AwayFromZero);
                Position = new ReviewPosition(position, frameIndex, true, true, null, null);
                RaiseStateChanged();
                return Task.CompletedTask;
            }

            public Task SeekToFrameAsync(long frameIndex, CancellationToken cancellationToken = default(CancellationToken))
            {
                SeekToFrameCallCount++;
                var normalizedFrameIndex = Math.Max(0L, frameIndex);
                Position = new ReviewPosition(
                    TimeSpan.FromTicks(_positionStep.Ticks * normalizedFrameIndex),
                    normalizedFrameIndex,
                    true,
                    true,
                    null,
                    null);
                RaiseStateChanged();
                return Task.CompletedTask;
            }

            public void Dispose()
            {
            }

            private void RaiseStateChanged()
            {
                StateChanged?.Invoke(
                    this,
                    new VideoReviewEngineStateChangedEventArgs(
                        IsMediaOpen,
                        IsPlaying,
                        CurrentFilePath,
                        LastErrorMessage,
                        _mediaInfo,
                        Position));
            }
        }
    }
}
