using System;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using FramePlayer.Engines.FFmpeg;
using Xunit;

namespace FramePlayer.Avalonia.Tests
{
    public sealed class WindowsAudioOutputContractTests
    {
        [Fact]
        public void SharedFfmpegEngine_SelectsWinMmAudioOutputOnWindows()
        {
            var factorySource = ReadRepositoryFile(
                "src",
                "FramePlayer.Engine.FFmpeg",
                "Engines",
                "FFmpeg",
                "AudioOutputFactory.cs");

            Assert.Contains("RuntimeInformation.IsOSPlatform(OSPlatform.Windows)", factorySource, StringComparison.Ordinal);
            Assert.Contains("return new WinMmAudioOutput(sampleRate, channelCount, bitsPerSample);", factorySource, StringComparison.Ordinal);
        }

        [Fact]
        public void WinMmAudioOutput_ImplementsSharedAudioOutputContract()
        {
            Assert.True(typeof(IAudioOutput).IsAssignableFrom(typeof(WinMmAudioOutput)));

            var nativeMethodNames = new[]
            {
                "waveOutOpen",
                "waveOutPrepareHeader",
                "waveOutWrite",
                "waveOutUnprepareHeader",
                "waveOutReset",
                "waveOutClose",
                "waveOutGetPosition"
            };
            foreach (var methodName in nativeMethodNames)
            {
                var method = typeof(WinMmAudioOutput).GetMethod(
                    methodName,
                    BindingFlags.NonPublic | BindingFlags.Static);

                Assert.NotNull(method);
                Assert.NotNull(method.GetCustomAttribute<LibraryImportAttribute>());
            }
        }

        [Theory]
        [InlineData(0, 2, 16, "sampleRate")]
        [InlineData(48_000, 0, 16, "channelCount")]
        [InlineData(48_000, 2, 0, "bitsPerSample")]
        public void WinMmAudioOutput_RejectsNonPositiveFormatValues(
            int sampleRate,
            int channelCount,
            int bitsPerSample,
            string expectedParameterName)
        {
            var exception = Assert.Throws<ArgumentOutOfRangeException>(() =>
                new WinMmAudioOutput(sampleRate, channelCount, bitsPerSample));

            Assert.Equal(expectedParameterName, exception.ParamName);
        }

        [Fact]
        public void UnifiedApplication_DoesNotBlockWindowsAudioMediaPlayback()
        {
            var mainWindowSource = ReadRepositoryFile(
                "src",
                "FramePlayer.Avalonia",
                "Views",
                "MainWindow.axaml.cs");

            Assert.DoesNotContain("WindowsAudioPlaybackUnavailableMessage", mainWindowSource, StringComparison.Ordinal);
            Assert.DoesNotContain("IsWindowsAudioPlaybackBlocked", mainWindowSource, StringComparison.Ordinal);
        }

        [Fact]
        public void UnifiedApplication_TimelineSeekPreservesActivePlayback()
        {
            var mainWindowSource = ReadRepositoryFile(
                "src",
                "FramePlayer.Avalonia",
                "Views",
                "MainWindow.axaml.cs");

            Assert.Contains("SeekToTimePreservingPlaybackAsync", mainWindowSource, StringComparison.Ordinal);
            Assert.Contains("var resumePlayback = engine.IsPlaying;", mainWindowSource, StringComparison.Ordinal);
            Assert.Contains("await engine.SeekToTimeAsync(target, cancellationToken);", mainWindowSource, StringComparison.Ordinal);
            Assert.Contains("await engine.PlayAsync();", mainWindowSource, StringComparison.Ordinal);
            Assert.Contains("QueueSliderScrub(TimeSpan.FromSeconds(PositionSlider.Value));", mainWindowSource, StringComparison.Ordinal);
            Assert.Contains("await SeekMasterTimelineAsync(_pendingSliderScrubTarget, _sliderScrubCts.Token);", mainWindowSource, StringComparison.Ordinal);
            Assert.Contains(
                "await SeekAllPaneToTimePreservingPlaybackAsync(target, cancellationToken).ConfigureAwait(false);",
                mainWindowSource,
                StringComparison.Ordinal);
        }

        private static string ReadRepositoryFile(params string[] pathParts)
        {
            var root = FindRepositoryRoot(AppContext.BaseDirectory);
            var fullPath = Path.Combine(root, Path.Combine(pathParts));
            return File.ReadAllText(fullPath);
        }

        private static string FindRepositoryRoot(string startDirectory)
        {
            var directory = new DirectoryInfo(startDirectory);
            while (directory != null)
            {
                if (File.Exists(Path.Combine(
                    directory.FullName,
                    "src",
                    "FramePlayer.Avalonia",
                    "FramePlayer.Avalonia.csproj")))
                {
                    return directory.FullName;
                }

                directory = directory.Parent;
            }

            throw new DirectoryNotFoundException("Could not find the frame-player repository root.");
        }
    }
}
