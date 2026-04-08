using System;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using FFmpeg.AutoGen;

namespace FramePlayer.Engines.FFmpeg
{
    internal unsafe sealed class FfmpegAudioPlaybackSession : IDisposable
    {
        private const int OutputChannelCount = 2;
        private const int OutputBitsPerSample = 16;
        private const AVSampleFormat OutputSampleFormat = AVSampleFormat.AV_SAMPLE_FMT_S16;

        private readonly string _filePath;
        private readonly TimeSpan _startPosition;
        private readonly CancellationToken _cancellationToken;
        private AVFormatContext* _formatContext;
        private AVCodecContext* _codecContext;
        private AVStream* _audioStream;
        private AVPacket* _packet;
        private AVFrame* _decodedFrame;
        private SwrContext* _resampler;
        private WinMmAudioOutput _audioOutput;
        private Task _decodeTask;
        private long _submittedAudioBytesSnapshot;
        private int _audioStreamIndex;
        private AVRational _audioStreamTimeBase;
        private int _outputSampleRate;
        private bool _disposed;

        private FfmpegAudioPlaybackSession(
            string filePath,
            TimeSpan startPosition,
            CancellationToken cancellationToken)
        {
            _filePath = filePath ?? throw new ArgumentNullException(nameof(filePath));
            _startPosition = startPosition < TimeSpan.Zero ? TimeSpan.Zero : startPosition;
            _cancellationToken = cancellationToken;
            _audioStreamIndex = -1;
        }

        public FfmpegAudioStreamInfo StreamInfo { get; private set; } = FfmpegAudioStreamInfo.None;

        public string LastErrorMessage { get; private set; } = string.Empty;

        public bool IsActive
        {
            get { return !_disposed && _audioOutput != null && _audioOutput.IsOpen; }
        }

        public TimeSpan PlaybackPosition
        {
            get
            {
                return IsActive
                    ? _startPosition + _audioOutput.PlayedDuration
                    : _startPosition;
            }
        }

        public long SubmittedAudioBytes
        {
            get { return _audioOutput != null ? _audioOutput.SubmittedBytes : _submittedAudioBytesSnapshot; }
        }

        public int OutputSampleRate
        {
            get { return _outputSampleRate; }
        }

        public static FfmpegAudioPlaybackSession Start(
            string filePath,
            TimeSpan startPosition,
            CancellationToken cancellationToken)
        {
            var session = new FfmpegAudioPlaybackSession(filePath, startPosition, cancellationToken);
            try
            {
                session.Open();
                session._decodeTask = Task.Run(() => session.DecodeLoop(cancellationToken), cancellationToken);
                return session;
            }
            catch
            {
                session.Dispose();
                throw;
            }
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;

            if (_audioOutput != null)
            {
                _submittedAudioBytesSnapshot = _audioOutput.SubmittedBytes;
                _audioOutput.Dispose();
            }

            _audioOutput = null;

            if (_decodeTask != null)
            {
                try
                {
                    _decodeTask.GetAwaiter().GetResult();
                }
                catch (OperationCanceledException)
                {
                }
                catch (Exception ex)
                {
                    LastErrorMessage = ex.Message;
                }

                _decodeTask = null;
            }

            ReleaseNativeState();
        }

        private void Open()
        {
            AVCodec* decoder = null;
            var formatContext = _formatContext;
            FfmpegNativeHelpers.ThrowIfError(
                FfmpegNativeHelpers.OpenInput(&formatContext, _filePath, null, null),
                "Open audio media container");
            _formatContext = formatContext;

            FfmpegNativeHelpers.ThrowIfError(
                ffmpeg.avformat_find_stream_info(_formatContext, null),
                "Probe audio media streams");

            var bestStreamIndex = ffmpeg.av_find_best_stream(
                _formatContext,
                AVMediaType.AVMEDIA_TYPE_AUDIO,
                -1,
                -1,
                &decoder,
                0);
            FfmpegNativeHelpers.ThrowIfError(bestStreamIndex, "Select primary audio stream");

            _audioStreamIndex = bestStreamIndex;
            _audioStream = _formatContext->streams[_audioStreamIndex];
            _audioStreamTimeBase = _audioStream->time_base;
            if (decoder == null)
            {
                decoder = ffmpeg.avcodec_find_decoder(_audioStream->codecpar->codec_id);
            }

            if (decoder == null)
            {
                throw new InvalidOperationException("No decoder is available for the selected audio stream.");
            }

            _codecContext = ffmpeg.avcodec_alloc_context3(decoder);
            if (_codecContext == null)
            {
                throw new InvalidOperationException("Could not allocate the FFmpeg audio codec context.");
            }

            FfmpegNativeHelpers.ThrowIfError(
                ffmpeg.avcodec_parameters_to_context(_codecContext, _audioStream->codecpar),
                "Copy audio codec parameters");
            _codecContext->pkt_timebase = _audioStreamTimeBase;

            FfmpegNativeHelpers.ThrowIfError(
                ffmpeg.avcodec_open2(_codecContext, _codecContext->codec, null),
                "Open audio decoder");

            _packet = ffmpeg.av_packet_alloc();
            if (_packet == null)
            {
                throw new InvalidOperationException("Could not allocate an FFmpeg audio packet.");
            }

            _decodedFrame = ffmpeg.av_frame_alloc();
            if (_decodedFrame == null)
            {
                throw new InvalidOperationException("Could not allocate an FFmpeg audio frame.");
            }

            _outputSampleRate = _codecContext->sample_rate > 0 ? _codecContext->sample_rate : 48000;
            InitializeResampler();
            _audioOutput = new WinMmAudioOutput(_outputSampleRate, OutputChannelCount, OutputBitsPerSample);

            StreamInfo = new FfmpegAudioStreamInfo(
                true,
                true,
                _audioStreamIndex,
                FfmpegNativeHelpers.GetCodecName(_codecContext->codec_id),
                _codecContext->sample_rate,
                GetChannelCount(_codecContext->ch_layout));

            SeekAudioDecoder(_startPosition);
        }

        private void InitializeResampler()
        {
            AVChannelLayout inputLayout = default(AVChannelLayout);
            AVChannelLayout outputLayout = default(AVChannelLayout);
            var inputLayoutOwned = false;
            try
            {
                inputLayout = _codecContext->ch_layout;
                if (ffmpeg.av_channel_layout_check(&inputLayout) <= 0 || inputLayout.nb_channels <= 0)
                {
                    ffmpeg.av_channel_layout_default(&inputLayout, Math.Max(1, GetChannelCount(_audioStream->codecpar->ch_layout)));
                    inputLayoutOwned = true;
                }

                ffmpeg.av_channel_layout_default(&outputLayout, OutputChannelCount);
                var resampler = _resampler;
                FfmpegNativeHelpers.ThrowIfError(
                    ffmpeg.swr_alloc_set_opts2(
                        &resampler,
                        &outputLayout,
                        OutputSampleFormat,
                        _outputSampleRate,
                        &inputLayout,
                        _codecContext->sample_fmt,
                        _codecContext->sample_rate > 0 ? _codecContext->sample_rate : _outputSampleRate,
                        0,
                        null),
                    "Configure audio resampler");
                _resampler = resampler;

                FfmpegNativeHelpers.ThrowIfError(ffmpeg.swr_init(_resampler), "Initialize audio resampler");
            }
            finally
            {
                ffmpeg.av_channel_layout_uninit(&outputLayout);
                if (inputLayoutOwned)
                {
                    ffmpeg.av_channel_layout_uninit(&inputLayout);
                }
            }
        }

        private void SeekAudioDecoder(TimeSpan position)
        {
            if (position <= TimeSpan.Zero)
            {
                ffmpeg.avcodec_flush_buffers(_codecContext);
                return;
            }

            var timestamp = FfmpegNativeHelpers.ToStreamTimestamp(position, _audioStreamTimeBase);
            var seekResult = ffmpeg.av_seek_frame(
                _formatContext,
                _audioStreamIndex,
                timestamp,
                ffmpeg.AVSEEK_FLAG_BACKWARD);
            if (seekResult < 0)
            {
                var globalTimestamp = checked((long)Math.Round(
                    position.TotalSeconds * ffmpeg.AV_TIME_BASE,
                    MidpointRounding.AwayFromZero));
                seekResult = ffmpeg.av_seek_frame(
                    _formatContext,
                    -1,
                    globalTimestamp,
                    ffmpeg.AVSEEK_FLAG_BACKWARD);
            }

            // Some simple containers/codecs do not expose an audio-seek index. Starting
            // from the current demux position and trimming decoded samples keeps playback
            // available without touching the review decoder used for exact stepping.
            if (seekResult < 0)
            {
                return;
            }

            ffmpeg.avcodec_flush_buffers(_codecContext);
        }

        private void DecodeLoop(CancellationToken cancellationToken)
        {
            try
            {
                while (true)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    var receiveResult = ffmpeg.avcodec_receive_frame(_codecContext, _decodedFrame);
                    if (receiveResult == 0)
                    {
                        try
                        {
                            WriteDecodedFrame(_decodedFrame, cancellationToken);
                        }
                        finally
                        {
                            ffmpeg.av_frame_unref(_decodedFrame);
                        }

                        continue;
                    }

                    if (receiveResult != ffmpeg.AVERROR(ffmpeg.EAGAIN) && receiveResult != ffmpeg.AVERROR_EOF)
                    {
                        FfmpegNativeHelpers.ThrowIfError(receiveResult, "Decode audio frame");
                    }

                    var readResult = ffmpeg.av_read_frame(_formatContext, _packet);
                    if (readResult == ffmpeg.AVERROR_EOF)
                    {
                        FfmpegNativeHelpers.ThrowIfError(
                            ffmpeg.avcodec_send_packet(_codecContext, null),
                            "Flush audio decoder");
                        DrainAudioDecoder(cancellationToken);
                        return;
                    }

                    FfmpegNativeHelpers.ThrowIfError(readResult, "Read encoded audio packet");
                    try
                    {
                        if (_packet->stream_index != _audioStreamIndex)
                        {
                            continue;
                        }

                        var sendResult = ffmpeg.avcodec_send_packet(_codecContext, _packet);
                        if (sendResult != ffmpeg.AVERROR(ffmpeg.EAGAIN))
                        {
                            FfmpegNativeHelpers.ThrowIfError(sendResult, "Submit audio packet to decoder");
                        }
                    }
                    finally
                    {
                        ffmpeg.av_packet_unref(_packet);
                    }
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
            }
            catch (Exception ex)
            {
                LastErrorMessage = ex.Message;
            }
        }

        private void DrainAudioDecoder(CancellationToken cancellationToken)
        {
            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var receiveResult = ffmpeg.avcodec_receive_frame(_codecContext, _decodedFrame);
                if (receiveResult == ffmpeg.AVERROR_EOF || receiveResult == ffmpeg.AVERROR(ffmpeg.EAGAIN))
                {
                    return;
                }

                FfmpegNativeHelpers.ThrowIfError(receiveResult, "Drain audio decoder");
                try
                {
                    WriteDecodedFrame(_decodedFrame, cancellationToken);
                }
                finally
                {
                    ffmpeg.av_frame_unref(_decodedFrame);
                }
            }
        }

        private void WriteDecodedFrame(AVFrame* frame, CancellationToken cancellationToken)
        {
            if (frame == null || frame->nb_samples <= 0)
            {
                return;
            }

            var inputSampleRate = frame->sample_rate > 0
                ? frame->sample_rate
                : _codecContext->sample_rate > 0
                    ? _codecContext->sample_rate
                    : _outputSampleRate;
            var outputSampleCount = checked((int)ffmpeg.av_rescale_rnd(
                ffmpeg.swr_get_delay(_resampler, inputSampleRate) + frame->nb_samples,
                _outputSampleRate,
                inputSampleRate,
                AVRounding.AV_ROUND_UP));

            byte** convertedData = null;
            var lineSize = 0;
            try
            {
                FfmpegNativeHelpers.ThrowIfError(
                    ffmpeg.av_samples_alloc_array_and_samples(
                        &convertedData,
                        &lineSize,
                        OutputChannelCount,
                        outputSampleCount,
                        OutputSampleFormat,
                        0),
                    "Allocate converted audio samples");

                var convertedSampleCount = ffmpeg.swr_convert(
                    _resampler,
                    convertedData,
                    outputSampleCount,
                    frame->extended_data,
                    frame->nb_samples);
                FfmpegNativeHelpers.ThrowIfError(convertedSampleCount, "Convert audio samples");

                var byteCount = ffmpeg.av_samples_get_buffer_size(
                    &lineSize,
                    OutputChannelCount,
                    convertedSampleCount,
                    OutputSampleFormat,
                    1);
                FfmpegNativeHelpers.ThrowIfError(byteCount, "Measure converted audio buffer");

                var skipBytes = CalculateTrimBytes(frame, convertedSampleCount);
                if (skipBytes >= byteCount)
                {
                    return;
                }

                var managedBuffer = new byte[byteCount - skipBytes];
                Marshal.Copy((IntPtr)(convertedData[0] + skipBytes), managedBuffer, 0, managedBuffer.Length);
                _audioOutput.Write(managedBuffer, cancellationToken);
            }
            finally
            {
                if (convertedData != null)
                {
                    ffmpeg.av_freep(&convertedData[0]);
                    ffmpeg.av_freep(&convertedData);
                }
            }
        }

        private int CalculateTrimBytes(AVFrame* frame, int convertedSampleCount)
        {
            var presentationTimestamp = FfmpegNativeHelpers.GetBestPresentationTimestamp(frame);
            if (!presentationTimestamp.HasValue)
            {
                return 0;
            }

            var presentationTime = FfmpegNativeHelpers.ToTimeSpan(presentationTimestamp.Value, _audioStreamTimeBase);
            if (presentationTime >= _startPosition)
            {
                return 0;
            }

            var trimDuration = _startPosition - presentationTime;
            var samplesToTrim = (int)Math.Round(trimDuration.TotalSeconds * _outputSampleRate, MidpointRounding.AwayFromZero);
            if (samplesToTrim <= 0)
            {
                return 0;
            }

            if (samplesToTrim >= convertedSampleCount)
            {
                return int.MaxValue;
            }

            return checked(samplesToTrim * OutputChannelCount * (OutputBitsPerSample / 8));
        }

        private static int GetChannelCount(AVChannelLayout channelLayout)
        {
            return channelLayout.nb_channels > 0 ? channelLayout.nb_channels : 0;
        }

        private void ReleaseNativeState()
        {
            if (_resampler != null)
            {
                var resampler = _resampler;
                ffmpeg.swr_free(&resampler);
                _resampler = null;
            }

            if (_decodedFrame != null)
            {
                var decodedFrame = _decodedFrame;
                ffmpeg.av_frame_free(&decodedFrame);
                _decodedFrame = null;
            }

            if (_packet != null)
            {
                var packet = _packet;
                ffmpeg.av_packet_free(&packet);
                _packet = null;
            }

            if (_codecContext != null)
            {
                var codecContext = _codecContext;
                ffmpeg.avcodec_free_context(&codecContext);
                _codecContext = null;
            }

            if (_formatContext != null)
            {
                var formatContext = _formatContext;
                ffmpeg.avformat_close_input(&formatContext);
                _formatContext = null;
            }

            _audioStream = null;
        }
    }
}
