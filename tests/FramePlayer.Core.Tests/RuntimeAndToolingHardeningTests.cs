using System;
using System.Globalization;
using System.IO;
using System.Linq;
using FFmpeg.AutoGen;
using FramePlayer.Services;
using Xunit;

namespace FramePlayer.Core.Tests
{
    public sealed class RuntimeAndToolingHardeningTests
    {
        [Fact]
        public void RuntimeManifestValidation_Succeeds_ForBundledRuntimeDirectory()
        {
            var runtimeDirectory = Path.Combine(GetRepositoryRoot(), "Runtime", "ffmpeg");
            if (!Directory.Exists(runtimeDirectory))
            {
                return;
            }

            var valid = RuntimeManifestService.TryValidateRuntimeDirectory(runtimeDirectory, out var errorMessage);

            Assert.True(valid, errorMessage);
            Assert.True(string.IsNullOrWhiteSpace(errorMessage));
        }

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
                "The built app output did not include the ffmpeg-export runtime directory. Run .\\scripts\\Ensure-DevExportRuntime.ps1 before building.");

            var valid = ExportRuntimeManifestService.TryValidateRuntimeDirectory(runtimeDirectory, out var errorMessage);

            Assert.True(valid, errorMessage);
            Assert.True(string.IsNullOrWhiteSpace(errorMessage));
        }

        [Fact]
        public void BundledAppOutput_DoesNotContainExportToolExecutables()
        {
            var toolsDirectory = Path.Combine(AppContext.BaseDirectory, "ffmpeg-tools");
            Assert.False(
                Directory.Exists(toolsDirectory),
                "The built app output still contains an ffmpeg-tools directory. Export CLI tools must remain dev/test-only and stay out of shipped output.");
            Assert.False(File.Exists(Path.Combine(AppContext.BaseDirectory, "ffmpeg.exe")));
            Assert.False(File.Exists(Path.Combine(AppContext.BaseDirectory, "ffprobe.exe")));
        }

        [Fact]
        public void ExportToolsManifestValidation_Succeeds_ForBundledToolsDirectory_WhenPresent()
        {
            var toolsDirectory = Path.Combine(GetRepositoryRoot(), "Runtime", "ffmpeg-tools");

            if (!Directory.Exists(toolsDirectory))
            {
                return;
            }

            var valid = ExportToolsManifestService.TryValidateToolsDirectory(toolsDirectory, out var errorMessage);

            Assert.True(valid, errorMessage);
            Assert.True(string.IsNullOrWhiteSpace(errorMessage));
        }

        [Fact]
        public void ExportToolsManifestValidation_Fails_WhenToolsDirectoryIsMissing()
        {
            var toolsDirectory = Path.Combine(GetRepositoryRoot(), "Runtime", "missing-ffmpeg-tools");

            var valid = ExportToolsManifestService.TryValidateToolsDirectory(toolsDirectory, out var errorMessage);

            Assert.False(valid);
            Assert.Contains("missing", errorMessage, StringComparison.OrdinalIgnoreCase);
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

        [Theory]
        [InlineData(null, false)]
        [InlineData("", false)]
        [InlineData("   ", false)]
        [InlineData("/absolute/path/file.dll", false)]
        [InlineData("nested/directory/file.dll", false)]
        [InlineData("file.dll", true)]
        [InlineData("ffmpeg.exe", true)]
        public void ManifestValidationHelper_IsSafeLeafFileName_WorksCorrectly(string? fileName, bool expected)
        {
            var result = ManifestValidationHelper.IsSafeLeafFileName(fileName);
            Assert.Equal(expected, result);
        }

        [Fact]
        public void ManifestValidationHelper_ComputeSha256_CalculatesCorrectHash()
        {
            var tempFile = Path.GetTempFileName();
            try
            {
                File.WriteAllText(tempFile, "hello world");
                // SHA256 of "hello world" text is b94d27b9934d3e08a52e52d7da7dabfac484efe37a5380ee9088f7ace2efcde9
                var hash = ManifestValidationHelper.ComputeSha256(tempFile);
                Assert.Equal("b94d27b9934d3e08a52e52d7da7dabfac484efe37a5380ee9088f7ace2efcde9", hash, ignoreCase: true);
            }
            finally
            {
                if (File.Exists(tempFile))
                {
                    File.Delete(tempFile);
                }
            }
        }

        [Fact]
        public void ManifestValidationHelper_LoadManifest_ReturnsNull_ForInvalidResource()
        {
            var manifest = ManifestValidationHelper.LoadManifest("NonExistentResourceName");
            Assert.Null(manifest);
        }

        [Fact]
        public void ManifestValidationHelper_LoadManifest_Succeeds_ForValidResource()
        {
            var manifest = ManifestValidationHelper.LoadManifest("FramePlayer.Runtime.runtime-manifest.json");
            Assert.NotNull(manifest);
            Assert.NotNull(manifest.Files);
            Assert.True(manifest.Files.Count > 0);
        }

        [Fact]
        public void ManifestValidationHelper_TryValidateDirectory_Fails_WhenDirectoryIsMissing()
        {
            var manifestLazy = new Lazy<ManifestData>(() => new ManifestData());
            var valid = ManifestValidationHelper.TryValidateDirectory(
                "non-existent-directory",
                manifestLazy,
                "dir-missing",
                "manifest-missing",
                "invalid-entry",
                "missing-file:",
                ":suffix",
                "integrity-fail:",
                ":suffix",
                out var errorMessage);

            Assert.False(valid);
            Assert.Equal("dir-missing", errorMessage);
        }

        [Fact]
        public void ManifestValidationHelper_TryValidateDirectory_Fails_WhenManifestIsEmpty()
        {
            var tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
            Directory.CreateDirectory(tempDir);
            try
            {
                var manifestLazy = new Lazy<ManifestData>(() => null);
                var valid = ManifestValidationHelper.TryValidateDirectory(
                    tempDir,
                    manifestLazy,
                    "dir-missing",
                    "manifest-missing",
                    "invalid-entry",
                    "missing-file:",
                    ":suffix",
                    "integrity-fail:",
                    ":suffix",
                    out var errorMessage);

                Assert.False(valid);
                Assert.Equal("manifest-missing", errorMessage);
            }
            finally
            {
                if (Directory.Exists(tempDir))
                {
                    Directory.Delete(tempDir, recursive: true);
                }
            }
        }

        [Fact]
        public void ManifestValidationHelper_TryValidateDirectory_Fails_WhenManifestHasInvalidFileEntry()
        {
            var tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
            Directory.CreateDirectory(tempDir);
            try
            {
                var manifestLazy = new Lazy<ManifestData>(() => new ManifestData
                {
                    Files = new System.Collections.Generic.Dictionary<string, string>
                    {
                        { "nested/invalid.dll", "somehash" }
                    }
                });
                var valid = ManifestValidationHelper.TryValidateDirectory(
                    tempDir,
                    manifestLazy,
                    "dir-missing",
                    "manifest-missing",
                    "invalid-entry",
                    "missing-file:",
                    ":suffix",
                    "integrity-fail:",
                    ":suffix",
                    out var errorMessage);

                Assert.False(valid);
                Assert.Equal("invalid-entry", errorMessage);
            }
            finally
            {
                if (Directory.Exists(tempDir))
                {
                    Directory.Delete(tempDir, recursive: true);
                }
            }
        }

        [Fact]
        public void ManifestValidationHelper_TryValidateDirectory_Fails_WhenFileIsMissing()
        {
            var tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
            Directory.CreateDirectory(tempDir);
            try
            {
                var manifestLazy = new Lazy<ManifestData>(() => new ManifestData
                {
                    Files = new System.Collections.Generic.Dictionary<string, string>
                    {
                        { "avcodec.dll", "somehash" }
                    }
                });
                var valid = ManifestValidationHelper.TryValidateDirectory(
                    tempDir,
                    manifestLazy,
                    "dir-missing",
                    "manifest-missing",
                    "invalid-entry",
                    "missing-file:",
                    ":suffix",
                    "integrity-fail:",
                    ":suffix",
                    out var errorMessage);

                Assert.False(valid);
                Assert.Equal("missing-file:avcodec.dll:suffix", errorMessage);
            }
            finally
            {
                if (Directory.Exists(tempDir))
                {
                    Directory.Delete(tempDir, recursive: true);
                }
            }
        }

        [Fact]
        public void ManifestValidationHelper_TryValidateDirectory_Fails_WhenIntegrityCheckFails()
        {
            var tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
            Directory.CreateDirectory(tempDir);
            try
            {
                var filePath = Path.Combine(tempDir, "avcodec.dll");
                File.WriteAllText(filePath, "wrong content");

                var manifestLazy = new Lazy<ManifestData>(() => new ManifestData
                {
                    Files = new System.Collections.Generic.Dictionary<string, string>
                    {
                        { "avcodec.dll", "correcthash" }
                    }
                });
                var valid = ManifestValidationHelper.TryValidateDirectory(
                    tempDir,
                    manifestLazy,
                    "dir-missing",
                    "manifest-missing",
                    "invalid-entry",
                    "missing-file:",
                    ":suffix",
                    "integrity-fail:",
                    ":suffix",
                    out var errorMessage);

                Assert.False(valid);
                Assert.Equal("integrity-fail:avcodec.dll:suffix", errorMessage);
            }
            finally
            {
                if (Directory.Exists(tempDir))
                {
                    Directory.Delete(tempDir, recursive: true);
                }
            }
        }

        [Fact]
        public void ManifestValidationHelper_TryValidateDirectory_Succeeds_WhenAllFilesValid()
        {
            var tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
            Directory.CreateDirectory(tempDir);
            try
            {
                var filePath = Path.Combine(tempDir, "avcodec.dll");
                File.WriteAllText(filePath, "hello world");
                var expectedHash = "b94d27b9934d3e08a52e52d7da7dabfac484efe37a5380ee9088f7ace2efcde9";

                var manifestLazy = new Lazy<ManifestData>(() => new ManifestData
                {
                    Files = new System.Collections.Generic.Dictionary<string, string>
                    {
                        { "avcodec.dll", expectedHash }
                    }
                });
                var valid = ManifestValidationHelper.TryValidateDirectory(
                    tempDir,
                    manifestLazy,
                    "dir-missing",
                    "manifest-missing",
                    "invalid-entry",
                    "missing-file:",
                    ":suffix",
                    "integrity-fail:",
                    ":suffix",
                    out var errorMessage);

                Assert.True(valid, errorMessage);
                Assert.Equal(string.Empty, errorMessage);
            }
            finally
            {
                if (Directory.Exists(tempDir))
                {
                    Directory.Delete(tempDir, recursive: true);
                }
            }
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

        [Fact]
        public void FfmpegCliToolingRunProcess_Throws_WhenExecutableIsMissing()
        {
            var workingDirectory = GetRepositoryRoot();

            var exception = Assert.Throws<FileNotFoundException>(
                () => FfmpegCliTooling.RunProcess(
                    Path.Combine(workingDirectory, "missing-tool.exe"),
                    string.Empty,
                    workingDirectory));

            Assert.Contains("missing-tool.exe", exception.FileName ?? string.Empty, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public void FfmpegCliToolingRunProcess_Throws_WhenWorkingDirectoryIsMissing()
        {
            var shell = GetShellCommand("exit 0");
            var workingDirectory = Path.Combine(GetRepositoryRoot(), "missing-working-directory");

            var exception = Assert.Throws<DirectoryNotFoundException>(
                () => FfmpegCliTooling.RunProcess(shell.FileName, shell.Arguments, workingDirectory));

            Assert.Contains("working directory", exception.Message, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public void FfmpegCliToolingRunProcess_CapturesLargeStdoutAndStderr_WithoutHanging()
        {
            var workingDirectory = GetRepositoryRoot();
            var shell = GetShellCommand(BuildLargeOutputCommand());

            var result = FfmpegCliTooling.RunProcess(shell.FileName, shell.Arguments, workingDirectory);

            Assert.Equal(7, result.ExitCode);
            Assert.Contains("stdout-1", result.StandardOutput, StringComparison.Ordinal);
            Assert.Contains("stdout-12000", result.StandardOutput, StringComparison.Ordinal);
            Assert.Contains("stderr-1", result.StandardError, StringComparison.Ordinal);
            Assert.Contains("stderr-12000", result.StandardError, StringComparison.Ordinal);
        }

        [Fact]
        public void BuildFailureMessage_PrefersStandardError_AndCondensesLines()
        {
            var result = new FfmpegProcessResult(23, "stdout line", "stderr line 1" + Environment.NewLine + "stderr line 2");

            var message = FfmpegCliTooling.BuildFailureMessage(result, "FFmpeg failed.");

            Assert.Equal("FFmpeg failed. stderr line 1 stderr line 2", message);
        }

        private static string GetRepositoryRoot()
        {
            var directory = new DirectoryInfo(AppContext.BaseDirectory);
            while (directory != null)
            {
                if (File.Exists(Path.Combine(directory.FullName, "FramePlayer.csproj")))
                {
                    return directory.FullName;
                }

                directory = directory.Parent;
            }

            throw new InvalidOperationException("Could not locate the repository root from the test output directory.");
        }

        private static string BuildLargeOutputCommand()
        {
            if (!OperatingSystem.IsWindows())
            {
                return "i=1; while [ $i -le 12000 ]; do echo stdout-$i; i=$((i + 1)); done; " +
                    "i=1; while [ $i -le 12000 ]; do echo stderr-$i >&2; i=$((i + 1)); done; exit 7";
            }

            return "$stdout = 1..12000 | ForEach-Object { 'stdout-' + $_ }; " +
                "$stderr = 1..12000 | ForEach-Object { 'stderr-' + $_ }; " +
                "$stdout | ForEach-Object { Write-Output $_ }; " +
                "$stderr | ForEach-Object { [Console]::Error.WriteLine($_) }; exit 7";
        }

        private static ShellCommand GetShellCommand(string command)
        {
            if (!OperatingSystem.IsWindows())
            {
                const string shellPath = "/bin/sh";
                Assert.True(File.Exists(shellPath), "Expected /bin/sh to be available for process tests.");
                return new ShellCommand(shellPath, "-c \"" + EscapePosixShellArgument(command) + "\"");
            }

            return new ShellCommand(GetWindowsPowerShellPath(), "-NoProfile -Command \"" + command.Replace("\"", "\\\"", StringComparison.Ordinal) + "\"");
        }

        private static string GetWindowsPowerShellPath()
        {
            var systemRoot = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
            var executablePath = Path.Combine(
                systemRoot,
                "System32",
                "WindowsPowerShell",
                "v1.0",
                "powershell.exe");

            Assert.True(
                File.Exists(executablePath),
                string.Format(
                    CultureInfo.InvariantCulture,
                    "Expected Windows PowerShell at '{0}'.",
                    executablePath));

            return executablePath;
        }

        private static string EscapePosixShellArgument(string value)
        {
            return (value ?? string.Empty)
                .Replace("\\", "\\\\", StringComparison.Ordinal)
                .Replace("\"", "\\\"", StringComparison.Ordinal);
        }

        private sealed class ShellCommand
        {
            public ShellCommand(string fileName, string arguments)
            {
                FileName = fileName;
                Arguments = arguments;
            }

            public string FileName { get; }

            public string Arguments { get; }
        }
    }
}
