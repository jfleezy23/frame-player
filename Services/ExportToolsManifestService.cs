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
    internal static class ExportToolsManifestService
    {
        private const string ResourceName = "FramePlayer.Runtime.export-tools-manifest.json";
        private static readonly Lazy<ExportToolsManifest> Manifest = new Lazy<ExportToolsManifest>(LoadManifest);

        public static bool TryValidateToolsDirectory(string toolsDirectory, out string errorMessage)
        {
            errorMessage = string.Empty;

            if (string.IsNullOrWhiteSpace(toolsDirectory) || !Directory.Exists(toolsDirectory))
            {
                errorMessage = "The bundled FFmpeg export tools directory is missing.";
                return false;
            }

            var manifest = Manifest.Value;
            if (manifest == null || manifest.Files == null || manifest.Files.Count == 0)
            {
                errorMessage = "The bundled FFmpeg export tools manifest is missing or invalid.";
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
            return Manifest.Value != null ? Manifest.Value.AssetName ?? string.Empty : string.Empty;
        }

        private static ExportToolsManifest LoadManifest()
        {
            var assembly = Assembly.GetExecutingAssembly();
            using (var stream = assembly.GetManifestResourceStream(ResourceName))
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

            [DataMember(Name = "files")]
            public Dictionary<string, string> Files { get; set; }
        }
    }
}
