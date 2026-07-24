using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using FramePlayer.Core.Models;
using FramePlayer.Engines.FFmpeg;
using FramePlayer.Services;
using Xunit;

namespace FramePlayer.Avalonia.Tests
{
    public sealed class ExportRuntimeHostTests
    {
        [Fact]
        public void ExportRuntimeManifestService_ValidatesCurrentPlatformSha256RuntimeDirectory()
        {
            using var temp = new TemporaryDirectory();
            WriteCurrentPlatformRuntimeWithSha256Sums(temp.Path);

            Assert.True(ExportRuntimeManifestService.TryValidateRuntimeDirectory(temp.Path, out var message), message);
        }

        [Fact]
        public void ExportRuntimeManifestService_RejectsCurrentPlatformSha256RuntimeMismatch()
        {
            using var temp = new TemporaryDirectory();
            var runtimeFile = ResolveCurrentPlatformRuntimeFiles()[0];
            File.WriteAllText(Path.Combine(temp.Path, runtimeFile), "tampered", Encoding.UTF8);
            File.WriteAllText(
                Path.Combine(temp.Path, "SHA256SUMS.txt"),
                new string('0', 64) + "  " + runtimeFile + Environment.NewLine,
                Encoding.UTF8);

            Assert.False(ExportRuntimeManifestService.TryValidateRuntimeDirectory(temp.Path, out var message));
            Assert.Contains("failed integrity validation", message, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public void ExportRuntimeManifestService_AcceptsBinaryMarkersAndFullPaths()
        {
            using var temp = new TemporaryDirectory();
            var lines = ResolveCurrentPlatformRuntimeFiles()
                .Select((fileName, index) =>
                {
                    var filePath = Path.Combine(temp.Path, fileName);
                    File.WriteAllText(filePath, "runtime-" + fileName, Encoding.UTF8);
                    var manifestName = index % 2 == 0
                        ? "*" + fileName
                        : "/tmp/frame-player/" + fileName;
                    return ComputeSha256(filePath) + "  " + manifestName;
                });
            File.WriteAllLines(Path.Combine(temp.Path, "SHA256SUMS.txt"), lines, Encoding.UTF8);

            Assert.True(
                ExportRuntimeManifestService.TryValidateRuntimeDirectory(temp.Path, out var message),
                message);
        }

        [Fact]
        public void ExportRuntimeManifestService_RejectsMalformedIncompleteAndMissingRuntimes()
        {
            using var temp = new TemporaryDirectory();
            var missingDirectory = Path.Combine(temp.Path, "missing");
            Assert.False(ExportRuntimeManifestService.TryValidateRuntimeDirectory(missingDirectory, out var message));
            Assert.Contains("directory is missing", message, StringComparison.OrdinalIgnoreCase);

            var checksumPath = Path.Combine(temp.Path, "SHA256SUMS.txt");
            File.WriteAllText(checksumPath, "# no runtime entries" + Environment.NewLine, Encoding.UTF8);
            Assert.False(ExportRuntimeManifestService.TryValidateRuntimeDirectory(temp.Path, out message));
            Assert.Contains("checksum file is empty", message, StringComparison.OrdinalIgnoreCase);

            File.WriteAllText(checksumPath, "invalid runtime.bin" + Environment.NewLine, Encoding.UTF8);
            Assert.False(ExportRuntimeManifestService.TryValidateRuntimeDirectory(temp.Path, out message));
            Assert.Contains("invalid entry", message, StringComparison.OrdinalIgnoreCase);

            var runtimeFile = ResolveCurrentPlatformRuntimeFiles()[0];
            var runtimeFilePath = Path.Combine(temp.Path, runtimeFile);
            File.WriteAllText(runtimeFilePath, "runtime", Encoding.UTF8);
            File.WriteAllText(
                checksumPath,
                ComputeSha256(runtimeFilePath) + "  " + runtimeFile + Environment.NewLine,
                Encoding.UTF8);
            Assert.False(ExportRuntimeManifestService.TryValidateRuntimeDirectory(temp.Path, out message));
            Assert.Contains("does not include", message, StringComparison.OrdinalIgnoreCase);

            File.WriteAllText(
                checksumPath,
                new string('0', 64) + "  missing-runtime.bin" + Environment.NewLine,
                Encoding.UTF8);
            Assert.False(ExportRuntimeManifestService.TryValidateRuntimeDirectory(temp.Path, out message));
            Assert.Contains("runtime is missing", message, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public void ExportHostClient_ProbesPlatformRuntimeAfterThePortableFolder()
        {
            using var temp = new TemporaryDirectory();
            var expectedPlatformRuntime = FfmpegRuntimeBootstrap.ResolveRuntimeDirectory(temp.Path);
            Directory.CreateDirectory(expectedPlatformRuntime);

            var candidates = ExportHostClient.ResolveRuntimeCandidateDirectories(temp.Path).ToArray();

            Assert.Contains(Path.Combine(temp.Path, ExportHostClient.ExportRuntimeFolderName), candidates);
            Assert.Contains(expectedPlatformRuntime, candidates);
        }

        [Fact]
        public void ExportHostClient_UsesPackagedAppDirectoryForHostLaunchAndRuntimeBase()
        {
            using var temp = new TemporaryDirectory();
            var appBundle = Path.Combine(temp.Path, "Frame Player.app");
            var appMacOs = Path.Combine(appBundle, "Contents", "MacOS");
            var configuredRuntimeBase = Path.Combine(temp.Path, "runtime-base");
            var executableName = RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
                ? "FramePlayer.Avalonia.exe"
                : "FramePlayer.Avalonia";
            var executablePath = Path.Combine(appMacOs, executableName);
            Directory.CreateDirectory(appMacOs);
            File.WriteAllText(executablePath, "#!/usr/bin/env bash\n", Encoding.UTF8);

            var previousAppBundle = Environment.GetEnvironmentVariable(ExportHostClient.AppBaseDirectoryEnvironmentVariable);
            var previousRuntimeBase = Environment.GetEnvironmentVariable(ExportHostClient.AppRuntimeBaseEnvironmentVariable);
            try
            {
                Environment.SetEnvironmentVariable(ExportHostClient.AppBaseDirectoryEnvironmentVariable, appMacOs);
                Environment.SetEnvironmentVariable(ExportHostClient.AppRuntimeBaseEnvironmentVariable, configuredRuntimeBase);

                var launchInfo = ExportHostClient.ResolveExportHostLaunchInfo();
                var runtimeBaseDirectories = ExportHostClient.ResolveRuntimeBaseDirectories("/tmp/frameplayer-tests").ToArray();

                Assert.Equal(executablePath, launchInfo.ExecutablePath);
                Assert.Equal(appMacOs, launchInfo.WorkingDirectory);
                Assert.Equal(appMacOs, runtimeBaseDirectories[0]);
                if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                {
                    Assert.Equal("/tmp/frameplayer-tests", runtimeBaseDirectories[1]);
                }
                else
                {
                    Assert.Equal(configuredRuntimeBase, runtimeBaseDirectories[1]);
                    Assert.Equal("/tmp/frameplayer-tests", runtimeBaseDirectories[2]);
                }
            }
            finally
            {
                Environment.SetEnvironmentVariable(ExportHostClient.AppBaseDirectoryEnvironmentVariable, previousAppBundle);
                Environment.SetEnvironmentVariable(ExportHostClient.AppRuntimeBaseEnvironmentVariable, previousRuntimeBase);
            }
        }

        [Fact]
        public async Task ExportHostClient_PublicOperationsRejectMissingConfiguredHost()
        {
            using var temp = new TemporaryDirectory();
            var runtimeDirectory = Path.Combine(temp.Path, ExportHostClient.ExportRuntimeFolderName);
            Directory.CreateDirectory(runtimeDirectory);
            WriteCurrentPlatformRuntimeWithSha256Sums(runtimeDirectory);
            var missingExecutable = Path.Combine(
                temp.Path,
                RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
                    ? "missing-export-host.exe"
                    : "missing-export-host");

            var previousAppBase = Environment.GetEnvironmentVariable(ExportHostClient.AppBaseDirectoryEnvironmentVariable);
            var previousRuntimeBase = Environment.GetEnvironmentVariable(ExportHostClient.AppRuntimeBaseEnvironmentVariable);
            var previousExecutable = Environment.GetEnvironmentVariable(ExportHostClient.ExportHostExecutableEnvironmentVariable);
            try
            {
                Environment.SetEnvironmentVariable(ExportHostClient.AppBaseDirectoryEnvironmentVariable, temp.Path);
                Environment.SetEnvironmentVariable(ExportHostClient.AppRuntimeBaseEnvironmentVariable, null);
                Environment.SetEnvironmentVariable(ExportHostClient.ExportHostExecutableEnvironmentVariable, missingExecutable);

                Assert.True(ExportHostClient.IsBundledRuntimeAvailable);
                Assert.Contains("ready", ExportHostClient.GetRuntimeAvailabilityMessage(), StringComparison.OrdinalIgnoreCase);

                var client = new ExportHostClient();
                await AssertMissingHostAsync(() => client.ProbeAsync("missing-source.mp4"));
                await AssertMissingHostAsync(() => client.InsertAudioAsync(new AudioInsertionPlan(
                    "source.mp4",
                    "audio.wav",
                    "output.mp4",
                    "Primary",
                    TimeSpan.FromSeconds(1),
                    string.Empty,
                    string.Empty,
                    string.Empty)));
                await AssertMissingHostAsync(() => client.ExportClipAsync(new ClipExportPlan(
                    "source.mp4",
                    "output.mp4",
                    "Primary",
                    "pane-primary",
                    isPaneLocal: true,
                    TimeSpan.Zero,
                    TimeSpan.FromSeconds(1),
                    0L,
                    30L,
                    "position-step",
                    PaneViewportSnapshot.CreateFullFrame(640, 480),
                    string.Empty,
                    string.Empty,
                    string.Empty)));
                await AssertMissingHostAsync(() => client.ExportCompareAsync(new CompareSideBySideExportPlan
                {
                    OutputFilePath = "output.mp4"
                }));
            }
            finally
            {
                Environment.SetEnvironmentVariable(ExportHostClient.AppBaseDirectoryEnvironmentVariable, previousAppBase);
                Environment.SetEnvironmentVariable(ExportHostClient.AppRuntimeBaseEnvironmentVariable, previousRuntimeBase);
                Environment.SetEnvironmentVariable(ExportHostClient.ExportHostExecutableEnvironmentVariable, previousExecutable);
            }
        }

        [Fact]
        public async Task ExportHostClient_ReportsChildStartupFailureFromCurrentExecutable()
        {
            using var temp = new TemporaryDirectory();
            var runtimeDirectory = Path.Combine(temp.Path, ExportHostClient.ExportRuntimeFolderName);
            Directory.CreateDirectory(runtimeDirectory);
            WriteCurrentPlatformRuntimeWithSha256Sums(runtimeDirectory);
            var executablePath = Path.Combine(
                AppContext.BaseDirectory,
                RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
                    ? "FramePlayer.Avalonia.exe"
                    : "FramePlayer.Avalonia");
            Assert.True(File.Exists(executablePath), "The built export host executable was not found.");

            var previousAppBase = Environment.GetEnvironmentVariable(ExportHostClient.AppBaseDirectoryEnvironmentVariable);
            var previousRuntimeBase = Environment.GetEnvironmentVariable(ExportHostClient.AppRuntimeBaseEnvironmentVariable);
            var previousExecutable = Environment.GetEnvironmentVariable(ExportHostClient.ExportHostExecutableEnvironmentVariable);
            try
            {
                Environment.SetEnvironmentVariable(ExportHostClient.AppBaseDirectoryEnvironmentVariable, temp.Path);
                Environment.SetEnvironmentVariable(
                    ExportHostClient.AppRuntimeBaseEnvironmentVariable,
                    Path.Combine(temp.Path, "missing-child-runtime"));
                Environment.SetEnvironmentVariable(ExportHostClient.ExportHostExecutableEnvironmentVariable, executablePath);

                var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                    new ExportHostClient().ProbeAsync(Path.Combine(temp.Path, "missing-source.mp4")));

                Assert.DoesNotContain("executable path could not be resolved", exception.Message, StringComparison.OrdinalIgnoreCase);
                Assert.False(string.IsNullOrWhiteSpace(exception.Message));
            }
            finally
            {
                Environment.SetEnvironmentVariable(ExportHostClient.AppBaseDirectoryEnvironmentVariable, previousAppBase);
                Environment.SetEnvironmentVariable(ExportHostClient.AppRuntimeBaseEnvironmentVariable, previousRuntimeBase);
                Environment.SetEnvironmentVariable(ExportHostClient.ExportHostExecutableEnvironmentVariable, previousExecutable);
            }
        }

        private static async Task AssertMissingHostAsync(Func<Task> operation)
        {
            var exception = await Assert.ThrowsAsync<InvalidOperationException>(operation);
            Assert.Contains("executable path could not be resolved", exception.Message, StringComparison.OrdinalIgnoreCase);
        }

        private static string ComputeSha256(string filePath)
        {
            using var stream = File.OpenRead(filePath);
            using var sha256 = SHA256.Create();
            return string.Concat(sha256.ComputeHash(stream).Select(value => value.ToString("x2", CultureInfo.InvariantCulture)));
        }

        private static string[] ResolveCurrentPlatformRuntimeFiles()
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                return new[]
                {
                    "avutil-60.dll",
                    "swresample-6.dll",
                    "swscale-9.dll",
                    "avfilter-11.dll",
                    "avcodec-62.dll",
                    "avformat-62.dll"
                };
            }

            if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            {
                return new[]
                {
                    "libavutil.60.dylib",
                    "libswresample.6.dylib",
                    "libswscale.9.dylib",
                    "libavfilter.11.dylib",
                    "libavcodec.62.dylib",
                    "libavformat.62.dylib"
                };
            }

            return new[]
            {
                "libavutil.so.60",
                "libswresample.so.6",
                "libswscale.so.9",
                "libavfilter.so.11",
                "libavcodec.so.62",
                "libavformat.so.62"
            };
        }

        private static void WriteCurrentPlatformRuntimeWithSha256Sums(string directoryPath)
        {
            var checksumBuilder = new StringBuilder();
            foreach (var fileName in ResolveCurrentPlatformRuntimeFiles())
            {
                var filePath = Path.Combine(directoryPath, fileName);
                File.WriteAllText(filePath, "fake ffmpeg runtime " + fileName, Encoding.UTF8);
                checksumBuilder.Append(ComputeSha256(filePath));
                checksumBuilder.Append("  ");
                checksumBuilder.AppendLine(fileName);
            }

            File.WriteAllText(Path.Combine(directoryPath, "SHA256SUMS.txt"), checksumBuilder.ToString(), Encoding.UTF8);
        }

        private sealed class TemporaryDirectory : IDisposable
        {
            public TemporaryDirectory()
            {
                Path = System.IO.Path.Combine(
                    System.IO.Path.GetTempPath(),
                    "frameplayer-runtime-tests-" + Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(Path);
            }

            public string Path { get; }

            public void Dispose()
            {
                if (Directory.Exists(Path))
                {
                    Directory.Delete(Path, true);
                }
            }
        }
    }
}
