using System;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using FFmpeg.AutoGen;

namespace FramePlayer.Engines.FFmpeg
{
    public static class FfmpegRuntimeBootstrap
    {
        private const string RuntimeFolderName = "Runtime";
        private static readonly object RuntimeConfigurationLock = new object();
        internal const string RuntimeDirectoryEnvironmentVariable = "FRAMEPLAYER_FFMPEG_RUNTIME_DIR";

        public static RustFfmpegProbeResult LastRustProbeResult { get; private set; } =
            RustFfmpegProbeResult.NotRun();

        public static string ConfigureForCurrentPlatform(string baseDirectory)
        {
            if (string.IsNullOrWhiteSpace(baseDirectory))
            {
                throw new ArgumentException("A base directory is required.", nameof(baseDirectory));
            }

            lock (RuntimeConfigurationLock)
            {
                return ConfigureRuntimeDirectory(ResolveRuntimeDirectory(baseDirectory));
            }
        }

        internal static string EnsureConfiguredForCurrentPlatform(string fallbackBaseDirectory)
        {
            lock (RuntimeConfigurationLock)
            {
                if (!string.IsNullOrWhiteSpace(ffmpeg.RootPath) && HasRequiredRuntimeLibraries(ffmpeg.RootPath))
                {
                    return ffmpeg.RootPath;
                }

                var runtimeDirectoryOverride = Environment.GetEnvironmentVariable(RuntimeDirectoryEnvironmentVariable);
                return ConfigureRuntimeDirectory(string.IsNullOrWhiteSpace(runtimeDirectoryOverride)
                    ? ResolveRuntimeDirectory(fallbackBaseDirectory)
                    : runtimeDirectoryOverride);
            }
        }

        private static string ConfigureRuntimeDirectory(string runtimeDirectory)
        {
            var missingLibraryName = GetMissingRequiredRuntimeLibrary(runtimeDirectory);
            if (!string.IsNullOrEmpty(missingLibraryName))
            {
                throw new DirectoryNotFoundException(
                    "The resolved FFmpeg runtime directory does not contain the required shared library " +
                    Path.Combine(runtimeDirectory, missingLibraryName) + ".");
            }

            var previousRootPath = ffmpeg.RootPath;
            ffmpeg.RootPath = runtimeDirectory;
            try
            {
                if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                {
                    DynamicallyLoadedBindings.Initialize();
                }

                LastRustProbeResult = RustFfmpegProbe.TryProbe(runtimeDirectory);
            }
            catch
            {
                ffmpeg.RootPath = previousRootPath;
                throw;
            }

            return runtimeDirectory;
        }

        public static string ResolveRuntimeDirectory(string baseDirectory)
        {
            var platformFolder = ResolvePlatformFolder();
            var candidates = RuntimeInformation.IsOSPlatform(OSPlatform.OSX)
                ? new[]
                {
                    Path.Combine(baseDirectory, RuntimeFolderName, "macos", platformFolder, "ffmpeg"),
                    Path.Combine(baseDirectory, RuntimeFolderName, "macos", platformFolder),
                    Path.Combine(baseDirectory, RuntimeFolderName, "ffmpeg"),
                    baseDirectory
                }
                : new[]
                {
                    Path.Combine(baseDirectory, RuntimeFolderName, "ffmpeg"),
                    baseDirectory
                };

            return candidates.FirstOrDefault(Directory.Exists) ?? candidates[0];
        }

        public static string ResolvePlatformFolder()
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            {
                return RuntimeInformation.ProcessArchitecture == Architecture.Arm64
                    ? "osx-arm64"
                    : "osx-x64";
            }

            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                return "win-x64";
            }

            return RuntimeInformation.ProcessArchitecture == Architecture.Arm64
                ? "linux-arm64"
                : "linux-x64";
        }

        public static string MapWindowsRuntimeLibraryName(string libraryName)
        {
            if (string.IsNullOrWhiteSpace(libraryName))
            {
                return string.Empty;
            }

            switch (libraryName.Trim().ToLowerInvariant())
            {
                case "avutil-60.dll":
                    return "libavutil.60.dylib";
                case "swresample-6.dll":
                    return "libswresample.6.dylib";
                case "swscale-9.dll":
                    return "libswscale.9.dylib";
                case "avcodec-62.dll":
                    return "libavcodec.62.dylib";
                case "avformat-62.dll":
                    return "libavformat.62.dylib";
                case "avfilter-11.dll":
                    return "libavfilter.11.dylib";
                default:
                    return libraryName;
            }
        }

        private static bool HasRequiredRuntimeLibraries(string runtimeDirectory)
        {
            return string.IsNullOrEmpty(GetMissingRequiredRuntimeLibrary(runtimeDirectory));
        }

        private static string GetMissingRequiredRuntimeLibrary(string runtimeDirectory)
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            {
                return GetFirstMissingLibrary(
                    runtimeDirectory,
                    "libavutil.60.dylib",
                    "libswresample.6.dylib",
                    "libswscale.9.dylib",
                    "libavfilter.11.dylib",
                    "libavcodec.62.dylib",
                    "libavformat.62.dylib");
            }

            if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            {
                return GetFirstMissingLibrary(
                    runtimeDirectory,
                    "libavutil.so.60",
                    "libswresample.so.6",
                    "libswscale.so.9",
                    "libavcodec.so.62",
                    "libavformat.so.62");
            }

            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                return GetFirstMissingLibrary(
                    runtimeDirectory,
                    "avutil-60.dll",
                    "swresample-6.dll",
                    "swscale-9.dll",
                    "avcodec-62.dll",
                    "avformat-62.dll");
            }

            return string.Empty;
        }

        private static string GetFirstMissingLibrary(string runtimeDirectory, params string[] libraryNames)
        {
            return libraryNames.FirstOrDefault(libraryName =>
                !File.Exists(Path.Combine(runtimeDirectory, libraryName))) ?? string.Empty;
        }
    }
}
