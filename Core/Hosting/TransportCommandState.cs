namespace FramePlayer.Core.Hosting
{
    public sealed class TransportCommandState
    {
        public static TransportCommandState Disabled
        {
            get { return new TransportCommandState(); }
        }

        public bool CanControlTransport { get; set; }

        public bool CanTogglePlayPause { get; set; }

        public bool CanStepBackward { get; set; }

        public bool CanStepForward { get; set; }

        public bool CanSeek { get; set; }

        public bool CanCloseMedia { get; set; }

        public bool CanInspectMedia { get; set; }

        public bool IsPlaying { get; set; }
    }
}
