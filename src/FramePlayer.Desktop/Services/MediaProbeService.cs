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
                FfmpegNativeHelpers.ThrowIfError(
                    FfmpegNativeHelpers.OpenInput(&formatContext, filePath, null, null),
                    "Open media container");
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

                var videoStream = formatContext->streams[videoStreamIndex];
                var codecParameters = videoStream->codecpar;
                var nominalFrameRate = FfmpegNativeHelpers.GetNominalFrameRate(formatContext, videoStream, null);
                var positionStep = FfmpegNativeHelpers.GetPositionStep(nominalFrameRate);
                var duration = FfmpegNativeHelpers.GetDuration(formatContext, videoStream);
                var pixelWidth = codecParameters != null && codecParameters->width > 0 ? codecParameters->width : 0;
                var pixelHeight = codecParameters != null && codecParameters->height > 0 ? codecParameters->height : 0;
                ResolveDisplayDimensions(
                    videoStream,
                    pixelWidth,
                    pixelHeight,
                    out var displayWidth,
                    out var displayHeight,
                    out var displayAspectRatioNumerator,
                    out var displayAspectRatioDenominator);

                var hasAudioStream = false;
                var audioCodecName = string.Empty;
                var audioStreamIndex = -1;
                var audioSampleRate = 0;
                var audioChannelCount = 0;
                long? audioBitRate = null;
                int? audioBitDepth = null;
                var audioDecoderAvailable = false;

                AVCodec* audioDecoder = null;
                var bestAudioStreamIndex = ffmpeg.av_find_best_stream(
                    formatContext,
                    AVMediaType.AVMEDIA_TYPE_AUDIO,
                    -1,
                    videoStreamIndex,
                    &audioDecoder,
                    0);
                if (bestAudioStreamIndex >= 0)
                {
                    hasAudioStream = true;
                    audioStreamIndex = bestAudioStreamIndex;
                    var audioStream = formatContext->streams[bestAudioStreamIndex];
                    var audioCodecParameters = audioStream->codecpar;
                    audioCodecName = FfmpegNativeHelpers.GetCodecName(audioCodecParameters->codec_id);
                    audioSampleRate = audioCodecParameters->sample_rate;
                    audioChannelCount = audioCodecParameters->ch_layout.nb_channels;
                    audioBitRate = audioCodecParameters->bit_rate > 0 ? (long?)audioCodecParameters->bit_rate : null;
                    audioBitDepth = FfmpegNativeHelpers.GetBitDepth(null, audioCodecParameters, AVPixelFormat.AV_PIX_FMT_NONE);
                    audioDecoderAvailable = audioDecoder != null ||
                        ffmpeg.avcodec_find_decoder(audioCodecParameters->codec_id) != null;
                }

                var sourcePixelFormat = codecParameters != null && codecParameters->format >= 0
                    ? FfmpegNativeHelpers.GetPixelFormatName((AVPixelFormat)codecParameters->format)
                    : null;
                var videoBitDepth = FfmpegNativeHelpers.GetBitDepth(
                    null,
                    codecParameters,
                    codecParameters != null && codecParameters->format >= 0
                        ? (AVPixelFormat)codecParameters->format
                        : AVPixelFormat.AV_PIX_FMT_NONE);
                var videoBitRate = codecParameters != null && codecParameters->bit_rate > 0
                    ? (long?)codecParameters->bit_rate
                    : formatContext->bit_rate > 0
                        ? (long?)formatContext->bit_rate
                        : null;

                mediaInfo = new VideoMediaInfo(
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
                    hasAudioStream,
                    hasAudioStream && audioDecoderAvailable,
                    audioCodecName,
                    audioStreamIndex,
                    audioSampleRate,
                    audioChannelCount,
                    displayWidth > 0 ? (int?)displayWidth : null,
                    displayHeight > 0 ? (int?)displayHeight : null,
                    displayAspectRatioNumerator > 0 ? (int?)displayAspectRatioNumerator : null,
                    displayAspectRatioDenominator > 0 ? (int?)displayAspectRatioDenominator : null,
                    string.IsNullOrWhiteSpace(sourcePixelFormat) ? null : sourcePixelFormat,
                    videoBitDepth,
                    videoBitRate,
                    null,
                    null,
                    null,
                    null,
                    audioBitRate,
                    audioBitDepth);
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
