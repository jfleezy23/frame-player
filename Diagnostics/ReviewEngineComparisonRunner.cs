using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using FFmpeg.AutoGen;
using FramePlayer.Core.Abstractions;
using FramePlayer.Core.Models;
using FramePlayer.Engines.FFmpeg;
using FramePlayer.Services;

namespace FramePlayer.Diagnostics
{
    public static class ReviewEngineComparisonRunner
    {
        public static async Task<ReviewEngineComparisonReport> RunAsync(
            string filePath,
            TimeSpan seekTime,
            long seekFrameIndex,
            CancellationToken cancellationToken = default(CancellationToken))
        {
            if (string.IsNullOrWhiteSpace(filePath))
            {
                throw new ArgumentException("A media file path is required.", nameof(filePath));
            }

            if (!File.Exists(filePath))
            {
                throw new FileNotFoundException("The requested media file was not found.", filePath);
            }

            EnsureRuntimePathsConfigured();

            var clampedFrameIndex = Math.Max(0L, seekFrameIndex);
            var customFfmpeg = await RunCustomScenarioAsync(filePath, seekTime, clampedFrameIndex, cancellationToken)
                .ConfigureAwait(false);

            return new ReviewEngineComparisonReport(
                filePath,
                seekTime,
                clampedFrameIndex,
                customFfmpeg);
        }

        private static Task<ReviewEngineScenarioReport> RunCustomScenarioAsync(
            string filePath,
            TimeSpan seekTime,
            long seekFrameIndex,
            CancellationToken cancellationToken)
        {
            return RunScenarioAsync(
                "custom-ffmpeg",
                CreateFfmpegReviewEngine(),
                filePath,
                seekTime,
                seekFrameIndex,
                cancellationToken);
        }

        private static async Task<ReviewEngineScenarioReport> RunScenarioAsync(
            string backendName,
            IVideoReviewEngine engine,
            string filePath,
            TimeSpan seekTime,
            long seekFrameIndex,
            CancellationToken cancellationToken)
        {
            ReviewOperationSnapshot openResult = null;
            ReviewOperationSnapshot playbackResult = null;
            ReviewOperationSnapshot seekToTimeResult = null;
            ReviewOperationSnapshot seekToFrameResult = null;
            ReviewStepOperationSnapshot backwardStepResult = null;
            ReviewStepOperationSnapshot forwardStepResult = null;
            string scenarioError = string.Empty;

            try
            {
                try
                {
                    var openElapsed = await MeasureAsync(
                            async () => await engine.OpenAsync(filePath, cancellationToken).ConfigureAwait(true))
                        .ConfigureAwait(true);
                    openResult = CreateSnapshot("open", engine, BuildOpenNote(engine), openElapsed);
                }
                catch (Exception ex)
                {
                    openResult = CreateFailedSnapshot("open", ex, engine, TimeSpan.Zero);
                    scenarioError = ex.Message;
                    return new ReviewEngineScenarioReport(
                        backendName,
                        openResult,
                        playbackResult,
                        seekToTimeResult,
                        seekToFrameResult,
                        backwardStepResult,
                        forwardStepResult,
                        scenarioError);
                }

                try
                {
                    var playbackElapsed = await MeasureAsync(
                            async () =>
                            {
                                await engine.PlayAsync().ConfigureAwait(true);
                                await Task.Delay(TimeSpan.FromMilliseconds(500d), cancellationToken).ConfigureAwait(true);
                                await engine.PauseAsync().ConfigureAwait(true);
                            })
                        .ConfigureAwait(true);
                    playbackResult = CreateSnapshot("playback", engine, BuildPlaybackNote(engine), playbackElapsed);
                }
                catch (Exception ex)
                {
                    playbackResult = CreateFailedSnapshot("playback", ex, engine, TimeSpan.Zero);
                    scenarioError = ex.Message;
                    return new ReviewEngineScenarioReport(
                        backendName,
                        openResult,
                        playbackResult,
                        seekToTimeResult,
                        seekToFrameResult,
                        backwardStepResult,
                        forwardStepResult,
                        scenarioError);
                }

                try
                {
                    var seekToTimeElapsed = await MeasureAsync(
                            async () => await engine.SeekToTimeAsync(seekTime, cancellationToken).ConfigureAwait(true))
                        .ConfigureAwait(true);
                    seekToTimeResult = CreateSnapshot(
                        "seek-to-time",
                        engine,
                        BuildSeekToTimeNote(engine),
                        seekToTimeElapsed);
                }
                catch (Exception ex)
                {
                    seekToTimeResult = CreateFailedSnapshot("seek-to-time", ex, engine, TimeSpan.Zero);
                    scenarioError = ex.Message;
                    return new ReviewEngineScenarioReport(
                        backendName,
                        openResult,
                        playbackResult,
                        seekToTimeResult,
                        seekToFrameResult,
                        backwardStepResult,
                        forwardStepResult,
                        scenarioError);
                }

                try
                {
                    var seekToFrameElapsed = await MeasureAsync(
                            async () => await engine.SeekToFrameAsync(seekFrameIndex, cancellationToken).ConfigureAwait(true))
                        .ConfigureAwait(true);
                    seekToFrameResult = CreateSnapshot(
                        "seek-to-frame",
                        engine,
                        BuildSeekToFrameNote(engine),
                        seekToFrameElapsed);
                }
                catch (Exception ex)
                {
                    seekToFrameResult = CreateFailedSnapshot("seek-to-frame", ex, engine, TimeSpan.Zero);
                    scenarioError = ex.Message;
                    return new ReviewEngineScenarioReport(
                        backendName,
                        openResult,
                        playbackResult,
                        seekToTimeResult,
                        seekToFrameResult,
                        backwardStepResult,
                        forwardStepResult,
                        scenarioError);
                }

                try
                {
                    var backwardStepOperation = await MeasureAsync(
                            async () => await engine.StepBackwardAsync(cancellationToken).ConfigureAwait(true))
                        .ConfigureAwait(true);
                    backwardStepResult = CreateStepSnapshot(
                        "step-backward",
                        engine,
                        backwardStepOperation.Result,
                        backwardStepOperation.Elapsed);
                }
                catch (Exception ex)
                {
                    backwardStepResult = CreateFailedStepSnapshot("step-backward", engine, ex, TimeSpan.Zero);
                    scenarioError = ex.Message;
                    return new ReviewEngineScenarioReport(
                        backendName,
                        openResult,
                        playbackResult,
                        seekToTimeResult,
                        seekToFrameResult,
                        backwardStepResult,
                        forwardStepResult,
                        scenarioError);
                }

                try
                {
                    var forwardStepOperation = await MeasureAsync(
                            async () => await engine.StepForwardAsync(cancellationToken).ConfigureAwait(true))
                        .ConfigureAwait(true);
                    forwardStepResult = CreateStepSnapshot(
                        "step-forward",
                        engine,
                        forwardStepOperation.Result,
                        forwardStepOperation.Elapsed);
                }
                catch (Exception ex)
                {
                    forwardStepResult = CreateFailedStepSnapshot("step-forward", engine, ex, TimeSpan.Zero);
                    scenarioError = ex.Message;
                }

                return new ReviewEngineScenarioReport(
                    backendName,
                    openResult,
                    playbackResult,
                    seekToTimeResult,
                    seekToFrameResult,
                    backwardStepResult,
                    forwardStepResult,
                    scenarioError);
            }
            finally
            {
                engine.Dispose();
            }
        }

        private static ReviewOperationSnapshot CreateSnapshot(
            string operationName,
            IVideoReviewEngine engine,
            string note,
            TimeSpan elapsed)
        {
            var diagnostics = CaptureDiagnostics(engine);
            return new ReviewOperationSnapshot(
                operationName,
                true,
                string.Empty,
                engine.IsMediaOpen,
                engine.Position,
                engine.MediaInfo,
                note,
                elapsed.TotalMilliseconds,
                diagnostics.IsGlobalFrameIndexAvailable,
                diagnostics.IndexedFrameCount,
                diagnostics.UsedGlobalIndex,
                diagnostics.AnchorStrategy,
                diagnostics.AnchorFrameIndex,
                diagnostics.ActiveDecodeBackend,
                diagnostics.IsGpuActive,
                diagnostics.GpuCapabilityStatus,
                diagnostics.GpuFallbackReason,
                diagnostics.OperationalQueueDepth,
                diagnostics.SessionDecodedFrameCacheBudgetBytes,
                diagnostics.DecodedFrameCacheBudgetBytes,
                diagnostics.BudgetBand,
                diagnostics.HostResourceClass,
                diagnostics.ActualBackendUsed,
                diagnostics.PreviousCachedFrameCount,
                diagnostics.ForwardCachedFrameCount,
                diagnostics.MaxPreviousCachedFrameCount,
                diagnostics.MaxForwardCachedFrameCount,
                diagnostics.ApproximateCachedFrameBytes,
                diagnostics.HardwareFrameTransferMilliseconds,
                diagnostics.BgraConversionMilliseconds,
                diagnostics.HasAudioStream,
                diagnostics.AudioPlaybackAvailable,
                diagnostics.AudioPlaybackActive,
                diagnostics.LastPlaybackUsedAudioClock,
                diagnostics.LastAudioSubmittedBytes,
                diagnostics.AudioCodecName,
                diagnostics.AudioErrorMessage);
        }

        private static ReviewOperationSnapshot CreateFailedSnapshot(
            string operationName,
            Exception exception,
            IVideoReviewEngine engine,
            TimeSpan elapsed)
        {
            var diagnostics = CaptureDiagnostics(engine);
            return new ReviewOperationSnapshot(
                operationName,
                false,
                exception?.Message ?? "The operation failed.",
                engine != null && engine.IsMediaOpen,
                engine != null ? engine.Position : ReviewPosition.Empty,
                engine != null ? engine.MediaInfo : VideoMediaInfo.Empty,
                string.Empty,
                elapsed.TotalMilliseconds,
                diagnostics.IsGlobalFrameIndexAvailable,
                diagnostics.IndexedFrameCount,
                diagnostics.UsedGlobalIndex,
                diagnostics.AnchorStrategy,
                diagnostics.AnchorFrameIndex,
                diagnostics.ActiveDecodeBackend,
                diagnostics.IsGpuActive,
                diagnostics.GpuCapabilityStatus,
                diagnostics.GpuFallbackReason,
                diagnostics.OperationalQueueDepth,
                diagnostics.SessionDecodedFrameCacheBudgetBytes,
                diagnostics.DecodedFrameCacheBudgetBytes,
                diagnostics.BudgetBand,
                diagnostics.HostResourceClass,
                diagnostics.ActualBackendUsed,
                diagnostics.PreviousCachedFrameCount,
                diagnostics.ForwardCachedFrameCount,
                diagnostics.MaxPreviousCachedFrameCount,
                diagnostics.MaxForwardCachedFrameCount,
                diagnostics.ApproximateCachedFrameBytes,
                diagnostics.HardwareFrameTransferMilliseconds,
                diagnostics.BgraConversionMilliseconds,
                diagnostics.HasAudioStream,
                diagnostics.AudioPlaybackAvailable,
                diagnostics.AudioPlaybackActive,
                diagnostics.LastPlaybackUsedAudioClock,
                diagnostics.LastAudioSubmittedBytes,
                diagnostics.AudioCodecName,
                diagnostics.AudioErrorMessage);
        }

        private static ReviewStepOperationSnapshot CreateStepSnapshot(
            string operationName,
            IVideoReviewEngine engine,
            FrameStepResult result,
            TimeSpan elapsed)
        {
            var diagnostics = CaptureDiagnostics(engine);
            return new ReviewStepOperationSnapshot(
                operationName,
                result ?? FrameStepResult.Failed(0, ReviewPosition.Empty, "No step result was returned."),
                engine.IsMediaOpen,
                engine.Position,
                engine.MediaInfo,
                elapsed.TotalMilliseconds,
                diagnostics.IsGlobalFrameIndexAvailable,
                diagnostics.IndexedFrameCount,
                diagnostics.UsedGlobalIndex,
                diagnostics.AnchorStrategy,
                diagnostics.AnchorFrameIndex,
                diagnostics.ActiveDecodeBackend,
                diagnostics.IsGpuActive,
                diagnostics.GpuCapabilityStatus,
                diagnostics.GpuFallbackReason,
                diagnostics.OperationalQueueDepth,
                diagnostics.SessionDecodedFrameCacheBudgetBytes,
                diagnostics.DecodedFrameCacheBudgetBytes,
                diagnostics.BudgetBand,
                diagnostics.HostResourceClass,
                diagnostics.ActualBackendUsed,
                diagnostics.PreviousCachedFrameCount,
                diagnostics.ForwardCachedFrameCount,
                diagnostics.MaxPreviousCachedFrameCount,
                diagnostics.MaxForwardCachedFrameCount,
                diagnostics.ApproximateCachedFrameBytes,
                diagnostics.HardwareFrameTransferMilliseconds,
                diagnostics.BgraConversionMilliseconds,
                diagnostics.HasAudioStream,
                diagnostics.AudioPlaybackAvailable,
                diagnostics.AudioPlaybackActive,
                diagnostics.LastPlaybackUsedAudioClock,
                diagnostics.LastAudioSubmittedBytes,
                diagnostics.AudioCodecName,
                diagnostics.AudioErrorMessage);
        }

        private static ReviewStepOperationSnapshot CreateFailedStepSnapshot(
            string operationName,
            IVideoReviewEngine engine,
            Exception exception,
            TimeSpan elapsed)
        {
            var diagnostics = CaptureDiagnostics(engine);
            return new ReviewStepOperationSnapshot(
                operationName,
                FrameStepResult.Failed(0, engine.Position, exception?.Message ?? "The step failed."),
                engine.IsMediaOpen,
                engine.Position,
                engine.MediaInfo,
                elapsed.TotalMilliseconds,
                diagnostics.IsGlobalFrameIndexAvailable,
                diagnostics.IndexedFrameCount,
                diagnostics.UsedGlobalIndex,
                diagnostics.AnchorStrategy,
                diagnostics.AnchorFrameIndex,
                diagnostics.ActiveDecodeBackend,
                diagnostics.IsGpuActive,
                diagnostics.GpuCapabilityStatus,
                diagnostics.GpuFallbackReason,
                diagnostics.OperationalQueueDepth,
                diagnostics.SessionDecodedFrameCacheBudgetBytes,
                diagnostics.DecodedFrameCacheBudgetBytes,
                diagnostics.BudgetBand,
                diagnostics.HostResourceClass,
                diagnostics.ActualBackendUsed,
                diagnostics.PreviousCachedFrameCount,
                diagnostics.ForwardCachedFrameCount,
                diagnostics.MaxPreviousCachedFrameCount,
                diagnostics.MaxForwardCachedFrameCount,
                diagnostics.ApproximateCachedFrameBytes,
                diagnostics.HardwareFrameTransferMilliseconds,
                diagnostics.BgraConversionMilliseconds,
                diagnostics.HasAudioStream,
                diagnostics.AudioPlaybackAvailable,
                diagnostics.AudioPlaybackActive,
                diagnostics.LastPlaybackUsedAudioClock,
                diagnostics.LastAudioSubmittedBytes,
                diagnostics.AudioCodecName,
                diagnostics.AudioErrorMessage);
        }

        private static string BuildSeekToTimeNote(IVideoReviewEngine engine)
        {
            var ffmpegEngine = engine as FfmpegReviewEngine;
            if (ffmpegEngine == null)
            {
                return string.Empty;
            }

            return string.Format(
                System.Globalization.CultureInfo.InvariantCulture,
                "Custom FFmpeg seek {0}. Global index used: {1}. Anchor: {2}{3}. Absolute frame identity: {4}.",
                ffmpegEngine.LastSeekLandedAtOrAfterTarget
                    ? "landed on or after the requested stream timestamp"
                    : "fell back to the closest available decoded frame near end of stream",
                ffmpegEngine.LastOperationUsedGlobalIndex ? "yes" : "no",
                string.IsNullOrWhiteSpace(ffmpegEngine.LastAnchorStrategy) ? "(none)" : ffmpegEngine.LastAnchorStrategy,
                ffmpegEngine.LastAnchorFrameIndex.HasValue
                    ? " @ frame " + ffmpegEngine.LastAnchorFrameIndex.Value.ToString(System.Globalization.CultureInfo.InvariantCulture)
                    : string.Empty,
                engine.Position.IsFrameIndexAbsolute ? "yes" : "no");
        }

        private static string BuildPlaybackNote(IVideoReviewEngine engine)
        {
            var ffmpegEngine = engine as FfmpegReviewEngine;
            if (ffmpegEngine == null)
            {
                return string.Empty;
            }

            if (!ffmpegEngine.HasAudioStream)
            {
                return "Custom FFmpeg playback ran video-only because no audio stream was present.";
            }

            return string.Format(
                System.Globalization.CultureInfo.InvariantCulture,
                "Custom FFmpeg playback ran {0}. Audio codec: {1}. Audio bytes submitted: {2}. Audio clock used: {3}.{4}",
                ffmpegEngine.LastAudioSubmittedBytes > 0L ? "audio+video" : "video-only",
                string.IsNullOrWhiteSpace(ffmpegEngine.AudioStreamInfo.CodecName) ? "(unknown)" : ffmpegEngine.AudioStreamInfo.CodecName,
                ffmpegEngine.LastAudioSubmittedBytes,
                ffmpegEngine.LastPlaybackUsedAudioClock ? "yes" : "no",
                string.IsNullOrWhiteSpace(ffmpegEngine.LastAudioErrorMessage)
                    ? string.Empty
                    : " Audio fallback reason: " + ffmpegEngine.LastAudioErrorMessage);
        }

        private static string BuildSeekToFrameNote(IVideoReviewEngine engine)
        {
            var ffmpegEngine = engine as FfmpegReviewEngine;
            if (ffmpegEngine == null)
            {
                return string.Empty;
            }

            return string.Format(
                System.Globalization.CultureInfo.InvariantCulture,
                "Custom FFmpeg frame seek {0}. Global index used: {1}. Anchor: {2}{3}. Absolute frame identity: {4}.",
                ffmpegEngine.LastFrameSeekWasCacheHit
                    ? "was satisfied from the decoded absolute-frame cache"
                    : "decoded the exact target frame from a global-index anchor",
                ffmpegEngine.LastOperationUsedGlobalIndex ? "yes" : "no",
                string.IsNullOrWhiteSpace(ffmpegEngine.LastAnchorStrategy) ? "(none)" : ffmpegEngine.LastAnchorStrategy,
                ffmpegEngine.LastAnchorFrameIndex.HasValue
                    ? " @ frame " + ffmpegEngine.LastAnchorFrameIndex.Value.ToString(System.Globalization.CultureInfo.InvariantCulture)
                    : string.Empty,
                engine.Position.IsFrameIndexAbsolute ? "yes" : "no");
        }

        private static string BuildOpenNote(IVideoReviewEngine engine)
        {
            var ffmpegEngine = engine as FfmpegReviewEngine;
            if (ffmpegEngine == null)
            {
                return string.Empty;
            }

            return string.Format(
                System.Globalization.CultureInfo.InvariantCulture,
                "Custom FFmpeg open made the first frame available while the file-global frame index status is '{0}' with {1} indexed frames. Absolute frame identity available: {2}. Open timings: total {3:0.0} ms, container/probe {4:0.0} ms, stream {5:0.0} ms, audio probe {6:0.0} ms, first frame {7:0.0} ms, cache warm {8:0.0} ms, index build {9:0.0} ms. Decode backend: {10}. GPU active: {11}. GPU status: {12}. Fallback: {13}. Budget band: {14}. Host class: {15}. Session budget: {16:0.0} MiB. Pane budget: {17:0.0} MiB. Queue depth: {18}. HW transfer: {19:0.###} ms. BGRA convert: {20:0.###} ms.",
                ffmpegEngine.GlobalFrameIndexStatus,
                ffmpegEngine.IndexedFrameCount,
                engine.Position.IsFrameIndexAbsolute ? "yes" : "no",
                ffmpegEngine.LastOpenTotalMilliseconds,
                ffmpegEngine.LastOpenContainerProbeMilliseconds,
                ffmpegEngine.LastOpenStreamDiscoveryMilliseconds,
                ffmpegEngine.LastOpenAudioProbeMilliseconds,
                ffmpegEngine.LastOpenFirstFrameDecodeMilliseconds,
                ffmpegEngine.LastOpenInitialCacheWarmMilliseconds,
                ffmpegEngine.LastGlobalFrameIndexBuildMilliseconds,
                string.IsNullOrWhiteSpace(ffmpegEngine.ActiveDecodeBackend) ? "(unknown)" : ffmpegEngine.ActiveDecodeBackend,
                ffmpegEngine.IsGpuActive ? "yes" : "no",
                string.IsNullOrWhiteSpace(ffmpegEngine.GpuCapabilityStatus) ? "(none)" : ffmpegEngine.GpuCapabilityStatus,
                string.IsNullOrWhiteSpace(ffmpegEngine.GpuFallbackReason) ? "(none)" : ffmpegEngine.GpuFallbackReason,
                string.IsNullOrWhiteSpace(ffmpegEngine.BudgetBand) ? "(none)" : ffmpegEngine.BudgetBand,
                string.IsNullOrWhiteSpace(ffmpegEngine.HostResourceClass) ? "(none)" : ffmpegEngine.HostResourceClass,
                ffmpegEngine.SessionDecodedFrameCacheBudgetBytes / 1048576d,
                ffmpegEngine.DecodedFrameCacheBudgetBytes / 1048576d,
                ffmpegEngine.OperationalQueueDepth,
                ffmpegEngine.LastHardwareFrameTransferMilliseconds,
                ffmpegEngine.LastBgraConversionMilliseconds);
        }

        private static FfmpegReviewEngine CreateFfmpegReviewEngine()
        {
            return new FfmpegReviewEngine(
                new FfmpegReviewEngineOptionsProvider(
                    new AppPreferencesService()));
        }

        private static void EnsureRuntimePathsConfigured()
        {
            var assemblyDirectory = Path.GetDirectoryName(typeof(ReviewEngineComparisonRunner).Assembly.Location) ??
                AppDomain.CurrentDomain.BaseDirectory;

            if (string.IsNullOrWhiteSpace(ffmpeg.RootPath))
            {
                ffmpeg.RootPath = assemblyDirectory;
            }
        }

        private static async Task<MeasuredOperationResult<T>> MeasureAsync<T>(Func<Task<T>> operation)
        {
            var stopwatch = Stopwatch.StartNew();
            var result = await operation().ConfigureAwait(true);
            stopwatch.Stop();
            return new MeasuredOperationResult<T>(result, stopwatch.Elapsed);
        }

        private static async Task<TimeSpan> MeasureAsync(Func<Task> operation)
        {
            var stopwatch = Stopwatch.StartNew();
            await operation().ConfigureAwait(true);
            stopwatch.Stop();
            return stopwatch.Elapsed;
        }

        private static ReviewEngineOperationDiagnostics CaptureDiagnostics(IVideoReviewEngine engine)
        {
            var ffmpegEngine = engine as FfmpegReviewEngine;
            if (ffmpegEngine == null)
            {
                return ReviewEngineOperationDiagnostics.Empty;
            }

            return new ReviewEngineOperationDiagnostics(
                ffmpegEngine.IsGlobalFrameIndexAvailable,
                ffmpegEngine.IndexedFrameCount,
                ffmpegEngine.LastOperationUsedGlobalIndex,
                ffmpegEngine.LastAnchorStrategy,
                ffmpegEngine.LastAnchorFrameIndex,
                ffmpegEngine.ActiveDecodeBackend,
                ffmpegEngine.IsGpuActive,
                ffmpegEngine.GpuCapabilityStatus,
                ffmpegEngine.GpuFallbackReason,
                ffmpegEngine.OperationalQueueDepth,
                ffmpegEngine.SessionDecodedFrameCacheBudgetBytes,
                ffmpegEngine.DecodedFrameCacheBudgetBytes,
                ffmpegEngine.BudgetBand,
                ffmpegEngine.HostResourceClass,
                ffmpegEngine.ActualBackendUsed,
                ffmpegEngine.PreviousCachedFrameCount,
                ffmpegEngine.ForwardCachedFrameCount,
                ffmpegEngine.MaxPreviousCachedFrameCount,
                ffmpegEngine.MaxForwardCachedFrameCount,
                ffmpegEngine.ApproximateCachedFrameBytes,
                ffmpegEngine.LastHardwareFrameTransferMilliseconds,
                ffmpegEngine.LastBgraConversionMilliseconds,
                ffmpegEngine.HasAudioStream,
                ffmpegEngine.AudioStreamInfo.DecoderAvailable,
                ffmpegEngine.IsAudioPlaybackActive,
                ffmpegEngine.LastPlaybackUsedAudioClock,
                ffmpegEngine.LastAudioSubmittedBytes,
                ffmpegEngine.AudioStreamInfo.CodecName,
                ffmpegEngine.LastAudioErrorMessage);
        }
    }

    internal sealed class MeasuredOperationResult<T>
    {
        public MeasuredOperationResult(T result, TimeSpan elapsed)
        {
            Result = result;
            Elapsed = elapsed;
        }

        public T Result { get; }

        public TimeSpan Elapsed { get; }
    }

    internal sealed class ReviewEngineOperationDiagnostics
    {
        public static ReviewEngineOperationDiagnostics Empty { get; } =
            new ReviewEngineOperationDiagnostics(
                null,
                null,
                null,
                string.Empty,
                null,
                string.Empty,
                null,
                string.Empty,
                string.Empty,
                null,
                null,
                null,
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
                null,
                null,
                null,
                string.Empty,
                string.Empty);

        public ReviewEngineOperationDiagnostics(
            bool? isGlobalFrameIndexAvailable,
            long? indexedFrameCount,
            bool? usedGlobalIndex,
            string anchorStrategy,
            long? anchorFrameIndex,
            string activeDecodeBackend,
            bool? isGpuActive,
            string gpuCapabilityStatus,
            string gpuFallbackReason,
            int? operationalQueueDepth,
            long? sessionDecodedFrameCacheBudgetBytes,
            long? decodedFrameCacheBudgetBytes,
            string budgetBand,
            string hostResourceClass,
            string actualBackendUsed,
            int? previousCachedFrameCount,
            int? forwardCachedFrameCount,
            int? maxPreviousCachedFrameCount,
            int? maxForwardCachedFrameCount,
            long? approximateCachedFrameBytes,
            double? hardwareFrameTransferMilliseconds,
            double? bgraConversionMilliseconds,
            bool? hasAudioStream,
            bool? audioPlaybackAvailable,
            bool? audioPlaybackActive,
            bool? lastPlaybackUsedAudioClock,
            long? lastAudioSubmittedBytes,
            string audioCodecName,
            string audioErrorMessage)
        {
            IsGlobalFrameIndexAvailable = isGlobalFrameIndexAvailable;
            IndexedFrameCount = indexedFrameCount;
            UsedGlobalIndex = usedGlobalIndex;
            AnchorStrategy = anchorStrategy ?? string.Empty;
            AnchorFrameIndex = anchorFrameIndex;
            ActiveDecodeBackend = activeDecodeBackend ?? string.Empty;
            IsGpuActive = isGpuActive;
            GpuCapabilityStatus = gpuCapabilityStatus ?? string.Empty;
            GpuFallbackReason = gpuFallbackReason ?? string.Empty;
            OperationalQueueDepth = operationalQueueDepth;
            SessionDecodedFrameCacheBudgetBytes = sessionDecodedFrameCacheBudgetBytes;
            DecodedFrameCacheBudgetBytes = decodedFrameCacheBudgetBytes;
            BudgetBand = budgetBand ?? string.Empty;
            HostResourceClass = hostResourceClass ?? string.Empty;
            ActualBackendUsed = actualBackendUsed ?? string.Empty;
            PreviousCachedFrameCount = previousCachedFrameCount;
            ForwardCachedFrameCount = forwardCachedFrameCount;
            MaxPreviousCachedFrameCount = maxPreviousCachedFrameCount;
            MaxForwardCachedFrameCount = maxForwardCachedFrameCount;
            ApproximateCachedFrameBytes = approximateCachedFrameBytes;
            HardwareFrameTransferMilliseconds = hardwareFrameTransferMilliseconds;
            BgraConversionMilliseconds = bgraConversionMilliseconds;
            HasAudioStream = hasAudioStream;
            AudioPlaybackAvailable = audioPlaybackAvailable;
            AudioPlaybackActive = audioPlaybackActive;
            LastPlaybackUsedAudioClock = lastPlaybackUsedAudioClock;
            LastAudioSubmittedBytes = lastAudioSubmittedBytes;
            AudioCodecName = audioCodecName ?? string.Empty;
            AudioErrorMessage = audioErrorMessage ?? string.Empty;
        }

        public bool? IsGlobalFrameIndexAvailable { get; }

        public long? IndexedFrameCount { get; }

        public bool? UsedGlobalIndex { get; }

        public string AnchorStrategy { get; }

        public long? AnchorFrameIndex { get; }

        public string ActiveDecodeBackend { get; }

        public bool? IsGpuActive { get; }

        public string GpuCapabilityStatus { get; }

        public string GpuFallbackReason { get; }

        public int? OperationalQueueDepth { get; }

        public long? SessionDecodedFrameCacheBudgetBytes { get; }

        public long? DecodedFrameCacheBudgetBytes { get; }

        public string BudgetBand { get; }

        public string HostResourceClass { get; }

        public string ActualBackendUsed { get; }

        public int? PreviousCachedFrameCount { get; }

        public int? ForwardCachedFrameCount { get; }

        public int? MaxPreviousCachedFrameCount { get; }

        public int? MaxForwardCachedFrameCount { get; }

        public long? ApproximateCachedFrameBytes { get; }

        public double? HardwareFrameTransferMilliseconds { get; }

        public double? BgraConversionMilliseconds { get; }

        public bool? HasAudioStream { get; }

        public bool? AudioPlaybackAvailable { get; }

        public bool? AudioPlaybackActive { get; }

        public bool? LastPlaybackUsedAudioClock { get; }

        public long? LastAudioSubmittedBytes { get; }

        public string AudioCodecName { get; }

        public string AudioErrorMessage { get; }
    }

    public sealed class ReviewEngineComparisonReport
    {
        public ReviewEngineComparisonReport(
            string filePath,
            TimeSpan seekTime,
            long seekFrameIndex,
            ReviewEngineScenarioReport customFfmpeg)
        {
            FilePath = filePath ?? string.Empty;
            SeekTime = seekTime;
            SeekFrameIndex = seekFrameIndex;
            CustomFfmpeg = customFfmpeg;
        }

        public string FilePath { get; }

        public TimeSpan SeekTime { get; }

        public long SeekFrameIndex { get; }

        public ReviewEngineScenarioReport CustomFfmpeg { get; }
    }

    public sealed class ReviewEngineScenarioReport
    {
        public ReviewEngineScenarioReport(
            string backendName,
            ReviewOperationSnapshot openResult,
            ReviewOperationSnapshot playbackResult,
            ReviewOperationSnapshot seekToTimeResult,
            ReviewOperationSnapshot seekToFrameResult,
            ReviewStepOperationSnapshot backwardStepResult,
            ReviewStepOperationSnapshot forwardStepResult,
            string scenarioError)
        {
            BackendName = backendName ?? string.Empty;
            OpenResult = openResult;
            PlaybackResult = playbackResult;
            SeekToTimeResult = seekToTimeResult;
            SeekToFrameResult = seekToFrameResult;
            BackwardStepResult = backwardStepResult;
            ForwardStepResult = forwardStepResult;
            ScenarioError = scenarioError ?? string.Empty;
        }

        public string BackendName { get; }

        public ReviewOperationSnapshot OpenResult { get; }

        public ReviewOperationSnapshot PlaybackResult { get; }

        public ReviewOperationSnapshot SeekToTimeResult { get; }

        public ReviewOperationSnapshot SeekToFrameResult { get; }

        public ReviewStepOperationSnapshot BackwardStepResult { get; }

        public ReviewStepOperationSnapshot ForwardStepResult { get; }

        public string ScenarioError { get; }
    }

    public sealed class ReviewOperationSnapshot
    {
        public ReviewOperationSnapshot(
            string operationName,
            bool succeeded,
            string errorMessage,
            bool isMediaOpen,
            ReviewPosition position,
            VideoMediaInfo mediaInfo,
            string note,
            double elapsedMilliseconds,
            bool? isGlobalFrameIndexAvailable,
            long? indexedFrameCount,
            bool? usedGlobalIndex,
            string anchorStrategy,
            long? anchorFrameIndex,
            string activeDecodeBackend,
            bool? isGpuActive,
            string gpuCapabilityStatus,
            string gpuFallbackReason,
            int? operationalQueueDepth,
            long? sessionDecodedFrameCacheBudgetBytes,
            long? decodedFrameCacheBudgetBytes,
            string budgetBand,
            string hostResourceClass,
            string actualBackendUsed,
            int? previousCachedFrameCount,
            int? forwardCachedFrameCount,
            int? maxPreviousCachedFrameCount,
            int? maxForwardCachedFrameCount,
            long? approximateCachedFrameBytes,
            double? hardwareFrameTransferMilliseconds,
            double? bgraConversionMilliseconds,
            bool? hasAudioStream,
            bool? audioPlaybackAvailable,
            bool? audioPlaybackActive,
            bool? lastPlaybackUsedAudioClock,
            long? lastAudioSubmittedBytes,
            string audioCodecName,
            string audioErrorMessage)
        {
            OperationName = operationName ?? string.Empty;
            Succeeded = succeeded;
            ErrorMessage = errorMessage ?? string.Empty;
            IsMediaOpen = isMediaOpen;
            Position = position ?? ReviewPosition.Empty;
            MediaInfo = mediaInfo ?? VideoMediaInfo.Empty;
            Note = note ?? string.Empty;
            ElapsedMilliseconds = elapsedMilliseconds;
            IsGlobalFrameIndexAvailable = isGlobalFrameIndexAvailable;
            IndexedFrameCount = indexedFrameCount;
            UsedGlobalIndex = usedGlobalIndex;
            AnchorStrategy = anchorStrategy ?? string.Empty;
            AnchorFrameIndex = anchorFrameIndex;
            ActiveDecodeBackend = activeDecodeBackend ?? string.Empty;
            IsGpuActive = isGpuActive;
            GpuCapabilityStatus = gpuCapabilityStatus ?? string.Empty;
            GpuFallbackReason = gpuFallbackReason ?? string.Empty;
            OperationalQueueDepth = operationalQueueDepth;
            SessionDecodedFrameCacheBudgetBytes = sessionDecodedFrameCacheBudgetBytes;
            DecodedFrameCacheBudgetBytes = decodedFrameCacheBudgetBytes;
            BudgetBand = budgetBand ?? string.Empty;
            HostResourceClass = hostResourceClass ?? string.Empty;
            ActualBackendUsed = actualBackendUsed ?? string.Empty;
            PreviousCachedFrameCount = previousCachedFrameCount;
            ForwardCachedFrameCount = forwardCachedFrameCount;
            MaxPreviousCachedFrameCount = maxPreviousCachedFrameCount;
            MaxForwardCachedFrameCount = maxForwardCachedFrameCount;
            ApproximateCachedFrameBytes = approximateCachedFrameBytes;
            HardwareFrameTransferMilliseconds = hardwareFrameTransferMilliseconds;
            BgraConversionMilliseconds = bgraConversionMilliseconds;
            HasAudioStream = hasAudioStream;
            AudioPlaybackAvailable = audioPlaybackAvailable;
            AudioPlaybackActive = audioPlaybackActive;
            LastPlaybackUsedAudioClock = lastPlaybackUsedAudioClock;
            LastAudioSubmittedBytes = lastAudioSubmittedBytes;
            AudioCodecName = audioCodecName ?? string.Empty;
            AudioErrorMessage = audioErrorMessage ?? string.Empty;
        }

        public string OperationName { get; }

        public bool Succeeded { get; }

        public string ErrorMessage { get; }

        public bool IsMediaOpen { get; }

        public ReviewPosition Position { get; }

        public VideoMediaInfo MediaInfo { get; }

        public string Note { get; }

        public double ElapsedMilliseconds { get; }

        public bool? IsGlobalFrameIndexAvailable { get; }

        public long? IndexedFrameCount { get; }

        public bool? UsedGlobalIndex { get; }

        public string AnchorStrategy { get; }

        public long? AnchorFrameIndex { get; }

        public string ActiveDecodeBackend { get; }

        public bool? IsGpuActive { get; }

        public string GpuCapabilityStatus { get; }

        public string GpuFallbackReason { get; }

        public int? OperationalQueueDepth { get; }

        public long? SessionDecodedFrameCacheBudgetBytes { get; }

        public long? DecodedFrameCacheBudgetBytes { get; }

        public string BudgetBand { get; }

        public string HostResourceClass { get; }

        public string ActualBackendUsed { get; }

        public int? PreviousCachedFrameCount { get; }

        public int? ForwardCachedFrameCount { get; }

        public int? MaxPreviousCachedFrameCount { get; }

        public int? MaxForwardCachedFrameCount { get; }

        public long? ApproximateCachedFrameBytes { get; }

        public double? HardwareFrameTransferMilliseconds { get; }

        public double? BgraConversionMilliseconds { get; }

        public bool? HasAudioStream { get; }

        public bool? AudioPlaybackAvailable { get; }

        public bool? AudioPlaybackActive { get; }

        public bool? LastPlaybackUsedAudioClock { get; }

        public long? LastAudioSubmittedBytes { get; }

        public string AudioCodecName { get; }

        public string AudioErrorMessage { get; }
    }

    public sealed class ReviewStepOperationSnapshot
    {
        public ReviewStepOperationSnapshot(
            string operationName,
            FrameStepResult stepResult,
            bool isMediaOpen,
            ReviewPosition position,
            VideoMediaInfo mediaInfo,
            double elapsedMilliseconds,
            bool? isGlobalFrameIndexAvailable,
            long? indexedFrameCount,
            bool? usedGlobalIndex,
            string anchorStrategy,
            long? anchorFrameIndex,
            string activeDecodeBackend,
            bool? isGpuActive,
            string gpuCapabilityStatus,
            string gpuFallbackReason,
            int? operationalQueueDepth,
            long? sessionDecodedFrameCacheBudgetBytes,
            long? decodedFrameCacheBudgetBytes,
            string budgetBand,
            string hostResourceClass,
            string actualBackendUsed,
            int? previousCachedFrameCount,
            int? forwardCachedFrameCount,
            int? maxPreviousCachedFrameCount,
            int? maxForwardCachedFrameCount,
            long? approximateCachedFrameBytes,
            double? hardwareFrameTransferMilliseconds,
            double? bgraConversionMilliseconds,
            bool? hasAudioStream,
            bool? audioPlaybackAvailable,
            bool? audioPlaybackActive,
            bool? lastPlaybackUsedAudioClock,
            long? lastAudioSubmittedBytes,
            string audioCodecName,
            string audioErrorMessage)
        {
            OperationName = operationName ?? string.Empty;
            StepResult = stepResult ?? FrameStepResult.Failed(0, ReviewPosition.Empty, "No step result was returned.");
            IsMediaOpen = isMediaOpen;
            Position = position ?? ReviewPosition.Empty;
            MediaInfo = mediaInfo ?? VideoMediaInfo.Empty;
            ElapsedMilliseconds = elapsedMilliseconds;
            IsGlobalFrameIndexAvailable = isGlobalFrameIndexAvailable;
            IndexedFrameCount = indexedFrameCount;
            UsedGlobalIndex = usedGlobalIndex;
            AnchorStrategy = anchorStrategy ?? string.Empty;
            AnchorFrameIndex = anchorFrameIndex;
            ActiveDecodeBackend = activeDecodeBackend ?? string.Empty;
            IsGpuActive = isGpuActive;
            GpuCapabilityStatus = gpuCapabilityStatus ?? string.Empty;
            GpuFallbackReason = gpuFallbackReason ?? string.Empty;
            OperationalQueueDepth = operationalQueueDepth;
            SessionDecodedFrameCacheBudgetBytes = sessionDecodedFrameCacheBudgetBytes;
            DecodedFrameCacheBudgetBytes = decodedFrameCacheBudgetBytes;
            BudgetBand = budgetBand ?? string.Empty;
            HostResourceClass = hostResourceClass ?? string.Empty;
            ActualBackendUsed = actualBackendUsed ?? string.Empty;
            PreviousCachedFrameCount = previousCachedFrameCount;
            ForwardCachedFrameCount = forwardCachedFrameCount;
            MaxPreviousCachedFrameCount = maxPreviousCachedFrameCount;
            MaxForwardCachedFrameCount = maxForwardCachedFrameCount;
            ApproximateCachedFrameBytes = approximateCachedFrameBytes;
            HardwareFrameTransferMilliseconds = hardwareFrameTransferMilliseconds;
            BgraConversionMilliseconds = bgraConversionMilliseconds;
            HasAudioStream = hasAudioStream;
            AudioPlaybackAvailable = audioPlaybackAvailable;
            AudioPlaybackActive = audioPlaybackActive;
            LastPlaybackUsedAudioClock = lastPlaybackUsedAudioClock;
            LastAudioSubmittedBytes = lastAudioSubmittedBytes;
            AudioCodecName = audioCodecName ?? string.Empty;
            AudioErrorMessage = audioErrorMessage ?? string.Empty;
        }

        public string OperationName { get; }

        public FrameStepResult StepResult { get; }

        public bool IsMediaOpen { get; }

        public ReviewPosition Position { get; }

        public VideoMediaInfo MediaInfo { get; }

        public double ElapsedMilliseconds { get; }

        public bool? IsGlobalFrameIndexAvailable { get; }

        public long? IndexedFrameCount { get; }

        public bool? UsedGlobalIndex { get; }

        public string AnchorStrategy { get; }

        public long? AnchorFrameIndex { get; }

        public string ActiveDecodeBackend { get; }

        public bool? IsGpuActive { get; }

        public string GpuCapabilityStatus { get; }

        public string GpuFallbackReason { get; }

        public int? OperationalQueueDepth { get; }

        public long? SessionDecodedFrameCacheBudgetBytes { get; }

        public long? DecodedFrameCacheBudgetBytes { get; }

        public string BudgetBand { get; }

        public string HostResourceClass { get; }

        public string ActualBackendUsed { get; }

        public int? PreviousCachedFrameCount { get; }

        public int? ForwardCachedFrameCount { get; }

        public int? MaxPreviousCachedFrameCount { get; }

        public int? MaxForwardCachedFrameCount { get; }

        public long? ApproximateCachedFrameBytes { get; }

        public double? HardwareFrameTransferMilliseconds { get; }

        public double? BgraConversionMilliseconds { get; }

        public bool? HasAudioStream { get; }

        public bool? AudioPlaybackAvailable { get; }

        public bool? AudioPlaybackActive { get; }

        public bool? LastPlaybackUsedAudioClock { get; }

        public long? LastAudioSubmittedBytes { get; }

        public string AudioCodecName { get; }

        public string AudioErrorMessage { get; }
    }
}
