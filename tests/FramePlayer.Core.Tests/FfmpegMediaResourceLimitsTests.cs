using System;
using System.Runtime.InteropServices;
using FFmpeg.AutoGen;
using FramePlayer.Engines.FFmpeg;
using Xunit;

namespace FramePlayer.Core.Tests
{
    public sealed class FfmpegMediaResourceLimitsTests
    {
        private const long MiB = 1024L * 1024L;

        [Fact]
        public void ResolveDecodedFrameByteLimit_UsesSmallerPaneBudget()
        {
            Assert.Equal(64L * MiB, FfmpegMediaResourceLimits.ResolveDecodedFrameByteLimit(64L * MiB));
            Assert.Equal(
                FfmpegMediaResourceLimits.AbsoluteDecodedFrameByteLimit,
                FfmpegMediaResourceLimits.ResolveDecodedFrameByteLimit(1024L * MiB));
            Assert.Equal(
                FfmpegMediaResourceLimits.AbsoluteDecodedFrameByteLimit,
                FfmpegMediaResourceLimits.ResolveDecodedFrameByteLimit(0L));
        }

        [Fact]
        public void EnsureBgraFrameWithinLimit_AcceptsEightKFrame()
        {
            var byteCount = FfmpegMediaResourceLimits.EnsureBgraFrameWithinLimit(
                width: 7680,
                height: 4320,
                maxBytes: FfmpegMediaResourceLimits.AbsoluteDecodedFrameByteLimit);

            Assert.Equal(132_710_400L, byteCount);
        }

        [Fact]
        public void EnsureBgraFrameWithinLimit_RejectsValidOversizedDimensionsBeforeAllocation()
        {
            var exception = Assert.Throws<FfmpegMediaResourceLimitException>(() =>
                FfmpegMediaResourceLimits.EnsureBgraFrameWithinLimit(
                    width: 16_255,
                    height: 16_255,
                    maxBytes: FfmpegMediaResourceLimits.AbsoluteDecodedFrameByteLimit));

            Assert.Contains("1,056,900,100", exception.Message, StringComparison.Ordinal);
        }

        [Fact]
        public void TryReserveBytes_EnforcesExactCumulativeBoundaryWithoutOverflow()
        {
            const long limit = 256L * MiB;

            Assert.True(FfmpegMediaResourceLimits.TryReserveBytes(limit - 4L, 4L, limit, out var exactTotal));
            Assert.Equal(limit, exactTotal);
            Assert.False(FfmpegMediaResourceLimits.TryReserveBytes(limit - 4L, 5L, limit, out _));
            Assert.False(FfmpegMediaResourceLimits.TryReserveBytes(long.MaxValue - 1L, 2L, long.MaxValue, out _));
        }

        [Fact]
        public void TryReserveDecodedFrameBytes_ChargesManagedAndNativeMetadata()
        {
            const long pixelBytes = 4L;
            const long nativeMetadataBytes = 96L;
            var exactLimit = pixelBytes +
                nativeMetadataBytes +
                FfmpegMediaResourceLimits.ManagedDecodedFrameOverheadBytes;

            Assert.True(FfmpegMediaResourceLimits.TryReserveDecodedFrameBytes(
                0L,
                pixelBytes,
                nativeMetadataBytes,
                exactLimit,
                out var exactTotal));
            Assert.Equal(exactLimit, exactTotal);
            Assert.False(FfmpegMediaResourceLimits.TryReserveDecodedFrameBytes(
                0L,
                pixelBytes,
                nativeMetadataBytes,
                exactLimit - 1L,
                out _));
        }

        [Fact]
        public void EnsureGlobalFrameIndexCapacity_EnforcesEntryAndElapsedTimeLimits()
        {
            Assert.True(
                FfmpegMediaResourceLimits.GlobalFrameIndexEntryLimit *
                FfmpegMediaResourceLimits.EstimatedManagedGlobalFrameIndexBytesPerEntry <=
                FfmpegMediaResourceLimits.GlobalFrameIndexManagedByteLimit);
            FfmpegMediaResourceLimits.EnsureGlobalFrameIndexCapacity(
                FfmpegMediaResourceLimits.GlobalFrameIndexEntryLimit - 1,
                FfmpegMediaResourceLimits.GlobalFrameIndexTimeLimit);

            Assert.Throws<FfmpegMediaResourceLimitException>(() =>
                FfmpegMediaResourceLimits.EnsureGlobalFrameIndexCapacity(
                    FfmpegMediaResourceLimits.GlobalFrameIndexEntryLimit,
                    TimeSpan.Zero));
            Assert.Throws<FfmpegMediaResourceLimitException>(() =>
                FfmpegMediaResourceLimits.EnsureGlobalFrameIndexCapacity(
                    0,
                FfmpegMediaResourceLimits.GlobalFrameIndexTimeLimit + TimeSpan.FromTicks(1L)));
        }

        [Fact]
        public void FfmpegInterruptCallbackLayout_MatchesPinnedNativeRuntimeContract()
        {
            Assert.Equal(
                new IntPtr(216),
                Marshal.OffsetOf<AVFormatContext>(nameof(AVFormatContext.interrupt_callback)));
            Assert.Equal(
                new IntPtr(IntPtr.Size),
                Marshal.OffsetOf<AVIOInterruptCB>(nameof(AVIOInterruptCB.opaque)));
            Assert.True(
                RustFfmpegNativeLayout.TryValidateDecodeCore(out var errorMessage),
                errorMessage);
        }

        [Fact]
        public void GlobalFrameIndex_AutoModeDoesNotBypassRustResourceLimit()
        {
            var resourceLimited = new RustFfmpegGlobalFrameIndexResult(
                false,
                "resource-limit-exceeded",
                "bounded",
                0,
                0,
                Array.Empty<RustFfmpegGlobalFrameIndexEntry>());
            var unavailable = RustFfmpegGlobalFrameIndexResult.Unavailable(
                "native-library-missing",
                "unavailable");

            Assert.False(FfmpegGlobalFrameIndex.ShouldFallBackToManagedIndex(
                RustFfmpegGlobalFrameIndexBuildMode.Auto,
                resourceLimited));
            Assert.True(FfmpegGlobalFrameIndex.ShouldFallBackToManagedIndex(
                RustFfmpegGlobalFrameIndexBuildMode.Auto,
                unavailable));
            Assert.False(FfmpegGlobalFrameIndex.ShouldFallBackToManagedIndex(
                RustFfmpegGlobalFrameIndexBuildMode.Rust,
                unavailable));
        }
    }
}
