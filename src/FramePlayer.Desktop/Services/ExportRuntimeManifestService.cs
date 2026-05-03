using System;
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
        private const string CopiedManifestRelativePath = "Runtime/export-runtime-manifest.json";
        private const string Sha256SumsFileName = "SHA256SUMS.txt";
        private const int Sha256HexLength = 64;
        private static readonly string[] WindowsRequiredRuntimeFiles = new[]
        {
            "avutil-60.dll",
            "swresample-6.dll",
            "swscale-9.dll",
            "avfilter-11.dll",
            "avcodec-62.dll",
            "avformat-62.dll"
        };
        private static readonly string[] MacRequiredRuntimeFiles = new[]
        {
            "libavutil.60.dylib",
            "libswresample.6.dylib",
            "libswscale.9.dylib",
            "libavfilter.11.dylib",
            "libavcodec.62.dylib",
            "libavformat.62.dylib"
        };
        private static readonly string[] LinuxRequiredRuntimeFiles = new[]
        {
            "libavutil.so.60",
            "libswresample.so.6",
            "libswscale.so.9",
            "libavfilter.so.11",
            "libavcodec.so.62",
            "libavformat.so.62"
        };
        private static readonly Lazy<ExportRuntimeManifest?> Manifest = new Lazy<ExportRuntimeManifest?>(LoadManifest);

        public static bool TryValidateRuntimeDirectory(string runtimeDirectory, out string errorMessage)
        {
            errorMessage = string.Empty;

            if (string.IsNullOrWhiteSpace(runtimeDirectory) || !Directory.Exists(runtimeDirectory))
            {
                errorMessage = "The bundled FFmpeg export runtime directory is missing.";
                return false;
            }

            var sha256SumsPath = Path.Combine(runtimeDirectory, Sha256SumsFileName);
            if (File.Exists(sha256SumsPath))
            {
                return TryValidateSha256SumsRuntime(runtimeDirectory, sha256SumsPath, out errorMessage);
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

        private static bool TryValidateSha256SumsRuntime(
            string runtimeDirectory,
            string sha256SumsPath,
            out string errorMessage)
        {
            errorMessage = string.Empty;
            var validatedFileCount = 0;
            var validatedFileNames = new HashSet<string>(StringComparer.Ordinal);
            foreach (var line in File.ReadLines(sha256SumsPath))
            {
                if (string.IsNullOrWhiteSpace(line) || line.TrimStart().StartsWith("#", StringComparison.Ordinal))
                {
                    continue;
                }

                if (!TryParseSha256Line(line, out var expectedHash, out var fileName))
                {
                    errorMessage = "The bundled FFmpeg shared-library runtime checksum file contains an invalid entry.";
                    return false;
                }

                if (!IsSafeLeafFileName(fileName))
                {
                    errorMessage = "The bundled FFmpeg shared-library runtime checksum file contains an unsafe file entry.";
                    return false;
                }

                var filePath = Path.Combine(runtimeDirectory, fileName);
                if (!File.Exists(filePath))
                {
                    errorMessage = "The bundled FFmpeg shared-library runtime is missing " + fileName + ".";
                    return false;
                }

                var actualHash = ComputeSha256(filePath);
                if (!string.Equals(actualHash, expectedHash, StringComparison.OrdinalIgnoreCase))
                {
                    errorMessage = "The bundled FFmpeg shared-library runtime failed integrity validation for " + fileName + ".";
                    return false;
                }

                validatedFileCount++;
                validatedFileNames.Add(fileName);
            }

            if (validatedFileCount == 0)
            {
                errorMessage = "The bundled FFmpeg shared-library runtime checksum file is empty.";
                return false;
            }

            foreach (var requiredFile in ResolveRequiredRuntimeFiles())
            {
                if (!validatedFileNames.Contains(requiredFile))
                {
                    errorMessage = "The bundled FFmpeg shared-library runtime checksum file does not include " + requiredFile + ".";
                    return false;
                }
            }

            return true;
        }

        private static string[] ResolveRequiredRuntimeFiles()
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                return WindowsRequiredRuntimeFiles;
            }

            if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            {
                return MacRequiredRuntimeFiles;
            }

            return LinuxRequiredRuntimeFiles;
        }

        private static bool TryParseSha256Line(string line, out string expectedHash, out string fileName)
        {
            expectedHash = string.Empty;
            fileName = string.Empty;
            var trimmed = line.Trim();
            var separatorIndex = trimmed.IndexOfAny(new[] { ' ', '\t' });
            if (separatorIndex <= 0)
            {
                return false;
            }

            expectedHash = trimmed.Substring(0, separatorIndex);
            fileName = trimmed.Substring(separatorIndex).Trim();
            return expectedHash.Length == Sha256HexLength &&
                expectedHash.All(IsHexCharacter) &&
                !string.IsNullOrWhiteSpace(fileName);
        }

        private static bool IsHexCharacter(char value)
        {
            return (value >= '0' && value <= '9') ||
                (value >= 'a' && value <= 'f') ||
                (value >= 'A' && value <= 'F');
        }

        private static ExportRuntimeManifest? LoadManifest()
        {
            var assembly = Assembly.GetExecutingAssembly();
            using (var stream = assembly.GetManifestResourceStream(ResourceName))
            {
                if (stream != null)
                {
                    return ReadManifest(stream);
                }
            }

            var copiedManifestPath = Path.Combine(AppContext.BaseDirectory, CopiedManifestRelativePath);
            if (!File.Exists(copiedManifestPath))
            {
                return null;
            }

            using (var stream = File.OpenRead(copiedManifestPath))
            {
                return ReadManifest(stream);
            }
        }

        private static ExportRuntimeManifest? ReadManifest(Stream stream)
        {
            var serializer = new DataContractJsonSerializer(
                typeof(ExportRuntimeManifest),
                new DataContractJsonSerializerSettings
                {
                    UseSimpleDictionaryFormat = true
                });
            return serializer.ReadObject(stream) as ExportRuntimeManifest;
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
            public string AssetName { get; set; } = string.Empty;

            [DataMember(Name = "files")]
            public Dictionary<string, string> Files { get; set; } = new Dictionary<string, string>();
        }
    }
}
