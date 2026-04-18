using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using FFmpeg.AutoGen;
using FramePlayer.Engines.FFmpeg;

namespace FramePlayer.Services
{
    internal static unsafe class NativeExportSupport
    {
        private const int AacBitRate = 192000;
        private const int AudioFrameSize = 1024;
        private const string VideoSinkName = "outv";
        private const string AudioSinkName = "outa";

        internal static GraphExportOutcome RunGraphExport(
            string outputFilePath,
            string filterGraph,
            int outputWidth,
            int outputHeight,
            bool includeAudio,
            System.Threading.CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(outputFilePath))
            {
                throw new ArgumentException("An output file path is required.", nameof(outputFilePath));
            }

            if (string.IsNullOrWhiteSpace(filterGraph))
            {
                throw new ArgumentException("A filter graph is required.", nameof(filterGraph));
            }

            cancellationToken.ThrowIfCancellationRequested();

            var stopwatch = Stopwatch.StartNew();

            AVFilterGraph* filterGraphContext = null;
            AVFilterContext* videoSinkContext = null;
            AVFilterContext* audioSinkContext = null;
            AVFormatContext* outputFormatContext = null;
            AVCodecContext* videoEncoderContext = null;
            AVCodecContext* audioEncoderContext = null;
            AVFrame* nextVideoFrame = null;
            AVFrame* nextAudioFrame = null;

            try
            {
                filterGraphContext = ffmpeg.avfilter_graph_alloc();
                if (filterGraphContext == null)
                {
                    throw new InvalidOperationException("Could not allocate the FFmpeg export filter graph.");
                }

                AVFilterInOut* inputs = null;
                AVFilterInOut* outputs = null;
                try
                {
                    FfmpegNativeHelpers.ThrowIfError(
                        ffmpeg.avfilter_graph_parse_ptr(filterGraphContext, filterGraph, &inputs, &outputs, null),
                        "Parse export filter graph");
                    FfmpegNativeHelpers.ThrowIfError(
                        ffmpeg.avfilter_graph_config(filterGraphContext, null),
                        "Configure export filter graph");
                }
                finally
                {
                    if (inputs != null)
                    {
                        ffmpeg.avfilter_inout_free(&inputs);
                    }

                    if (outputs != null)
                    {
                        ffmpeg.avfilter_inout_free(&outputs);
                    }
                }

                videoSinkContext = ResolveFilterContext(filterGraphContext, VideoSinkName, "buffersink");
                if (videoSinkContext == null)
                {
                    throw new InvalidOperationException("The export filter graph did not expose the expected video sink.");
                }

                if (includeAudio)
                {
                    audioSinkContext = ResolveFilterContext(filterGraphContext, AudioSinkName, "abuffersink");
                }

                var videoSinkTimeBase = ffmpeg.av_buffersink_get_time_base(videoSinkContext);
                var videoSinkFrameRate = ffmpeg.av_buffersink_get_frame_rate(videoSinkContext);
                nextAudioFrame = ReadNextFilterFrame(audioSinkContext);

                outputFormatContext = CreateMp4OutputContext(outputFilePath);
                var outputVideoStream = AddVideoEncoderStream(
                    outputFormatContext,
                    outputWidth,
                    outputHeight,
                    videoSinkTimeBase,
                    videoSinkFrameRate,
                    out videoEncoderContext);
                AVStream* outputAudioStream = null;
                AVRational audioSinkTimeBase = default;
                if (nextAudioFrame != null)
                {
                    audioSinkTimeBase = audioSinkContext != null
                        ? ffmpeg.av_buffersink_get_time_base(audioSinkContext)
                        : default;
                    outputAudioStream = AddAudioEncoderStream(
                        outputFormatContext,
                        nextAudioFrame,
                        out audioEncoderContext);
                }

                OpenOutputIo(outputFormatContext, outputFilePath);
                WriteOutputHeader(outputFormatContext);

                nextVideoFrame = ReadNextFilterFrame(videoSinkContext);
                while (nextVideoFrame != null || nextAudioFrame != null)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    var writeVideoFrame = ShouldProcessVideoFrameFirst(
                        nextVideoFrame,
                        videoSinkTimeBase,
                        nextAudioFrame,
                        audioSinkTimeBase);
                    if (writeVideoFrame && nextVideoFrame != null)
                    {
                        WriteVideoFrame(
                            outputFormatContext,
                            outputVideoStream,
                            videoEncoderContext,
                            nextVideoFrame,
                            videoSinkTimeBase);
                        FreeFrame(ref nextVideoFrame);
                        nextVideoFrame = ReadNextFilterFrame(videoSinkContext);
                        continue;
                    }

                    if (nextAudioFrame != null && outputAudioStream != null && audioEncoderContext != null)
                    {
                        WriteAudioFrame(
                            outputFormatContext,
                            outputAudioStream,
                            audioEncoderContext,
                            nextAudioFrame,
                            audioSinkTimeBase);
                        FreeFrame(ref nextAudioFrame);
                        nextAudioFrame = ReadNextFilterFrame(audioSinkContext);
                        continue;
                    }

                    break;
                }

                FlushEncoder(outputFormatContext, outputVideoStream, videoEncoderContext, "Flush export video encoder");
                if (outputAudioStream != null && audioEncoderContext != null)
                {
                    FlushEncoder(outputFormatContext, outputAudioStream, audioEncoderContext, "Flush export audio encoder");
                }

                FfmpegNativeHelpers.ThrowIfError(
                    ffmpeg.av_write_trailer(outputFormatContext),
                    "Finalize export output");

                stopwatch.Stop();
                ProbeOutput(outputFilePath, out var probedDuration, out var probedVideoWidth, out var probedVideoHeight, out var probedHasAudioStream);
                return new GraphExportOutcome(
                    true,
                    string.Empty,
                    0,
                    stopwatch.Elapsed,
                    probedDuration,
                    probedVideoWidth,
                    probedVideoHeight,
                    probedHasAudioStream,
                    string.Empty,
                    string.Empty);
            }
            catch (Exception ex)
            {
                stopwatch.Stop();
                return new GraphExportOutcome(
                    false,
                    ex.Message,
                    -1,
                    stopwatch.Elapsed,
                    null,
                    null,
                    null,
                    null,
                    string.Empty,
                    ex.ToString());
            }
            finally
            {
                FreeFrame(ref nextVideoFrame);
                FreeFrame(ref nextAudioFrame);
                FreeCodecContext(ref audioEncoderContext);
                FreeCodecContext(ref videoEncoderContext);
                CloseOutputContext(ref outputFormatContext);
                FreeFilterGraph(ref filterGraphContext);
            }
        }

        internal static AVFormatContext* CreateMp4OutputContext(string outputFilePath)
        {
            AVFormatContext* outputFormatContext = null;
            FfmpegNativeHelpers.ThrowIfError(
                ffmpeg.avformat_alloc_output_context2(&outputFormatContext, null, null, outputFilePath),
                "Create output container");
            if (outputFormatContext == null)
            {
                throw new InvalidOperationException("Could not allocate the FFmpeg output container.");
            }

            return outputFormatContext;
        }

        internal static void OpenOutputIo(AVFormatContext* outputFormatContext, string outputFilePath)
        {
            if (outputFormatContext == null)
            {
                throw new ArgumentNullException(nameof(outputFormatContext));
            }

            if ((outputFormatContext->oformat->flags & ffmpeg.AVFMT_NOFILE) != 0)
            {
                return;
            }

            FfmpegNativeHelpers.ThrowIfError(
                ffmpeg.avio_open(&outputFormatContext->pb, outputFilePath, ffmpeg.AVIO_FLAG_WRITE),
                "Open export output file");
        }

        internal static void WriteOutputHeader(AVFormatContext* outputFormatContext)
        {
            AVDictionary* options = null;
            try
            {
                ffmpeg.av_dict_set(&options, "movflags", "+faststart", 0);
                FfmpegNativeHelpers.ThrowIfError(
                    ffmpeg.avformat_write_header(outputFormatContext, &options),
                    "Write export output header");
            }
            finally
            {
                if (options != null)
                {
                    ffmpeg.av_dict_free(&options);
                }
            }
        }

        internal static AVStream* AddVideoEncoderStream(
            AVFormatContext* outputFormatContext,
            int outputWidth,
            int outputHeight,
            AVRational sinkTimeBase,
            AVRational sinkFrameRate,
            out AVCodecContext* encoderContext)
        {
            var encoder = ffmpeg.avcodec_find_encoder_by_name("libx264");
            if (encoder == null)
            {
                throw new InvalidOperationException("The bundled FFmpeg export runtime does not provide libx264.");
            }

            encoderContext = ffmpeg.avcodec_alloc_context3(encoder);
            if (encoderContext == null)
            {
                throw new InvalidOperationException("Could not allocate the FFmpeg video encoder context.");
            }

            var resolvedTimeBase = FfmpegNativeHelpers.IsValid(sinkTimeBase)
                ? sinkTimeBase
                : FfmpegNativeHelpers.IsValid(sinkFrameRate)
                    ? ffmpeg.av_inv_q(sinkFrameRate)
                    : new AVRational { num = 1, den = 30 };
            var resolvedFrameRate = FfmpegNativeHelpers.IsValid(sinkFrameRate)
                ? sinkFrameRate
                : FfmpegNativeHelpers.IsValid(resolvedTimeBase)
                    ? ffmpeg.av_inv_q(resolvedTimeBase)
                    : new AVRational { num = 30, den = 1 };

            encoderContext->codec_id = encoder->id;
            encoderContext->codec_type = AVMediaType.AVMEDIA_TYPE_VIDEO;
            encoderContext->width = Math.Max(2, outputWidth);
            encoderContext->height = Math.Max(2, outputHeight);
            encoderContext->pix_fmt = AVPixelFormat.AV_PIX_FMT_YUV420P;
            encoderContext->sample_aspect_ratio = new AVRational { num = 1, den = 1 };
            encoderContext->time_base = resolvedTimeBase;
            encoderContext->framerate = resolvedFrameRate;

            if ((outputFormatContext->oformat->flags & ffmpeg.AVFMT_GLOBALHEADER) != 0)
            {
                encoderContext->flags |= ffmpeg.AV_CODEC_FLAG_GLOBAL_HEADER;
            }

            AVDictionary* options = null;
            try
            {
                ffmpeg.av_dict_set(&options, "preset", "medium", 0);
                ffmpeg.av_dict_set(&options, "crf", "18", 0);
                FfmpegNativeHelpers.ThrowIfError(
                    ffmpeg.avcodec_open2(encoderContext, encoder, &options),
                    "Open export video encoder");
            }
            finally
            {
                if (options != null)
                {
                    ffmpeg.av_dict_free(&options);
                }
            }

            var outputStream = ffmpeg.avformat_new_stream(outputFormatContext, null);
            if (outputStream == null)
            {
                throw new InvalidOperationException("Could not allocate the output video stream.");
            }

            outputStream->time_base = encoderContext->time_base;
            FfmpegNativeHelpers.ThrowIfError(
                ffmpeg.avcodec_parameters_from_context(outputStream->codecpar, encoderContext),
                "Copy export video stream parameters");
            outputStream->codecpar->codec_tag = 0;
            return outputStream;
        }

        internal static AVStream* AddAudioEncoderStream(
            AVFormatContext* outputFormatContext,
            AVFrame* templateFrame,
            out AVCodecContext* encoderContext)
        {
            var encoder = ffmpeg.avcodec_find_encoder(AVCodecID.AV_CODEC_ID_AAC);
            if (encoder == null)
            {
                throw new InvalidOperationException("The bundled FFmpeg export runtime does not provide the AAC encoder.");
            }

            encoderContext = ffmpeg.avcodec_alloc_context3(encoder);
            if (encoderContext == null)
            {
                throw new InvalidOperationException("Could not allocate the FFmpeg audio encoder context.");
            }

            var sampleFormat = ResolveSupportedSampleFormat(encoder, (AVSampleFormat)templateFrame->format);
            var sampleRate = ResolveSupportedSampleRate(encoder, templateFrame->sample_rate);

            AVChannelLayout sourceLayout = default;
            var ownsSourceLayout = false;
            try
            {
                sourceLayout = templateFrame->ch_layout;
                if (ffmpeg.av_channel_layout_check(&sourceLayout) <= 0 || sourceLayout.nb_channels <= 0)
                {
                    ffmpeg.av_channel_layout_default(&sourceLayout, 2);
                    ownsSourceLayout = true;
                }

                encoderContext->codec_id = encoder->id;
                encoderContext->codec_type = AVMediaType.AVMEDIA_TYPE_AUDIO;
                encoderContext->sample_fmt = sampleFormat;
                encoderContext->sample_rate = sampleRate;
                encoderContext->time_base = new AVRational { num = 1, den = sampleRate };
                encoderContext->bit_rate = AacBitRate;
                FfmpegNativeHelpers.ThrowIfError(
                    ffmpeg.av_channel_layout_copy(&encoderContext->ch_layout, &sourceLayout),
                    "Copy export audio channel layout");

                if ((outputFormatContext->oformat->flags & ffmpeg.AVFMT_GLOBALHEADER) != 0)
                {
                    encoderContext->flags |= ffmpeg.AV_CODEC_FLAG_GLOBAL_HEADER;
                }

                FfmpegNativeHelpers.ThrowIfError(
                    ffmpeg.avcodec_open2(encoderContext, encoder, null),
                    "Open export audio encoder");
            }
            finally
            {
                if (ownsSourceLayout)
                {
                    ffmpeg.av_channel_layout_uninit(&sourceLayout);
                }
            }

            var outputStream = ffmpeg.avformat_new_stream(outputFormatContext, null);
            if (outputStream == null)
            {
                throw new InvalidOperationException("Could not allocate the output audio stream.");
            }

            outputStream->time_base = encoderContext->time_base;
            FfmpegNativeHelpers.ThrowIfError(
                ffmpeg.avcodec_parameters_from_context(outputStream->codecpar, encoderContext),
                "Copy export audio stream parameters");
            outputStream->codecpar->codec_tag = 0;
            return outputStream;
        }

        internal static AVStream* AddCopiedStream(
            AVFormatContext* outputFormatContext,
            AVStream* sourceStream)
        {
            if (outputFormatContext == null)
            {
                throw new ArgumentNullException(nameof(outputFormatContext));
            }

            if (sourceStream == null)
            {
                throw new ArgumentNullException(nameof(sourceStream));
            }

            var outputStream = ffmpeg.avformat_new_stream(outputFormatContext, null);
            if (outputStream == null)
            {
                throw new InvalidOperationException("Could not allocate the copied output stream.");
            }

            FfmpegNativeHelpers.ThrowIfError(
                ffmpeg.avcodec_parameters_copy(outputStream->codecpar, sourceStream->codecpar),
                "Copy output stream parameters");
            outputStream->codecpar->codec_tag = 0;
            outputStream->time_base = sourceStream->time_base;
            outputStream->avg_frame_rate = sourceStream->avg_frame_rate;
            outputStream->r_frame_rate = sourceStream->r_frame_rate;
            return outputStream;
        }

        internal static void WriteVideoFrame(
            AVFormatContext* outputFormatContext,
            AVStream* outputStream,
            AVCodecContext* encoderContext,
            AVFrame* frame,
            AVRational sinkTimeBase)
        {
            if (frame == null)
            {
                return;
            }

            frame->pts = frame->pts == ffmpeg.AV_NOPTS_VALUE
                ? ffmpeg.AV_NOPTS_VALUE
                : ffmpeg.av_rescale_q(frame->pts, sinkTimeBase, encoderContext->time_base);
            SendFrameAndWritePackets(outputFormatContext, outputStream, encoderContext, frame, "Encode export video frame");
        }

        internal static void WriteAudioFrame(
            AVFormatContext* outputFormatContext,
            AVStream* outputStream,
            AVCodecContext* encoderContext,
            AVFrame* frame,
            AVRational sinkTimeBase)
        {
            if (frame == null)
            {
                return;
            }

            frame->pts = frame->pts == ffmpeg.AV_NOPTS_VALUE
                ? ffmpeg.AV_NOPTS_VALUE
                : ffmpeg.av_rescale_q(frame->pts, sinkTimeBase, encoderContext->time_base);
            SendFrameAndWritePackets(outputFormatContext, outputStream, encoderContext, frame, "Encode export audio frame");
        }

        internal static void FlushEncoder(
            AVFormatContext* outputFormatContext,
            AVStream* outputStream,
            AVCodecContext* encoderContext,
            string operation)
        {
            if (outputStream == null || encoderContext == null)
            {
                return;
            }

            SendFrameAndWritePackets(outputFormatContext, outputStream, encoderContext, null, operation);
        }

        internal static AVFrame* ReadNextFilterFrame(AVFilterContext* sinkContext)
        {
            if (sinkContext == null)
            {
                return null;
            }

            var frame = ffmpeg.av_frame_alloc();
            if (frame == null)
            {
                throw new InvalidOperationException("Could not allocate the FFmpeg export frame.");
            }

            var result = ffmpeg.av_buffersink_get_frame(sinkContext, frame);
            if (result == ffmpeg.AVERROR_EOF || result == ffmpeg.AVERROR(ffmpeg.EAGAIN))
            {
                FreeFrame(ref frame);
                return null;
            }

            FfmpegNativeHelpers.ThrowIfError(result, "Pull filtered export frame");
            return frame;
        }

        internal static string EscapeFilterFilePath(string filePath)
        {
            var normalizedPath = Path.GetFullPath(filePath ?? string.Empty)
                .Replace("\\", "/", StringComparison.Ordinal)
                .Replace(":", "\\:", StringComparison.Ordinal)
                .Replace("'", "\\'", StringComparison.Ordinal);
            return normalizedPath;
        }

        internal static string FormatFilterTime(TimeSpan value)
        {
            return value.TotalSeconds.ToString("0.######", CultureInfo.InvariantCulture);
        }

        internal static void ProbeOutput(
            string outputFilePath,
            out TimeSpan? probedDuration,
            out int? probedVideoWidth,
            out int? probedVideoHeight,
            out bool? probedHasAudioStream)
        {
            probedDuration = null;
            probedVideoWidth = null;
            probedVideoHeight = null;
            probedHasAudioStream = null;

            if (!MediaProbeService.TryProbeVideoMediaInfo(outputFilePath, out var mediaInfo, out _))
            {
                return;
            }

            probedDuration = mediaInfo.Duration > TimeSpan.Zero ? mediaInfo.Duration : null;
            probedVideoWidth = mediaInfo.PixelWidth > 0 ? (int?)mediaInfo.PixelWidth : null;
            probedVideoHeight = mediaInfo.PixelHeight > 0 ? (int?)mediaInfo.PixelHeight : null;
            probedHasAudioStream = mediaInfo.HasAudioStream;
        }

        internal static void FreeFrame(ref AVFrame* frame)
        {
            if (frame == null)
            {
                return;
            }

            var frameToFree = frame;
            ffmpeg.av_frame_free(&frameToFree);
            frame = null;
        }

        internal static void FreePacket(ref AVPacket* packet)
        {
            if (packet == null)
            {
                return;
            }

            var packetToFree = packet;
            ffmpeg.av_packet_free(&packetToFree);
            packet = null;
        }

        internal static void FreeCodecContext(ref AVCodecContext* codecContext)
        {
            if (codecContext == null)
            {
                return;
            }

            var codecContextToFree = codecContext;
            ffmpeg.avcodec_free_context(&codecContextToFree);
            codecContext = null;
        }

        internal static void CloseOutputContext(ref AVFormatContext* outputFormatContext)
        {
            if (outputFormatContext == null)
            {
                return;
            }

            if ((outputFormatContext->oformat->flags & ffmpeg.AVFMT_NOFILE) == 0 && outputFormatContext->pb != null)
            {
                ffmpeg.avio_closep(&outputFormatContext->pb);
            }

            var outputFormatContextToFree = outputFormatContext;
            ffmpeg.avformat_free_context(outputFormatContextToFree);
            outputFormatContext = null;
        }

        internal static void FreeFilterGraph(ref AVFilterGraph* filterGraph)
        {
            if (filterGraph == null)
            {
                return;
            }

            var filterGraphToFree = filterGraph;
            ffmpeg.avfilter_graph_free(&filterGraphToFree);
            filterGraph = null;
        }

        internal static bool ShouldProcessVideoFrameFirst(
            AVFrame* videoFrame,
            AVRational videoTimeBase,
            AVFrame* audioFrame,
            AVRational audioTimeBase)
        {
            if (videoFrame == null)
            {
                return false;
            }

            if (audioFrame == null)
            {
                return true;
            }

            var videoPts = videoFrame->pts == ffmpeg.AV_NOPTS_VALUE ? 0L : videoFrame->pts;
            var audioPts = audioFrame->pts == ffmpeg.AV_NOPTS_VALUE ? 0L : audioFrame->pts;
            return ffmpeg.av_compare_ts(videoPts, videoTimeBase, audioPts, audioTimeBase) <= 0;
        }

        internal static bool ShouldProcessVideoPacketFirst(
            AVPacket* videoPacket,
            AVRational videoTimeBase,
            AVFrame* audioFrame,
            AVRational audioTimeBase)
        {
            if (videoPacket == null)
            {
                return false;
            }

            if (audioFrame == null)
            {
                return true;
            }

            var videoTimestamp = videoPacket->dts != ffmpeg.AV_NOPTS_VALUE
                ? videoPacket->dts
                : videoPacket->pts != ffmpeg.AV_NOPTS_VALUE
                    ? videoPacket->pts
                    : 0L;
            var audioTimestamp = audioFrame->pts == ffmpeg.AV_NOPTS_VALUE ? 0L : audioFrame->pts;
            return ffmpeg.av_compare_ts(videoTimestamp, videoTimeBase, audioTimestamp, audioTimeBase) <= 0;
        }

        internal static AVFilterContext* ResolveFilterContext(
            AVFilterGraph* filterGraph,
            string preferredInstanceName,
            string filterName)
        {
            if (filterGraph == null)
            {
                return null;
            }

            if (!string.IsNullOrWhiteSpace(preferredInstanceName))
            {
                var namedFilter = ffmpeg.avfilter_graph_get_filter(filterGraph, preferredInstanceName);
                if (namedFilter != null)
                {
                    return namedFilter;
                }
            }

            AVFilterContext* matchedFilter = null;
            for (uint index = 0; index < filterGraph->nb_filters; index++)
            {
                var candidate = filterGraph->filters[index];
                if (candidate == null || candidate->filter == null || candidate->filter->name == null)
                {
                    continue;
                }

                var candidateFilterName = Marshal.PtrToStringAnsi((IntPtr)candidate->filter->name) ?? string.Empty;
                if (!string.Equals(candidateFilterName, filterName, StringComparison.Ordinal))
                {
                    continue;
                }

                if (matchedFilter != null)
                {
                    throw new InvalidOperationException(
                        "The export filter graph exposed multiple '" + filterName + "' filter nodes.");
                }

                matchedFilter = candidate;
            }

            return matchedFilter;
        }

        internal static bool TryReadNextPacketForStream(
            AVFormatContext* inputFormatContext,
            int streamIndex,
            AVPacket* packet)
        {
            if (inputFormatContext == null || packet == null)
            {
                return false;
            }

            while (true)
            {
                var readResult = ffmpeg.av_read_frame(inputFormatContext, packet);
                if (readResult == ffmpeg.AVERROR_EOF)
                {
                    return false;
                }

                FfmpegNativeHelpers.ThrowIfError(readResult, "Read export input packet");
                if (packet->stream_index == streamIndex)
                {
                    return true;
                }

                ffmpeg.av_packet_unref(packet);
            }
        }

        internal static void WriteCopiedPacket(
            AVFormatContext* outputFormatContext,
            AVStream* inputStream,
            AVStream* outputStream,
            AVPacket* packet)
        {
            if (outputFormatContext == null || inputStream == null || outputStream == null || packet == null)
            {
                throw new ArgumentNullException("Copied stream writing requires valid FFmpeg stream state.");
            }

            ffmpeg.av_packet_rescale_ts(packet, inputStream->time_base, outputStream->time_base);
            packet->stream_index = outputStream->index;
            packet->pos = -1;
            FfmpegNativeHelpers.ThrowIfError(
                ffmpeg.av_interleaved_write_frame(outputFormatContext, packet),
                "Write copied export packet");
        }

        private static void SendFrameAndWritePackets(
            AVFormatContext* outputFormatContext,
            AVStream* outputStream,
            AVCodecContext* encoderContext,
            AVFrame* frame,
            string operation)
        {
            while (true)
            {
                var sendResult = ffmpeg.avcodec_send_frame(encoderContext, frame);
                if (sendResult == ffmpeg.AVERROR(ffmpeg.EAGAIN))
                {
                    DrainEncoderPackets(outputFormatContext, outputStream, encoderContext, operation);
                    continue;
                }

                if (sendResult == ffmpeg.AVERROR_EOF)
                {
                    DrainEncoderPackets(outputFormatContext, outputStream, encoderContext, operation);
                    return;
                }

                FfmpegNativeHelpers.ThrowIfError(sendResult, operation);
                break;
            }

            DrainEncoderPackets(outputFormatContext, outputStream, encoderContext, operation);
        }

        private static void DrainEncoderPackets(
            AVFormatContext* outputFormatContext,
            AVStream* outputStream,
            AVCodecContext* encoderContext,
            string operation)
        {
            AVPacket* packet = ffmpeg.av_packet_alloc();
            if (packet == null)
            {
                throw new InvalidOperationException("Could not allocate the FFmpeg export packet.");
            }

            try
            {
                while (true)
                {
                    var receiveResult = ffmpeg.avcodec_receive_packet(encoderContext, packet);
                    if (receiveResult == ffmpeg.AVERROR_EOF || receiveResult == ffmpeg.AVERROR(ffmpeg.EAGAIN))
                    {
                        return;
                    }

                    FfmpegNativeHelpers.ThrowIfError(receiveResult, operation);

                    ffmpeg.av_packet_rescale_ts(packet, encoderContext->time_base, outputStream->time_base);
                    packet->stream_index = outputStream->index;
                    FfmpegNativeHelpers.ThrowIfError(
                        ffmpeg.av_interleaved_write_frame(outputFormatContext, packet),
                        "Write export packet");
                    ffmpeg.av_packet_unref(packet);
                }
            }
            finally
            {
                FreePacket(ref packet);
            }
        }

        #pragma warning disable CS0618
        private static AVSampleFormat ResolveSupportedSampleFormat(AVCodec* encoder, AVSampleFormat requestedSampleFormat)
        {
            if (encoder == null || encoder->sample_fmts == null)
            {
                return requestedSampleFormat;
            }

            for (var sampleFormat = encoder->sample_fmts; *sampleFormat != AVSampleFormat.AV_SAMPLE_FMT_NONE; sampleFormat++)
            {
                if (*sampleFormat == requestedSampleFormat)
                {
                    return *sampleFormat;
                }
            }

            return encoder->sample_fmts[0];
        }

        private static int ResolveSupportedSampleRate(AVCodec* encoder, int requestedSampleRate)
        {
            if (requestedSampleRate <= 0 || encoder == null || encoder->supported_samplerates == null)
            {
                return requestedSampleRate > 0 ? requestedSampleRate : 48000;
            }

            for (var sampleRate = encoder->supported_samplerates; *sampleRate != 0; sampleRate++)
            {
                if (*sampleRate == requestedSampleRate)
                {
                    return *sampleRate;
                }
            }

            return encoder->supported_samplerates[0] > 0
                ? encoder->supported_samplerates[0]
                : requestedSampleRate;
        }
        #pragma warning restore CS0618

        internal readonly struct GraphExportOutcome
        {
            public GraphExportOutcome(
                bool succeeded,
                string message,
                int exitCode,
                TimeSpan elapsed,
                TimeSpan? probedDuration,
                int? probedVideoWidth,
                int? probedVideoHeight,
                bool? probedHasAudioStream,
                string standardOutput,
                string standardError)
            {
                Succeeded = succeeded;
                Message = message ?? string.Empty;
                ExitCode = exitCode;
                Elapsed = elapsed < TimeSpan.Zero ? TimeSpan.Zero : elapsed;
                ProbedDuration = probedDuration;
                ProbedVideoWidth = probedVideoWidth;
                ProbedVideoHeight = probedVideoHeight;
                ProbedHasAudioStream = probedHasAudioStream;
                StandardOutput = standardOutput ?? string.Empty;
                StandardError = standardError ?? string.Empty;
            }

            public bool Succeeded { get; }

            public string Message { get; }

            public int ExitCode { get; }

            public TimeSpan Elapsed { get; }

            public TimeSpan? ProbedDuration { get; }

            public int? ProbedVideoWidth { get; }

            public int? ProbedVideoHeight { get; }

            public bool? ProbedHasAudioStream { get; }

            public string StandardOutput { get; }

            public string StandardError { get; }
        }
    }
}
