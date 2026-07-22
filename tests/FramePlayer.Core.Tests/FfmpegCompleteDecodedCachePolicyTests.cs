using System;
using System.Collections.Generic;
using FramePlayer.Core.Models;
using FramePlayer.Engines.FFmpeg;
using Microsoft.Win32.SafeHandles;
using Xunit;

namespace FramePlayer.Core.Tests
{
    public sealed class FfmpegCompleteDecodedCachePolicyTests
    {
        private const long MiB = 1024L * 1024L;
        private const int BgraBytesPerPixel = 4;

        [Fact]
        public void CompleteDecodedCachePolicy_AcceptsSmallClipWithinBudgetThreshold()
        {
            var frameBytes = 640L * 480L * BgraBytesPerPixel;
            var paneBudgetBytes = 64L * MiB;

            var eligible = FfmpegReviewEngine.IsCompleteDecodedCachePolicyEligible(
                indexedFrameCount: 10L,
                approximateFrameBytes: frameBytes,
                paneBudgetBytes: paneBudgetBytes);

            Assert.True(eligible);
            Assert.Equal(9, FfmpegReviewEngine.ResolveCompleteDecodedCacheLimit(10L));
        }

        [Fact]
        public void CompleteDecodedCachePolicy_RejectsLargeClipOverBudgetThreshold()
        {
            var frameBytes = 1920L * 1230L * BgraBytesPerPixel;
            var paneBudgetBytes = 1024L * MiB;

            var eligible = FfmpegReviewEngine.IsCompleteDecodedCachePolicyEligible(
                indexedFrameCount: 1571L,
                approximateFrameBytes: frameBytes,
                paneBudgetBytes: paneBudgetBytes);

            Assert.False(eligible);
        }

        [Fact]
        public void CompleteDecodedCachePolicy_ChargesOverheadForTinyFrames()
        {
            Assert.False(FfmpegReviewEngine.IsCompleteDecodedCachePolicyEligible(
                indexedFrameCount: 100_000L,
                approximateFrameBytes: BgraBytesPerPixel,
                paneBudgetBytes: 64L * MiB));
        }

        [Fact]
        public void CompleteDecodedCachePolicy_DisablesWhenBudgetFallsBelowThreshold()
        {
            var frameBytes = 640L * 480L * BgraBytesPerPixel;
            var indexedFrameCount = 10L;
            var retainedFrameBytes = frameBytes + FfmpegMediaResourceLimits.ManagedDecodedFrameOverheadBytes;
            var requiredRetainedBytes = indexedFrameCount * retainedFrameBytes;
            var exactEligibleBudgetBytes = (requiredRetainedBytes * 4L + 2L) / 3L;

            Assert.True(FfmpegReviewEngine.IsCompleteDecodedCachePolicyEligible(
                indexedFrameCount,
                frameBytes,
                exactEligibleBudgetBytes));
            Assert.False(FfmpegReviewEngine.IsCompleteDecodedCachePolicyEligible(
                indexedFrameCount,
                frameBytes,
                exactEligibleBudgetBytes - 1L));
        }

        [Fact]
        public void CompleteDecodedCachePolicy_RetriesOnlyAfterBudgetIncreases()
        {
            const long rejectedBudgetBytes = 256L * MiB;

            Assert.False(FfmpegReviewEngine.ShouldRetryCompleteDecodedCache(0L, rejectedBudgetBytes));
            Assert.False(FfmpegReviewEngine.ShouldRetryCompleteDecodedCache(
                rejectedBudgetBytes,
                rejectedBudgetBytes));
            Assert.True(FfmpegReviewEngine.ShouldRetryCompleteDecodedCache(
                rejectedBudgetBytes,
                rejectedBudgetBytes + 1L));
        }

        [Fact]
        public void CompleteDecodedCachePolicy_DisposesFramesUntilOwnershipTransfers()
        {
            var releasedHandle = new CountingSafeHandle(new IntPtr(1234));
            var releasedFrame = CreateNativeFrame(releasedHandle);

            FfmpegReviewEngine.DisposeDecodedFramesUnlessTransferred(
                new[] { releasedFrame },
                ownershipTransferred: false);

            Assert.Equal(1, releasedHandle.ReleaseCount);

            var transferredHandle = new CountingSafeHandle(new IntPtr(5678));
            var transferredFrame = CreateNativeFrame(transferredHandle);
            FfmpegReviewEngine.DisposeDecodedFramesUnlessTransferred(
                new[] { transferredFrame },
                ownershipTransferred: true);

            Assert.Equal(0, transferredHandle.ReleaseCount);
            transferredFrame.Dispose();
            Assert.Equal(1, transferredHandle.ReleaseCount);
        }

        [Fact]
        public void DecodedFrameCache_UpdateLimitsTrimsCompleteWindow()
        {
            var cache = new FfmpegDecodedFrameCache(maxPreviousFrames: 9, maxForwardFrames: 9);
            cache.LoadWindow(CreateFrames(10), currentIndex: 5);

            cache.UpdateLimits(maxPreviousFrames: 2, maxForwardFrames: 2);

            Assert.Equal(5, cache.Count);
            Assert.Equal(2, cache.PreviousCount);
            Assert.Equal(2, cache.ForwardCount);

            DecodedFrameBuffer frame;
            Assert.False(cache.TryMoveToAbsoluteFrameIndex(0L, out frame));
            Assert.True(cache.TryMoveToAbsoluteFrameIndex(5L, out frame));
            Assert.NotNull(frame);
        }

        private static List<DecodedFrameBuffer> CreateFrames(int count)
        {
            var frames = new List<DecodedFrameBuffer>(count);
            for (var index = 0; index < count; index++)
            {
                frames.Add(new DecodedFrameBuffer(
                    new FrameDescriptor(
                        index,
                        TimeSpan.FromMilliseconds(index * 33d),
                        isKeyFrame: index == 0,
                        isFrameIndexAbsolute: true,
                        pixelWidth: 1,
                        pixelHeight: 1,
                        pixelFormatName: "bgra",
                        sourcePixelFormatName: "bgra",
                        presentationTimestamp: index,
                        decodeTimestamp: index,
                        durationTimestamp: null),
                    new byte[BgraBytesPerPixel],
                    stride: BgraBytesPerPixel,
                    pixelFormatName: "bgra"));
            }

            return frames;
        }

        private static DecodedFrameBuffer CreateNativeFrame(CountingSafeHandle handle)
        {
            return new DecodedFrameBuffer(
                new FrameDescriptor(
                    0L,
                    TimeSpan.Zero,
                    isKeyFrame: true,
                    isFrameIndexAbsolute: true,
                    pixelWidth: 1,
                    pixelHeight: 1,
                    pixelFormatName: "bgra",
                    sourcePixelFormatName: "bgra",
                    presentationTimestamp: 0L,
                    decodeTimestamp: 0L,
                    durationTimestamp: null),
                handle,
                new IntPtr(1234),
                pixelBufferLength: BgraBytesPerPixel,
                stride: BgraBytesPerPixel,
                pixelFormatName: "bgra");
        }

        private sealed class CountingSafeHandle : SafeHandleZeroOrMinusOneIsInvalid
        {
            internal CountingSafeHandle(IntPtr handle)
                : base(ownsHandle: true)
            {
                SetHandle(handle);
            }

            internal int ReleaseCount { get; private set; }

            protected override bool ReleaseHandle()
            {
                ReleaseCount++;
                handle = IntPtr.Zero;
                return true;
            }
        }
    }
}
