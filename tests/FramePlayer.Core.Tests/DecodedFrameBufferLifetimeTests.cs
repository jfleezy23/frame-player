using System;
using FramePlayer.Core.Models;
using Microsoft.Win32.SafeHandles;
using Xunit;

namespace FramePlayer.Core.Tests
{
    public sealed class DecodedFrameBufferLifetimeTests
    {
        [Fact]
        public void NativeBuffer_IsReleasedAfterRetainedViewsAreDisposed()
        {
            var handle = new CountingSafeHandle(new IntPtr(1234));
            var frame = new DecodedFrameBuffer(
                CreateDescriptor(1L),
                handle,
                new IntPtr(5678),
                pixelBufferLength: 16,
                stride: 8,
                pixelFormatName: "bgra");

            var retainedFrame = frame.Retain();
            var normalizedFrame = frame.WithDescriptor(CreateDescriptor(2L));

            frame.Dispose();
            Assert.Equal(0, handle.ReleaseCount);

            retainedFrame.Dispose();
            Assert.Equal(0, handle.ReleaseCount);

            normalizedFrame.Dispose();
            Assert.Equal(1, handle.ReleaseCount);

            normalizedFrame.Dispose();
            Assert.Equal(1, handle.ReleaseCount);
        }

        private static FrameDescriptor CreateDescriptor(long frameIndex)
        {
            return new FrameDescriptor(
                frameIndex,
                TimeSpan.FromMilliseconds(frameIndex * 33d),
                isKeyFrame: frameIndex == 0L,
                isFrameIndexAbsolute: true,
                pixelWidth: 2,
                pixelHeight: 2,
                pixelFormatName: "bgra",
                sourcePixelFormatName: "bgra",
                presentationTimestamp: frameIndex,
                decodeTimestamp: frameIndex,
                durationTimestamp: null);
        }

        private sealed class CountingSafeHandle : SafeHandleZeroOrMinusOneIsInvalid
        {
            public CountingSafeHandle(IntPtr handle)
                : base(ownsHandle: true)
            {
                SetHandle(handle);
            }

            public int ReleaseCount { get; private set; }

            protected override bool ReleaseHandle()
            {
                ReleaseCount++;
                return true;
            }
        }
    }
}
