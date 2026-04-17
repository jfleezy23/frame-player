using System;
using FramePlayer.Engines.FFmpeg;

namespace FramePlayer.Services
{
    internal sealed class FfmpegReviewEngineOptionsProvider
    {
        private readonly AppPreferencesService _preferencesService;
        private AppPreferences _preferences;

        public FfmpegReviewEngineOptionsProvider(AppPreferencesService preferencesService)
        {
            _preferencesService = preferencesService ?? throw new ArgumentNullException(nameof(preferencesService));
            _preferences = _preferencesService.Load();
        }

        public bool UseGpuAcceleration
        {
            get
            {
                return ResolveGpuBackendPreference() != GpuBackendPreference.Disabled;
            }
        }

        public bool CanUserToggleGpuAcceleration
        {
            get { return false; }
        }

        public void SetUseGpuAcceleration(bool value)
        {
            _preferences = new AppPreferences(value);
            _preferencesService.Save(_preferences);
        }

        public FfmpegReviewEngineOptions GetCurrent()
        {
            return new FfmpegReviewEngineOptions(
                ResolveGpuBackendPreference(),
                ResolveCacheBudgetOverrideMegabytes());
        }

        private GpuBackendPreference ResolveGpuBackendPreference()
        {
            if (TryResolveEnvironmentOverride(out var gpuBackendPreference))
            {
                return gpuBackendPreference;
            }

            return GpuBackendPreference.Disabled;
        }

        private static bool TryResolveEnvironmentOverride(out GpuBackendPreference gpuBackendPreference)
        {
            gpuBackendPreference = GpuBackendPreference.Disabled;

            var environmentOverride = Environment.GetEnvironmentVariable("FRAMEPLAYER_GPU_BACKEND");
            if (string.IsNullOrWhiteSpace(environmentOverride))
            {
                return false;
            }

            switch (environmentOverride.Trim().ToLowerInvariant())
            {
                case "0":
                case "cpu":
                case "disabled":
                case "off":
                    gpuBackendPreference = GpuBackendPreference.Disabled;
                    return true;
                case "force":
                case "force-vulkan":
                case "vulkan":
                    gpuBackendPreference = GpuBackendPreference.ForceVulkanForDiagnostics;
                    return true;
                case "1":
                case "auto":
                default:
                    gpuBackendPreference = GpuBackendPreference.Auto;
                    return true;
            }
        }

        private static int? ResolveCacheBudgetOverrideMegabytes()
        {
            var rawValue = Environment.GetEnvironmentVariable("FRAMEPLAYER_DECODE_CACHE_MB");
            if (string.IsNullOrWhiteSpace(rawValue))
            {
                return null;
            }

            if (!int.TryParse(rawValue.Trim(), out var parsedValue) || parsedValue <= 0)
            {
                return null;
            }

            return Math.Max(64, Math.Min(4096, parsedValue));
        }
    }
}
