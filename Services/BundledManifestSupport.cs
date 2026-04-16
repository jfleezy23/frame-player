using System;
using System.Collections.Concurrent;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Runtime.Serialization.Json;
using System.Security.Cryptography;

namespace FramePlayer.Services
{
    internal static class BundledManifestSupport
    {
        private const string ResourcePrefix = "FramePlayer.Runtime.manifests.";

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

        public static string GetDefaultExecutableName(string toolName)
        {
            return RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
                ? toolName + ".exe"
                : toolName;
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

        public static TManifest GetManifest<TManifest>(
            ConcurrentDictionary<string, Lazy<TManifest>> manifestCache,
            string runtimeIdentifier,
            string manifestFileName)
            where TManifest : class
        {
            if (manifestCache == null || string.IsNullOrWhiteSpace(runtimeIdentifier) || string.IsNullOrWhiteSpace(manifestFileName))
            {
                return null;
            }

            var lazyManifest = manifestCache.GetOrAdd(
                runtimeIdentifier,
                key => new Lazy<TManifest>(() => LoadManifestResource<TManifest>(key, manifestFileName)));
            return lazyManifest.Value;
        }

        private static TManifest LoadManifestResource<TManifest>(string runtimeIdentifier, string manifestFileName)
            where TManifest : class
        {
            var assembly = Assembly.GetExecutingAssembly();
            var resourceName = ResourcePrefix + runtimeIdentifier + "." + manifestFileName;
            using (var stream = assembly.GetManifestResourceStream(resourceName))
            {
                if (stream == null)
                {
                    return null;
                }

                var serializer = new DataContractJsonSerializer(
                    typeof(TManifest),
                    new DataContractJsonSerializerSettings
                    {
                        UseSimpleDictionaryFormat = true
                    });
                return serializer.ReadObject(stream) as TManifest;
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
    }
}
