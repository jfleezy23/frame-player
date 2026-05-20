using System;

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
        private static readonly Lazy<ManifestData> Manifest = new Lazy<ManifestData>(() => ManifestValidationHelper.LoadManifest(ResourceName));

        public static bool TryValidateRuntimeDirectory(string runtimeDirectory, out string errorMessage)
        {
            return ManifestValidationHelper.TryValidateDirectory(
                runtimeDirectory,
                Manifest,
                "The bundled FFmpeg export runtime directory is missing.",
                "The bundled FFmpeg export runtime manifest is missing or invalid.",
                "The bundled FFmpeg export runtime manifest contains an invalid file entry.",
                "The bundled FFmpeg export runtime is missing ",
                ".",
                "The bundled FFmpeg export runtime failed integrity validation for ",
                ".",
                out errorMessage);
        }
    }
}

