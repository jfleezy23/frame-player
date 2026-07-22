using System;
using System.Runtime.InteropServices;
using System.Threading;

namespace FramePlayer.Core.Models
{
    // Platform-neutral raw frame payload for universal rendering surfaces.
    public sealed class DecodedFrameBuffer : IDisposable
    {
        public DecodedFrameBuffer(
            FrameDescriptor descriptor,
            byte[] pixelBuffer,
            int stride,
            string pixelFormatName)
        {
            Descriptor = descriptor ?? throw new ArgumentNullException(nameof(descriptor));
            PixelBuffer = pixelBuffer ?? Array.Empty<byte>();
            PixelBufferLength = PixelBuffer.Length;
            Stride = stride;
            PixelFormatName = pixelFormatName ?? string.Empty;
        }

        public DecodedFrameBuffer(
            FrameDescriptor descriptor,
            SafeHandle nativePixelBuffer,
            IntPtr pixelBufferPointer,
            int pixelBufferLength,
            int stride,
            string pixelFormatName)
        {
            ArgumentOutOfRangeException.ThrowIfNegative(pixelBufferLength);

            Descriptor = descriptor ?? throw new ArgumentNullException(nameof(descriptor));
            _nativePixelBuffer = new NativePixelBufferReference(
                nativePixelBuffer ?? throw new ArgumentNullException(nameof(nativePixelBuffer)),
                pixelBufferPointer,
                pixelBufferLength);
            PixelBufferLength = pixelBufferLength;
            PixelBuffer = Array.Empty<byte>();
            Stride = stride;
            PixelFormatName = pixelFormatName ?? string.Empty;
        }

        private DecodedFrameBuffer(
            FrameDescriptor descriptor,
            NativePixelBufferReference nativePixelBuffer,
            int stride,
            string pixelFormatName)
        {
            Descriptor = descriptor ?? throw new ArgumentNullException(nameof(descriptor));
            _nativePixelBuffer = nativePixelBuffer ?? throw new ArgumentNullException(nameof(nativePixelBuffer));
            PixelBufferLength = nativePixelBuffer.Length;
            PixelBuffer = Array.Empty<byte>();
            Stride = stride;
            PixelFormatName = pixelFormatName ?? string.Empty;
        }

        private readonly NativePixelBufferReference _nativePixelBuffer;
        private int _disposed;

        public FrameDescriptor Descriptor { get; }

        public byte[] PixelBuffer { get; }

        public SafeHandle NativePixelBuffer
        {
            get { return _nativePixelBuffer?.Handle; }
        }

        public IntPtr PixelBufferPointer
        {
            get { return _nativePixelBuffer != null ? _nativePixelBuffer.Pointer : IntPtr.Zero; }
        }

        public int PixelBufferLength { get; }

        public int Stride { get; }

        public string PixelFormatName { get; }

        public bool HasNativePixelBuffer
        {
            get
            {
                return NativePixelBuffer != null &&
                    !NativePixelBuffer.IsInvalid &&
                    _disposed == 0 &&
                    PixelBufferPointer != IntPtr.Zero &&
                    PixelBufferLength > 0;
            }
        }

        public bool HasPixelBuffer
        {
            get { return PixelBuffer.Length > 0 || HasNativePixelBuffer; }
        }

        public int ApproximateByteCount
        {
            get { return PixelBufferLength; }
        }

        public bool TryGetPixelBufferPointer(out IntPtr pixelBufferPointer)
        {
            if (HasNativePixelBuffer)
            {
                pixelBufferPointer = PixelBufferPointer;
                return true;
            }

            pixelBufferPointer = IntPtr.Zero;
            return false;
        }

        public DecodedFrameBuffer WithDescriptor(FrameDescriptor descriptor)
        {
            if (HasNativePixelBuffer)
            {
                return new DecodedFrameBuffer(
                    descriptor ?? throw new ArgumentNullException(nameof(descriptor)),
                    _nativePixelBuffer.Retain(),
                    Stride,
                    PixelFormatName);
            }

            return new DecodedFrameBuffer(
                descriptor ?? throw new ArgumentNullException(nameof(descriptor)),
                PixelBuffer,
                Stride,
                PixelFormatName);
        }

        public DecodedFrameBuffer Retain()
        {
            if (HasNativePixelBuffer)
            {
                return new DecodedFrameBuffer(
                    Descriptor,
                    _nativePixelBuffer.Retain(),
                    Stride,
                    PixelFormatName);
            }

            return new DecodedFrameBuffer(
                Descriptor,
                PixelBuffer,
                Stride,
                PixelFormatName);
        }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 0)
            {
                _nativePixelBuffer?.Release();
            }
        }

        private sealed class NativePixelBufferReference
        {
            private int _referenceCount = 1;

            public NativePixelBufferReference(SafeHandle handle, IntPtr pointer, int length)
            {
                Handle = handle ?? throw new ArgumentNullException(nameof(handle));
                Pointer = pointer;
                Length = length;
            }

            public SafeHandle Handle { get; }

            public IntPtr Pointer { get; }

            public int Length { get; }

            public NativePixelBufferReference Retain()
            {
                while (true)
                {
                    var current = Volatile.Read(ref _referenceCount);
                    ObjectDisposedException.ThrowIf(current <= 0, typeof(DecodedFrameBuffer));

                    if (Interlocked.CompareExchange(ref _referenceCount, current + 1, current) == current)
                    {
                        return this;
                    }
                }
            }

            public void Release()
            {
                var remaining = Interlocked.Decrement(ref _referenceCount);
                if (remaining == 0)
                {
                    Handle.Dispose();
                    return;
                }

                ObjectDisposedException.ThrowIf(remaining < 0, typeof(DecodedFrameBuffer));
            }
        }
    }
}
