using System;
using FramePlayer.Core.Abstractions;
using FramePlayer.Engines.FFmpeg;

namespace FramePlayer.Services
{
    internal sealed class VideoReviewEngineFactory
    {
        private readonly FfmpegReviewEngineOptionsProvider _optionsProvider;

        public VideoReviewEngineFactory(FfmpegReviewEngineOptionsProvider optionsProvider)
        {
            _optionsProvider = optionsProvider ?? throw new ArgumentNullException(nameof(optionsProvider));
        }

        public IVideoReviewEngine Create()
        {
            return new FfmpegReviewEngine(_optionsProvider);
        }
    }
}
