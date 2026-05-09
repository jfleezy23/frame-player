using System;
using System.Runtime.InteropServices;
using FFmpeg.AutoGen;

namespace FramePlayer.Engines.FFmpeg
{
    internal static class RustFfmpegNativeLayout
    {
        private const int AVFormatContextNbStreamsOffset = 44;
        private const int AVFormatContextStreamsOffset = 48;
        private const int AVStreamCodecparOffset = 16;
        private const int AVStreamTimeBaseOffset = 32;
        private const int AVStreamAverageFrameRateOffset = 88;
        private const int AVStreamRealFrameRateOffset = 204;
        private const int AVCodecParametersCodecIdOffset = 4;
        private const int AVCodecContextPacketTimeBaseOffset = 92;
        private const int AVCodecContextFrameRateOffset = 100;
        private const int AVPacketStreamIndexOffset = 36;
        private const int AVFrameDataOffset = 0;
        private const int AVFrameLinesizeOffset = 64;
        private const int AVFrameWidthOffset = 104;
        private const int AVFrameHeightOffset = 108;
        private const int AVFrameFormatOffset = 116;
        private const int AVFramePtsOffset = 136;
        private const int AVFramePacketDtsOffset = 144;
        private const int AVFrameFlagsOffset = 276;
        private const int AVFrameBestEffortTimestampOffset = 304;
        private const int AVFrameDurationOffset = 408;

        public static bool TryValidateFrameConverter(out string errorMessage)
        {
            return TryValidateAVFrameOffsets(out errorMessage);
        }

        public static bool TryValidateDecodeCore(out string errorMessage)
        {
            return TryValidateAVFrameOffsets(out errorMessage) &&
                TryValidateOffset<AVFormatContext>(nameof(AVFormatContext.nb_streams), AVFormatContextNbStreamsOffset, out errorMessage) &&
                TryValidateOffset<AVFormatContext>(nameof(AVFormatContext.streams), AVFormatContextStreamsOffset, out errorMessage) &&
                TryValidateOffset<AVStream>(nameof(AVStream.codecpar), AVStreamCodecparOffset, out errorMessage) &&
                TryValidateOffset<AVStream>(nameof(AVStream.time_base), AVStreamTimeBaseOffset, out errorMessage) &&
                TryValidateOffset<AVStream>(nameof(AVStream.avg_frame_rate), AVStreamAverageFrameRateOffset, out errorMessage) &&
                TryValidateOffset<AVStream>(nameof(AVStream.r_frame_rate), AVStreamRealFrameRateOffset, out errorMessage) &&
                TryValidateOffset<AVCodecParameters>(nameof(AVCodecParameters.codec_id), AVCodecParametersCodecIdOffset, out errorMessage) &&
                TryValidateOffset<AVCodecContext>(nameof(AVCodecContext.pkt_timebase), AVCodecContextPacketTimeBaseOffset, out errorMessage) &&
                TryValidateOffset<AVCodecContext>(nameof(AVCodecContext.framerate), AVCodecContextFrameRateOffset, out errorMessage) &&
                TryValidateOffset<AVPacket>(nameof(AVPacket.stream_index), AVPacketStreamIndexOffset, out errorMessage);
        }

        private static bool TryValidateAVFrameOffsets(out string errorMessage)
        {
            return TryValidateOffset<AVFrame>(nameof(AVFrame.data), AVFrameDataOffset, out errorMessage) &&
                TryValidateOffset<AVFrame>(nameof(AVFrame.linesize), AVFrameLinesizeOffset, out errorMessage) &&
                TryValidateOffset<AVFrame>(nameof(AVFrame.width), AVFrameWidthOffset, out errorMessage) &&
                TryValidateOffset<AVFrame>(nameof(AVFrame.height), AVFrameHeightOffset, out errorMessage) &&
                TryValidateOffset<AVFrame>(nameof(AVFrame.format), AVFrameFormatOffset, out errorMessage) &&
                TryValidateOffset<AVFrame>(nameof(AVFrame.pts), AVFramePtsOffset, out errorMessage) &&
                TryValidateOffset<AVFrame>(nameof(AVFrame.pkt_dts), AVFramePacketDtsOffset, out errorMessage) &&
                TryValidateOffset<AVFrame>(nameof(AVFrame.flags), AVFrameFlagsOffset, out errorMessage) &&
                TryValidateOffset<AVFrame>(nameof(AVFrame.best_effort_timestamp), AVFrameBestEffortTimestampOffset, out errorMessage) &&
                TryValidateOffset<AVFrame>(nameof(AVFrame.duration), AVFrameDurationOffset, out errorMessage);
        }

        private static bool TryValidateOffset<T>(string fieldName, int expectedOffset, out string errorMessage)
        {
            var actualOffset = Marshal.OffsetOf<T>(fieldName).ToInt32();
            if (actualOffset == expectedOffset)
            {
                errorMessage = string.Empty;
                return true;
            }

            errorMessage = string.Format(
                System.Globalization.CultureInfo.InvariantCulture,
                "FFmpeg.AutoGen layout mismatch for {0}.{1}: expected offset {2}, actual offset {3}.",
                typeof(T).Name,
                fieldName,
                expectedOffset,
                actualOffset);
            return false;
        }
    }
}
