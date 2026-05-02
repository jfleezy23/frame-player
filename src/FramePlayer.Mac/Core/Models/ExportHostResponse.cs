namespace FramePlayer.Core.Models
{
    /// <summary>
    /// Local JSON response written by the hidden export-host child process.
    /// </summary>
    /// <remarks>
    /// The response is read from the same private temporary workspace used for the request and is
    /// deleted by the caller after the operation completes.
    /// </remarks>
    internal sealed class ExportHostResponse
    {
        public string Operation { get; set; } = string.Empty;

        public string FailureMessage { get; set; } = string.Empty;

        public VideoMediaInfo? MediaInfo { get; set; }

        public ClipExportResult? ClipExportResult { get; set; }

        public AudioInsertionResult? AudioInsertionResult { get; set; }

        public CompareSideBySideExportResult? CompareSideBySideExportResult { get; set; }
    }
}
