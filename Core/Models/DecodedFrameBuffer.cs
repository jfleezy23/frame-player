using System;

namespace FramePlayer.Core.Models
{
    // Platform-neutral raw frame payload for future non-WPF rendering surfaces.
    public sealed class DecodedFrameBuffer
    {
        public DecodedFrameBuffer(
            FrameDescriptor descriptor,
            byte[] pixelBuffer,
            int stride,
            string pixelFormatName)
        {
            Descriptor = descriptor ?? throw new ArgumentNullException(nameof(descriptor));
            PixelBuffer = pixelBuffer ?? Array.Empty<byte>();
            Stride = stride;
            PixelFormatName = pixelFormatName ?? string.Empty;
        }

        public FrameDescriptor Descriptor { get; }

        public byte[] PixelBuffer { get; }

        public int Stride { get; }

        public string PixelFormatName { get; }

        public bool HasPixelBuffer
        {
            get { return PixelBuffer.Length > 0; }
        }

        public int ApproximateByteCount
        {
            get { return PixelBuffer.Length; }
        }
    }
}
