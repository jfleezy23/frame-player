using System;

namespace FramePlayer.Services
{
    /// <summary>
    /// Validates developer/test FFmpeg CLI tools before scripts or harness flows use them.
    /// </summary>
    /// <remarks>
    /// The CLI tools are intentionally excluded from shipped app output; this validator exists for
    /// local harness and packaging support paths that need those tools during development.
    /// </remarks>
    internal static class ExportToolsManifestService
    {
        private const string ResourceName = "FramePlayer.Runtime.export-tools-manifest.json";
        private static readonly Lazy<ManifestData> Manifest = new Lazy<ManifestData>(() => ManifestValidationHelper.LoadManifest(ResourceName));

        public static bool TryValidateToolsDirectory(string toolsDirectory, out string errorMessage)
        {
            return ManifestValidationHelper.TryValidateDirectory(
                toolsDirectory,
                Manifest,
                "The bundled FFmpeg export tools directory is missing.",
                "The bundled FFmpeg export tools manifest is missing or invalid.",
                "The bundled FFmpeg export tools manifest contains an invalid file entry.",
                "The bundled FFmpeg export tools are missing ",
                ".",
                "The bundled FFmpeg export tools failed integrity validation for ",
                ".",
                out errorMessage);
        }

        public static string GetExpectedAssetName()
        {
            return Manifest.Value != null ? Manifest.Value.AssetName ?? string.Empty : string.Empty;
        }
    }
}

