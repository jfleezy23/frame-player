using System;
using FramePlayer.Core.Abstractions;
using FramePlayer.Engines.FFmpeg;

namespace FramePlayer.Services
{
    public sealed class VideoReviewEngineFactory
    {
        private readonly FfmpegReviewEngineOptionsProvider _optionsProvider;
        private readonly DecodedFrameBudgetCoordinator _budgetCoordinator;
        private readonly IAudioOutputFactory _audioOutputFactory;

        public VideoReviewEngineFactory(
            FfmpegReviewEngineOptionsProvider optionsProvider,
            IAudioOutputFactory audioOutputFactory = null)
        {
            _optionsProvider = optionsProvider ?? throw new ArgumentNullException(nameof(optionsProvider));
            _budgetCoordinator = new DecodedFrameBudgetCoordinator();
            _audioOutputFactory = audioOutputFactory ?? WinMmAudioOutputFactory.Instance;
        }

        public IVideoReviewEngine Create()
        {
            return Create("pane-" + Guid.NewGuid().ToString("N"));
        }

        public IVideoReviewEngine Create(string paneId)
        {
            return new FfmpegReviewEngine(_optionsProvider, _budgetCoordinator, paneId, _audioOutputFactory);
        }
    }
}
