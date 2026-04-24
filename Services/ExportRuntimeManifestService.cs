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
    /// <summary>
    /// Validates the bundled FFmpeg export runtime used by the hidden export-host process.
    /// </summary>
    /// <remarks>
    /// Export DLL validation is separate from playback DLL validation so the review engine and
    /// export host keep distinct native-runtime trust boundaries.
    /// </remarks>
    internal static class ExportRuntimeManifestService
    {
        private const string ResourceName = "FramePlayer.Runtime.export-runtime-manifest.json";
        private static readonly Lazy<ExportRuntimeManifest> Manifest = new Lazy<ExportRuntimeManifest>(LoadManifest);

        public static bool TryValidateRuntimeDirectory(string runtimeDirectory, out string errorMessage)
        {
            errorMessage = string.Empty;

            if (string.IsNullOrWhiteSpace(runtimeDirectory) || !Directory.Exists(runtimeDirectory))
            {
                errorMessage = "The bundled FFmpeg export runtime directory is missing.";
                return false;
            }

            var manifest = Manifest.Value;
            if (manifest == null || manifest.Files == null || manifest.Files.Count == 0)
            {
                errorMessage = "The bundled FFmpeg export runtime manifest is missing or invalid.";
                return false;
            }

            foreach (var file in manifest.Files)
            {
                if (!IsSafeLeafFileName(file.Key))
                {
                    errorMessage = "The bundled FFmpeg export runtime manifest contains an invalid file entry.";
                    return false;
                }

                var filePath = Path.Combine(runtimeDirectory, file.Key);
                if (!File.Exists(filePath))
                {
                    errorMessage = "The bundled FFmpeg export runtime is missing " + file.Key + ".";
                    return false;
                }

                var actualHash = ComputeSha256(filePath);
                if (!string.Equals(actualHash, file.Value, StringComparison.OrdinalIgnoreCase))
                {
                    errorMessage = "The bundled FFmpeg export runtime failed integrity validation for " + file.Key + ".";
                    return false;
                }
            }

            return true;
        }

        private static ExportRuntimeManifest LoadManifest()
        {
            var assembly = Assembly.GetExecutingAssembly();
            using (var stream = assembly.GetManifestResourceStream(ResourceName))
            {
                if (stream == null)
                {
                    return null;
                }

                var serializer = new DataContractJsonSerializer(
                    typeof(ExportRuntimeManifest),
                    new DataContractJsonSerializerSettings
                    {
                        UseSimpleDictionaryFormat = true
                    });
                return serializer.ReadObject(stream) as ExportRuntimeManifest;
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

        private static bool IsSafeLeafFileName(string fileName)
        {
            if (string.IsNullOrWhiteSpace(fileName) || Path.IsPathRooted(fileName))
            {
                return false;
            }

            return string.Equals(
                Path.GetFileName(fileName),
                fileName,
                StringComparison.Ordinal);
        }

        [DataContract]
        private sealed class ExportRuntimeManifest
        {
            [DataMember(Name = "assetName")]
            public string AssetName { get; set; }

            [DataMember(Name = "files")]
            public Dictionary<string, string> Files { get; set; }
        }
    }
}
