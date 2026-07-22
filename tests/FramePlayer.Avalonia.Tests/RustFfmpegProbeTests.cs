using Avalonia.Controls;
using FramePlayer.Core.Abstractions;
using FramePlayer.Core.Models;
using FramePlayer.Avalonia.Views;
using FramePlayer.Engines.FFmpeg;
using FramePlayer.Services;
using FFmpeg.AutoGen;
using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace FramePlayer.Avalonia.Tests
{
    public sealed class RustFfmpegProbeTests : IClassFixture<AvaloniaHeadlessFixture>
    {
        private readonly AvaloniaHeadlessFixture _fixture;

        public RustFfmpegProbeTests(AvaloniaHeadlessFixture fixture)
        {
            _fixture = fixture;
        }

        [Fact]
        public void TryProbe_ReportsMissingRuntimeDirectoryWithoutLoadingNativeLibrary()
        {
            var result = RustFfmpegProbe.TryProbe(string.Empty);

            Assert.False(result.IsAvailable);
            Assert.Equal("runtime-directory-missing", result.StatusName);
            Assert.Contains("runtime directory", result.Message);
        }

        [Fact]
        public void NotRunResult_IsExplicitlyUnavailable()
        {
            var result = RustFfmpegProbeResult.NotRun();

            Assert.False(result.IsAvailable);
            Assert.Equal("not-run", result.StatusName);
        }

        [Fact]
        public void TryProbe_LoadsNativeProbeWhenRuntimeDirectoryIsProvided()
        {
            var runtimeDirectory = Environment.GetEnvironmentVariable("FRAMEPLAYER_FFMPEG_RUNTIME_DIR");
            if (string.IsNullOrWhiteSpace(runtimeDirectory))
            {
                return;
            }

            var result = RustFfmpegProbe.TryProbe(runtimeDirectory);

            Assert.True(result.IsAvailable, result.StatusName + ": " + result.Message);
            Assert.True(result.AvutilVersion > 0);
            Assert.True(result.AvcodecVersion > 0);
            Assert.True(result.AvformatVersion > 0);
        }

        [Fact]
        public void TryProbe_DecodesNativeErrorMessageWhenRuntimeDirectoryIsMissing()
        {
            var runtimeDirectory = Environment.GetEnvironmentVariable("FRAMEPLAYER_FFMPEG_RUNTIME_DIR");
            if (string.IsNullOrWhiteSpace(runtimeDirectory))
            {
                return;
            }

            var missingRuntimeDirectory = Path.Combine(
                Path.GetTempPath(),
                "frame-player-missing-rust-probe-runtime-" + Guid.NewGuid().ToString("N"));
            var result = RustFfmpegProbe.TryProbe(missingRuntimeDirectory);

            Assert.False(result.IsAvailable);
            Assert.Equal("runtime-directory-missing", result.StatusName);
            Assert.Contains("does not exist", result.Message, StringComparison.Ordinal);
        }

        [Fact]
        public void ExactGlobalFrameIndex_MatchesManagedIndexForSampleMedia()
        {
            if (!string.Equals(
                Environment.GetEnvironmentVariable("FRAMEPLAYER_ENABLE_RUST_INDEX_PARITY_TESTS"),
                "1",
                StringComparison.Ordinal))
            {
                return;
            }

            var repoRoot = FindRepoRoot();
            var mediaPath = Environment.GetEnvironmentVariable("FRAMEPLAYER_RUST_INDEX_TEST_MEDIA");
            if (string.IsNullOrWhiteSpace(mediaPath))
            {
                mediaPath = Path.Combine(
                    repoRoot,
                    "Video Test Files",
                    "frameplayer-expanded-corpus",
                    "seed",
                    "sample-test.mp4");
            }

            if (!File.Exists(mediaPath))
            {
                return;
            }

            FfmpegRuntimeBootstrap.ConfigureForCurrentPlatform(repoRoot);
            var previousBuilderMode = Environment.GetEnvironmentVariable("FRAMEPLAYER_FFMPEG_INDEX_BUILDER");
            try
            {
                Environment.SetEnvironmentVariable("FRAMEPLAYER_FFMPEG_INDEX_BUILDER", "managed");
                var managedIndex = FfmpegGlobalFrameIndex.Build(mediaPath, 0, default);

                Environment.SetEnvironmentVariable("FRAMEPLAYER_FFMPEG_INDEX_BUILDER", "rust");
                var rustIndex = FfmpegGlobalFrameIndex.Build(mediaPath, 0, default);

                Assert.Equal(managedIndex.Count, rustIndex.Count);
                Assert.True(rustIndex.Count > 0);
                foreach (var frameIndex in GetSampleFrameIndexes(rustIndex.Count))
                {
                    Assert.True(managedIndex.TryGetByAbsoluteFrameIndex(frameIndex, out var managedEntry));
                    Assert.True(rustIndex.TryGetByAbsoluteFrameIndex(frameIndex, out var rustEntry));
                    Assert.Equal(managedEntry.AbsoluteFrameIndex, rustEntry.AbsoluteFrameIndex);
                    Assert.Equal(managedEntry.PresentationTime, rustEntry.PresentationTime);
                    Assert.Equal(managedEntry.PresentationTimestamp, rustEntry.PresentationTimestamp);
                    Assert.Equal(managedEntry.DecodeTimestamp, rustEntry.DecodeTimestamp);
                    Assert.Equal(managedEntry.SearchTimestamp, rustEntry.SearchTimestamp);
                    Assert.Equal(managedEntry.IsKeyFrame, rustEntry.IsKeyFrame);
                    Assert.Equal(managedEntry.SeekAnchorFrameIndex, rustEntry.SeekAnchorFrameIndex);
                    Assert.Equal(managedEntry.SeekAnchorTimestamp, rustEntry.SeekAnchorTimestamp);
                    Assert.Equal(managedEntry.SeekAnchorStrategy, rustEntry.SeekAnchorStrategy);
                }
            }
            finally
            {
                Environment.SetEnvironmentVariable("FRAMEPLAYER_FFMPEG_INDEX_BUILDER", previousBuilderMode);
            }
        }

        [Fact]
        public void DecodeCore_DecodesIndexedWindowIntoNativeBgraBuffers()
        {
            if (!string.Equals(
                Environment.GetEnvironmentVariable("FRAMEPLAYER_ENABLE_RUST_DECODE_CORE_TESTS"),
                "1",
                StringComparison.Ordinal))
            {
                return;
            }

            var repoRoot = FindRepoRoot();
            var mediaPath = ResolveSampleMediaPath(repoRoot);
            if (!File.Exists(mediaPath))
            {
                return;
            }

            FfmpegRuntimeBootstrap.ConfigureForCurrentPlatform(repoRoot);
            var previousBuilderMode = Environment.GetEnvironmentVariable("FRAMEPLAYER_FFMPEG_INDEX_BUILDER");
            List<DecodedFrameBuffer>? frames = null;
            try
            {
                Environment.SetEnvironmentVariable("FRAMEPLAYER_FFMPEG_INDEX_BUILDER", "rust");
                var index = FfmpegGlobalFrameIndex.Build(mediaPath, 0, CancellationToken.None);
                Assert.True(index.Count > 0);
                Assert.True(index.TryGetByAbsoluteFrameIndex(0L, out var firstEntry));

                var targetFrameIndex = Math.Min(5L, index.Count - 1L);
                Assert.True(index.TryGetByAbsoluteFrameIndex(targetFrameIndex, out var targetEntry));

                var decoded = RustFfmpegDecodeCore.TryDecodeIndexedWindow(
                    ffmpeg.RootPath,
                    mediaPath,
                    0,
                    firstEntry,
                    targetEntry,
                    previousFrameLimit: 3,
                    forwardFrameLimit: 1,
                    maxFrameBytes: FfmpegMediaResourceLimits.AbsoluteDecodedFrameByteLimit,
                    maxWindowBytes: FfmpegMediaResourceLimits.AbsoluteDecodedFrameByteLimit,
                    videoStreamTimeBase: new AVRational { num = 1, den = 1000 },
                    cancellationToken: CancellationToken.None,
                    frames: out frames,
                    currentIndex: out var currentIndex,
                    resourceLimitExceeded: out var resourceLimitExceeded,
                    errorMessage: out var errorMessage);

                Assert.True(decoded, errorMessage);
                Assert.False(resourceLimitExceeded, errorMessage);
                var decodedFrames = frames ?? throw new InvalidOperationException("Rust decode core returned no frame list.");
                Assert.InRange(currentIndex, 0, decodedFrames.Count - 1);
                Assert.Equal(targetFrameIndex, decodedFrames[currentIndex].Descriptor.FrameIndex);
                Assert.All(decodedFrames, frame =>
                {
                    Assert.True(frame.HasNativePixelBuffer);
                    Assert.True(frame.ApproximateByteCount > 0);
                    Assert.Equal("bgra", frame.PixelFormatName);
                });
            }
            finally
            {
                if (frames != null)
                {
                    foreach (var frame in frames)
                    {
                        frame?.Dispose();
                    }
                }

                Environment.SetEnvironmentVariable("FRAMEPLAYER_FFMPEG_INDEX_BUILDER", previousBuilderMode);
            }
        }

        [Fact]
        public void DecodeCore_RejectsFrameBeforeReturningWindowAboveByteLimit()
        {
            if (!string.Equals(
                Environment.GetEnvironmentVariable("FRAMEPLAYER_ENABLE_RUST_DECODE_CORE_TESTS"),
                "1",
                StringComparison.Ordinal))
            {
                return;
            }

            var repoRoot = FindRepoRoot();
            var mediaPath = ResolveSampleMediaPath(repoRoot);
            if (!File.Exists(mediaPath))
            {
                return;
            }

            FfmpegRuntimeBootstrap.ConfigureForCurrentPlatform(repoRoot);
            var previousBuilderMode = Environment.GetEnvironmentVariable("FRAMEPLAYER_FFMPEG_INDEX_BUILDER");
            List<DecodedFrameBuffer>? frames = null;
            try
            {
                Environment.SetEnvironmentVariable("FRAMEPLAYER_FFMPEG_INDEX_BUILDER", "rust");
                var index = FfmpegGlobalFrameIndex.Build(mediaPath, 0, CancellationToken.None);
                Assert.True(index.TryGetByAbsoluteFrameIndex(0L, out var firstEntry));

                var decoded = RustFfmpegDecodeCore.TryDecodeIndexedWindow(
                    ffmpeg.RootPath,
                    mediaPath,
                    0,
                    firstEntry,
                    firstEntry,
                    previousFrameLimit: 0,
                    forwardFrameLimit: 0,
                    maxFrameBytes: 1L,
                    maxWindowBytes: 1L,
                    videoStreamTimeBase: new AVRational { num = 1, den = 1000 },
                    cancellationToken: CancellationToken.None,
                    frames: out frames,
                    currentIndex: out var currentIndex,
                    resourceLimitExceeded: out var resourceLimitExceeded,
                    errorMessage: out var errorMessage);

                Assert.False(decoded);
                Assert.True(resourceLimitExceeded, errorMessage);
                Assert.Null(frames);
                Assert.Equal(-1, currentIndex);
                Assert.Contains("resource-limit-exceeded", errorMessage, StringComparison.Ordinal);
            }
            finally
            {
                if (frames != null)
                {
                    foreach (var frame in frames)
                    {
                        frame?.Dispose();
                    }
                }

                Environment.SetEnvironmentVariable("FRAMEPLAYER_FFMPEG_INDEX_BUILDER", previousBuilderMode);
            }
        }

        [Fact]
        public async Task ForcedRustPlayback_RemainsResumableAfterCompareModeRoundTrip()
        {
            if (!string.Equals(
                Environment.GetEnvironmentVariable("FRAMEPLAYER_ENABLE_RUST_PLAYBACK_FLOW_TESTS"),
                "1",
                StringComparison.Ordinal))
            {
                return;
            }

            var repoRoot = FindRepoRoot();
            var mediaPath = Environment.GetEnvironmentVariable("FRAMEPLAYER_RUST_PLAYBACK_TEST_MEDIA");
            if (string.IsNullOrWhiteSpace(mediaPath))
            {
                mediaPath = Path.Combine(
                    repoRoot,
                    "Video Test Files",
                    "frameplayer-expanded-corpus",
                    "derived",
                    "hevc-2398-20s.mp4");
            }

            if (!File.Exists(mediaPath))
            {
                return;
            }

            FfmpegRuntimeBootstrap.ConfigureForCurrentPlatform(repoRoot);
            var previousBuilderMode = Environment.GetEnvironmentVariable("FRAMEPLAYER_FFMPEG_INDEX_BUILDER");
            var previousDecodeMode = Environment.GetEnvironmentVariable("FRAMEPLAYER_FFMPEG_DECODE_CORE");
            var previousConverterMode = Environment.GetEnvironmentVariable("FRAMEPLAYER_FFMPEG_FRAME_CONVERTER");
            MainWindow? window = null;
            FfmpegReviewEngine? primaryEngine = null;

            try
            {
                Environment.SetEnvironmentVariable("FRAMEPLAYER_FFMPEG_INDEX_BUILDER", "rust");
                Environment.SetEnvironmentVariable("FRAMEPLAYER_FFMPEG_DECODE_CORE", "rust");
                Environment.SetEnvironmentVariable("FRAMEPLAYER_FFMPEG_FRAME_CONVERTER", "rust");

                await _fixture.RunAsync(async () =>
                {
                    window = new MainWindow();
                    await InvokePrivateTask(window, "OpenMediaAsync", mediaPath, "pane-primary");
                    primaryEngine = RequirePrivateField<FfmpegReviewEngine>(window, "_primaryEngine");
                    Assert.True(primaryEngine.IsMediaOpen, primaryEngine.LastErrorMessage);
                    await primaryEngine.PlayAsync();
                },
                TimeSpan.FromSeconds(20d),
                "Open and play forced-Rust media: " + mediaPath);

                await WaitForPlaybackHealthyAsync(primaryEngine!, mediaPath, "initial playback");

                _fixture.Run(() =>
                {
                    RequireControl<CheckBox>(window!, "CompareModeCheckBox").IsChecked = true;
                });
                await WaitForPlaybackHealthyAsync(primaryEngine!, mediaPath, "compare mode enabled");

                _fixture.Run(() =>
                {
                    RequireControl<CheckBox>(window!, "CompareModeCheckBox").IsChecked = false;
                });
                await WaitForPlaybackHealthyAsync(primaryEngine!, mediaPath, "compare mode disabled");

                await primaryEngine!.PauseAsync();
                await primaryEngine.PlayAsync();
                await WaitForPlaybackHealthyAsync(primaryEngine, mediaPath, "resume after compare mode");

                Assert.True(primaryEngine.IsMediaOpen);
                Assert.True(primaryEngine.IsPlaying, primaryEngine.LastErrorMessage);
                Assert.True(string.IsNullOrWhiteSpace(primaryEngine.LastErrorMessage), primaryEngine.LastErrorMessage);
            }
            finally
            {
                if (primaryEngine != null)
                {
                    await primaryEngine.PauseAsync();
                    await primaryEngine.CloseAsync();
                    primaryEngine.Dispose();
                }

                if (window != null)
                {
                    await CloseAndDisposeCompareEngineAsync(window);
                }

                Environment.SetEnvironmentVariable("FRAMEPLAYER_FFMPEG_INDEX_BUILDER", previousBuilderMode);
                Environment.SetEnvironmentVariable("FRAMEPLAYER_FFMPEG_DECODE_CORE", previousDecodeMode);
                Environment.SetEnvironmentVariable("FRAMEPLAYER_FFMPEG_FRAME_CONVERTER", previousConverterMode);
            }
        }

        [Fact]
        public async Task ForcedRustPlayback_RemainsMonotonicAfterIndexedSeek()
        {
            if (!string.Equals(
                Environment.GetEnvironmentVariable("FRAMEPLAYER_ENABLE_RUST_PLAYBACK_FLOW_TESTS"),
                "1",
                StringComparison.Ordinal))
            {
                return;
            }

            var repoRoot = FindRepoRoot();
            var mediaPath = Environment.GetEnvironmentVariable("FRAMEPLAYER_RUST_PLAYBACK_TEST_MEDIA");
            if (string.IsNullOrWhiteSpace(mediaPath))
            {
                mediaPath = Path.Combine(
                    repoRoot,
                    "Video Test Files",
                    "frameplayer-expanded-corpus",
                    "derived",
                    "hevc-2398-20s.mp4");
            }

            if (!File.Exists(mediaPath))
            {
                return;
            }

            FfmpegRuntimeBootstrap.ConfigureForCurrentPlatform(repoRoot);
            var previousBuilderMode = Environment.GetEnvironmentVariable("FRAMEPLAYER_FFMPEG_INDEX_BUILDER");
            var previousDecodeMode = Environment.GetEnvironmentVariable("FRAMEPLAYER_FFMPEG_DECODE_CORE");
            var previousConverterMode = Environment.GetEnvironmentVariable("FRAMEPLAYER_FFMPEG_FRAME_CONVERTER");
            var previousCacheBudget = Environment.GetEnvironmentVariable("FRAMEPLAYER_DECODE_CACHE_MB");
            FfmpegReviewEngine? engine = null;

            try
            {
                Environment.SetEnvironmentVariable("FRAMEPLAYER_FFMPEG_INDEX_BUILDER", "rust");
                Environment.SetEnvironmentVariable("FRAMEPLAYER_FFMPEG_DECODE_CORE", "rust");
                Environment.SetEnvironmentVariable("FRAMEPLAYER_FFMPEG_FRAME_CONVERTER", "rust");
                Environment.SetEnvironmentVariable("FRAMEPLAYER_DECODE_CACHE_MB", "64");

                engine = new FfmpegReviewEngine(new FfmpegReviewEngineOptionsProvider(new AppPreferencesService()));
                await engine.OpenAsync(mediaPath);
                await WaitForIndexAsync(engine);

                Assert.True(engine.IsGlobalFrameIndexAvailable, engine.GlobalFrameIndexStatus);
                Assert.False(engine.IsCompleteDecodedCacheLoaded);

                var target = TimeSpan.FromSeconds(Math.Min(5d, Math.Max(1d, engine.MediaInfo.Duration.TotalSeconds / 2d)));
                await engine.SeekToTimeAsync(target);
                var landedPosition = engine.Position.PresentationTime;

                Assert.True(
                    RequirePrivateField<bool>(engine, "_playbackStartNeedsDecoderRealignment"),
                    "Rust indexed seek must force the managed playback cursor to realign before resume.");

                await engine.PlayAsync();
                var advancedPosition = await WaitForPositionAdvanceAsync(engine, landedPosition);

                Assert.True(
                    advancedPosition >= landedPosition,
                    "Playback moved backward after a Rust indexed seek. Landed at " +
                    landedPosition +
                    ", advanced to " +
                    advancedPosition +
                    ".");
                Assert.True(string.IsNullOrWhiteSpace(engine.LastErrorMessage), engine.LastErrorMessage);
            }
            finally
            {
                if (engine != null)
                {
                    await engine.PauseAsync();
                    await engine.CloseAsync();
                    engine.Dispose();
                }

                Environment.SetEnvironmentVariable("FRAMEPLAYER_FFMPEG_INDEX_BUILDER", previousBuilderMode);
                Environment.SetEnvironmentVariable("FRAMEPLAYER_FFMPEG_DECODE_CORE", previousDecodeMode);
                Environment.SetEnvironmentVariable("FRAMEPLAYER_FFMPEG_FRAME_CONVERTER", previousConverterMode);
                Environment.SetEnvironmentVariable("FRAMEPLAYER_DECODE_CACHE_MB", previousCacheBudget);
            }
        }

        [Fact]
        public void RustIndexedSeekWindows_RequireDecoderRealignmentBeforePlayback()
        {
            var source = File.ReadAllText(Path.Combine(
                FindRepoRoot(),
                "src",
                "FramePlayer.Engine.FFmpeg",
                "Engines",
                "FFmpeg",
                "FfmpegReviewEngine.cs"));
            var realignMethod = ExtractMethodBody(
                source,
                "private void RealignPlaybackStartStateIfNeeded()",
                "private bool TryPreparePlaybackFrame(");

            Assert.Contains(
                "return new FrameSeekWindowResult(frames, currentIndex, requiresDecoderRealignment: true);",
                source,
                StringComparison.Ordinal);
            Assert.Contains("allowRust: false", realignMethod, StringComparison.Ordinal);
            Assert.Contains(
                "_playbackStartNeedsDecoderRealignment = indexedWindow.RequiresDecoderRealignment;",
                source,
                StringComparison.Ordinal);
            Assert.Contains(
                "_playbackStartNeedsDecoderRealignment = reconstruction.RequiresDecoderRealignment;",
                source,
                StringComparison.Ordinal);
        }

        private static IEnumerable<long> GetSampleFrameIndexes(long frameCount)
        {
            yield return 0L;
            if (frameCount <= 1L)
            {
                yield break;
            }

            yield return frameCount / 2L;
            yield return frameCount - 1L;
            if (frameCount > 10L)
            {
                yield return frameCount / 4L;
                yield return frameCount * 3L / 4L;
            }
        }

        private static string ResolveSampleMediaPath(string repoRoot)
        {
            var mediaPath = Environment.GetEnvironmentVariable("FRAMEPLAYER_RUST_INDEX_TEST_MEDIA");
            if (!string.IsNullOrWhiteSpace(mediaPath))
            {
                return mediaPath;
            }

            return Path.Combine(
                repoRoot,
                "Video Test Files",
                "frameplayer-expanded-corpus",
                "seed",
                "sample-test.mp4");
        }

        private static string FindRepoRoot()
        {
            var directory = AppContext.BaseDirectory;
            while (!string.IsNullOrWhiteSpace(directory))
            {
                if (File.Exists(Path.Combine(directory, "AGENTS.md")) &&
                    Directory.Exists(Path.Combine(directory, "Runtime")))
                {
                    return directory;
                }

                directory = Directory.GetParent(directory)?.FullName;
            }

            throw new DirectoryNotFoundException("Could not locate the frame-player repository root.");
        }

        private static async Task WaitForPlaybackHealthyAsync(
            FfmpegReviewEngine engine,
            string mediaPath,
            string phase)
        {
            await Task.Delay(250);

            Assert.True(engine.IsPlaying, "Playback stopped during " + phase + " for " + mediaPath + ".");
            Assert.True(
                string.IsNullOrWhiteSpace(engine.LastErrorMessage),
                "Playback reported an error during " + phase + " for " + mediaPath + ": " + engine.LastErrorMessage);
        }

        private static Task InvokePrivateTask(MainWindow window, string methodName, params object[] args)
        {
            return InvokePrivate<Task>(window, methodName, args);
        }

        private static T InvokePrivate<T>(MainWindow window, string methodName, params object[] args)
        {
            var method = Array.Find(
                    typeof(MainWindow).GetMethods(BindingFlags.Instance | BindingFlags.NonPublic),
                    candidate => string.Equals(candidate.Name, methodName, StringComparison.Ordinal) &&
                        candidate.GetParameters().Length == args.Length)
                ?? throw new MissingMethodException(typeof(MainWindow).FullName, methodName);
            return (T)(method.Invoke(window, args) ?? throw new InvalidOperationException("Missing result for " + methodName + "."));
        }

        private static T RequirePrivateField<T>(MainWindow window, string fieldName)
        {
            var field = typeof(MainWindow).GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic)
                ?? throw new MissingFieldException(typeof(MainWindow).FullName, fieldName);
            return (T)(field.GetValue(window) ?? throw new InvalidOperationException("Missing field value: " + fieldName + "."));
        }

        private static T RequirePrivateField<T>(object target, string fieldName)
        {
            var field = target.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic)
                ?? throw new MissingFieldException(target.GetType().FullName, fieldName);
            return (T)(field.GetValue(target) ?? throw new InvalidOperationException("Missing field value: " + fieldName + "."));
        }

        private static async Task CloseAndDisposeCompareEngineAsync(MainWindow window)
        {
            var field = typeof(MainWindow).GetField("_compareEngine", BindingFlags.Instance | BindingFlags.NonPublic)
                ?? throw new MissingFieldException(typeof(MainWindow).FullName, "_compareEngine");
            if (field.GetValue(window) is IVideoReviewEngine engine)
            {
                await engine.CloseAsync();
                engine.Dispose();
            }
        }

        private static async Task WaitForIndexAsync(FfmpegReviewEngine engine)
        {
            var deadline = DateTimeOffset.UtcNow.AddSeconds(10);
            while (DateTimeOffset.UtcNow < deadline)
            {
                if (engine.IsGlobalFrameIndexAvailable)
                {
                    return;
                }

                await Task.Delay(100);
            }
        }

        private static async Task<TimeSpan> WaitForPositionAdvanceAsync(
            FfmpegReviewEngine engine,
            TimeSpan startingPosition)
        {
            var latestPosition = engine.Position.PresentationTime;
            var deadline = DateTimeOffset.UtcNow.AddSeconds(3);
            while (DateTimeOffset.UtcNow < deadline)
            {
                await Task.Delay(100);
                latestPosition = engine.Position.PresentationTime;
                if (latestPosition > startingPosition)
                {
                    return latestPosition;
                }
            }

            return latestPosition;
        }

        private static string ExtractMethodBody(string source, string methodStart, string nextMethodStart)
        {
            var start = source.IndexOf(methodStart, StringComparison.Ordinal);
            Assert.True(start >= 0, "Missing method: " + methodStart);

            var end = source.IndexOf(nextMethodStart, start, StringComparison.Ordinal);
            Assert.True(end > start, "Missing next method: " + nextMethodStart);

            return source.Substring(start, end - start);
        }

        private static T RequireControl<T>(Window window, string name)
            where T : Control
        {
            return window.FindControl<T>(name)
                ?? throw new InvalidOperationException("Missing control: " + name);
        }
    }
}
