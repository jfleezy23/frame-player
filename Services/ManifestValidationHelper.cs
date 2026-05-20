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
    [DataContract]
    internal sealed class ManifestData
    {
        [DataMember(Name = "assetName")]
        public string AssetName { get; set; }

        [DataMember(Name = "files")]
        public Dictionary<string, string> Files { get; set; }
    }

    internal static class ManifestValidationHelper
    {
        public static ManifestData LoadManifest(string resourceName)
        {
            var assembly = Assembly.GetExecutingAssembly();
            using (var stream = assembly.GetManifestResourceStream(resourceName))
            {
                if (stream == null)
                {
                    return null;
                }

                var serializer = new DataContractJsonSerializer(
                    typeof(ManifestData),
                    new DataContractJsonSerializerSettings
                    {
                        UseSimpleDictionaryFormat = true
                    });
                return serializer.ReadObject(stream) as ManifestData;
            }
        }

        public static string ComputeSha256(string filePath)
        {
            using (var stream = File.OpenRead(filePath))
            using (var sha256 = SHA256.Create())
            {
                var hashBytes = sha256.ComputeHash(stream);
                return string.Concat(hashBytes.Select(hashByte => hashByte.ToString("x2")));
            }
        }

        public static bool IsSafeLeafFileName(string fileName)
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

        public static bool TryValidateDirectory(
            string directory,
            Lazy<ManifestData> manifestLazy,
            string directoryMissingError,
            string manifestMissingOrInvalidError,
            string invalidFileEntryError,
            string missingFileErrorPrefix,
            string missingFileErrorSuffix,
            string integrityFailedErrorPrefix,
            string integrityFailedErrorSuffix,
            out string errorMessage)
        {
            errorMessage = string.Empty;

            if (string.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory))
            {
                errorMessage = directoryMissingError;
                return false;
            }

            var manifest = manifestLazy.Value;
            if (manifest == null || manifest.Files == null || manifest.Files.Count == 0)
            {
                errorMessage = manifestMissingOrInvalidError;
                return false;
            }

            foreach (var file in manifest.Files)
            {
                if (!IsSafeLeafFileName(file.Key))
                {
                    errorMessage = invalidFileEntryError;
                    return false;
                }

                var filePath = Path.Combine(directory, file.Key);
                if (!File.Exists(filePath))
                {
                    errorMessage = missingFileErrorPrefix + file.Key + missingFileErrorSuffix;
                    return false;
                }

                var actualHash = ComputeSha256(filePath);
                if (!string.Equals(actualHash, file.Value, StringComparison.OrdinalIgnoreCase))
                {
                    errorMessage = integrityFailedErrorPrefix + file.Key + integrityFailedErrorSuffix;
                    return false;
                }
            }

            return true;
        }
    }
}
