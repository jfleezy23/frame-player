using System;
using System.Threading;
using System.Threading.Tasks;
using FFmpeg.AutoGen;
using FramePlayer.Core.Models;
using FramePlayer.Engines.FFmpeg;

namespace FramePlayer.Services
{
    internal static unsafe class NativeAudioInsertionService
    {
        public static Task<AudioInsertionResult> InsertAsync(
            AudioInsertionPlan plan,
            CancellationToken cancellationToken = default(CancellationToken))
        {
            ArgumentNullException.ThrowIfNull(plan);

            return Task.Run(
                () =>
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    AVFormatContext* inputFormatContext = null;
                    AVFormatContext* outputFormatContext = null;
                    AVFilterGraph* filterGraph = null;
                    AVFilterContext* audioSinkContext = null;
                    AVCodecContext* audioEncoderContext = null;
                    AVFrame* nextAudioFrame = null;
                    AVPacket* nextVideoPacket = null;

                    var stopwatch = System.Diagnostics.Stopwatch.StartNew();
                    try
                    {
                        FfmpegNativeHelpers.ThrowIfError(
                            FfmpegNativeHelpers.OpenInput(&inputFormatContext, plan.SourceFilePath, null, null),
                            "Open source media container");
                        FfmpegNativeHelpers.ThrowIfError(
                            ffmpeg.avformat_find_stream_info(inputFormatContext, null),
                            "Probe source media streams");

                        var videoStreamIndex = ffmpeg.av_find_best_stream(
                            inputFormatContext,
                            AVMediaType.AVMEDIA_TYPE_VIDEO,
                            -1,
                            -1,
                            null,
                            0);
                        FfmpegNativeHelpers.ThrowIfError(videoStreamIndex, "Select source video stream");
                        var inputVideoStream = inputFormatContext->streams[videoStreamIndex];

                        filterGraph = ffmpeg.avfilter_graph_alloc();
                        if (filterGraph == null)
                        {
                            throw new InvalidOperationException("Could not allocate the FFmpeg audio insertion filter graph.");
                        }

                        var audioGraph = BuildAudioFilterGraph(plan);
                        AVFilterInOut* inputs = null;
                        AVFilterInOut* outputs = null;
                        try
                        {
                            FfmpegNativeHelpers.ThrowIfError(
                                ffmpeg.avfilter_graph_parse_ptr(filterGraph, audioGraph, &inputs, &outputs, null),
                                "Parse audio insertion filter graph");
                            FfmpegNativeHelpers.ThrowIfError(
                                ffmpeg.avfilter_graph_config(filterGraph, null),
                                "Configure audio insertion filter graph");
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

                        audioSinkContext = NativeExportSupport.ResolveFilterContext(filterGraph, "outa", "abuffersink");
                        if (audioSinkContext == null)
                        {
                            throw new InvalidOperationException("The audio insertion filter graph did not expose the expected audio sink.");
                        }

                        nextAudioFrame = NativeExportSupport.ReadNextFilterFrame(audioSinkContext);
                        if (nextAudioFrame == null)
                        {
                            throw new InvalidOperationException("The replacement audio file did not produce any decodable audio samples.");
                        }

                        var audioSinkTimeBase = ffmpeg.av_buffersink_get_time_base(audioSinkContext);
                        outputFormatContext = NativeExportSupport.CreateMp4OutputContext(plan.OutputFilePath);
                        var outputVideoStream = NativeExportSupport.AddCopiedStream(outputFormatContext, inputVideoStream);
                        var outputAudioStream = NativeExportSupport.AddAudioEncoderStream(
                            outputFormatContext,
                            nextAudioFrame,
                            out audioEncoderContext);

                        NativeExportSupport.OpenOutputIo(outputFormatContext, plan.OutputFilePath);
                        NativeExportSupport.WriteOutputHeader(outputFormatContext);

                        nextVideoPacket = ffmpeg.av_packet_alloc();
                        if (nextVideoPacket == null)
                        {
                            throw new InvalidOperationException("Could not allocate the FFmpeg packet used for source video copy.");
                        }

                        long? lastAudioFramePts = null;

                        var hasVideoPacket = NativeExportSupport.TryReadNextPacketForStream(
                            inputFormatContext,
                            videoStreamIndex,
                            nextVideoPacket);
                        while (hasVideoPacket || nextAudioFrame != null)
                        {
                            cancellationToken.ThrowIfCancellationRequested();

                            if (hasVideoPacket &&
                                NativeExportSupport.ShouldProcessVideoPacketFirst(
                                    nextVideoPacket,
                                    inputVideoStream->time_base,
                                    nextAudioFrame,
                                    audioSinkTimeBase))
                            {
                                NativeExportSupport.WriteCopiedPacket(
                                    outputFormatContext,
                                    inputVideoStream,
                                    outputVideoStream,
                                    nextVideoPacket);
                                ffmpeg.av_packet_unref(nextVideoPacket);
                                hasVideoPacket = NativeExportSupport.TryReadNextPacketForStream(
                                    inputFormatContext,
                                    videoStreamIndex,
                                    nextVideoPacket);
                                continue;
                            }

                            if (nextAudioFrame != null)
                            {
                                NativeExportSupport.WriteAudioFrame(
                                    outputFormatContext,
                                    outputAudioStream,
                                    audioEncoderContext,
                                    nextAudioFrame,
                                    audioSinkTimeBase,
                                    ref lastAudioFramePts);
                                NativeExportSupport.FreeFrame(ref nextAudioFrame);
                                nextAudioFrame = NativeExportSupport.ReadNextFilterFrame(audioSinkContext);
                            }
                        }

                        NativeExportSupport.FlushEncoder(
                            outputFormatContext,
                            outputAudioStream,
                            audioEncoderContext,
                            "Flush audio insertion encoder");
                        FfmpegNativeHelpers.ThrowIfError(
                            ffmpeg.av_write_trailer(outputFormatContext),
                            "Finalize audio insertion output");

                        stopwatch.Stop();
                        NativeExportSupport.ProbeOutput(plan.OutputFilePath, out var probedDuration, out _, out _, out var probedHasAudioStream);
                        return new AudioInsertionResult(
                            true,
                            plan,
                            "Audio insertion completed.",
                            0,
                            stopwatch.Elapsed,
                            probedDuration,
                            probedHasAudioStream,
                            string.Empty,
                            string.Empty);
                    }
                    catch (Exception ex)
                    {
                        stopwatch.Stop();
                        return new AudioInsertionResult(
                            false,
                            plan,
                            ex.Message,
                            -1,
                            stopwatch.Elapsed,
                            null,
                            null,
                            string.Empty,
                            ex.ToString());
                    }
                    finally
                    {
                        NativeExportSupport.FreeFrame(ref nextAudioFrame);
                        NativeExportSupport.FreePacket(ref nextVideoPacket);
                        NativeExportSupport.FreeCodecContext(ref audioEncoderContext);
                        NativeExportSupport.CloseOutputContext(ref outputFormatContext);
                        NativeExportSupport.FreeFilterGraph(ref filterGraph);

                        if (inputFormatContext != null)
                        {
                            var inputFormatContextToClose = inputFormatContext;
                            ffmpeg.avformat_close_input(&inputFormatContextToClose);
                            inputFormatContext = null;
                        }
                    }
                },
                cancellationToken);
        }

        private static string BuildAudioFilterGraph(AudioInsertionPlan plan)
        {
            return string.Format(
                System.Globalization.CultureInfo.InvariantCulture,
                "amovie=filename='{0}',apad=whole_dur={1},atrim=duration={1},asetpts=PTS-STARTPTS,aformat=sample_fmts=fltp,asetnsamples=n=1024:p=0,abuffersink@outa",
                NativeExportSupport.EscapeFilterFilePath(plan.ReplacementAudioFilePath),
                NativeExportSupport.FormatFilterTime(plan.VideoDuration));
        }
    }
}
