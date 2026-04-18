namespace FramePlayer.Core.Models
{
    internal sealed class ExportHostResponse
    {
        public string Operation { get; set; } = string.Empty;

        public string FailureMessage { get; set; } = string.Empty;

        public VideoMediaInfo MediaInfo { get; set; }

        public ClipExportResult ClipExportResult { get; set; }

        public AudioInsertionResult AudioInsertionResult { get; set; }

        public CompareSideBySideExportResult CompareSideBySideExportResult { get; set; }
    }
}
