using System;

namespace FramePlayer.Services
{
    /// <summary>
    /// Validates the in-process FFmpeg playback runtime against the embedded manifest.
    /// </summary>
    /// <remarks>
    /// This is a startup/runtime trust boundary: the app only enables the bundled native
    /// playback DLLs when every manifest-listed leaf file exists and matches its SHA256.
    /// </remarks>
    internal static class RuntimeManifestService
    {
        private const string ResourceName = "FramePlayer.Runtime.runtime-manifest.json";
        private static readonly Lazy<ManifestData> Manifest = new Lazy<ManifestData>(() => ManifestValidationHelper.LoadManifest(ResourceName));

        public static bool TryValidateRuntimeDirectory(string runtimeDirectory, out string errorMessage)
        {
            return ManifestValidationHelper.TryValidateDirectory(
                runtimeDirectory,
                Manifest,
                "The FFmpeg runtime directory is missing.",
                "The embedded runtime manifest is missing or invalid.",
                "The embedded runtime manifest contains an invalid file entry.",
                "The FFmpeg runtime is missing ",
                ".",
                "The FFmpeg runtime failed integrity validation for ",
                ".",
                out errorMessage);
        }

        public static string GetExpectedAssetName()
        {
            return Manifest.Value != null ? Manifest.Value.AssetName ?? string.Empty : string.Empty;
        }
    }
}

