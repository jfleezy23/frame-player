using System;

namespace FramePlayer.Core.Models
{
    /// <summary>
    /// Local JSON contract sent from the interactive app to its hidden export-host child process.
    /// </summary>
    /// <remarks>
    /// The export host is another Frame Player process launched with a private temporary request file.
    /// It is not a socket listener, web service, or telemetry channel.
    /// </remarks>
    internal sealed class ExportHostRequest
    {
        public string Operation { get; set; } = string.Empty;

        public string ResponseJsonPath { get; set; } = string.Empty;

        public string ErrorPath { get; set; } = string.Empty;

        public string ProbeFilePath { get; set; } = string.Empty;

        public ClipExportPlan? ClipExportPlan { get; set; }

        public AudioInsertionPlan? AudioInsertionPlan { get; set; }

        public CompareSideBySideExportPlan? CompareSideBySideExportPlan { get; set; }
    }
}
