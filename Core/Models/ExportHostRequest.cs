using System;

namespace FramePlayer.Core.Models
{
    internal sealed class ExportHostRequest
    {
        public string Operation { get; set; } = string.Empty;

        public string ResponseJsonPath { get; set; } = string.Empty;

        public string ErrorPath { get; set; } = string.Empty;

        public string ProbeFilePath { get; set; } = string.Empty;

        public ClipExportPlan ClipExportPlan { get; set; }

        public AudioInsertionPlan AudioInsertionPlan { get; set; }

        public CompareSideBySideExportPlan CompareSideBySideExportPlan { get; set; }
    }
}
