namespace FramePlayer.Engines.FFmpeg
{
    public sealed class RustFfmpegProbeResult
    {
        private RustFfmpegProbeResult(
            bool isAvailable,
            int nativeStatus,
            string statusName,
            string message,
            uint avutilVersion,
            uint avcodecVersion,
            uint avformatVersion)
        {
            IsAvailable = isAvailable;
            NativeStatus = nativeStatus;
            StatusName = statusName ?? string.Empty;
            Message = message ?? string.Empty;
            AvutilVersion = avutilVersion;
            AvcodecVersion = avcodecVersion;
            AvformatVersion = avformatVersion;
        }

        public bool IsAvailable { get; }

        public int NativeStatus { get; }

        public string StatusName { get; }

        public string Message { get; }

        public uint AvutilVersion { get; }

        public uint AvcodecVersion { get; }

        public uint AvformatVersion { get; }

        public static RustFfmpegProbeResult NotRun()
        {
            return Unavailable("not-run", "Rust FFmpeg runtime probe has not run.");
        }

        public static RustFfmpegProbeResult Unavailable(string statusName, string message)
        {
            return new RustFfmpegProbeResult(false, -1, statusName, message, 0, 0, 0);
        }

        internal static RustFfmpegProbeResult FromNative(
            int nativeStatus,
            string statusName,
            string message,
            uint avutilVersion,
            uint avcodecVersion,
            uint avformatVersion)
        {
            return new RustFfmpegProbeResult(
                nativeStatus == 0,
                nativeStatus,
                statusName,
                message,
                avutilVersion,
                avcodecVersion,
                avformatVersion);
        }
    }
}
