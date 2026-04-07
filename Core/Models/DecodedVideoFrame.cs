using System;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace FramePlayer.Core.Models
{
    public sealed class DecodedVideoFrame
    {
        public DecodedVideoFrame(FrameDescriptor descriptor, BitmapSource bitmapSource)
            : this(descriptor, bitmapSource, null, 0, default(PixelFormat))
        {
        }

        public DecodedVideoFrame(
            FrameDescriptor descriptor,
            BitmapSource bitmapSource,
            byte[] pixelBuffer,
            int stride,
            PixelFormat pixelFormat)
        {
            Descriptor = descriptor ?? throw new ArgumentNullException(nameof(descriptor));
            BitmapSource = bitmapSource;
            // The engine hands ownership of this managed BGRA copy to the frame instance.
            // Callers should treat the buffer as immutable and should not reuse or mutate it.
            PixelBuffer = pixelBuffer;
            Stride = stride;
            PixelFormat = pixelFormat;
        }

        public FrameDescriptor Descriptor { get; }

        public BitmapSource BitmapSource { get; }

        public byte[] PixelBuffer { get; }

        public int Stride { get; }

        public PixelFormat PixelFormat { get; }

        public bool HasPixelBuffer
        {
            get { return PixelBuffer != null && PixelBuffer.Length > 0; }
        }
    }
}
