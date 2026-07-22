using System;
using System.IO;
using FramePlayer.Engines.FFmpeg;
using Xunit;

namespace FramePlayer.Avalonia.Tests
{
    public sealed class FfmpegRuntimeBootstrapTests
    {
        [Theory]
        [InlineData("avutil-60.dll", "libavutil.60.dylib")]
        [InlineData("swresample-6.dll", "libswresample.6.dylib")]
        [InlineData("swscale-9.dll", "libswscale.9.dylib")]
        [InlineData("avcodec-62.dll", "libavcodec.62.dylib")]
        [InlineData("avformat-62.dll", "libavformat.62.dylib")]
        [InlineData("avfilter-11.dll", "libavfilter.11.dylib")]
        public void MapWindowsRuntimeLibraryName_ReturnsMacDylibName(string windowsName, string expectedMacName)
        {
            Assert.Equal(expectedMacName, FfmpegRuntimeBootstrap.MapWindowsRuntimeLibraryName(windowsName));
        }

        [Fact]
        public void ResolveRuntimeDirectory_PrefersCurrentPlatformRuntimeFolder()
        {
            var root = Path.Combine(Path.GetTempPath(), "frame-player-runtime-" + Guid.NewGuid().ToString("N"));
            try
            {
                var genericRuntime = Path.Combine(root, "Runtime", "ffmpeg");
                var expected = OperatingSystem.IsMacOS()
                    ? Path.Combine(
                        root,
                        "Runtime",
                        "macos",
                        FfmpegRuntimeBootstrap.ResolvePlatformFolder(),
                        "ffmpeg")
                    : genericRuntime;
                Directory.CreateDirectory(genericRuntime);
                Directory.CreateDirectory(Path.Combine(
                    root,
                    "Runtime",
                    "macos",
                    FfmpegRuntimeBootstrap.ResolvePlatformFolder(),
                    "ffmpeg"));
                Directory.CreateDirectory(expected);

                Assert.Equal(expected, FfmpegRuntimeBootstrap.ResolveRuntimeDirectory(root));
            }
            finally
            {
                if (Directory.Exists(root))
                {
                    Directory.Delete(root, recursive: true);
                }
            }
        }
    }
}
