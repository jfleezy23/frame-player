namespace FramePlayer.Core.Hosting
{
    public sealed class TransportCommandState
    {
        public static TransportCommandState Disabled { get; } =
            new TransportCommandState(false, false, false, false, false, false, false, false);

        public TransportCommandState(
            bool canControlTransport,
            bool canTogglePlayPause,
            bool canStepBackward,
            bool canStepForward,
            bool canSeek,
            bool canCloseMedia,
            bool canInspectMedia,
            bool isPlaying)
        {
            CanControlTransport = canControlTransport;
            CanTogglePlayPause = canTogglePlayPause;
            CanStepBackward = canStepBackward;
            CanStepForward = canStepForward;
            CanSeek = canSeek;
            CanCloseMedia = canCloseMedia;
            CanInspectMedia = canInspectMedia;
            IsPlaying = isPlaying;
        }

        public bool CanControlTransport { get; }

        public bool CanTogglePlayPause { get; }

        public bool CanStepBackward { get; }

        public bool CanStepForward { get; }

        public bool CanSeek { get; }

        public bool CanCloseMedia { get; }

        public bool CanInspectMedia { get; }

        public bool IsPlaying { get; }
    }
}
