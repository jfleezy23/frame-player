using System;
using System.IO;
using System.Runtime.InteropServices;
using FFmpeg.AutoGen;

namespace FramePlayer.Engines.FFmpeg
{
    public static class FfmpegRuntimeBootstrap
    {
        public static string ConfigureForCurrentPlatform(string baseDirectory)
        {
            if (string.IsNullOrWhiteSpace(baseDirectory))
            {
                throw new ArgumentException("A base directory is required.", nameof(baseDirectory));
            }

            var runtimeDirectory = ResolveRuntimeDirectory(baseDirectory);
            ffmpeg.RootPath = runtimeDirectory;
            if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                if (!HasRequiredRuntimeLibraries(runtimeDirectory))
                {
                    throw new DirectoryNotFoundException("The resolved FFmpeg runtime directory does not contain the required macOS/Linux shared libraries: " + runtimeDirectory);
                }

                DynamicallyLoadedBindings.Initialize();
            }

            return runtimeDirectory;
        }

        public static string ResolveRuntimeDirectory(string baseDirectory)
        {
            var platformFolder = ResolvePlatformFolder();
            var candidates = new[]
            {
                Path.Combine(baseDirectory, "Runtime", "macos", platformFolder, "ffmpeg"),
                Path.Combine(baseDirectory, "Runtime", "macos", platformFolder),
                Path.Combine(baseDirectory, "Runtime", "ffmpeg"),
                baseDirectory
            };

            foreach (var candidate in candidates)
            {
                if (Directory.Exists(candidate))
                {
                    return candidate;
                }
            }

            return candidates[0];
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
            if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            {
                return File.Exists(Path.Combine(runtimeDirectory, "libavutil.60.dylib")) &&
                    File.Exists(Path.Combine(runtimeDirectory, "libswresample.6.dylib")) &&
                    File.Exists(Path.Combine(runtimeDirectory, "libswscale.9.dylib")) &&
                    File.Exists(Path.Combine(runtimeDirectory, "libavfilter.11.dylib")) &&
                    File.Exists(Path.Combine(runtimeDirectory, "libavcodec.62.dylib")) &&
                    File.Exists(Path.Combine(runtimeDirectory, "libavformat.62.dylib"));
            }

            if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            {
                return File.Exists(Path.Combine(runtimeDirectory, "libavutil.so.60")) &&
                    File.Exists(Path.Combine(runtimeDirectory, "libswresample.so.6")) &&
                    File.Exists(Path.Combine(runtimeDirectory, "libswscale.so.9")) &&
                    File.Exists(Path.Combine(runtimeDirectory, "libavcodec.so.62")) &&
                    File.Exists(Path.Combine(runtimeDirectory, "libavformat.so.62"));
            }

            return true;
        }
    }
}
