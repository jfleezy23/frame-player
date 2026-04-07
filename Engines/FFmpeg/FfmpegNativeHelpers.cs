using System;
using System.Globalization;
using System.Runtime.InteropServices;
using FFmpeg.AutoGen;

namespace FramePlayer.Engines.FFmpeg
{
    internal static unsafe class FfmpegNativeHelpers
    {
        private static readonly AVRational AvTimeBaseRational = new AVRational
        {
            num = 1,
            den = ffmpeg.AV_TIME_BASE
        };

        internal static void ThrowIfError(int errorCode, string operation)
        {
            if (errorCode >= 0)
            {
                return;
            }

            throw new InvalidOperationException(string.Format(
                CultureInfo.InvariantCulture,
                "{0} failed: {1}",
                operation,
                GetErrorMessage(errorCode)));
        }

        internal static string GetErrorMessage(int errorCode)
        {
            const int BufferSize = 1024;
            var buffer = stackalloc byte[BufferSize];
            ffmpeg.av_strerror(errorCode, buffer, (ulong)BufferSize);
            return PtrToString(buffer);
        }

        internal static string PtrToString(byte* value)
        {
            return value == null
                ? string.Empty
                : Marshal.PtrToStringAnsi((IntPtr)value) ?? string.Empty;
        }

        internal static string GetCodecName(AVCodecID codecId)
        {
            var name = ffmpeg.avcodec_get_name(codecId);
            return string.IsNullOrWhiteSpace(name)
                ? codecId.ToString()
                : name;
        }

        internal static string GetPixelFormatName(AVPixelFormat pixelFormat)
        {
            var name = ffmpeg.av_get_pix_fmt_name(pixelFormat);
            return string.IsNullOrWhiteSpace(name)
                ? pixelFormat.ToString()
                : name;
        }

        internal static bool IsValid(AVRational rational)
        {
            return rational.num != 0 && rational.den != 0;
        }

        internal static double ToDouble(AVRational rational)
        {
            return IsValid(rational)
                ? rational.num / (double)rational.den
                : 0d;
        }

        internal static TimeSpan ToTimeSpan(long timestamp, AVRational timeBase)
        {
            if (!IsValid(timeBase) || timestamp == ffmpeg.AV_NOPTS_VALUE)
            {
                return TimeSpan.Zero;
            }

            var seconds = timestamp * timeBase.num / (double)timeBase.den;
            if (double.IsNaN(seconds) || double.IsInfinity(seconds))
            {
                return TimeSpan.Zero;
            }

            return TimeSpan.FromTicks(checked((long)Math.Round(
                seconds * TimeSpan.TicksPerSecond,
                MidpointRounding.AwayFromZero)));
        }

        internal static TimeSpan ToTimeSpanFromAvTime(long timestamp)
        {
            if (timestamp == ffmpeg.AV_NOPTS_VALUE || timestamp <= 0)
            {
                return TimeSpan.Zero;
            }

            var seconds = timestamp / (double)ffmpeg.AV_TIME_BASE;
            return TimeSpan.FromTicks(checked((long)Math.Round(
                seconds * TimeSpan.TicksPerSecond,
                MidpointRounding.AwayFromZero)));
        }

        internal static TimeSpan GetDuration(AVFormatContext* formatContext, AVStream* stream)
        {
            if (stream != null && stream->duration > 0 && IsValid(stream->time_base))
            {
                return ToTimeSpan(stream->duration, stream->time_base);
            }

            if (formatContext != null && formatContext->duration > 0)
            {
                return ToTimeSpanFromAvTime(formatContext->duration);
            }

            return TimeSpan.Zero;
        }

        internal static AVRational GetNominalFrameRate(AVFormatContext* formatContext, AVStream* stream, AVFrame* frame)
        {
            var guessed = ffmpeg.av_guess_frame_rate(formatContext, stream, frame);
            if (IsValid(guessed))
            {
                return guessed;
            }

            if (stream != null)
            {
                if (IsValid(stream->avg_frame_rate))
                {
                    return stream->avg_frame_rate;
                }

                if (IsValid(stream->r_frame_rate))
                {
                    return stream->r_frame_rate;
                }
            }

            return default(AVRational);
        }

        internal static TimeSpan GetPositionStep(AVRational frameRate)
        {
            var framesPerSecond = ToDouble(frameRate);
            if (framesPerSecond <= 0d)
            {
                return TimeSpan.Zero;
            }

            return TimeSpan.FromTicks(checked((long)Math.Round(
                TimeSpan.TicksPerSecond / framesPerSecond,
                MidpointRounding.AwayFromZero)));
        }

        internal static long ToStreamTimestamp(TimeSpan position, AVRational timeBase)
        {
            if (!IsValid(timeBase))
            {
                return 0L;
            }

            var positionInAvTimeBase = checked((long)Math.Round(
                position.TotalSeconds * ffmpeg.AV_TIME_BASE,
                MidpointRounding.AwayFromZero));
            return ffmpeg.av_rescale_q(positionInAvTimeBase, AvTimeBaseRational, timeBase);
        }

        internal static long? GetBestPresentationTimestamp(AVFrame* decodedFrame)
        {
            if (decodedFrame == null)
            {
                return null;
            }

            var bestEffortTimestamp = AsNullableTimestamp(decodedFrame->best_effort_timestamp);
            if (bestEffortTimestamp.HasValue)
            {
                return bestEffortTimestamp;
            }

            var presentationTimestamp = AsNullableTimestamp(decodedFrame->pts);
            if (presentationTimestamp.HasValue)
            {
                return presentationTimestamp;
            }

            return AsNullableTimestamp(decodedFrame->pkt_dts);
        }

        internal static long? GetBestAvailableTimestamp(long? presentationTimestamp, long? decodeTimestamp)
        {
            return presentationTimestamp ?? decodeTimestamp;
        }

        internal static long? AsNullableTimestamp(long timestamp)
        {
            return timestamp == ffmpeg.AV_NOPTS_VALUE
                ? (long?)null
                : timestamp;
        }
    }
}
