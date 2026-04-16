using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Runtime.Serialization;

namespace FramePlayer.Services
{
    internal static class ExportToolsManifestService
    {
        private static readonly ConcurrentDictionary<string, Lazy<ExportToolsManifest>> ManifestByRuntimeIdentifier =
            new ConcurrentDictionary<string, Lazy<ExportToolsManifest>>(StringComparer.OrdinalIgnoreCase);

        public static bool TryValidateToolsDirectory(string toolsDirectory, out string errorMessage)
        {
            return TryValidateToolsDirectory(toolsDirectory, BundledManifestSupport.GetCurrentRuntimeIdentifier(), out errorMessage);
        }

        public static bool TryValidateToolsDirectory(string toolsDirectory, string runtimeIdentifier, out string errorMessage)
        {
            errorMessage = string.Empty;

            if (string.IsNullOrWhiteSpace(toolsDirectory) || !Directory.Exists(toolsDirectory))
            {
                errorMessage = "The bundled FFmpeg export tools directory is missing.";
                return false;
            }

            var manifest = GetManifest(runtimeIdentifier);
            if (manifest == null || manifest.Files == null || manifest.Files.Count == 0)
            {
                errorMessage = "The bundled FFmpeg export tools are not available for " + runtimeIdentifier + ".";
                return false;
            }

            return BundledManifestSupport.TryValidateManifestFiles(
                toolsDirectory,
                manifest.Files,
                fileName => "The bundled FFmpeg export tools are missing " + fileName + ".",
                fileName => "The bundled FFmpeg export tools failed integrity validation for " + fileName + ".",
                out errorMessage);
        }

        public static string GetExpectedAssetName()
        {
            return GetExpectedAssetName(BundledManifestSupport.GetCurrentRuntimeIdentifier());
        }

        public static string GetExpectedAssetName(string runtimeIdentifier)
        {
            var manifest = GetManifest(runtimeIdentifier);
            return manifest != null ? manifest.AssetName ?? string.Empty : string.Empty;
        }

        public static bool TryGetToolPaths(string toolsDirectory, out string ffmpegPath, out string ffprobePath, out string errorMessage)
        {
            return TryGetToolPaths(toolsDirectory, BundledManifestSupport.GetCurrentRuntimeIdentifier(), out ffmpegPath, out ffprobePath, out errorMessage);
        }

        public static bool TryGetToolPaths(string toolsDirectory, string runtimeIdentifier, out string ffmpegPath, out string ffprobePath, out string errorMessage)
        {
            ffmpegPath = string.Empty;
            ffprobePath = string.Empty;

            if (!TryValidateToolsDirectory(toolsDirectory, runtimeIdentifier, out errorMessage))
            {
                return false;
            }

            var manifest = GetManifest(runtimeIdentifier);
            var ffmpegExecutableName = manifest != null && !string.IsNullOrWhiteSpace(manifest.FfmpegExecutable)
                ? manifest.FfmpegExecutable
                : BundledManifestSupport.GetDefaultExecutableName("ffmpeg");
            var ffprobeExecutableName = manifest != null && !string.IsNullOrWhiteSpace(manifest.FfprobeExecutable)
                ? manifest.FfprobeExecutable
                : BundledManifestSupport.GetDefaultExecutableName("ffprobe");

            ffmpegPath = Path.Combine(toolsDirectory, ffmpegExecutableName);
            ffprobePath = Path.Combine(toolsDirectory, ffprobeExecutableName);
            if (!File.Exists(ffmpegPath) || !File.Exists(ffprobePath))
            {
                errorMessage = "The bundled FFmpeg export tools are incomplete for " + runtimeIdentifier + ".";
                return false;
            }

            errorMessage = string.Empty;
            return true;
        }

        private static ExportToolsManifest GetManifest(string runtimeIdentifier)
        {
            return BundledManifestSupport.GetManifest(
                ManifestByRuntimeIdentifier,
                runtimeIdentifier,
                "export-tools-manifest.json");
        }

        [DataContract]
        private sealed class ExportToolsManifest
        {
            [DataMember(Name = "assetName")]
            public string AssetName { get; set; }

            [DataMember(Name = "ffmpegExecutable")]
            public string FfmpegExecutable { get; set; }

            [DataMember(Name = "ffprobeExecutable")]
            public string FfprobeExecutable { get; set; }

            [DataMember(Name = "files")]
            public Dictionary<string, string> Files { get; set; }
        }
    }
}
