using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Security.Cryptography;

namespace FramePlayer.Services
{
    internal static class ExportToolsManifestService
    {
        private const string ResourcePrefix = "FramePlayer.Runtime.manifests.";
        private static readonly ConcurrentDictionary<string, Lazy<ExportToolsManifest>> ManifestByRuntimeIdentifier =
            new ConcurrentDictionary<string, Lazy<ExportToolsManifest>>(StringComparer.OrdinalIgnoreCase);

        public static bool TryValidateToolsDirectory(string toolsDirectory, out string errorMessage)
        {
            return TryValidateToolsDirectory(toolsDirectory, GetCurrentRuntimeIdentifier(), out errorMessage);
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

            foreach (var file in manifest.Files)
            {
                var filePath = Path.Combine(toolsDirectory, file.Key);
                if (!File.Exists(filePath))
                {
                    errorMessage = "The bundled FFmpeg export tools are missing " + file.Key + ".";
                    return false;
                }

                var actualHash = ComputeSha256(filePath);
                if (!string.Equals(actualHash, file.Value, StringComparison.OrdinalIgnoreCase))
                {
                    errorMessage = "The bundled FFmpeg export tools failed integrity validation for " + file.Key + ".";
                    return false;
                }
            }

            return true;
        }

        public static string GetExpectedAssetName()
        {
            return GetExpectedAssetName(GetCurrentRuntimeIdentifier());
        }

        public static string GetExpectedAssetName(string runtimeIdentifier)
        {
            var manifest = GetManifest(runtimeIdentifier);
            return manifest != null ? manifest.AssetName ?? string.Empty : string.Empty;
        }

        public static bool TryGetToolPaths(string toolsDirectory, out string ffmpegPath, out string ffprobePath, out string errorMessage)
        {
            return TryGetToolPaths(toolsDirectory, GetCurrentRuntimeIdentifier(), out ffmpegPath, out ffprobePath, out errorMessage);
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
                : GetDefaultExecutableName("ffmpeg");
            var ffprobeExecutableName = manifest != null && !string.IsNullOrWhiteSpace(manifest.FfprobeExecutable)
                ? manifest.FfprobeExecutable
                : GetDefaultExecutableName("ffprobe");

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
            if (string.IsNullOrWhiteSpace(runtimeIdentifier))
            {
                return null;
            }

            var lazyManifest = ManifestByRuntimeIdentifier.GetOrAdd(
                runtimeIdentifier,
                key => new Lazy<ExportToolsManifest>(() => LoadManifest(key)));
            return lazyManifest.Value;
        }

        private static ExportToolsManifest LoadManifest(string runtimeIdentifier)
        {
            var assembly = Assembly.GetExecutingAssembly();
            var resourceName = ResourcePrefix + runtimeIdentifier + ".export-tools-manifest.json";
            using (var stream = assembly.GetManifestResourceStream(resourceName))
            {
                if (stream == null)
                {
                    return null;
                }

                var serializer = new DataContractJsonSerializer(
                    typeof(ExportToolsManifest),
                    new DataContractJsonSerializerSettings
                    {
                        UseSimpleDictionaryFormat = true
                    });
                return serializer.ReadObject(stream) as ExportToolsManifest;
            }
        }

        private static string GetCurrentRuntimeIdentifier()
        {
            var architectureSuffix = GetArchitectureSuffix(RuntimeInformation.ProcessArchitecture);
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                return "win-" + architectureSuffix;
            }

            if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            {
                return "osx-" + architectureSuffix;
            }

            if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            {
                return "linux-" + architectureSuffix;
            }

            return "unknown-" + architectureSuffix;
        }

        private static string GetArchitectureSuffix(Architecture architecture)
        {
            switch (architecture)
            {
                case Architecture.X64:
                    return "x64";
                case Architecture.Arm64:
                    return "arm64";
                case Architecture.X86:
                    return "x86";
                case Architecture.Arm:
                    return "arm";
                default:
                    return architecture.ToString().ToLowerInvariant();
            }
        }

        private static string GetDefaultExecutableName(string toolName)
        {
            return RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
                ? toolName + ".exe"
                : toolName;
        }

        private static string ComputeSha256(string filePath)
        {
            using (var stream = File.OpenRead(filePath))
            using (var sha256 = SHA256.Create())
            {
                var hashBytes = sha256.ComputeHash(stream);
                return string.Concat(hashBytes.Select(hashByte => hashByte.ToString("x2")));
            }
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
