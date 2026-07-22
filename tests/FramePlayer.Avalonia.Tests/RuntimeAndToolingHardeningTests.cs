using System;
using System.IO;
using System.Linq;
using FFmpeg.AutoGen;
using FramePlayer.Services;
using Xunit;

namespace FramePlayer.Avalonia.Tests
{
    public sealed class RuntimeAndToolingHardeningTests
    {
        [Fact]
        public void ExportRuntimeManifestValidation_Succeeds_ForBundledRuntimeDirectory_WhenPresent()
        {
            var runtimeDirectory = Path.Combine(GetRepositoryRoot(), "Runtime", "ffmpeg-export");
            if (!Directory.Exists(runtimeDirectory))
            {
                return;
            }

            var valid = ExportRuntimeManifestService.TryValidateRuntimeDirectory(runtimeDirectory, out var errorMessage);

            Assert.True(valid, errorMessage);
            Assert.True(string.IsNullOrWhiteSpace(errorMessage));
        }

        [Fact]
        public void ExportRuntimeManifestValidation_Succeeds_ForBundledAppOutputDirectory()
        {
            var runtimeDirectory = Path.Combine(AppContext.BaseDirectory, ExportHostClient.ExportRuntimeFolderName);
            if (!Directory.Exists(runtimeDirectory) && !OperatingSystem.IsWindows())
            {
                return;
            }

            Assert.True(
                Directory.Exists(runtimeDirectory),
                "The built universal app output did not include the ffmpeg-export runtime directory.");

            var valid = ExportRuntimeManifestService.TryValidateRuntimeDirectory(runtimeDirectory, out var errorMessage);

            Assert.True(valid, errorMessage);
            Assert.True(string.IsNullOrWhiteSpace(errorMessage));
        }

        [Fact]
        public void BundledAppOutput_DoesNotContainExportToolExecutables()
        {
            Assert.False(Directory.Exists(Path.Combine(AppContext.BaseDirectory, "ffmpeg-tools")));
            Assert.False(File.Exists(Path.Combine(AppContext.BaseDirectory, "ffmpeg.exe")));
            Assert.False(File.Exists(Path.Combine(AppContext.BaseDirectory, "ffprobe.exe")));
        }

        [Theory]
        [InlineData("Build-FFmpeg-8.1.ps1")]
        [InlineData("Build-FFmpeg-ExportRuntime-8.1.ps1")]
        [InlineData("Build-FFmpeg-Tools-8.1.ps1")]
        public void FfmpegWindowsSourceBuildScripts_PinSecurityRelease812(string scriptName)
        {
            var scriptPath = Path.Combine(GetRepositoryRoot(), "scripts", "ffmpeg", scriptName);
            var script = File.ReadAllText(scriptPath);

            Assert.Contains("n8.1.2", script, StringComparison.Ordinal);
            Assert.Contains("38b88335f99e76ed89ff3c93f877fdefce736c13", script, StringComparison.Ordinal);
            Assert.DoesNotContain("--branch n8.1 --depth", script, StringComparison.Ordinal);
        }

        [Fact]
        public void FfmpegMacSourceBuildAndProvenance_PinSecurityRelease812()
        {
            var root = GetRepositoryRoot();
            var buildScript = File.ReadAllText(Path.Combine(root, "scripts", "ffmpeg", "Build-FFmpeg-macOS-8.1.sh"));
            var provenance = File.ReadAllText(Path.Combine(root, "Runtime", "macos", "osx-arm64", "ffmpeg", "build-provenance.txt"));
            var checksums = File.ReadAllText(Path.Combine(root, "Runtime", "macos", "osx-arm64", "ffmpeg", "SHA256SUMS.txt"));
            var manifest = File.ReadAllText(Path.Combine(root, "Runtime", "macos", "osx-arm64", "ffmpeg-runtime-manifest.json"));

            Assert.Contains("FFMPEG_TAG=\"n8.1.2\"", buildScript, StringComparison.Ordinal);
            Assert.Contains("FFMPEG_COMMIT=\"38b88335f99e76ed89ff3c93f877fdefce736c13\"", buildScript, StringComparison.Ordinal);
            Assert.Contains("MACOS_DEPLOYMENT_TARGET=\"13.0\"", buildScript, StringComparison.Ordinal);
            Assert.Contains("--extra-cflags=-mmacosx-version-min=$MACOS_DEPLOYMENT_TARGET", buildScript, StringComparison.Ordinal);
            Assert.Contains("otool -l", buildScript, StringComparison.Ordinal);
            Assert.Contains("FFmpeg tag: n8.1.2", provenance, StringComparison.Ordinal);
            Assert.Contains("FFmpeg commit: 38b88335f99e76ed89ff3c93f877fdefce736c13", provenance, StringComparison.Ordinal);
            Assert.Contains("libavcodec.62.28.102.dylib", checksums, StringComparison.Ordinal);
            Assert.DoesNotContain(".100.dylib", checksums, StringComparison.Ordinal);
            Assert.Contains("\"sourceTag\": \"n8.1.2\"", manifest, StringComparison.Ordinal);
            Assert.Contains("\"sourceCommit\": \"38b88335f99e76ed89ff3c93f877fdefce736c13\"", manifest, StringComparison.Ordinal);
            Assert.Contains("\"assetName\": \"FramePlayer-ffmpeg-runtime-osx-arm64-8.1.2.zip\"", manifest, StringComparison.Ordinal);
            Assert.Contains("\"assetSha256\": \"dc27d2333f39cd195cd520854f15f5e99d76f851561462c04ea46fe3cbc4bd1d\"", manifest, StringComparison.Ordinal);
        }

        [Theory]
        [InlineData("runtime-manifest.json", "n8.1.2-frameplayer-source", "FramePlayer-ffmpeg-runtime-x64-8.1.2.zip")]
        [InlineData("export-runtime-manifest.json", "n8.1.2-frameplayer-export-runtime", "FramePlayer-ffmpeg-export-runtime-x64-8.1.2.zip")]
        [InlineData("export-tools-manifest.json", "n8.1.2-frameplayer-export-tools", "FramePlayer-ffmpeg-tools-x64-8.1.2.zip")]
        public void FfmpegWindowsManifests_PinPublishedSecurityRelease812(
            string manifestName,
            string expectedVersion,
            string expectedAssetName)
        {
            var manifest = File.ReadAllText(Path.Combine(GetRepositoryRoot(), "Runtime", manifestName));

            Assert.Contains("\"sourceTag\": \"n8.1.2\"", manifest, StringComparison.Ordinal);
            Assert.Contains("\"sourceCommit\": \"38b88335f99e76ed89ff3c93f877fdefce736c13\"", manifest, StringComparison.Ordinal);
            Assert.Contains("\"ffmpegVersion\": \"" + expectedVersion + "\"", manifest, StringComparison.Ordinal);
            Assert.Contains("\"assetName\": \"" + expectedAssetName + "\"", manifest, StringComparison.Ordinal);
            Assert.Contains("https://github.com/jfleezy23/frame-player/releases/download/v2.0.0/" + expectedAssetName, manifest, StringComparison.Ordinal);
        }

        [Fact]
        public void ExportRuntimeBootstrap_DoesNotSeedFromDistinctDeveloperToolBundle()
        {
            var script = File.ReadAllText(Path.Combine(
                GetRepositoryRoot(),
                "scripts",
                "Ensure-DevExportRuntime.ps1"));

            Assert.DoesNotContain("ffmpeg-tools", script, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("$manifest.assetUrl", script, StringComparison.Ordinal);
            Assert.Contains("Test-ExportRuntimeIntegrity", script, StringComparison.Ordinal);
        }

        [Fact]
        public void MainWindow_RoutesLongRunningExportsThroughExportHost()
        {
            var source = File.ReadAllText(Path.Combine(
                GetRepositoryRoot(),
                "src",
                "FramePlayer.Avalonia",
                "Views",
                "MainWindow.axaml.cs"));

            Assert.Contains("ClipExportService.ExportPlanAsync(plan)", source, StringComparison.Ordinal);
            Assert.Contains("CompareSideBySideExportService.ExportPlanAsync(plan)", source, StringComparison.Ordinal);
            Assert.Contains("AudioInsertionService.InsertPlanAsync(plan)", source, StringComparison.Ordinal);
            Assert.DoesNotContain("NativeClipExportService.ExportAsync(plan)", source, StringComparison.Ordinal);
            Assert.DoesNotContain("NativeCompareSideBySideExportService.ExportAsync(plan)", source, StringComparison.Ordinal);
            Assert.DoesNotContain("NativeAudioInsertionService.InsertAsync(plan)", source, StringComparison.Ordinal);
        }

        [Fact]
        public void Repository_ContainsOnlyTheUniversalProjectSet()
        {
            var root = GetRepositoryRoot();
            var projects = Directory.GetFiles(root, "*.csproj", SearchOption.AllDirectories)
                .Select(path => Path.GetRelativePath(root, path).Replace('\\', '/'))
                .OrderBy(path => path, StringComparer.Ordinal)
                .ToArray();

            Assert.Equal(
                new[]
                {
                    "src/FramePlayer.Avalonia/FramePlayer.Avalonia.csproj",
                    "src/FramePlayer.Core/FramePlayer.Core.csproj",
                    "src/FramePlayer.Engine.FFmpeg/FramePlayer.Engine.FFmpeg.csproj",
                    "tests/FramePlayer.Avalonia.Tests/FramePlayer.Avalonia.Tests.csproj",
                    "tests/FramePlayer.Core.Tests/FramePlayer.Core.Tests.csproj"
                },
                projects);
        }

        [Fact]
        public void MediaProbeService_ReportsVideoMetadata_ForSampleClip()
        {
            var sampleFilePath = Path.Combine(GetRepositoryRoot(), "artifacts", "generated-test-media", "sample-test-h264.mp4");
            if (!File.Exists(sampleFilePath))
            {
                return;
            }

            ffmpeg.RootPath = Path.Combine(GetRepositoryRoot(), "Runtime", "ffmpeg");
            var probed = MediaProbeService.TryProbeVideoMediaInfo(sampleFilePath, out var mediaInfo, out var errorMessage);

            Assert.True(probed, errorMessage);
            Assert.NotNull(mediaInfo);
            Assert.True(mediaInfo.Duration > TimeSpan.Zero);
            Assert.True(mediaInfo.PixelWidth > 0);
            Assert.True(mediaInfo.PixelHeight > 0);
            Assert.False(string.IsNullOrWhiteSpace(mediaInfo.VideoCodecName));
        }

        private static string GetRepositoryRoot()
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
