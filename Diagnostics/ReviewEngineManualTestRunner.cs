using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FramePlayer.Engines.FFmpeg;

namespace FramePlayer.Diagnostics
{
    public static class ReviewEngineManualTestRunner
    {
        private static readonly StringComparer FilePathComparer = StringComparer.OrdinalIgnoreCase;
        private static readonly TimeSpan DefaultSeekMargin = TimeSpan.FromMilliseconds(100d);

        public static async Task<ReviewEngineManualTestSessionReport> RunAsync(
            IEnumerable<string> filePaths,
            CancellationToken cancellationToken = default(CancellationToken))
        {
            if (filePaths == null)
            {
                throw new ArgumentNullException(nameof(filePaths));
            }

            var normalizedFilePaths = NormalizeFilePaths(filePaths).ToArray();
            if (normalizedFilePaths.Length == 0)
            {
                throw new ArgumentException("At least one media file path is required.", nameof(filePaths));
            }

            var fileReports = new List<ReviewEngineManualTestFileReport>(normalizedFilePaths.Length);
            foreach (var filePath in normalizedFilePaths)
            {
                cancellationToken.ThrowIfCancellationRequested();
                fileReports.Add(await RunFileAsync(filePath, cancellationToken).ConfigureAwait(false));
            }

            return new ReviewEngineManualTestSessionReport(
                DateTimeOffset.UtcNow,
                normalizedFilePaths,
                fileReports.ToArray(),
                BuildSummary(fileReports));
        }

        private static IEnumerable<string> NormalizeFilePaths(IEnumerable<string> filePaths)
        {
            var seenPaths = new HashSet<string>(FilePathComparer);
            foreach (var rawPath in filePaths)
            {
                if (string.IsNullOrWhiteSpace(rawPath))
                {
                    continue;
                }

                var fullPath = Path.GetFullPath(rawPath);
                if (!File.Exists(fullPath))
                {
                    throw new FileNotFoundException("The requested media file was not found.", fullPath);
                }

                if (seenPaths.Add(fullPath))
                {
                    yield return fullPath;
                }
            }
        }

        private static async Task<ReviewEngineManualTestFileReport> RunFileAsync(
            string filePath,
            CancellationToken cancellationToken)
        {
            var plan = await BuildTestPlanAsync(filePath, cancellationToken).ConfigureAwait(false);
            var comparisonReport = await ReviewEngineComparisonRunner.RunAsync(
                    filePath,
                    plan.SeekTime,
                    plan.SeekFrameIndex,
                    cancellationToken)
                .ConfigureAwait(false);

            var backendResults = new[]
            {
                EvaluateBackend(comparisonReport.CustomFfmpeg, plan)
            };

            return new ReviewEngineManualTestFileReport(
                filePath,
                plan,
                comparisonReport,
                backendResults,
                BuildComparisonHighlights(comparisonReport, backendResults));
        }

        private static async Task<ReviewEngineManualTestPlan> BuildTestPlanAsync(
            string filePath,
            CancellationToken cancellationToken)
        {
            var warnings = new List<string>();
            var duration = TimeSpan.Zero;
            var positionStep = TimeSpan.Zero;
            var framesPerSecond = 0d;
            var indexedFrameCount = 0L;
            var indexAvailable = false;
            var preflightError = string.Empty;

            using (var engine = new FfmpegReviewEngine())
            {
                try
                {
                    await engine.OpenAsync(filePath, cancellationToken).ConfigureAwait(false);
                    duration = engine.MediaInfo.Duration;
                    positionStep = engine.MediaInfo.PositionStep;
                    framesPerSecond = engine.MediaInfo.FramesPerSecond;
                    indexAvailable = engine.IsGlobalFrameIndexAvailable;
                    indexedFrameCount = engine.IndexedFrameCount;
                }
                catch (Exception ex)
                {
                    preflightError = ex.Message;
                    warnings.Add("Custom FFmpeg preflight probe failed, so the test plan fell back to start-position defaults.");
                }
            }

            var durationKnown = duration > TimeSpan.Zero;
            var nominalFpsKnown = framesPerSecond > 0d;
            var reducedTestPathUsed = false;

            TimeSpan seekTime;
            string seekTimeStrategy;
            if (durationKnown)
            {
                seekTime = SelectSeekTime(duration, positionStep);
                if (seekTime > TimeSpan.Zero)
                {
                    seekTimeStrategy = "quarter-duration";
                }
                else
                {
                    seekTimeStrategy = "start-short-clip";
                    reducedTestPathUsed = true;
                    warnings.Add("The clip is short enough that the manual seek-to-time target was clamped to the start position.");
                }
            }
            else
            {
                seekTime = TimeSpan.Zero;
                seekTimeStrategy = "start-fallback";
                reducedTestPathUsed = true;
                warnings.Add("Duration was unavailable, so seek-to-time uses the start position.");
            }

            long seekFrameIndex;
            string seekFrameStrategy;
            var limitedSteppingExpected = false;

            if (indexAvailable && indexedFrameCount > 0L)
            {
                seekFrameIndex = SelectMidpointFrameIndex(indexedFrameCount);
                seekFrameStrategy = indexedFrameCount > 1L ? "global-index-midpoint" : "global-index-first-frame";
                limitedSteppingExpected = indexedFrameCount <= 1L;
            }
            else if (durationKnown && nominalFpsKnown)
            {
                var estimatedFrameCount = EstimateFrameCount(duration, framesPerSecond);
                seekFrameIndex = SelectMidpointFrameIndex(estimatedFrameCount);
                seekFrameStrategy = estimatedFrameCount > 1L ? "duration-fps-midpoint" : "duration-fps-start-fallback";
                reducedTestPathUsed = true;
                limitedSteppingExpected = estimatedFrameCount <= 1L;
                warnings.Add("Global index data was unavailable during planning, so the frame target used duration/fps estimation.");
            }
            else
            {
                seekFrameIndex = 0L;
                seekFrameStrategy = "start-frame-fallback";
                reducedTestPathUsed = true;
                limitedSteppingExpected = true;
                warnings.Add("Frame-target planning fell back to frame 0 because no reliable index or duration/fps estimate was available.");
            }

            if (!indexAvailable && string.IsNullOrWhiteSpace(preflightError))
            {
                warnings.Add("The custom FFmpeg global index was unavailable during preflight planning.");
            }

            if (limitedSteppingExpected)
            {
                warnings.Add("This clip only exposes one safe target frame in the current plan, so backward/forward steps may stop at a boundary.");
            }

            return new ReviewEngineManualTestPlan(
                filePath,
                seekTime,
                seekFrameIndex,
                durationKnown,
                nominalFpsKnown,
                indexAvailable,
                indexedFrameCount,
                reducedTestPathUsed,
                limitedSteppingExpected,
                seekTimeStrategy,
                seekFrameStrategy,
                string.Format(
                    CultureInfo.InvariantCulture,
                    "Open, run a brief playback/pause check, seek-to-time via {0} at {1}, seek-to-frame via {2} at frame {3}, then step backward once and forward once.",
                    seekTimeStrategy,
                    seekTime,
                    seekFrameStrategy,
                    seekFrameIndex),
                warnings.ToArray(),
                preflightError);
        }

        private static TimeSpan SelectSeekTime(TimeSpan duration, TimeSpan positionStep)
        {
            if (duration <= TimeSpan.Zero)
            {
                return TimeSpan.Zero;
            }

            var margin = positionStep > TimeSpan.Zero ? positionStep : DefaultSeekMargin;
            if (duration <= margin)
            {
                return TimeSpan.Zero;
            }

            var candidate = TimeSpan.FromTicks(duration.Ticks / 4L);
            if (candidate < margin)
            {
                candidate = margin;
            }

            var maxCandidate = duration - margin;
            if (maxCandidate <= TimeSpan.Zero)
            {
                return TimeSpan.Zero;
            }

            if (candidate > maxCandidate)
            {
                candidate = maxCandidate;
            }

            return candidate < TimeSpan.Zero ? TimeSpan.Zero : candidate;
        }

        private static long SelectMidpointFrameIndex(long frameCount)
        {
            if (frameCount <= 1L)
            {
                return 0L;
            }

            var midpoint = frameCount / 2L;
            return midpoint >= frameCount ? frameCount - 1L : midpoint;
        }

        private static long EstimateFrameCount(TimeSpan duration, double framesPerSecond)
        {
            if (duration <= TimeSpan.Zero || framesPerSecond <= 0d)
            {
                return 0L;
            }

            var estimatedFrameCount = Math.Floor(duration.TotalSeconds * framesPerSecond);
            if (estimatedFrameCount <= 0d)
            {
                return 0L;
            }

            if (estimatedFrameCount >= long.MaxValue)
            {
                return long.MaxValue;
            }

            return (long)estimatedFrameCount;
        }

        private static ReviewEngineManualBackendResult EvaluateBackend(
            ReviewEngineScenarioReport scenario,
            ReviewEngineManualTestPlan plan)
        {
            var warnings = new List<string>();
            var failures = new List<string>();

            if (plan != null && plan.Warnings != null && plan.Warnings.Length > 0)
            {
                warnings.AddRange(plan.Warnings);
            }

            if (scenario == null)
            {
                failures.Add("The backend scenario report was not produced.");
                return new ReviewEngineManualBackendResult(string.Empty, "fail", warnings.ToArray(), failures.ToArray());
            }

            EvaluateOperation("open", scenario.OpenResult, failures);
            EvaluateOperation("playback", scenario.PlaybackResult, failures);
            EvaluateOperation("seek-to-time", scenario.SeekToTimeResult, failures);
            EvaluateOperation("seek-to-frame", scenario.SeekToFrameResult, failures);
            EvaluateStep("step-backward", scenario.BackwardStepResult, failures, warnings, plan != null && plan.LimitedSteppingExpected);
            EvaluateStep("step-forward", scenario.ForwardStepResult, failures, warnings, plan != null && plan.LimitedSteppingExpected);

            if (!string.IsNullOrWhiteSpace(scenario.ScenarioError))
            {
                failures.Add("Scenario error: " + scenario.ScenarioError);
            }

            if (string.Equals(scenario.BackendName, "custom-ffmpeg", StringComparison.OrdinalIgnoreCase))
            {
                if (scenario.OpenResult == null || scenario.OpenResult.IsGlobalFrameIndexAvailable != true)
                {
                    warnings.Add("Custom FFmpeg did not report a usable global frame index during the test run.");
                }
            }

            if (scenario.SeekToFrameResult != null &&
                scenario.SeekToFrameResult.Succeeded &&
                !scenario.SeekToFrameResult.Position.IsFrameIndexAbsolute)
            {
                warnings.Add("Seek-to-frame did not retain absolute frame identity.");
            }

            if (scenario.SeekToTimeResult != null &&
                scenario.SeekToTimeResult.Succeeded &&
                !scenario.SeekToTimeResult.Position.IsFrameIndexAbsolute)
            {
                warnings.Add("Seek-to-time did not report absolute frame identity.");
            }

            var classification = failures.Count > 0
                ? "fail"
                : warnings.Count > 0
                    ? "warning"
                    : "pass";

            return new ReviewEngineManualBackendResult(
                scenario.BackendName,
                classification,
                warnings.Distinct(StringComparer.Ordinal).ToArray(),
                failures.Distinct(StringComparer.Ordinal).ToArray());
        }

        private static void EvaluateOperation(
            string operationName,
            ReviewOperationSnapshot snapshot,
            ICollection<string> failures)
        {
            if (snapshot == null)
            {
                failures.Add(operationName + " did not produce a snapshot.");
                return;
            }

            if (!snapshot.Succeeded)
            {
                failures.Add(
                    string.Format(
                        CultureInfo.InvariantCulture,
                        "{0} failed: {1}",
                        operationName,
                        string.IsNullOrWhiteSpace(snapshot.ErrorMessage) ? "Unknown error." : snapshot.ErrorMessage));
            }
        }

        private static void EvaluateStep(
            string operationName,
            ReviewStepOperationSnapshot snapshot,
            ICollection<string> failures,
            ICollection<string> warnings,
            bool limitedSteppingExpected)
        {
            if (snapshot == null)
            {
                failures.Add(operationName + " did not produce a snapshot.");
                return;
            }

            if (snapshot.StepResult == null)
            {
                failures.Add(operationName + " did not return a step result.");
                return;
            }

            if (snapshot.StepResult.Success)
            {
                return;
            }

            var detail = string.IsNullOrWhiteSpace(snapshot.StepResult.Message)
                ? "The operation did not succeed."
                : snapshot.StepResult.Message;

            if (limitedSteppingExpected)
            {
                warnings.Add(operationName + " hit a clip boundary or unsupported step case during the reduced test path: " + detail);
                return;
            }

            failures.Add(operationName + " failed: " + detail);
        }

        private static string[] BuildComparisonHighlights(
            ReviewEngineComparisonReport comparisonReport,
            IReadOnlyList<ReviewEngineManualBackendResult> backendResults)
        {
            var highlights = new List<string>();
            if (comparisonReport == null)
            {
                return highlights.ToArray();
            }

            var customSeekFrame = comparisonReport.CustomFfmpeg != null ? comparisonReport.CustomFfmpeg.SeekToFrameResult : null;

            if (customSeekFrame != null && customSeekFrame.Succeeded)
            {
                highlights.Add(
                    string.Format(
                        CultureInfo.InvariantCulture,
                        "Custom FFmpeg absolute frame identity after seek-to-frame: {0}.",
                        customSeekFrame.Position.IsFrameIndexAbsolute ? "yes" : "no"));
            }

            return highlights.ToArray();
        }

        private static ReviewEngineManualTestSummary BuildSummary(
            IReadOnlyCollection<ReviewEngineManualTestFileReport> fileReports)
        {
            var backendResults = fileReports
                .Where(report => report != null && report.BackendResults != null)
                .SelectMany(report => report.BackendResults)
                .Where(result => result != null)
                .ToArray();

            var backendSummaries = backendResults
                .GroupBy(result => result.BackendName ?? string.Empty, StringComparer.OrdinalIgnoreCase)
                .Select(group => new ReviewEngineManualBackendSummary(
                    group.Key,
                    group.Count(),
                    group.Count(result => string.Equals(result.Classification, "pass", StringComparison.OrdinalIgnoreCase)),
                    group.Count(result => string.Equals(result.Classification, "warning", StringComparison.OrdinalIgnoreCase)),
                    group.Count(result => string.Equals(result.Classification, "fail", StringComparison.OrdinalIgnoreCase))))
                .OrderBy(summary => summary.BackendName, StringComparer.OrdinalIgnoreCase)
                .ToArray();

            return new ReviewEngineManualTestSummary(
                fileReports.Count,
                backendResults.Length,
                backendSummaries);
        }
    }

    public sealed class ReviewEngineManualTestSessionReport
    {
        public ReviewEngineManualTestSessionReport(
            DateTimeOffset generatedAtUtc,
            string[] inputFiles,
            ReviewEngineManualTestFileReport[] fileResults,
            ReviewEngineManualTestSummary summary)
        {
            GeneratedAtUtc = generatedAtUtc;
            InputFiles = inputFiles ?? new string[0];
            FileResults = fileResults ?? new ReviewEngineManualTestFileReport[0];
            Summary = summary;
        }

        public DateTimeOffset GeneratedAtUtc { get; }

        public string[] InputFiles { get; }

        public ReviewEngineManualTestFileReport[] FileResults { get; }

        public ReviewEngineManualTestSummary Summary { get; }
    }

    public sealed class ReviewEngineManualTestFileReport
    {
        public ReviewEngineManualTestFileReport(
            string filePath,
            ReviewEngineManualTestPlan testPlan,
            ReviewEngineComparisonReport comparisonReport,
            ReviewEngineManualBackendResult[] backendResults,
            string[] comparisonHighlights)
        {
            FilePath = filePath ?? string.Empty;
            TestPlan = testPlan;
            ComparisonReport = comparisonReport;
            BackendResults = backendResults ?? new ReviewEngineManualBackendResult[0];
            ComparisonHighlights = comparisonHighlights ?? new string[0];
        }

        public string FilePath { get; }

        public ReviewEngineManualTestPlan TestPlan { get; }

        public ReviewEngineComparisonReport ComparisonReport { get; }

        public ReviewEngineManualBackendResult[] BackendResults { get; }

        public string[] ComparisonHighlights { get; }
    }

    public sealed class ReviewEngineManualTestPlan
    {
        public ReviewEngineManualTestPlan(
            string filePath,
            TimeSpan seekTime,
            long seekFrameIndex,
            bool durationKnown,
            bool nominalFpsKnown,
            bool indexAvailable,
            long indexedFrameCount,
            bool reducedTestPathUsed,
            bool limitedSteppingExpected,
            string seekTimeStrategy,
            string seekFrameStrategy,
            string sequenceSummary,
            string[] warnings,
            string preflightError)
        {
            FilePath = filePath ?? string.Empty;
            SeekTime = seekTime;
            SeekFrameIndex = seekFrameIndex;
            DurationKnown = durationKnown;
            NominalFpsKnown = nominalFpsKnown;
            IndexAvailable = indexAvailable;
            IndexedFrameCount = indexedFrameCount;
            ReducedTestPathUsed = reducedTestPathUsed;
            LimitedSteppingExpected = limitedSteppingExpected;
            SeekTimeStrategy = seekTimeStrategy ?? string.Empty;
            SeekFrameStrategy = seekFrameStrategy ?? string.Empty;
            SequenceSummary = sequenceSummary ?? string.Empty;
            Warnings = warnings ?? new string[0];
            PreflightError = preflightError ?? string.Empty;
        }

        public string FilePath { get; }

        public TimeSpan SeekTime { get; }

        public long SeekFrameIndex { get; }

        public bool DurationKnown { get; }

        public bool NominalFpsKnown { get; }

        public bool IndexAvailable { get; }

        public long IndexedFrameCount { get; }

        public bool ReducedTestPathUsed { get; }

        public bool LimitedSteppingExpected { get; }

        public string SeekTimeStrategy { get; }

        public string SeekFrameStrategy { get; }

        public string SequenceSummary { get; }

        public string[] Warnings { get; }

        public string PreflightError { get; }
    }

    public sealed class ReviewEngineManualBackendResult
    {
        public ReviewEngineManualBackendResult(
            string backendName,
            string classification,
            string[] warnings,
            string[] failures)
        {
            BackendName = backendName ?? string.Empty;
            Classification = classification ?? string.Empty;
            Warnings = warnings ?? new string[0];
            Failures = failures ?? new string[0];
        }

        public string BackendName { get; }

        public string Classification { get; }

        public string[] Warnings { get; }

        public string[] Failures { get; }
    }

    public sealed class ReviewEngineManualTestSummary
    {
        public ReviewEngineManualTestSummary(
            int filesTested,
            int backendRunsAttempted,
            ReviewEngineManualBackendSummary[] backends)
        {
            FilesTested = filesTested;
            BackendRunsAttempted = backendRunsAttempted;
            Backends = backends ?? new ReviewEngineManualBackendSummary[0];
        }

        public int FilesTested { get; }

        public int BackendRunsAttempted { get; }

        public ReviewEngineManualBackendSummary[] Backends { get; }
    }

    public sealed class ReviewEngineManualBackendSummary
    {
        public ReviewEngineManualBackendSummary(
            string backendName,
            int attempted,
            int passCount,
            int warningCount,
            int failCount)
        {
            BackendName = backendName ?? string.Empty;
            Attempted = attempted;
            PassCount = passCount;
            WarningCount = warningCount;
            FailCount = failCount;
        }

        public string BackendName { get; }

        public int Attempted { get; }

        public int PassCount { get; }

        public int WarningCount { get; }

        public int FailCount { get; }
    }
}
