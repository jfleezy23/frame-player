using System;
using FFmpeg.AutoGen;
using FramePlayer.Core.Models;
using FramePlayer.Engines.FFmpeg;

namespace FramePlayer.Services
{
    internal static unsafe class MediaProbeService
    {
        public static bool TryProbeVideoMediaInfo(string filePath, out VideoMediaInfo mediaInfo, out string errorMessage)
        {
            mediaInfo = VideoMediaInfo.Empty;
            errorMessage = string.Empty;

            if (string.IsNullOrWhiteSpace(filePath))
            {
                errorMessage = "A media file path is required for probing.";
                return false;
            }

            AVFormatContext* formatContext = null;
            try
            {
                FfmpegRuntimeBootstrap.EnsureConfiguredForCurrentPlatform(AppDomain.CurrentDomain.BaseDirectory);
                FfmpegNativeHelpers.ThrowIfError(
                    FfmpegNativeHelpers.OpenInput(&formatContext, filePath, null, null),
                    "Open media container");
                if (formatContext == null)
                {
                    throw new InvalidOperationException("FFmpeg did not return an opened media container.");
                }

                FfmpegNativeHelpers.ThrowIfError(
                    ffmpeg.avformat_find_stream_info(formatContext, null),
                    "Probe media streams");

                AVCodec* videoDecoder = null;
                var videoStreamIndex = ffmpeg.av_find_best_stream(
                    formatContext,
                    AVMediaType.AVMEDIA_TYPE_VIDEO,
                    -1,
                    -1,
                    &videoDecoder,
                    0);
                FfmpegNativeHelpers.ThrowIfError(videoStreamIndex, "Select primary video stream");

                var videoStream = GetRequiredVideoStream(formatContext, videoStreamIndex);
                mediaInfo = BuildMediaInfo(filePath, formatContext, videoStream, videoStreamIndex);
                return true;
            }
            catch (Exception ex)
            {
                errorMessage = ex.Message;
                mediaInfo = VideoMediaInfo.Empty;
                return false;
            }
            finally
            {
                if (formatContext != null)
                {
                    var formatContextToClose = formatContext;
                    ffmpeg.avformat_close_input(&formatContextToClose);
                }
            }
        }

        private static AVStream* GetRequiredVideoStream(AVFormatContext* formatContext, int videoStreamIndex)
        {
            if (formatContext == null)
            {
                throw new ArgumentNullException(nameof(formatContext));
            }

            if (formatContext->streams == null || (uint)videoStreamIndex >= formatContext->nb_streams)
            {
                throw new InvalidOperationException("FFmpeg returned an invalid primary video stream index.");
            }

            var videoStream = formatContext->streams[videoStreamIndex];
            if (videoStream == null || videoStream->codecpar == null)
            {
                throw new InvalidOperationException("FFmpeg returned an invalid primary video stream.");
            }

            return videoStream;
        }

        private static VideoMediaInfo BuildMediaInfo(
            string filePath,
            AVFormatContext* formatContext,
            AVStream* videoStream,
            int videoStreamIndex)
        {
            if (formatContext == null)
            {
                throw new ArgumentNullException(nameof(formatContext));
            }

            if (videoStream == null)
            {
                throw new ArgumentNullException(nameof(videoStream));
            }

            if (videoStream->codecpar == null)
            {
                throw new InvalidOperationException("FFmpeg returned an invalid primary video stream.");
            }

            var codecParameters = videoStream->codecpar;
            var nominalFrameRate = FfmpegNativeHelpers.GetNominalFrameRate(formatContext, videoStream, null);
            var positionStep = FfmpegNativeHelpers.GetPositionStep(nominalFrameRate);
            var duration = FfmpegNativeHelpers.GetDuration(formatContext, videoStream);
            var pixelWidth = codecParameters->width > 0 ? codecParameters->width : 0;
            var pixelHeight = codecParameters->height > 0 ? codecParameters->height : 0;
            ResolveDisplayDimensions(
                videoStream,
                pixelWidth,
                pixelHeight,
                out var displayWidth,
                out var displayHeight,
                out var displayAspectRatioNumerator,
                out var displayAspectRatioDenominator);

            var audioInfo = ProbePrimaryAudioStream(formatContext, videoStreamIndex);
            var sourcePixelFormat = codecParameters->format >= 0
                ? FfmpegNativeHelpers.GetPixelFormatName((AVPixelFormat)codecParameters->format)
                : null;
            var videoBitDepth = FfmpegNativeHelpers.GetBitDepth(
                null,
                codecParameters,
                codecParameters->format >= 0
                    ? (AVPixelFormat)codecParameters->format
                    : AVPixelFormat.AV_PIX_FMT_NONE);
            var containerBitRate = formatContext->bit_rate > 0
                ? (long?)formatContext->bit_rate
                : null;
            var videoBitRate = codecParameters->bit_rate > 0
                ? (long?)codecParameters->bit_rate
                : containerBitRate;

            return new VideoMediaInfo(
                filePath,
                duration,
                positionStep,
                FfmpegNativeHelpers.ToDouble(nominalFrameRate),
                pixelWidth,
                pixelHeight,
                FfmpegNativeHelpers.GetCodecName(codecParameters->codec_id),
                videoStreamIndex,
                nominalFrameRate.num,
                nominalFrameRate.den,
                videoStream->time_base.num,
                videoStream->time_base.den,
                audioInfo.HasStream,
                audioInfo.HasStream && audioInfo.DecoderAvailable,
                audioInfo.CodecName,
                audioInfo.StreamIndex,
                audioInfo.SampleRate,
                audioInfo.ChannelCount,
                PositiveOrNull(displayWidth),
                PositiveOrNull(displayHeight),
                PositiveOrNull(displayAspectRatioNumerator),
                PositiveOrNull(displayAspectRatioDenominator),
                string.IsNullOrWhiteSpace(sourcePixelFormat) ? null : sourcePixelFormat,
                videoBitDepth,
                videoBitRate,
                null,
                null,
                null,
                null,
                audioInfo.BitRate,
                audioInfo.BitDepth);
        }

        private static int? PositiveOrNull(int value)
        {
            return value > 0 ? (int?)value : null;
        }

        private static ProbedAudioInfo ProbePrimaryAudioStream(
            AVFormatContext* formatContext,
            int videoStreamIndex)
        {
            if (formatContext == null)
            {
                throw new ArgumentNullException(nameof(formatContext));
            }

            AVCodec* audioDecoder = null;
            var bestAudioStreamIndex = ffmpeg.av_find_best_stream(
                formatContext,
                AVMediaType.AVMEDIA_TYPE_AUDIO,
                -1,
                videoStreamIndex,
                &audioDecoder,
                0);
            if (bestAudioStreamIndex < 0)
            {
                return ProbedAudioInfo.None;
            }

            if (formatContext->streams == null || (uint)bestAudioStreamIndex >= formatContext->nb_streams)
            {
                throw new InvalidOperationException("FFmpeg returned an invalid primary audio stream index.");
            }

            var audioStream = formatContext->streams[bestAudioStreamIndex];
            if (audioStream == null || audioStream->codecpar == null)
            {
                throw new InvalidOperationException("FFmpeg returned an invalid primary audio stream.");
            }

            var audioCodecParameters = audioStream->codecpar;
            var codecName = FfmpegNativeHelpers.GetCodecName(audioCodecParameters->codec_id);
            var sampleRate = audioCodecParameters->sample_rate;
            var channelCount = audioCodecParameters->ch_layout.nb_channels;
            var bitRate = audioCodecParameters->bit_rate > 0 ? (long?)audioCodecParameters->bit_rate : null;
            var bitDepth = FfmpegNativeHelpers.GetBitDepth(null, audioCodecParameters, AVPixelFormat.AV_PIX_FMT_NONE);
            var decoderAvailable = audioDecoder != null ||
                ffmpeg.avcodec_find_decoder(audioCodecParameters->codec_id) != null;
            return new ProbedAudioInfo
            {
                HasStream = true,
                DecoderAvailable = decoderAvailable,
                CodecName = codecName,
                StreamIndex = bestAudioStreamIndex,
                SampleRate = sampleRate,
                ChannelCount = channelCount,
                BitRate = bitRate,
                BitDepth = bitDepth
            };
        }

        private readonly struct ProbedAudioInfo
        {
            internal static ProbedAudioInfo None { get; } = new ProbedAudioInfo
            {
                CodecName = string.Empty,
                StreamIndex = -1
            };

            internal bool HasStream { get; init; }

            internal bool DecoderAvailable { get; init; }

            internal string CodecName { get; init; }

            internal int StreamIndex { get; init; }

            internal int SampleRate { get; init; }

            internal int ChannelCount { get; init; }

            internal long? BitRate { get; init; }

            internal int? BitDepth { get; init; }
        }

        private static void ResolveDisplayDimensions(
            AVStream* stream,
            int pixelWidth,
            int pixelHeight,
            out int displayWidth,
            out int displayHeight,
            out int displayAspectRatioNumerator,
            out int displayAspectRatioDenominator)
        {
            displayWidth = Math.Max(0, pixelWidth);
            displayHeight = Math.Max(0, pixelHeight);
            displayAspectRatioNumerator = 0;
            displayAspectRatioDenominator = 0;

            if (stream == null || displayWidth <= 0 || displayHeight <= 0)
            {
                return;
            }

            var sampleAspectRatio = stream->sample_aspect_ratio;
            if (!FfmpegNativeHelpers.IsValid(sampleAspectRatio) &&
                stream->codecpar != null &&
                FfmpegNativeHelpers.IsValid(stream->codecpar->sample_aspect_ratio))
            {
                sampleAspectRatio = stream->codecpar->sample_aspect_ratio;
            }

            if (!FfmpegNativeHelpers.IsValid(sampleAspectRatio) ||
                sampleAspectRatio.num <= 0 ||
                sampleAspectRatio.den <= 0 ||
                sampleAspectRatio.num == sampleAspectRatio.den)
            {
                return;
            }

            if (sampleAspectRatio.num > sampleAspectRatio.den)
            {
                displayWidth = Math.Max(
                    1,
                    (int)Math.Round(
                        displayWidth * sampleAspectRatio.num / (double)sampleAspectRatio.den,
                        MidpointRounding.AwayFromZero));
            }
            else
            {
                displayHeight = Math.Max(
                    1,
                    (int)Math.Round(
                        displayHeight * sampleAspectRatio.den / (double)sampleAspectRatio.num,
                        MidpointRounding.AwayFromZero));
            }

            FfmpegNativeHelpers.TryReduceRatio(
                displayWidth,
                displayHeight,
                out displayAspectRatioNumerator,
                out displayAspectRatioDenominator);
        }
    }
}
