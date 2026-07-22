using System;
using System.IO;
using FFmpeg.AutoGen;
using FramePlayer.Engines.FFmpeg;
using FramePlayer.Services;
using Xunit;

namespace FramePlayer.NativeExport.Tests
{
    public sealed class NativeExportSupportSafetyTests
    {
        [Fact]
        public unsafe void FilterFrameReadError_ReleasesTheAllocatedFrameBeforeThrowing()
        {
            FfmpegRuntimeBootstrap.ConfigureForCurrentPlatform(FindRepositoryRoot());
            var frame = ffmpeg.av_frame_alloc();
            Assert.True(frame != null, "Could not allocate the FFmpeg frame used by the ownership test.");

            var threw = false;
            try
            {
                NativeExportSupport.HandleFilterFrameReadResult(ref frame, -1);
            }
            catch (InvalidOperationException)
            {
                threw = true;
            }

            Assert.True(threw, "The simulated FFmpeg receive error did not propagate.");
            Assert.True(frame == null, "The failed FFmpeg receive left its allocated frame owned.");
        }

        [Fact]
        public unsafe void FilterFrameEndOfStream_ReleasesTheAllocatedFrameWithoutThrowing()
        {
            FfmpegRuntimeBootstrap.ConfigureForCurrentPlatform(FindRepositoryRoot());
            var frame = ffmpeg.av_frame_alloc();
            Assert.True(frame != null, "Could not allocate the FFmpeg frame used by the ownership test.");

            var hasFrame = NativeExportSupport.HandleFilterFrameReadResult(ref frame, ffmpeg.AVERROR_EOF);

            Assert.False(hasFrame);
            Assert.True(frame == null, "The end-of-stream path left its allocated frame owned.");
        }

        private static string FindRepositoryRoot()
        {
            var directory = new DirectoryInfo(AppContext.BaseDirectory);
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

            throw new DirectoryNotFoundException(
                "Could not locate the frame-player repository from " + AppContext.BaseDirectory);
        }
    }
}
