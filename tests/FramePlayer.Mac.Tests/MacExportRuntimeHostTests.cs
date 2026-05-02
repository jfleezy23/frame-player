using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using FramePlayer.Engines.FFmpeg;
using FramePlayer.Services;
using Xunit;

namespace FramePlayer.Mac.Tests
{
    public sealed class MacExportRuntimeHostTests
    {
        [Fact]
        public void ExportRuntimeManifestService_ValidatesMacSha256RuntimeDirectory()
        {
            using var temp = new TemporaryDirectory();
            WriteMacRuntimeWithSha256Sums(temp.Path);

            Assert.True(ExportRuntimeManifestService.TryValidateRuntimeDirectory(temp.Path, out var message), message);
        }

        [Fact]
        public void ExportRuntimeManifestService_RejectsMacSha256RuntimeMismatch()
        {
            using var temp = new TemporaryDirectory();
            File.WriteAllText(Path.Combine(temp.Path, "libavformat.62.dylib"), "tampered", Encoding.UTF8);
            File.WriteAllText(
                Path.Combine(temp.Path, "SHA256SUMS.txt"),
                new string('0', 64) + "  libavformat.62.dylib" + Environment.NewLine,
                Encoding.UTF8);

            Assert.False(ExportRuntimeManifestService.TryValidateRuntimeDirectory(temp.Path, out var message));
            Assert.Contains("failed integrity validation", message, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public void ExportHostClient_ProbesPackagedMacRuntimeBeforeFallingBackToWindowsFolder()
        {
            using var temp = new TemporaryDirectory();
            var expectedMacRuntime = Path.Combine(
                temp.Path,
                "Runtime",
                "macos",
                FfmpegRuntimeBootstrap.ResolvePlatformFolder(),
                "ffmpeg");
            Directory.CreateDirectory(expectedMacRuntime);

            var candidates = ExportHostClient.ResolveRuntimeCandidateDirectories(temp.Path).ToArray();

            Assert.Contains(Path.Combine(temp.Path, ExportHostClient.ExportRuntimeFolderName), candidates);
            Assert.Contains(expectedMacRuntime, candidates);
        }

        [Fact]
        public void ExportHostClient_UsesPackagedAppBundleForHostLaunchAndRuntimeBase()
        {
            using var temp = new TemporaryDirectory();
            var appBundle = Path.Combine(temp.Path, "Frame Player.app");
            var appMacOs = Path.Combine(appBundle, "Contents", "MacOS");
            var executablePath = Path.Combine(appMacOs, "FramePlayer.Mac");
            Directory.CreateDirectory(appMacOs);
            File.WriteAllText(executablePath, "#!/usr/bin/env bash\n", Encoding.UTF8);

            var previousAppBundle = Environment.GetEnvironmentVariable(ExportHostClient.MacAppBundleEnvironmentVariable);
            try
            {
                Environment.SetEnvironmentVariable(ExportHostClient.MacAppBundleEnvironmentVariable, appBundle);

                var launchInfo = ExportHostClient.ResolveExportHostLaunchInfo();
                var runtimeBaseDirectories = ExportHostClient.ResolveRuntimeBaseDirectories("/tmp/frameplayer-tests").ToArray();

                Assert.Equal(executablePath, launchInfo.ExecutablePath);
                Assert.Equal(appMacOs, launchInfo.WorkingDirectory);
                Assert.Equal(appMacOs, runtimeBaseDirectories[0]);
            }
            finally
            {
                Environment.SetEnvironmentVariable(ExportHostClient.MacAppBundleEnvironmentVariable, previousAppBundle);
            }
        }

        private static string ComputeSha256(string filePath)
        {
            using var stream = File.OpenRead(filePath);
            using var sha256 = SHA256.Create();
            return string.Concat(sha256.ComputeHash(stream).Select(value => value.ToString("x2")));
        }

        private static void WriteMacRuntimeWithSha256Sums(string directoryPath)
        {
            var requiredFiles = new[]
            {
                "libavutil.60.dylib",
                "libswresample.6.dylib",
                "libswscale.9.dylib",
                "libavfilter.11.dylib",
                "libavcodec.62.dylib",
                "libavformat.62.dylib"
            };
            var checksumBuilder = new StringBuilder();
            foreach (var fileName in requiredFiles)
            {
                var filePath = Path.Combine(directoryPath, fileName);
                File.WriteAllText(filePath, "fake mac ffmpeg runtime " + fileName, Encoding.UTF8);
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
                    "frameplayer-mac-runtime-tests-" + Guid.NewGuid().ToString("N"));
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
