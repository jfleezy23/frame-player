using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FramePlayer.Core.Coordination;
using FramePlayer.Core.Models;
using FramePlayer.Engines.FFmpeg;
using FramePlayer.Services;

namespace FramePlayer.Diagnostics
{
    internal static class DualPaneBudgetHarnessRunner
    {
        private static readonly TimeSpan IndexReadyTimeout = TimeSpan.FromSeconds(30d);
        private static readonly TimeSpan StepPause = TimeSpan.FromMilliseconds(10d);

        public static async Task<DualPaneBudgetHarnessReport> RunAsync(
            IEnumerable<DualPaneBudgetHarnessPairRequest> pairRequests,
            IEnumerable<DualPaneBudgetHarnessHostScenarioRequest> hostScenarios,
            CancellationToken cancellationToken)
        {
            if (pairRequests == null)
            {
                throw new ArgumentNullException(nameof(pairRequests));
            }

            if (hostScenarios == null)
            {
                throw new ArgumentNullException(nameof(hostScenarios));
            }

            var normalizedPairs = pairRequests
                .Where(pair => pair != null)
                .Select(NormalizePairRequest)
                .ToArray();
            if (normalizedPairs.Length == 0)
            {
                throw new ArgumentException("At least one dual-pane compare pair is required.", nameof(pairRequests));
            }

            var normalizedHostScenarios = hostScenarios
                .Where(scenario => scenario != null)
                .Select(NormalizeHostScenario)
                .ToArray();
            if (normalizedHostScenarios.Length == 0)
            {
                throw new ArgumentException("At least one dual-pane host scenario is required.", nameof(hostScenarios));
            }

            var hostReports = new List<DualPaneBudgetHarnessHostScenarioReport>(normalizedHostScenarios.Length);
            foreach (var hostScenario in normalizedHostScenarios)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var pairReports = new List<DualPaneBudgetHarnessPairReport>(normalizedPairs.Length);
                foreach (var pair in normalizedPairs)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    pairReports.Add(await RunPairAsync(pair, hostScenario, cancellationToken).ConfigureAwait(false));
                }

                hostReports.Add(new DualPaneBudgetHarnessHostScenarioReport(
                    hostScenario.Name,
                    hostScenario.TotalPhysicalMemoryBytes,
                    hostScenario.AvailablePhysicalMemoryBytes,
                    pairReports.ToArray(),
                    BuildHostSummary(pairReports)));
            }

            return new DualPaneBudgetHarnessReport(
                DateTimeOffset.UtcNow.ToString("o", CultureInfo.InvariantCulture),
                hostReports.ToArray(),
                BuildOverallSummary(hostReports));
        }

        private static async Task<DualPaneBudgetHarnessPairReport> RunPairAsync(
            DualPaneBudgetHarnessPairRequest pair,
            DualPaneBudgetHarnessHostScenarioRequest hostScenario,
            CancellationToken cancellationToken)
        {
            var checks = new List<DualPaneBudgetHarnessCheck>();
            var primaryStepMetrics = new DualPaneBudgetHarnessStepMetrics();
            var compareStepMetrics = new DualPaneBudgetHarnessStepMetrics();
            var primaryOpenElapsed = 0d;
            var compareOpenElapsed = 0d;
            var primaryIndexReadyElapsed = 0d;
            var compareIndexReadyElapsed = 0d;
            var seekElapsed = 0d;
            var stepWindow = 0;
            var allPanePaneCount = 0;
            var harnessError = string.Empty;
            var optionsProvider = new FfmpegReviewEngineOptionsProvider(new AppPreferencesService());
            var coordinator = new DecodedFrameBudgetCoordinator(
                hostScenario.TotalPhysicalMemoryBytes,
                hostScenario.AvailablePhysicalMemoryBytes);

            DualPaneBudgetHarnessDecodeSnapshot primarySnapshot = DualPaneBudgetHarnessDecodeSnapshot.Empty;
            DualPaneBudgetHarnessDecodeSnapshot compareSnapshot = DualPaneBudgetHarnessDecodeSnapshot.Empty;

            using (var primaryEngine = new FfmpegReviewEngine(optionsProvider, coordinator, "pane-primary"))
            using (var compareEngine = new FfmpegReviewEngine(optionsProvider, coordinator, "pane-compare"))
            using (var primarySession = new ReviewSessionCoordinator(primaryEngine, "session-primary", "Primary"))
            using (var compareSession = new ReviewSessionCoordinator(compareEngine, "session-compare", "Compare"))
            {
                ReviewPosition primaryBaselinePosition = ReviewPosition.Empty;
                ReviewPosition compareBaselinePosition = ReviewPosition.Empty;
                try
                {
                    var primaryOpen = await MeasureAsync(() => primaryEngine.OpenAsync(pair.PrimaryPath, cancellationToken)).ConfigureAwait(false);
                    primaryOpenElapsed = primaryOpen.Elapsed.TotalMilliseconds;

                    var compareOpen = await MeasureAsync(() => compareEngine.OpenAsync(pair.ComparePath, cancellationToken)).ConfigureAwait(false);
                    compareOpenElapsed = compareOpen.Elapsed.TotalMilliseconds;

                    primarySession.RefreshFromEngine();
                    compareSession.RefreshFromEngine();

                    var primaryIndexReady = await WaitForIndexReadyAsync(primaryEngine, IndexReadyTimeout, cancellationToken).ConfigureAwait(false);
                    var compareIndexReady = await WaitForIndexReadyAsync(compareEngine, IndexReadyTimeout, cancellationToken).ConfigureAwait(false);
                    primaryIndexReadyElapsed = primaryIndexReady.ElapsedMilliseconds;
                    compareIndexReadyElapsed = compareIndexReady.ElapsedMilliseconds;

                    primarySnapshot = BuildDecodeSnapshot(primaryEngine);
                    compareSnapshot = BuildDecodeSnapshot(compareEngine);

                    checks.Add(CheckTrue("open-primary", primaryEngine.IsMediaOpen, "Primary pane opened successfully."));
                    checks.Add(CheckTrue("open-compare", compareEngine.IsMediaOpen, "Compare pane opened successfully."));
                    checks.Add(CheckTrue("index-ready-primary", primaryIndexReady.Ready, "Primary pane global index became available."));
                    checks.Add(CheckTrue("index-ready-compare", compareIndexReady.Ready, "Compare pane global index became available."));
                    checks.Add(CheckEqual("budget-band-primary", "DualPaneBackendAware", primarySnapshot.BudgetBand, "Primary pane should use the dual-pane band."));
                    checks.Add(CheckEqual("budget-band-compare", "DualPaneBackendAware", compareSnapshot.BudgetBand, "Compare pane should use the dual-pane band."));
                    checks.Add(CheckEqual("session-budget-primary", ResolveExpectedDualSessionBudgetBytes(hostScenario), primarySnapshot.SessionBudgetBytes, "Primary pane should report the expected dual-pane session budget."));
                    checks.Add(CheckEqual("session-budget-compare", ResolveExpectedDualSessionBudgetBytes(hostScenario), compareSnapshot.SessionBudgetBytes, "Compare pane should report the expected dual-pane session budget."));

                    using (var workspace = new ReviewWorkspaceCoordinator(primaryEngine, primarySession))
                    {
                        workspace.TryBindPane("pane-compare", compareSession, "Compare", TimeSpan.Zero, false, false);
                        workspace.TrySelectPane("pane-primary", WorkspacePaneSelectionMode.ActiveAndFocused);
                        checks.Add(CheckTrue("workspace-pane-count", workspace.CurrentWorkspace != null && workspace.CurrentWorkspace.PaneCount == 2, "The workspace should have both panes bound."));

                        primaryBaselinePosition = primaryEngine.Position ?? ReviewPosition.Empty;
                        compareBaselinePosition = compareEngine.Position ?? ReviewPosition.Empty;

                        stepWindow = DetermineStepWindow(primaryEngine.IndexedFrameCount, compareEngine.IndexedFrameCount);
                        checks.Add(CheckTrue("step-window", stepWindow > 0, "The pair should have enough indexed frames for repeated stepping."));

                        if (stepWindow > 0)
                        {
                            await RunStepWindowAsync(
                                    workspace,
                                    pair.RequirePaneAlignment,
                                    stepWindow,
                                    primaryStepMetrics,
                                    compareStepMetrics,
                                    checks,
                                    cancellationToken)
                                .ConfigureAwait(false);

                            checks.Add(CheckEquivalentPosition("return-to-baseline-primary", primaryBaselinePosition, primaryEngine.Position, "Primary pane should return to its baseline frame after synchronized stepping."));
                            checks.Add(CheckEquivalentPosition("return-to-baseline-compare", compareBaselinePosition, compareEngine.Position, "Compare pane should return to its baseline frame after synchronized stepping."));
                        }

                        var seekTarget = SelectSeekTarget(
                            primaryEngine.MediaInfo.Duration,
                            compareEngine.MediaInfo.Duration,
                            primaryEngine.MediaInfo.PositionStep,
                            compareEngine.MediaInfo.PositionStep);
                        if (seekTarget > TimeSpan.Zero)
                        {
                            var seekResult = await MeasureAsync(
                                    () => workspace.SeekToTimeWithPaneResultsAsync(seekTarget, SynchronizedOperationScope.AllPanes, cancellationToken))
                                .ConfigureAwait(false);
                            seekElapsed = seekResult.Elapsed.TotalMilliseconds;
                            checks.Add(CheckPaneOperation("seek-to-time", seekResult.Result, pair.RequirePaneAlignment));

                            var primarySeekPosition = GetPanePosition(seekResult.Result, "pane-primary");
                            var compareSeekPosition = GetPanePosition(seekResult.Result, "pane-compare");
                            checks.Add(CheckTrue("seek-to-time-primary-absolute", primarySeekPosition != null && primarySeekPosition.IsFrameIndexAbsolute, "Primary pane seek-to-time should be absolute once indexed."));
                            checks.Add(CheckTrue("seek-to-time-compare-absolute", compareSeekPosition != null && compareSeekPosition.IsFrameIndexAbsolute, "Compare pane seek-to-time should be absolute once indexed."));
                            if (pair.RequirePaneAlignment)
                            {
                                checks.Add(CheckEquivalentPosition("seek-to-time-aligned", primarySeekPosition, compareSeekPosition, "Equivalent compare panes should land on the same frame after a synchronized seek."));
                            }
                        }

                        allPanePaneCount = 2;
                        primarySnapshot = BuildDecodeSnapshot(primaryEngine);
                        compareSnapshot = BuildDecodeSnapshot(compareEngine);
                    }
                }
                catch (Exception ex)
                {
                    harnessError = ex.ToString();
                    checks.Add(CheckTrue("harness-exception", false, "Dual-pane runtime harness threw an exception: " + ex.Message));
                    primarySnapshot = BuildDecodeSnapshot(primaryEngine);
                    compareSnapshot = BuildDecodeSnapshot(compareEngine);
                }
            }

            var summary = BuildPairSummary(checks);
            return new DualPaneBudgetHarnessPairReport(
                pair.Label,
                pair.PrimaryPath,
                pair.ComparePath,
                pair.RequirePaneAlignment,
                hostScenario.Name,
                hostScenario.TotalPhysicalMemoryBytes,
                hostScenario.AvailablePhysicalMemoryBytes,
                primaryOpenElapsed,
                compareOpenElapsed,
                primaryIndexReadyElapsed,
                compareIndexReadyElapsed,
                seekElapsed,
                stepWindow,
                allPanePaneCount,
                primaryStepMetrics,
                compareStepMetrics,
                primarySnapshot,
                compareSnapshot,
                checks.ToArray(),
                summary,
                harnessError);
        }

        private static async Task RunStepWindowAsync(
            ReviewWorkspaceCoordinator workspace,
            bool requirePaneAlignment,
            int stepWindow,
            DualPaneBudgetHarnessStepMetrics primaryStepMetrics,
            DualPaneBudgetHarnessStepMetrics compareStepMetrics,
            ICollection<DualPaneBudgetHarnessCheck> checks,
            CancellationToken cancellationToken)
        {
            for (var index = 1; index <= stepWindow; index++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var stepForward = await workspace.StepForwardWithPaneResultsAsync(SynchronizedOperationScope.AllPanes, cancellationToken).ConfigureAwait(false);
                ObserveStepMetrics(stepForward, "pane-primary", primaryStepMetrics);
                ObserveStepMetrics(stepForward, "pane-compare", compareStepMetrics);
                checks.Add(CheckPaneOperation("step-forward-" + index.ToString(CultureInfo.InvariantCulture), stepForward, requirePaneAlignment));
                if (requirePaneAlignment)
                {
                    checks.Add(CheckEquivalentPosition(
                        "aligned-step-forward-" + index.ToString(CultureInfo.InvariantCulture),
                        GetPanePosition(stepForward, "pane-primary"),
                        GetPanePosition(stepForward, "pane-compare"),
                        "Equivalent compare panes should stay frame-identical while stepping forward together."));
                }

                await Task.Delay(StepPause, cancellationToken).ConfigureAwait(false);
            }

            for (var index = 1; index <= stepWindow; index++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var stepBackward = await workspace.StepBackwardWithPaneResultsAsync(SynchronizedOperationScope.AllPanes, cancellationToken).ConfigureAwait(false);
                ObserveStepMetrics(stepBackward, "pane-primary", primaryStepMetrics);
                ObserveStepMetrics(stepBackward, "pane-compare", compareStepMetrics);
                checks.Add(CheckPaneOperation("step-backward-" + index.ToString(CultureInfo.InvariantCulture), stepBackward, requirePaneAlignment));
                if (requirePaneAlignment)
                {
                    checks.Add(CheckEquivalentPosition(
                        "aligned-step-backward-" + index.ToString(CultureInfo.InvariantCulture),
                        GetPanePosition(stepBackward, "pane-primary"),
                        GetPanePosition(stepBackward, "pane-compare"),
                        "Equivalent compare panes should stay frame-identical while stepping backward together."));
                }

                await Task.Delay(StepPause, cancellationToken).ConfigureAwait(false);
            }
        }

        private static DualPaneBudgetHarnessPairRequest NormalizePairRequest(DualPaneBudgetHarnessPairRequest pair)
        {
            if (pair == null)
            {
                throw new ArgumentNullException(nameof(pair));
            }

            if (string.IsNullOrWhiteSpace(pair.PrimaryPath) || string.IsNullOrWhiteSpace(pair.ComparePath))
            {
                throw new ArgumentException("Both primary and compare media paths are required.");
            }

            var primaryPath = Path.GetFullPath(pair.PrimaryPath);
            var comparePath = Path.GetFullPath(pair.ComparePath);
            if (!File.Exists(primaryPath))
            {
                throw new FileNotFoundException("The requested primary compare media file was not found.", primaryPath);
            }

            if (!File.Exists(comparePath))
            {
                throw new FileNotFoundException("The requested compare media file was not found.", comparePath);
            }

            return new DualPaneBudgetHarnessPairRequest(
                string.IsNullOrWhiteSpace(pair.Label)
                    ? Path.GetFileName(primaryPath) + " vs " + Path.GetFileName(comparePath)
                    : pair.Label,
                primaryPath,
                comparePath,
                pair.RequirePaneAlignment);
        }

        private static DualPaneBudgetHarnessHostScenarioRequest NormalizeHostScenario(DualPaneBudgetHarnessHostScenarioRequest hostScenario)
        {
            if (hostScenario == null)
            {
                throw new ArgumentNullException(nameof(hostScenario));
            }

            if (string.IsNullOrWhiteSpace(hostScenario.Name))
            {
                throw new ArgumentException("Each host scenario must have a name.");
            }

            if (hostScenario.TotalPhysicalMemoryBytes <= 0L)
            {
                throw new ArgumentException("Each host scenario must have a positive total physical memory value.");
            }

            var available = hostScenario.AvailablePhysicalMemoryBytes > 0L
                ? hostScenario.AvailablePhysicalMemoryBytes
                : hostScenario.TotalPhysicalMemoryBytes;
            return new DualPaneBudgetHarnessHostScenarioRequest(hostScenario.Name, hostScenario.TotalPhysicalMemoryBytes, available);
        }

        private static async Task<MeasuredOperation> MeasureAsync(Func<Task> operation)
        {
            if (operation == null)
            {
                throw new ArgumentNullException(nameof(operation));
            }

            var stopwatch = Stopwatch.StartNew();
            await operation().ConfigureAwait(false);
            stopwatch.Stop();
            return new MeasuredOperation(stopwatch.Elapsed);
        }

        private static async Task<MeasuredOperation<T>> MeasureAsync<T>(Func<Task<T>> operation)
        {
            if (operation == null)
            {
                throw new ArgumentNullException(nameof(operation));
            }

            var stopwatch = Stopwatch.StartNew();
            var result = await operation().ConfigureAwait(false);
            stopwatch.Stop();
            return new MeasuredOperation<T>(result, stopwatch.Elapsed);
        }

        private static async Task<IndexReadyResult> WaitForIndexReadyAsync(
            FfmpegReviewEngine engine,
            TimeSpan timeout,
            CancellationToken cancellationToken)
        {
            var stopwatch = Stopwatch.StartNew();
            while (stopwatch.Elapsed < timeout)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (engine.IsGlobalFrameIndexAvailable)
                {
                    stopwatch.Stop();
                    return new IndexReadyResult(true, stopwatch.Elapsed.TotalMilliseconds);
                }

                await Task.Delay(50, cancellationToken).ConfigureAwait(false);
            }

            stopwatch.Stop();
            return new IndexReadyResult(engine.IsGlobalFrameIndexAvailable, stopwatch.Elapsed.TotalMilliseconds);
        }

        private static DualPaneBudgetHarnessDecodeSnapshot BuildDecodeSnapshot(FfmpegReviewEngine engine)
        {
            return new DualPaneBudgetHarnessDecodeSnapshot(
                string.IsNullOrWhiteSpace(engine.ActiveDecodeBackend) ? string.Empty : engine.ActiveDecodeBackend,
                string.IsNullOrWhiteSpace(engine.ActualBackendUsed) ? string.Empty : engine.ActualBackendUsed,
                engine.IsGpuActive,
                string.IsNullOrWhiteSpace(engine.GpuCapabilityStatus) ? string.Empty : engine.GpuCapabilityStatus,
                string.IsNullOrWhiteSpace(engine.GpuFallbackReason) ? string.Empty : engine.GpuFallbackReason,
                string.IsNullOrWhiteSpace(engine.BudgetBand) ? string.Empty : engine.BudgetBand,
                string.IsNullOrWhiteSpace(engine.HostResourceClass) ? string.Empty : engine.HostResourceClass,
                engine.SessionDecodedFrameCacheBudgetBytes,
                engine.DecodedFrameCacheBudgetBytes,
                engine.MaxPreviousCachedFrameCount,
                engine.MaxForwardCachedFrameCount,
                engine.PreviousCachedFrameCount,
                engine.ForwardCachedFrameCount,
                engine.OperationalQueueDepth,
                engine.LastHardwareFrameTransferMilliseconds,
                engine.LastBgraConversionMilliseconds,
                engine.IndexedFrameCount,
                engine.IsGlobalFrameIndexAvailable,
                string.IsNullOrWhiteSpace(engine.GlobalFrameIndexStatus) ? string.Empty : engine.GlobalFrameIndexStatus,
                engine.MediaInfo != null ? engine.MediaInfo.VideoCodecName : string.Empty,
                engine.MediaInfo != null ? engine.MediaInfo.FramesPerSecond : 0d);
        }

        private static ReviewWorkspacePaneOperationResult GetPaneResult(ReviewWorkspaceOperationResult result, string paneId)
        {
            if (result == null || string.IsNullOrWhiteSpace(paneId))
            {
                return null;
            }

            ReviewWorkspacePaneOperationResult paneResult;
            return result.TryGetPaneResult(paneId, out paneResult) ? paneResult : null;
        }

        private static ReviewPosition GetPanePosition(ReviewWorkspaceOperationResult result, string paneId)
        {
            var paneResult = GetPaneResult(result, paneId);
            return paneResult != null ? paneResult.Position : ReviewPosition.Empty;
        }

        private static void ObserveStepMetrics(
            ReviewWorkspaceOperationResult result,
            string paneId,
            DualPaneBudgetHarnessStepMetrics metrics)
        {
            if (result == null || metrics == null)
            {
                return;
            }

            var paneResult = GetPaneResult(result, paneId);
            if (paneResult == null || paneResult.FrameStepResult == null)
            {
                return;
            }

            metrics.TotalSteps++;
            if (paneResult.FrameStepResult.WasCacheHit)
            {
                metrics.CacheHitCount++;
            }

            if (paneResult.FrameStepResult.RequiredReconstruction)
            {
                metrics.ReconstructionCount++;
            }
        }

        private static int DetermineStepWindow(long primaryIndexedFrameCount, long compareIndexedFrameCount)
        {
            var minimumFrames = ResolveMinimumPositive(primaryIndexedFrameCount, compareIndexedFrameCount);
            if (minimumFrames <= 1L)
            {
                return 0;
            }

            return (int)Math.Max(1L, Math.Min(24L, minimumFrames - 1L));
        }

        private static long ResolveMinimumPositive(long left, long right)
        {
            if (left > 0L && right > 0L)
            {
                return Math.Min(left, right);
            }

            if (left > 0L)
            {
                return left;
            }

            if (right > 0L)
            {
                return right;
            }

            return 0L;
        }

        private static TimeSpan SelectSeekTarget(
            TimeSpan primaryDuration,
            TimeSpan compareDuration,
            TimeSpan primaryStep,
            TimeSpan compareStep)
        {
            var duration = primaryDuration <= TimeSpan.Zero
                ? compareDuration
                : compareDuration <= TimeSpan.Zero
                    ? primaryDuration
                    : primaryDuration <= compareDuration
                        ? primaryDuration
                        : compareDuration;
            if (duration <= TimeSpan.Zero)
            {
                return TimeSpan.Zero;
            }

            var margin = primaryStep > TimeSpan.Zero && compareStep > TimeSpan.Zero
                ? (primaryStep > compareStep ? primaryStep : compareStep)
                : primaryStep > TimeSpan.Zero
                    ? primaryStep
                    : compareStep;
            if (margin <= TimeSpan.Zero)
            {
                margin = TimeSpan.FromMilliseconds(1d);
            }

            var target = TimeSpan.FromTicks(duration.Ticks / 4L);
            if (target < margin)
            {
                target = margin;
            }

            var maxTarget = duration - margin;
            return target > maxTarget ? maxTarget : target;
        }

        private static long ResolveExpectedDualSessionBudgetBytes(DualPaneBudgetHarnessHostScenarioRequest hostScenario)
        {
            switch (hostScenario != null ? hostScenario.Name : string.Empty)
            {
                case "Business16":
                    return 768L * 1024L * 1024L;
                case "Workstation32To64":
                    return 1536L * 1024L * 1024L;
                case "Workstation128Plus":
                    return 2048L * 1024L * 1024L;
                default:
                    return hostScenario != null ? hostScenario.AvailablePhysicalMemoryBytes : 0L;
            }
        }

        private static DualPaneBudgetHarnessCheck CheckPaneOperation(
            string name,
            ReviewWorkspaceOperationResult operationResult,
            bool requirePaneAlignment)
        {
            if (operationResult == null)
            {
                return CheckTrue(name, false, "The synchronized workspace operation returned no result.");
            }

            if (!operationResult.Succeeded)
            {
                var failure = operationResult.FirstFailedPaneResult;
                return CheckTrue(
                    name,
                    false,
                    string.Format(
                        CultureInfo.InvariantCulture,
                        "The synchronized workspace operation failed. Pane={0}. Detail={1}.",
                        failure != null ? failure.PaneId : "(none)",
                        failure != null ? failure.FailureDetail : "(none)"));
            }

            if (operationResult.PaneCount != 2 ||
                operationResult.AttemptedPaneCount != 2 ||
                operationResult.SucceededPaneCount != 2)
            {
                return CheckTrue(
                    name,
                    false,
                    string.Format(
                        CultureInfo.InvariantCulture,
                        "The synchronized workspace operation did not attempt and succeed both panes. PaneCount={0}; Attempted={1}; Succeeded={2}.",
                        operationResult.PaneCount,
                        operationResult.AttemptedPaneCount,
                        operationResult.SucceededPaneCount));
            }

            var primaryPane = GetPaneResult(operationResult, "pane-primary");
            var comparePane = GetPaneResult(operationResult, "pane-compare");
            if (primaryPane == null || comparePane == null)
            {
                return CheckTrue(name, false, "The synchronized workspace operation did not return both pane results.");
            }

            if (primaryPane.FrameStepResult != null && !primaryPane.FrameStepResult.Success)
            {
                return CheckTrue(name, false, "Primary pane step result reported failure.");
            }

            if (comparePane.FrameStepResult != null && !comparePane.FrameStepResult.Success)
            {
                return CheckTrue(name, false, "Compare pane step result reported failure.");
            }

            if (requirePaneAlignment &&
                primaryPane.Position != null &&
                comparePane.Position != null &&
                (!primaryPane.Position.IsFrameIndexAbsolute || !comparePane.Position.IsFrameIndexAbsolute))
            {
                return CheckTrue(name, false, "Equivalent compare panes lost absolute frame identity during a synchronized operation.");
            }

            return CheckTrue(name, true, "The synchronized workspace operation succeeded for both panes.");
        }

        private static DualPaneBudgetHarnessCheck CheckEquivalentPosition(
            string name,
            ReviewPosition expected,
            ReviewPosition actual,
            string description)
        {
            expected = expected ?? ReviewPosition.Empty;
            actual = actual ?? ReviewPosition.Empty;
            var passed =
                expected.FrameIndex == actual.FrameIndex &&
                expected.IsFrameIndexAbsolute == actual.IsFrameIndexAbsolute &&
                expected.IsFrameAccurate == actual.IsFrameAccurate &&
                expected.PresentationTime == actual.PresentationTime;
            return CheckTrue(
                name,
                passed,
                string.Format(
                    CultureInfo.InvariantCulture,
                    "{0} Expected frame/time={1}/{2}; Actual frame/time={3}/{4}.",
                    description,
                    FormatFrameIndex(expected.FrameIndex),
                    FormatTime(expected.PresentationTime),
                    FormatFrameIndex(actual.FrameIndex),
                    FormatTime(actual.PresentationTime)));
        }

        private static DualPaneBudgetHarnessCheck CheckEqual<T>(
            string name,
            T expected,
            T actual,
            string description)
        {
            var passed = EqualityComparer<T>.Default.Equals(expected, actual);
            return CheckTrue(
                name,
                passed,
                string.Format(
                    CultureInfo.InvariantCulture,
                    "{0} Expected={1}; Actual={2}.",
                    description,
                    expected,
                    actual));
        }

        private static DualPaneBudgetHarnessCheck CheckTrue(string name, bool passed, string message)
        {
            return new DualPaneBudgetHarnessCheck(name, passed, message);
        }

        private static DualPaneBudgetHarnessPairSummary BuildPairSummary(IEnumerable<DualPaneBudgetHarnessCheck> checks)
        {
            var allChecks = checks != null ? checks.ToArray() : Array.Empty<DualPaneBudgetHarnessCheck>();
            var passCount = allChecks.Count(check => check != null && check.Passed);
            var failCount = allChecks.Count(check => check != null && !check.Passed);
            return new DualPaneBudgetHarnessPairSummary(allChecks.Length, passCount, failCount);
        }

        private static DualPaneBudgetHarnessHostSummary BuildHostSummary(IEnumerable<DualPaneBudgetHarnessPairReport> pairReports)
        {
            var reports = pairReports != null ? pairReports.ToArray() : Array.Empty<DualPaneBudgetHarnessPairReport>();
            var totalChecks = reports.Sum(report => report != null && report.Summary != null ? report.Summary.ChecksRun : 0);
            var totalPass = reports.Sum(report => report != null && report.Summary != null ? report.Summary.PassCount : 0);
            var totalFail = reports.Sum(report => report != null && report.Summary != null ? report.Summary.FailCount : 0);
            var pairCount = reports.Length;
            var failedPairs = reports.Count(report => report != null && report.Summary != null && report.Summary.FailCount > 0);
            return new DualPaneBudgetHarnessHostSummary(pairCount, failedPairs, totalChecks, totalPass, totalFail);
        }

        private static DualPaneBudgetHarnessSummary BuildOverallSummary(IEnumerable<DualPaneBudgetHarnessHostScenarioReport> hostReports)
        {
            var reports = hostReports != null ? hostReports.ToArray() : Array.Empty<DualPaneBudgetHarnessHostScenarioReport>();
            var pairCount = reports.Sum(report => report != null && report.Summary != null ? report.Summary.PairCount : 0);
            var failedPairs = reports.Sum(report => report != null && report.Summary != null ? report.Summary.FailedPairCount : 0);
            var totalChecks = reports.Sum(report => report != null && report.Summary != null ? report.Summary.ChecksRun : 0);
            var totalPass = reports.Sum(report => report != null && report.Summary != null ? report.Summary.PassCount : 0);
            var totalFail = reports.Sum(report => report != null && report.Summary != null ? report.Summary.FailCount : 0);
            return new DualPaneBudgetHarnessSummary(pairCount, failedPairs, totalChecks, totalPass, totalFail);
        }

        private static string FormatFrameIndex(long? frameIndex)
        {
            return frameIndex.HasValue ? frameIndex.Value.ToString(CultureInfo.InvariantCulture) : "(none)";
        }

        private static string FormatTime(TimeSpan time)
        {
            return time.ToString(@"hh\:mm\:ss\.fff", CultureInfo.InvariantCulture);
        }

        private sealed class MeasuredOperation
        {
            public MeasuredOperation(TimeSpan elapsed)
            {
                Elapsed = elapsed;
            }

            public TimeSpan Elapsed { get; }
        }

        private sealed class MeasuredOperation<T>
        {
            public MeasuredOperation(T result, TimeSpan elapsed)
            {
                Result = result;
                Elapsed = elapsed;
            }

            public T Result { get; }

            public TimeSpan Elapsed { get; }
        }

        private sealed class IndexReadyResult
        {
            public IndexReadyResult(bool ready, double elapsedMilliseconds)
            {
                Ready = ready;
                ElapsedMilliseconds = elapsedMilliseconds;
            }

            public bool Ready { get; }

            public double ElapsedMilliseconds { get; }
        }
    }
}
