namespace FramePlayer.Core.Hosting
{
    public sealed class RecentFileViewState
    {
        public RecentFileViewState(
            string filePath,
            string displayLabel,
            string toolTip,
            bool existsOnDisk)
        {
            FilePath = filePath ?? string.Empty;
            DisplayLabel = displayLabel ?? string.Empty;
            ToolTip = toolTip ?? string.Empty;
            ExistsOnDisk = existsOnDisk;
        }

        public string FilePath { get; }

        public string DisplayLabel { get; }

        public string ToolTip { get; }

        public bool ExistsOnDisk { get; }
    }
}
