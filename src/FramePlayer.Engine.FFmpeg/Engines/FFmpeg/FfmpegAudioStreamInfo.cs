using System.Diagnostics.CodeAnalysis;

namespace FramePlayer.Engines.FFmpeg
{
    internal sealed class FfmpegAudioStreamInfo
    {
        public static FfmpegAudioStreamInfo None { get; } =
            new FfmpegAudioStreamInfo(false, false, -1, string.Empty, 0, 0);

        [SuppressMessage("Major Code Smell", "S107:Methods should not have too many parameters", Justification = "This immutable DTO mirrors FFmpeg stream metadata and keeps call sites explicit.")]
        public FfmpegAudioStreamInfo(
            bool hasAudioStream,
            bool decoderAvailable,
            int streamIndex,
            string codecName,
            int sampleRate,
            int channelCount,
            long? bitRate = null,
            int? bitDepth = null)
        {
            HasAudioStream = hasAudioStream;
            DecoderAvailable = decoderAvailable;
            StreamIndex = streamIndex;
            CodecName = codecName ?? string.Empty;
            SampleRate = sampleRate;
            ChannelCount = channelCount;
            BitRate = bitRate;
            BitDepth = bitDepth;
        }

        public bool HasAudioStream { get; }

        public bool DecoderAvailable { get; }

        public int StreamIndex { get; }

        public string CodecName { get; }

        public int SampleRate { get; }

        public int ChannelCount { get; }

        public long? BitRate { get; }

        public int? BitDepth { get; }
    }
}
