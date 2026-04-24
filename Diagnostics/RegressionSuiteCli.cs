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
    internal static class RegressionSuiteCli
    {
        private const string RequestArgument = "--run-regression-suite-request";

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
                    throw new ArgumentException("The regression suite request path is missing.");
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
                throw new ArgumentException("A regression suite request path is required.", nameof(requestPath));
            }

            var request = ReadRequest(requestPath);
            if (request == null)
            {
                throw new InvalidOperationException("The regression suite request could not be read.");
            }

            RegressionSuiteRunner.DiagnosticTracePath = request.TracePath;

            if (string.IsNullOrWhiteSpace(request.ReportJsonPath))
            {
                throw new InvalidOperationException("The regression suite request did not include a report output path.");
            }

            var report = await RegressionSuiteRunner.RunAsync(
                    request.FilePaths ?? new string[0],
                    request.PackagedOutputDirectory,
                    request.PackagedArtifactPath,
                    request.RuntimeManifestPath,
                    request.AudioInsertionMp3FixturePath,
                    cancellationToken)
                .ConfigureAwait(true);

            Directory.CreateDirectory(Path.GetDirectoryName(request.ReportJsonPath) ?? ".");
            WriteReport(request.ReportJsonPath, report);

            return report.Summary != null && report.Summary.FailCount > 0 ? 2 : 0;
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
                File.WriteAllText(errorPath, exception.ToString());
            }
            catch
            {
                // Best-effort failure logging must never hide the original regression-suite exception path.
            }
        }

        private static RegressionSuiteRequest ReadRequest(string requestPath)
        {
            if (!File.Exists(requestPath))
            {
                throw new FileNotFoundException("The regression suite request file was not found.", requestPath);
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
                var serializer = new DataContractJsonSerializer(typeof(RegressionSuiteRequest));
                return serializer.ReadObject(jsonStream) as RegressionSuiteRequest;
            }
        }

        private static void WriteReport(string reportPath, RegressionSuiteReport report)
        {
            var export = RegressionSuiteReportExport.FromReport(report);
            using (var stream = File.Create(reportPath))
            {
                var serializer = new DataContractJsonSerializer(
                    typeof(RegressionSuiteReportExport),
                    new DataContractJsonSerializerSettings
                    {
                        UseSimpleDictionaryFormat = true
                    });
                serializer.WriteObject(stream, export);
            }
        }

        [DataContract]
        public sealed class RegressionSuiteRequest
        {
            [DataMember(Name = "filePaths")]
            public string[] FilePaths { get; set; }

            [DataMember(Name = "packagedOutputDirectory")]
            public string PackagedOutputDirectory { get; set; }

            [DataMember(Name = "packagedArtifactPath")]
            public string PackagedArtifactPath { get; set; }

            [DataMember(Name = "runtimeManifestPath")]
            public string RuntimeManifestPath { get; set; }

            [DataMember(Name = "audioInsertionMp3FixturePath")]
            public string AudioInsertionMp3FixturePath { get; set; }

            [DataMember(Name = "reportJsonPath")]
            public string ReportJsonPath { get; set; }

            [DataMember(Name = "errorPath")]
            public string ErrorPath { get; set; }

            [DataMember(Name = "tracePath")]
            public string TracePath { get; set; }
        }

        [DataContract]
        private sealed class RegressionSuiteReportExport
        {
            [DataMember(Name = "generatedAtUtc")]
            public string GeneratedAtUtc { get; set; }

            [DataMember(Name = "packagedOutputDirectory")]
            public string PackagedOutputDirectory { get; set; }

            [DataMember(Name = "packagedArtifactPath")]
            public string PackagedArtifactPath { get; set; }

            [DataMember(Name = "packaging")]
            public RegressionPackagingReportExport Packaging { get; set; }

            [DataMember(Name = "fileResults")]
            public RegressionFileReportExport[] FileResults { get; set; }

            [DataMember(Name = "summary")]
            public RegressionSummaryExport Summary { get; set; }

            public static RegressionSuiteReportExport FromReport(RegressionSuiteReport report)
            {
                report = report ?? new RegressionSuiteReport(
                    string.Empty,
                    string.Empty,
                    string.Empty,
                    null,
                    new RegressionFileReport[0],
                    new RegressionSummary(0, 0, 0, 0, 0));

                return new RegressionSuiteReportExport
                {
                    GeneratedAtUtc = report.GeneratedAtUtc ?? string.Empty,
                    PackagedOutputDirectory = report.PackagedOutputDirectory ?? string.Empty,
                    PackagedArtifactPath = report.PackagedArtifactPath ?? string.Empty,
                    Packaging = RegressionPackagingReportExport.FromReport(report.Packaging),
                    FileResults = (report.FileResults ?? new RegressionFileReport[0])
                        .Select(RegressionFileReportExport.FromReport)
                        .ToArray(),
                    Summary = RegressionSummaryExport.FromReport(report.Summary)
                };
            }
        }

        [DataContract]
        private sealed class RegressionPackagingReportExport
        {
            [DataMember(Name = "outputDirectory")]
            public string OutputDirectory { get; set; }

            [DataMember(Name = "artifactPath")]
            public string ArtifactPath { get; set; }

            [DataMember(Name = "expectedRuntimeFiles")]
            public string[] ExpectedRuntimeFiles { get; set; }

            [DataMember(Name = "presentRuntimeFiles")]
            public string[] PresentRuntimeFiles { get; set; }

            [DataMember(Name = "missingRuntimeFiles")]
            public string[] MissingRuntimeFiles { get; set; }

            [DataMember(Name = "staleRuntimeFiles")]
            public string[] StaleRuntimeFiles { get; set; }

            [DataMember(Name = "checks")]
            public RegressionCheckResultExport[] Checks { get; set; }

            [DataMember(Name = "classification")]
            public string Classification { get; set; }

            public static RegressionPackagingReportExport FromReport(RegressionPackagingReport report)
            {
                report = report ?? new RegressionPackagingReport(
                    string.Empty,
                    string.Empty,
                    new string[0],
                    new string[0],
                    new string[0],
                    new string[0],
                    new RegressionCheckResult[0],
                    string.Empty);

                return new RegressionPackagingReportExport
                {
                    OutputDirectory = report.OutputDirectory ?? string.Empty,
                    ArtifactPath = report.ArtifactPath ?? string.Empty,
                    ExpectedRuntimeFiles = report.ExpectedRuntimeFiles ?? new string[0],
                    PresentRuntimeFiles = report.PresentRuntimeFiles ?? new string[0],
                    MissingRuntimeFiles = report.MissingRuntimeFiles ?? new string[0],
                    StaleRuntimeFiles = report.StaleRuntimeFiles ?? new string[0],
                    Checks = (report.Checks ?? new RegressionCheckResult[0])
                        .Select(RegressionCheckResultExport.FromReport)
                        .ToArray(),
                    Classification = report.Classification ?? string.Empty
                };
            }
        }

        [DataContract]
        private sealed class RegressionFileReportExport
        {
            [DataMember(Name = "filePath")]
            public string FilePath { get; set; }

            [DataMember(Name = "fileName")]
            public string FileName { get; set; }

            [DataMember(Name = "mediaProfile")]
            public RegressionMediaProfileExport MediaProfile { get; set; }

            [DataMember(Name = "decodeProfile")]
            public RegressionDecodeProfileExport DecodeProfile { get; set; }

            [DataMember(Name = "engineChecks")]
            public RegressionCheckResultExport[] EngineChecks { get; set; }

            [DataMember(Name = "uiChecks")]
            public RegressionCheckResultExport[] UiChecks { get; set; }

            [DataMember(Name = "engineMetrics")]
            public RegressionMetricsExport EngineMetrics { get; set; }

            [DataMember(Name = "uiMetrics")]
            public RegressionMetricsExport UiMetrics { get; set; }

            [DataMember(Name = "notes")]
            public string[] Notes { get; set; }

            public static RegressionFileReportExport FromReport(RegressionFileReport report)
            {
                report = report ?? new RegressionFileReport(
                    string.Empty,
                    string.Empty,
                    null,
                    null,
                    new RegressionCheckResult[0],
                    new RegressionCheckResult[0],
                    new RegressionMetrics(),
                    new RegressionMetrics(),
                    new string[0]);

                return new RegressionFileReportExport
                {
                    FilePath = report.FilePath ?? string.Empty,
                    FileName = report.FileName ?? string.Empty,
                    MediaProfile = RegressionMediaProfileExport.FromReport(report.MediaProfile),
                    DecodeProfile = RegressionDecodeProfileExport.FromReport(report.DecodeProfile),
                    EngineChecks = (report.EngineChecks ?? new RegressionCheckResult[0])
                        .Select(RegressionCheckResultExport.FromReport)
                        .ToArray(),
                    UiChecks = (report.UiChecks ?? new RegressionCheckResult[0])
                        .Select(RegressionCheckResultExport.FromReport)
                        .ToArray(),
                    EngineMetrics = RegressionMetricsExport.FromMetrics(report.EngineMetrics),
                    UiMetrics = RegressionMetricsExport.FromMetrics(report.UiMetrics),
                    Notes = report.Notes ?? new string[0]
                };
            }
        }

        [DataContract]
        private sealed class RegressionDecodeProfileExport
        {
            [DataMember(Name = "activeDecodeBackend")]
            public string ActiveDecodeBackend { get; set; }

            [DataMember(Name = "actualBackendUsed")]
            public string ActualBackendUsed { get; set; }

            [DataMember(Name = "isGpuActive")]
            public bool IsGpuActive { get; set; }

            [DataMember(Name = "gpuCapabilityStatus")]
            public string GpuCapabilityStatus { get; set; }

            [DataMember(Name = "gpuFallbackReason")]
            public string GpuFallbackReason { get; set; }

            [DataMember(Name = "operationalQueueDepth")]
            public int OperationalQueueDepth { get; set; }

            [DataMember(Name = "sessionDecodedFrameCacheBudgetBytes")]
            public long SessionDecodedFrameCacheBudgetBytes { get; set; }

            [DataMember(Name = "decodedFrameCacheBudgetBytes")]
            public long DecodedFrameCacheBudgetBytes { get; set; }

            [DataMember(Name = "budgetBand")]
            public string BudgetBand { get; set; }

            [DataMember(Name = "hostResourceClass")]
            public string HostResourceClass { get; set; }

            [DataMember(Name = "configuredPreviousCachedFrames")]
            public int ConfiguredPreviousCachedFrames { get; set; }

            [DataMember(Name = "configuredForwardCachedFrames")]
            public int ConfiguredForwardCachedFrames { get; set; }

            [DataMember(Name = "observedPreviousCachedFrames")]
            public int ObservedPreviousCachedFrames { get; set; }

            [DataMember(Name = "observedForwardCachedFrames")]
            public int ObservedForwardCachedFrames { get; set; }

            [DataMember(Name = "observedApproximateCacheBytes")]
            public long ObservedApproximateCacheBytes { get; set; }

            [DataMember(Name = "backwardStepCacheHits")]
            public int BackwardStepCacheHits { get; set; }

            [DataMember(Name = "backwardStepReconstructionCount")]
            public int BackwardStepReconstructionCount { get; set; }

            [DataMember(Name = "forwardStepCacheHits")]
            public int ForwardStepCacheHits { get; set; }

            [DataMember(Name = "forwardStepReconstructionCount")]
            public int ForwardStepReconstructionCount { get; set; }

            [DataMember(Name = "forwardStepCacheHitRate")]
            public double ForwardStepCacheHitRate { get; set; }

            [DataMember(Name = "hardwareFrameTransferMilliseconds")]
            public double HardwareFrameTransferMilliseconds { get; set; }

            [DataMember(Name = "bgraConversionMilliseconds")]
            public double BgraConversionMilliseconds { get; set; }

            [DataMember(Name = "globalFrameIndexStatus")]
            public string GlobalFrameIndexStatus { get; set; }

            [DataMember(Name = "isGlobalFrameIndexAvailable")]
            public bool IsGlobalFrameIndexAvailable { get; set; }

            [DataMember(Name = "lastObservedCacheRefillReason")]
            public string LastObservedCacheRefillReason { get; set; }

            [DataMember(Name = "lastObservedCacheRefillMode")]
            public string LastObservedCacheRefillMode { get; set; }

            [DataMember(Name = "lastObservedCacheRefillMilliseconds")]
            public double LastObservedCacheRefillMilliseconds { get; set; }

            public static RegressionDecodeProfileExport FromReport(RegressionDecodeProfile report)
            {
                report = report ?? RegressionDecodeProfile.Empty;
                return new RegressionDecodeProfileExport
                {
                    ActiveDecodeBackend = report.ActiveDecodeBackend ?? string.Empty,
                    ActualBackendUsed = report.ActualBackendUsed ?? string.Empty,
                    IsGpuActive = report.IsGpuActive,
                    GpuCapabilityStatus = report.GpuCapabilityStatus ?? string.Empty,
                    GpuFallbackReason = report.GpuFallbackReason ?? string.Empty,
                    OperationalQueueDepth = report.OperationalQueueDepth,
                    SessionDecodedFrameCacheBudgetBytes = report.SessionDecodedFrameCacheBudgetBytes,
                    DecodedFrameCacheBudgetBytes = report.DecodedFrameCacheBudgetBytes,
                    BudgetBand = report.BudgetBand ?? string.Empty,
                    HostResourceClass = report.HostResourceClass ?? string.Empty,
                    ConfiguredPreviousCachedFrames = report.ConfiguredPreviousCachedFrames,
                    ConfiguredForwardCachedFrames = report.ConfiguredForwardCachedFrames,
                    ObservedPreviousCachedFrames = report.ObservedPreviousCachedFrames,
                    ObservedForwardCachedFrames = report.ObservedForwardCachedFrames,
                    ObservedApproximateCacheBytes = report.ObservedApproximateCacheBytes,
                    BackwardStepCacheHits = report.BackwardStepCacheHits,
                    BackwardStepReconstructionCount = report.BackwardStepReconstructionCount,
                    ForwardStepCacheHits = report.ForwardStepCacheHits,
                    ForwardStepReconstructionCount = report.ForwardStepReconstructionCount,
                    ForwardStepCacheHitRate = report.ForwardStepCacheHitRate,
                    HardwareFrameTransferMilliseconds = report.HardwareFrameTransferMilliseconds,
                    BgraConversionMilliseconds = report.BgraConversionMilliseconds,
                    GlobalFrameIndexStatus = report.GlobalFrameIndexStatus ?? string.Empty,
                    IsGlobalFrameIndexAvailable = report.IsGlobalFrameIndexAvailable,
                    LastObservedCacheRefillReason = report.LastObservedCacheRefillReason ?? string.Empty,
                    LastObservedCacheRefillMode = report.LastObservedCacheRefillMode ?? string.Empty,
                    LastObservedCacheRefillMilliseconds = report.LastObservedCacheRefillMilliseconds
                };
            }
        }

        [DataContract]
        private sealed class RegressionMediaProfileExport
        {
            [DataMember(Name = "videoCodecName")]
            public string VideoCodecName { get; set; }

            [DataMember(Name = "pixelWidth")]
            public int PixelWidth { get; set; }

            [DataMember(Name = "pixelHeight")]
            public int PixelHeight { get; set; }

            [DataMember(Name = "duration")]
            public string Duration { get; set; }

            [DataMember(Name = "framesPerSecond")]
            public double FramesPerSecond { get; set; }

            [DataMember(Name = "hasAudioStream")]
            public bool HasAudioStream { get; set; }

            [DataMember(Name = "isAudioPlaybackAvailable")]
            public bool IsAudioPlaybackAvailable { get; set; }

            [DataMember(Name = "audioCodecName")]
            public string AudioCodecName { get; set; }

            [DataMember(Name = "audioSampleRate")]
            public int AudioSampleRate { get; set; }

            [DataMember(Name = "audioChannelCount")]
            public int AudioChannelCount { get; set; }

            public static RegressionMediaProfileExport FromReport(RegressionMediaProfile report)
            {
                report = report ?? new RegressionMediaProfile(string.Empty, 0, 0, string.Empty, 0d, false, false, string.Empty, 0, 0);
                return new RegressionMediaProfileExport
                {
                    VideoCodecName = report.VideoCodecName ?? string.Empty,
                    PixelWidth = report.PixelWidth,
                    PixelHeight = report.PixelHeight,
                    Duration = report.Duration ?? string.Empty,
                    FramesPerSecond = report.FramesPerSecond,
                    HasAudioStream = report.HasAudioStream,
                    IsAudioPlaybackAvailable = report.IsAudioPlaybackAvailable,
                    AudioCodecName = report.AudioCodecName ?? string.Empty,
                    AudioSampleRate = report.AudioSampleRate,
                    AudioChannelCount = report.AudioChannelCount
                };
            }
        }

        [DataContract]
        private sealed class RegressionMetricsExport
        {
            [DataMember(Name = "openMilliseconds")]
            public double OpenMilliseconds { get; set; }

            [DataMember(Name = "preIndexSeekMilliseconds")]
            public double PreIndexSeekMilliseconds { get; set; }

            [DataMember(Name = "indexReadyMilliseconds")]
            public double IndexReadyMilliseconds { get; set; }

            [DataMember(Name = "indexedSeekMilliseconds")]
            public double IndexedSeekMilliseconds { get; set; }

            [DataMember(Name = "playbackMilliseconds")]
            public double PlaybackMilliseconds { get; set; }

            [DataMember(Name = "reopenMilliseconds")]
            public double ReopenMilliseconds { get; set; }

            [DataMember(Name = "uiOpenMilliseconds")]
            public double UiOpenMilliseconds { get; set; }

            [DataMember(Name = "uiPreIndexClickMilliseconds")]
            public double UiPreIndexClickMilliseconds { get; set; }

            [DataMember(Name = "uiClickSeekMilliseconds")]
            public double UiClickSeekMilliseconds { get; set; }

            [DataMember(Name = "uiDragSeekMilliseconds")]
            public double UiDragSeekMilliseconds { get; set; }

            [DataMember(Name = "uiEndSeekMilliseconds")]
            public double UiEndSeekMilliseconds { get; set; }

            [DataMember(Name = "uiIndexReadyMilliseconds")]
            public double UiIndexReadyMilliseconds { get; set; }

            [DataMember(Name = "maxObservedPreviousCachedFrames")]
            public int MaxObservedPreviousCachedFrames { get; set; }

            [DataMember(Name = "maxObservedForwardCachedFrames")]
            public int MaxObservedForwardCachedFrames { get; set; }

            [DataMember(Name = "maxObservedApproximateCacheBytes")]
            public long MaxObservedApproximateCacheBytes { get; set; }

            [DataMember(Name = "backwardStepCacheHits")]
            public int BackwardStepCacheHits { get; set; }

            [DataMember(Name = "backwardStepReconstructionCount")]
            public int BackwardStepReconstructionCount { get; set; }

            [DataMember(Name = "forwardStepCacheHits")]
            public int ForwardStepCacheHits { get; set; }

            [DataMember(Name = "forwardStepReconstructionCount")]
            public int ForwardStepReconstructionCount { get; set; }

            [DataMember(Name = "lastObservedCacheRefillMilliseconds")]
            public double LastObservedCacheRefillMilliseconds { get; set; }

            [DataMember(Name = "lastObservedCacheRefillReason")]
            public string LastObservedCacheRefillReason { get; set; }

            [DataMember(Name = "lastObservedCacheRefillMode")]
            public string LastObservedCacheRefillMode { get; set; }

            public static RegressionMetricsExport FromMetrics(RegressionMetrics metrics)
            {
                metrics = metrics ?? new RegressionMetrics();
                return new RegressionMetricsExport
                {
                    OpenMilliseconds = metrics.OpenMilliseconds,
                    PreIndexSeekMilliseconds = metrics.PreIndexSeekMilliseconds,
                    IndexReadyMilliseconds = metrics.IndexReadyMilliseconds,
                    IndexedSeekMilliseconds = metrics.IndexedSeekMilliseconds,
                    PlaybackMilliseconds = metrics.PlaybackMilliseconds,
                    ReopenMilliseconds = metrics.ReopenMilliseconds,
                    UiOpenMilliseconds = metrics.UiOpenMilliseconds,
                    UiPreIndexClickMilliseconds = metrics.UiPreIndexClickMilliseconds,
                    UiClickSeekMilliseconds = metrics.UiClickSeekMilliseconds,
                    UiDragSeekMilliseconds = metrics.UiDragSeekMilliseconds,
                    UiEndSeekMilliseconds = metrics.UiEndSeekMilliseconds,
                    UiIndexReadyMilliseconds = metrics.UiIndexReadyMilliseconds,
                    MaxObservedPreviousCachedFrames = metrics.MaxObservedPreviousCachedFrames,
                    MaxObservedForwardCachedFrames = metrics.MaxObservedForwardCachedFrames,
                    MaxObservedApproximateCacheBytes = metrics.MaxObservedApproximateCacheBytes,
                    BackwardStepCacheHits = metrics.BackwardStepCacheHits,
                    BackwardStepReconstructionCount = metrics.BackwardStepReconstructionCount,
                    ForwardStepCacheHits = metrics.ForwardStepCacheHits,
                    ForwardStepReconstructionCount = metrics.ForwardStepReconstructionCount,
                    LastObservedCacheRefillMilliseconds = metrics.LastObservedCacheRefillMilliseconds,
                    LastObservedCacheRefillReason = metrics.LastObservedCacheRefillReason ?? string.Empty,
                    LastObservedCacheRefillMode = metrics.LastObservedCacheRefillMode ?? string.Empty
                };
            }
        }

        [DataContract]
        private sealed class RegressionCheckResultExport
        {
            [DataMember(Name = "filePath")]
            public string FilePath { get; set; }

            [DataMember(Name = "scope")]
            public string Scope { get; set; }

            [DataMember(Name = "category")]
            public string Category { get; set; }

            [DataMember(Name = "name")]
            public string Name { get; set; }

            [DataMember(Name = "classification")]
            public string Classification { get; set; }

            [DataMember(Name = "message")]
            public string Message { get; set; }

            [DataMember(Name = "expectedFrameIndex")]
            public long? ExpectedFrameIndex { get; set; }

            [DataMember(Name = "actualFrameIndex")]
            public long? ActualFrameIndex { get; set; }

            [DataMember(Name = "expectedDisplayedFrame")]
            public long? ExpectedDisplayedFrame { get; set; }

            [DataMember(Name = "actualDisplayedFrame")]
            public long? ActualDisplayedFrame { get; set; }

            [DataMember(Name = "requestedTime")]
            public string RequestedTime { get; set; }

            [DataMember(Name = "actualTime")]
            public string ActualTime { get; set; }

            [DataMember(Name = "sliderValueSeconds")]
            public double? SliderValueSeconds { get; set; }

            [DataMember(Name = "sliderMaximumSeconds")]
            public double? SliderMaximumSeconds { get; set; }

            [DataMember(Name = "elapsedMilliseconds")]
            public double? ElapsedMilliseconds { get; set; }

            [DataMember(Name = "indexReady")]
            public bool? IndexReady { get; set; }

            [DataMember(Name = "usedGlobalIndex")]
            public bool? UsedGlobalIndex { get; set; }

            [DataMember(Name = "cacheHit")]
            public bool? CacheHit { get; set; }

            [DataMember(Name = "requiredReconstruction")]
            public bool? RequiredReconstruction { get; set; }

            public static RegressionCheckResultExport FromReport(RegressionCheckResult report)
            {
                report = report ?? new RegressionCheckResult(
                    string.Empty,
                    string.Empty,
                    string.Empty,
                    string.Empty,
                    string.Empty,
                    string.Empty,
                    null,
                    null,
                    null,
                    null,
                    string.Empty,
                    string.Empty,
                    null,
                    null,
                    null,
                    null,
                    null,
                    null,
                    null);

                return new RegressionCheckResultExport
                {
                    FilePath = report.FilePath ?? string.Empty,
                    Scope = report.Scope ?? string.Empty,
                    Category = report.Category ?? string.Empty,
                    Name = report.Name ?? string.Empty,
                    Classification = report.Classification ?? string.Empty,
                    Message = report.Message ?? string.Empty,
                    ExpectedFrameIndex = report.ExpectedFrameIndex,
                    ActualFrameIndex = report.ActualFrameIndex,
                    ExpectedDisplayedFrame = report.ExpectedDisplayedFrame,
                    ActualDisplayedFrame = report.ActualDisplayedFrame,
                    RequestedTime = report.RequestedTime ?? string.Empty,
                    ActualTime = report.ActualTime ?? string.Empty,
                    SliderValueSeconds = report.SliderValueSeconds,
                    SliderMaximumSeconds = report.SliderMaximumSeconds,
                    ElapsedMilliseconds = report.ElapsedMilliseconds,
                    IndexReady = report.IndexReady,
                    UsedGlobalIndex = report.UsedGlobalIndex,
                    CacheHit = report.CacheHit,
                    RequiredReconstruction = report.RequiredReconstruction
                };
            }
        }

        [DataContract]
        private sealed class RegressionSummaryExport
        {
            [DataMember(Name = "filesTested")]
            public int FilesTested { get; set; }

            [DataMember(Name = "checksRun")]
            public int ChecksRun { get; set; }

            [DataMember(Name = "passCount")]
            public int PassCount { get; set; }

            [DataMember(Name = "warningCount")]
            public int WarningCount { get; set; }

            [DataMember(Name = "failCount")]
            public int FailCount { get; set; }

            public static RegressionSummaryExport FromReport(RegressionSummary summary)
            {
                summary = summary ?? new RegressionSummary(0, 0, 0, 0, 0);
                return new RegressionSummaryExport
                {
                    FilesTested = summary.FilesTested,
                    ChecksRun = summary.ChecksRun,
                    PassCount = summary.PassCount,
                    WarningCount = summary.WarningCount,
                    FailCount = summary.FailCount
                };
            }
        }
    }
}
