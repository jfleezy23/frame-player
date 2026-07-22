using System;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;
using FramePlayer.Engines.FFmpeg;
using Xunit;

namespace FramePlayer.Core.Tests
{
    public sealed class RustFfmpegInteropSafetyTests
    {
        [Fact]
        public void CancellationFlag_ExposesPinnedAtomicStateToNativeCode()
        {
            using var cancellationFlag = new RustFfmpegCancellationFlag();
            var pointer = cancellationFlag.Pointer;
            Assert.NotEqual(IntPtr.Zero, pointer);
            Assert.Equal(0L, pointer.ToInt64() % sizeof(int));
            Assert.Equal(0, Marshal.ReadInt32(pointer));

            using (var cancellation = new CancellationTokenSource())
            using (cancellationFlag.Register(cancellation.Token))
            {
                cancellation.Cancel();
                Assert.Equal(1, Marshal.ReadInt32(pointer));
            }

            cancellationFlag.Dispose();
            Assert.Throws<ObjectDisposedException>(() => cancellationFlag.Pointer);
        }

        [Fact]
        public void NativeArray_RoundTripsFrameMetadataAndChecksOffsets()
        {
            const int frameCount = 2;
            var frameBytes = Marshal.SizeOf<RustFfmpegBgraFrameConverter.NativeFrame>();
            var frames = Marshal.AllocHGlobal(checked(frameCount * frameBytes));
            try
            {
                var expected = new RustFfmpegBgraFrameConverter.NativeFrame
                {
                    AbsoluteFrameIndex = 7,
                    PresentationTimestamp = 11,
                    PixelBuffer = new IntPtr(1234),
                    PixelData = new IntPtr(5678),
                    PixelBufferLength = new UIntPtr(4096),
                    Width = 32,
                    Height = 18
                };

                RustFfmpegNativeArray.Write(frames, 1, frameCount, expected);
                var actual = RustFfmpegNativeArray.Read<RustFfmpegBgraFrameConverter.NativeFrame>(
                    frames,
                    1,
                    frameCount);

                Assert.Equal(expected.AbsoluteFrameIndex, actual.AbsoluteFrameIndex);
                Assert.Equal(expected.PresentationTimestamp, actual.PresentationTimestamp);
                Assert.Equal(expected.PixelBuffer, actual.PixelBuffer);
                Assert.Equal(expected.PixelData, actual.PixelData);
                Assert.Equal(expected.PixelBufferLength, actual.PixelBufferLength);
                Assert.Equal(expected.Width, actual.Width);
                Assert.Equal(expected.Height, actual.Height);

                actual.PixelBuffer = IntPtr.Zero;
                RustFfmpegNativeArray.Write(frames, 1, frameCount, actual);
                Assert.Equal(
                    IntPtr.Zero,
                    RustFfmpegNativeArray.Read<RustFfmpegBgraFrameConverter.NativeFrame>(
                        frames,
                        1,
                        frameCount).PixelBuffer);
            }
            finally
            {
                Marshal.FreeHGlobal(frames);
            }

            Assert.Throws<ArgumentException>(() =>
                RustFfmpegNativeArray.Read<RustFfmpegBgraFrameConverter.NativeFrame>(
                    IntPtr.Zero,
                    0,
                    1));
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                RustFfmpegNativeArray.Read<RustFfmpegBgraFrameConverter.NativeFrame>(
                    new IntPtr(1),
                    1,
                    1));
            Assert.Throws<OverflowException>(() =>
                RustFfmpegNativeArray.Read<RustFfmpegBgraFrameConverter.NativeFrame>(
                    new IntPtr(1),
                    int.MaxValue - 1,
                    int.MaxValue));
        }

        [Fact]
        public void RustFrameConverter_AcceptsOpaquePointerWithoutPointerTypedWrapperApi()
        {
            var method = typeof(RustFfmpegBgraFrameConverter).GetMethod(
                nameof(RustFfmpegBgraFrameConverter.TryConvert),
                BindingFlags.Instance | BindingFlags.Public);
            var firstParameter = Assert.Single(
                method?.GetParameters().Take(1) ?? Array.Empty<ParameterInfo>());

            Assert.Equal(typeof(IntPtr), firstParameter.ParameterType);
            Assert.False(firstParameter.ParameterType.IsPointer);
        }

        [Fact]
        public void DecodeFrameCopy_PreservesOriginalValidationFailureBeforeOwnershipTransfer()
        {
            var nativeFrame = new RustFfmpegBgraFrameConverter.NativeFrame();
            var timeBase = new FFmpeg.AutoGen.AVRational { num = 1, den = 1000 };
            var toDecodedFrame = typeof(RustFfmpegDecodeCore).GetMethod(
                "ToDecodedFrameBuffer",
                BindingFlags.Static | BindingFlags.NonPublic);
            var directInvocation = Assert.Throws<TargetInvocationException>(() =>
                toDecodedFrame?.Invoke(null, new object[] { nativeFrame, timeBase }));

            var frameBytes = Marshal.SizeOf<RustFfmpegBgraFrameConverter.NativeFrame>();
            var frames = Marshal.AllocHGlobal(frameBytes);
            try
            {
                RustFfmpegNativeArray.Write(
                    frames,
                    0,
                    1,
                    nativeFrame);
                var result = new RustFfmpegDecodeCore.NativeDecodeWindowResult
                {
                    Frames = frames,
                    FrameCount = 1
                };
                var copyFrames = typeof(RustFfmpegDecodeCore).GetMethod(
                    "CopyFrames",
                    BindingFlags.Static | BindingFlags.NonPublic);

                var invocation = Assert.Throws<TargetInvocationException>(() =>
                    copyFrames?.Invoke(
                        null,
                        new object[]
                        {
                            result,
                            timeBase,
                            4096L,
                            4096L,
                            CancellationToken.None
                        }));
                Assert.NotNull(directInvocation.InnerException);
                Assert.NotNull(invocation.InnerException);
                Assert.IsNotType<NullReferenceException>(invocation.InnerException);
                Assert.Equal(directInvocation.InnerException.GetType(), invocation.InnerException.GetType());
                Assert.Equal(directInvocation.InnerException.Message, invocation.InnerException.Message);
            }
            finally
            {
                Marshal.FreeHGlobal(frames);
            }
        }
    }
}
