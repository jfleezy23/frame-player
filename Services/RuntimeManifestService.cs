using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Security.Cryptography;

namespace FramePlayer.Services
{
    public static class RuntimeManifestService
    {
        private const string ResourceName = "FramePlayer.Runtime.runtime-manifest.json";
        private static readonly Lazy<RuntimeManifest> Manifest = new Lazy<RuntimeManifest>(LoadManifest);

        public static bool TryValidateRuntimeDirectory(string runtimeDirectory, out string errorMessage)
        {
            errorMessage = string.Empty;

            if (string.IsNullOrWhiteSpace(runtimeDirectory) || !Directory.Exists(runtimeDirectory))
            {
                errorMessage = "The FFmpeg runtime directory is missing.";
                return false;
            }

            var manifest = Manifest.Value;
            if (manifest == null || manifest.Files == null || manifest.Files.Count == 0)
            {
                errorMessage = "The embedded runtime manifest is missing or invalid.";
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

                var actualHash = ComputeSha256(filePath);
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
            return Manifest.Value != null ? Manifest.Value.AssetName ?? string.Empty : string.Empty;
        }

        private static RuntimeManifest LoadManifest()
        {
            var assembly = Assembly.GetExecutingAssembly();
            using (var stream = assembly.GetManifestResourceStream(ResourceName))
            {
                if (stream == null)
                {
                    return null;
                }

                var serializer = new DataContractJsonSerializer(
                    typeof(RuntimeManifest),
                    new DataContractJsonSerializerSettings
                    {
                        UseSimpleDictionaryFormat = true
                    });
                return serializer.ReadObject(stream) as RuntimeManifest;
            }
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
        private sealed class RuntimeManifest
        {
            [DataMember(Name = "assetName")]
            public string AssetName { get; set; }

            [DataMember(Name = "files")]
            public Dictionary<string, string> Files { get; set; }
        }
    }
}
