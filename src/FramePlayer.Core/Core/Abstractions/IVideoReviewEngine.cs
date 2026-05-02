using System;
using System.Threading;
using System.Threading.Tasks;
using FramePlayer.Core.Events;
using FramePlayer.Core.Models;

namespace FramePlayer.Core.Abstractions
{
    /// <summary>
    /// Defines the frame-first playback contract used by the WPF shell and review coordinators.
    /// Implementations must preserve decoded display-order frame identity for pause, seek, and
    /// step operations instead of deriving visible state from slider position or wall-clock time.
    /// </summary>
    public interface IVideoReviewEngine : IDisposable
    {
        /// <summary>
        /// Gets whether an engine session currently owns an open media resource.
        /// </summary>
        bool IsMediaOpen { get; }

        /// <summary>
        /// Gets whether timed playback is active. Frame stepping remains explicit even when playback is stopped.
        /// </summary>
        bool IsPlaying { get; }

        /// <summary>
        /// Gets the local file path for the open media. This value is for local review state only.
        /// </summary>
        string CurrentFilePath { get; }

        /// <summary>
        /// Gets the most recent engine-visible error message, when one is available.
        /// </summary>
        string LastErrorMessage { get; }

        /// <summary>
        /// Gets the current media metadata reported by the backing engine.
        /// </summary>
        VideoMediaInfo MediaInfo { get; }

        /// <summary>
        /// Gets the current review position, including frame identity when the engine can prove it.
        /// </summary>
        ReviewPosition Position { get; }

        /// <summary>
        /// Raised when playback, media, or position state changes in a way the shell should reflect.
        /// </summary>
        event EventHandler<VideoReviewEngineStateChangedEventArgs> StateChanged;

        /// <summary>
        /// Raised when a decoded frame buffer is ready for presentation.
        /// </summary>
        event EventHandler<FramePresentedEventArgs> FramePresented;

        /// <summary>
        /// Opens a local media file and positions the review cursor on the first displayable frame.
        /// </summary>
        Task OpenAsync(string filePath, CancellationToken cancellationToken = default(CancellationToken));

        /// <summary>
        /// Closes the current media session and releases native resources.
        /// </summary>
        Task CloseAsync();

        /// <summary>
        /// Starts timed playback from the current decoded review position.
        /// </summary>
        Task PlayAsync();

        /// <summary>
        /// Pauses timed playback while preserving the current decoded frame position.
        /// </summary>
        Task PauseAsync();

        /// <summary>
        /// Advances to the next decoded display-order frame.
        /// </summary>
        Task<FrameStepResult> StepForwardAsync(CancellationToken cancellationToken = default(CancellationToken));

        /// <summary>
        /// Moves to the previous decoded display-order frame, using cache or seek/decode reconstruction.
        /// </summary>
        Task<FrameStepResult> StepBackwardAsync(CancellationToken cancellationToken = default(CancellationToken));

        /// <summary>
        /// Seeks near a media time and reports whether the resulting frame identity is absolute or provisional.
        /// </summary>
        Task SeekToTimeAsync(TimeSpan position, CancellationToken cancellationToken = default(CancellationToken));

        /// <summary>
        /// Seeks to a zero-based absolute frame index when the engine has enough index data to prove it.
        /// </summary>
        Task SeekToFrameAsync(long frameIndex, CancellationToken cancellationToken = default(CancellationToken));
    }
}
