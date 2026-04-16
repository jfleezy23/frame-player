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
    public static class RuntimeManifestService
    {
        private const string ResourcePrefix = "FramePlayer.Runtime.manifests.";
        private static readonly ConcurrentDictionary<string, Lazy<RuntimeManifest>> ManifestByRuntimeIdentifier =
            new ConcurrentDictionary<string, Lazy<RuntimeManifest>>(StringComparer.OrdinalIgnoreCase);

        public static bool TryValidateRuntimeDirectory(string runtimeDirectory, out string errorMessage)
        {
            return TryValidateRuntimeDirectory(runtimeDirectory, GetCurrentRuntimeIdentifier(), out errorMessage);
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
            return GetExpectedAssetName(GetCurrentRuntimeIdentifier());
        }

        public static string GetExpectedAssetName(string runtimeIdentifier)
        {
            var manifest = GetManifest(runtimeIdentifier);
            return manifest != null ? manifest.AssetName ?? string.Empty : string.Empty;
        }

        public static string GetCurrentRuntimeIdentifier()
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

        private static RuntimeManifest GetManifest(string runtimeIdentifier)
        {
            if (string.IsNullOrWhiteSpace(runtimeIdentifier))
            {
                return null;
            }

            var lazyManifest = ManifestByRuntimeIdentifier.GetOrAdd(
                runtimeIdentifier,
                key => new Lazy<RuntimeManifest>(() => LoadManifest(key)));
            return lazyManifest.Value;
        }

        private static RuntimeManifest LoadManifest(string runtimeIdentifier)
        {
            var assembly = Assembly.GetExecutingAssembly();
            var resourceName = ResourcePrefix + runtimeIdentifier + ".runtime-manifest.json";
            using (var stream = assembly.GetManifestResourceStream(resourceName))
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
