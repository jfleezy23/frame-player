using System;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Media.Imaging;
using FramePlayer.Avalonia.Services;
using FramePlayer.Core.Models;
using Microsoft.Win32.SafeHandles;
using Xunit;

namespace FramePlayer.Avalonia.Tests
{
    public sealed class AvaloniaFrameBufferPresenterSafetyTests : IClassFixture<AvaloniaHeadlessFixture>
    {
        private readonly AvaloniaHeadlessFixture _fixture;

        public AvaloniaFrameBufferPresenterSafetyTests(AvaloniaHeadlessFixture fixture)
        {
            _fixture = fixture;
        }

        [Fact]
        public void PresentBitmap_RejectsSourceLayoutsThatCannotContainTheFrame()
        {
            _fixture.Run(() =>
            {
                AssertRejectedSourceLayout(new byte[15], stride: 8);
                AssertRejectedSourceLayout(new byte[16], stride: 7);
                AssertRejectedSourceLayout(new byte[16], stride: -8);
            });
        }

        [Fact]
        public void PresentBitmap_CopiesNativePixelsWhileTheBufferLeaseIsHeld()
        {
            _fixture.Run(() =>
            {
                var expectedPixels = new byte[]
                {
                    1, 2, 3, 255, 4, 5, 6, 255,
                    7, 8, 9, 255, 10, 11, 12, 255
                };
                var pointer = Marshal.AllocHGlobal(expectedPixels.Length);
                Marshal.Copy(expectedPixels, 0, pointer, expectedPixels.Length);
                var handle = new HGlobalPixelBufferHandle(pointer);
                using var frameBuffer = new DecodedFrameBuffer(
                    CreateDescriptor(),
                    handle,
                    pointer,
                    expectedPixels.Length,
                    stride: 8,
                    pixelFormatName: "bgra");
                WriteableBitmap? reusableBitmap = null;

                try
                {
                    var bitmap = AvaloniaFrameBufferPresenter.PresentBitmap(
                        frameBuffer,
                        viewportSnapshot: null,
                        ref reusableBitmap);

                    Assert.NotNull(bitmap);
                    var actualPixels = new byte[expectedPixels.Length];
                    using (var locked = bitmap!.Lock())
                    {
                        for (var row = 0; row < 2; row++)
                        {
                            Marshal.Copy(
                                IntPtr.Add(locked.Address, row * locked.RowBytes),
                                actualPixels,
                                row * 8,
                                8);
                        }
                    }

                    Assert.Equal(expectedPixels, actualPixels);
                    Assert.Equal(0, handle.ReleaseCount);
                }
                finally
                {
                    reusableBitmap?.Dispose();
                }

                frameBuffer.Dispose();
                Assert.Equal(1, handle.ReleaseCount);
            });
        }

        [Fact]
        public void PresentBitmap_KeepsNativePixelsAliveWhenTheCallerDisposesConcurrently()
        {
            _fixture.Run(() =>
            {
                var pixels = new byte[]
                {
                    1, 2, 3, 255, 4, 5, 6, 255,
                    7, 8, 9, 255, 10, 11, 12, 255
                };
                var pointer = Marshal.AllocHGlobal(pixels.Length);
                Marshal.Copy(pixels, 0, pointer, pixels.Length);
                var handle = new HGlobalPixelBufferHandle(pointer, synchronizeSecondValidityCheck: true);
                var frameBuffer = new DecodedFrameBuffer(
                    CreateDescriptor(),
                    handle,
                    pointer,
                    pixels.Length,
                    stride: 8,
                    pixelFormatName: "bgra");
                WriteableBitmap? reusableBitmap = null;
                var callerDisposeTask = Task.Run(() =>
                {
                    Assert.True(
                        handle.SecondValidityCheckReached.Wait(TimeSpan.FromSeconds(5)),
                        "The presenter did not acquire its native buffer lease.");
                    try
                    {
                        frameBuffer.Dispose();
                        Assert.Equal(0, handle.ReleaseCount);
                    }
                    finally
                    {
                        handle.ContinueSecondValidityCheck.Set();
                    }
                });

                try
                {
                    var bitmap = AvaloniaFrameBufferPresenter.PresentBitmap(
                        frameBuffer,
                        viewportSnapshot: null,
                        ref reusableBitmap);

                    Assert.True(
                        callerDisposeTask.Wait(TimeSpan.FromSeconds(5)),
                        "The concurrent caller disposal did not complete.");
                    callerDisposeTask.GetAwaiter().GetResult();
                    Assert.NotNull(bitmap);
                    Assert.Equal(1, handle.ReleaseCount);
                }
                finally
                {
                    handle.ContinueSecondValidityCheck.Set();
                    frameBuffer.Dispose();
                    reusableBitmap?.Dispose();
                    callerDisposeTask.GetAwaiter().GetResult();
                }
            });
        }

        private static void AssertRejectedSourceLayout(byte[] pixels, int stride)
        {
            using var frameBuffer = new DecodedFrameBuffer(
                CreateDescriptor(),
                pixels,
                stride,
                "bgra");
            WriteableBitmap? reusableBitmap = null;

            var bitmap = AvaloniaFrameBufferPresenter.PresentBitmap(
                frameBuffer,
                viewportSnapshot: null,
                ref reusableBitmap);

            Assert.Null(bitmap);
            Assert.Null(reusableBitmap);
        }

        private static FrameDescriptor CreateDescriptor()
        {
            return new FrameDescriptor(
                0,
                TimeSpan.Zero,
                isKeyFrame: true,
                isFrameIndexAbsolute: true,
                pixelWidth: 2,
                pixelHeight: 2,
                pixelFormatName: "bgra",
                sourcePixelFormatName: "bgra",
                presentationTimestamp: 0,
                decodeTimestamp: 0,
                durationTimestamp: null);
        }

        private sealed class HGlobalPixelBufferHandle : SafeHandleZeroOrMinusOneIsInvalid
        {
            private readonly bool _synchronizeSecondValidityCheck;
            private int _validityCheckCount;

            public HGlobalPixelBufferHandle(IntPtr pointer, bool synchronizeSecondValidityCheck = false)
                : base(ownsHandle: true)
            {
                _synchronizeSecondValidityCheck = synchronizeSecondValidityCheck;
                SetHandle(pointer);
            }

            public int ReleaseCount { get; private set; }

            public ManualResetEventSlim SecondValidityCheckReached { get; } = new ManualResetEventSlim(false);

            public ManualResetEventSlim ContinueSecondValidityCheck { get; } = new ManualResetEventSlim(false);

            public override bool IsInvalid
            {
                get
                {
                    var checkCount = Interlocked.Increment(ref _validityCheckCount);
                    if (_synchronizeSecondValidityCheck && checkCount == 2)
                    {
                        SecondValidityCheckReached.Set();
                        Assert.True(
                            ContinueSecondValidityCheck.Wait(TimeSpan.FromSeconds(5)),
                            "The concurrent caller disposal did not release the presenter.");
                    }

                    return handle == IntPtr.Zero || handle == new IntPtr(-1);
                }
            }

            protected override bool ReleaseHandle()
            {
                Marshal.FreeHGlobal(handle);
                ReleaseCount++;
                return true;
            }
        }
    }
}
