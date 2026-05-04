using System;
using System.IO;
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
            var outputSource = ReadRepositoryFile(
                "src",
                "FramePlayer.Engine.FFmpeg",
                "Engines",
                "FFmpeg",
                "WinMmAudioOutput.cs");

            Assert.Contains("internal sealed class WinMmAudioOutput : IAudioOutput", outputSource, StringComparison.Ordinal);
            Assert.Contains("[DllImport(\"winmm.dll\"", outputSource, StringComparison.Ordinal);
        }

        [Fact]
        public void UnifiedPreview_DoesNotBlockWindowsAudioMediaPlayback()
        {
            var mainWindowSource = ReadRepositoryFile(
                "src",
                "FramePlayer.Avalonia",
                "Views",
                "MainWindow.axaml.cs");

            Assert.DoesNotContain("WindowsAudioPlaybackUnavailableMessage", mainWindowSource, StringComparison.Ordinal);
            Assert.DoesNotContain("IsWindowsAudioPlaybackBlocked", mainWindowSource, StringComparison.Ordinal);
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
                if (File.Exists(Path.Combine(directory.FullName, "FramePlayer.csproj")))
                {
                    return directory.FullName;
                }

                directory = directory.Parent;
            }

            throw new DirectoryNotFoundException("Could not find the frame-player repository root.");
        }
    }
}
