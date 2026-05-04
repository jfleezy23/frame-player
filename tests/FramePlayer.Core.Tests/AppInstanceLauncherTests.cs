using System;
using System.IO;
using FramePlayer.Services;
using Xunit;

namespace FramePlayer.Core.Tests
{
    public sealed class AppInstanceLauncherTests
    {
        [Fact]
        public void BuildNewInstanceStartInfo_UsesExecutableWithoutStartupArguments()
        {
            var executablePath = GetLauncherExecutablePath();
            var baseDirectory = Path.GetDirectoryName(executablePath);
            Assert.False(string.IsNullOrWhiteSpace(baseDirectory));
            var resolvedBaseDirectory = baseDirectory ?? throw new InvalidOperationException("PowerShell base directory was unavailable.");

            var startInfo = AppInstanceLauncher.BuildNewInstanceStartInfo(executablePath, resolvedBaseDirectory);

            Assert.Equal(Path.GetFullPath(executablePath), startInfo.FileName);
            Assert.True(startInfo.UseShellExecute);
            Assert.Equal(Path.GetFullPath(resolvedBaseDirectory), startInfo.WorkingDirectory);
            Assert.True(string.IsNullOrEmpty(startInfo.Arguments));
            Assert.Empty(startInfo.ArgumentList);
        }

        [Fact]
        public void BuildNewInstanceStartInfo_Throws_WhenExecutableIsMissing()
        {
            var missingPath = Path.Combine(Path.GetTempPath(), "missing-frame-player-" + Guid.NewGuid().ToString("N") + ".exe");

            var exception = Assert.Throws<FileNotFoundException>(
                () => AppInstanceLauncher.BuildNewInstanceStartInfo(missingPath, Path.GetTempPath()));

            Assert.Equal(Path.GetFullPath(missingPath), exception.FileName);
        }

        private static string GetLauncherExecutablePath()
        {
            if (!OperatingSystem.IsWindows())
            {
                const string shellPath = "/bin/sh";
                Assert.True(File.Exists(shellPath), "Expected /bin/sh to be available for launcher tests.");
                return shellPath;
            }

            var systemRoot = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
            var executablePath = Path.Combine(
                systemRoot,
                "System32",
                "WindowsPowerShell",
                "v1.0",
                "powershell.exe");

            Assert.True(File.Exists(executablePath), "Expected Windows PowerShell to be available for launcher tests.");
            return executablePath;
        }
    }
}
