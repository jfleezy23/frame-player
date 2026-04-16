namespace FramePlayer.Core.Hosting
{
    public sealed class ExportCommandState
    {
        public static ExportCommandState Empty { get; } =
            new ExportCommandState(false, false, "Clip export is unavailable.");

        public ExportCommandState(
            bool isToolingAvailable,
            bool canExportCurrentLoop,
            string statusText)
        {
            IsToolingAvailable = isToolingAvailable;
            CanExportCurrentLoop = canExportCurrentLoop;
            StatusText = statusText ?? string.Empty;
        }

        public bool IsToolingAvailable { get; }

        public bool CanExportCurrentLoop { get; }

        public string StatusText { get; }
    }
}
