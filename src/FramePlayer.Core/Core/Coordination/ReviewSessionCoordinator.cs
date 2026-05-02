using System;
using FramePlayer.Core.Abstractions;
using FramePlayer.Core.Events;
using FramePlayer.Core.Models;

namespace FramePlayer.Core.Coordination
{
    // Tracks engine-backed single-session review state in a shell-neutral shape.
    public sealed class ReviewSessionCoordinator : IDisposable
    {
        private const double DefaultFramesPerSecond = 30.0;

        private readonly IVideoReviewEngine _engine;
        private readonly string _sessionId;
        private readonly string _displayLabel;

        public ReviewSessionCoordinator(
            IVideoReviewEngine engine,
            string sessionId = "primary",
            string displayLabel = "Primary")
        {
            _engine = engine ?? throw new ArgumentNullException(nameof(engine));
            _sessionId = string.IsNullOrWhiteSpace(sessionId) ? "primary" : sessionId;
            _displayLabel = displayLabel ?? string.Empty;
            CurrentSession = BuildSnapshot(
                _engine.IsMediaOpen,
                _engine.IsPlaying,
                _engine.CurrentFilePath,
                _engine.MediaInfo,
                _engine.Position);
            _engine.StateChanged += Engine_StateChanged;
        }

        public ReviewSessionSnapshot CurrentSession { get; private set; }

        public IVideoReviewEngine Engine
        {
            get { return _engine; }
        }

        public TimeSpan CurrentPositionStep
        {
            get
            {
                var positionStep = CurrentSession.MediaInfo.PositionStep;
                return positionStep > TimeSpan.Zero
                    ? positionStep
                    : TimeSpan.FromSeconds(1d / DefaultFramesPerSecond);
            }
        }

        public double CurrentFramesPerSecond
        {
            get
            {
                var framesPerSecond = CurrentSession.MediaInfo.FramesPerSecond;
                if (framesPerSecond > 0d)
                {
                    return framesPerSecond;
                }

                return CurrentPositionStep > TimeSpan.Zero
                    ? 1d / CurrentPositionStep.TotalSeconds
                    : DefaultFramesPerSecond;
            }
        }

        public TimeSpan CurrentDuration
        {
            get
            {
                return CurrentSession.MediaInfo.Duration > TimeSpan.Zero
                    ? CurrentSession.MediaInfo.Duration
                    : TimeSpan.Zero;
            }
        }

        public event EventHandler<ReviewSessionChangedEventArgs> SessionChanged;

        public ReviewSessionSnapshot RefreshFromEngine()
        {
            return ApplySnapshot(BuildSnapshot(
                _engine.IsMediaOpen,
                _engine.IsPlaying,
                _engine.CurrentFilePath,
                _engine.MediaInfo,
                _engine.Position));
        }

        public ReviewSessionSnapshot Reset(string currentFilePath = "")
        {
            return ApplySnapshot(
                new ReviewSessionSnapshot(
                    _sessionId,
                    _displayLabel,
                    ReviewPlaybackState.Closed,
                    currentFilePath ?? string.Empty,
                    VideoMediaInfo.Empty,
                    ReviewPosition.Empty));
        }

        public void Dispose()
        {
            _engine.StateChanged -= Engine_StateChanged;
        }

        private void Engine_StateChanged(object sender, VideoReviewEngineStateChangedEventArgs e)
        {
            if (e == null)
            {
                return;
            }

            ApplySnapshot(BuildSnapshot(
                e.IsMediaOpen,
                e.IsPlaying,
                e.CurrentFilePath,
                e.MediaInfo,
                e.Position));
        }

        private ReviewSessionSnapshot ApplySnapshot(ReviewSessionSnapshot nextSession)
        {
            var previousSession = CurrentSession ?? ReviewSessionSnapshot.Empty;
            var normalizedNextSession = nextSession ?? ReviewSessionSnapshot.Empty;
            if (SnapshotsEqual(previousSession, normalizedNextSession))
            {
                return CurrentSession;
            }

            CurrentSession = normalizedNextSession;
            SessionChanged?.Invoke(
                this,
                new ReviewSessionChangedEventArgs(previousSession, normalizedNextSession));
            return normalizedNextSession;
        }

        private ReviewSessionSnapshot BuildSnapshot(
            bool isMediaOpen,
            bool isPlaying,
            string currentFilePath,
            VideoMediaInfo mediaInfo,
            ReviewPosition position)
        {
            return new ReviewSessionSnapshot(
                _sessionId,
                _displayLabel,
                ReviewSessionSnapshot.FromEngineState(isMediaOpen, isPlaying),
                currentFilePath,
                mediaInfo ?? VideoMediaInfo.Empty,
                position ?? ReviewPosition.Empty);
        }

        private static bool SnapshotsEqual(ReviewSessionSnapshot left, ReviewSessionSnapshot right)
        {
            if (ReferenceEquals(left, right))
            {
                return true;
            }

            if (left == null || right == null)
            {
                return false;
            }

            return left.PlaybackState == right.PlaybackState &&
                   string.Equals(left.CurrentFilePath, right.CurrentFilePath, StringComparison.Ordinal) &&
                   left.Position.PresentationTime == right.Position.PresentationTime &&
                   left.Position.FrameIndex == right.Position.FrameIndex &&
                   left.Position.IsFrameAccurate == right.Position.IsFrameAccurate &&
                   left.Position.IsFrameIndexAbsolute == right.Position.IsFrameIndexAbsolute &&
                   left.MediaInfo.Duration == right.MediaInfo.Duration &&
                   left.MediaInfo.PositionStep == right.MediaInfo.PositionStep &&
                   Math.Abs(left.MediaInfo.FramesPerSecond - right.MediaInfo.FramesPerSecond) < 0.0001d &&
                   left.MediaInfo.HasAudioStream == right.MediaInfo.HasAudioStream &&
                   left.MediaInfo.IsAudioPlaybackAvailable == right.MediaInfo.IsAudioPlaybackAvailable;
        }
    }
}
