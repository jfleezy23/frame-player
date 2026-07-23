using System;
using System.IO;
using FFmpeg.AutoGen;
using FramePlayer.Engines.FFmpeg;
using Xunit;

namespace FramePlayer.Avalonia.Tests
{
    public sealed class FfmpegRuntimeBootstrapTests
    {
        [Theory]
        [InlineData("")]
        [InlineData(" ")]
        public void ConfigureForCurrentPlatform_RejectsMissingBaseDirectory(string baseDirectory)
        {
            var exception = Assert.Throws<ArgumentException>(() =>
                FfmpegRuntimeBootstrap.ConfigureForCurrentPlatform(baseDirectory));

            Assert.Equal("baseDirectory", exception.ParamName);
        }

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

        [Theory]
        [InlineData("")]
        [InlineData(" ")]
        public void MapWindowsRuntimeLibraryName_ReturnsEmptyForMissingName(string libraryName)
        {
            Assert.Equal(string.Empty, FfmpegRuntimeBootstrap.MapWindowsRuntimeLibraryName(libraryName));
        }

        [Fact]
        public void MapWindowsRuntimeLibraryName_PreservesUnknownName()
        {
            Assert.Equal("custom.dll", FfmpegRuntimeBootstrap.MapWindowsRuntimeLibraryName("custom.dll"));
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

        [Fact]
        public void ConfigureForCurrentPlatform_DoesNotChangeRootPathWhenRuntimeValidationFails()
        {
            var root = Path.Combine(Path.GetTempPath(), "frame-player-invalid-runtime-" + Guid.NewGuid().ToString("N"));
            var previousRootPath = ffmpeg.RootPath;
            var configuredRootPath = Path.Combine(root, "previous-runtime");
            try
            {
                Directory.CreateDirectory(configuredRootPath);
                ffmpeg.RootPath = configuredRootPath;

                var exception = Assert.Throws<DirectoryNotFoundException>(() =>
                    FfmpegRuntimeBootstrap.ConfigureForCurrentPlatform(root));
                Assert.Equal(configuredRootPath, ffmpeg.RootPath);
                Assert.Contains(root, exception.Message, StringComparison.Ordinal);
            }
            finally
            {
                ffmpeg.RootPath = previousRootPath;
                if (Directory.Exists(root))
                {
                    Directory.Delete(root, recursive: true);
                }
            }
        }

        [Fact]
        public void EnsureConfiguredForCurrentPlatform_ReplacesInvalidRootWithRuntimeOverride()
        {
            var repositoryRoot = FindRepositoryRoot();
            var runtimeDirectory = FfmpegRuntimeBootstrap.ResolveRuntimeDirectory(repositoryRoot);
            if (!File.Exists(Path.Combine(runtimeDirectory, "libavutil.60.dylib")) &&
                !File.Exists(Path.Combine(runtimeDirectory, "libavutil.so.60")) &&
                !File.Exists(Path.Combine(runtimeDirectory, "avutil-60.dll")))
            {
                return;
            }

            var invalidRootPath = Path.Combine(Path.GetTempPath(), "frame-player-invalid-root-" + Guid.NewGuid().ToString("N"));
            var previousRootPath = ffmpeg.RootPath;
            var previousRuntimeOverride = Environment.GetEnvironmentVariable(
                FfmpegRuntimeBootstrap.RuntimeDirectoryEnvironmentVariable);
            try
            {
                Directory.CreateDirectory(invalidRootPath);
                ffmpeg.RootPath = invalidRootPath;
                Environment.SetEnvironmentVariable(
                    FfmpegRuntimeBootstrap.RuntimeDirectoryEnvironmentVariable,
                    runtimeDirectory);

                var configuredRootPath = FfmpegRuntimeBootstrap.EnsureConfiguredForCurrentPlatform(invalidRootPath);

                Assert.Equal(runtimeDirectory, configuredRootPath);
                Assert.Equal(runtimeDirectory, ffmpeg.RootPath);
            }
            finally
            {
                Environment.SetEnvironmentVariable(
                    FfmpegRuntimeBootstrap.RuntimeDirectoryEnvironmentVariable,
                    previousRuntimeOverride);
                ffmpeg.RootPath = previousRootPath;
                if (Directory.Exists(invalidRootPath))
                {
                    Directory.Delete(invalidRootPath, recursive: true);
                }
            }
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

            throw new InvalidOperationException("Could not locate the repository root from the test output directory.");
        }
    }
}
