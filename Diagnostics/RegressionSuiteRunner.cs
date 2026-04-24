using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using FFmpeg.AutoGen;
using FramePlayer.Core.Coordination;
using FramePlayer.Core.Models;
using FramePlayer.Controls;
using FramePlayer.Engines.FFmpeg;
using FramePlayer.Services;

namespace FramePlayer.Diagnostics
{
    public static class RegressionSuiteRunner
    {
        private const string PrimaryPaneId = "pane-primary";
        private const string ComparePaneId = "pane-compare-a";
        private const string EngineScope = "engine";
        private const string CorrectnessCategory = "correctness";
        private const string LifecycleCategory = "lifecycle";
        private const string CoverageCategory = "coverage";
        private const string PackagingCategory = "packaging";
        private const string PackagingScope = "(packaging)";
        private const string WarningClassification = "warning";
        private const string ClickSeekInteraction = "click";
        private const string UiAudioInsertionWavCheckName = "ui-audio-insertion-wav";
        private const string UiAudioInsertionMp3CheckName = "ui-audio-insertion-mp3";
        private const string ForwardOnlyStepProofCheckName = "forward-only-step-proof";
        private const string NoneText = "(none)";
        private static readonly StringComparer FilePathComparer = StringComparer.OrdinalIgnoreCase;
        private static readonly HashSet<string> SupportedRegressionExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            ".avi",
            ".m4v",
            ".mkv",
            ".mp4",
            ".wmv"
        };
        private static readonly TimeSpan IndexReadyTimeout = TimeSpan.FromSeconds(30d);
        private static readonly TimeSpan UiSeekWarningThreshold = TimeSpan.FromMilliseconds(2000d);
        private static readonly TimeSpan PlaybackDelay = TimeSpan.FromMilliseconds(500d);
        private static readonly TimeSpan LoopUiReadyTimeout = TimeSpan.FromSeconds(5d);
        private static readonly TimeSpan MinimumFullMediaLoopWrapDuration = TimeSpan.FromSeconds(1d);
        private static readonly TimeSpan LoopPlaybackRecoveryDelay = TimeSpan.FromMilliseconds(150d);
        private const long MinimumPaneLocalLoopCoverageFrames = 50L;
        private static readonly string[] StaleRuntimeFiles =
        {
            "avcodec-61.dll",
            "avdevice-61.dll",
            "avfilter-10.dll",
            "avformat-61.dll",
            "avutil-59.dll",
            "swresample-5.dll",
            "swscale-8.dll"
        };

        public static string DiagnosticTracePath { get; set; }

        public static async Task<RegressionSuiteReport> RunAsync(
            IEnumerable<string> filePaths,
            string packagedOutputDirectory,
            string packagedArtifactPath,
            string runtimeManifestPath,
            CancellationToken cancellationToken = default(CancellationToken))
        {
            return await RunAsync(
                    filePaths,
                    packagedOutputDirectory,
                    packagedArtifactPath,
                    runtimeManifestPath,
                    string.Empty,
                    cancellationToken)
                .ConfigureAwait(false);
        }

        public static async Task<RegressionSuiteReport> RunAsync(
            IEnumerable<string> filePaths,
            string packagedOutputDirectory,
            string packagedArtifactPath,
            string runtimeManifestPath,
            string audioInsertionMp3FixturePath,
            CancellationToken cancellationToken = default(CancellationToken))
        {
            ArgumentNullException.ThrowIfNull(filePaths);

            var normalizedFilePaths = NormalizeFilePaths(filePaths).ToArray();
            if (normalizedFilePaths.Length == 0)
            {
                throw new ArgumentException("At least one media file path is required.", nameof(filePaths));
            }

            if (string.IsNullOrWhiteSpace(packagedOutputDirectory))
            {
                throw new ArgumentException("A packaged output directory is required.", nameof(packagedOutputDirectory));
            }

            if (string.IsNullOrWhiteSpace(packagedArtifactPath))
            {
                throw new ArgumentException("A packaged artifact path is required.", nameof(packagedArtifactPath));
            }

            if (string.IsNullOrWhiteSpace(runtimeManifestPath))
            {
                throw new ArgumentException("A runtime manifest path is required.", nameof(runtimeManifestPath));
            }

            ffmpeg.RootPath = packagedOutputDirectory;
            var resolvedAudioInsertionMp3FixturePath = ResolveExistingFilePathOrEmpty(audioInsertionMp3FixturePath);
            Trace("RegressionSuiteRunner.RunAsync starting.");

            var packaging = PackagingRegressionValidator.Validate(
                packagedOutputDirectory,
                packagedArtifactPath,
                runtimeManifestPath);
            Trace("Packaging validation complete.");

            var fileReports = new List<RegressionFileReport>(normalizedFilePaths.Length);
            foreach (var filePath in normalizedFilePaths)
            {
                cancellationToken.ThrowIfCancellationRequested();
                Trace("Running file: " + filePath);
                fileReports.Add(await RunFileAsync(filePath, resolvedAudioInsertionMp3FixturePath, cancellationToken).ConfigureAwait(false));
                Trace("Completed file: " + filePath);
            }

            Trace("RegressionSuiteRunner.RunAsync completed.");
            return new RegressionSuiteReport(
                DateTimeOffset.UtcNow.ToString("o", CultureInfo.InvariantCulture),
                packagedOutputDirectory,
                packagedArtifactPath,
                packaging,
                fileReports.ToArray(),
                BuildSummary(packaging, fileReports));
        }

        private static string ResolveExistingFilePathOrEmpty(string filePath)
        {
            if (string.IsNullOrWhiteSpace(filePath))
            {
                return string.Empty;
            }

            try
            {
                var resolvedPath = Path.GetFullPath(filePath);
                return File.Exists(resolvedPath) ? resolvedPath : string.Empty;
            }
            catch
            {
                return string.Empty;
            }
        }

        private static IEnumerable<string> NormalizeFilePaths(IEnumerable<string> filePaths)
        {
            var seen = new HashSet<string>(FilePathComparer);
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

                if (!IsSupportedVideoFile(fullPath))
                {
                    Trace("Skipping unsupported regression media file: " + fullPath);
                    continue;
                }

                if (seen.Add(fullPath))
                {
                    yield return fullPath;
                }
            }
        }

        private static bool IsSupportedVideoFile(string filePath)
        {
            var extension = Path.GetExtension(filePath);
            if (string.IsNullOrWhiteSpace(extension))
            {
                return false;
            }

            return SupportedRegressionExtensions.Contains(extension);
        }

        private static async Task<RegressionFileReport> RunFileAsync(
            string filePath,
            string audioInsertionMp3FixturePath,
            CancellationToken cancellationToken)
        {
            Trace("Engine checks starting: " + filePath);
            var engineResult = await RunEngineChecksAsync(filePath, cancellationToken).ConfigureAwait(false);
            Trace("Engine checks complete: " + filePath);
            Trace("UI checks starting: " + filePath);
            var uiResult = await MainWindowRegressionHarness.RunInMainWindowAsync(
                    filePath,
                    true,
                    audioInsertionMp3FixturePath,
                    cancellationToken)
                .ConfigureAwait(false);
            Trace("UI checks complete: " + filePath);

            return new RegressionFileReport(
                filePath,
                Path.GetFileName(filePath),
                engineResult.MediaProfile,
                engineResult.DecodeProfile,
                engineResult.Checks.ToArray(),
                uiResult.Checks.ToArray(),
                engineResult.Metrics,
                uiResult.Metrics,
                engineResult.Notes.Concat(uiResult.Notes).ToArray());
        }

        private sealed class EngineRegressionResult
        {
            public EngineRegressionResult(
                RegressionMediaProfile mediaProfile,
                RegressionDecodeProfile decodeProfile,
                IEnumerable<RegressionCheckResult> checks,
                IEnumerable<string> notes,
                RegressionMetrics metrics)
            {
                MediaProfile = mediaProfile;
                DecodeProfile = decodeProfile ?? RegressionDecodeProfile.Empty;
                Checks = checks != null ? checks.ToArray() : Array.Empty<RegressionCheckResult>();
                Notes = notes != null ? notes.ToArray() : Array.Empty<string>();
                Metrics = metrics ?? new RegressionMetrics();
            }

            public RegressionMediaProfile MediaProfile { get; }

            public RegressionDecodeProfile DecodeProfile { get; }

            public RegressionCheckResult[] Checks { get; }

            public string[] Notes { get; }

            public RegressionMetrics Metrics { get; }
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

        private static async Task<EngineRegressionResult> RunEngineChecksAsync(string filePath, CancellationToken cancellationToken)
        {
            var checks = new List<RegressionCheckResult>();
            var notes = new List<string>();
            var metrics = new RegressionMetrics();

            using (var engine = CreateFfmpegReviewEngine())
            {
                try
                {
                    var openOperation = await MeasureAsync(
                            async () => await engine.OpenAsync(filePath, cancellationToken).ConfigureAwait(false))
                        .ConfigureAwait(false);
                    metrics.OpenMilliseconds = openOperation.Elapsed.TotalMilliseconds;
                    ObserveCacheMetrics(metrics, engine);
                }
                catch (Exception ex)
                {
                    checks.Add(Fail(
                        filePath,
                        EngineScope,
                        CorrectnessCategory,
                        "open-first-frame",
                        "Opening the file failed: " + ex.Message));

                    return new EngineRegressionResult(
                        BuildMediaProfile(engine.MediaInfo),
                        BuildDecodeProfile(engine, metrics),
                        checks,
                        notes,
                        metrics);
                }

                var initialPosition = engine.Position ?? ReviewPosition.Empty;
                notes.Add(string.Format(
                    CultureInfo.InvariantCulture,
                    "Open timings: total {0:0.###} ms, probe {1:0.###} ms, stream {2:0.###} ms, first frame {3:0.###} ms, cache warm {4:0.###} ms. Index status: {5}. Decode backend: {6}. GPU active: {7}. GPU status: {8}. Fallback: {9}. Queue depth: {10}. Budget band: {11}. Host class: {12}. Session budget: {13:0.0} MiB. Pane budget: {14:0.0} MiB. HW transfer: {15:0.###} ms. BGRA convert: {16:0.###} ms.",
                    engine.LastOpenTotalMilliseconds,
                    engine.LastOpenContainerProbeMilliseconds,
                    engine.LastOpenStreamDiscoveryMilliseconds,
                    engine.LastOpenFirstFrameDecodeMilliseconds,
                    engine.LastOpenInitialCacheWarmMilliseconds,
                    engine.GlobalFrameIndexStatus,
                    string.IsNullOrWhiteSpace(engine.ActiveDecodeBackend) ? "(unknown)" : engine.ActiveDecodeBackend,
                    engine.IsGpuActive ? "yes" : "no",
                    string.IsNullOrWhiteSpace(engine.GpuCapabilityStatus) ? NoneText : engine.GpuCapabilityStatus,
                    string.IsNullOrWhiteSpace(engine.GpuFallbackReason) ? NoneText : engine.GpuFallbackReason,
                    engine.OperationalQueueDepth,
                    string.IsNullOrWhiteSpace(engine.BudgetBand) ? NoneText : engine.BudgetBand,
                    string.IsNullOrWhiteSpace(engine.HostResourceClass) ? NoneText : engine.HostResourceClass,
                    engine.SessionDecodedFrameCacheBudgetBytes / 1048576d,
                    engine.DecodedFrameCacheBudgetBytes / 1048576d,
                    engine.LastHardwareFrameTransferMilliseconds,
                    engine.LastBgraConversionMilliseconds));
                notes.Add(string.Format(
                    CultureInfo.InvariantCulture,
                    "Open cache window: {0} back / {1} ahead, approx {2:0.0} MiB of {3:0.0} MiB pane budget ({4:0.0} MiB session), refill {5} {6:0.###} ms ({7}->{8} ahead).",
                    engine.PreviousCachedFrameCount,
                    engine.ForwardCachedFrameCount,
                    engine.ApproximateCachedFrameBytes / 1048576d,
                    engine.DecodedFrameCacheBudgetBytes / 1048576d,
                    engine.SessionDecodedFrameCacheBudgetBytes / 1048576d,
                    string.IsNullOrWhiteSpace(engine.LastCacheRefillReason) ? NoneText : engine.LastCacheRefillReason,
                    engine.LastCacheRefillMilliseconds,
                    engine.LastCacheRefillStartingForwardCount,
                    engine.LastCacheRefillCompletedForwardCount));

                checks.Add(EvaluateExpectedFrame(
                    filePath,
                    EngineScope,
                    "open-first-frame",
                    0L,
                    initialPosition,
                    string.Format(
                        CultureInfo.InvariantCulture,
                        "Open landed at frame {0}. Absolute identity: {1}.",
                        FormatFrameIndex(initialPosition.FrameIndex),
                        initialPosition.IsFrameIndexAbsolute ? "yes" : "no"),
                    metrics.OpenMilliseconds));

                var preIndexSeekTarget = SelectQuarterDurationTarget(engine.MediaInfo.Duration, engine.MediaInfo.PositionStep);
                if (preIndexSeekTarget > TimeSpan.Zero)
                {
                    try
                    {
                        var preIndexSeek = await MeasureAsync(
                                async () => await engine.SeekToTimeAsync(preIndexSeekTarget, cancellationToken).ConfigureAwait(false))
                            .ConfigureAwait(false);
                        metrics.PreIndexSeekMilliseconds = preIndexSeek.Elapsed.TotalMilliseconds;
                        ObserveCacheMetrics(metrics, engine);

                        var preIndexPosition = engine.Position ?? ReviewPosition.Empty;
                        notes.Add(string.Format(
                            CultureInfo.InvariantCulture,
                            "Pre-index seek cache: landed in {0:0.###} ms, refill {1} {2:0.###} ms ({3}), cache {4} back / {5} ahead, approx {6:0.0} MiB of {7:0.0} MiB budget. Backend {8}, GPU {9}, status {10}.",
                            preIndexSeek.Elapsed.TotalMilliseconds,
                            string.IsNullOrWhiteSpace(engine.LastCacheRefillReason) ? NoneText : engine.LastCacheRefillReason,
                            engine.LastCacheRefillMilliseconds,
                            string.IsNullOrWhiteSpace(engine.LastCacheRefillMode) ? "none" : engine.LastCacheRefillMode,
                            engine.PreviousCachedFrameCount,
                            engine.ForwardCachedFrameCount,
                            engine.ApproximateCachedFrameBytes / 1048576d,
                            engine.DecodedFrameCacheBudgetBytes / 1048576d,
                            string.IsNullOrWhiteSpace(engine.ActiveDecodeBackend) ? "(unknown)" : engine.ActiveDecodeBackend,
                            engine.IsGpuActive ? "active" : "inactive",
                            string.IsNullOrWhiteSpace(engine.GpuCapabilityStatus) ? NoneText : engine.GpuCapabilityStatus));
                        if (preIndexPosition.IsFrameIndexAbsolute)
                        {
                            checks.Add(Pass(
                                filePath,
                                EngineScope,
                                CorrectnessCategory,
                                "seek-to-time-before-index-ready",
                                string.Format(
                                    CultureInfo.InvariantCulture,
                                    "Pre-index seek to {0} landed on absolute frame {1} at {2}.",
                                    FormatTime(preIndexSeekTarget),
                                    FormatFrameIndex(preIndexPosition.FrameIndex),
                                    FormatTime(preIndexPosition.PresentationTime)),
                                actualFrameIndex: preIndexPosition.FrameIndex,
                                actualTime: preIndexPosition.PresentationTime,
                                elapsedMilliseconds: metrics.PreIndexSeekMilliseconds,
                                indexReady: engine.IsGlobalFrameIndexAvailable,
                                usedGlobalIndex: engine.LastOperationUsedGlobalIndex));
                        }
                        else
                        {
                            checks.Add(Warning(
                                filePath,
                                EngineScope,
                                CorrectnessCategory,
                                "seek-to-time-before-index-ready",
                                string.Format(
                                    CultureInfo.InvariantCulture,
                                    "Pre-index seek to {0} landed at {1} while the global index was still '{2}', so frame identity remained pending instead of absolute.",
                                    FormatTime(preIndexSeekTarget),
                                    FormatTime(preIndexPosition.PresentationTime),
                                    engine.GlobalFrameIndexStatus),
                                actualFrameIndex: preIndexPosition.FrameIndex,
                                actualTime: preIndexPosition.PresentationTime,
                                elapsedMilliseconds: metrics.PreIndexSeekMilliseconds,
                                indexReady: engine.IsGlobalFrameIndexAvailable,
                                usedGlobalIndex: engine.LastOperationUsedGlobalIndex));
                        }
                    }
                    catch (Exception ex)
                    {
                        checks.Add(Fail(
                            filePath,
                            EngineScope,
                            CorrectnessCategory,
                            "seek-to-time-before-index-ready",
                            "Pre-index seek-to-time failed: " + ex.Message));
                    }
                }

                var indexReadyResult = await WaitForIndexReadyAsync(engine, IndexReadyTimeout, cancellationToken).ConfigureAwait(false);
                metrics.IndexReadyMilliseconds = indexReadyResult.ElapsedMilliseconds;
                if (indexReadyResult.Ready)
                {
                    checks.Add(Pass(
                        filePath,
                        EngineScope,
                        LifecycleCategory,
                        "background-index-ready",
                        string.Format(
                            CultureInfo.InvariantCulture,
                            "Global index became ready with {0} frames in {1:0.###} ms.",
                            engine.IndexedFrameCount,
                            indexReadyResult.ElapsedMilliseconds),
                        elapsedMilliseconds: indexReadyResult.ElapsedMilliseconds,
                        indexReady: true));
                }
                else
                {
                    checks.Add(Warning(
                        filePath,
                        EngineScope,
                        LifecycleCategory,
                        "background-index-ready",
                        string.Format(
                            CultureInfo.InvariantCulture,
                            "Global index was still '{0}' after waiting {1:0.###} ms, so exact last-frame checks may be skipped.",
                            engine.GlobalFrameIndexStatus,
                            indexReadyResult.ElapsedMilliseconds),
                        elapsedMilliseconds: indexReadyResult.ElapsedMilliseconds,
                        indexReady: false));
                }

                var totalFrames = engine.IsGlobalFrameIndexAvailable ? engine.IndexedFrameCount : -1L;
                var midpointFrame = SelectMidpointFrameIndex(totalFrames);
                if (midpointFrame >= 0L)
                {
                    await RunExactFrameSeekChecksAsync(filePath, engine, midpointFrame, "midpoint-frame-seek", metrics, checks, cancellationToken).ConfigureAwait(false);

                    var repeatedStepWindow = SelectRepeatedStepWindow(midpointFrame, totalFrames);
                    if (repeatedStepWindow > 0)
                    {
                        await RunForwardOnlyStepProofAsync(
                                filePath,
                                engine,
                                midpointFrame,
                                repeatedStepWindow,
                                metrics,
                                checks,
                                cancellationToken)
                            .ConfigureAwait(false);

                        await RunRepeatedStepChecksAsync(
                                filePath,
                                engine,
                                midpointFrame,
                                repeatedStepWindow,
                                metrics,
                                checks,
                                cancellationToken)
                            .ConfigureAwait(false);
                    }
                    else
                    {
                        checks.Add(Warning(
                            filePath,
                            EngineScope,
                            CorrectnessCategory,
                            "repeated-step-window",
                            "The indexed midpoint did not leave enough headroom for a repeated stepping window."));
                    }
                }
                else
                {
                    checks.Add(Warning(
                        filePath,
                        EngineScope,
                        CorrectnessCategory,
                        "midpoint-frame-seek",
                        "The global index never became available, so exact midpoint frame-seek checks were skipped."));
                }

                try
                {
                    var playbackStartFrame = engine.Position != null ? engine.Position.FrameIndex : null;
                    var playback = await MeasureAsync(
                            async () =>
                            {
                                await engine.PlayAsync().ConfigureAwait(false);
                                await Task.Delay(PlaybackDelay, cancellationToken).ConfigureAwait(false);
                                await engine.PauseAsync().ConfigureAwait(false);
                            })
                        .ConfigureAwait(false);
                    metrics.PlaybackMilliseconds = playback.Elapsed.TotalMilliseconds;
                    ObserveCacheMetrics(metrics, engine);
                    var playbackPosition = engine.Position ?? ReviewPosition.Empty;
                    var playbackAdvanced = playbackStartFrame.HasValue &&
                        playbackPosition.FrameIndex.HasValue &&
                        playbackPosition.FrameIndex.Value > playbackStartFrame.Value;

                    checks.Add(playbackAdvanced
                        ? Pass(
                            filePath,
                            EngineScope,
                            LifecycleCategory,
                            "playback-pause-progress",
                            string.Format(
                                CultureInfo.InvariantCulture,
                                "Playback advanced from frame {0} to frame {1}, then paused cleanly.",
                                playbackStartFrame.Value,
                                playbackPosition.FrameIndex.HasValue ? playbackPosition.FrameIndex.Value.ToString(CultureInfo.InvariantCulture) : NoneText),
                            actualFrameIndex: playbackPosition.FrameIndex,
                            actualTime: playbackPosition.PresentationTime,
                            elapsedMilliseconds: metrics.PlaybackMilliseconds)
                        : Fail(
                            filePath,
                            EngineScope,
                            LifecycleCategory,
                            "playback-pause-progress",
                            string.Format(
                                CultureInfo.InvariantCulture,
                                "Playback did not advance a decoded frame before pause. Start frame {0}, landed frame {1}.",
                                FormatFrameIndex(playbackStartFrame),
                                FormatFrameIndex(playbackPosition.FrameIndex)),
                            actualFrameIndex: playbackPosition.FrameIndex,
                            actualTime: playbackPosition.PresentationTime,
                            elapsedMilliseconds: metrics.PlaybackMilliseconds));

                    if (engine.HasAudioStream)
                    {
                        var audioHealthy = engine.AudioStreamInfo.DecoderAvailable &&
                            engine.LastAudioSubmittedBytes > 0L &&
                            engine.LastPlaybackUsedAudioClock;
                        checks.Add(audioHealthy
                            ? Pass(
                                filePath,
                                EngineScope,
                                CorrectnessCategory,
                                "audio-playback-path",
                                string.Format(
                                    CultureInfo.InvariantCulture,
                                    "Audio playback initialized with codec {0}, submitted {1} bytes, and used the audio clock.",
                                    string.IsNullOrWhiteSpace(engine.AudioStreamInfo.CodecName) ? "(unknown)" : engine.AudioStreamInfo.CodecName,
                                    engine.LastAudioSubmittedBytes))
                            : Fail(
                                filePath,
                                EngineScope,
                                CorrectnessCategory,
                                "audio-playback-path",
                                string.Format(
                                    CultureInfo.InvariantCulture,
                                    "Audio stream was present but playback diagnostics were unhealthy. Decoder available: {0}; submitted bytes: {1}; audio clock used: {2}; error: {3}",
                                    engine.AudioStreamInfo.DecoderAvailable ? "yes" : "no",
                                    engine.LastAudioSubmittedBytes,
                                    engine.LastPlaybackUsedAudioClock ? "yes" : "no",
                                    string.IsNullOrWhiteSpace(engine.LastAudioErrorMessage) ? NoneText : engine.LastAudioErrorMessage)));
                    }
                    else
                    {
                        checks.Add(Pass(
                            filePath,
                            EngineScope,
                            LifecycleCategory,
                            "video-only-playback-path",
                            "No audio stream was present, and playback correctly ran video-only."));
                    }
                }
                catch (Exception ex)
                {
                    checks.Add(Fail(
                        filePath,
                        EngineScope,
                        LifecycleCategory,
                        "playback-pause-progress",
                        "Playback/pause regression sequence failed: " + ex.Message));
                }

                if (midpointFrame >= 0L)
                {
                    var postPlaybackFrame = Math.Max(0L, midpointFrame / 2L);
                    await RunExactFrameSeekChecksAsync(
                            filePath,
                            engine,
                            postPlaybackFrame,
                            "post-playback-seek-step",
                            metrics,
                            checks,
                            cancellationToken)
                        .ConfigureAwait(false);
                }

                if (preIndexSeekTarget > TimeSpan.Zero)
                {
                    try
                    {
                        var indexedSeek = await MeasureAsync(
                                async () => await engine.SeekToTimeAsync(preIndexSeekTarget, cancellationToken).ConfigureAwait(false))
                            .ConfigureAwait(false);
                        metrics.IndexedSeekMilliseconds = indexedSeek.Elapsed.TotalMilliseconds;
                        ObserveCacheMetrics(metrics, engine);
                        var indexedSeekPosition = engine.Position ?? ReviewPosition.Empty;
                        notes.Add(string.Format(
                            CultureInfo.InvariantCulture,
                            "Indexed seek cache: landed in {0:0.###} ms, refill {1} {2:0.###} ms ({3}), cache {4} back / {5} ahead, approx {6:0.0} MiB.",
                            indexedSeek.Elapsed.TotalMilliseconds,
                            string.IsNullOrWhiteSpace(engine.LastCacheRefillReason) ? NoneText : engine.LastCacheRefillReason,
                            engine.LastCacheRefillMilliseconds,
                            string.IsNullOrWhiteSpace(engine.LastCacheRefillMode) ? "none" : engine.LastCacheRefillMode,
                            engine.PreviousCachedFrameCount,
                            engine.ForwardCachedFrameCount,
                            engine.ApproximateCachedFrameBytes / 1048576d));
                        var indexedSeekValid = indexedSeekPosition.IsFrameIndexAbsolute &&
                            engine.LastSeekLandedAtOrAfterTarget;
                        checks.Add(indexedSeekValid
                            ? Pass(
                                filePath,
                                EngineScope,
                                CorrectnessCategory,
                                "seek-to-time-after-index-ready",
                                string.Format(
                                    CultureInfo.InvariantCulture,
                                    "Indexed seek to {0} landed on absolute frame {1} at {2}.",
                                    FormatTime(preIndexSeekTarget),
                                    FormatFrameIndex(indexedSeekPosition.FrameIndex),
                                    FormatTime(indexedSeekPosition.PresentationTime)),
                                actualFrameIndex: indexedSeekPosition.FrameIndex,
                                actualTime: indexedSeekPosition.PresentationTime,
                                elapsedMilliseconds: metrics.IndexedSeekMilliseconds,
                                indexReady: engine.IsGlobalFrameIndexAvailable,
                                usedGlobalIndex: engine.LastOperationUsedGlobalIndex)
                            : Fail(
                                filePath,
                                EngineScope,
                                CorrectnessCategory,
                                "seek-to-time-after-index-ready",
                                string.Format(
                                    CultureInfo.InvariantCulture,
                                    "Indexed seek to {0} did not preserve truthful absolute landing. Absolute: {1}; landed-on-or-after-target: {2}; actual frame: {3}; actual time: {4}.",
                                    FormatTime(preIndexSeekTarget),
                                    indexedSeekPosition.IsFrameIndexAbsolute ? "yes" : "no",
                                    engine.LastSeekLandedAtOrAfterTarget ? "yes" : "no",
                                    FormatFrameIndex(indexedSeekPosition.FrameIndex),
                                    FormatTime(indexedSeekPosition.PresentationTime)),
                                actualFrameIndex: indexedSeekPosition.FrameIndex,
                                actualTime: indexedSeekPosition.PresentationTime,
                                elapsedMilliseconds: metrics.IndexedSeekMilliseconds,
                                indexReady: engine.IsGlobalFrameIndexAvailable,
                                usedGlobalIndex: engine.LastOperationUsedGlobalIndex));
                    }
                    catch (Exception ex)
                    {
                        checks.Add(Fail(
                            filePath,
                            EngineScope,
                            CorrectnessCategory,
                            "seek-to-time-after-index-ready",
                            "Indexed seek-to-time failed: " + ex.Message));
                    }
                }

                if (totalFrames > 0L)
                {
                    var lastFrameIndex = totalFrames - 1L;
                    await RunExactFrameSeekChecksAsync(
                            filePath,
                            engine,
                            lastFrameIndex,
                            "last-frame-end-of-video",
                            metrics,
                            checks,
                            cancellationToken)
                        .ConfigureAwait(false);
                }
                else
                {
                    checks.Add(Warning(
                        filePath,
                        EngineScope,
                        CorrectnessCategory,
                        "last-frame-end-of-video",
                        "The global index was unavailable, so exact last-frame regression checks were skipped."));
                }

                try
                {
                    var reopen = await MeasureAsync(
                            async () =>
                            {
                                await engine.CloseAsync().ConfigureAwait(false);
                                await engine.OpenAsync(filePath, cancellationToken).ConfigureAwait(false);
                            })
                        .ConfigureAwait(false);
                    metrics.ReopenMilliseconds = reopen.Elapsed.TotalMilliseconds;
                    ObserveCacheMetrics(metrics, engine);
                    checks.Add(EvaluateExpectedFrame(
                        filePath,
                        EngineScope,
                        "close-reopen-first-frame",
                        0L,
                        engine.Position,
                        string.Format(
                            CultureInfo.InvariantCulture,
                            "Close/reopen completed in {0:0.###} ms.",
                            metrics.ReopenMilliseconds),
                        metrics.ReopenMilliseconds));
                }
                catch (Exception ex)
                {
                    checks.Add(Fail(
                        filePath,
                        EngineScope,
                        LifecycleCategory,
                        "close-reopen-first-frame",
                        "Close/reopen failed: " + ex.Message));
                }

                return new EngineRegressionResult(
                    BuildMediaProfile(engine.MediaInfo),
                    BuildDecodeProfile(engine, metrics),
                    checks,
                    notes,
                    metrics);
            }
        }

        private static FfmpegReviewEngine CreateFfmpegReviewEngine()
        {
            return new FfmpegReviewEngine(
                new FfmpegReviewEngineOptionsProvider(
                    new AppPreferencesService()));
        }

        private static async Task RunExactFrameSeekChecksAsync(
            string filePath,
            FfmpegReviewEngine engine,
            long targetFrameIndex,
            string checkPrefix,
            RegressionMetrics metrics,
            List<RegressionCheckResult> checks,
            CancellationToken cancellationToken)
        {
            try
            {
                var seek = await MeasureAsync(
                        async () => await engine.SeekToFrameAsync(targetFrameIndex, cancellationToken).ConfigureAwait(false))
                    .ConfigureAwait(false);
                ObserveCacheMetrics(metrics, engine);
                checks.Add(EvaluateExpectedFrame(
                    filePath,
                    EngineScope,
                    checkPrefix + "-seek",
                    targetFrameIndex,
                    engine.Position,
                    string.Format(
                        CultureInfo.InvariantCulture,
                        "Seek-to-frame target {0} completed via {1}.",
                        targetFrameIndex,
                        string.IsNullOrWhiteSpace(engine.LastAnchorStrategy) ? "(no anchor)" : engine.LastAnchorStrategy),
                    seek.Elapsed.TotalMilliseconds,
                    engine.IsGlobalFrameIndexAvailable,
                    engine.LastOperationUsedGlobalIndex));
            }
            catch (Exception ex)
            {
                checks.Add(Fail(
                    filePath,
                    EngineScope,
                    CorrectnessCategory,
                    checkPrefix + "-seek",
                    "Seek-to-frame failed: " + ex.Message,
                    expectedFrameIndex: targetFrameIndex));
                return;
            }

            if (targetFrameIndex <= 0L)
            {
                return;
            }

            try
            {
                var backward = await MeasureAsync(
                        async () => await engine.StepBackwardAsync(cancellationToken).ConfigureAwait(false))
                    .ConfigureAwait(false);
                ObserveCacheMetrics(metrics, engine);
                checks.Add(EvaluateExpectedStep(
                    filePath,
                    EngineScope,
                    checkPrefix + "-step-backward",
                    targetFrameIndex - 1L,
                    backward.Result,
                    backward.Elapsed.TotalMilliseconds));
            }
            catch (Exception ex)
            {
                checks.Add(Fail(
                    filePath,
                    EngineScope,
                    CorrectnessCategory,
                    checkPrefix + "-step-backward",
                    "Backward step failed: " + ex.Message,
                    expectedFrameIndex: targetFrameIndex - 1L));
                return;
            }

            try
            {
                var forward = await MeasureAsync(
                        async () => await engine.StepForwardAsync(cancellationToken).ConfigureAwait(false))
                    .ConfigureAwait(false);
                ObserveCacheMetrics(metrics, engine);
                checks.Add(EvaluateExpectedStep(
                    filePath,
                    EngineScope,
                    checkPrefix + "-step-forward",
                    targetFrameIndex,
                    forward.Result,
                    forward.Elapsed.TotalMilliseconds));
            }
            catch (Exception ex)
            {
                checks.Add(Fail(
                    filePath,
                    EngineScope,
                    CorrectnessCategory,
                    checkPrefix + "-step-forward",
                    "Forward step failed: " + ex.Message,
                    expectedFrameIndex: targetFrameIndex));
            }
        }

        private static async Task RunForwardOnlyStepProofAsync(
            string filePath,
            FfmpegReviewEngine engine,
            long startFrameIndex,
            int stepWindow,
            RegressionMetrics metrics,
            List<RegressionCheckResult> checks,
            CancellationToken cancellationToken)
        {
            await engine.SeekToFrameAsync(startFrameIndex, cancellationToken).ConfigureAwait(false);
            ObserveCacheMetrics(metrics, engine);

            var startingForwardCount = engine.ForwardCachedFrameCount;
            var forwardCacheHits = 0;
            var forwardReconstructs = 0;

            for (var i = 1; i <= stepWindow; i++)
            {
                var forward = await engine.StepForwardAsync(cancellationToken).ConfigureAwait(false);
                var expectedFrame = startFrameIndex + i;
                if (!forward.Success ||
                    forward.Position == null ||
                    !forward.Position.IsFrameIndexAbsolute ||
                    !forward.Position.FrameIndex.HasValue ||
                    forward.Position.FrameIndex.Value != expectedFrame)
                {
                    checks.Add(Fail(
                        filePath,
                        EngineScope,
                        CorrectnessCategory,
                        ForwardOnlyStepProofCheckName,
                        string.Format(
                            CultureInfo.InvariantCulture,
                            "Forward-only step {0} expected frame {1} but landed on {2}.",
                            i,
                            expectedFrame,
                            forward.Position != null && forward.Position.FrameIndex.HasValue
                                ? forward.Position.FrameIndex.Value.ToString(CultureInfo.InvariantCulture)
                                : NoneText),
                        expectedFrameIndex: expectedFrame,
                        actualFrameIndex: forward.Position != null ? forward.Position.FrameIndex : null,
                        actualTime: forward.Position != null ? (TimeSpan?)forward.Position.PresentationTime : null,
                        cacheHit: forward.WasCacheHit,
                        requiredReconstruction: forward.RequiredReconstruction));
                    return;
                }

                if (forward.WasCacheHit)
                {
                    forwardCacheHits++;
                }

                if (forward.RequiredReconstruction)
                {
                    forwardReconstructs++;
                }

                ObserveCacheMetrics(metrics, engine);
            }

            if (engine.MaxForwardCachedFrameCount == 0)
            {
                if (startingForwardCount == 0 && forwardCacheHits == 0)
                {
                    checks.Add(Pass(
                        filePath,
                        EngineScope,
                        "behavior",
                        ForwardOnlyStepProofCheckName,
                        string.Format(
                            CultureInfo.InvariantCulture,
                            "Fresh-seek forward stepping stayed exact across {0} steps with no proactive forward cache. Starting forward cache: {1}; forward cache hits: {2}; forward reconstructions: {3}.",
                            stepWindow,
                            startingForwardCount,
                            forwardCacheHits,
                            forwardReconstructs),
                        expectedFrameIndex: startFrameIndex + stepWindow,
                        actualFrameIndex: engine.Position != null ? engine.Position.FrameIndex : null,
                        actualTime: engine.Position != null ? (TimeSpan?)engine.Position.PresentationTime : null));
                }
                else
                {
                    checks.Add(Fail(
                        filePath,
                        EngineScope,
                        "behavior",
                        ForwardOnlyStepProofCheckName,
                        string.Format(
                            CultureInfo.InvariantCulture,
                            "Configured forward cache is 0, but fresh-seek forward stepping started with {0} forward frames and observed {1} forward cache hits across {2} steps.",
                            startingForwardCount,
                            forwardCacheHits,
                            stepWindow),
                        expectedFrameIndex: startFrameIndex + stepWindow,
                        actualFrameIndex: engine.Position != null ? engine.Position.FrameIndex : null,
                        actualTime: engine.Position != null ? (TimeSpan?)engine.Position.PresentationTime : null));
                }

                return;
            }

            checks.Add(Pass(
                filePath,
                EngineScope,
                "behavior",
                ForwardOnlyStepProofCheckName,
                string.Format(
                    CultureInfo.InvariantCulture,
                    "Fresh-seek forward stepping stayed exact across {0} steps. Starting forward cache: {1}; configured forward cache: {2}; forward cache hits: {3}; forward reconstructions: {4}.",
                    stepWindow,
                    startingForwardCount,
                    engine.MaxForwardCachedFrameCount,
                    forwardCacheHits,
                    forwardReconstructs),
                expectedFrameIndex: startFrameIndex + stepWindow,
                actualFrameIndex: engine.Position != null ? engine.Position.FrameIndex : null,
                actualTime: engine.Position != null ? (TimeSpan?)engine.Position.PresentationTime : null));
        }

        private static async Task RunRepeatedStepChecksAsync(
            string filePath,
            FfmpegReviewEngine engine,
            long startFrameIndex,
            int stepWindow,
            RegressionMetrics metrics,
            List<RegressionCheckResult> checks,
            CancellationToken cancellationToken)
        {
            await engine.SeekToFrameAsync(startFrameIndex, cancellationToken).ConfigureAwait(false);
            ObserveCacheMetrics(metrics, engine);

            var backwardCacheHits = 0;
            var backwardReconstructs = 0;
            for (var i = 1; i <= stepWindow; i++)
            {
                var backward = await engine.StepBackwardAsync(cancellationToken).ConfigureAwait(false);
                var expectedFrame = startFrameIndex - i;
                if (!backward.Success ||
                    backward.Position == null ||
                    !backward.Position.IsFrameIndexAbsolute ||
                    !backward.Position.FrameIndex.HasValue ||
                    backward.Position.FrameIndex.Value != expectedFrame)
                {
                    checks.Add(Fail(
                        filePath,
                        EngineScope,
                        CorrectnessCategory,
                        "repeated-backward-steps",
                        string.Format(
                            CultureInfo.InvariantCulture,
                            "Backward step {0} expected frame {1} but landed on {2}.",
                            i,
                            expectedFrame,
                            backward.Position != null && backward.Position.FrameIndex.HasValue
                                ? backward.Position.FrameIndex.Value.ToString(CultureInfo.InvariantCulture)
                                : NoneText),
                        expectedFrameIndex: expectedFrame,
                        actualFrameIndex: backward.Position != null ? backward.Position.FrameIndex : null,
                        actualTime: backward.Position != null ? (TimeSpan?)backward.Position.PresentationTime : null,
                        cacheHit: backward.WasCacheHit,
                        requiredReconstruction: backward.RequiredReconstruction));
                    return;
                }

                if (backward.WasCacheHit)
                {
                    backwardCacheHits++;
                }

                if (backward.RequiredReconstruction)
                {
                    backwardReconstructs++;
                }

                ObserveCacheMetrics(metrics, engine);
            }

            var forwardCacheHits = 0;
            var forwardReconstructs = 0;
            for (var i = 1; i <= stepWindow; i++)
            {
                var forward = await engine.StepForwardAsync(cancellationToken).ConfigureAwait(false);
                var expectedFrame = (startFrameIndex - stepWindow) + i;
                if (!forward.Success ||
                    forward.Position == null ||
                    !forward.Position.IsFrameIndexAbsolute ||
                    !forward.Position.FrameIndex.HasValue ||
                    forward.Position.FrameIndex.Value != expectedFrame)
                {
                    checks.Add(Fail(
                        filePath,
                        EngineScope,
                        CorrectnessCategory,
                        "repeated-forward-steps",
                        string.Format(
                            CultureInfo.InvariantCulture,
                            "Forward step {0} expected frame {1} but landed on {2}.",
                            i,
                            expectedFrame,
                            forward.Position != null && forward.Position.FrameIndex.HasValue
                                ? forward.Position.FrameIndex.Value.ToString(CultureInfo.InvariantCulture)
                                : NoneText),
                        expectedFrameIndex: expectedFrame,
                        actualFrameIndex: forward.Position != null ? forward.Position.FrameIndex : null,
                        actualTime: forward.Position != null ? (TimeSpan?)forward.Position.PresentationTime : null,
                        cacheHit: forward.WasCacheHit,
                        requiredReconstruction: forward.RequiredReconstruction));
                    return;
                }

                if (forward.WasCacheHit)
                {
                    forwardCacheHits++;
                }

                if (forward.RequiredReconstruction)
                {
                    forwardReconstructs++;
                }

                ObserveCacheMetrics(metrics, engine);
            }

            metrics.BackwardStepCacheHits += backwardCacheHits;
            metrics.BackwardStepReconstructionCount += backwardReconstructs;
            metrics.ForwardStepCacheHits += forwardCacheHits;
            metrics.ForwardStepReconstructionCount += forwardReconstructs;

            checks.Add(Pass(
                filePath,
                EngineScope,
                CorrectnessCategory,
                "repeated-step-roundtrip",
                string.Format(
                    CultureInfo.InvariantCulture,
                    "Repeated stepping stayed exact across {0} backward and {0} forward steps. Back cache hits: {1}; back reconstructions: {2}; forward cache hits: {3}; forward reconstructions: {4}.",
                    stepWindow,
                    backwardCacheHits,
                    backwardReconstructs,
                    forwardCacheHits,
                    forwardReconstructs),
                expectedFrameIndex: startFrameIndex,
                actualFrameIndex: engine.Position != null ? engine.Position.FrameIndex : null,
                actualTime: engine.Position != null ? (TimeSpan?)engine.Position.PresentationTime : null));
        }

        private static void ObserveCacheMetrics(RegressionMetrics metrics, FfmpegReviewEngine engine)
        {
            if (metrics == null || engine == null)
            {
                return;
            }

            metrics.MaxObservedPreviousCachedFrames = Math.Max(metrics.MaxObservedPreviousCachedFrames, engine.PreviousCachedFrameCount);
            metrics.MaxObservedForwardCachedFrames = Math.Max(metrics.MaxObservedForwardCachedFrames, engine.ForwardCachedFrameCount);
            metrics.MaxObservedApproximateCacheBytes = Math.Max(metrics.MaxObservedApproximateCacheBytes, engine.ApproximateCachedFrameBytes);
            metrics.LastObservedCacheRefillMilliseconds = engine.LastCacheRefillMilliseconds;
            metrics.LastObservedCacheRefillReason = string.IsNullOrWhiteSpace(engine.LastCacheRefillReason)
                ? string.Empty
                : engine.LastCacheRefillReason;
            metrics.LastObservedCacheRefillMode = string.IsNullOrWhiteSpace(engine.LastCacheRefillMode)
                ? string.Empty
                : engine.LastCacheRefillMode;
        }

        private static RegressionMediaProfile BuildMediaProfile(VideoMediaInfo mediaInfo)
        {
            mediaInfo = mediaInfo ?? VideoMediaInfo.Empty;
            return new RegressionMediaProfile(
                mediaInfo.VideoCodecName,
                mediaInfo.PixelWidth,
                mediaInfo.PixelHeight,
                FormatTime(mediaInfo.Duration),
                mediaInfo.FramesPerSecond,
                mediaInfo.HasAudioStream,
                mediaInfo.IsAudioPlaybackAvailable,
                mediaInfo.AudioCodecName,
                mediaInfo.AudioSampleRate,
                mediaInfo.AudioChannelCount);
        }

        private static RegressionDecodeProfile BuildDecodeProfile(
            FfmpegReviewEngine engine,
            RegressionMetrics metrics)
        {
            if (engine == null)
            {
                return RegressionDecodeProfile.Empty;
            }

            metrics = metrics ?? new RegressionMetrics();
            var forwardStepAttempts = metrics.ForwardStepCacheHits + metrics.ForwardStepReconstructionCount;
            return new RegressionDecodeProfile(
                string.IsNullOrWhiteSpace(engine.ActiveDecodeBackend) ? string.Empty : engine.ActiveDecodeBackend,
                string.IsNullOrWhiteSpace(engine.ActualBackendUsed) ? string.Empty : engine.ActualBackendUsed,
                engine.IsGpuActive,
                string.IsNullOrWhiteSpace(engine.GpuCapabilityStatus) ? string.Empty : engine.GpuCapabilityStatus,
                string.IsNullOrWhiteSpace(engine.GpuFallbackReason) ? string.Empty : engine.GpuFallbackReason,
                string.IsNullOrWhiteSpace(engine.BudgetBand) ? string.Empty : engine.BudgetBand,
                string.IsNullOrWhiteSpace(engine.HostResourceClass) ? string.Empty : engine.HostResourceClass,
                engine.OperationalQueueDepth,
                engine.SessionDecodedFrameCacheBudgetBytes,
                engine.DecodedFrameCacheBudgetBytes,
                engine.MaxPreviousCachedFrameCount,
                engine.MaxForwardCachedFrameCount,
                metrics.MaxObservedPreviousCachedFrames,
                metrics.MaxObservedForwardCachedFrames,
                metrics.MaxObservedApproximateCacheBytes,
                metrics.BackwardStepCacheHits,
                metrics.BackwardStepReconstructionCount,
                metrics.ForwardStepCacheHits,
                metrics.ForwardStepReconstructionCount,
                forwardStepAttempts > 0
                    ? metrics.ForwardStepCacheHits / (double)forwardStepAttempts
                    : 0d,
                engine.LastHardwareFrameTransferMilliseconds,
                engine.LastBgraConversionMilliseconds,
                string.IsNullOrWhiteSpace(engine.GlobalFrameIndexStatus) ? string.Empty : engine.GlobalFrameIndexStatus,
                engine.IsGlobalFrameIndexAvailable,
                string.IsNullOrWhiteSpace(metrics.LastObservedCacheRefillReason) ? string.Empty : metrics.LastObservedCacheRefillReason,
                string.IsNullOrWhiteSpace(metrics.LastObservedCacheRefillMode) ? string.Empty : metrics.LastObservedCacheRefillMode,
                metrics.LastObservedCacheRefillMilliseconds);
        }

        private static long SelectMidpointFrameIndex(long totalFrames)
        {
            if (totalFrames <= 0L)
            {
                return -1L;
            }

            if (totalFrames == 1L)
            {
                return 0L;
            }

            var midpoint = totalFrames / 2L;
            return midpoint >= totalFrames ? totalFrames - 1L : midpoint;
        }

        private static int SelectRepeatedStepWindow(long startFrameIndex, long totalFrames)
        {
            if (startFrameIndex <= 0L || totalFrames <= 1L)
            {
                return 0;
            }

            var forwardHeadroom = Math.Max(0L, totalFrames - startFrameIndex - 1L);
            var backwardHeadroom = startFrameIndex;
            var window = (int)Math.Min(10L, Math.Min(backwardHeadroom, forwardHeadroom));
            return Math.Max(0, window);
        }

        private static TimeSpan SelectQuarterDurationTarget(TimeSpan duration, TimeSpan positionStep)
        {
            if (duration <= TimeSpan.Zero)
            {
                return TimeSpan.Zero;
            }

            var margin = positionStep > TimeSpan.Zero ? positionStep : TimeSpan.FromMilliseconds(100d);
            if (duration <= margin)
            {
                return TimeSpan.Zero;
            }

            var target = TimeSpan.FromTicks(duration.Ticks / 4L);
            if (target < margin)
            {
                target = margin;
            }

            var maxTarget = duration - margin;
            return target > maxTarget ? maxTarget : target;
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

        private static RegressionCheckResult EvaluateExpectedFrame(
            string filePath,
            string scope,
            string name,
            long expectedFrameIndex,
            ReviewPosition actualPosition,
            string message,
            double? elapsedMilliseconds = null,
            bool? indexReady = null,
            bool? usedGlobalIndex = null)
        {
            actualPosition = actualPosition ?? ReviewPosition.Empty;
            var success = actualPosition.IsFrameIndexAbsolute &&
                actualPosition.FrameIndex.HasValue &&
                actualPosition.FrameIndex.Value == expectedFrameIndex;

            return success
                ? Pass(
                    filePath,
                    scope,
                    CorrectnessCategory,
                    name,
                    message,
                    expectedFrameIndex,
                    actualPosition.FrameIndex,
                    actualTime: actualPosition.PresentationTime,
                    elapsedMilliseconds: elapsedMilliseconds,
                    indexReady: indexReady,
                    usedGlobalIndex: usedGlobalIndex)
                : Fail(
                    filePath,
                    scope,
                    CorrectnessCategory,
                    name,
                    string.Format(
                        CultureInfo.InvariantCulture,
                        "{0} Expected frame {1} but landed on {2}. Absolute identity: {3}.",
                        message,
                        expectedFrameIndex,
                        FormatFrameIndex(actualPosition.FrameIndex),
                        actualPosition.IsFrameIndexAbsolute ? "yes" : "no"),
                    expectedFrameIndex,
                    actualPosition.FrameIndex,
                    actualTime: actualPosition.PresentationTime,
                    elapsedMilliseconds: elapsedMilliseconds,
                    indexReady: indexReady,
                    usedGlobalIndex: usedGlobalIndex);
        }

        private static RegressionCheckResult EvaluateExpectedStep(
            string filePath,
            string scope,
            string name,
            long expectedFrameIndex,
            FrameStepResult stepResult,
            double elapsedMilliseconds)
        {
            if (stepResult == null)
            {
                return Fail(
                    filePath,
                    scope,
                    CorrectnessCategory,
                    name,
                    "No step result was returned.",
                    expectedFrameIndex: expectedFrameIndex,
                    elapsedMilliseconds: elapsedMilliseconds);
            }

            var position = stepResult.Position ?? ReviewPosition.Empty;
            var success = stepResult.Success &&
                position.IsFrameIndexAbsolute &&
                position.FrameIndex.HasValue &&
                position.FrameIndex.Value == expectedFrameIndex;

            return success
                ? Pass(
                    filePath,
                    scope,
                    CorrectnessCategory,
                    name,
                    string.Format(
                        CultureInfo.InvariantCulture,
                        "Step landed on exact frame {0}. Cache hit: {1}. Reconstruction: {2}.",
                        expectedFrameIndex,
                        stepResult.WasCacheHit ? "yes" : "no",
                        stepResult.RequiredReconstruction ? "yes" : "no"),
                    expectedFrameIndex,
                    position.FrameIndex,
                    actualTime: position.PresentationTime,
                    elapsedMilliseconds: elapsedMilliseconds,
                    cacheHit: stepResult.WasCacheHit,
                    requiredReconstruction: stepResult.RequiredReconstruction)
                : Fail(
                    filePath,
                    scope,
                    CorrectnessCategory,
                    name,
                    string.Format(
                        CultureInfo.InvariantCulture,
                        "Step expected frame {0} but landed on {1}. Success: {2}. Message: {3}",
                        expectedFrameIndex,
                        FormatFrameIndex(position.FrameIndex),
                        stepResult.Success ? "yes" : "no",
                        string.IsNullOrWhiteSpace(stepResult.Message) ? NoneText : stepResult.Message),
                    expectedFrameIndex,
                    position.FrameIndex,
                    actualTime: position.PresentationTime,
                    elapsedMilliseconds: elapsedMilliseconds,
                    cacheHit: stepResult.WasCacheHit,
                    requiredReconstruction: stepResult.RequiredReconstruction);
        }

        private static RegressionCheckResult Pass(
            string filePath,
            string scope,
            string category,
            string name,
            string message,
            long? expectedFrameIndex = null,
            long? actualFrameIndex = null,
            long? expectedDisplayedFrame = null,
            long? actualDisplayedFrame = null,
            TimeSpan? requestedTime = null,
            TimeSpan? actualTime = null,
            double? sliderValueSeconds = null,
            double? sliderMaximumSeconds = null,
            double? elapsedMilliseconds = null,
            bool? indexReady = null,
            bool? usedGlobalIndex = null,
            bool? cacheHit = null,
            bool? requiredReconstruction = null)
        {
            return new RegressionCheckResult(
                filePath,
                scope,
                category,
                name,
                "pass",
                message,
                expectedFrameIndex,
                actualFrameIndex,
                expectedDisplayedFrame,
                actualDisplayedFrame,
                requestedTime.HasValue ? FormatTime(requestedTime.Value) : string.Empty,
                actualTime.HasValue ? FormatTime(actualTime.Value) : string.Empty,
                sliderValueSeconds,
                sliderMaximumSeconds,
                elapsedMilliseconds,
                indexReady,
                usedGlobalIndex,
                cacheHit,
                requiredReconstruction);
        }

        private static RegressionCheckResult Warning(
            string filePath,
            string scope,
            string category,
            string name,
            string message,
            long? expectedFrameIndex = null,
            long? actualFrameIndex = null,
            long? expectedDisplayedFrame = null,
            long? actualDisplayedFrame = null,
            TimeSpan? requestedTime = null,
            TimeSpan? actualTime = null,
            double? sliderValueSeconds = null,
            double? sliderMaximumSeconds = null,
            double? elapsedMilliseconds = null,
            bool? indexReady = null,
            bool? usedGlobalIndex = null,
            bool? cacheHit = null,
            bool? requiredReconstruction = null)
        {
            return new RegressionCheckResult(
                filePath,
                scope,
                category,
                name,
                WarningClassification,
                message,
                expectedFrameIndex,
                actualFrameIndex,
                expectedDisplayedFrame,
                actualDisplayedFrame,
                requestedTime.HasValue ? FormatTime(requestedTime.Value) : string.Empty,
                actualTime.HasValue ? FormatTime(actualTime.Value) : string.Empty,
                sliderValueSeconds,
                sliderMaximumSeconds,
                elapsedMilliseconds,
                indexReady,
                usedGlobalIndex,
                cacheHit,
                requiredReconstruction);
        }

        private static RegressionCheckResult Fail(
            string filePath,
            string scope,
            string category,
            string name,
            string message,
            long? expectedFrameIndex = null,
            long? actualFrameIndex = null,
            long? expectedDisplayedFrame = null,
            long? actualDisplayedFrame = null,
            TimeSpan? requestedTime = null,
            TimeSpan? actualTime = null,
            double? sliderValueSeconds = null,
            double? sliderMaximumSeconds = null,
            double? elapsedMilliseconds = null,
            bool? indexReady = null,
            bool? usedGlobalIndex = null,
            bool? cacheHit = null,
            bool? requiredReconstruction = null)
        {
            return new RegressionCheckResult(
                filePath,
                scope,
                category,
                name,
                "fail",
                message,
                expectedFrameIndex,
                actualFrameIndex,
                expectedDisplayedFrame,
                actualDisplayedFrame,
                requestedTime.HasValue ? FormatTime(requestedTime.Value) : string.Empty,
                actualTime.HasValue ? FormatTime(actualTime.Value) : string.Empty,
                sliderValueSeconds,
                sliderMaximumSeconds,
                elapsedMilliseconds,
                indexReady,
                usedGlobalIndex,
                cacheHit,
                requiredReconstruction);
        }

        private static async Task<MeasuredOperation> MeasureAsync(Func<Task> operation)
        {
            var stopwatch = Stopwatch.StartNew();
            await operation().ConfigureAwait(false);
            stopwatch.Stop();
            return new MeasuredOperation(stopwatch.Elapsed);
        }

        private static async Task<MeasuredOperation<T>> MeasureAsync<T>(Func<Task<T>> operation)
        {
            var stopwatch = Stopwatch.StartNew();
            var result = await operation().ConfigureAwait(false);
            stopwatch.Stop();
            return new MeasuredOperation<T>(result, stopwatch.Elapsed);
        }

        private static RegressionSummary BuildSummary(
            RegressionPackagingReport packaging,
            List<RegressionFileReport> fileReports)
        {
            var checks = new List<RegressionCheckResult>();
            if (packaging != null && packaging.Checks != null)
            {
                checks.AddRange(packaging.Checks);
            }

            foreach (var fileReport in fileReports.Where(report => report != null))
            {
                if (fileReport.EngineChecks != null)
                {
                    checks.AddRange(fileReport.EngineChecks);
                }

                if (fileReport.UiChecks != null)
                {
                    checks.AddRange(fileReport.UiChecks);
                }
            }

            return new RegressionSummary(
                fileReports.Count,
                checks.Count,
                checks.Count(check => string.Equals(check.Classification, "pass", StringComparison.OrdinalIgnoreCase)),
                checks.Count(check => string.Equals(check.Classification, WarningClassification, StringComparison.OrdinalIgnoreCase)),
                checks.Count(check => string.Equals(check.Classification, "fail", StringComparison.OrdinalIgnoreCase)));
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

        private static string FormatFrameIndex(long? frameIndex)
        {
            return frameIndex.HasValue
                ? frameIndex.Value.ToString(CultureInfo.InvariantCulture)
                : NoneText;
        }

        private static void Trace(string message)
        {
            var tracePath = DiagnosticTracePath;
            if (string.IsNullOrWhiteSpace(tracePath))
            {
                return;
            }

            try
            {
                File.AppendAllText(
                    tracePath,
                    DateTimeOffset.Now.ToString("o", CultureInfo.InvariantCulture) + " " + message + Environment.NewLine);
            }
            catch
            {
            }
        }

        private static class PackagingRegressionValidator
        {
            public static RegressionPackagingReport Validate(
                string packagedOutputDirectory,
                string packagedArtifactPath,
                string runtimeManifestPath)
            {
                var checks = new List<RegressionCheckResult>();
                var manifest = LoadManifest(runtimeManifestPath);

                if (!Directory.Exists(packagedOutputDirectory))
                {
                    checks.Add(Fail(
                        PackagingScope,
                        PackagingCategory,
                        PackagingCategory,
                        "output-directory-exists",
                        "Packaged output directory was not found: " + packagedOutputDirectory));
                    return new RegressionPackagingReport(
                        packagedOutputDirectory,
                        packagedArtifactPath,
                        Array.Empty<string>(),
                        Array.Empty<string>(),
                        Array.Empty<string>(),
                        StaleRuntimeFiles,
                        checks.ToArray(),
                        "fail");
                }

                var expectedFiles = manifest != null && manifest.Files != null
                    ? manifest.Files.Keys.OrderBy(name => name, StringComparer.OrdinalIgnoreCase).ToArray()
                    : Array.Empty<string>();
                var presentRuntimeFiles = Directory.GetFiles(packagedOutputDirectory, "*.dll")
                    .Select(Path.GetFileName)
                    .Where(name => !string.IsNullOrWhiteSpace(name))
                    .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
                    .ToArray();

                var missingFiles = new List<string>();
                var hashMismatches = new List<string>();
                foreach (var file in expectedFiles)
                {
                    var filePath = Path.Combine(packagedOutputDirectory, file);
                    if (!File.Exists(filePath))
                    {
                        missingFiles.Add(file);
                        continue;
                    }

                    var actualHash = ComputeSha256(filePath);
                    var expectedHash = manifest.Files[file];
                    if (!string.Equals(actualHash, expectedHash, StringComparison.OrdinalIgnoreCase))
                    {
                        hashMismatches.Add(file);
                    }
                }

                checks.Add(missingFiles.Count == 0
                    ? Pass(
                        PackagingScope,
                        PackagingCategory,
                        PackagingCategory,
                        "runtime-files-present",
                        string.Format(
                            CultureInfo.InvariantCulture,
                            "Packaged output contains all {0} expected runtime files.",
                            expectedFiles.Length))
                    : Fail(
                        PackagingScope,
                        PackagingCategory,
                        PackagingCategory,
                        "runtime-files-present",
                        "Packaged output is missing runtime files: " + string.Join(", ", missingFiles)));

                checks.Add(hashMismatches.Count == 0
                    ? Pass(
                        PackagingScope,
                        PackagingCategory,
                        PackagingCategory,
                        "runtime-hashes-match-manifest",
                        "Packaged output runtime hashes match the active runtime manifest.")
                    : Fail(
                        PackagingScope,
                        PackagingCategory,
                        PackagingCategory,
                        "runtime-hashes-match-manifest",
                        "Packaged output runtime hashes do not match the manifest for: " + string.Join(", ", hashMismatches)));

                var staleFiles = StaleRuntimeFiles
                    .Where(file => File.Exists(Path.Combine(packagedOutputDirectory, file)))
                    .OrderBy(file => file, StringComparer.OrdinalIgnoreCase)
                    .ToArray();
                checks.Add(staleFiles.Length == 0
                    ? Pass(
                        PackagingScope,
                        PackagingCategory,
                        PackagingCategory,
                        "no-stale-runtime-dlls",
                        "Packaged output contains no stale FFmpeg 7-era runtime DLLs.")
                    : Fail(
                        PackagingScope,
                        PackagingCategory,
                        PackagingCategory,
                        "no-stale-runtime-dlls",
                        "Packaged output still contains stale runtime DLLs: " + string.Join(", ", staleFiles)));

                var executablePath = Path.Combine(packagedOutputDirectory, "FramePlayer.exe");
                checks.Add(File.Exists(executablePath)
                    ? Pass(
                        PackagingScope,
                        PackagingCategory,
                        PackagingCategory,
                        "packaged-executable-present",
                        "Packaged executable is present.")
                    : Fail(
                        PackagingScope,
                        PackagingCategory,
                        PackagingCategory,
                        "packaged-executable-present",
                        "Packaged executable is missing from the output directory."));

                ValidateZip(packagedArtifactPath, expectedFiles, manifest, checks);

                var classification = checks.Any(check => string.Equals(check.Classification, "fail", StringComparison.OrdinalIgnoreCase))
                    ? "fail"
                    : checks.Any(check => string.Equals(check.Classification, WarningClassification, StringComparison.OrdinalIgnoreCase))
                        ? WarningClassification
                        : "pass";

                return new RegressionPackagingReport(
                    packagedOutputDirectory,
                    packagedArtifactPath,
                    expectedFiles,
                    presentRuntimeFiles,
                    missingFiles.ToArray(),
                    staleFiles,
                    checks.ToArray(),
                    classification);
            }

            private static void ValidateZip(
                string packagedArtifactPath,
                string[] expectedFiles,
                PackagingManifest manifest,
                List<RegressionCheckResult> checks)
            {
                if (!File.Exists(packagedArtifactPath))
                {
                    checks.Add(Fail(
                        PackagingScope,
                        PackagingCategory,
                        PackagingCategory,
                        "artifact-zip-present",
                        "Packaged zip artifact was not found: " + packagedArtifactPath));
                    return;
                }

                checks.Add(Pass(
                    PackagingScope,
                    PackagingCategory,
                    PackagingCategory,
                    "artifact-zip-present",
                    "Packaged zip artifact is present."));

                using (var archive = ZipFile.OpenRead(packagedArtifactPath))
                {
                    var entryLookup = archive.Entries
                        .GroupBy(entry => entry.FullName, StringComparer.OrdinalIgnoreCase)
                        .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);

                    var missingEntries = expectedFiles
                        .Where(file => !entryLookup.ContainsKey(file))
                        .OrderBy(file => file, StringComparer.OrdinalIgnoreCase)
                        .ToArray();
                    checks.Add(missingEntries.Length == 0
                        ? Pass(
                            PackagingScope,
                            PackagingCategory,
                            PackagingCategory,
                            "artifact-zip-runtime-files",
                            "Packaged zip contains the expected runtime DLL set.")
                        : Fail(
                            PackagingScope,
                            PackagingCategory,
                            PackagingCategory,
                            "artifact-zip-runtime-files",
                            "Packaged zip is missing runtime files: " + string.Join(", ", missingEntries)));

                    if (manifest != null && manifest.Files != null)
                    {
                        var zipHashMismatches = new List<string>();
                        foreach (var pair in manifest.Files)
                        {
                            ZipArchiveEntry entry;
                            if (!entryLookup.TryGetValue(pair.Key, out entry))
                            {
                                continue;
                            }

                            using (var stream = entry.Open())
                            {
                                var actualHash = ComputeSha256(stream);
                                if (!string.Equals(actualHash, pair.Value, StringComparison.OrdinalIgnoreCase))
                                {
                                    zipHashMismatches.Add(pair.Key);
                                }
                            }
                        }

                        checks.Add(zipHashMismatches.Count == 0
                            ? Pass(
                                PackagingScope,
                                PackagingCategory,
                                PackagingCategory,
                                "artifact-zip-hashes-match-manifest",
                                "Packaged zip runtime hashes match the active runtime manifest.")
                            : Fail(
                                PackagingScope,
                                PackagingCategory,
                                PackagingCategory,
                                "artifact-zip-hashes-match-manifest",
                                "Packaged zip runtime hashes do not match the manifest for: " + string.Join(", ", zipHashMismatches)));
                    }

                    var staleEntries = archive.Entries
                        .Select(entry => Path.GetFileName(entry.FullName))
                        .Where(name => StaleRuntimeFiles.Contains(name, StringComparer.OrdinalIgnoreCase))
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
                        .ToArray();
                    checks.Add(staleEntries.Length == 0
                        ? Pass(
                            PackagingScope,
                            PackagingCategory,
                            PackagingCategory,
                            "artifact-zip-no-stale-runtime-dlls",
                            "Packaged zip contains no stale FFmpeg 7-era runtime DLLs.")
                        : Fail(
                            PackagingScope,
                            PackagingCategory,
                            PackagingCategory,
                            "artifact-zip-no-stale-runtime-dlls",
                            "Packaged zip still contains stale runtime DLLs: " + string.Join(", ", staleEntries)));
                }
            }

            private static PackagingManifest LoadManifest(string manifestPath)
            {
                using (var stream = File.OpenRead(manifestPath))
                {
                    var serializer = new DataContractJsonSerializer(
                        typeof(PackagingManifest),
                        new DataContractJsonSerializerSettings
                        {
                            UseSimpleDictionaryFormat = true
                        });
                    return serializer.ReadObject(stream) as PackagingManifest;
                }
            }

            private static string ComputeSha256(string filePath)
            {
                using (var stream = File.OpenRead(filePath))
                {
                    return ComputeSha256(stream);
                }
            }

            private static string ComputeSha256(Stream stream)
            {
                using (var hash = System.Security.Cryptography.SHA256.Create())
                {
                    var bytes = hash.ComputeHash(stream);
                    return string.Concat(bytes.Select(value => value.ToString("x2", CultureInfo.InvariantCulture)));
                }
            }
        }

        [DataContract]
        private sealed class PackagingManifest
        {
            [DataMember(Name = "files")]
            public Dictionary<string, string> Files { get; set; }
        }

        private static class MainWindowRegressionHarness
        {
            private static string ResolveCompareCompanionPath(string primaryFilePath)
            {
                if (string.IsNullOrWhiteSpace(primaryFilePath) || !File.Exists(primaryFilePath))
                {
                    return primaryFilePath ?? string.Empty;
                }

                if (!string.Equals(Path.GetExtension(primaryFilePath), ".avi", StringComparison.OrdinalIgnoreCase))
                {
                    return primaryFilePath;
                }

                var directoryPath = Path.GetDirectoryName(primaryFilePath);
                if (string.IsNullOrWhiteSpace(directoryPath) || !Directory.Exists(directoryPath))
                {
                    return primaryFilePath;
                }

                var companionPath = Directory
                    .EnumerateFiles(directoryPath, "*.mp4", SearchOption.TopDirectoryOnly)
                    .Select(Path.GetFullPath)
                    .FirstOrDefault(candidatePath => !string.Equals(candidatePath, primaryFilePath, StringComparison.OrdinalIgnoreCase));
                return string.IsNullOrWhiteSpace(companionPath)
                    ? primaryFilePath
                    : companionPath;
            }

            internal static Task<UiRegressionResult> RunInMainWindowAsync(
                string filePath,
                bool runPlaybackLifecycleChecks,
                string audioInsertionMp3FixturePath,
                CancellationToken cancellationToken)
            {
                var currentApplication = Application.Current;
                if (currentApplication != null)
                {
                    var dispatcher = currentApplication.Dispatcher;
                    if (dispatcher.CheckAccess())
                    {
                        return RunOnUiThreadAsync(filePath, runPlaybackLifecycleChecks, audioInsertionMp3FixturePath, cancellationToken);
                    }

                    return dispatcher.InvokeAsync(
                        () => RunOnUiThreadAsync(filePath, runPlaybackLifecycleChecks, audioInsertionMp3FixturePath, cancellationToken),
                        DispatcherPriority.Normal).Task.Unwrap();
                }

                var completion = new TaskCompletionSource<UiRegressionResult>();
                var thread = new Thread(() =>
                {
                    try
                    {
                        SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(Dispatcher.CurrentDispatcher));
                        if (Application.Current == null)
                        {
                            var application = new Application();
                            application.ShutdownMode = ShutdownMode.OnExplicitShutdown;
                        }

                        RunOnUiThreadAsync(filePath, runPlaybackLifecycleChecks, audioInsertionMp3FixturePath, cancellationToken).ContinueWith(
                            task =>
                            {
                                if (task.IsFaulted)
                                {
                                    completion.TrySetException(task.Exception.InnerExceptions);
                                }
                                else if (task.IsCanceled)
                                {
                                    completion.TrySetCanceled();
                                }
                                else
                                {
                                    completion.TrySetResult(task.Result);
                                }

                                Dispatcher.CurrentDispatcher.BeginInvokeShutdown(DispatcherPriority.Background);
                            },
                            CancellationToken.None,
                            TaskContinuationOptions.None,
                            TaskScheduler.FromCurrentSynchronizationContext());

                        Dispatcher.Run();
                    }
                    catch (Exception ex)
                    {
                        completion.TrySetException(ex);
                    }
                })
                {
                    IsBackground = true,
                    Name = "FramePlayerRegressionUI"
                };

                thread.SetApartmentState(ApartmentState.STA);
                thread.Start();
                return completion.Task;
            }

            private static bool HasPlaybackHeadroom(
                long? currentFrameIndex,
                long indexedFrameCount,
                TimeSpan currentTime,
                TimeSpan duration,
                TimeSpan positionStep)
            {
                if (currentFrameIndex.HasValue && indexedFrameCount > 0L)
                {
                    return currentFrameIndex.Value < indexedFrameCount - 1L;
                }

                var fallbackStep = positionStep > TimeSpan.Zero
                    ? positionStep
                    : TimeSpan.FromMilliseconds(100d);
                return duration - currentTime > fallbackStep;
            }

            private static bool CanRunFullMediaLoopWrapSmoke(TimeSpan duration)
            {
                return duration >= MinimumFullMediaLoopWrapDuration;
            }

            private static bool CanRunPaneLocalLoopCoverage(long indexedFrameCount)
            {
                return indexedFrameCount >= MinimumPaneLocalLoopCoverageFrames;
            }

            private static bool IsPlayingPlaybackState(string playbackStateText)
            {
                return !string.IsNullOrWhiteSpace(playbackStateText) &&
                       playbackStateText.Contains("playing", StringComparison.OrdinalIgnoreCase);
            }

            private static int CountObservedFrameWraps(long[] observedFrameIndices)
            {
                if (observedFrameIndices == null || observedFrameIndices.Length <= 1)
                {
                    return 0;
                }

                var observedWrapCount = 0;
                for (var observationIndex = 1; observationIndex < observedFrameIndices.Length; observationIndex++)
                {
                    if (observedFrameIndices[observationIndex] < observedFrameIndices[observationIndex - 1])
                    {
                        observedWrapCount++;
                    }
                }

                return observedWrapCount;
            }

            private static async Task<UiRegressionResult> RunOnUiThreadAsync(
                string filePath,
                bool runPlaybackLifecycleChecks,
                string audioInsertionMp3FixturePath,
                CancellationToken cancellationToken)
            {
                var checks = new List<RegressionCheckResult>();
                var notes = new List<string>();
                var metrics = new RegressionMetrics();

                MainWindow window = null;
                MainWindowController controller = null;

                try
                {
                    Trace("UI harness creating window: " + filePath);
                    window = new MainWindow
                    {
                        ShowInTaskbar = false,
                        ShowActivated = false,
                        WindowStartupLocation = WindowStartupLocation.Manual,
                        Left = -20000,
                        Top = -20000,
                        Width = 1280,
                        Height = 800,
                        Opacity = 0d
                    };
                    window.Show();
                    await WaitForUiIdleAsync(window.Dispatcher).ConfigureAwait(true);
                    Trace("UI harness window shown: " + filePath);

                    controller = new MainWindowController(window);
                    Trace("UI harness opening media: " + filePath);
                    var open = await MeasureAsync(async () => await controller.OpenAsync(filePath).ConfigureAwait(true)).ConfigureAwait(true);
                    metrics.UiOpenMilliseconds = open.Elapsed.TotalMilliseconds;
                    Trace("UI harness media open complete: " + filePath);

                    var initial = controller.CaptureSnapshot();
                    checks.Add(EvaluateUiSnapshot(
                        filePath,
                        "ui-first-frame",
                        initial,
                        0L,
                        1L,
                        false,
                        metrics.UiOpenMilliseconds));

                    try
                    {
                        Trace("UI harness inspector open: " + filePath);
                        var inspector = await controller.OpenVideoInfoWindowAsync().ConfigureAwait(true);
                        checks.Add(
                            inspector.HasRenderableContent
                                ? Pass(
                                    filePath,
                                    "ui",
                                    CoverageCategory,
                                    "ui-video-info-opens",
                                    string.Format(
                                        CultureInfo.InvariantCulture,
                                        "Video Info opened with heading '{0}', {1} summary fields, {2} video fields, {3} audio fields, and {4} advanced fields.",
                                        inspector.HeadingText,
                                        inspector.SummaryFieldCount,
                                        inspector.VideoFieldCount,
                                        inspector.AudioFieldCount,
                                        inspector.AdvancedFieldCount),
                                    elapsedMilliseconds: inspector.ElapsedMilliseconds)
                                : Fail(
                                    filePath,
                                    "ui",
                                    CoverageCategory,
                                    "ui-video-info-opens",
                                    string.Format(
                                        CultureInfo.InvariantCulture,
                                        "Video Info opened but rendered incomplete content. Heading '{0}', summary={1}, video={2}, audio={3}, audio-empty='{4}', advanced={5}.",
                                        inspector.HeadingText,
                                        inspector.SummaryFieldCount,
                                        inspector.VideoFieldCount,
                                        inspector.AudioFieldCount,
                                        inspector.AudioEmptyMessage,
                                        inspector.AdvancedFieldCount),
                                    elapsedMilliseconds: inspector.ElapsedMilliseconds));
                    }
                    catch (Exception ex)
                    {
                        checks.Add(Fail(
                            filePath,
                            "ui",
                            CoverageCategory,
                            "ui-video-info-opens",
                            "Video Info failed to open cleanly: " + ex.Message));
                    }

                    var primaryMediaInfo = controller.GetPaneMediaInfo(PrimaryPaneId);
                    var singlePaneAudioInsertionState = controller.CaptureAudioInsertionCommandState();
                    var audioInsertionEligible = IsAudioInsertionEligible(filePath, primaryMediaInfo);
                    if (audioInsertionEligible)
                    {
                        checks.Add(singlePaneAudioInsertionState.IsEnabled &&
                                   (singlePaneAudioInsertionState.ToolTip ?? string.Empty).Contains("Replace the reviewed source audio", StringComparison.OrdinalIgnoreCase)
                            ? Pass(
                                filePath,
                                "ui",
                                CoverageCategory,
                                "ui-audio-insertion-single-pane-enabled",
                                "Audio insertion stayed enabled for a loaded single-pane H.264 MP4 source.")
                            : Fail(
                                filePath,
                                "ui",
                                CoverageCategory,
                                "ui-audio-insertion-single-pane-enabled",
                                string.Format(
                                    CultureInfo.InvariantCulture,
                                    "Audio insertion should have been enabled for this H.264 MP4 source. Enabled={0}; tooltip='{1}'.",
                                    singlePaneAudioInsertionState.IsEnabled,
                                    singlePaneAudioInsertionState.ToolTip)));
                    }
                    else
                    {
                        var expectedDisabledTooltip = GetExpectedAudioInsertionDisabledTooltip(filePath, primaryMediaInfo);
                        checks.Add(!singlePaneAudioInsertionState.IsEnabled &&
                                   (singlePaneAudioInsertionState.ToolTip ?? string.Empty).Contains(expectedDisabledTooltip, StringComparison.OrdinalIgnoreCase)
                            ? Pass(
                                filePath,
                                "ui",
                                CoverageCategory,
                                "ui-audio-insertion-ineligible-source-disabled",
                                string.Format(
                                    CultureInfo.InvariantCulture,
                                    "Audio insertion stayed disabled for an ineligible source with tooltip '{0}'.",
                                    singlePaneAudioInsertionState.ToolTip))
                            : Fail(
                                filePath,
                                "ui",
                                CoverageCategory,
                                "ui-audio-insertion-ineligible-source-disabled",
                                string.Format(
                                    CultureInfo.InvariantCulture,
                                    "Audio insertion should have been disabled for this source. Enabled={0}; tooltip='{1}'; expected to mention '{2}'.",
                                    singlePaneAudioInsertionState.IsEnabled,
                                    singlePaneAudioInsertionState.ToolTip,
                                    expectedDisabledTooltip)));
                    }

                    var immediateSliderTarget = controller.GetQuarterDurationTarget();
                    if (immediateSliderTarget > TimeSpan.Zero)
                    {
                        Trace("UI harness pre-index click seek: " + filePath);
                        var preIndexClick = await controller.CommitSliderSeekAsync(ClickSeekInteraction, immediateSliderTarget).ConfigureAwait(true);
                        metrics.UiPreIndexClickMilliseconds = preIndexClick.ElapsedMilliseconds;
                        checks.Add(EvaluateUiFrameTruth(
                            filePath,
                            "ui-pre-index-click-seek",
                            preIndexClick,
                            immediateSliderTarget,
                            true,
                            preIndexClick.ElapsedMilliseconds));

                        await controller.SetSharedLoopMarkerAsync(LoopPlaybackMarkerEndpoint.In).ConfigureAwait(true);
                        var pendingLoopUi = controller.CaptureMainLoopUiSnapshot();
                        var expectsPendingMarker = !preIndexClick.EngineFrameAbsolute;
                        var pendingMarkerHonest = expectsPendingMarker
                            ? pendingLoopUi.IsInPending &&
                              (pendingLoopUi.StatusText ?? string.Empty).Contains("pending", StringComparison.OrdinalIgnoreCase)
                            : !pendingLoopUi.IsInvalid &&
                              !double.IsNaN(pendingLoopUi.InPosition);
                        checks.Add(pendingMarkerHonest
                            ? Pass(
                                filePath,
                                "ui",
                                CoverageCategory,
                                "ui-loop-preindex-marker-honesty",
                                expectsPendingMarker
                                    ? "Loop in stayed visibly pending before the global index was ready."
                                    : "Loop in rendered immediately because the index was already absolute at capture time.")
                            : Fail(
                                filePath,
                                "ui",
                                CoverageCategory,
                                "ui-loop-preindex-marker-honesty",
                                string.Format(
                                    CultureInfo.InvariantCulture,
                                    "Loop in honesty was wrong before the index settled. Status='{0}', in={1}, pending={2}, engine-absolute={3}.",
                                    pendingLoopUi.StatusText,
                                    pendingLoopUi.InPosition,
                                    pendingLoopUi.IsInPending,
                                    preIndexClick.EngineFrameAbsolute)));
                        await controller.ClearLoopPointsAsync().ConfigureAwait(true);
                    }

                    var indexReady = await controller.WaitForIndexReadyUiAsync(IndexReadyTimeout, cancellationToken).ConfigureAwait(true);
                    metrics.UiIndexReadyMilliseconds = indexReady.ElapsedMilliseconds;
                    Trace("UI harness index-ready wait complete: " + filePath + " ready=" + indexReady.Ready.ToString());
                    if (indexReady.Ready)
                    {
                        checks.Add(Pass(
                            filePath,
                            "ui",
                            LifecycleCategory,
                            "ui-background-index-ready",
                            string.Format(
                                CultureInfo.InvariantCulture,
                                "UI observed the global index becoming ready with {0} frames in {1:0.###} ms.",
                                indexReady.IndexedFrameCount,
                                indexReady.ElapsedMilliseconds),
                            elapsedMilliseconds: indexReady.ElapsedMilliseconds,
                            indexReady: true));
                    }
                    else
                    {
                        checks.Add(Warning(
                            filePath,
                            "ui",
                            LifecycleCategory,
                            "ui-background-index-ready",
                            string.Format(
                                CultureInfo.InvariantCulture,
                                "UI never observed a ready global index before timeout ({0:0.###} ms).",
                                indexReady.ElapsedMilliseconds),
                            elapsedMilliseconds: indexReady.ElapsedMilliseconds,
                            indexReady: false));
                    }

                    if (audioInsertionEligible)
                    {
                        var tooling = new FfmpegCliTooling();
                        var syntheticAudioDirectory = Path.Combine(
                            Path.GetTempPath(),
                            "frameplayer-audio-insertion-" + Guid.NewGuid().ToString("N"));
                        Directory.CreateDirectory(syntheticAudioDirectory);
                        try
                        {
                            var sourceDuration = primaryMediaInfo != null && primaryMediaInfo.Duration > TimeSpan.Zero
                                ? primaryMediaInfo.Duration
                                : TimeSpan.FromSeconds(Math.Max(1d, controller.SliderMaximumSeconds));
                            var shortAudioDuration = TimeSpan.FromSeconds(Math.Max(0.2d, sourceDuration.TotalSeconds * 0.25d));
                            var longAudioDuration = sourceDuration + TimeSpan.FromSeconds(Math.Max(0.5d, sourceDuration.TotalSeconds * 0.35d));

                            var wavReplacementPath = Path.Combine(syntheticAudioDirectory, "replacement-short.wav");
                            var wavOutputPath = Path.Combine(syntheticAudioDirectory, "inserted-short.wav-test.mp4");
                            CreateSyntheticAudioTrack(null, wavReplacementPath, shortAudioDuration, false);
                            var wavInsertionResult = await controller.ReplaceAudioTrackAsync(wavReplacementPath, wavOutputPath).ConfigureAwait(true);
                            if (wavInsertionResult == null)
                            {
                                checks.Add(Fail(
                                    filePath,
                                    "ui",
                                    LifecycleCategory,
                                    UiAudioInsertionWavCheckName,
                                    "WAV audio insertion returned no result."));
                            }
                            else if (!wavInsertionResult.Succeeded)
                            {
                                checks.Add(Fail(
                                    filePath,
                                    "ui",
                                    LifecycleCategory,
                                    UiAudioInsertionWavCheckName,
                                    "WAV audio insertion failed: " + wavInsertionResult.Message));
                            }
                            else
                            {
                                var actualDuration = wavInsertionResult.ProbedDuration ?? wavInsertionResult.Plan.VideoDuration;
                                var durationDelta = Math.Abs((actualDuration - wavInsertionResult.Plan.VideoDuration).TotalMilliseconds);
                                var wavSucceeded = File.Exists(wavOutputPath) &&
                                                   wavInsertionResult.ProbedHasAudioStream.GetValueOrDefault(false) &&
                                                   durationDelta <= 250d;
                                checks.Add(wavSucceeded
                                    ? Pass(
                                        filePath,
                                        "ui",
                                        CorrectnessCategory,
                                        UiAudioInsertionWavCheckName,
                                        string.Format(
                                            CultureInfo.InvariantCulture,
                                            "WAV audio insertion produced an MP4 with audio and preserved video duration within tolerance ({0:0.0} ms delta).",
                                            durationDelta))
                                    : Fail(
                                        filePath,
                                        "ui",
                                        CorrectnessCategory,
                                        UiAudioInsertionWavCheckName,
                                        string.Format(
                                            CultureInfo.InvariantCulture,
                                            "WAV audio insertion output did not meet expectations. Exists={0}; probed-audio={1}; duration-delta-ms={2:0.0}.",
                                            File.Exists(wavOutputPath),
                                            wavInsertionResult.ProbedHasAudioStream,
                                            durationDelta)));
                            }

                            var generatedMp3ReplacementPath = Path.Combine(syntheticAudioDirectory, "replacement-long.mp3");
                            var mp3ReplacementPath = !string.IsNullOrWhiteSpace(audioInsertionMp3FixturePath) &&
                                                     File.Exists(audioInsertionMp3FixturePath)
                                ? audioInsertionMp3FixturePath
                                : generatedMp3ReplacementPath;
                            var mp3OutputPath = Path.Combine(syntheticAudioDirectory, "inserted-long.mp3-test.mp4");
                            if (!File.Exists(mp3ReplacementPath))
                            {
                                if (!tooling.IsBundledToolingAvailable)
                                {
                                    Trace("UI harness skipping MP3 audio insertion coverage because no external fixture was available and packaged CLI tools were not available: " + tooling.GetToolAvailabilityMessage());
                                    checks.Add(Warning(
                                        filePath,
                                        "ui",
                                        CoverageCategory,
                                        UiAudioInsertionMp3CheckName,
                                        "MP3 audio insertion coverage was skipped because no usable external MP3 fixture was provided to the packaged regression run, and the packaged output does not include the development FFmpeg CLI tools required for fallback synthesis. " +
                                        tooling.GetToolAvailabilityMessage()));
                                }
                                else
                                {
                                    var toolPaths = tooling.GetRequiredToolPaths();
                                    CreateSyntheticAudioTrack(toolPaths.FfmpegPath, mp3ReplacementPath, longAudioDuration, true);
                                }
                            }

                            if (File.Exists(mp3ReplacementPath))
                            {
                                Trace("UI harness running MP3 audio insertion coverage with fixture: " + mp3ReplacementPath);
                                var mp3InsertionResult = await controller.ReplaceAudioTrackAsync(mp3ReplacementPath, mp3OutputPath).ConfigureAwait(true);
                                if (mp3InsertionResult == null)
                                {
                                    checks.Add(Fail(
                                        filePath,
                                        "ui",
                                        LifecycleCategory,
                                        UiAudioInsertionMp3CheckName,
                                        "MP3 audio insertion returned no result."));
                                }
                                else if (!mp3InsertionResult.Succeeded)
                                {
                                    checks.Add(Fail(
                                        filePath,
                                        "ui",
                                        LifecycleCategory,
                                        UiAudioInsertionMp3CheckName,
                                        "MP3 audio insertion failed: " + mp3InsertionResult.Message));
                                }
                                else
                                {
                                    var actualDuration = mp3InsertionResult.ProbedDuration ?? mp3InsertionResult.Plan.VideoDuration;
                                    var durationDelta = Math.Abs((actualDuration - mp3InsertionResult.Plan.VideoDuration).TotalMilliseconds);
                                    var mp3Succeeded = File.Exists(mp3OutputPath) &&
                                                       mp3InsertionResult.ProbedHasAudioStream.GetValueOrDefault(false) &&
                                                       durationDelta <= 250d;
                                    checks.Add(mp3Succeeded
                                        ? Pass(
                                            filePath,
                                            "ui",
                                            CorrectnessCategory,
                                            UiAudioInsertionMp3CheckName,
                                            string.Format(
                                                CultureInfo.InvariantCulture,
                                                "MP3 audio insertion produced an MP4 with audio and preserved video duration within tolerance ({0:0.0} ms delta).",
                                                durationDelta))
                                        : Fail(
                                            filePath,
                                            "ui",
                                            CorrectnessCategory,
                                            UiAudioInsertionMp3CheckName,
                                            string.Format(
                                                CultureInfo.InvariantCulture,
                                                "MP3 audio insertion output did not meet expectations. Exists={0}; probed-audio={1}; duration-delta-ms={2:0.0}.",
                                                File.Exists(mp3OutputPath),
                                                mp3InsertionResult.ProbedHasAudioStream,
                                                durationDelta)));
                                }
                            }
                        }
                        finally
                        {
                            if (Directory.Exists(syntheticAudioDirectory))
                            {
                                Directory.Delete(syntheticAudioDirectory, true);
                            }
                        }
                    }

                    await controller.SetCompareModeAsync(true).ConfigureAwait(true);
                    var compareModeAudioInsertionState = controller.CaptureAudioInsertionCommandState();
                    checks.Add(!compareModeAudioInsertionState.IsEnabled &&
                               (compareModeAudioInsertionState.ToolTip ?? string.Empty).Contains("two-pane compare mode", StringComparison.OrdinalIgnoreCase)
                        ? Pass(
                            filePath,
                            "ui",
                            CoverageCategory,
                            "ui-audio-insertion-compare-disabled",
                            "Audio insertion stayed disabled with the expected tooltip while two-pane compare mode was enabled.")
                        : Fail(
                            filePath,
                            "ui",
                            CoverageCategory,
                            "ui-audio-insertion-compare-disabled",
                            string.Format(
                                CultureInfo.InvariantCulture,
                                "Audio insertion should have been disabled in two-pane compare mode. Enabled={0}; tooltip='{1}'.",
                                compareModeAudioInsertionState.IsEnabled,
                                compareModeAudioInsertionState.ToolTip)));
                    await controller.SetCompareModeAsync(false).ConfigureAwait(true);

                    if (indexReady.Ready && indexReady.IndexedFrameCount > 0L)
                    {
                        var clickTarget = controller.GetSliderTargetFromRatio(0.35d);
                        Trace("UI harness indexed click seek: " + filePath);
                        var clickSeek = await controller.CommitSliderSeekAsync(ClickSeekInteraction, clickTarget).ConfigureAwait(true);
                        metrics.UiClickSeekMilliseconds = clickSeek.ElapsedMilliseconds;
                        checks.Add(EvaluateUiFrameTruth(
                            filePath,
                            "ui-click-slider-seek",
                            clickSeek,
                            clickTarget,
                            false,
                            clickSeek.ElapsedMilliseconds));
                        checks.AddRange(await controller.RunUiStepRoundTripAsync(filePath, "ui-click-slider-seek", cancellationToken).ConfigureAwait(true));

                        var dragTarget = controller.GetSliderTargetFromRatio(0.72d);
                        Trace("UI harness indexed drag seek: " + filePath);
                        var dragSeek = await controller.DragSliderSeekAsync(dragTarget).ConfigureAwait(true);
                        metrics.UiDragSeekMilliseconds = dragSeek.ElapsedMilliseconds;
                        checks.Add(EvaluateUiFrameTruth(
                            filePath,
                            "ui-drag-slider-seek",
                            dragSeek,
                            dragTarget,
                            false,
                            dragSeek.ElapsedMilliseconds));
                        checks.AddRange(await controller.RunUiStepRoundTripAsync(filePath, "ui-drag-slider-seek", cancellationToken).ConfigureAwait(true));

                        var primaryFullViewport = controller.CapturePaneViewportSnapshot(PrimaryPaneId);
                        await controller.ZoomInFocusedPaneAsync(PrimaryPaneId).ConfigureAwait(true);
                        var primaryShortcutZoomViewport = controller.CapturePaneViewportSnapshot(PrimaryPaneId);
                        var shortcutResetZoomState = controller.CaptureResetZoomCommandState();
                        var primaryShortcutZoomApplied = primaryShortcutZoomViewport != null &&
                                                         primaryShortcutZoomViewport.IsZoomed &&
                                                         (primaryShortcutZoomViewport.SourceCropWidth < primaryFullViewport.SourceCropWidth ||
                                                          primaryShortcutZoomViewport.SourceCropHeight < primaryFullViewport.SourceCropHeight ||
                                                          primaryShortcutZoomViewport.SourceCropX != primaryFullViewport.SourceCropX ||
                                                          primaryShortcutZoomViewport.SourceCropY != primaryFullViewport.SourceCropY) &&
                                                         shortcutResetZoomState.IsEnabled;
                        checks.Add(primaryShortcutZoomApplied
                            ? Pass(
                                filePath,
                                "ui",
                                CoverageCategory,
                                "ui-zoom-shortcut-single-pane-apply",
                                string.Format(
                                    CultureInfo.InvariantCulture,
                                    "Single-pane shortcut zoom applied immediately after load while paused ({0}).",
                                    FormatViewportSnapshot(primaryShortcutZoomViewport)))
                            : Fail(
                                filePath,
                                "ui",
                                CoverageCategory,
                                "ui-zoom-shortcut-single-pane-apply",
                                string.Format(
                                    CultureInfo.InvariantCulture,
                                    "Single-pane shortcut zoom did not apply as expected. Full={0}; zoomed={1}; reset-enabled={2}; reset-tooltip='{3}'.",
                                    FormatViewportSnapshot(primaryFullViewport),
                                    FormatViewportSnapshot(primaryShortcutZoomViewport),
                                    shortcutResetZoomState.IsEnabled,
                                    shortcutResetZoomState.ToolTip)));
                        await controller.ZoomOutFocusedPaneAsync(PrimaryPaneId).ConfigureAwait(true);
                        var primaryShortcutZoomOutViewport = controller.CapturePaneViewportSnapshot(PrimaryPaneId);
                        checks.Add(ViewportSnapshotsMatch(primaryFullViewport, primaryShortcutZoomOutViewport)
                            ? Pass(
                                filePath,
                                "ui",
                                CoverageCategory,
                                "ui-zoom-shortcut-single-pane-out",
                                "Single-pane shortcut zoom-out returned to the full-frame viewport.")
                            : Fail(
                                filePath,
                                "ui",
                                CoverageCategory,
                                "ui-zoom-shortcut-single-pane-out",
                                string.Format(
                                    CultureInfo.InvariantCulture,
                                    "Single-pane shortcut zoom-out did not return to full frame. Expected {0}; actual {1}.",
                                    FormatViewportSnapshot(primaryFullViewport),
                                    FormatViewportSnapshot(primaryShortcutZoomOutViewport))));
                        await controller.SetPaneViewportAsync(PrimaryPaneId, 2.6d, 0.68d, 0.34d).ConfigureAwait(true);
                        var primaryZoomViewport = controller.CapturePaneViewportSnapshot(PrimaryPaneId);
                        var resetZoomState = controller.CaptureResetZoomCommandState();
                        var primaryZoomApplied = primaryZoomViewport != null &&
                                                 primaryZoomViewport.IsZoomed &&
                                                 primaryZoomViewport.SourceCropWidth < primaryFullViewport.SourceCropWidth &&
                                                 primaryZoomViewport.SourceCropHeight < primaryFullViewport.SourceCropHeight &&
                                                 (primaryZoomViewport.SourceCropX != primaryFullViewport.SourceCropX ||
                                                  primaryZoomViewport.SourceCropY != primaryFullViewport.SourceCropY) &&
                                                 resetZoomState.IsEnabled;
                        checks.Add(primaryZoomApplied
                            ? Pass(
                                filePath,
                                "ui",
                                CoverageCategory,
                                "ui-zoom-single-pane-apply",
                                string.Format(
                                    CultureInfo.InvariantCulture,
                                    "Single-pane zoom applied and exposed a cropped viewport snapshot ({0}).",
                                    FormatViewportSnapshot(primaryZoomViewport)))
                            : Fail(
                                filePath,
                                "ui",
                                CoverageCategory,
                                "ui-zoom-single-pane-apply",
                                string.Format(
                                    CultureInfo.InvariantCulture,
                                    "Single-pane zoom did not apply as expected. Full={0}; zoomed={1}; reset-enabled={2}; reset-tooltip='{3}'.",
                                    FormatViewportSnapshot(primaryFullViewport),
                                    FormatViewportSnapshot(primaryZoomViewport),
                                    resetZoomState.IsEnabled,
                                    resetZoomState.ToolTip)));

                        var loopStartTarget = dragSeek.EnginePresentationTime;
                        var loopStartSet = await controller.SetTimelineLoopMarkerAtAsync(null, LoopPlaybackMarkerEndpoint.In, loopStartTarget).ConfigureAwait(true);
                        var loopStartSnapshot = controller.CaptureSnapshot();
                        await controller.StepFrameAsync(12).ConfigureAwait(true);
                        var loopEndSnapshot = controller.CaptureSnapshot();
                        var loopStep = TimeSpan.FromSeconds(Math.Max(loopStartSnapshot.PositionStepSeconds, 0.001d));
                        var blockedMainLoopOutTarget = loopStartSnapshot.EnginePresentationTime > loopStep
                            ? loopStartSnapshot.EnginePresentationTime - loopStep
                            : TimeSpan.Zero;
                        var blockedMainLoopOut = await controller.SetTimelineLoopMarkerAtAsync(
                            null,
                            LoopPlaybackMarkerEndpoint.Out,
                            blockedMainLoopOutTarget).ConfigureAwait(true);
                        checks.Add(!blockedMainLoopOut
                            ? Pass(
                                filePath,
                                "ui",
                                CoverageCategory,
                                "ui-loop-main-timeline-blocks-b-before-a",
                                "Main timeline right-click rejected Position B before Position A.")
                            : Fail(
                                filePath,
                                "ui",
                                CoverageCategory,
                                "ui-loop-main-timeline-blocks-b-before-a",
                                "Main timeline allowed Position B before Position A."));

                        var loopEndSet = await controller.SetTimelineLoopMarkerAtAsync(
                            null,
                            LoopPlaybackMarkerEndpoint.Out,
                            loopEndSnapshot.EnginePresentationTime).ConfigureAwait(true);
                        var sharedLoopUi = await controller.WaitForLoopUiReadyAsync(null, LoopUiReadyTimeout, cancellationToken).ConfigureAwait(true);
                        var sharedLoopReady = loopStartSet &&
                                              loopEndSet &&
                                              !sharedLoopUi.IsInvalid &&
                                              !sharedLoopUi.IsInPending &&
                                              !sharedLoopUi.IsOutPending &&
                                              !double.IsNaN(sharedLoopUi.InPosition) &&
                                              !double.IsNaN(sharedLoopUi.OutPosition) &&
                                              sharedLoopUi.OutPosition >= sharedLoopUi.InPosition;
                        checks.Add(sharedLoopReady
                            ? Pass(
                                filePath,
                                        "ui",
                                        CoverageCategory,
                                        "ui-loop-main-range-renders",
                                        string.Format(
                                            CultureInfo.InvariantCulture,
                                            "Main timeline right-click rendered an exact A/B box from frame {0} to {1}.",
                                            FormatFrameIndex(loopStartSnapshot.EngineFrameIndex),
                                            FormatFrameIndex(loopEndSnapshot.EngineFrameIndex)))
                            : Fail(
                                filePath,
                                "ui",
                                CoverageCategory,
                                "ui-loop-main-range-renders",
                                string.Format(
                                    CultureInfo.InvariantCulture,
                                    "Main transport loop UI did not render a ready exact range. Status='{0}', in={1}, out={2}, pending-in={3}, pending-out={4}, invalid={5}.",
                                    sharedLoopUi.StatusText,
                                    sharedLoopUi.InPosition,
                                    sharedLoopUi.OutPosition,
                                    sharedLoopUi.IsInPending,
                                    sharedLoopUi.IsOutPending,
                                    sharedLoopUi.IsInvalid)));

                        var loopPlaybackEnabled = false;
                        var mainExportOutputPath = Path.Combine(
                            Path.GetTempPath(),
                            "frameplayer-loop-export-main-" + Guid.NewGuid().ToString("N") + ".mp4");
                        try
                        {
                            if (runPlaybackLifecycleChecks)
                            {
                                var playbackStart = controller.CaptureSnapshot();
                                if (!HasPlaybackHeadroom(
                                        playbackStart.EngineFrameIndex,
                                        indexReady.IndexedFrameCount,
                                        playbackStart.EnginePresentationTime,
                                        TimeSpan.FromSeconds(playbackStart.SliderMaximumSeconds),
                                        TimeSpan.FromSeconds(Math.Max(playbackStart.PositionStepSeconds, 0d))))
                                {
                                    var playbackSmokeTarget = controller.GetQuarterDurationTarget();
                                    if (playbackSmokeTarget > TimeSpan.Zero)
                                    {
                                        Trace("UI harness playback smoke reposition: " + filePath);
                                        playbackStart = await controller.CommitSliderSeekAsync(ClickSeekInteraction, playbackSmokeTarget).ConfigureAwait(true);
                                    }
                                }

                                var canMeasurePlaybackProgress = HasPlaybackHeadroom(
                                    playbackStart.EngineFrameIndex,
                                    indexReady.IndexedFrameCount,
                                    playbackStart.EnginePresentationTime,
                                    TimeSpan.FromSeconds(playbackStart.SliderMaximumSeconds),
                                    TimeSpan.FromSeconds(Math.Max(playbackStart.PositionStepSeconds, 0d)));
                                if (canMeasurePlaybackProgress)
                                {
                                    Trace("UI harness playback start: " + filePath);
                                    await controller.StartPlaybackAsync().ConfigureAwait(true);
                                    await Task.Delay(PlaybackDelay, cancellationToken).ConfigureAwait(true);
                                    await controller.PausePlaybackAsync().ConfigureAwait(true);
                                    Trace("UI harness playback pause: " + filePath);
                                    var playbackPaused = controller.CaptureSnapshot();
                                    var playbackAdvanced = playbackStart.EngineFrameIndex.HasValue &&
                                        playbackPaused.EngineFrameIndex.HasValue &&
                                        playbackPaused.EngineFrameIndex.Value > playbackStart.EngineFrameIndex.Value;
                                    checks.Add(playbackAdvanced
                                        ? Pass(
                                            filePath,
                                            "ui",
                                            LifecycleCategory,
                                            "ui-playback-pause-progress",
                                            string.Format(
                                                CultureInfo.InvariantCulture,
                                                "UI playback advanced from frame {0} to frame {1} before pause.",
                                                playbackStart.EngineFrameIndex.Value,
                                                playbackPaused.EngineFrameIndex.Value))
                                        : Fail(
                                            filePath,
                                            "ui",
                                            LifecycleCategory,
                                            "ui-playback-pause-progress",
                                            string.Format(
                                                CultureInfo.InvariantCulture,
                                                "UI playback did not advance before pause. Start frame {0}, paused frame {1}.",
                                                FormatFrameIndex(playbackStart.EngineFrameIndex),
                                                FormatFrameIndex(playbackPaused.EngineFrameIndex)),
                                            actualFrameIndex: playbackPaused.EngineFrameIndex));
                                }
                                else
                                {
                                    checks.Add(Warning(
                                        filePath,
                                        "ui",
                                        CoverageCategory,
                                        "ui-playback-pause-progress-skipped",
                                        "Timed playback smoke was skipped because the post-loop setup position had no forward headroom left in this short clip."));
                                }

                                var postPlaybackTarget = controller.GetSliderTargetFromRatio(0.5d);
                                Trace("UI harness post-playback seek: " + filePath);
                                var postPlaybackSeek = await controller.CommitSliderSeekAsync(ClickSeekInteraction, postPlaybackTarget).ConfigureAwait(true);
                                checks.Add(EvaluateUiFrameTruth(
                                    filePath,
                                    "ui-post-playback-slider-seek",
                                    postPlaybackSeek,
                                    postPlaybackTarget,
                                    false,
                                    postPlaybackSeek.ElapsedMilliseconds));
                                checks.AddRange(await controller.RunUiStepRoundTripAsync(filePath, "ui-post-playback-slider-seek", cancellationToken).ConfigureAwait(true));

                                var primaryViewportAfterPlayback = controller.CapturePaneViewportSnapshot(PrimaryPaneId);
                                checks.Add(ViewportSnapshotsMatch(primaryZoomViewport, primaryViewportAfterPlayback)
                                    ? Pass(
                                        filePath,
                                        "ui",
                                        CorrectnessCategory,
                                        "ui-zoom-single-pane-persists",
                                        "Single-pane zoom stayed intact through play, pause, seek, and frame-step operations.")
                                    : Fail(
                                        filePath,
                                        "ui",
                                        CorrectnessCategory,
                                        "ui-zoom-single-pane-persists",
                                        string.Format(
                                            CultureInfo.InvariantCulture,
                                            "Single-pane zoom changed after playback operations. Expected {0}; actual {1}.",
                                            FormatViewportSnapshot(primaryZoomViewport),
                                            FormatViewportSnapshot(primaryViewportAfterPlayback))));

                                await controller.SetLoopPlaybackEnabledAsync(true).ConfigureAwait(true);
                                loopPlaybackEnabled = true;
                                var loopEntrySeek = await controller.CommitSliderSeekAsync(ClickSeekInteraction, loopStartSnapshot.EnginePresentationTime).ConfigureAwait(true);
                                var loopEntryLandedInsideRange = loopStartSnapshot.EngineFrameIndex.HasValue &&
                                                                 loopEndSnapshot.EngineFrameIndex.HasValue &&
                                                                 loopEntrySeek.EngineFrameIndex.HasValue &&
                                                                 loopEntrySeek.EngineFrameIndex.Value >= loopStartSnapshot.EngineFrameIndex.Value &&
                                                                 loopEntrySeek.EngineFrameIndex.Value <= loopEndSnapshot.EngineFrameIndex.Value;
                                checks.Add(loopEntryLandedInsideRange
                                    ? Pass(
                                        filePath,
                                        "ui",
                                        LifecycleCategory,
                                        "ui-loop-main-entry-seek",
                                        string.Format(
                                            CultureInfo.InvariantCulture,
                                            "Loop entry seek landed inside the boxed frame range {0}..{1} before timed playback.",
                                            loopStartSnapshot.EngineFrameIndex.Value,
                                            loopEndSnapshot.EngineFrameIndex.Value),
                                        actualFrameIndex: loopEntrySeek.EngineFrameIndex)
                                    : Fail(
                                        filePath,
                                        "ui",
                                        LifecycleCategory,
                                        "ui-loop-main-entry-seek",
                                        string.Format(
                                            CultureInfo.InvariantCulture,
                                            "Loop entry seek did not land inside the boxed frame range {0}..{1}. Landed at frame {2}.",
                                            FormatFrameIndex(loopStartSnapshot.EngineFrameIndex),
                                            FormatFrameIndex(loopEndSnapshot.EngineFrameIndex),
                                            FormatFrameIndex(loopEntrySeek.EngineFrameIndex)),
                                        actualFrameIndex: loopEntrySeek.EngineFrameIndex));
                                Trace("UI harness loop playback smoke: " + filePath);
                                Trace("UI harness loop playback start request: " + filePath);
                                await controller.StartPlaybackAsync().ConfigureAwait(true);
                                Trace("UI harness loop playback start complete: " + filePath);
                                var loopPlaybackObservations = (await controller.CapturePlaybackObservationsAsync(
                                        8,
                                        TimeSpan.FromMilliseconds(250d),
                                        cancellationToken)
                                    .ConfigureAwait(true)).ToList();
                                Trace("UI harness loop playback observation window complete: " + filePath);

                                var observedFrameIndices = loopPlaybackObservations
                                    .Where(snapshot => snapshot.EngineFrameIndex.HasValue)
                                    .Select(snapshot => snapshot.EngineFrameIndex.Value)
                                    .ToArray();
                                var observedWrapCount = CountObservedFrameWraps(observedFrameIndices);
                                for (var recoveryIndex = 0; recoveryIndex < 2; recoveryIndex++)
                                {
                                    var canCaptureRecoverySample = observedWrapCount >= 2 &&
                                                                   loopStartSnapshot.EngineFrameIndex.HasValue &&
                                                                   loopEndSnapshot.EngineFrameIndex.HasValue &&
                                                                   loopPlaybackObservations.Count > 0 &&
                                                                   loopPlaybackObservations.Count(snapshot => !IsPlayingPlaybackState(snapshot.PlaybackStateText)) <= 1;
                                    if (canCaptureRecoverySample)
                                    {
                                        var lastObservation = loopPlaybackObservations[loopPlaybackObservations.Count - 1];
                                        canCaptureRecoverySample = lastObservation != null &&
                                                                   !IsPlayingPlaybackState(lastObservation.PlaybackStateText) &&
                                                                   lastObservation.EngineFrameIndex.HasValue &&
                                                                   lastObservation.EngineFrameIndex.Value >= loopStartSnapshot.EngineFrameIndex.Value &&
                                                                   lastObservation.EngineFrameIndex.Value <= loopEndSnapshot.EngineFrameIndex.Value &&
                                                                   lastObservation.EngineFrameIndex.Value - loopStartSnapshot.EngineFrameIndex.Value <= 1L;
                                    }

                                    if (!canCaptureRecoverySample)
                                    {
                                        break;
                                    }

                                    loopPlaybackObservations.Add(
                                        await controller.CaptureSnapshotAfterDelayAsync(
                                                LoopPlaybackRecoveryDelay,
                                                cancellationToken)
                                            .ConfigureAwait(true));
                                    observedFrameIndices = loopPlaybackObservations
                                        .Where(snapshot => snapshot.EngineFrameIndex.HasValue)
                                        .Select(snapshot => snapshot.EngineFrameIndex.Value)
                                        .ToArray();
                                    observedWrapCount = CountObservedFrameWraps(observedFrameIndices);
                                }

                                Trace("UI harness loop playback pause request: " + filePath);
                                await controller.PausePlaybackAsync().ConfigureAwait(true);
                                Trace("UI harness loop playback pause complete: " + filePath);
                                var loopPlaybackSnapshot = controller.CaptureSnapshot();
                                var loopStayedWithinRange = loopStartSnapshot.EngineFrameIndex.HasValue &&
                                                            loopEndSnapshot.EngineFrameIndex.HasValue &&
                                                            loopPlaybackSnapshot.EngineFrameIndex.HasValue &&
                                                            loopPlaybackSnapshot.EngineFrameIndex.Value >= loopStartSnapshot.EngineFrameIndex.Value &&
                                                            loopPlaybackSnapshot.EngineFrameIndex.Value <= loopEndSnapshot.EngineFrameIndex.Value;
                                var observationsStayedWithinRange = loopStartSnapshot.EngineFrameIndex.HasValue &&
                                                                   loopEndSnapshot.EngineFrameIndex.HasValue &&
                                                                   loopPlaybackObservations.All(
                                                                       snapshot => snapshot.EngineFrameIndex.HasValue &&
                                                                                   snapshot.EngineFrameIndex.Value >= loopStartSnapshot.EngineFrameIndex.Value &&
                                                                                   snapshot.EngineFrameIndex.Value <= loopEndSnapshot.EngineFrameIndex.Value);
                                var observedNonPlayingStates = loopPlaybackObservations
                                    .Select(snapshot => snapshot.PlaybackStateText ?? string.Empty)
                                    .Where(state => !IsPlayingPlaybackState(state))
                                    .ToArray();
                                var remainedPlayingThroughWrap = loopPlaybackObservations.Count > 0 &&
                                                                observedNonPlayingStates.Length <= 1 &&
                                                                IsPlayingPlaybackState(loopPlaybackObservations[loopPlaybackObservations.Count - 1].PlaybackStateText);
                                var observedDistinctFrames = observedFrameIndices.Distinct().Count();
                                var observedFrameSummary = observedFrameIndices.Length > 0
                                    ? string.Join(", ", observedFrameIndices.Select(frame => FormatFrameIndex(frame)))
                                    : "<none>";
                                checks.Add(loopStayedWithinRange
                                    ? Pass(
                                        filePath,
                                        "ui",
                                        LifecycleCategory,
                                        "ui-loop-main-playback-bounded",
                                        string.Format(
                                            CultureInfo.InvariantCulture,
                                            "Loop playback stayed inside the boxed frame range {0}..{1} after timed playback.",
                                            loopStartSnapshot.EngineFrameIndex.Value,
                                            loopEndSnapshot.EngineFrameIndex.Value),
                                        actualFrameIndex: loopPlaybackSnapshot.EngineFrameIndex)
                                    : Fail(
                                        filePath,
                                        "ui",
                                        LifecycleCategory,
                                        "ui-loop-main-playback-bounded",
                                        string.Format(
                                            CultureInfo.InvariantCulture,
                                            "Loop playback escaped the boxed range {0}..{1}. Paused at frame {2}.",
                                            FormatFrameIndex(loopStartSnapshot.EngineFrameIndex),
                                            FormatFrameIndex(loopEndSnapshot.EngineFrameIndex),
                                            FormatFrameIndex(loopPlaybackSnapshot.EngineFrameIndex)),
                                        actualFrameIndex: loopPlaybackSnapshot.EngineFrameIndex));
                                if (CanRunFullMediaLoopWrapSmoke(TimeSpan.FromSeconds(controller.SliderMaximumSeconds)))
                                {
                                    checks.Add(observationsStayedWithinRange && remainedPlayingThroughWrap && observedDistinctFrames >= 2 && observedWrapCount >= 2
                                        ? Pass(
                                            filePath,
                                            "ui",
                                            LifecycleCategory,
                                            "ui-loop-main-playback-multiwrap",
                                            string.Format(
                                                CultureInfo.InvariantCulture,
                                                "Loop playback stayed active through {0} wraps with observed frames {1}.",
                                                observedWrapCount,
                                                observedFrameSummary))
                                        : Fail(
                                            filePath,
                                            "ui",
                                            LifecycleCategory,
                                            "ui-loop-main-playback-multiwrap",
                                            string.Format(
                                                CultureInfo.InvariantCulture,
                                                "Loop playback did not stay active across repeated wraps. States={0}; frames={1}; within-range={2}; wraps={3}.",
                                                string.Join(", ", loopPlaybackObservations.Select(snapshot => snapshot.PlaybackStateText ?? string.Empty)),
                                                observedFrameSummary,
                                                observationsStayedWithinRange,
                                                observedWrapCount)));
                                }
                                else
                                {
                                    checks.Add(Warning(
                                        filePath,
                                        "ui",
                                        CoverageCategory,
                                        "ui-loop-main-playback-multiwrap-skipped",
                                        "Main-loop repeated-wrap smoke was skipped because this clip is too short to observe a meaningful repeated wrap sequence."));
                                }
                            }
                            else
                            {
                                checks.Add(Warning(
                                    filePath,
                                    "ui",
                                    CoverageCategory,
                                    "ui-playback-lifecycle-skipped",
                                    "Timed playback was skipped in the hidden-window UI harness for this audio-bearing corpus file. Engine-level playback and audio checks still ran."));
                            }

                            if (!sharedLoopReady)
                            {
                                checks.Add(Warning(
                                    filePath,
                                    "ui",
                                    CoverageCategory,
                                    "ui-loop-export-main-skipped",
                                    "Main-loop clip export was skipped because the reviewed A/B range never settled into a ready exact box."));
                            }
                            else
                            {
                                var mainExportResult = await controller.ExportLoopClipAsync(mainExportOutputPath).ConfigureAwait(true);
                                if (mainExportResult == null)
                                {
                                    checks.Add(Warning(
                                        filePath,
                                        "ui",
                                        CoverageCategory,
                                        "ui-loop-export-main-skipped",
                                        "Main-loop clip export was skipped because the export runtime was unavailable or the loop state changed before export."));
                                }
                                else if (!mainExportResult.Succeeded)
                                {
                                    checks.Add(Fail(
                                        filePath,
                                        "ui",
                                        LifecycleCategory,
                                        "ui-loop-export-main",
                                        "Main-loop clip export failed: " + mainExportResult.Message));
                                }
                                else
                                {
                                    checks.Add(File.Exists(mainExportOutputPath)
                                        ? Pass(
                                            filePath,
                                            "ui",
                                            LifecycleCategory,
                                            "ui-loop-export-main",
                                            "Main-loop clip export produced an MP4 output file.")
                                        : Fail(
                                            filePath,
                                            "ui",
                                            LifecycleCategory,
                                            "ui-loop-export-main",
                                            "Main-loop clip export reported success but did not produce the expected output file."));

                                    var mainExportZoomed = mainExportResult.Plan != null &&
                                                          mainExportResult.Plan.ViewportSnapshot != null &&
                                                          mainExportResult.Plan.ViewportSnapshot.IsZoomed &&
                                                          ViewportSnapshotsMatch(primaryZoomViewport, mainExportResult.Plan.ViewportSnapshot);
                                    checks.Add(mainExportZoomed
                                        ? Pass(
                                            filePath,
                                            "ui",
                                            CorrectnessCategory,
                                            "ui-loop-export-main-zoomed",
                                            "Main-loop clip export carried the current zoomed viewport into the export plan.")
                                        : Fail(
                                            filePath,
                                            "ui",
                                            CorrectnessCategory,
                                            "ui-loop-export-main-zoomed",
                                            string.Format(
                                                CultureInfo.InvariantCulture,
                                                "Main-loop clip export did not preserve the active zoomed viewport. Expected {0}; actual {1}.",
                                                FormatViewportSnapshot(primaryZoomViewport),
                                                FormatViewportSnapshot(mainExportResult.Plan != null ? mainExportResult.Plan.ViewportSnapshot : null))));

                                    var expectedDuration = mainExportResult.Plan != null
                                        ? mainExportResult.Plan.Duration
                                        : TimeSpan.Zero;
                                    var actualDuration = mainExportResult.ProbedDuration ?? expectedDuration;
                                    var durationDelta = Math.Abs((actualDuration - expectedDuration).TotalMilliseconds);
                                    checks.Add(durationDelta <= 250d
                                        ? Pass(
                                            filePath,
                                            "ui",
                                            CorrectnessCategory,
                                            "ui-loop-export-main-duration",
                                            string.Format(
                                                CultureInfo.InvariantCulture,
                                                "Main-loop clip export duration matched the reviewed A/B range within tolerance ({0:0.0} ms delta).",
                                                durationDelta))
                                        : Fail(
                                            filePath,
                                            "ui",
                                            CorrectnessCategory,
                                            "ui-loop-export-main-duration",
                                            string.Format(
                                                CultureInfo.InvariantCulture,
                                                "Main-loop clip export duration drifted from the reviewed A/B range by {0:0.0} ms.",
                                                durationDelta)));
                                }
                            }

                            await controller.ClearLoopPointsAsync().ConfigureAwait(true);
                            if (CanRunFullMediaLoopWrapSmoke(TimeSpan.FromSeconds(controller.SliderMaximumSeconds)))
                            {
                                var fullMediaLeadSeconds = Math.Max(0.25d, Math.Min(0.75d, controller.SliderMaximumSeconds * 0.05d));
                                var fullMediaEntryTarget = TimeSpan.FromSeconds(Math.Max(0d, controller.SliderMaximumSeconds - fullMediaLeadSeconds));
                                Trace("UI harness full-media loop playback smoke: " + filePath);
                                var fullMediaEntrySeek = await controller.CommitSliderSeekAsync(ClickSeekInteraction, fullMediaEntryTarget).ConfigureAwait(true);
                                checks.Add(EvaluateUiFrameTruth(
                                    filePath,
                                    "ui-loop-full-media-entry-seek",
                                    fullMediaEntrySeek,
                                    fullMediaEntryTarget,
                                    false,
                                    fullMediaEntrySeek.ElapsedMilliseconds));
                                await controller.StartPlaybackAsync().ConfigureAwait(true);
                                var fullMediaLoopObservations = (await controller.CapturePlaybackObservationsAsync(
                                        6,
                                        TimeSpan.FromMilliseconds(250d),
                                        cancellationToken)
                                    .ConfigureAwait(true)).ToList();
                                await controller.PausePlaybackAsync().ConfigureAwait(true);
                                var fullMediaObservedPositions = fullMediaLoopObservations
                                    .Select(snapshot => snapshot.EnginePresentationTime.TotalSeconds)
                                    .ToArray();
                                var fullMediaObservedFrameIndices = fullMediaLoopObservations
                                    .Where(snapshot => snapshot.EngineFrameIndex.HasValue)
                                    .Select(snapshot => snapshot.EngineFrameIndex.Value)
                                    .ToArray();
                                var fullMediaWrappedBeforeFirstSample = fullMediaObservedPositions.Length > 0 &&
                                                                       fullMediaObservedPositions[0] + 0.010d < fullMediaEntryTarget.TotalSeconds;
                                var fullMediaWrapCount = fullMediaWrappedBeforeFirstSample ? 1 : 0;
                                for (var observationIndex = 1; observationIndex < fullMediaObservedPositions.Length; observationIndex++)
                                {
                                    if (fullMediaObservedPositions[observationIndex] + 0.010d < fullMediaObservedPositions[observationIndex - 1])
                                    {
                                        fullMediaWrapCount++;
                                    }
                                }

                                var fullMediaNonPlayingStates = fullMediaLoopObservations
                                    .Select(snapshot => snapshot.PlaybackStateText ?? string.Empty)
                                    .Where(state => !state.Contains("playing", StringComparison.OrdinalIgnoreCase))
                                    .ToArray();
                                var fullMediaResumedAfterWrap = fullMediaLoopObservations.Count > 0 &&
                                                                (fullMediaLoopObservations[fullMediaLoopObservations.Count - 1].PlaybackStateText ?? string.Empty).Contains("playing", StringComparison.OrdinalIgnoreCase) &&
                                                                fullMediaObservedPositions.Length > 0 &&
                                                                fullMediaObservedPositions[fullMediaObservedPositions.Length - 1] > 0.10d;
                                var fullMediaFramesMoved = fullMediaObservedFrameIndices.Distinct().Count() >= 2;
                                var fullMediaFrameSummary = fullMediaObservedFrameIndices.Length > 0
                                    ? string.Join(", ", fullMediaObservedFrameIndices.Select(frame => FormatFrameIndex(frame)))
                                    : "<none>";
                                checks.Add(fullMediaWrapCount >= 1 && fullMediaResumedAfterWrap && fullMediaFramesMoved && fullMediaNonPlayingStates.Length <= 1
                                    ? Pass(
                                        filePath,
                                        "ui",
                                        LifecycleCategory,
                                        "ui-loop-full-media-playback-wrap",
                                        string.Format(
                                            CultureInfo.InvariantCulture,
                                            "Full-media loop playback restarted across the end boundary with positions {0}.",
                                            string.Join(", ", fullMediaObservedPositions.Select(position => position.ToString("0.000", CultureInfo.InvariantCulture)))))
                                    : Fail(
                                        filePath,
                                        "ui",
                                        LifecycleCategory,
                                        "ui-loop-full-media-playback-wrap",
                                        string.Format(
                                            CultureInfo.InvariantCulture,
                                            "Full-media loop playback did not restart cleanly. States={0}; positions={1}; frames={2}; wraps={3}; resumed={4}.",
                                            string.Join(", ", fullMediaLoopObservations.Select(snapshot => snapshot.PlaybackStateText ?? string.Empty)),
                                            string.Join(", ", fullMediaObservedPositions.Select(position => position.ToString("0.000", CultureInfo.InvariantCulture))),
                                            fullMediaFrameSummary,
                                            fullMediaWrapCount,
                                            fullMediaResumedAfterWrap)));
                            }
                            else
                            {
                                checks.Add(Warning(
                                    filePath,
                                    "ui",
                                    CoverageCategory,
                                    "ui-loop-full-media-playback-skipped",
                                    "Full-media loop playback smoke was skipped because this clip is too short to observe a meaningful end-boundary wrap sequence."));
                            }
                        }
                        finally
                            {
                                if (loopPlaybackEnabled)
                                {
                                    await controller.SetLoopPlaybackEnabledAsync(false).ConfigureAwait(true);
                                }

                                await controller.ClearLoopPointsAsync().ConfigureAwait(true);

                                if (File.Exists(mainExportOutputPath))
                                {
                                    File.Delete(mainExportOutputPath);
                                }
                            }

                        await controller.ResetPaneZoomAsync(PrimaryPaneId).ConfigureAwait(true);
                        var primaryResetViewport = controller.CapturePaneViewportSnapshot(PrimaryPaneId);
                        checks.Add(primaryResetViewport != null && !primaryResetViewport.IsZoomed
                            ? Pass(
                                filePath,
                                "ui",
                                CorrectnessCategory,
                                "ui-zoom-single-pane-reset",
                                "Reset Zoom returned the primary pane to the full-frame view.")
                            : Fail(
                                filePath,
                                "ui",
                                CorrectnessCategory,
                                "ui-zoom-single-pane-reset",
                                string.Format(
                                    CultureInfo.InvariantCulture,
                                    "Reset Zoom did not restore the primary pane to the full frame. Actual {0}.",
                                    FormatViewportSnapshot(primaryResetViewport))));

                        if (CanRunPaneLocalLoopCoverage(indexReady.IndexedFrameCount))
                        {
                            var compareCompanionPath = ResolveCompareCompanionPath(filePath);
                            await controller.SetCompareModeAsync(true).ConfigureAwait(true);
                            await controller.OpenAsync(compareCompanionPath, ComparePaneId).ConfigureAwait(true);
                            var primaryCompareFullViewport = controller.CapturePaneViewportSnapshot(PrimaryPaneId);
                            var compareFullViewport = controller.CapturePaneViewportSnapshot(ComparePaneId);
                            var linkedZoomDefaultState = controller.CaptureLinkPaneZoomCommandState();
                            checks.Add(linkedZoomDefaultState.IsEnabled && linkedZoomDefaultState.IsChecked
                                ? Pass(
                                    filePath,
                                    "ui",
                                    CoverageCategory,
                                    "ui-zoom-compare-link-default-on",
                                    "Compare pane Link Zoom was enabled by default.")
                                : Fail(
                                    filePath,
                                    "ui",
                                    CoverageCategory,
                                    "ui-zoom-compare-link-default-on",
                                    string.Format(
                                        CultureInfo.InvariantCulture,
                                        "Compare pane Link Zoom should be enabled by default. Enabled={0}; checked={1}; tooltip='{2}'.",
                                        linkedZoomDefaultState.IsEnabled,
                                        linkedZoomDefaultState.IsChecked,
                                        linkedZoomDefaultState.ToolTip)));
                            await controller.ZoomInFocusedPaneAsync(PrimaryPaneId).ConfigureAwait(true);
                            var primaryShortcutCompareZoomViewport = controller.CapturePaneViewportSnapshot(PrimaryPaneId);
                            var compareViewportAfterPrimaryShortcut = controller.CapturePaneViewportSnapshot(ComparePaneId);
                            var primaryShortcutLinked = primaryShortcutCompareZoomViewport != null &&
                                                        compareViewportAfterPrimaryShortcut != null &&
                                                        primaryShortcutCompareZoomViewport.IsZoomed &&
                                                        compareViewportAfterPrimaryShortcut.IsZoomed &&
                                                        !ViewportSnapshotsMatch(compareFullViewport, compareViewportAfterPrimaryShortcut) &&
                                                        ViewportZoomAndCenterMatch(primaryShortcutCompareZoomViewport, compareViewportAfterPrimaryShortcut);
                            checks.Add(primaryShortcutLinked
                                ? Pass(
                                    filePath,
                                    "ui",
                                    CoverageCategory,
                                    "ui-zoom-shortcut-compare-primary-linked",
                                    "Focused-pane zoom shortcut mirrored the primary pane viewport to the compare pane while Link Zoom was enabled.")
                                : Fail(
                                    filePath,
                                    "ui",
                                    CoverageCategory,
                                    "ui-zoom-shortcut-compare-primary-linked",
                                    string.Format(
                                        CultureInfo.InvariantCulture,
                                        "Primary focused zoom shortcut should have linked both compare panes. Primary full={0}; primary zoomed={1}; compare full={2}; compare actual={3}.",
                                        FormatViewportSnapshot(primaryCompareFullViewport),
                                        FormatViewportSnapshot(primaryShortcutCompareZoomViewport),
                                        FormatViewportSnapshot(compareFullViewport),
                                        FormatViewportSnapshot(compareViewportAfterPrimaryShortcut))));
                            await controller.ZoomInFocusedPaneAsync(ComparePaneId).ConfigureAwait(true);
                            var compareShortcutZoomViewport = controller.CapturePaneViewportSnapshot(ComparePaneId);
                            var primaryViewportAfterCompareShortcut = controller.CapturePaneViewportSnapshot(PrimaryPaneId);
                            var compareShortcutLinked = compareShortcutZoomViewport != null &&
                                                        primaryViewportAfterCompareShortcut != null &&
                                                        compareShortcutZoomViewport.IsZoomed &&
                                                        primaryViewportAfterCompareShortcut.IsZoomed &&
                                                        !ViewportSnapshotsMatch(primaryShortcutCompareZoomViewport, primaryViewportAfterCompareShortcut) &&
                                                        ViewportZoomAndCenterMatch(compareShortcutZoomViewport, primaryViewportAfterCompareShortcut);
                            checks.Add(compareShortcutLinked
                                ? Pass(
                                    filePath,
                                    "ui",
                                    CoverageCategory,
                                    "ui-zoom-shortcut-compare-compare-linked",
                                    "Focused-pane zoom shortcut mirrored the compare pane viewport to the primary pane while Link Zoom was enabled.")
                                : Fail(
                                    filePath,
                                    "ui",
                                    CoverageCategory,
                                    "ui-zoom-shortcut-compare-compare-linked",
                                    string.Format(
                                        CultureInfo.InvariantCulture,
                                        "Compare focused zoom shortcut should have linked both compare panes. Compare full={0}; compare zoomed={1}; primary before={2}; primary actual={3}.",
                                        FormatViewportSnapshot(compareFullViewport),
                                        FormatViewportSnapshot(compareShortcutZoomViewport),
                                        FormatViewportSnapshot(primaryShortcutCompareZoomViewport),
                                        FormatViewportSnapshot(primaryViewportAfterCompareShortcut))));
                            await controller.SetLinkedPaneZoomAsync(false).ConfigureAwait(true);
                            var linkedZoomDisabledState = controller.CaptureLinkPaneZoomCommandState();
                            checks.Add(linkedZoomDisabledState.IsEnabled && !linkedZoomDisabledState.IsChecked
                                ? Pass(
                                    filePath,
                                    "ui",
                                    CoverageCategory,
                                    "ui-zoom-compare-link-can-disable",
                                    "Compare pane Link Zoom could be disabled for independent viewport review.")
                                : Fail(
                                    filePath,
                                    "ui",
                                    CoverageCategory,
                                    "ui-zoom-compare-link-can-disable",
                                    string.Format(
                                        CultureInfo.InvariantCulture,
                                        "Compare pane Link Zoom should have been disabled. Enabled={0}; checked={1}; tooltip='{2}'.",
                                        linkedZoomDisabledState.IsEnabled,
                                        linkedZoomDisabledState.IsChecked,
                                        linkedZoomDisabledState.ToolTip)));
                            await controller.SetPaneViewportAsync(PrimaryPaneId, 2.2d, 0.34d, 0.46d).ConfigureAwait(true);
                            await controller.SetPaneViewportAsync(ComparePaneId, 2.8d, 0.72d, 0.58d).ConfigureAwait(true);
                            var primaryPaneZoomViewport = controller.CapturePaneViewportSnapshot(PrimaryPaneId);
                            var comparePaneZoomViewport = controller.CapturePaneViewportSnapshot(ComparePaneId);
                            var paneZoomsIndependent = primaryPaneZoomViewport != null &&
                                                       comparePaneZoomViewport != null &&
                                                       primaryPaneZoomViewport.IsZoomed &&
                                                       comparePaneZoomViewport.IsZoomed &&
                                                       !ViewportSnapshotsMatch(primaryPaneZoomViewport, comparePaneZoomViewport);
                            checks.Add(paneZoomsIndependent
                                ? Pass(
                                    filePath,
                                    "ui",
                                    CoverageCategory,
                                    "ui-zoom-compare-pane-independent",
                                    string.Format(
                                        CultureInfo.InvariantCulture,
                                        "Primary and compare panes kept independent zoomed viewports ({0} vs {1}).",
                                        FormatViewportSnapshot(primaryPaneZoomViewport),
                                        FormatViewportSnapshot(comparePaneZoomViewport)))
                                : Fail(
                                    filePath,
                                    "ui",
                                    CoverageCategory,
                                    "ui-zoom-compare-pane-independent",
                                    string.Format(
                                        CultureInfo.InvariantCulture,
                                        "Two-pane zoom did not keep pane-local viewport state independent. Primary={0}; compare={1}.",
                                        FormatViewportSnapshot(primaryPaneZoomViewport),
                                        FormatViewportSnapshot(comparePaneZoomViewport))));

                            await controller.SelectPaneAsync(PrimaryPaneId).ConfigureAwait(true);
                            var compareModePlaybackStart = controller.CaptureSnapshot();
                            if (HasPlaybackHeadroom(
                                    compareModePlaybackStart.EngineFrameIndex,
                                    indexReady.IndexedFrameCount,
                                    compareModePlaybackStart.EnginePresentationTime,
                                    TimeSpan.FromSeconds(compareModePlaybackStart.SliderMaximumSeconds),
                                    TimeSpan.FromSeconds(Math.Max(compareModePlaybackStart.PositionStepSeconds, 0d))))
                            {
                                await controller.StartPlaybackAsync(SynchronizedOperationScope.FocusedPane, PrimaryPaneId).ConfigureAwait(true);
                                await Task.Delay(PlaybackDelay, cancellationToken).ConfigureAwait(true);
                                await controller.PausePlaybackAsync().ConfigureAwait(true);
                                var primaryViewportAfterComparePlayback = controller.CapturePaneViewportSnapshot(PrimaryPaneId);
                                var compareViewportAfterComparePlayback = controller.CapturePaneViewportSnapshot(ComparePaneId);
                                checks.Add(ViewportSnapshotsMatch(primaryPaneZoomViewport, primaryViewportAfterComparePlayback) &&
                                           ViewportSnapshotsMatch(comparePaneZoomViewport, compareViewportAfterComparePlayback)
                                    ? Pass(
                                        filePath,
                                        "ui",
                                        CorrectnessCategory,
                                        "ui-zoom-compare-playback-persists",
                                        "Two-pane zoom state stayed intact during focused-pane playback.")
                                    : Fail(
                                        filePath,
                                        "ui",
                                        CorrectnessCategory,
                                        "ui-zoom-compare-playback-persists",
                                        string.Format(
                                            CultureInfo.InvariantCulture,
                                            "Two-pane zoom state changed during focused-pane playback. Primary expected {0}, actual {1}; compare expected {2}, actual {3}.",
                                            FormatViewportSnapshot(primaryPaneZoomViewport),
                                            FormatViewportSnapshot(primaryViewportAfterComparePlayback),
                                            FormatViewportSnapshot(comparePaneZoomViewport),
                                            FormatViewportSnapshot(compareViewportAfterComparePlayback))));
                            }
                            else
                            {
                                checks.Add(Warning(
                                    filePath,
                                    "ui",
                                    CoverageCategory,
                                    "ui-zoom-compare-playback-persists-skipped",
                                    "Two-pane zoom playback persistence was skipped because the primary pane had no forward headroom left."));
                            }

                        var primaryLoopInTarget = controller.GetPaneTargetFromRatio(PrimaryPaneId, 0.20d);
                        var primaryLoopInSet = await controller.SetTimelineLoopMarkerAtAsync(
                            PrimaryPaneId,
                            LoopPlaybackMarkerEndpoint.In,
                            primaryLoopInTarget).ConfigureAwait(true);
                        var primaryLoopInReady = primaryLoopInSet &&
                                                 await controller.WaitForLoopMarkerReadyAsync(
                                                         PrimaryPaneId,
                                                         LoopPlaybackMarkerEndpoint.In,
                                                         IndexReadyTimeout,
                                                         cancellationToken)
                                                     .ConfigureAwait(true);
                        var primaryLoopStartSnapshot = controller.CaptureSnapshot();
                        var primaryLoopStep = TimeSpan.FromSeconds(Math.Max(primaryLoopStartSnapshot.PositionStepSeconds, 0.001d));
                        var blockedPrimaryLoopOutTarget = primaryLoopStartSnapshot.EnginePresentationTime > primaryLoopStep
                            ? primaryLoopStartSnapshot.EnginePresentationTime - primaryLoopStep
                            : TimeSpan.Zero;
                        var blockedPrimaryLoopOut = await controller.SetTimelineLoopMarkerAtAsync(
                            PrimaryPaneId,
                            LoopPlaybackMarkerEndpoint.Out,
                            blockedPrimaryLoopOutTarget).ConfigureAwait(true);
                        checks.Add(!blockedPrimaryLoopOut
                            ? Pass(
                                filePath,
                                "ui",
                                CoverageCategory,
                                "ui-loop-pane-primary-timeline-blocks-b-before-a",
                                "Primary timeline right-click rejected Position B before Position A.")
                            : Fail(
                                filePath,
                                "ui",
                                CoverageCategory,
                                "ui-loop-pane-primary-timeline-blocks-b-before-a",
                                "Primary timeline allowed Position B before Position A."));

                        var primaryLoopOutSet = primaryLoopInReady &&
                                                await controller.SetTimelineLoopMarkerAtAsync(
                                                        PrimaryPaneId,
                                                        LoopPlaybackMarkerEndpoint.Out,
                                                        controller.GetPaneTargetFromRatio(PrimaryPaneId, 0.22d))
                                                    .ConfigureAwait(true);
                        var compareLoopInSet = await controller.SetTimelineLoopMarkerAtAsync(
                            ComparePaneId,
                            LoopPlaybackMarkerEndpoint.In,
                            controller.GetPaneTargetFromRatio(ComparePaneId, 0.40d)).ConfigureAwait(true);
                        var compareLoopInReady = compareLoopInSet &&
                                                 await controller.WaitForLoopMarkerReadyAsync(
                                                         ComparePaneId,
                                                         LoopPlaybackMarkerEndpoint.In,
                                                         IndexReadyTimeout,
                                                         cancellationToken)
                                                     .ConfigureAwait(true);
                        var compareLoopOutSet = compareLoopInReady &&
                                                await controller.SetTimelineLoopMarkerAtAsync(
                                                        ComparePaneId,
                                                        LoopPlaybackMarkerEndpoint.Out,
                                                        controller.GetPaneTargetFromRatio(ComparePaneId, 0.42d))
                                                    .ConfigureAwait(true);

                        var primaryPaneLoopUi = await controller.WaitForLoopUiReadyAsync(PrimaryPaneId, LoopUiReadyTimeout, cancellationToken).ConfigureAwait(true);
                        var comparePaneLoopUi = await controller.WaitForLoopUiReadyAsync(ComparePaneId, LoopUiReadyTimeout, cancellationToken).ConfigureAwait(true);
                        var paneLocalLoopsIndependent = primaryLoopInSet &&
                                                        primaryLoopInReady &&
                                                        primaryLoopOutSet &&
                                                        compareLoopInSet &&
                                                        compareLoopInReady &&
                                                        compareLoopOutSet &&
                                                        !primaryPaneLoopUi.IsInvalid &&
                                                        !comparePaneLoopUi.IsInvalid &&
                                                        !double.IsNaN(primaryPaneLoopUi.InPosition) &&
                                                        !double.IsNaN(primaryPaneLoopUi.OutPosition) &&
                                                        !double.IsNaN(comparePaneLoopUi.InPosition) &&
                                                        !double.IsNaN(comparePaneLoopUi.OutPosition) &&
                                                        Math.Abs(primaryPaneLoopUi.InPosition - comparePaneLoopUi.InPosition) > 0.01d;
                        checks.Add(paneLocalLoopsIndependent
                            ? Pass(
                                filePath,
                                "ui",
                                CoverageCategory,
                                "ui-loop-pane-local-independent",
                                string.Format(
                                    CultureInfo.InvariantCulture,
                                    "Primary and Compare sliders rendered independent pane-local loop boxes ({0:0.###} vs {1:0.###} seconds).",
                                    primaryPaneLoopUi.InPosition,
                                    comparePaneLoopUi.InPosition))
                            : Fail(
                                filePath,
                                "ui",
                                CoverageCategory,
                                "ui-loop-pane-local-independent",
                                string.Format(
                                    CultureInfo.InvariantCulture,
                                    "Pane-local loop boxes did not stay independent. Primary status='{0}' in={1} out={2}; Compare status='{3}' in={4} out={5}.",
                                    primaryPaneLoopUi.StatusText,
                                    primaryPaneLoopUi.InPosition,
                                    primaryPaneLoopUi.OutPosition,
                                    comparePaneLoopUi.StatusText,
                                    comparePaneLoopUi.InPosition,
                                    comparePaneLoopUi.OutPosition)));

                        var primaryPaneExportOutputPath = Path.Combine(
                            Path.GetTempPath(),
                            "frameplayer-loop-export-primary-" + Guid.NewGuid().ToString("N") + ".mp4");
                        try
                        {
                            var primaryPaneExport = await controller.ExportLoopClipAsync(primaryPaneExportOutputPath, PrimaryPaneId).ConfigureAwait(true);
                            if (primaryPaneExport == null)
                            {
                                checks.Add(Warning(
                                    filePath,
                                    "ui",
                                    CoverageCategory,
                                    "ui-loop-export-pane-primary-skipped",
                                    "Primary pane clip export was skipped because the export runtime was unavailable or the pane loop state changed before export."));
                            }
                            else if (!primaryPaneExport.Succeeded)
                            {
                                checks.Add(Fail(
                                    filePath,
                                    "ui",
                                    LifecycleCategory,
                                    "ui-loop-export-pane-primary",
                                    "Primary pane clip export failed: " + primaryPaneExport.Message));
                            }
                            else
                            {
                                checks.Add(File.Exists(primaryPaneExportOutputPath)
                                    ? Pass(
                                        filePath,
                                        "ui",
                                        LifecycleCategory,
                                        "ui-loop-export-pane-primary",
                                        "Primary pane clip export produced an MP4 output file.")
                                    : Fail(
                                        filePath,
                                        "ui",
                                        LifecycleCategory,
                                        "ui-loop-export-pane-primary",
                                        "Primary pane clip export reported success but did not produce the expected output file."));

                                var primaryPaneExportZoomed = primaryPaneExport.Plan != null &&
                                                              primaryPaneExport.Plan.ViewportSnapshot != null &&
                                                              primaryPaneExport.Plan.ViewportSnapshot.IsZoomed &&
                                                              ViewportSnapshotsMatch(primaryPaneZoomViewport, primaryPaneExport.Plan.ViewportSnapshot);
                                checks.Add(primaryPaneExportZoomed
                                    ? Pass(
                                        filePath,
                                        "ui",
                                        CorrectnessCategory,
                                        "ui-loop-export-pane-primary-zoomed",
                                        "Primary pane clip export preserved the primary pane's zoomed viewport.")
                                    : Fail(
                                        filePath,
                                        "ui",
                                        CorrectnessCategory,
                                        "ui-loop-export-pane-primary-zoomed",
                                        string.Format(
                                            CultureInfo.InvariantCulture,
                                            "Primary pane clip export did not preserve the primary pane viewport. Expected {0}; actual {1}.",
                                            FormatViewportSnapshot(primaryPaneZoomViewport),
                                            FormatViewportSnapshot(primaryPaneExport.Plan != null ? primaryPaneExport.Plan.ViewportSnapshot : null))));
                            }
                        }
                        finally
                        {
                            if (File.Exists(primaryPaneExportOutputPath))
                            {
                                File.Delete(primaryPaneExportOutputPath);
                            }
                        }

                        var comparePaneExportOutputPath = Path.Combine(
                            Path.GetTempPath(),
                            "frameplayer-loop-export-compare-" + Guid.NewGuid().ToString("N") + ".mp4");
                        try
                        {
                            var comparePaneExport = await controller.ExportLoopClipAsync(comparePaneExportOutputPath, ComparePaneId).ConfigureAwait(true);
                            if (comparePaneExport == null)
                            {
                                checks.Add(Warning(
                                    filePath,
                                    "ui",
                                    CoverageCategory,
                                    "ui-loop-export-pane-compare-skipped",
                                    "Compare pane clip export was skipped because the export runtime was unavailable or the pane loop state changed before export."));
                            }
                            else if (!comparePaneExport.Succeeded)
                            {
                                checks.Add(Fail(
                                    filePath,
                                    "ui",
                                    LifecycleCategory,
                                    "ui-loop-export-pane-compare",
                                    "Compare pane clip export failed: " + comparePaneExport.Message));
                            }
                            else
                            {
                                checks.Add(File.Exists(comparePaneExportOutputPath)
                                    ? Pass(
                                        filePath,
                                        "ui",
                                        LifecycleCategory,
                                        "ui-loop-export-pane-compare",
                                        "Compare pane clip export produced an MP4 output file.")
                                    : Fail(
                                        filePath,
                                        "ui",
                                        LifecycleCategory,
                                        "ui-loop-export-pane-compare",
                                        "Compare pane clip export reported success but did not produce the expected output file."));

                                var comparePaneExportZoomed = comparePaneExport.Plan != null &&
                                                              comparePaneExport.Plan.ViewportSnapshot != null &&
                                                              comparePaneExport.Plan.ViewportSnapshot.IsZoomed &&
                                                              ViewportSnapshotsMatch(comparePaneZoomViewport, comparePaneExport.Plan.ViewportSnapshot);
                                checks.Add(comparePaneExportZoomed
                                    ? Pass(
                                        filePath,
                                        "ui",
                                        CorrectnessCategory,
                                        "ui-loop-export-pane-compare-zoomed",
                                        "Compare pane clip export preserved the compare pane's zoomed viewport.")
                                    : Fail(
                                        filePath,
                                        "ui",
                                        CorrectnessCategory,
                                        "ui-loop-export-pane-compare-zoomed",
                                        string.Format(
                                            CultureInfo.InvariantCulture,
                                            "Compare pane clip export did not preserve the compare pane viewport. Expected {0}; actual {1}.",
                                            FormatViewportSnapshot(comparePaneZoomViewport),
                                            FormatViewportSnapshot(comparePaneExport.Plan != null ? comparePaneExport.Plan.ViewportSnapshot : null))));

                                var paneLoopStartDeltaSeconds = Math.Abs(primaryPaneLoopUi.InPosition - comparePaneLoopUi.InPosition);
                                var paneExportIndependent = comparePaneExport.Plan != null &&
                                                            paneLoopStartDeltaSeconds > 0.001d &&
                                                            comparePaneExport.Plan.StartTime.TotalSeconds >= comparePaneLoopUi.InPosition - 0.25d &&
                                                            comparePaneExport.Plan.StartTime.TotalSeconds <= comparePaneLoopUi.InPosition + 0.25d;
                                checks.Add(paneExportIndependent
                                    ? Pass(
                                        filePath,
                                        "ui",
                                        CorrectnessCategory,
                                        "ui-loop-export-pane-independence",
                                        "Compare pane clip export respected the compare pane's own loop range instead of the primary pane range.")
                                    : Fail(
                                        filePath,
                                        "ui",
                                        CorrectnessCategory,
                                        "ui-loop-export-pane-independence",
                                        "Compare pane clip export did not line up with the compare pane's pane-local loop range."));
                            }
                        }
                        finally
                        {
                            if (File.Exists(comparePaneExportOutputPath))
                            {
                                File.Delete(comparePaneExportOutputPath);
                            }
                        }

                        var compareLoopMergeOutputPath = Path.Combine(
                            Path.GetTempPath(),
                            "frameplayer-compare-side-by-side-loop-" + Guid.NewGuid().ToString("N") + ".mp4");
                        try
                        {
                            var compareLoopMerge = await controller.ExportSideBySideCompareAsync(
                                compareLoopMergeOutputPath,
                                CompareSideBySideExportMode.Loop,
                                CompareSideBySideExportAudioSource.Primary).ConfigureAwait(true);
                            if (compareLoopMerge == null)
                            {
                                checks.Add(Warning(
                                    filePath,
                                    "ui",
                                    CoverageCategory,
                                    "ui-compare-side-by-side-loop-skipped",
                                    "Loop-mode side-by-side compare export was skipped because the compare export path was unavailable."));
                            }
                            else if (!compareLoopMerge.Succeeded)
                            {
                                checks.Add(Fail(
                                    filePath,
                                    "ui",
                                    LifecycleCategory,
                                    "ui-compare-side-by-side-loop",
                                    "Loop-mode side-by-side compare export failed: " + compareLoopMerge.Message));
                            }
                            else
                            {
                                checks.Add(File.Exists(compareLoopMergeOutputPath)
                                    ? Pass(
                                        filePath,
                                        "ui",
                                        LifecycleCategory,
                                        "ui-compare-side-by-side-loop",
                                        "Loop-mode side-by-side compare export produced an MP4 output file.")
                                    : Fail(
                                        filePath,
                                        "ui",
                                        LifecycleCategory,
                                        "ui-compare-side-by-side-loop",
                                        "Loop-mode side-by-side compare export reported success but did not produce the expected output file."));

                                var compareLoopZoomed = compareLoopMerge.Plan != null &&
                                                        compareLoopMerge.Plan.PrimaryViewportSnapshot != null &&
                                                        compareLoopMerge.Plan.CompareViewportSnapshot != null &&
                                                        compareLoopMerge.Plan.PrimaryViewportSnapshot.IsZoomed &&
                                                        compareLoopMerge.Plan.CompareViewportSnapshot.IsZoomed &&
                                                        ViewportSnapshotsMatch(primaryPaneZoomViewport, compareLoopMerge.Plan.PrimaryViewportSnapshot) &&
                                                        ViewportSnapshotsMatch(comparePaneZoomViewport, compareLoopMerge.Plan.CompareViewportSnapshot);
                                checks.Add(compareLoopZoomed
                                    ? Pass(
                                        filePath,
                                        "ui",
                                        CorrectnessCategory,
                                        "ui-compare-side-by-side-loop-zoomed",
                                        "Loop-mode side-by-side compare export preserved both panes' zoomed viewports.")
                                    : Fail(
                                        filePath,
                                        "ui",
                                        CorrectnessCategory,
                                        "ui-compare-side-by-side-loop-zoomed",
                                        string.Format(
                                            CultureInfo.InvariantCulture,
                                            "Loop-mode side-by-side compare export did not preserve pane-local viewports. Primary expected {0}, actual {1}; compare expected {2}, actual {3}.",
                                            FormatViewportSnapshot(primaryPaneZoomViewport),
                                            FormatViewportSnapshot(compareLoopMerge.Plan != null ? compareLoopMerge.Plan.PrimaryViewportSnapshot : null),
                                            FormatViewportSnapshot(comparePaneZoomViewport),
                                            FormatViewportSnapshot(compareLoopMerge.Plan != null ? compareLoopMerge.Plan.CompareViewportSnapshot : null))));

                                var expectedDuration = compareLoopMerge.Plan != null
                                    ? compareLoopMerge.Plan.OutputDuration
                                    : TimeSpan.Zero;
                                var actualDuration = compareLoopMerge.ProbedDuration ?? expectedDuration;
                                var durationDelta = Math.Abs((actualDuration - expectedDuration).TotalMilliseconds);
                                checks.Add(durationDelta <= 250d
                                    ? Pass(
                                        filePath,
                                        "ui",
                                        CorrectnessCategory,
                                        "ui-compare-side-by-side-loop-duration",
                                        string.Format(
                                            CultureInfo.InvariantCulture,
                                            "Loop-mode side-by-side compare export duration matched the planned output within tolerance ({0:0.0} ms delta).",
                                            durationDelta))
                                    : Fail(
                                        filePath,
                                        "ui",
                                        CorrectnessCategory,
                                        "ui-compare-side-by-side-loop-duration",
                                        string.Format(
                                            CultureInfo.InvariantCulture,
                                            "Loop-mode side-by-side compare export duration drifted from the plan by {0:0.0} ms.",
                                            durationDelta)));

                                var dimensionsValid = compareLoopMerge.Plan != null &&
                                                      compareLoopMerge.ProbedVideoWidth.HasValue &&
                                                      compareLoopMerge.ProbedVideoHeight.HasValue &&
                                                      compareLoopMerge.ProbedVideoWidth.Value >= compareLoopMerge.Plan.OutputWidth &&
                                                      compareLoopMerge.ProbedVideoHeight.Value >= compareLoopMerge.Plan.OutputHeight;
                                var plannedLoopWidth = compareLoopMerge.Plan?.OutputWidth ?? 0;
                                var plannedLoopHeight = compareLoopMerge.Plan?.OutputHeight ?? 0;
                                checks.Add(dimensionsValid
                                    ? Pass(
                                        filePath,
                                        "ui",
                                        CorrectnessCategory,
                                        "ui-compare-side-by-side-loop-dimensions",
                                        string.Format(
                                            CultureInfo.InvariantCulture,
                                            "Loop-mode side-by-side compare export preserved the planned full-resolution canvas ({0}x{1}).",
                                            compareLoopMerge.ProbedVideoWidth,
                                            compareLoopMerge.ProbedVideoHeight))
                                    : Fail(
                                        filePath,
                                        "ui",
                                        CorrectnessCategory,
                                        "ui-compare-side-by-side-loop-dimensions",
                                        string.Format(
                                            CultureInfo.InvariantCulture,
                                            "Loop-mode side-by-side compare export dimensions were smaller than expected. Planned={0}x{1}; probed={2}x{3}.",
                                            plannedLoopWidth,
                                            plannedLoopHeight,
                                            compareLoopMerge.ProbedVideoWidth,
                                            compareLoopMerge.ProbedVideoHeight)));

                                var expectedAudio = compareLoopMerge.Plan != null && compareLoopMerge.Plan.SelectedAudioHasStream;
                                var actualAudio = compareLoopMerge.ProbedHasAudioStream.GetValueOrDefault(false);
                                var loopAudioSuccessMessage = expectedAudio
                                    ? "Loop-mode side-by-side compare export preserved the selected primary-pane audio track."
                                    : "Loop-mode side-by-side compare export correctly produced a silent output when the selected primary pane had no audio.";
                                checks.Add(actualAudio == expectedAudio
                                    ? Pass(
                                        filePath,
                                        "ui",
                                        CorrectnessCategory,
                                        "ui-compare-side-by-side-loop-audio-selection",
                                        loopAudioSuccessMessage)
                                    : Fail(
                                        filePath,
                                        "ui",
                                        CorrectnessCategory,
                                        "ui-compare-side-by-side-loop-audio-selection",
                                        string.Format(
                                            CultureInfo.InvariantCulture,
                                            "Loop-mode side-by-side compare export audio presence did not match the selected primary-pane audio source. Expected audio={0}; actual audio={1}.",
                                            expectedAudio,
                                            actualAudio)));

                                if (!string.Equals(compareCompanionPath, filePath, StringComparison.OrdinalIgnoreCase))
                                {
                                    checks.Add(string.Equals(Path.GetExtension(filePath), ".avi", StringComparison.OrdinalIgnoreCase) &&
                                               string.Equals(Path.GetExtension(compareCompanionPath), ".mp4", StringComparison.OrdinalIgnoreCase)
                                        ? Pass(
                                            filePath,
                                            "ui",
                                            CoverageCategory,
                                            "ui-compare-side-by-side-mixed-pair",
                                            "Side-by-side compare export covered an AVI primary plus MP4 compare pair.")
                                        : Pass(
                                            filePath,
                                            "ui",
                                            CoverageCategory,
                                            "ui-compare-side-by-side-secondary-pair",
                                            "Side-by-side compare export covered a mixed compare pair."));
                                }
                            }
                        }
                        finally
                        {
                            if (File.Exists(compareLoopMergeOutputPath))
                            {
                                File.Delete(compareLoopMergeOutputPath);
                            }
                        }

                        var compareWholeMergeOutputPath = Path.Combine(
                            Path.GetTempPath(),
                            "frameplayer-compare-side-by-side-whole-" + Guid.NewGuid().ToString("N") + ".mp4");
                        try
                        {
                            var compareWholeMerge = await controller.ExportSideBySideCompareAsync(
                                compareWholeMergeOutputPath,
                                CompareSideBySideExportMode.WholeVideo,
                                CompareSideBySideExportAudioSource.Compare).ConfigureAwait(true);
                            if (compareWholeMerge == null)
                            {
                                checks.Add(Warning(
                                    filePath,
                                    "ui",
                                    CoverageCategory,
                                    "ui-compare-side-by-side-whole-skipped",
                                    "Whole-video side-by-side compare export was skipped because the compare export path was unavailable."));
                            }
                            else if (!compareWholeMerge.Succeeded)
                            {
                                checks.Add(Fail(
                                    filePath,
                                    "ui",
                                    LifecycleCategory,
                                    "ui-compare-side-by-side-whole",
                                    "Whole-video side-by-side compare export failed: " + compareWholeMerge.Message));
                            }
                            else
                            {
                                checks.Add(File.Exists(compareWholeMergeOutputPath)
                                    ? Pass(
                                        filePath,
                                        "ui",
                                        LifecycleCategory,
                                        "ui-compare-side-by-side-whole",
                                        "Whole-video side-by-side compare export produced an MP4 output file.")
                                    : Fail(
                                        filePath,
                                        "ui",
                                        LifecycleCategory,
                                        "ui-compare-side-by-side-whole",
                                        "Whole-video side-by-side compare export reported success but did not produce the expected output file."));

                                var compareWholeZoomed = compareWholeMerge.Plan != null &&
                                                         compareWholeMerge.Plan.PrimaryViewportSnapshot != null &&
                                                         compareWholeMerge.Plan.CompareViewportSnapshot != null &&
                                                         compareWholeMerge.Plan.PrimaryViewportSnapshot.IsZoomed &&
                                                         compareWholeMerge.Plan.CompareViewportSnapshot.IsZoomed &&
                                                         ViewportSnapshotsMatch(primaryPaneZoomViewport, compareWholeMerge.Plan.PrimaryViewportSnapshot) &&
                                                         ViewportSnapshotsMatch(comparePaneZoomViewport, compareWholeMerge.Plan.CompareViewportSnapshot);
                                checks.Add(compareWholeZoomed
                                    ? Pass(
                                        filePath,
                                        "ui",
                                        CorrectnessCategory,
                                        "ui-compare-side-by-side-whole-zoomed",
                                        "Whole-video side-by-side compare export preserved both panes' zoomed viewports.")
                                    : Fail(
                                        filePath,
                                        "ui",
                                        CorrectnessCategory,
                                        "ui-compare-side-by-side-whole-zoomed",
                                        string.Format(
                                            CultureInfo.InvariantCulture,
                                            "Whole-video side-by-side compare export did not preserve pane-local viewports. Primary expected {0}, actual {1}; compare expected {2}, actual {3}.",
                                            FormatViewportSnapshot(primaryPaneZoomViewport),
                                            FormatViewportSnapshot(compareWholeMerge.Plan != null ? compareWholeMerge.Plan.PrimaryViewportSnapshot : null),
                                            FormatViewportSnapshot(comparePaneZoomViewport),
                                            FormatViewportSnapshot(compareWholeMerge.Plan != null ? compareWholeMerge.Plan.CompareViewportSnapshot : null))));

                                var expectedDuration = compareWholeMerge.Plan != null
                                    ? compareWholeMerge.Plan.OutputDuration
                                    : TimeSpan.Zero;
                                var actualDuration = compareWholeMerge.ProbedDuration ?? expectedDuration;
                                var durationDelta = Math.Abs((actualDuration - expectedDuration).TotalMilliseconds);
                                checks.Add(durationDelta <= 250d
                                    ? Pass(
                                        filePath,
                                        "ui",
                                        CorrectnessCategory,
                                        "ui-compare-side-by-side-whole-duration",
                                        string.Format(
                                            CultureInfo.InvariantCulture,
                                            "Whole-video side-by-side compare export duration matched the planned output within tolerance ({0:0.0} ms delta).",
                                            durationDelta))
                                    : Fail(
                                        filePath,
                                        "ui",
                                        CorrectnessCategory,
                                        "ui-compare-side-by-side-whole-duration",
                                        string.Format(
                                            CultureInfo.InvariantCulture,
                                            "Whole-video side-by-side compare export duration drifted from the plan by {0:0.0} ms.",
                                            durationDelta)));

                                var alignmentPreserved = compareWholeMerge.Plan != null &&
                                                         (compareWholeMerge.Plan.PrimaryLeadingPad > TimeSpan.Zero ||
                                                          compareWholeMerge.Plan.CompareLeadingPad > TimeSpan.Zero);
                                checks.Add(alignmentPreserved
                                    ? Pass(
                                        filePath,
                                        "ui",
                                        CorrectnessCategory,
                                        "ui-compare-side-by-side-whole-alignment",
                                        "Whole-video side-by-side compare export preserved the current compare alignment with a non-zero leading pad.")
                                    : Fail(
                                        filePath,
                                        "ui",
                                        CorrectnessCategory,
                                        "ui-compare-side-by-side-whole-alignment",
                                        "Whole-video side-by-side compare export did not preserve the current compare alignment as a leading pad."));

                                var dimensionsValid = compareWholeMerge.Plan != null &&
                                                      compareWholeMerge.ProbedVideoWidth.HasValue &&
                                                      compareWholeMerge.ProbedVideoHeight.HasValue &&
                                                      compareWholeMerge.ProbedVideoWidth.Value >= compareWholeMerge.Plan.OutputWidth &&
                                                      compareWholeMerge.ProbedVideoHeight.Value >= compareWholeMerge.Plan.OutputHeight;
                                var plannedWholeWidth = compareWholeMerge.Plan?.OutputWidth ?? 0;
                                var plannedWholeHeight = compareWholeMerge.Plan?.OutputHeight ?? 0;
                                checks.Add(dimensionsValid
                                    ? Pass(
                                        filePath,
                                        "ui",
                                        CorrectnessCategory,
                                        "ui-compare-side-by-side-whole-dimensions",
                                        string.Format(
                                            CultureInfo.InvariantCulture,
                                            "Whole-video side-by-side compare export preserved the planned full-resolution canvas ({0}x{1}).",
                                            compareWholeMerge.ProbedVideoWidth,
                                            compareWholeMerge.ProbedVideoHeight))
                                    : Fail(
                                        filePath,
                                        "ui",
                                        CorrectnessCategory,
                                        "ui-compare-side-by-side-whole-dimensions",
                                        string.Format(
                                            CultureInfo.InvariantCulture,
                                            "Whole-video side-by-side compare export dimensions were smaller than expected. Planned={0}x{1}; probed={2}x{3}.",
                                            plannedWholeWidth,
                                            plannedWholeHeight,
                                            compareWholeMerge.ProbedVideoWidth,
                                            compareWholeMerge.ProbedVideoHeight)));

                                var expectedAudio = compareWholeMerge.Plan != null && compareWholeMerge.Plan.SelectedAudioHasStream;
                                var actualAudio = compareWholeMerge.ProbedHasAudioStream.GetValueOrDefault(false);
                                var wholeAudioSuccessMessage = expectedAudio
                                    ? "Whole-video side-by-side compare export preserved the selected compare-pane audio track."
                                    : "Whole-video side-by-side compare export correctly produced a silent output when the selected compare pane had no audio.";
                                checks.Add(actualAudio == expectedAudio
                                    ? Pass(
                                        filePath,
                                        "ui",
                                        CorrectnessCategory,
                                        "ui-compare-side-by-side-whole-audio-selection",
                                        wholeAudioSuccessMessage)
                                    : Fail(
                                        filePath,
                                        "ui",
                                        CorrectnessCategory,
                                        "ui-compare-side-by-side-whole-audio-selection",
                                        string.Format(
                                            CultureInfo.InvariantCulture,
                                            "Whole-video side-by-side compare export audio presence did not match the selected compare-pane audio source. Expected audio={0}; actual audio={1}.",
                                            expectedAudio,
                                            actualAudio)));
                            }
                        }
                        finally
                        {
                            if (File.Exists(compareWholeMergeOutputPath))
                            {
                                File.Delete(compareWholeMergeOutputPath);
                            }
                        }

                            await controller.ResetPaneZoomAsync(PrimaryPaneId).ConfigureAwait(true);
                            await controller.ResetPaneZoomAsync(ComparePaneId).ConfigureAwait(true);
                            var primaryPaneResetViewport = controller.CapturePaneViewportSnapshot(PrimaryPaneId);
                            var comparePaneResetViewport = controller.CapturePaneViewportSnapshot(ComparePaneId);
                            checks.Add(primaryPaneResetViewport != null &&
                                       comparePaneResetViewport != null &&
                                       !primaryPaneResetViewport.IsZoomed &&
                                       !comparePaneResetViewport.IsZoomed
                                ? Pass(
                                    filePath,
                                    "ui",
                                    CorrectnessCategory,
                                    "ui-zoom-compare-reset",
                                    "Reset Zoom restored both panes to their full-frame view in compare mode.")
                                : Fail(
                                    filePath,
                                    "ui",
                                    CorrectnessCategory,
                                    "ui-zoom-compare-reset",
                                    string.Format(
                                        CultureInfo.InvariantCulture,
                                        "Compare-mode zoom reset did not restore both panes to full frame. Primary={0}; compare={1}.",
                                        FormatViewportSnapshot(primaryPaneResetViewport),
                                        FormatViewportSnapshot(comparePaneResetViewport))));

                            await controller.ClearLoopPointsAsync(PrimaryPaneId).ConfigureAwait(true);
                            await controller.ClearLoopPointsAsync(ComparePaneId).ConfigureAwait(true);
                            await controller.SetCompareModeAsync(false).ConfigureAwait(true);
                        }
                        else
                        {
                            checks.Add(Warning(
                                filePath,
                                "ui",
                                CoverageCategory,
                                "ui-loop-pane-local-coverage-skipped",
                                string.Format(
                                    CultureInfo.InvariantCulture,
                                    "Pane-local loop coverage was skipped because this clip only exposes {0} indexed frames, which is not enough resolution for the 20%/22% and 40%/42% pane-local marker separation checks.",
                                    indexReady.IndexedFrameCount)));
                        }

                        Trace("UI harness end seek: " + filePath);
                        var endSeek = await controller.CommitSliderSeekAsync(ClickSeekInteraction, TimeSpan.FromSeconds(controller.SliderMaximumSeconds)).ConfigureAwait(true);
                        metrics.UiEndSeekMilliseconds = endSeek.ElapsedMilliseconds;
                        var expectedLastFrame = indexReady.IndexedFrameCount - 1L;
                        checks.Add(EvaluateUiSnapshot(
                            filePath,
                            "ui-end-of-video-slider-agreement",
                            endSeek,
                            expectedLastFrame,
                            expectedLastFrame + 1L,
                            true,
                            endSeek.ElapsedMilliseconds));
                        checks.AddRange(await controller.RunUiStepRoundTripAsync(filePath, "ui-end-of-video-slider-agreement", cancellationToken).ConfigureAwait(true));
                    }
                    else
                    {
                        checks.Add(Warning(
                            filePath,
                            "ui",
                            CorrectnessCategory,
                            "ui-index-dependent-slider-checks",
                            "Slider correctness checks that require the indexed last-frame boundary were skipped because the global index never became ready."));
                    }

                    WarnForSlowUiSeek(filePath, "ui-click-slider-seek-latency", metrics.UiClickSeekMilliseconds, checks);
                    WarnForSlowUiSeek(filePath, "ui-drag-slider-seek-latency", metrics.UiDragSeekMilliseconds, checks);
                    Trace("UI harness complete: " + filePath);

                    return new UiRegressionResult(checks, notes, metrics);
                }
                catch (Exception ex)
                {
                    Trace("UI harness exception: " + filePath + " :: " + ex);
                    checks.Add(Fail(
                        filePath,
                        "ui",
                        LifecycleCategory,
                        "ui-regression-harness",
                        "UI regression harness failed: " + ex.Message));
                    return new UiRegressionResult(checks, notes, metrics);
                }
                finally
                {
                    if (controller != null)
                    {
                        try
                        {
                            await controller.CloseAsync().ConfigureAwait(true);
                        }
                        catch
                        {
                        }
                    }

                    if (window != null)
                    {
                        window.Close();
                    }
                }
            }

            private static void WarnForSlowUiSeek(
                string filePath,
                string name,
                double elapsedMilliseconds,
                List<RegressionCheckResult> checks)
            {
                if (elapsedMilliseconds <= 0d)
                {
                    return;
                }

                if (elapsedMilliseconds > UiSeekWarningThreshold.TotalMilliseconds)
                {
                    checks.Add(Warning(
                        filePath,
                        "ui",
                        "responsiveness",
                        name,
                        string.Format(
                            CultureInfo.InvariantCulture,
                            "Seek took {0:0.###} ms, which is slow enough to inspect manually even though correctness still held.",
                            elapsedMilliseconds),
                        elapsedMilliseconds: elapsedMilliseconds));
                }
            }

            private static bool IsAudioInsertionEligible(string filePath, VideoMediaInfo mediaInfo)
            {
                return string.Equals(Path.GetExtension(filePath), ".mp4", StringComparison.OrdinalIgnoreCase) &&
                       IsH264CodecName(mediaInfo != null ? mediaInfo.VideoCodecName : string.Empty);
            }

            private static string GetExpectedAudioInsertionDisabledTooltip(string filePath, VideoMediaInfo mediaInfo)
            {
                if (!string.Equals(Path.GetExtension(filePath), ".mp4", StringComparison.OrdinalIgnoreCase))
                {
                    return "H.264 MP4";
                }

                var codecName = mediaInfo != null ? mediaInfo.VideoCodecName : string.Empty;
                if (string.IsNullOrWhiteSpace(codecName))
                {
                    return "codec could not be resolved";
                }

                return "H.264 video";
            }

            private static bool IsH264CodecName(string codecName)
            {
                if (string.IsNullOrWhiteSpace(codecName))
                {
                    return false;
                }

                var normalizedCodec = codecName.Replace(".", string.Empty).Trim();
                return string.Equals(normalizedCodec, "h264", StringComparison.OrdinalIgnoreCase);
            }

            private static bool ViewportSnapshotsMatch(PaneViewportSnapshot expected, PaneViewportSnapshot actual)
            {
                if (expected == null || actual == null)
                {
                    return false;
                }

                const int CropPixelTolerance = 3;
                return Math.Abs(expected.ZoomFactor - actual.ZoomFactor) <= 0.001d &&
                       Math.Abs(expected.NormalizedCenterX - actual.NormalizedCenterX) <= 0.001d &&
                       Math.Abs(expected.NormalizedCenterY - actual.NormalizedCenterY) <= 0.001d &&
                       expected.SourcePixelWidth == actual.SourcePixelWidth &&
                       expected.SourcePixelHeight == actual.SourcePixelHeight &&
                       Math.Abs(expected.SourceCropX - actual.SourceCropX) <= CropPixelTolerance &&
                       Math.Abs(expected.SourceCropY - actual.SourceCropY) <= CropPixelTolerance &&
                       Math.Abs(expected.SourceCropWidth - actual.SourceCropWidth) <= CropPixelTolerance &&
                       Math.Abs(expected.SourceCropHeight - actual.SourceCropHeight) <= CropPixelTolerance;
            }

            private static bool ViewportZoomAndCenterMatch(PaneViewportSnapshot expected, PaneViewportSnapshot actual)
            {
                if (expected == null || actual == null)
                {
                    return false;
                }

                return Math.Abs(expected.ZoomFactor - actual.ZoomFactor) <= 0.001d &&
                       Math.Abs(expected.NormalizedCenterX - actual.NormalizedCenterX) <= 0.001d &&
                       Math.Abs(expected.NormalizedCenterY - actual.NormalizedCenterY) <= 0.001d;
            }

            private static string FormatViewportSnapshot(PaneViewportSnapshot snapshot)
            {
                if (snapshot == null)
                {
                    return "(null)";
                }

                return string.Format(
                    CultureInfo.InvariantCulture,
                    "zoom={0:0.###}, center=({1:0.###},{2:0.###}), crop={3},{4},{5},{6} / source={7}x{8}",
                    snapshot.ZoomFactor,
                    snapshot.NormalizedCenterX,
                    snapshot.NormalizedCenterY,
                    snapshot.SourceCropX,
                    snapshot.SourceCropY,
                    snapshot.SourceCropWidth,
                    snapshot.SourceCropHeight,
                    snapshot.SourcePixelWidth,
                    snapshot.SourcePixelHeight);
            }

            private static void CreateSyntheticAudioTrack(
                string ffmpegPath,
                string outputPath,
                TimeSpan duration,
                bool encodeMp3)
            {
                if (encodeMp3 &&
                    (string.IsNullOrWhiteSpace(ffmpegPath) || !File.Exists(ffmpegPath)))
                {
                    throw new FileNotFoundException("FFmpeg tooling was not available for synthetic audio generation.", ffmpegPath);
                }

                if (string.IsNullOrWhiteSpace(outputPath))
                {
                    throw new InvalidOperationException("A synthetic audio output path is required.");
                }

                var resolvedDuration = duration <= TimeSpan.Zero
                    ? TimeSpan.FromSeconds(1d)
                    : duration;
                Directory.CreateDirectory(Path.GetDirectoryName(outputPath) ?? Path.GetTempPath());

                var sampleRate = encodeMp3 ? 44100 : 48000;
                var channelCount = encodeMp3 ? 2 : 1;
                var frequencyHz = encodeMp3 ? 660d : 990d;

                if (!encodeMp3)
                {
                    WriteSyntheticWaveFile(outputPath, resolvedDuration, sampleRate, channelCount, frequencyHz);
                    return;
                }

                var temporaryWavePath = Path.Combine(
                    Path.GetDirectoryName(outputPath) ?? Path.GetTempPath(),
                    Path.GetFileNameWithoutExtension(outputPath) + ".tmp.wav");
                WriteSyntheticWaveFile(temporaryWavePath, resolvedDuration, sampleRate, channelCount, frequencyHz);

                try
                {
                    var arguments = string.Format(
                        CultureInfo.InvariantCulture,
                        "-v error -y -i \"{0}\" -c:a mp3_mf -ar {1} -ac {2} \"{3}\"",
                        temporaryWavePath,
                        sampleRate,
                        channelCount,
                        outputPath);

                    var processResult = FfmpegCliTooling.RunProcess(
                        ffmpegPath,
                        arguments,
                        Path.GetDirectoryName(outputPath));
                    if (processResult.ExitCode != 0 || !File.Exists(outputPath))
                    {
                        throw new InvalidOperationException(
                            FfmpegCliTooling.BuildFailureMessage(
                                processResult,
                                "Synthetic audio generation failed."));
                    }
                }
                finally
                {
                    try
                    {
                        if (File.Exists(temporaryWavePath))
                        {
                            File.Delete(temporaryWavePath);
                        }
                    }
                    catch
                    {
                    }
                }
            }

            private static void WriteSyntheticWaveFile(
                string outputPath,
                TimeSpan duration,
                int sampleRate,
                int channelCount,
                double frequencyHz)
            {
                var totalSamples = Math.Max(1, (int)Math.Ceiling(duration.TotalSeconds * sampleRate));
                const short BitsPerSample = 16;
                const short AudioFormatPcm = 1;
                const short PeakAmplitude = 12000;
                var bytesPerSample = BitsPerSample / 8;
                var blockAlign = (short)(channelCount * bytesPerSample);
                var byteRate = sampleRate * blockAlign;
                var dataSize = totalSamples * blockAlign;

                using (var stream = File.Create(outputPath))
                using (var writer = new BinaryWriter(stream))
                {
                    writer.Write(new byte[] { (byte)'R', (byte)'I', (byte)'F', (byte)'F' });
                    writer.Write(36 + dataSize);
                    writer.Write(new byte[] { (byte)'W', (byte)'A', (byte)'V', (byte)'E' });
                    writer.Write(new byte[] { (byte)'f', (byte)'m', (byte)'t', (byte)' ' });
                    writer.Write(16);
                    writer.Write(AudioFormatPcm);
                    writer.Write((short)channelCount);
                    writer.Write(sampleRate);
                    writer.Write(byteRate);
                    writer.Write(blockAlign);
                    writer.Write(BitsPerSample);
                    writer.Write(new byte[] { (byte)'d', (byte)'a', (byte)'t', (byte)'a' });
                    writer.Write(dataSize);

                    for (var sampleIndex = 0; sampleIndex < totalSamples; sampleIndex++)
                    {
                        var sample = (short)(Math.Sin(
                                                2d * Math.PI * frequencyHz * sampleIndex / sampleRate) *
                                            PeakAmplitude);
                        for (var channelIndex = 0; channelIndex < channelCount; channelIndex++)
                        {
                            writer.Write(sample);
                        }
                    }
                }
            }

            private static RegressionCheckResult EvaluateUiFrameTruth(
                string filePath,
                string name,
                UiSnapshot snapshot,
                TimeSpan requestedTarget,
                bool allowPendingIdentity,
                double elapsedMilliseconds)
            {
                snapshot = snapshot ?? UiSnapshot.Empty;

                if (!snapshot.EngineFrameAbsolute)
                {
                    if (allowPendingIdentity &&
                        (snapshot.CurrentFrameText ?? string.Empty).Contains("--", StringComparison.OrdinalIgnoreCase) &&
                        !snapshot.DisplayedFrameNumber.HasValue)
                    {
                        return Warning(
                            filePath,
                            "ui",
                            CorrectnessCategory,
                            name,
                            string.Format(
                                CultureInfo.InvariantCulture,
                                "Seek to {0} completed before absolute frame identity was ready, and the UI correctly withheld a numeric frame claim.",
                                FormatTime(requestedTarget)),
                            requestedTime: requestedTarget,
                            actualTime: snapshot.EnginePresentationTime,
                            sliderValueSeconds: snapshot.SliderValueSeconds,
                            sliderMaximumSeconds: snapshot.SliderMaximumSeconds,
                            elapsedMilliseconds: elapsedMilliseconds,
                            indexReady: snapshot.IndexReady,
                            usedGlobalIndex: snapshot.UsedGlobalIndex);
                    }

                    return Fail(
                        filePath,
                        "ui",
                        CorrectnessCategory,
                        name,
                        string.Format(
                            CultureInfo.InvariantCulture,
                            "UI seek to {0} completed without absolute engine frame identity, and the visible frame display remained '{1}' / textbox '{2}'.",
                            FormatTime(requestedTarget),
                            snapshot.CurrentFrameText,
                            snapshot.FrameNumberText),
                        requestedTime: requestedTarget,
                        actualTime: snapshot.EnginePresentationTime,
                        sliderValueSeconds: snapshot.SliderValueSeconds,
                        sliderMaximumSeconds: snapshot.SliderMaximumSeconds,
                        elapsedMilliseconds: elapsedMilliseconds,
                        indexReady: snapshot.IndexReady,
                        usedGlobalIndex: snapshot.UsedGlobalIndex);
                }

                var expectedDisplayedFrame = snapshot.EngineFrameIndex.Value + 1L;
                var frameMatches = snapshot.DisplayedFrameNumber.HasValue &&
                    snapshot.DisplayedFrameNumber.Value == expectedDisplayedFrame;
                var timeMatches = string.Equals(snapshot.CurrentPositionText, FormatTime(snapshot.EnginePresentationTime), StringComparison.Ordinal);
                var sliderExpected = Math.Min(snapshot.EnginePresentationTime.TotalSeconds, snapshot.SliderMaximumSeconds);
                var sliderMatches = Math.Abs(snapshot.SliderValueSeconds - sliderExpected) <= Math.Max(0.001d, snapshot.PositionStepSeconds / 10d);

                if (frameMatches && timeMatches && sliderMatches)
                {
                    return Pass(
                        filePath,
                        "ui",
                        CorrectnessCategory,
                        name,
                        string.Format(
                            CultureInfo.InvariantCulture,
                            "UI seek to {0} agreed with engine frame {1} / displayed frame {2}. Slider {3:0.###}/{4:0.###}.",
                            FormatTime(requestedTarget),
                            snapshot.EngineFrameIndex.Value,
                            expectedDisplayedFrame,
                            snapshot.SliderValueSeconds,
                            snapshot.SliderMaximumSeconds),
                        actualFrameIndex: snapshot.EngineFrameIndex,
                        expectedDisplayedFrame: expectedDisplayedFrame,
                        actualDisplayedFrame: snapshot.DisplayedFrameNumber,
                        requestedTime: requestedTarget,
                        actualTime: snapshot.EnginePresentationTime,
                        sliderValueSeconds: snapshot.SliderValueSeconds,
                        sliderMaximumSeconds: snapshot.SliderMaximumSeconds,
                        elapsedMilliseconds: elapsedMilliseconds,
                        indexReady: snapshot.IndexReady,
                        usedGlobalIndex: snapshot.UsedGlobalIndex);
                }

                return Fail(
                    filePath,
                    "ui",
                    CorrectnessCategory,
                    name,
                    string.Format(
                        CultureInfo.InvariantCulture,
                        "UI seek to {0} drifted from engine truth. Engine frame {1}, displayed frame {2}, current-position text '{3}', slider {4:0.###}/{5:0.###}.",
                        FormatTime(requestedTarget),
                        snapshot.EngineFrameIndex.Value,
                        snapshot.DisplayedFrameNumber.HasValue
                            ? snapshot.DisplayedFrameNumber.Value.ToString(CultureInfo.InvariantCulture)
                            : NoneText,
                        snapshot.CurrentPositionText,
                        snapshot.SliderValueSeconds,
                        snapshot.SliderMaximumSeconds),
                    actualFrameIndex: snapshot.EngineFrameIndex,
                    expectedDisplayedFrame: expectedDisplayedFrame,
                    actualDisplayedFrame: snapshot.DisplayedFrameNumber,
                    requestedTime: requestedTarget,
                    actualTime: snapshot.EnginePresentationTime,
                    sliderValueSeconds: snapshot.SliderValueSeconds,
                    sliderMaximumSeconds: snapshot.SliderMaximumSeconds,
                    elapsedMilliseconds: elapsedMilliseconds,
                    indexReady: snapshot.IndexReady,
                    usedGlobalIndex: snapshot.UsedGlobalIndex);
            }

            private static RegressionCheckResult EvaluateUiSnapshot(
                string filePath,
                string name,
                UiSnapshot snapshot,
                long expectedFrameIndex,
                long expectedDisplayedFrame,
                bool requireSliderMaximumAgreement,
                double elapsedMilliseconds)
            {
                snapshot = snapshot ?? UiSnapshot.Empty;
                var frameMatches = snapshot.EngineFrameAbsolute &&
                    snapshot.EngineFrameIndex.HasValue &&
                    snapshot.EngineFrameIndex.Value == expectedFrameIndex &&
                    snapshot.DisplayedFrameNumber.HasValue &&
                    snapshot.DisplayedFrameNumber.Value == expectedDisplayedFrame;
                var timeMatches = string.Equals(snapshot.CurrentPositionText, FormatTime(snapshot.EnginePresentationTime), StringComparison.Ordinal);
                var sliderMatches = !requireSliderMaximumAgreement
                    ? Math.Abs(snapshot.SliderValueSeconds - Math.Min(snapshot.EnginePresentationTime.TotalSeconds, snapshot.SliderMaximumSeconds)) <= Math.Max(0.001d, snapshot.PositionStepSeconds / 10d)
                    : Math.Abs(snapshot.SliderValueSeconds - snapshot.SliderMaximumSeconds) <= Math.Max(0.001d, snapshot.PositionStepSeconds / 10d);

                return frameMatches && timeMatches && sliderMatches
                    ? Pass(
                        filePath,
                        "ui",
                        CorrectnessCategory,
                        name,
                        string.Format(
                            CultureInfo.InvariantCulture,
                            "UI state agreed with engine frame {0} / displayed frame {1}. Slider {2:0.###}/{3:0.###}.",
                            expectedFrameIndex,
                            expectedDisplayedFrame,
                            snapshot.SliderValueSeconds,
                            snapshot.SliderMaximumSeconds),
                        expectedFrameIndex: expectedFrameIndex,
                        actualFrameIndex: snapshot.EngineFrameIndex,
                        expectedDisplayedFrame: expectedDisplayedFrame,
                        actualDisplayedFrame: snapshot.DisplayedFrameNumber,
                        actualTime: snapshot.EnginePresentationTime,
                        sliderValueSeconds: snapshot.SliderValueSeconds,
                        sliderMaximumSeconds: snapshot.SliderMaximumSeconds,
                        elapsedMilliseconds: elapsedMilliseconds,
                        indexReady: snapshot.IndexReady,
                        usedGlobalIndex: snapshot.UsedGlobalIndex)
                    : Fail(
                        filePath,
                        "ui",
                        CorrectnessCategory,
                        name,
                        string.Format(
                            CultureInfo.InvariantCulture,
                            "UI state did not agree with engine truth. Engine frame {0}, displayed frame {1}, slider {2:0.###}/{3:0.###}, current-position '{4}'.",
                            FormatFrameIndex(snapshot.EngineFrameIndex),
                            snapshot.DisplayedFrameNumber.HasValue
                                ? snapshot.DisplayedFrameNumber.Value.ToString(CultureInfo.InvariantCulture)
                                : NoneText,
                            snapshot.SliderValueSeconds,
                            snapshot.SliderMaximumSeconds,
                            snapshot.CurrentPositionText),
                        expectedFrameIndex: expectedFrameIndex,
                        actualFrameIndex: snapshot.EngineFrameIndex,
                        expectedDisplayedFrame: expectedDisplayedFrame,
                        actualDisplayedFrame: snapshot.DisplayedFrameNumber,
                        actualTime: snapshot.EnginePresentationTime,
                        sliderValueSeconds: snapshot.SliderValueSeconds,
                        sliderMaximumSeconds: snapshot.SliderMaximumSeconds,
                        elapsedMilliseconds: elapsedMilliseconds,
                        indexReady: snapshot.IndexReady,
                        usedGlobalIndex: snapshot.UsedGlobalIndex);
            }

            private static async Task WaitForUiIdleAsync(Dispatcher dispatcher)
            {
                var contextIdleTask = dispatcher.InvokeAsync(
                    () => { },
                    DispatcherPriority.ContextIdle).Task;
                var completedTask = await Task.WhenAny(
                        contextIdleTask,
                        Task.Delay(250))
                    .ConfigureAwait(true);
                if (completedTask == contextIdleTask)
                {
                    await contextIdleTask.ConfigureAwait(true);
                    return;
                }

                // Loop playback keeps transport timers and restart work active,
                // so the hidden harness cannot require full application idleness
                // before continuing with the next assertion.
                await dispatcher.InvokeAsync(
                    () => { },
                    DispatcherPriority.Background).Task;
            }

            private sealed class MainWindowController
            {
                private readonly MainWindow _window;
                private readonly MethodInfo _openMediaAsyncMethod;
                private readonly MethodInfo _openMediaToPaneAsyncMethod;
                private readonly MethodInfo _closeMediaAsyncMethod;
                private readonly MethodInfo _commitSliderSeekAsyncMethod;
                private readonly MethodInfo _stepFrameAsyncMethod;
                private readonly MethodInfo _startPlaybackAsyncMethod;
                private readonly MethodInfo _pausePlaybackAsyncMethod;
                private readonly MethodInfo _buildVideoInfoSnapshotMethod;
                private readonly MethodInfo _getEngineForPaneMethod;
                private readonly MethodInfo _replaceAudioTrackAsyncMethod;
                private readonly MethodInfo _setLoopMarkerMethod;
                private readonly MethodInfo _setTimelineLoopMarkerAtAsyncMethod;
                private readonly MethodInfo _clearLoopPointsMethod;
                private readonly MethodInfo _exportLoopClipAsyncMethod;
                private readonly MethodInfo _exportSideBySideCompareAsyncMethod;
                private readonly MethodInfo _getPaneViewportStateMethod;
                private readonly MethodInfo _buildPaneViewportSnapshotMethod;
                private readonly MethodInfo _updatePaneViewportLayoutMethod;
                private readonly MethodInfo _resetZoomForPaneMethod;
                private readonly MethodInfo _zoomInFocusedPaneMethod;
                private readonly MethodInfo _zoomOutFocusedPaneMethod;
                private readonly MethodInfo _setSharedLoopCommandContextMethod;
                private readonly MethodInfo _setPaneLoopCommandContextMethod;
                private readonly MethodInfo _trySelectPaneForShellMethod;
                private readonly MethodInfo _updateWorkspacePanePresentationMethod;
                private readonly FieldInfo _videoReviewEngineField;
                private readonly FieldInfo _isSliderDragActiveField;

                public MainWindowController(MainWindow window)
                {
                    _window = window ?? throw new ArgumentNullException(nameof(window));
                    var windowType = typeof(MainWindow);
                    _openMediaAsyncMethod = RequireMethod(windowType, "OpenMediaAsync", typeof(string));
                    _openMediaToPaneAsyncMethod = RequireMethod(windowType, "OpenMediaAsync", typeof(string), typeof(string));
                    _closeMediaAsyncMethod = RequireMethod(windowType, "CloseMediaAsync");
                    _commitSliderSeekAsyncMethod = RequireMethod(windowType, "CommitSliderSeekAsync", typeof(string), typeof(TimeSpan));
                    _stepFrameAsyncMethod = RequireMethod(windowType, "StepFrameAsync", typeof(int));
                    _startPlaybackAsyncMethod = RequireMethod(windowType, "StartPlaybackAsync", typeof(SynchronizedOperationScope?), typeof(string));
                    _pausePlaybackAsyncMethod = RequireMethod(windowType, "PausePlaybackAsync", typeof(bool));
                    _buildVideoInfoSnapshotMethod = RequireMethod(windowType, "BuildVideoInfoSnapshot", typeof(string), typeof(VideoMediaInfo));
                    _getEngineForPaneMethod = RequireMethod(windowType, "GetEngineForPane", typeof(string));
                    _replaceAudioTrackAsyncMethod = RequireMethod(windowType, "ReplaceAudioTrackAsync", typeof(string), typeof(string));
                    _setLoopMarkerMethod = RequireMethod(windowType, "SetLoopMarker", typeof(LoopPlaybackMarkerEndpoint));
                    _setTimelineLoopMarkerAtAsyncMethod = RequireMethod(windowType, "SetTimelineLoopMarkerAtAsync", typeof(string), typeof(LoopPlaybackMarkerEndpoint), typeof(TimeSpan));
                    _clearLoopPointsMethod = RequireMethod(windowType, "ClearLoopPoints");
                    _exportLoopClipAsyncMethod = RequireMethod(windowType, "ExportLoopClipAsync", typeof(string), typeof(string));
                    _exportSideBySideCompareAsyncMethod = RequireMethod(
                        windowType,
                        "ExportSideBySideCompareAsync",
                        typeof(string),
                        typeof(CompareSideBySideExportMode),
                        typeof(CompareSideBySideExportAudioSource));
                    _getPaneViewportStateMethod = RequireMethod(windowType, "GetPaneViewportState", typeof(string));
                    _buildPaneViewportSnapshotMethod = RequireMethod(windowType, "BuildPaneViewportSnapshot", typeof(string));
                    _updatePaneViewportLayoutMethod = RequireMethod(windowType, "UpdatePaneViewportLayout", typeof(string));
                    _resetZoomForPaneMethod = RequireMethod(windowType, "ResetZoomForPane", typeof(string));
                    _zoomInFocusedPaneMethod = RequireMethod(windowType, "ZoomInFocusedPane");
                    _zoomOutFocusedPaneMethod = RequireMethod(windowType, "ZoomOutFocusedPane");
                    _setSharedLoopCommandContextMethod = RequireMethod(windowType, "SetSharedLoopCommandContext");
                    _setPaneLoopCommandContextMethod = RequireMethod(windowType, "SetPaneLoopCommandContext", typeof(string));
                    _trySelectPaneForShellMethod = RequireMethod(windowType, "TrySelectPaneForShell", typeof(string));
                    _updateWorkspacePanePresentationMethod = RequireMethod(windowType, "UpdateWorkspacePanePresentation");
                    _videoReviewEngineField = RequireField(windowType, "_videoReviewEngine");
                    _isSliderDragActiveField = RequireField(windowType, "_isSliderDragActive");
                }

                public double SliderMaximumSeconds
                {
                    get { return GetPositionSlider().Maximum; }
                }

                public TimeSpan GetQuarterDurationTarget()
                {
                    var engine = GetEngine();
                    return SelectQuarterDurationTarget(engine.MediaInfo.Duration, engine.MediaInfo.PositionStep);
                }

                public TimeSpan GetSliderTargetFromRatio(double ratio)
                {
                    ratio = Math.Max(0d, Math.Min(1d, ratio));
                    return TimeSpan.FromSeconds(SliderMaximumSeconds * ratio);
                }

                public TimeSpan GetPaneTargetFromRatio(string paneId, double ratio)
                {
                    ratio = Math.Max(0d, Math.Min(1d, ratio));
                    var engine = _getEngineForPaneMethod.Invoke(_window, new object[] { paneId }) as FfmpegReviewEngine;
                    var duration = engine != null && engine.MediaInfo != null && engine.MediaInfo.Duration > TimeSpan.Zero
                        ? engine.MediaInfo.Duration
                        : TimeSpan.Zero;
                    return TimeSpan.FromSeconds(duration.TotalSeconds * ratio);
                }

                public async Task OpenAsync(string filePath)
                {
                    await InvokeTaskAsync(_openMediaAsyncMethod, filePath).ConfigureAwait(true);
                    await WaitForUiIdleAsync(_window.Dispatcher).ConfigureAwait(true);
                }

                public async Task OpenAsync(string filePath, string paneId)
                {
                    await InvokeTaskAsync(_openMediaToPaneAsyncMethod, filePath, paneId).ConfigureAwait(true);
                    await WaitForUiIdleAsync(_window.Dispatcher).ConfigureAwait(true);
                }

                public async Task RefreshUiStateAsync()
                {
                    _updateWorkspacePanePresentationMethod.Invoke(_window, null);
                    await WaitForUiIdleAsync(_window.Dispatcher).ConfigureAwait(true);
                }

                public VideoMediaInfo GetPaneMediaInfo(string paneId)
                {
                    var engine = GetPaneEngine(paneId);
                    return engine != null ? engine.MediaInfo ?? VideoMediaInfo.Empty : VideoMediaInfo.Empty;
                }

                public CommandStateSnapshot CaptureAudioInsertionCommandState()
                {
                    var menuItem = (MenuItem)_window.FindName("ReplaceAudioTrackMenuItem");
                    return new CommandStateSnapshot
                    {
                        IsEnabled = menuItem != null && menuItem.IsEnabled,
                        ToolTip = menuItem != null && menuItem.ToolTip != null
                            ? menuItem.ToolTip.ToString() ?? string.Empty
                            : string.Empty
                    };
                }

                public CommandStateSnapshot CaptureResetZoomCommandState()
                {
                    var menuItem = (MenuItem)_window.FindName("ResetZoomMenuItem");
                    return new CommandStateSnapshot
                    {
                        IsEnabled = menuItem != null && menuItem.IsEnabled,
                        ToolTip = menuItem != null && menuItem.ToolTip != null
                            ? menuItem.ToolTip.ToString() ?? string.Empty
                            : string.Empty
                    };
                }

                public CommandStateSnapshot CaptureLinkPaneZoomCommandState()
                {
                    var checkBox = (CheckBox)_window.FindName("LinkPaneZoomCheckBox");
                    return new CommandStateSnapshot
                    {
                        IsEnabled = checkBox != null && checkBox.IsEnabled,
                        IsChecked = checkBox != null && checkBox.IsChecked == true,
                        ToolTip = checkBox != null && checkBox.ToolTip != null
                            ? checkBox.ToolTip.ToString() ?? string.Empty
                            : string.Empty
                    };
                }

                public async Task CloseAsync()
                {
                    await InvokeTaskAsync(_closeMediaAsyncMethod).ConfigureAwait(true);
                    await WaitForUiIdleAsync(_window.Dispatcher).ConfigureAwait(true);
                }

                public async Task StartPlaybackAsync()
                {
                    await StartPlaybackAsync(null, null).ConfigureAwait(true);
                }

                public async Task StartPlaybackAsync(SynchronizedOperationScope? operationScope, string paneId)
                {
                    var result = _startPlaybackAsyncMethod.Invoke(_window, new object[] { operationScope, paneId });
                    var task = result as Task;
                    if (task == null)
                    {
                        return;
                    }

                    var completedTask = await Task.WhenAny(
                            task,
                            Task.Delay(TimeSpan.FromSeconds(2d)))
                        .ConfigureAwait(true);
                    if (completedTask == task)
                    {
                        await task.ConfigureAwait(true);
                        return;
                    }

                    Trace("UI harness start playback task timed out while waiting for completion. Continuing with observed transport state.");
                }

                public async Task PausePlaybackAsync()
                {
                    await InvokeTaskWithTimeoutAsync(
                            _pausePlaybackAsyncMethod,
                            TimeSpan.FromSeconds(2d),
                            "pause playback",
                            true)
                        .ConfigureAwait(true);
                }

                public async Task SetLoopPlaybackEnabledAsync(bool isEnabled)
                {
                    var loopPlaybackMenuItem = (MenuItem)_window.FindName("LoopPlaybackMenuItem");
                    if (loopPlaybackMenuItem == null)
                    {
                        throw new InvalidOperationException("Loop playback menu item was not found.");
                    }

                    loopPlaybackMenuItem.IsChecked = isEnabled;
                    await WaitForUiIdleAsync(_window.Dispatcher).ConfigureAwait(true);
                }

                public async Task SetSharedLoopMarkerAsync(LoopPlaybackMarkerEndpoint endpoint)
                {
                    _setSharedLoopCommandContextMethod.Invoke(_window, null);
                    _setLoopMarkerMethod.Invoke(_window, new object[] { endpoint });
                    await WaitForUiIdleAsync(_window.Dispatcher).ConfigureAwait(true);
                }

                public async Task<bool> SetTimelineLoopMarkerAtAsync(string paneId, LoopPlaybackMarkerEndpoint endpoint, TimeSpan target)
                {
                    var result = await InvokeTaskWithResultAsync<bool>(
                            _setTimelineLoopMarkerAtAsyncMethod,
                            paneId,
                            endpoint,
                            target)
                        .ConfigureAwait(true);
                    await WaitForUiIdleAsync(_window.Dispatcher).ConfigureAwait(true);
                    return result;
                }

                public async Task ClearLoopPointsAsync(string paneId = null)
                {
                    if (string.IsNullOrWhiteSpace(paneId))
                    {
                        _setSharedLoopCommandContextMethod.Invoke(_window, null);
                    }
                    else
                    {
                        _setPaneLoopCommandContextMethod.Invoke(_window, new object[] { paneId });
                    }

                    _clearLoopPointsMethod.Invoke(_window, null);
                    await WaitForUiIdleAsync(_window.Dispatcher).ConfigureAwait(true);
                }

                public async Task<AudioInsertionResult> ReplaceAudioTrackAsync(string replacementAudioFilePath, string outputPath)
                {
                    return await InvokeTaskWithResultAsync<AudioInsertionResult>(
                            _replaceAudioTrackAsyncMethod,
                            replacementAudioFilePath,
                            outputPath)
                        .ConfigureAwait(true);
                }

                public async Task<ClipExportResult> ExportLoopClipAsync(string outputPath, string paneId = null)
                {
                    return await InvokeTaskWithResultAsync<ClipExportResult>(_exportLoopClipAsyncMethod, outputPath, paneId).ConfigureAwait(true);
                }

                public async Task<CompareSideBySideExportResult> ExportSideBySideCompareAsync(
                    string outputPath,
                    CompareSideBySideExportMode mode,
                    CompareSideBySideExportAudioSource audioSource)
                {
                    return await InvokeTaskWithResultAsync<CompareSideBySideExportResult>(
                            _exportSideBySideCompareAsyncMethod,
                            outputPath,
                            mode,
                            audioSource)
                        .ConfigureAwait(true);
                }

                public async Task<IReadOnlyList<UiSnapshot>> CapturePlaybackObservationsAsync(
                    int observationCount,
                    TimeSpan interval,
                    CancellationToken cancellationToken)
                {
                    var observations = new List<UiSnapshot>();
                    var sampleCount = Math.Max(0, observationCount);
                    for (var observationIndex = 0; observationIndex < sampleCount; observationIndex++)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        await Task.Delay(interval, cancellationToken).ConfigureAwait(false);
                        observations.Add(await InvokeOnUiThreadAsync(CaptureSnapshot).ConfigureAwait(false));
                    }

                    return observations;
                }

                public async Task<UiSnapshot> CaptureSnapshotAfterDelayAsync(
                    TimeSpan delay,
                    CancellationToken cancellationToken)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
                    return await InvokeOnUiThreadAsync(CaptureSnapshot).ConfigureAwait(false);
                }

                public async Task SetCompareModeAsync(bool isEnabled)
                {
                    var compareModeCheckBox = (CheckBox)_window.FindName("CompareModeCheckBox");
                    if (compareModeCheckBox == null)
                    {
                        throw new InvalidOperationException("Compare mode checkbox was not found.");
                    }

                    compareModeCheckBox.IsChecked = isEnabled;
                    await WaitForUiIdleAsync(_window.Dispatcher).ConfigureAwait(true);
                    await Task.Delay(50).ConfigureAwait(true);
                    await WaitForUiIdleAsync(_window.Dispatcher).ConfigureAwait(true);
                }

                public async Task SetLinkedPaneZoomAsync(bool isEnabled)
                {
                    var linkPaneZoomCheckBox = (CheckBox)_window.FindName("LinkPaneZoomCheckBox");
                    if (linkPaneZoomCheckBox == null)
                    {
                        throw new InvalidOperationException("Link pane zoom checkbox was not found.");
                    }

                    linkPaneZoomCheckBox.IsChecked = isEnabled;
                    await WaitForUiIdleAsync(_window.Dispatcher).ConfigureAwait(true);
                }

                public async Task<bool> SelectPaneAsync(string paneId)
                {
                    var selected = InvokeWithResult<bool>(_trySelectPaneForShellMethod, paneId);
                    await RefreshUiStateAsync().ConfigureAwait(true);
                    return selected;
                }

                public async Task SetPaneViewportAsync(
                    string paneId,
                    double zoomFactor,
                    double normalizedCenterX,
                    double normalizedCenterY)
                {
                    var resolvedPaneId = string.IsNullOrWhiteSpace(paneId)
                        ? PrimaryPaneId
                        : paneId;
                    await SelectPaneAsync(resolvedPaneId).ConfigureAwait(true);

                    var viewportState = _getPaneViewportStateMethod.Invoke(_window, new object[] { resolvedPaneId });
                    if (viewportState == null)
                    {
                        throw new InvalidOperationException("Pane viewport state was unavailable.");
                    }

                    var viewportStateType = viewportState.GetType();
                    viewportStateType.GetProperty("ZoomFactor")?.SetValue(viewportState, zoomFactor);
                    viewportStateType.GetProperty("NormalizedCenter")?.SetValue(
                        viewportState,
                        new Point(normalizedCenterX, normalizedCenterY));
                    viewportStateType.GetProperty("IsPanActive")?.SetValue(viewportState, false);
                    viewportStateType.GetProperty("PanAnchorNormalizedCenter")?.SetValue(
                        viewportState,
                        new Point(normalizedCenterX, normalizedCenterY));
                    _updatePaneViewportLayoutMethod.Invoke(_window, new object[] { resolvedPaneId });
                    await RefreshUiStateAsync().ConfigureAwait(true);
                }

                public async Task ResetPaneZoomAsync(string paneId)
                {
                    var resolvedPaneId = string.IsNullOrWhiteSpace(paneId)
                        ? PrimaryPaneId
                        : paneId;
                    _resetZoomForPaneMethod.Invoke(_window, new object[] { resolvedPaneId });
                    await RefreshUiStateAsync().ConfigureAwait(true);
                }

                public async Task ZoomInFocusedPaneAsync(string paneId = null)
                {
                    if (!string.IsNullOrWhiteSpace(paneId))
                    {
                        await SelectPaneAsync(paneId).ConfigureAwait(true);
                    }

                    _zoomInFocusedPaneMethod.Invoke(_window, null);
                    await RefreshUiStateAsync().ConfigureAwait(true);
                }

                public async Task ZoomOutFocusedPaneAsync(string paneId = null)
                {
                    if (!string.IsNullOrWhiteSpace(paneId))
                    {
                        await SelectPaneAsync(paneId).ConfigureAwait(true);
                    }

                    _zoomOutFocusedPaneMethod.Invoke(_window, null);
                    await RefreshUiStateAsync().ConfigureAwait(true);
                }

                public PaneViewportSnapshot CapturePaneViewportSnapshot(string paneId)
                {
                    var resolvedPaneId = string.IsNullOrWhiteSpace(paneId)
                        ? PrimaryPaneId
                        : paneId;
                    return InvokeWithResult<PaneViewportSnapshot>(_buildPaneViewportSnapshotMethod, resolvedPaneId);
                }

                public LoopUiSnapshot CaptureMainLoopUiSnapshot()
                {
                    return CaptureLoopUiSnapshot(
                        (TextBlock)_window.FindName("LoopStatusTextBlock"),
                        (LoopRangeOverlay)_window.FindName("PositionLoopRangeOverlay"));
                }

                public LoopUiSnapshot CapturePaneLoopUiSnapshot(string paneId)
                {
                    if (string.Equals(paneId, ComparePaneId, StringComparison.Ordinal))
                    {
                        return CaptureLoopUiSnapshot(
                            (TextBlock)_window.FindName("ComparePaneLoopStatusTextBlock"),
                            (LoopRangeOverlay)_window.FindName("ComparePaneLoopRangeOverlay"));
                    }

                    return CaptureLoopUiSnapshot(
                        (TextBlock)_window.FindName("PrimaryPaneLoopStatusTextBlock"),
                        (LoopRangeOverlay)_window.FindName("PrimaryPaneLoopRangeOverlay"));
                }

                public async Task<UiSnapshot> CommitSliderSeekAsync(string interactionName, TimeSpan target)
                {
                    var measured = await MeasureAsync(
                            async () => await InvokeTaskAsync(_commitSliderSeekAsyncMethod, interactionName, target).ConfigureAwait(true))
                        .ConfigureAwait(true);
                    var snapshot = CaptureSnapshot();
                    snapshot.ElapsedMilliseconds = measured.Elapsed.TotalMilliseconds;
                    return snapshot;
                }

                public async Task<UiSnapshot> DragSliderSeekAsync(TimeSpan target)
                {
                    var slider = GetPositionSlider();
                    _isSliderDragActiveField.SetValue(_window, true);
                    slider.Value = Math.Max(slider.Minimum, Math.Min(slider.Maximum, target.TotalSeconds));
                    await WaitForUiIdleAsync(_window.Dispatcher).ConfigureAwait(true);
                    _isSliderDragActiveField.SetValue(_window, false);
                    return await CommitSliderSeekAsync("drag", TimeSpan.FromSeconds(slider.Value)).ConfigureAwait(true);
                }

                public async Task<InspectorUiResult> OpenVideoInfoWindowAsync()
                {
                    var engine = GetEngine();
                    var mediaInfo = engine.MediaInfo ?? VideoMediaInfo.Empty;
                    var snapshot = (VideoInfoSnapshot)_buildVideoInfoSnapshotMethod.Invoke(
                        _window,
                        new object[] { "Primary", mediaInfo });
                    InspectorUiResult result = null;

                    var measured = await MeasureAsync(
                            async () =>
                            {
                                var infoWindow = new VideoInfoWindow(snapshot)
                                {
                                    Owner = _window,
                                    ShowInTaskbar = false,
                                    WindowStartupLocation = WindowStartupLocation.Manual,
                                    Left = -19000,
                                    Top = -19000
                                };

                                try
                                {
                                    infoWindow.Show();
                                    await WaitForUiIdleAsync(infoWindow.Dispatcher).ConfigureAwait(true);

                                    result = new InspectorUiResult
                                    {
                                        HeadingText = ((TextBlock)infoWindow.FindName("HeadingTextBlock"))?.Text ?? string.Empty,
                                        SummaryFieldCount = ((ItemsControl)infoWindow.FindName("SummaryItemsControl"))?.Items.Count ?? 0,
                                        VideoFieldCount = ((ItemsControl)infoWindow.FindName("VideoItemsControl"))?.Items.Count ?? 0,
                                        AudioFieldCount = ((ItemsControl)infoWindow.FindName("AudioItemsControl"))?.Items.Count ?? 0,
                                        AudioEmptyMessage = ((TextBlock)infoWindow.FindName("AudioEmptyTextBlock"))?.Text ?? string.Empty,
                                        AdvancedFieldCount = ((ItemsControl)infoWindow.FindName("AdvancedItemsControl"))?.Items.Count ?? 0
                                    };
                                }
                                finally
                                {
                                    if (infoWindow.IsVisible)
                                    {
                                        infoWindow.Close();
                                        await WaitForUiIdleAsync(_window.Dispatcher).ConfigureAwait(true);
                                    }
                                }
                            })
                        .ConfigureAwait(true);

                    if (result == null)
                    {
                        result = new InspectorUiResult();
                    }

                    result.ElapsedMilliseconds = measured.Elapsed.TotalMilliseconds;
                    return result;
                }

                public async Task<IndexReadyUiResult> WaitForIndexReadyUiAsync(TimeSpan timeout, CancellationToken cancellationToken)
                {
                    var stopwatch = Stopwatch.StartNew();
                    while (stopwatch.Elapsed < timeout)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        var engine = GetEngine();
                        if (engine.IsGlobalFrameIndexAvailable)
                        {
                            stopwatch.Stop();
                            return new IndexReadyUiResult(true, stopwatch.Elapsed.TotalMilliseconds, engine.IndexedFrameCount);
                        }

                        await Task.Delay(50, cancellationToken).ConfigureAwait(true);
                    }

                    stopwatch.Stop();
                    var finalEngine = GetEngine();
                    return new IndexReadyUiResult(finalEngine.IsGlobalFrameIndexAvailable, stopwatch.Elapsed.TotalMilliseconds, finalEngine.IndexedFrameCount);
                }

                public async Task<LoopUiSnapshot> WaitForLoopUiReadyAsync(string paneId, TimeSpan timeout, CancellationToken cancellationToken)
                {
                    var stopwatch = Stopwatch.StartNew();
                    LoopUiSnapshot lastSnapshot = null;
                    while (stopwatch.Elapsed < timeout)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        lastSnapshot = string.IsNullOrWhiteSpace(paneId)
                            ? CaptureMainLoopUiSnapshot()
                            : CapturePaneLoopUiSnapshot(paneId);
                        if (IsLoopUiReady(lastSnapshot))
                        {
                            return lastSnapshot;
                        }

                        await Task.Delay(50, cancellationToken).ConfigureAwait(true);
                    }

                    return lastSnapshot ?? new LoopUiSnapshot
                    {
                        StatusText = string.Empty,
                        InPosition = double.NaN,
                        OutPosition = double.NaN,
                        IsInPending = true,
                        IsOutPending = true,
                        IsInvalid = true
                    };
                }

                public async Task<bool> WaitForLoopMarkerReadyAsync(
                    string paneId,
                    LoopPlaybackMarkerEndpoint endpoint,
                    TimeSpan timeout,
                    CancellationToken cancellationToken)
                {
                    var stopwatch = Stopwatch.StartNew();
                    while (stopwatch.Elapsed < timeout)
                    {
                        cancellationToken.ThrowIfCancellationRequested();

                        var snapshot = string.IsNullOrWhiteSpace(paneId)
                            ? CaptureMainLoopUiSnapshot()
                            : CapturePaneLoopUiSnapshot(paneId);
                        var isPending = endpoint == LoopPlaybackMarkerEndpoint.In
                            ? snapshot.IsInPending
                            : snapshot.IsOutPending;
                        var hasPosition = endpoint == LoopPlaybackMarkerEndpoint.In
                            ? !double.IsNaN(snapshot.InPosition)
                            : !double.IsNaN(snapshot.OutPosition);
                        if (!isPending && hasPosition && !snapshot.IsInvalid)
                        {
                            return true;
                        }

                        await Task.Delay(50, cancellationToken).ConfigureAwait(true);
                    }

                    return false;
                }

                public async Task StepFrameAsync(int delta)
                {
                    await InvokeTaskAsync(_stepFrameAsyncMethod, delta).ConfigureAwait(true);
                }

                public async Task<IReadOnlyCollection<RegressionCheckResult>> RunUiStepRoundTripAsync(
                    string filePath,
                    string prefix,
                    CancellationToken cancellationToken)
                {
                    var checks = new List<RegressionCheckResult>();
                    var before = CaptureSnapshot();
                    if (!before.EngineFrameAbsolute || !before.EngineFrameIndex.HasValue)
                    {
                        checks.Add(Fail(
                            filePath,
                            "ui",
                            CorrectnessCategory,
                            prefix + "-step-roundtrip",
                            "UI step roundtrip started without an absolute engine frame."));
                        return checks;
                    }

                    var startFrame = before.EngineFrameIndex.Value;
                    if (startFrame <= 0L)
                    {
                        checks.Add(Warning(
                            filePath,
                            "ui",
                            CorrectnessCategory,
                            prefix + "-step-roundtrip",
                            "UI step roundtrip skipped because the current frame was already at the start boundary."));
                        return checks;
                    }

                    var backward = await MeasureAsync(async () => await InvokeTaskAsync(_stepFrameAsyncMethod, -1).ConfigureAwait(true)).ConfigureAwait(true);
                    var backwardSnapshot = CaptureSnapshot();
                    checks.Add(EvaluateUiSnapshot(
                        filePath,
                        prefix + "-step-backward",
                        backwardSnapshot,
                        startFrame - 1L,
                        startFrame,
                        false,
                        backward.Elapsed.TotalMilliseconds));

                    var forward = await MeasureAsync(async () => await InvokeTaskAsync(_stepFrameAsyncMethod, 1).ConfigureAwait(true)).ConfigureAwait(true);
                    var forwardSnapshot = CaptureSnapshot();
                    checks.Add(EvaluateUiSnapshot(
                        filePath,
                        prefix + "-step-forward",
                        forwardSnapshot,
                        startFrame,
                        startFrame + 1L,
                        false,
                        forward.Elapsed.TotalMilliseconds));

                    return checks;
                }

                public UiSnapshot CaptureSnapshot()
                {
                    var engine = GetEngine();
                    var frameNumberTextBox = GetFrameNumberTextBox();
                    var currentFrameText = GetTextBlock("CurrentFrameTextBlock").Text ?? string.Empty;
                    var currentPositionText = GetTextBlock("CurrentPositionTextBlock").Text ?? string.Empty;
                    var durationText = GetTextBlock("DurationTextBlock").Text ?? string.Empty;
                    var playbackStateText = GetTextBlock("PlaybackStateTextBlock").Text ?? string.Empty;
                    var cacheStatusText = GetTextBlock("CacheStatusTextBlock").Text ?? string.Empty;
                    var mediaSummaryText = GetTextBlock("MediaSummaryTextBlock").Text ?? string.Empty;
                    var slider = GetPositionSlider();

                    long displayedFrameNumber;
                    long? parsedDisplayedFrame = long.TryParse(frameNumberTextBox.Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out displayedFrameNumber)
                        ? (long?)displayedFrameNumber
                        : null;

                    var position = engine.Position ?? ReviewPosition.Empty;

                    return new UiSnapshot
                    {
                        CurrentFrameText = currentFrameText,
                        FrameNumberText = frameNumberTextBox.Text ?? string.Empty,
                        DisplayedFrameNumber = parsedDisplayedFrame,
                        CurrentPositionText = currentPositionText,
                        DurationText = durationText,
                        PlaybackStateText = playbackStateText,
                        CacheStatusText = cacheStatusText,
                        MediaSummaryText = mediaSummaryText,
                        SliderValueSeconds = slider.Value,
                        SliderMaximumSeconds = slider.Maximum,
                        EngineFrameIndex = position.FrameIndex,
                        EngineFrameAbsolute = position.IsFrameIndexAbsolute,
                        EnginePresentationTime = position.PresentationTime,
                        IndexReady = engine.IsGlobalFrameIndexAvailable,
                        IndexedFrameCount = engine.IndexedFrameCount,
                        UsedGlobalIndex = engine.LastOperationUsedGlobalIndex,
                        PositionStepSeconds = engine.MediaInfo.PositionStep > TimeSpan.Zero
                            ? engine.MediaInfo.PositionStep.TotalSeconds
                            : (engine.MediaInfo.FramesPerSecond > 0d ? 1d / engine.MediaInfo.FramesPerSecond : 0.033333333d)
                    };
                }

                private static LoopUiSnapshot CaptureLoopUiSnapshot(TextBlock statusTextBlock, LoopRangeOverlay loopOverlay)
                {
                    return new LoopUiSnapshot
                    {
                        StatusText = statusTextBlock != null ? statusTextBlock.Text ?? string.Empty : string.Empty,
                        InPosition = loopOverlay != null ? loopOverlay.InPosition : double.NaN,
                        OutPosition = loopOverlay != null ? loopOverlay.OutPosition : double.NaN,
                        IsInPending = loopOverlay != null && loopOverlay.IsInPending,
                        IsOutPending = loopOverlay != null && loopOverlay.IsOutPending,
                        IsInvalid = loopOverlay != null && loopOverlay.IsInvalid
                    };
                }

                private static bool IsLoopUiReady(LoopUiSnapshot snapshot)
                {
                    return snapshot != null &&
                           !snapshot.IsInvalid &&
                           !snapshot.IsInPending &&
                           !snapshot.IsOutPending &&
                           !double.IsNaN(snapshot.InPosition) &&
                           !double.IsNaN(snapshot.OutPosition) &&
                           snapshot.OutPosition >= snapshot.InPosition;
                }

                private FfmpegReviewEngine GetEngine()
                {
                    return (FfmpegReviewEngine)_videoReviewEngineField.GetValue(_window);
                }

                private FfmpegReviewEngine GetPaneEngine(string paneId)
                {
                    if (string.IsNullOrWhiteSpace(paneId) ||
                        string.Equals(paneId, PrimaryPaneId, StringComparison.Ordinal))
                    {
                        return GetEngine();
                    }

                    return _getEngineForPaneMethod.Invoke(_window, new object[] { paneId }) as FfmpegReviewEngine;
                }

                private TextBox GetFrameNumberTextBox()
                {
                    return (TextBox)_window.FindName("FrameNumberTextBox");
                }

                private TextBlock GetTextBlock(string name)
                {
                    return (TextBlock)_window.FindName(name);
                }

                private Slider GetPositionSlider()
                {
                    return (Slider)_window.FindName("PositionSlider");
                }

                private async Task InvokeTaskAsync(MethodInfo method, params object[] arguments)
                {
                    var result = method.Invoke(_window, arguments);
                    var task = result as Task;
                    if (task != null)
                    {
                        await task.ConfigureAwait(true);
                    }

                    await WaitForUiIdleAsync(_window.Dispatcher).ConfigureAwait(true);
                }

                private async Task InvokeTaskWithTimeoutAsync(
                    MethodInfo method,
                    TimeSpan timeout,
                    string operationName,
                    params object[] arguments)
                {
                    var result = method.Invoke(_window, arguments);
                    var task = result as Task;
                    if (task == null)
                    {
                        return;
                    }

                    var completedTask = await Task.WhenAny(task, Task.Delay(timeout)).ConfigureAwait(true);
                    if (completedTask == task)
                    {
                        await task.ConfigureAwait(true);
                        return;
                    }

                    Trace("UI harness " + operationName + " task timed out while waiting for completion. Continuing with observed transport state.");
                }

                private async Task<T> InvokeTaskWithResultAsync<T>(MethodInfo method, params object[] arguments)
                {
                    var result = method.Invoke(_window, arguments);
                    var task = result as Task<T>;
                    if (task == null)
                    {
                        await WaitForUiIdleAsync(_window.Dispatcher).ConfigureAwait(true);
                        return default(T);
                    }

                    var awaitedResult = await task.ConfigureAwait(true);
                    await WaitForUiIdleAsync(_window.Dispatcher).ConfigureAwait(true);
                    return awaitedResult;
                }

                private T InvokeWithResult<T>(MethodInfo method, params object[] arguments)
                {
                    var result = method.Invoke(_window, arguments);
                    if (result == null)
                    {
                        return default(T);
                    }

                    return (T)result;
                }

                private Task<T> InvokeOnUiThreadAsync<T>(Func<T> callback)
                {
                    return _window.Dispatcher.InvokeAsync(callback).Task;
                }

                private static MethodInfo RequireMethod(Type type, string name, params Type[] parameterTypes)
                {
                    var method = type.GetMethod(
                        name,
                        BindingFlags.Instance | BindingFlags.Static | BindingFlags.NonPublic,
                        null,
                        parameterTypes,
                        null);
                    if (method == null)
                    {
                        throw new MissingMethodException(type.FullName, name);
                    }

                    return method;
                }

                private static FieldInfo RequireField(Type type, string name)
                {
                    var field = type.GetField(name, BindingFlags.Instance | BindingFlags.NonPublic);
                    if (field == null)
                    {
                        throw new MissingFieldException(type.FullName, name);
                    }

                    return field;
                }
            }

            private sealed class CommandStateSnapshot
            {
                public bool IsEnabled { get; init; }

                public bool IsChecked { get; init; }

                public string ToolTip { get; init; } = string.Empty;
            }

            private sealed class IndexReadyUiResult
            {
                public IndexReadyUiResult(bool ready, double elapsedMilliseconds, long indexedFrameCount)
                {
                    Ready = ready;
                    ElapsedMilliseconds = elapsedMilliseconds;
                    IndexedFrameCount = indexedFrameCount;
                }

                public bool Ready { get; }

                public double ElapsedMilliseconds { get; }

                public long IndexedFrameCount { get; }
            }

            internal sealed class UiRegressionResult
            {
                public UiRegressionResult(
                    IEnumerable<RegressionCheckResult> checks,
                    IEnumerable<string> notes,
                    RegressionMetrics metrics)
                {
                    Checks = checks != null ? checks.ToArray() : Array.Empty<RegressionCheckResult>();
                    Notes = notes != null ? notes.ToArray() : Array.Empty<string>();
                    Metrics = metrics ?? new RegressionMetrics();
                }

                public RegressionCheckResult[] Checks { get; }

                public string[] Notes { get; }

                public RegressionMetrics Metrics { get; }
            }

            private sealed class UiSnapshot
            {
                public static UiSnapshot Empty { get; } = new UiSnapshot();

                public string CurrentFrameText { get; set; }

                public string FrameNumberText { get; set; }

                public long? DisplayedFrameNumber { get; set; }

                public string CurrentPositionText { get; set; }

                public string DurationText { get; set; }

                public string PlaybackStateText { get; set; }

                public string CacheStatusText { get; set; }

                public string MediaSummaryText { get; set; }

                public double SliderValueSeconds { get; set; }

                public double SliderMaximumSeconds { get; set; }

                public long? EngineFrameIndex { get; set; }

                public bool EngineFrameAbsolute { get; set; }

                public TimeSpan EnginePresentationTime { get; set; }

                public bool IndexReady { get; set; }

                public long IndexedFrameCount { get; set; }

                public bool UsedGlobalIndex { get; set; }

                public double PositionStepSeconds { get; set; }

                public double ElapsedMilliseconds { get; set; }
            }

            private sealed class InspectorUiResult
            {
                public string HeadingText { get; set; }

                public int SummaryFieldCount { get; set; }

                public int VideoFieldCount { get; set; }

                public int AudioFieldCount { get; set; }

                public string AudioEmptyMessage { get; set; }

                public int AdvancedFieldCount { get; set; }

                public double ElapsedMilliseconds { get; set; }

                public bool HasRenderableContent
                {
                    get
                    {
                        return !string.IsNullOrWhiteSpace(HeadingText) &&
                               SummaryFieldCount > 0 &&
                               VideoFieldCount > 0 &&
                               (AudioFieldCount > 0 || !string.IsNullOrWhiteSpace(AudioEmptyMessage));
                    }
                }
            }

            private sealed class LoopUiSnapshot
            {
                public string StatusText { get; set; }

                public double InPosition { get; set; }

                public double OutPosition { get; set; }

                public bool IsInPending { get; set; }

                public bool IsOutPending { get; set; }

                public bool IsInvalid { get; set; }
            }
        }
    }

    public sealed class RegressionSuiteReport
    {
        public RegressionSuiteReport(
            string generatedAtUtc,
            string packagedOutputDirectory,
            string packagedArtifactPath,
            RegressionPackagingReport packaging,
            RegressionFileReport[] fileResults,
            RegressionSummary summary)
        {
            GeneratedAtUtc = generatedAtUtc ?? string.Empty;
            PackagedOutputDirectory = packagedOutputDirectory ?? string.Empty;
            PackagedArtifactPath = packagedArtifactPath ?? string.Empty;
            Packaging = packaging;
            FileResults = fileResults ?? Array.Empty<RegressionFileReport>();
            Summary = summary ?? new RegressionSummary(0, 0, 0, 0, 0);
        }

        public string GeneratedAtUtc { get; }

        public string PackagedOutputDirectory { get; }

        public string PackagedArtifactPath { get; }

        public RegressionPackagingReport Packaging { get; }

        public RegressionFileReport[] FileResults { get; }

        public RegressionSummary Summary { get; }
    }

    public sealed class RegressionPackagingReport
    {
        [SuppressMessage(
            "Major Code Smell",
            "S107:Methods should not have too many parameters",
            Justification = "Regression packaging reports are immutable diagnostics snapshots with explicit scalar fields for stable serialization and review output.")]
        public RegressionPackagingReport(
            string outputDirectory,
            string artifactPath,
            string[] expectedRuntimeFiles,
            string[] presentRuntimeFiles,
            string[] missingRuntimeFiles,
            string[] staleRuntimeFiles,
            RegressionCheckResult[] checks,
            string classification)
        {
            OutputDirectory = outputDirectory ?? string.Empty;
            ArtifactPath = artifactPath ?? string.Empty;
            ExpectedRuntimeFiles = expectedRuntimeFiles ?? Array.Empty<string>();
            PresentRuntimeFiles = presentRuntimeFiles ?? Array.Empty<string>();
            MissingRuntimeFiles = missingRuntimeFiles ?? Array.Empty<string>();
            StaleRuntimeFiles = staleRuntimeFiles ?? Array.Empty<string>();
            Checks = checks ?? Array.Empty<RegressionCheckResult>();
            Classification = classification ?? string.Empty;
        }

        public string OutputDirectory { get; }

        public string ArtifactPath { get; }

        public string[] ExpectedRuntimeFiles { get; }

        public string[] PresentRuntimeFiles { get; }

        public string[] MissingRuntimeFiles { get; }

        public string[] StaleRuntimeFiles { get; }

        public RegressionCheckResult[] Checks { get; }

        public string Classification { get; }
    }

    public sealed class RegressionFileReport
    {
        [SuppressMessage(
            "Major Code Smell",
            "S107:Methods should not have too many parameters",
            Justification = "Regression file reports are immutable diagnostics transport models that keep scalar fields explicit for reporting and corpus review.")]
        public RegressionFileReport(
            string filePath,
            string fileName,
            RegressionMediaProfile mediaProfile,
            RegressionDecodeProfile decodeProfile,
            RegressionCheckResult[] engineChecks,
            RegressionCheckResult[] uiChecks,
            RegressionMetrics engineMetrics,
            RegressionMetrics uiMetrics,
            string[] notes)
        {
            FilePath = filePath ?? string.Empty;
            FileName = fileName ?? string.Empty;
            MediaProfile = mediaProfile ?? new RegressionMediaProfile(string.Empty, 0, 0, string.Empty, 0d, false, false, string.Empty, 0, 0);
            DecodeProfile = decodeProfile ?? RegressionDecodeProfile.Empty;
            EngineChecks = engineChecks ?? Array.Empty<RegressionCheckResult>();
            UiChecks = uiChecks ?? Array.Empty<RegressionCheckResult>();
            EngineMetrics = engineMetrics ?? new RegressionMetrics();
            UiMetrics = uiMetrics ?? new RegressionMetrics();
            Notes = notes ?? Array.Empty<string>();
        }

        public string FilePath { get; }

        public string FileName { get; }

        public RegressionMediaProfile MediaProfile { get; }

        public RegressionDecodeProfile DecodeProfile { get; }

        public RegressionCheckResult[] EngineChecks { get; }

        public RegressionCheckResult[] UiChecks { get; }

        public RegressionMetrics EngineMetrics { get; }

        public RegressionMetrics UiMetrics { get; }

        public string[] Notes { get; }
    }

    public sealed class RegressionDecodeProfile
    {
        public static RegressionDecodeProfile Empty { get; } =
            new RegressionDecodeProfile(
                string.Empty,
                string.Empty,
                false,
                string.Empty,
                string.Empty,
                string.Empty,
                string.Empty,
                0,
                0L,
                0L,
                0,
                0,
                0,
                0,
                0L,
                0,
                0,
                0,
                0,
                0d,
                0d,
                0d,
                string.Empty,
                false,
                string.Empty,
                string.Empty,
                0d);

        [SuppressMessage(
            "Major Code Smell",
            "S107:Methods should not have too many parameters",
            Justification = "Regression decode profiles are immutable diagnostics snapshots that intentionally mirror explicit decode counters and timings without extra wrapper layers.")]
        public RegressionDecodeProfile(
            string activeDecodeBackend,
            string actualBackendUsed,
            bool isGpuActive,
            string gpuCapabilityStatus,
            string gpuFallbackReason,
            string budgetBand,
            string hostResourceClass,
            int operationalQueueDepth,
            long sessionDecodedFrameCacheBudgetBytes,
            long decodedFrameCacheBudgetBytes,
            int configuredPreviousCachedFrames,
            int configuredForwardCachedFrames,
            int observedPreviousCachedFrames,
            int observedForwardCachedFrames,
            long observedApproximateCacheBytes,
            int backwardStepCacheHits,
            int backwardStepReconstructionCount,
            int forwardStepCacheHits,
            int forwardStepReconstructionCount,
            double forwardStepCacheHitRate,
            double hardwareFrameTransferMilliseconds,
            double bgraConversionMilliseconds,
            string globalFrameIndexStatus,
            bool isGlobalFrameIndexAvailable,
            string lastObservedCacheRefillReason,
            string lastObservedCacheRefillMode,
            double lastObservedCacheRefillMilliseconds)
        {
            ActiveDecodeBackend = activeDecodeBackend ?? string.Empty;
            ActualBackendUsed = actualBackendUsed ?? string.Empty;
            IsGpuActive = isGpuActive;
            GpuCapabilityStatus = gpuCapabilityStatus ?? string.Empty;
            GpuFallbackReason = gpuFallbackReason ?? string.Empty;
            BudgetBand = budgetBand ?? string.Empty;
            HostResourceClass = hostResourceClass ?? string.Empty;
            OperationalQueueDepth = operationalQueueDepth;
            SessionDecodedFrameCacheBudgetBytes = sessionDecodedFrameCacheBudgetBytes;
            DecodedFrameCacheBudgetBytes = decodedFrameCacheBudgetBytes;
            ConfiguredPreviousCachedFrames = configuredPreviousCachedFrames;
            ConfiguredForwardCachedFrames = configuredForwardCachedFrames;
            ObservedPreviousCachedFrames = observedPreviousCachedFrames;
            ObservedForwardCachedFrames = observedForwardCachedFrames;
            ObservedApproximateCacheBytes = observedApproximateCacheBytes;
            BackwardStepCacheHits = backwardStepCacheHits;
            BackwardStepReconstructionCount = backwardStepReconstructionCount;
            ForwardStepCacheHits = forwardStepCacheHits;
            ForwardStepReconstructionCount = forwardStepReconstructionCount;
            ForwardStepCacheHitRate = forwardStepCacheHitRate;
            HardwareFrameTransferMilliseconds = hardwareFrameTransferMilliseconds;
            BgraConversionMilliseconds = bgraConversionMilliseconds;
            GlobalFrameIndexStatus = globalFrameIndexStatus ?? string.Empty;
            IsGlobalFrameIndexAvailable = isGlobalFrameIndexAvailable;
            LastObservedCacheRefillReason = lastObservedCacheRefillReason ?? string.Empty;
            LastObservedCacheRefillMode = lastObservedCacheRefillMode ?? string.Empty;
            LastObservedCacheRefillMilliseconds = lastObservedCacheRefillMilliseconds;
        }

        public string ActiveDecodeBackend { get; }

        public string ActualBackendUsed { get; }

        public bool IsGpuActive { get; }

        public string GpuCapabilityStatus { get; }

        public string GpuFallbackReason { get; }

        public string BudgetBand { get; }

        public string HostResourceClass { get; }

        public int OperationalQueueDepth { get; }

        public long SessionDecodedFrameCacheBudgetBytes { get; }

        public long DecodedFrameCacheBudgetBytes { get; }

        public int ConfiguredPreviousCachedFrames { get; }

        public int ConfiguredForwardCachedFrames { get; }

        public int ObservedPreviousCachedFrames { get; }

        public int ObservedForwardCachedFrames { get; }

        public long ObservedApproximateCacheBytes { get; }

        public int BackwardStepCacheHits { get; }

        public int BackwardStepReconstructionCount { get; }

        public int ForwardStepCacheHits { get; }

        public int ForwardStepReconstructionCount { get; }

        public double ForwardStepCacheHitRate { get; }

        public double HardwareFrameTransferMilliseconds { get; }

        public double BgraConversionMilliseconds { get; }

        public string GlobalFrameIndexStatus { get; }

        public bool IsGlobalFrameIndexAvailable { get; }

        public string LastObservedCacheRefillReason { get; }

        public string LastObservedCacheRefillMode { get; }

        public double LastObservedCacheRefillMilliseconds { get; }
    }

    public sealed class RegressionMediaProfile
    {
        public RegressionMediaProfile(
            string videoCodecName,
            int pixelWidth,
            int pixelHeight,
            string duration,
            double framesPerSecond,
            bool hasAudioStream,
            bool isAudioPlaybackAvailable,
            string audioCodecName,
            int audioSampleRate,
            int audioChannelCount)
        {
            VideoCodecName = videoCodecName ?? string.Empty;
            PixelWidth = pixelWidth;
            PixelHeight = pixelHeight;
            Duration = duration ?? string.Empty;
            FramesPerSecond = framesPerSecond;
            HasAudioStream = hasAudioStream;
            IsAudioPlaybackAvailable = isAudioPlaybackAvailable;
            AudioCodecName = audioCodecName ?? string.Empty;
            AudioSampleRate = audioSampleRate;
            AudioChannelCount = audioChannelCount;
        }

        public string VideoCodecName { get; }

        public int PixelWidth { get; }

        public int PixelHeight { get; }

        public string Duration { get; }

        public double FramesPerSecond { get; }

        public bool HasAudioStream { get; }

        public bool IsAudioPlaybackAvailable { get; }

        public string AudioCodecName { get; }

        public int AudioSampleRate { get; }

        public int AudioChannelCount { get; }
    }

    public sealed class RegressionMetrics
    {
        public double OpenMilliseconds { get; set; }

        public double PreIndexSeekMilliseconds { get; set; }

        public double IndexReadyMilliseconds { get; set; }

        public double IndexedSeekMilliseconds { get; set; }

        public double PlaybackMilliseconds { get; set; }

        public double ReopenMilliseconds { get; set; }

        public double UiOpenMilliseconds { get; set; }

        public double UiPreIndexClickMilliseconds { get; set; }

        public double UiClickSeekMilliseconds { get; set; }

        public double UiDragSeekMilliseconds { get; set; }

        public double UiEndSeekMilliseconds { get; set; }

        public double UiIndexReadyMilliseconds { get; set; }

        public int MaxObservedPreviousCachedFrames { get; set; }

        public int MaxObservedForwardCachedFrames { get; set; }

        public long MaxObservedApproximateCacheBytes { get; set; }

        public int BackwardStepCacheHits { get; set; }

        public int BackwardStepReconstructionCount { get; set; }

        public int ForwardStepCacheHits { get; set; }

        public int ForwardStepReconstructionCount { get; set; }

        public double LastObservedCacheRefillMilliseconds { get; set; }

        public string LastObservedCacheRefillReason { get; set; }

        public string LastObservedCacheRefillMode { get; set; }
    }

    public sealed class RegressionCheckResult
    {
        public RegressionCheckResult(
            string filePath,
            string scope,
            string category,
            string name,
            string classification,
            string message,
            long? expectedFrameIndex,
            long? actualFrameIndex,
            long? expectedDisplayedFrame,
            long? actualDisplayedFrame,
            string requestedTime,
            string actualTime,
            double? sliderValueSeconds,
            double? sliderMaximumSeconds,
            double? elapsedMilliseconds,
            bool? indexReady,
            bool? usedGlobalIndex,
            bool? cacheHit,
            bool? requiredReconstruction)
        {
            FilePath = filePath ?? string.Empty;
            Scope = scope ?? string.Empty;
            Category = category ?? string.Empty;
            Name = name ?? string.Empty;
            Classification = classification ?? string.Empty;
            Message = message ?? string.Empty;
            ExpectedFrameIndex = expectedFrameIndex;
            ActualFrameIndex = actualFrameIndex;
            ExpectedDisplayedFrame = expectedDisplayedFrame;
            ActualDisplayedFrame = actualDisplayedFrame;
            RequestedTime = requestedTime ?? string.Empty;
            ActualTime = actualTime ?? string.Empty;
            SliderValueSeconds = sliderValueSeconds;
            SliderMaximumSeconds = sliderMaximumSeconds;
            ElapsedMilliseconds = elapsedMilliseconds;
            IndexReady = indexReady;
            UsedGlobalIndex = usedGlobalIndex;
            CacheHit = cacheHit;
            RequiredReconstruction = requiredReconstruction;
        }

        public string FilePath { get; }

        public string Scope { get; }

        public string Category { get; }

        public string Name { get; }

        public string Classification { get; }

        public string Message { get; }

        public long? ExpectedFrameIndex { get; }

        public long? ActualFrameIndex { get; }

        public long? ExpectedDisplayedFrame { get; }

        public long? ActualDisplayedFrame { get; }

        public string RequestedTime { get; }

        public string ActualTime { get; }

        public double? SliderValueSeconds { get; }

        public double? SliderMaximumSeconds { get; }

        public double? ElapsedMilliseconds { get; }

        public bool? IndexReady { get; }

        public bool? UsedGlobalIndex { get; }

        public bool? CacheHit { get; }

        public bool? RequiredReconstruction { get; }
    }

    public sealed class RegressionSummary
    {
        public RegressionSummary(
            int filesTested,
            int checksRun,
            int passCount,
            int warningCount,
            int failCount)
        {
            FilesTested = filesTested;
            ChecksRun = checksRun;
            PassCount = passCount;
            WarningCount = warningCount;
            FailCount = failCount;
        }

        public int FilesTested { get; }

        public int ChecksRun { get; }

        public int PassCount { get; }

        public int WarningCount { get; }

        public int FailCount { get; }
    }
}
