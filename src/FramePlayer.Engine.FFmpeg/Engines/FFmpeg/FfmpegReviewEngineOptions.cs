namespace FramePlayer.Engines.FFmpeg
{
    public sealed class FfmpegReviewEngineOptions
    {
        public static FfmpegReviewEngineOptions Default { get; } =
            new FfmpegReviewEngineOptions(GpuBackendPreference.Auto, null);

        public FfmpegReviewEngineOptions(
            GpuBackendPreference gpuBackendPreference,
            int? cacheBudgetOverrideMegabytes)
        {
            GpuBackendPreference = gpuBackendPreference;
            CacheBudgetOverrideMegabytes = cacheBudgetOverrideMegabytes;
        }

        public GpuBackendPreference GpuBackendPreference { get; }

        public int? CacheBudgetOverrideMegabytes { get; }

        public bool UsesAutomaticCacheBudget
        {
            get { return !CacheBudgetOverrideMegabytes.HasValue; }
        }
    }
}
