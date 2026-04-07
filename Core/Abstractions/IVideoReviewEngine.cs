using System;
using System.Threading;
using System.Threading.Tasks;
using FramePlayer.Core.Events;
using FramePlayer.Core.Models;

namespace FramePlayer.Core.Abstractions
{
    public interface IVideoReviewEngine : IDisposable
    {
        bool IsMediaOpen { get; }

        bool IsPlaying { get; }

        string CurrentFilePath { get; }

        string LastErrorMessage { get; }

        VideoMediaInfo MediaInfo { get; }

        ReviewPosition Position { get; }

        event EventHandler<VideoReviewEngineStateChangedEventArgs> StateChanged;

        event EventHandler<FramePresentedEventArgs> FramePresented;

        Task OpenAsync(string filePath, CancellationToken cancellationToken = default(CancellationToken));

        Task CloseAsync();

        Task PlayAsync();

        Task PauseAsync();

        Task<FrameStepResult> StepForwardAsync(CancellationToken cancellationToken = default(CancellationToken));

        Task<FrameStepResult> StepBackwardAsync(CancellationToken cancellationToken = default(CancellationToken));

        Task SeekToTimeAsync(TimeSpan position, CancellationToken cancellationToken = default(CancellationToken));

        Task SeekToFrameAsync(long frameIndex, CancellationToken cancellationToken = default(CancellationToken));
    }
}
