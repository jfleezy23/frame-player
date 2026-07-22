using System;
using System.IO;
using FramePlayer.Engines.FFmpeg;
using FramePlayer.Services;
using Xunit;

namespace FramePlayer.Core.Tests
{
    public sealed class AppPreferencesServiceTests
    {
        [Fact]
        public void Preferences_RoundTripAndIgnoreMalformedEntries()
        {
            using var storage = new TemporaryPreferencesStorage();
            var service = new AppPreferencesService(storage.FilePath);

            Assert.True(service.Load().UseGpuAcceleration);
            service.Save(new AppPreferences(useGpuAcceleration: false));
            Assert.False(service.Load().UseGpuAcceleration);
            Assert.Contains("useGpuAcceleration=false", File.ReadAllText(storage.FilePath), StringComparison.Ordinal);

            File.WriteAllLines(storage.FilePath, new[]
            {
                string.Empty,
                "invalid",
                "=true",
                "other=value",
                "useGpuAcceleration=not-a-boolean"
            });
            Assert.True(service.Load().UseGpuAcceleration);

            Assert.Throws<ArgumentNullException>(() => service.Save(null!));
            Assert.Throws<ArgumentException>(() => new AppPreferencesService(" "));
        }

        [Fact]
        public void OptionsProvider_AppliesPreferenceAndClampedEnvironmentOverrides()
        {
            using var storage = new TemporaryPreferencesStorage();
            var service = new AppPreferencesService(storage.FilePath);
            service.Save(new AppPreferences(useGpuAcceleration: false));
            var previousGpuBackend = Environment.GetEnvironmentVariable("FRAMEPLAYER_GPU_BACKEND");
            var previousCacheBudget = Environment.GetEnvironmentVariable("FRAMEPLAYER_DECODE_CACHE_MB");

            try
            {
                var provider = new FfmpegReviewEngineOptionsProvider(service);
                Assert.False(provider.UseGpuAcceleration);
                Assert.Equal(GpuBackendPreference.Disabled, provider.GetCurrent().GpuBackendPreference);
                Assert.True(provider.GetCurrent().UsesAutomaticCacheBudget);

                Environment.SetEnvironmentVariable("FRAMEPLAYER_GPU_BACKEND", "force-vulkan");
                Environment.SetEnvironmentVariable("FRAMEPLAYER_DECODE_CACHE_MB", "32");
                var options = provider.GetCurrent();
                Assert.Equal(GpuBackendPreference.ForceVulkanForDiagnostics, options.GpuBackendPreference);
                Assert.Equal(64, options.CacheBudgetOverrideMegabytes);

                Environment.SetEnvironmentVariable("FRAMEPLAYER_GPU_BACKEND", "off");
                Environment.SetEnvironmentVariable("FRAMEPLAYER_DECODE_CACHE_MB", "5000");
                options = provider.GetCurrent();
                Assert.Equal(GpuBackendPreference.Disabled, options.GpuBackendPreference);
                Assert.Equal(4096, options.CacheBudgetOverrideMegabytes);

                Environment.SetEnvironmentVariable("FRAMEPLAYER_GPU_BACKEND", "unexpected");
                Environment.SetEnvironmentVariable("FRAMEPLAYER_DECODE_CACHE_MB", "invalid");
                options = provider.GetCurrent();
                Assert.Equal(GpuBackendPreference.Auto, options.GpuBackendPreference);
                Assert.Null(options.CacheBudgetOverrideMegabytes);

                provider.SetUseGpuAcceleration(true);
                Environment.SetEnvironmentVariable("FRAMEPLAYER_GPU_BACKEND", null);
                Environment.SetEnvironmentVariable("FRAMEPLAYER_DECODE_CACHE_MB", "128");
                options = provider.GetCurrent();
                Assert.True(provider.UseGpuAcceleration);
                Assert.Equal(GpuBackendPreference.Auto, options.GpuBackendPreference);
                Assert.Equal(128, options.CacheBudgetOverrideMegabytes);
                Assert.True(service.Load().UseGpuAcceleration);
            }
            finally
            {
                Environment.SetEnvironmentVariable("FRAMEPLAYER_GPU_BACKEND", previousGpuBackend);
                Environment.SetEnvironmentVariable("FRAMEPLAYER_DECODE_CACHE_MB", previousCacheBudget);
            }
        }

        private sealed class TemporaryPreferencesStorage : IDisposable
        {
            public TemporaryPreferencesStorage()
            {
                DirectoryPath = Path.Combine(
                    Path.GetTempPath(),
                    "frame-player-preferences-tests-" + Guid.NewGuid().ToString("N"));
                FilePath = Path.Combine(DirectoryPath, "preferences.txt");
            }

            public string DirectoryPath { get; }

            public string FilePath { get; }

            public void Dispose()
            {
                if (Directory.Exists(DirectoryPath))
                {
                    Directory.Delete(DirectoryPath, recursive: true);
                }
            }
        }
    }
}
