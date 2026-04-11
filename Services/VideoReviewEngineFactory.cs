using System;
using FramePlayer.Core.Abstractions;
using FramePlayer.Engines.FFmpeg;

namespace FramePlayer.Services
{
    internal sealed class VideoReviewEngineFactory
    {
        private readonly FfmpegReviewEngineOptionsProvider _optionsProvider;
        private readonly DecodedFrameBudgetCoordinator _budgetCoordinator;

        public VideoReviewEngineFactory(FfmpegReviewEngineOptionsProvider optionsProvider)
        {
            _optionsProvider = optionsProvider ?? throw new ArgumentNullException(nameof(optionsProvider));
            _budgetCoordinator = new DecodedFrameBudgetCoordinator();
        }

        public IVideoReviewEngine Create()
        {
            return Create("pane-" + Guid.NewGuid().ToString("N"));
        }

        public IVideoReviewEngine Create(string paneId)
        {
            return new FfmpegReviewEngine(_optionsProvider, _budgetCoordinator, paneId);
        }
    }
}
