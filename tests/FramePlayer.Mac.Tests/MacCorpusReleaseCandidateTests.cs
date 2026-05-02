using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using FramePlayer.Engines.FFmpeg;
using FramePlayer.Services;
using Xunit;

namespace FramePlayer.Mac.Tests
{
    public sealed class MacCorpusReleaseCandidateTests
    {
        private static readonly string[] SupportedExtensions =
        {
            ".avi",
            ".m4v",
            ".mkv",
            ".mov",
            ".mp4",
            ".ts",
            ".wmv"
        };

        [Fact]
        [Trait("Category", "ReleaseCandidate")]
        public async Task ReleaseCandidate_OpensSeeksAndStepsThroughProvidedCorpus()
        {
            var corpusPath = Environment.GetEnvironmentVariable("FRAMEPLAYER_MAC_CORPUS");
            var requireCorpus = string.Equals(
                Environment.GetEnvironmentVariable("FRAMEPLAYER_MAC_REQUIRE_CORPUS"),
                "1",
                StringComparison.Ordinal);

            if (string.IsNullOrWhiteSpace(corpusPath))
            {
                if (requireCorpus)
                {
                    throw new InvalidOperationException("FRAMEPLAYER_MAC_CORPUS must point at the release-candidate test corpus.");
                }

                return;
            }

            if (!Directory.Exists(corpusPath))
            {
                throw new DirectoryNotFoundException(corpusPath);
            }

            var runtimeBase = ResolveRuntimeBaseDirectory();
            FfmpegRuntimeBootstrap.ConfigureForCurrentPlatform(runtimeBase);

            var files = Directory.EnumerateFiles(corpusPath, "*", SearchOption.AllDirectories)
                .Where(path => SupportedExtensions.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase))
                .OrderBy(path => path, StringComparer.Ordinal)
                .ToArray();

            Assert.NotEmpty(files);

            var factory = new VideoReviewEngineFactory(new FfmpegReviewEngineOptionsProvider(new AppPreferencesService()));
            foreach (var file in files)
            {
                using var engine = factory.Create(Path.GetFileNameWithoutExtension(file));

                await engine.OpenAsync(file);
                Assert.True(engine.IsMediaOpen);
                Assert.NotNull(engine.MediaInfo);
                Assert.True(engine.MediaInfo.Duration >= TimeSpan.Zero);
                Assert.True(engine.MediaInfo.PixelWidth > 0, file);
                Assert.True(engine.MediaInfo.PixelHeight > 0, file);

                await engine.SeekToTimeAsync(ResolveSafeSeekTarget(engine.MediaInfo.Duration));
                var forward = await engine.StepForwardAsync();
                Assert.True(forward.Success, file + ": next frame failed - " + forward.Message);

                var backward = await engine.StepBackwardAsync();
                Assert.True(backward.Success || engine.Position.PresentationTime == TimeSpan.Zero, file + ": previous frame failed - " + backward.Message);

                if (engine is FfmpegReviewEngine ffmpegEngine &&
                    ffmpegEngine.MediaInfo.HasAudioStream &&
                    ffmpegEngine.MediaInfo.IsAudioPlaybackAvailable &&
                    HasPlaybackHeadroom(ffmpegEngine))
                {
                    var startFrame = ffmpegEngine.Position.FrameIndex;
                    await ffmpegEngine.PlayAsync();
                    await Task.Delay(TimeSpan.FromMilliseconds(600));
                    await ffmpegEngine.PauseAsync();

                    Assert.True(
                        ffmpegEngine.LastAudioSubmittedBytes > 0,
                        file + ": playback reported audio but did not submit audio bytes. " + ffmpegEngine.LastAudioErrorMessage);
                    Assert.True(
                        ffmpegEngine.LastPlaybackUsedAudioClock,
                        file + ": playback reported audio but did not use the audio clock.");
                    if (startFrame.HasValue && ffmpegEngine.Position.FrameIndex.HasValue)
                    {
                        Assert.True(
                            ffmpegEngine.Position.FrameIndex.Value > startFrame.Value,
                            file + ": timed playback did not advance frames.");
                    }
                }
            }
        }

        private static string ResolveRuntimeBaseDirectory()
        {
            var appBundle = Environment.GetEnvironmentVariable("FRAMEPLAYER_MAC_APP_BUNDLE");
            if (!string.IsNullOrWhiteSpace(appBundle))
            {
                return Path.Combine(appBundle, "Contents", "MacOS");
            }

            var runtimeBase = Environment.GetEnvironmentVariable("FRAMEPLAYER_MAC_RUNTIME_BASE");
            if (!string.IsNullOrWhiteSpace(runtimeBase))
            {
                return runtimeBase;
            }

            var directory = new DirectoryInfo(AppContext.BaseDirectory);
            while (directory != null)
            {
                if (Directory.Exists(Path.Combine(directory.FullName, "Runtime", "macos")))
                {
                    return directory.FullName;
                }

                directory = directory.Parent;
            }

            return AppContext.BaseDirectory;
        }

        private static TimeSpan ResolveSafeSeekTarget(TimeSpan duration)
        {
            if (duration <= TimeSpan.FromMilliseconds(250))
            {
                return TimeSpan.Zero;
            }

            return TimeSpan.FromTicks(duration.Ticks / 4);
        }

        private static bool HasPlaybackHeadroom(FfmpegReviewEngine engine)
        {
            if (engine.MediaInfo.Duration <= TimeSpan.Zero)
            {
                return true;
            }

            var step = engine.MediaInfo.PositionStep > TimeSpan.Zero
                ? engine.MediaInfo.PositionStep
                : TimeSpan.FromMilliseconds(50);
            return engine.Position.PresentationTime + step + TimeSpan.FromMilliseconds(600) < engine.MediaInfo.Duration;
        }
    }
}
