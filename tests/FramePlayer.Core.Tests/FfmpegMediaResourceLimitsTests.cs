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
        public void RustInteropResultLayouts_MatchNativeReprCContracts()
        {
            Assert.True(Environment.Is64BitProcess);
            Assert.Equal(256, Marshal.SizeOf<RustFfmpegNativeMessage>());

            Assert.Equal(272, Marshal.SizeOf<RustFfmpegProbe.NativeProbeResult>());
            Assert.Equal(
                new IntPtr(16),
                Marshal.OffsetOf<RustFfmpegProbe.NativeProbeResult>(
                    nameof(RustFfmpegProbe.NativeProbeResult.Message)));

            Assert.Equal(88, Marshal.SizeOf<RustFfmpegBgraFrameConverter.NativeFrame>());
            Assert.Equal(352, Marshal.SizeOf<RustFfmpegBgraFrameConverter.NativeFrameConvertResult>());
            Assert.Equal(
                new IntPtr(96),
                Marshal.OffsetOf<RustFfmpegBgraFrameConverter.NativeFrameConvertResult>(
                    nameof(RustFfmpegBgraFrameConverter.NativeFrameConvertResult.Message)));

            Assert.Equal(288, Marshal.SizeOf<RustFfmpegDecodeCore.NativeDecodeWindowResult>());
            Assert.Equal(
                new IntPtr(28),
                Marshal.OffsetOf<RustFfmpegDecodeCore.NativeDecodeWindowResult>(
                    nameof(RustFfmpegDecodeCore.NativeDecodeWindowResult.Message)));

            Assert.Equal(288, Marshal.SizeOf<RustFfmpegGlobalFrameIndexBuilder.NativeGlobalFrameIndexResult>());
            Assert.Equal(
                new IntPtr(32),
                Marshal.OffsetOf<RustFfmpegGlobalFrameIndexBuilder.NativeGlobalFrameIndexResult>(
                    nameof(RustFfmpegGlobalFrameIndexBuilder.NativeGlobalFrameIndexResult.Message)));
        }

        [Fact]
        public unsafe void OpenInput_RejectsMissingFormatContextStorage()
        {
            Assert.Throws<ArgumentNullException>(() =>
                FfmpegNativeHelpers.OpenInput(null, "sample.mov", null, null));
        }

        [Fact]
        public void RustInteropMessage_DecodesNullTerminatedAndFullCapacityUtf8()
        {
            var message = default(RustFfmpegNativeMessage);
            for (var index = 0; index < RustFfmpegNativeMessage.Capacity; index++)
            {
                message[index] = byte.MaxValue;
            }

            message = new RustFfmpegNativeMessage();
            for (var index = 0; index < RustFfmpegNativeMessage.Capacity; index++)
            {
                Assert.Equal(0, message[index]);
            }

            Assert.Equal(string.Empty, message.ToString());

            message[0] = (byte)'o';
            message[1] = (byte)'k';
            message[2] = 0;
            message[3] = (byte)'x';

            Assert.Equal("ok", message.ToString());

            for (var index = 0; index < RustFfmpegNativeMessage.Capacity; index++)
            {
                message[index] = (byte)'a';
            }

            Assert.Equal(new string('a', RustFfmpegNativeMessage.Capacity), message.ToString());
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
