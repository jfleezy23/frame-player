using System;
using System.Diagnostics;
using System.IO;

namespace FramePlayer.Services
{
    internal static class AppInstanceLauncher
    {
        public static ProcessStartInfo BuildNewInstanceStartInfo(
            string executablePath,
            string baseDirectory)
        {
            if (string.IsNullOrWhiteSpace(executablePath))
            {
                throw new ArgumentException("An executable path is required.", nameof(executablePath));
            }

            var resolvedExecutablePath = Path.GetFullPath(executablePath);
            if (!File.Exists(resolvedExecutablePath))
            {
                throw new FileNotFoundException("The Frame Player executable was not found.", resolvedExecutablePath);
            }

            var resolvedBaseDirectory = string.IsNullOrWhiteSpace(baseDirectory)
                ? Path.GetDirectoryName(resolvedExecutablePath)
                : Path.GetFullPath(baseDirectory);

            return new ProcessStartInfo
            {
                FileName = resolvedExecutablePath,
                Arguments = string.Empty,
                WorkingDirectory = string.IsNullOrWhiteSpace(resolvedBaseDirectory)
                    ? AppContext.BaseDirectory
                    : resolvedBaseDirectory,
                UseShellExecute = true
            };
        }

        public static void LaunchNewInstance()
        {
            var executablePath = Environment.ProcessPath;
            if (string.IsNullOrWhiteSpace(executablePath))
            {
                throw new InvalidOperationException("The current Frame Player executable path could not be resolved.");
            }

            var process = Process.Start(BuildNewInstanceStartInfo(
                executablePath,
                AppContext.BaseDirectory));
            process?.Dispose();
        }
    }
}
