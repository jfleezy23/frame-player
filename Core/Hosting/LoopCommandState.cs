namespace FramePlayer.Core.Hosting
{
    public sealed class LoopCommandState
    {
        public LoopCommandState()
        {
            StatusText = "Loop: off";
            ToolTip = "No loop markers are active.";
        }

        public static LoopCommandState Empty
        {
            get { return new LoopCommandState(); }
        }

        public bool CanSetMarkers { get; set; }

        public bool CanClearMarkers { get; set; }

        public bool HasAnyMarkers { get; set; }

        public bool HasReadyRange { get; set; }

        public bool HasPendingMarkers { get; set; }

        public bool IsInvalidRange { get; set; }

        public string StatusText { get; set; }

        public string ToolTip { get; set; }
    }
}
