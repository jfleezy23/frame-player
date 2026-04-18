using System;
using System.Globalization;
using System.IO;
using System.Linq;
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

            var valid = RuntimeManifestService.TryValidateRuntimeDirectory(runtimeDirectory, out var errorMessage);

            Assert.True(valid, errorMessage);
            Assert.True(string.IsNullOrWhiteSpace(errorMessage));
        }

        [Fact]
        public void ExportToolsManifestValidation_Succeeds_ForBundledToolsDirectory()
        {
            var toolsDirectory = Path.Combine(GetRepositoryRoot(), "Runtime", "ffmpeg-tools");

            var valid = ExportToolsManifestService.TryValidateToolsDirectory(toolsDirectory, out var errorMessage);

            Assert.True(valid, errorMessage);
            Assert.True(string.IsNullOrWhiteSpace(errorMessage));
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
            var executablePath = GetWindowsPowerShellPath();
            var workingDirectory = Path.Combine(GetRepositoryRoot(), "missing-working-directory");

            var exception = Assert.Throws<DirectoryNotFoundException>(
                () => FfmpegCliTooling.RunProcess(executablePath, "-NoProfile -Command \"exit 0\"", workingDirectory));

            Assert.Contains("working directory", exception.Message, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public void FfmpegCliToolingRunProcess_CapturesLargeStdoutAndStderr_WithoutHanging()
        {
            var executablePath = GetWindowsPowerShellPath();
            var workingDirectory = GetRepositoryRoot();
            var arguments =
                "-NoProfile -Command \"$stdout = 1..12000 | ForEach-Object { 'stdout-' + $_ }; " +
                "$stderr = 1..12000 | ForEach-Object { 'stderr-' + $_ }; " +
                "$stdout | ForEach-Object { Write-Output $_ }; " +
                "$stderr | ForEach-Object { [Console]::Error.WriteLine($_) }; exit 7\"";

            var result = FfmpegCliTooling.RunProcess(executablePath, arguments, workingDirectory);

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
    }
}
