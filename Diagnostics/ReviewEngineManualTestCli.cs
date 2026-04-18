using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace FramePlayer.Diagnostics
{
    internal static class ReviewEngineManualTestCli
    {
        private const string RequestArgument = "--run-review-engine-manual-tests-request";

        public static bool TryGetRequestPath(string[] args, out string requestPath)
        {
            requestPath = string.Empty;
            if (args == null || args.Length == 0)
            {
                return false;
            }

            for (var i = 0; i < args.Length; i++)
            {
                if (!string.Equals(args[i], RequestArgument, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (i + 1 >= args.Length || string.IsNullOrWhiteSpace(args[i + 1]))
                {
                    throw new ArgumentException("The manual test request path is missing.");
                }

                requestPath = Path.GetFullPath(args[i + 1]);
                return true;
            }

            return false;
        }

        public static async Task<int> RunAsync(string requestPath, CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(requestPath))
            {
                throw new ArgumentException("A manual test request path is required.", nameof(requestPath));
            }

            var request = ReadRequest(requestPath);
            if (request == null)
            {
                throw new InvalidOperationException("The manual test request could not be read.");
            }

            if (string.IsNullOrWhiteSpace(request.ReportJsonPath))
            {
                throw new InvalidOperationException("The manual test request did not include a report output path.");
            }

            var report = await ReviewEngineManualTestRunner.RunAsync(
                    request.FilePaths ?? Array.Empty<string>(),
                    cancellationToken)
                .ConfigureAwait(true);

            Directory.CreateDirectory(Path.GetDirectoryName(request.ReportJsonPath) ?? ".");
            WriteReport(request.ReportJsonPath, report);

            return CountFailures(report) > 0 ? 2 : 0;
        }

        public static void TryWriteFailure(string requestPath, Exception exception)
        {
            if (string.IsNullOrWhiteSpace(requestPath) || exception == null)
            {
                return;
            }

            try
            {
                var request = ReadRequest(requestPath);
                var errorPath = string.IsNullOrWhiteSpace(request.ErrorPath)
                    ? request.ReportJsonPath + ".error.txt"
                    : request.ErrorPath;
                if (string.IsNullOrWhiteSpace(errorPath))
                {
                    return;
                }

                Directory.CreateDirectory(Path.GetDirectoryName(errorPath) ?? ".");
                File.WriteAllText(errorPath, exception.ToString(), Encoding.UTF8);
            }
            catch
            {
            }
        }

        private static int CountFailures(ReviewEngineManualTestSessionReport report)
        {
            if (report == null || report.FileResults == null)
            {
                return 0;
            }

            return report.FileResults
                .Where(fileReport => fileReport != null && fileReport.BackendResults != null)
                .SelectMany(fileReport => fileReport.BackendResults)
                .Count(backendResult =>
                    backendResult != null &&
                    string.Equals(backendResult.Classification, "fail", StringComparison.OrdinalIgnoreCase));
        }

        private static ReviewEngineManualTestRequest ReadRequest(string requestPath)
        {
            if (!File.Exists(requestPath))
            {
                throw new FileNotFoundException("The manual test request file was not found.", requestPath);
            }

            string json;
            using (var stream = File.OpenRead(requestPath))
            using (var reader = new StreamReader(stream, Encoding.UTF8, true))
            {
                json = reader.ReadToEnd();
            }

            if (string.IsNullOrWhiteSpace(json))
            {
                return null;
            }

            using (var jsonStream = new MemoryStream(Encoding.UTF8.GetBytes(json.TrimStart('\uFEFF'))))
            {
                var serializer = new DataContractJsonSerializer(typeof(ReviewEngineManualTestRequest));
                return serializer.ReadObject(jsonStream) as ReviewEngineManualTestRequest;
            }
        }

        private static void WriteReport(string reportPath, ReviewEngineManualTestSessionReport report)
        {
            var export = ReviewEngineManualTestReportExport.FromReport(report);
            using (var stream = File.Create(reportPath))
            {
                var serializer = new DataContractJsonSerializer(
                    typeof(ReviewEngineManualTestReportExport),
                    new DataContractJsonSerializerSettings
                    {
                        UseSimpleDictionaryFormat = true
                    });
                serializer.WriteObject(stream, export);
            }
        }

        private static string FormatDuration(TimeSpan value)
        {
            return value.ToString("c", CultureInfo.InvariantCulture);
        }

        private static ReviewEngineDecodeSnapshotExport CreateDecodeSnapshotExport(
            string activeDecodeBackend,
            string actualBackendUsed,
            bool? isGpuActive,
            string gpuCapabilityStatus,
            string gpuFallbackReason,
            string budgetBand,
            string hostResourceClass,
            int? operationalQueueDepth,
            long? sessionDecodedFrameCacheBudgetBytes,
            long? decodedFrameCacheBudgetBytes,
            int? previousCachedFrameCount,
            int? forwardCachedFrameCount,
            int? maxPreviousCachedFrameCount,
            int? maxForwardCachedFrameCount,
            long? approximateCachedFrameBytes,
            double? hardwareFrameTransferMilliseconds,
            double? bgraConversionMilliseconds)
        {
            return new ReviewEngineDecodeSnapshotExport
            {
                DecodeBackend = activeDecodeBackend ?? string.Empty,
                ActualBackendUsed = actualBackendUsed ?? string.Empty,
                GpuActive = isGpuActive,
                GpuStatus = gpuCapabilityStatus ?? string.Empty,
                GpuFallbackReason = gpuFallbackReason ?? string.Empty,
                BudgetBand = budgetBand ?? string.Empty,
                HostResourceClass = hostResourceClass ?? string.Empty,
                QueueDepth = operationalQueueDepth,
                SessionCacheBudgetMiB = sessionDecodedFrameCacheBudgetBytes.HasValue
                    ? Math.Round(sessionDecodedFrameCacheBudgetBytes.Value / 1048576d, 3)
                    : (double?)null,
                CacheBudgetMiB = decodedFrameCacheBudgetBytes.HasValue
                    ? Math.Round(decodedFrameCacheBudgetBytes.Value / 1048576d, 3)
                    : (double?)null,
                CacheBack = previousCachedFrameCount,
                CacheAhead = forwardCachedFrameCount,
                MaxCacheBack = maxPreviousCachedFrameCount,
                MaxCacheAhead = maxForwardCachedFrameCount,
                ApproximateCacheMiB = approximateCachedFrameBytes.HasValue
                    ? Math.Round(approximateCachedFrameBytes.Value / 1048576d, 3)
                    : (double?)null,
                HwTransferMilliseconds = hardwareFrameTransferMilliseconds,
                BgraConversionMilliseconds = bgraConversionMilliseconds
            };
        }

        [DataContract]
        private sealed class ReviewEngineManualTestRequest
        {
            [DataMember(Name = "filePaths")]
            public string[] FilePaths { get; set; }

            [DataMember(Name = "reportJsonPath")]
            public string ReportJsonPath { get; set; }

            [DataMember(Name = "errorPath")]
            public string ErrorPath { get; set; }

            [DataMember(Name = "tracePath")]
            public string TracePath { get; set; }
        }

        [DataContract]
        private sealed class ReviewEngineManualTestReportExport
        {
            [DataMember(Name = "generatedAtUtc")]
            public string GeneratedAtUtc { get; set; }

            [DataMember(Name = "inputFiles")]
            public string[] InputFiles { get; set; }

            [DataMember(Name = "summary")]
            public ReviewEngineManualTestSummaryExport Summary { get; set; }

            [DataMember(Name = "files")]
            public ReviewEngineManualTestFileReportExport[] Files { get; set; }

            public static ReviewEngineManualTestReportExport FromReport(ReviewEngineManualTestSessionReport report)
            {
                report = report ?? new ReviewEngineManualTestSessionReport(
                    DateTimeOffset.MinValue,
                    Array.Empty<string>(),
                    Array.Empty<ReviewEngineManualTestFileReport>(),
                    new ReviewEngineManualTestSummary(0, 0, Array.Empty<ReviewEngineManualBackendSummary>()));

                return new ReviewEngineManualTestReportExport
                {
                    GeneratedAtUtc = report.GeneratedAtUtc.ToString("o", CultureInfo.InvariantCulture),
                    InputFiles = report.InputFiles ?? Array.Empty<string>(),
                    Summary = ReviewEngineManualTestSummaryExport.FromReport(report.Summary),
                    Files = (report.FileResults ?? Array.Empty<ReviewEngineManualTestFileReport>())
                        .Select(ReviewEngineManualTestFileReportExport.FromReport)
                        .ToArray()
                };
            }
        }

        [DataContract]
        private sealed class ReviewEngineManualTestSummaryExport
        {
            [DataMember(Name = "filesTested")]
            public int FilesTested { get; set; }

            [DataMember(Name = "backendRunsAttempted")]
            public int BackendRunsAttempted { get; set; }

            [DataMember(Name = "backends")]
            public ReviewEngineManualBackendSummaryExport[] Backends { get; set; }

            public static ReviewEngineManualTestSummaryExport FromReport(ReviewEngineManualTestSummary report)
            {
                report = report ?? new ReviewEngineManualTestSummary(0, 0, Array.Empty<ReviewEngineManualBackendSummary>());

                return new ReviewEngineManualTestSummaryExport
                {
                    FilesTested = report.FilesTested,
                    BackendRunsAttempted = report.BackendRunsAttempted,
                    Backends = (report.Backends ?? Array.Empty<ReviewEngineManualBackendSummary>())
                        .Select(ReviewEngineManualBackendSummaryExport.FromReport)
                        .ToArray()
                };
            }
        }

        [DataContract]
        private sealed class ReviewEngineManualBackendSummaryExport
        {
            [DataMember(Name = "backendName")]
            public string BackendName { get; set; }

            [DataMember(Name = "attempted")]
            public int Attempted { get; set; }

            [DataMember(Name = "passCount")]
            public int PassCount { get; set; }

            [DataMember(Name = "warningCount")]
            public int WarningCount { get; set; }

            [DataMember(Name = "failCount")]
            public int FailCount { get; set; }

            public static ReviewEngineManualBackendSummaryExport FromReport(ReviewEngineManualBackendSummary report)
            {
                report = report ?? new ReviewEngineManualBackendSummary(string.Empty, 0, 0, 0, 0);

                return new ReviewEngineManualBackendSummaryExport
                {
                    BackendName = report.BackendName ?? string.Empty,
                    Attempted = report.Attempted,
                    PassCount = report.PassCount,
                    WarningCount = report.WarningCount,
                    FailCount = report.FailCount
                };
            }
        }

        [DataContract]
        private sealed class ReviewEngineManualTestFileReportExport
        {
            [DataMember(Name = "filePath")]
            public string FilePath { get; set; }

            [DataMember(Name = "fileName")]
            public string FileName { get; set; }

            [DataMember(Name = "testPlan")]
            public ReviewEngineManualTestPlanExport TestPlan { get; set; }

            [DataMember(Name = "comparisonHighlights")]
            public string[] ComparisonHighlights { get; set; }

            [DataMember(Name = "backends")]
            public ReviewEngineManualBackendResultExport[] Backends { get; set; }

            public static ReviewEngineManualTestFileReportExport FromReport(ReviewEngineManualTestFileReport report)
            {
                report = report ?? new ReviewEngineManualTestFileReport(
                    string.Empty,
                    null,
                    null,
                    Array.Empty<ReviewEngineManualBackendResult>(),
                    Array.Empty<string>());

                return new ReviewEngineManualTestFileReportExport
                {
                    FilePath = report.FilePath ?? string.Empty,
                    FileName = Path.GetFileName(report.FilePath ?? string.Empty) ?? string.Empty,
                    TestPlan = ReviewEngineManualTestPlanExport.FromReport(report.TestPlan),
                    ComparisonHighlights = report.ComparisonHighlights ?? Array.Empty<string>(),
                    Backends = (report.BackendResults ?? Array.Empty<ReviewEngineManualBackendResult>())
                        .Select(backendResult => ReviewEngineManualBackendResultExport.FromReport(report, backendResult))
                        .ToArray()
                };
            }
        }

        [DataContract]
        private sealed class ReviewEngineManualTestPlanExport
        {
            [DataMember(Name = "seekTime")]
            public string SeekTime { get; set; }

            [DataMember(Name = "seekFrameIndex")]
            public long SeekFrameIndex { get; set; }

            [DataMember(Name = "seekTimeStrategy")]
            public string SeekTimeStrategy { get; set; }

            [DataMember(Name = "seekFrameStrategy")]
            public string SeekFrameStrategy { get; set; }

            [DataMember(Name = "durationKnown")]
            public bool DurationKnown { get; set; }

            [DataMember(Name = "nominalFpsKnown")]
            public bool NominalFpsKnown { get; set; }

            [DataMember(Name = "indexAvailable")]
            public bool IndexAvailable { get; set; }

            [DataMember(Name = "indexedFrameCount")]
            public long IndexedFrameCount { get; set; }

            [DataMember(Name = "reducedTestPathUsed")]
            public bool ReducedTestPathUsed { get; set; }

            [DataMember(Name = "sequenceSummary")]
            public string SequenceSummary { get; set; }

            [DataMember(Name = "warnings")]
            public string[] Warnings { get; set; }

            [DataMember(Name = "preflightError")]
            public string PreflightError { get; set; }

            public static ReviewEngineManualTestPlanExport FromReport(ReviewEngineManualTestPlan report)
            {
                report = report ?? new ReviewEngineManualTestPlan(
                    string.Empty,
                    TimeSpan.Zero,
                    0L,
                    false,
                    false,
                    false,
                    0L,
                    false,
                    false,
                    string.Empty,
                    string.Empty,
                    string.Empty,
                    Array.Empty<string>(),
                    string.Empty);

                return new ReviewEngineManualTestPlanExport
                {
                    SeekTime = FormatDuration(report.SeekTime),
                    SeekFrameIndex = report.SeekFrameIndex,
                    SeekTimeStrategy = report.SeekTimeStrategy ?? string.Empty,
                    SeekFrameStrategy = report.SeekFrameStrategy ?? string.Empty,
                    DurationKnown = report.DurationKnown,
                    NominalFpsKnown = report.NominalFpsKnown,
                    IndexAvailable = report.IndexAvailable,
                    IndexedFrameCount = report.IndexedFrameCount,
                    ReducedTestPathUsed = report.ReducedTestPathUsed,
                    SequenceSummary = report.SequenceSummary ?? string.Empty,
                    Warnings = report.Warnings ?? Array.Empty<string>(),
                    PreflightError = report.PreflightError ?? string.Empty
                };
            }
        }

        [DataContract]
        private sealed class ReviewEngineManualBackendResultExport
        {
            [DataMember(Name = "backendName")]
            public string BackendName { get; set; }

            [DataMember(Name = "classification")]
            public string Classification { get; set; }

            [DataMember(Name = "warnings")]
            public string[] Warnings { get; set; }

            [DataMember(Name = "failures")]
            public string[] Failures { get; set; }

            [DataMember(Name = "open")]
            public ReviewEngineOperationExport Open { get; set; }

            [DataMember(Name = "playback")]
            public ReviewEnginePlaybackOperationExport Playback { get; set; }

            [DataMember(Name = "seekToTime")]
            public ReviewEngineSeekOperationExport SeekToTime { get; set; }

            [DataMember(Name = "seekToFrame")]
            public ReviewEngineSeekOperationExport SeekToFrame { get; set; }

            [DataMember(Name = "stepBackward")]
            public ReviewEngineStepOperationExport StepBackward { get; set; }

            [DataMember(Name = "stepForward")]
            public ReviewEngineStepOperationExport StepForward { get; set; }

            [DataMember(Name = "scenarioError")]
            public string ScenarioError { get; set; }

            public static ReviewEngineManualBackendResultExport FromReport(
                ReviewEngineManualTestFileReport fileReport,
                ReviewEngineManualBackendResult report)
            {
                report = report ?? new ReviewEngineManualBackendResult(string.Empty, string.Empty, Array.Empty<string>(), Array.Empty<string>());
                var scenario = GetScenarioReportForBackend(fileReport, report.BackendName);

                return new ReviewEngineManualBackendResultExport
                {
                    BackendName = report.BackendName ?? string.Empty,
                    Classification = report.Classification ?? string.Empty,
                    Warnings = report.Warnings ?? Array.Empty<string>(),
                    Failures = report.Failures ?? Array.Empty<string>(),
                    Open = ReviewEngineOperationExport.FromOperationSnapshot(scenario != null ? scenario.OpenResult : null),
                    Playback = ReviewEnginePlaybackOperationExport.FromOperationSnapshot(scenario != null ? scenario.PlaybackResult : null),
                    SeekToTime = ReviewEngineSeekOperationExport.FromOperationSnapshot(scenario != null ? scenario.SeekToTimeResult : null),
                    SeekToFrame = ReviewEngineSeekOperationExport.FromOperationSnapshot(scenario != null ? scenario.SeekToFrameResult : null),
                    StepBackward = ReviewEngineStepOperationExport.FromStepOperationSnapshot(scenario != null ? scenario.BackwardStepResult : null),
                    StepForward = ReviewEngineStepOperationExport.FromStepOperationSnapshot(scenario != null ? scenario.ForwardStepResult : null),
                    ScenarioError = scenario != null ? scenario.ScenarioError ?? string.Empty : string.Empty
                };
            }

            private static ReviewEngineScenarioReport GetScenarioReportForBackend(
                ReviewEngineManualTestFileReport fileReport,
                string backendName)
            {
                if (fileReport == null || fileReport.ComparisonReport == null || string.IsNullOrWhiteSpace(backendName))
                {
                    return null;
                }

                if (string.Equals(backendName, "custom-ffmpeg", StringComparison.OrdinalIgnoreCase))
                {
                    return fileReport.ComparisonReport.CustomFfmpeg;
                }

                return null;
            }
        }

        [DataContract]
        private sealed class ReviewEngineOperationExport
        {
            [DataMember(Name = "succeeded")]
            public bool Succeeded { get; set; }

            [DataMember(Name = "elapsedMilliseconds")]
            public double ElapsedMilliseconds { get; set; }

            [DataMember(Name = "note")]
            public string Note { get; set; }

            [DataMember(Name = "errorMessage")]
            public string ErrorMessage { get; set; }

            [DataMember(Name = "codec")]
            public string Codec { get; set; }

            [DataMember(Name = "width")]
            public int Width { get; set; }

            [DataMember(Name = "height")]
            public int Height { get; set; }

            [DataMember(Name = "duration")]
            public string Duration { get; set; }

            [DataMember(Name = "nominalFps")]
            public double NominalFps { get; set; }

            [DataMember(Name = "hasAudioStream")]
            public bool? HasAudioStream { get; set; }

            [DataMember(Name = "audioPlaybackAvailable")]
            public bool? AudioPlaybackAvailable { get; set; }

            [DataMember(Name = "audioPlaybackActive")]
            public bool? AudioPlaybackActive { get; set; }

            [DataMember(Name = "lastPlaybackUsedAudioClock")]
            public bool? LastPlaybackUsedAudioClock { get; set; }

            [DataMember(Name = "lastAudioSubmittedBytes")]
            public long? LastAudioSubmittedBytes { get; set; }

            [DataMember(Name = "audioCodecName")]
            public string AudioCodecName { get; set; }

            [DataMember(Name = "audioErrorMessage")]
            public string AudioErrorMessage { get; set; }

            [DataMember(Name = "isGlobalIndexAvailable")]
            public bool? IsGlobalIndexAvailable { get; set; }

            [DataMember(Name = "indexedFrameCount")]
            public long? IndexedFrameCount { get; set; }

            [DataMember(Name = "usedGlobalIndex")]
            public bool? UsedGlobalIndex { get; set; }

            [DataMember(Name = "anchorStrategy")]
            public string AnchorStrategy { get; set; }

            [DataMember(Name = "anchorFrameIndex")]
            public long? AnchorFrameIndex { get; set; }

            [DataMember(Name = "positionFrameIndex")]
            public long? PositionFrameIndex { get; set; }

            [DataMember(Name = "positionAbsolute")]
            public bool PositionAbsolute { get; set; }

            [DataMember(Name = "decode")]
            public ReviewEngineDecodeSnapshotExport Decode { get; set; }

            public static ReviewEngineOperationExport FromOperationSnapshot(ReviewOperationSnapshot snapshot)
            {
                return new ReviewEngineOperationExport
                {
                    Succeeded = snapshot != null && snapshot.Succeeded,
                    ElapsedMilliseconds = snapshot != null ? Math.Round(snapshot.ElapsedMilliseconds, 3) : 0d,
                    Note = snapshot != null ? snapshot.Note ?? string.Empty : string.Empty,
                    ErrorMessage = snapshot != null ? snapshot.ErrorMessage ?? string.Empty : string.Empty,
                    Codec = snapshot != null && snapshot.MediaInfo != null ? snapshot.MediaInfo.VideoCodecName ?? string.Empty : string.Empty,
                    Width = snapshot != null && snapshot.MediaInfo != null ? snapshot.MediaInfo.PixelWidth : 0,
                    Height = snapshot != null && snapshot.MediaInfo != null ? snapshot.MediaInfo.PixelHeight : 0,
                    Duration = snapshot != null && snapshot.MediaInfo != null
                        ? FormatDuration(snapshot.MediaInfo.Duration)
                        : FormatDuration(TimeSpan.Zero),
                    NominalFps = snapshot != null && snapshot.MediaInfo != null ? snapshot.MediaInfo.FramesPerSecond : 0d,
                    HasAudioStream = snapshot != null ? snapshot.HasAudioStream : null,
                    AudioPlaybackAvailable = snapshot != null ? snapshot.AudioPlaybackAvailable : null,
                    AudioPlaybackActive = snapshot != null ? snapshot.AudioPlaybackActive : null,
                    LastPlaybackUsedAudioClock = snapshot != null ? snapshot.LastPlaybackUsedAudioClock : null,
                    LastAudioSubmittedBytes = snapshot != null ? snapshot.LastAudioSubmittedBytes : null,
                    AudioCodecName = snapshot != null ? snapshot.AudioCodecName ?? string.Empty : string.Empty,
                    AudioErrorMessage = snapshot != null ? snapshot.AudioErrorMessage ?? string.Empty : string.Empty,
                    IsGlobalIndexAvailable = snapshot != null ? snapshot.IsGlobalFrameIndexAvailable : null,
                    IndexedFrameCount = snapshot != null ? snapshot.IndexedFrameCount : null,
                    UsedGlobalIndex = snapshot != null ? snapshot.UsedGlobalIndex : null,
                    AnchorStrategy = snapshot != null ? snapshot.AnchorStrategy ?? string.Empty : string.Empty,
                    AnchorFrameIndex = snapshot != null ? snapshot.AnchorFrameIndex : null,
                    PositionFrameIndex = snapshot != null && snapshot.Position != null ? snapshot.Position.FrameIndex : null,
                    PositionAbsolute = snapshot != null && snapshot.Position != null && snapshot.Position.IsFrameIndexAbsolute,
                    Decode = snapshot != null
                        ? CreateDecodeSnapshotExport(
                            snapshot.ActiveDecodeBackend,
                            snapshot.ActualBackendUsed,
                            snapshot.IsGpuActive,
                            snapshot.GpuCapabilityStatus,
                            snapshot.GpuFallbackReason,
                            snapshot.BudgetBand,
                            snapshot.HostResourceClass,
                            snapshot.OperationalQueueDepth,
                            snapshot.SessionDecodedFrameCacheBudgetBytes,
                            snapshot.DecodedFrameCacheBudgetBytes,
                            snapshot.PreviousCachedFrameCount,
                            snapshot.ForwardCachedFrameCount,
                            snapshot.MaxPreviousCachedFrameCount,
                            snapshot.MaxForwardCachedFrameCount,
                            snapshot.ApproximateCachedFrameBytes,
                            snapshot.HardwareFrameTransferMilliseconds,
                            snapshot.BgraConversionMilliseconds)
                        : CreateDecodeSnapshotExport(
                            string.Empty,
                            string.Empty,
                            null,
                            string.Empty,
                            string.Empty,
                            string.Empty,
                            string.Empty,
                            null,
                            null,
                            null,
                            null,
                            null,
                            null,
                            null,
                            null,
                            null,
                            null)
                };
            }
        }

        [DataContract]
        private sealed class ReviewEnginePlaybackOperationExport
        {
            [DataMember(Name = "succeeded")]
            public bool Succeeded { get; set; }

            [DataMember(Name = "elapsedMilliseconds")]
            public double ElapsedMilliseconds { get; set; }

            [DataMember(Name = "note")]
            public string Note { get; set; }

            [DataMember(Name = "errorMessage")]
            public string ErrorMessage { get; set; }

            [DataMember(Name = "frameIndex")]
            public long? FrameIndex { get; set; }

            [DataMember(Name = "isFrameIndexAbsolute")]
            public bool IsFrameIndexAbsolute { get; set; }

            [DataMember(Name = "hasAudioStream")]
            public bool? HasAudioStream { get; set; }

            [DataMember(Name = "audioPlaybackAvailable")]
            public bool? AudioPlaybackAvailable { get; set; }

            [DataMember(Name = "audioPlaybackActive")]
            public bool? AudioPlaybackActive { get; set; }

            [DataMember(Name = "lastPlaybackUsedAudioClock")]
            public bool? LastPlaybackUsedAudioClock { get; set; }

            [DataMember(Name = "lastAudioSubmittedBytes")]
            public long? LastAudioSubmittedBytes { get; set; }

            [DataMember(Name = "audioCodecName")]
            public string AudioCodecName { get; set; }

            [DataMember(Name = "audioErrorMessage")]
            public string AudioErrorMessage { get; set; }

            [DataMember(Name = "decode")]
            public ReviewEngineDecodeSnapshotExport Decode { get; set; }

            public static ReviewEnginePlaybackOperationExport FromOperationSnapshot(ReviewOperationSnapshot snapshot)
            {
                return new ReviewEnginePlaybackOperationExport
                {
                    Succeeded = snapshot != null && snapshot.Succeeded,
                    ElapsedMilliseconds = snapshot != null ? Math.Round(snapshot.ElapsedMilliseconds, 3) : 0d,
                    Note = snapshot != null ? snapshot.Note ?? string.Empty : string.Empty,
                    ErrorMessage = snapshot != null ? snapshot.ErrorMessage ?? string.Empty : string.Empty,
                    FrameIndex = snapshot != null && snapshot.Position != null ? snapshot.Position.FrameIndex : null,
                    IsFrameIndexAbsolute = snapshot != null && snapshot.Position != null && snapshot.Position.IsFrameIndexAbsolute,
                    HasAudioStream = snapshot != null ? snapshot.HasAudioStream : null,
                    AudioPlaybackAvailable = snapshot != null ? snapshot.AudioPlaybackAvailable : null,
                    AudioPlaybackActive = snapshot != null ? snapshot.AudioPlaybackActive : null,
                    LastPlaybackUsedAudioClock = snapshot != null ? snapshot.LastPlaybackUsedAudioClock : null,
                    LastAudioSubmittedBytes = snapshot != null ? snapshot.LastAudioSubmittedBytes : null,
                    AudioCodecName = snapshot != null ? snapshot.AudioCodecName ?? string.Empty : string.Empty,
                    AudioErrorMessage = snapshot != null ? snapshot.AudioErrorMessage ?? string.Empty : string.Empty,
                    Decode = snapshot != null
                        ? CreateDecodeSnapshotExport(
                            snapshot.ActiveDecodeBackend,
                            snapshot.ActualBackendUsed,
                            snapshot.IsGpuActive,
                            snapshot.GpuCapabilityStatus,
                            snapshot.GpuFallbackReason,
                            snapshot.BudgetBand,
                            snapshot.HostResourceClass,
                            snapshot.OperationalQueueDepth,
                            snapshot.SessionDecodedFrameCacheBudgetBytes,
                            snapshot.DecodedFrameCacheBudgetBytes,
                            snapshot.PreviousCachedFrameCount,
                            snapshot.ForwardCachedFrameCount,
                            snapshot.MaxPreviousCachedFrameCount,
                            snapshot.MaxForwardCachedFrameCount,
                            snapshot.ApproximateCachedFrameBytes,
                            snapshot.HardwareFrameTransferMilliseconds,
                            snapshot.BgraConversionMilliseconds)
                        : CreateDecodeSnapshotExport(
                            string.Empty,
                            string.Empty,
                            null,
                            string.Empty,
                            string.Empty,
                            string.Empty,
                            string.Empty,
                            null,
                            null,
                            null,
                            null,
                            null,
                            null,
                            null,
                            null,
                            null,
                            null)
                };
            }
        }

        [DataContract]
        private sealed class ReviewEngineSeekOperationExport
        {
            [DataMember(Name = "succeeded")]
            public bool Succeeded { get; set; }

            [DataMember(Name = "elapsedMilliseconds")]
            public double ElapsedMilliseconds { get; set; }

            [DataMember(Name = "note")]
            public string Note { get; set; }

            [DataMember(Name = "errorMessage")]
            public string ErrorMessage { get; set; }

            [DataMember(Name = "presentationTime")]
            public string PresentationTime { get; set; }

            [DataMember(Name = "frameIndex")]
            public long? FrameIndex { get; set; }

            [DataMember(Name = "isFrameIndexAbsolute")]
            public bool IsFrameIndexAbsolute { get; set; }

            [DataMember(Name = "usedGlobalIndex")]
            public bool? UsedGlobalIndex { get; set; }

            [DataMember(Name = "anchorStrategy")]
            public string AnchorStrategy { get; set; }

            [DataMember(Name = "anchorFrameIndex")]
            public long? AnchorFrameIndex { get; set; }

            [DataMember(Name = "lastPlaybackUsedAudioClock")]
            public bool? LastPlaybackUsedAudioClock { get; set; }

            [DataMember(Name = "lastAudioSubmittedBytes")]
            public long? LastAudioSubmittedBytes { get; set; }

            [DataMember(Name = "decode")]
            public ReviewEngineDecodeSnapshotExport Decode { get; set; }

            public static ReviewEngineSeekOperationExport FromOperationSnapshot(ReviewOperationSnapshot snapshot)
            {
                return new ReviewEngineSeekOperationExport
                {
                    Succeeded = snapshot != null && snapshot.Succeeded,
                    ElapsedMilliseconds = snapshot != null ? Math.Round(snapshot.ElapsedMilliseconds, 3) : 0d,
                    Note = snapshot != null ? snapshot.Note ?? string.Empty : string.Empty,
                    ErrorMessage = snapshot != null ? snapshot.ErrorMessage ?? string.Empty : string.Empty,
                    PresentationTime = snapshot != null && snapshot.Position != null
                        ? FormatDuration(snapshot.Position.PresentationTime)
                        : FormatDuration(TimeSpan.Zero),
                    FrameIndex = snapshot != null && snapshot.Position != null ? snapshot.Position.FrameIndex : null,
                    IsFrameIndexAbsolute = snapshot != null && snapshot.Position != null && snapshot.Position.IsFrameIndexAbsolute,
                    UsedGlobalIndex = snapshot != null ? snapshot.UsedGlobalIndex : null,
                    AnchorStrategy = snapshot != null ? snapshot.AnchorStrategy ?? string.Empty : string.Empty,
                    AnchorFrameIndex = snapshot != null ? snapshot.AnchorFrameIndex : null,
                    LastPlaybackUsedAudioClock = snapshot != null ? snapshot.LastPlaybackUsedAudioClock : null,
                    LastAudioSubmittedBytes = snapshot != null ? snapshot.LastAudioSubmittedBytes : null,
                    Decode = snapshot != null
                        ? CreateDecodeSnapshotExport(
                            snapshot.ActiveDecodeBackend,
                            snapshot.ActualBackendUsed,
                            snapshot.IsGpuActive,
                            snapshot.GpuCapabilityStatus,
                            snapshot.GpuFallbackReason,
                            snapshot.BudgetBand,
                            snapshot.HostResourceClass,
                            snapshot.OperationalQueueDepth,
                            snapshot.SessionDecodedFrameCacheBudgetBytes,
                            snapshot.DecodedFrameCacheBudgetBytes,
                            snapshot.PreviousCachedFrameCount,
                            snapshot.ForwardCachedFrameCount,
                            snapshot.MaxPreviousCachedFrameCount,
                            snapshot.MaxForwardCachedFrameCount,
                            snapshot.ApproximateCachedFrameBytes,
                            snapshot.HardwareFrameTransferMilliseconds,
                            snapshot.BgraConversionMilliseconds)
                        : CreateDecodeSnapshotExport(
                            string.Empty,
                            string.Empty,
                            null,
                            string.Empty,
                            string.Empty,
                            string.Empty,
                            string.Empty,
                            null,
                            null,
                            null,
                            null,
                            null,
                            null,
                            null,
                            null,
                            null,
                            null)
                };
            }
        }

        [DataContract]
        private sealed class ReviewEngineStepOperationExport
        {
            [DataMember(Name = "succeeded")]
            public bool Succeeded { get; set; }

            [DataMember(Name = "elapsedMilliseconds")]
            public double ElapsedMilliseconds { get; set; }

            [DataMember(Name = "message")]
            public string Message { get; set; }

            [DataMember(Name = "frameIndex")]
            public long? FrameIndex { get; set; }

            [DataMember(Name = "isFrameIndexAbsolute")]
            public bool IsFrameIndexAbsolute { get; set; }

            [DataMember(Name = "wasCacheHit")]
            public bool WasCacheHit { get; set; }

            [DataMember(Name = "requiredReconstruction")]
            public bool RequiredReconstruction { get; set; }

            [DataMember(Name = "usedGlobalIndex")]
            public bool? UsedGlobalIndex { get; set; }

            [DataMember(Name = "anchorStrategy")]
            public string AnchorStrategy { get; set; }

            [DataMember(Name = "anchorFrameIndex")]
            public long? AnchorFrameIndex { get; set; }

            [DataMember(Name = "lastPlaybackUsedAudioClock")]
            public bool? LastPlaybackUsedAudioClock { get; set; }

            [DataMember(Name = "lastAudioSubmittedBytes")]
            public long? LastAudioSubmittedBytes { get; set; }

            [DataMember(Name = "decode")]
            public ReviewEngineDecodeSnapshotExport Decode { get; set; }

            public static ReviewEngineStepOperationExport FromStepOperationSnapshot(ReviewStepOperationSnapshot snapshot)
            {
                return new ReviewEngineStepOperationExport
                {
                    Succeeded = snapshot != null && snapshot.StepResult != null && snapshot.StepResult.Success,
                    ElapsedMilliseconds = snapshot != null ? Math.Round(snapshot.ElapsedMilliseconds, 3) : 0d,
                    Message = snapshot != null && snapshot.StepResult != null ? snapshot.StepResult.Message ?? string.Empty : string.Empty,
                    FrameIndex = snapshot != null && snapshot.StepResult != null && snapshot.StepResult.Position != null
                        ? snapshot.StepResult.Position.FrameIndex
                        : null,
                    IsFrameIndexAbsolute = snapshot != null &&
                                           snapshot.StepResult != null &&
                                           snapshot.StepResult.Position != null &&
                                           snapshot.StepResult.Position.IsFrameIndexAbsolute,
                    WasCacheHit = snapshot != null && snapshot.StepResult != null && snapshot.StepResult.WasCacheHit,
                    RequiredReconstruction = snapshot != null &&
                                             snapshot.StepResult != null &&
                                             snapshot.StepResult.RequiredReconstruction,
                    UsedGlobalIndex = snapshot != null ? snapshot.UsedGlobalIndex : null,
                    AnchorStrategy = snapshot != null ? snapshot.AnchorStrategy ?? string.Empty : string.Empty,
                    AnchorFrameIndex = snapshot != null ? snapshot.AnchorFrameIndex : null,
                    LastPlaybackUsedAudioClock = snapshot != null ? snapshot.LastPlaybackUsedAudioClock : null,
                    LastAudioSubmittedBytes = snapshot != null ? snapshot.LastAudioSubmittedBytes : null,
                    Decode = snapshot != null
                        ? CreateDecodeSnapshotExport(
                            snapshot.ActiveDecodeBackend,
                            snapshot.ActualBackendUsed,
                            snapshot.IsGpuActive,
                            snapshot.GpuCapabilityStatus,
                            snapshot.GpuFallbackReason,
                            snapshot.BudgetBand,
                            snapshot.HostResourceClass,
                            snapshot.OperationalQueueDepth,
                            snapshot.SessionDecodedFrameCacheBudgetBytes,
                            snapshot.DecodedFrameCacheBudgetBytes,
                            snapshot.PreviousCachedFrameCount,
                            snapshot.ForwardCachedFrameCount,
                            snapshot.MaxPreviousCachedFrameCount,
                            snapshot.MaxForwardCachedFrameCount,
                            snapshot.ApproximateCachedFrameBytes,
                            snapshot.HardwareFrameTransferMilliseconds,
                            snapshot.BgraConversionMilliseconds)
                        : CreateDecodeSnapshotExport(
                            string.Empty,
                            string.Empty,
                            null,
                            string.Empty,
                            string.Empty,
                            string.Empty,
                            string.Empty,
                            null,
                            null,
                            null,
                            null,
                            null,
                            null,
                            null,
                            null,
                            null,
                            null)
                };
            }
        }

        [DataContract]
        private sealed class ReviewEngineDecodeSnapshotExport
        {
            [DataMember(Name = "decodeBackend")]
            public string DecodeBackend { get; set; }

            [DataMember(Name = "actualBackendUsed")]
            public string ActualBackendUsed { get; set; }

            [DataMember(Name = "gpuActive")]
            public bool? GpuActive { get; set; }

            [DataMember(Name = "gpuStatus")]
            public string GpuStatus { get; set; }

            [DataMember(Name = "gpuFallbackReason")]
            public string GpuFallbackReason { get; set; }

            [DataMember(Name = "budgetBand")]
            public string BudgetBand { get; set; }

            [DataMember(Name = "hostResourceClass")]
            public string HostResourceClass { get; set; }

            [DataMember(Name = "queueDepth")]
            public int? QueueDepth { get; set; }

            [DataMember(Name = "sessionCacheBudgetMiB")]
            public double? SessionCacheBudgetMiB { get; set; }

            [DataMember(Name = "cacheBudgetMiB")]
            public double? CacheBudgetMiB { get; set; }

            [DataMember(Name = "cacheBack")]
            public int? CacheBack { get; set; }

            [DataMember(Name = "cacheAhead")]
            public int? CacheAhead { get; set; }

            [DataMember(Name = "maxCacheBack")]
            public int? MaxCacheBack { get; set; }

            [DataMember(Name = "maxCacheAhead")]
            public int? MaxCacheAhead { get; set; }

            [DataMember(Name = "approximateCacheMiB")]
            public double? ApproximateCacheMiB { get; set; }

            [DataMember(Name = "hwTransferMilliseconds")]
            public double? HwTransferMilliseconds { get; set; }

            [DataMember(Name = "bgraConversionMilliseconds")]
            public double? BgraConversionMilliseconds { get; set; }
        }
    }
}
