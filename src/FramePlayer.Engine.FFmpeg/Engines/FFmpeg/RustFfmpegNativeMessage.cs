using System;
using System.Runtime.CompilerServices;
using System.Text;

namespace FramePlayer.Engines.FFmpeg
{
    [InlineArray(Capacity)]
    internal struct RustFfmpegNativeMessage
    {
        internal const int Capacity = 256;

        private byte _element0;

        public RustFfmpegNativeMessage()
        {
            this = default;
            _element0 = 0;
        }

        public override string ToString()
        {
            if (_element0 == 0)
            {
                return string.Empty;
            }

            ReadOnlySpan<byte> bytes = this;
            var length = bytes.IndexOf((byte)0);
            if (length < 0)
            {
                length = bytes.Length;
            }

            return Encoding.UTF8.GetString(bytes[..length]);
        }
    }
}
