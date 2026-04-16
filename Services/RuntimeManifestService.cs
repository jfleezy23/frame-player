using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Runtime.Serialization;

namespace FramePlayer.Services
{
    public static class RuntimeManifestService
    {
        private static readonly ConcurrentDictionary<string, Lazy<RuntimeManifest>> ManifestByRuntimeIdentifier =
            new ConcurrentDictionary<string, Lazy<RuntimeManifest>>(StringComparer.OrdinalIgnoreCase);

        public static bool TryValidateRuntimeDirectory(string runtimeDirectory, out string errorMessage)
        {
            return TryValidateRuntimeDirectory(runtimeDirectory, BundledManifestSupport.GetCurrentRuntimeIdentifier(), out errorMessage);
        }

        public static bool TryValidateRuntimeDirectory(string runtimeDirectory, string runtimeIdentifier, out string errorMessage)
        {
            errorMessage = string.Empty;

            if (string.IsNullOrWhiteSpace(runtimeDirectory) || !Directory.Exists(runtimeDirectory))
            {
                errorMessage = "The FFmpeg runtime directory is missing.";
                return false;
            }

            var manifest = GetManifest(runtimeIdentifier);
            if (manifest == null || manifest.Files == null || manifest.Files.Count == 0)
            {
                errorMessage = "The bundled FFmpeg runtime is not available for " + runtimeIdentifier + ".";
                return false;
            }

            foreach (var file in manifest.Files)
            {
                var filePath = Path.Combine(runtimeDirectory, file.Key);
                if (!File.Exists(filePath))
                {
                    errorMessage = "The FFmpeg runtime is missing " + file.Key + ".";
                    return false;
                }

                var actualHash = BundledManifestSupport.ComputeSha256(filePath);
                if (!string.Equals(actualHash, file.Value, StringComparison.OrdinalIgnoreCase))
                {
                    errorMessage = "The FFmpeg runtime failed integrity validation for " + file.Key + ".";
                    return false;
                }
            }

            return true;
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

        private static RuntimeManifest GetManifest(string runtimeIdentifier)
        {
            return BundledManifestSupport.GetManifest(
                ManifestByRuntimeIdentifier,
                runtimeIdentifier,
                "runtime-manifest.json");
        }

        [DataContract]
        private sealed class RuntimeManifest
        {
            [DataMember(Name = "assetName")]
            public string AssetName { get; set; }

            [DataMember(Name = "files")]
            public Dictionary<string, string> Files { get; set; }
        }
    }
}
