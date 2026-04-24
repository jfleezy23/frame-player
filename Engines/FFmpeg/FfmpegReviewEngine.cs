using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using FFmpeg.AutoGen;
using FramePlayer.Core.Abstractions;
using FramePlayer.Core.Events;
using FramePlayer.Core.Models;
using FramePlayer.Services;

namespace FramePlayer.Engines.FFmpeg
{
    public unsafe sealed class FfmpegReviewEngine : IVideoReviewEngine
    {
        private const int DefaultCachedPreviousFrameCount = 10;
        private const int DefaultCachedForwardFrameCount = 1;
        private const int CpuOperationalQueueDepth = 1;
        private const int GpuOperationalQueueDepth = 2;
        private const int AvCodecHwConfigMethodHwDeviceContext = 0x01;
        private const int AvCodecHwConfigMethodHwFramesContext = 0x02;
        private static readonly TimeSpan MinimumPlaybackDelay = TimeSpan.FromMilliseconds(1d);
        private static readonly TimeSpan AudioClockStallTolerance = TimeSpan.FromMilliseconds(250d);
        private static readonly TimeSpan AudioClockProgressTolerance = TimeSpan.FromMilliseconds(2d);

        // Decoded display-order frame identity is the source of truth for this engine.
        // Frame stepping must be cache- or decode-based rather than timestamp math.
        // Backward stepping should first use cached decoded frames, then seek to the prior
        // keyframe and decode forward until the requested display-order frame is reconstructed.
        private const string OutputPixelFormatName = "bgra";
        private const string StreamStartAnchorStrategy = "stream-start";

        private readonly object _playbackSync = new object();
        private readonly object _indexBuildSync = new object();
        private readonly FfmpegDecodedFrameCache _frameCache;
        private readonly FfmpegReviewEngineOptionsProvider _optionsProvider;
        private readonly DecodedFrameBudgetCoordinator _budgetCoordinator;
        private readonly string _paneId;
        private FfmpegGlobalFrameIndex _globalFrameIndex;
        private FfmpegAudioPlaybackSession _audioPlaybackSession;
        private FfmpegAudioStreamInfo _audioStreamInfo;
        private bool _disposed;
        private bool _hasPendingVideoPacket;
        private bool _inputExhausted;
        private bool _flushPacketSent;
        private bool _segmentFrameIndexAbsolute;
        private string _currentFilePath;
        private string _lastErrorMessage;
        private string _lastAudioErrorMessage;
        private VideoMediaInfo _mediaInfo;
        private ReviewPosition _position;
        private DecodedFrameBuffer _currentFrame;
        private long _decodedSegmentFrameCount;
        private int _videoStreamIndex;
        private AVRational _videoStreamTimeBase;
        private AVRational _nominalFrameRate;
        private AVFormatContext* _formatContext;
        private AVCodec* _videoDecoder;
        private AVCodecContext* _codecContext;
        private AVStream* _videoStream;
        private AVPacket* _packet;
        private AVFrame* _decodedFrame;
        private AVFrame* _softwareFrame;
        private AVBufferRef* _hardwareDeviceContext;
        private AVPixelFormat _hardwarePixelFormat;
        private FfmpegFrameConverter _frameConverter;
        [SuppressMessage(
            "Major Code Smell",
            "S1450:Private fields only used as local variables in methods should become local variables",
            Justification = "FFmpeg stores the callback in unmanaged state; the engine must keep a managed delegate alive for the codec context lifetime.")]
        private readonly AVCodecContext_get_format _hardwareGetFormatCallback;
        private CancellationTokenSource _playbackCancellationSource;
        private Task _playbackTask;
        private CancellationTokenSource _indexBuildCancellationSource;
        private Task _indexBuildTask;
        private long _lastAudioSubmittedBytes;
        private bool _playbackStartNeedsDecoderRealignment;
        private int _indexBuildGeneration;
        private bool _isGlobalFrameIndexBuildInProgress;
        private bool _hardwareDecodeFormatSelected;
        private string _globalFrameIndexStatus;
        private FfmpegReviewEngineOptions _currentOpenOptions;
        private int _maxPreviousCachedFrameCount;
        private int _maxForwardCachedFrameCount;
        private long _configuredCacheBudgetBytes;
        private long _sessionDecodedFrameCacheBudgetBytes;
        private DecodedFrameBudgetBand _budgetBand;
        private HostResourceClass _hostResourceClass;

        public FfmpegReviewEngine()
            : this(null, null, "pane-primary")
        {
        }

        internal FfmpegReviewEngine(FfmpegReviewEngineOptionsProvider optionsProvider)
            : this(optionsProvider, null, "pane-primary")
        {
        }

        internal FfmpegReviewEngine(
            FfmpegReviewEngineOptionsProvider optionsProvider,
            DecodedFrameBudgetCoordinator budgetCoordinator,
            string paneId)
        {
            _optionsProvider = optionsProvider;
            _frameCache = new FfmpegDecodedFrameCache(DefaultCachedPreviousFrameCount, DefaultCachedForwardFrameCount);
            _budgetCoordinator = budgetCoordinator ?? new DecodedFrameBudgetCoordinator();
            _paneId = string.IsNullOrWhiteSpace(paneId)
                ? "pane-" + Guid.NewGuid().ToString("N")
                : paneId;
            _currentFilePath = string.Empty;
            _lastErrorMessage = string.Empty;
            _mediaInfo = VideoMediaInfo.Empty;
            _position = ReviewPosition.Empty;
            _globalFrameIndex = null;
            _audioStreamInfo = FfmpegAudioStreamInfo.None;
            _lastAudioErrorMessage = string.Empty;
            _globalFrameIndexStatus = "not-started";
            LastSeekMode = string.Empty;
            _videoStreamIndex = -1;
            _videoStreamTimeBase = default(AVRational);
            _nominalFrameRate = default(AVRational);
            _currentOpenOptions = FfmpegReviewEngineOptions.Default;
            _maxPreviousCachedFrameCount = DefaultCachedPreviousFrameCount;
            _maxForwardCachedFrameCount = DefaultCachedForwardFrameCount;
            _configuredCacheBudgetBytes = 0L;
            _sessionDecodedFrameCacheBudgetBytes = 0L;
            _budgetBand = DecodedFrameBudgetBand.SinglePaneCpu;
            _hostResourceClass = _budgetCoordinator.HostResourceClass;
            ActiveDecodeBackend = "ffmpeg-cpu";
            GpuCapabilityStatus = "not-requested";
            GpuFallbackReason = string.Empty;
            _hardwareGetFormatCallback = SelectHardwarePixelFormat;
            OperationalQueueDepth = CpuOperationalQueueDepth;
            _budgetCoordinator.AllocationChanged += BudgetCoordinator_AllocationChanged;
            _budgetCoordinator.RegisterPane(_paneId);
            RefreshDecodedFrameBudgetState(isOpen: false, approximateFrameBytes: 0, gpuActive: false);
        }

        public bool IsMediaOpen { get; private set; }

        public bool IsPlaying { get; private set; }

        public bool LastSeekLandedAtOrAfterTarget { get; private set; }

        public bool LastFrameAdvanceWasCacheHit { get; private set; }

        public bool LastFrameSeekWasCacheHit { get; private set; }

        public bool IsGpuActive { get; private set; }

        public string ActiveDecodeBackend { get; private set; }

        public string GpuCapabilityStatus { get; private set; }

        public string GpuFallbackReason { get; private set; }

        public long DecodedFrameCacheBudgetBytes
        {
            get { return _configuredCacheBudgetBytes; }
        }

        public long SessionDecodedFrameCacheBudgetBytes
        {
            get { return _sessionDecodedFrameCacheBudgetBytes; }
        }

        public string BudgetBand
        {
            get { return _budgetBand.ToString(); }
        }

        public string HostResourceClass
        {
            get { return _hostResourceClass.ToString(); }
        }

        public string ActualBackendUsed
        {
            get { return ActiveDecodeBackend; }
        }

        public int OperationalQueueDepth { get; private set; }

        public bool HasAudioStream
        {
            get { return _audioStreamInfo != null && _audioStreamInfo.HasAudioStream; }
        }

        public bool IsAudioPlaybackActive
        {
            get { return _audioPlaybackSession != null && _audioPlaybackSession.IsActive; }
        }

        public bool LastPlaybackUsedAudioClock { get; private set; }

        public long LastAudioSubmittedBytes
        {
            get
            {
                var session = _audioPlaybackSession;
                if (session != null)
                {
                    _lastAudioSubmittedBytes = Math.Max(_lastAudioSubmittedBytes, session.SubmittedAudioBytes);
                }

                return _lastAudioSubmittedBytes;
            }
        }

        public string LastAudioErrorMessage
        {
            get { return _lastAudioErrorMessage; }
        }

        internal FfmpegAudioStreamInfo AudioStreamInfo
        {
            get { return _audioStreamInfo ?? FfmpegAudioStreamInfo.None; }
        }

        public bool IsGlobalFrameIndexAvailable
        {
            get { return _globalFrameIndex != null && _globalFrameIndex.IsAvailable; }
        }

        public bool IsGlobalFrameIndexBuildInProgress
        {
            get { return _isGlobalFrameIndexBuildInProgress; }
        }

        public string GlobalFrameIndexStatus
        {
            get { return _globalFrameIndexStatus; }
        }

        public long IndexedFrameCount
        {
            get { return _globalFrameIndex != null ? _globalFrameIndex.Count : 0L; }
        }

        public TimeSpan LastIndexedFramePresentationTime
        {
            get
            {
                FfmpegGlobalFrameIndexEntry lastEntry;
                return _globalFrameIndex != null && _globalFrameIndex.TryGetLastEntry(out lastEntry)
                    ? lastEntry.PresentationTime
                    : TimeSpan.Zero;
            }
        }

        internal bool TryGetIndexedPresentationTime(long absoluteFrameIndex, out TimeSpan presentationTime)
        {
            FfmpegGlobalFrameIndexEntry entry;
            if (_globalFrameIndex != null &&
                _globalFrameIndex.TryGetByAbsoluteFrameIndex(absoluteFrameIndex, out entry) &&
                entry != null)
            {
                presentationTime = entry.PresentationTime;
                return true;
            }

            presentationTime = TimeSpan.Zero;
            return false;
        }

        internal bool TryResolveIndexedFrameAtOrAfterPresentationTime(TimeSpan position, out long absoluteFrameIndex)
        {
            absoluteFrameIndex = 0L;
            if (!IsGlobalFrameIndexAvailable)
            {
                return false;
            }

            var clampedTarget = ClampSeekTarget(position);
            var targetTimestamp = FfmpegNativeHelpers.ToStreamTimestamp(clampedTarget, _videoStreamTimeBase);
            FfmpegGlobalFrameIndexEntry entry;
            if (_globalFrameIndex.TryGetFirstAtOrAfterTimestamp(targetTimestamp, out entry) && entry != null)
            {
                absoluteFrameIndex = entry.AbsoluteFrameIndex;
                return true;
            }

            FfmpegGlobalFrameIndexEntry lastEntry;
            if (_globalFrameIndex.TryGetLastEntry(out lastEntry) &&
                lastEntry != null &&
                clampedTarget >= lastEntry.PresentationTime)
            {
                absoluteFrameIndex = lastEntry.AbsoluteFrameIndex;
                return true;
            }

            return false;
        }

        internal bool TryResolveIndexedFrameIdentity(
            long? presentationTimestamp,
            long? decodeTimestamp,
            out long absoluteFrameIndex,
            out TimeSpan presentationTime)
        {
            FfmpegGlobalFrameIndexEntry entry;
            if (_globalFrameIndex != null &&
                _globalFrameIndex.TryResolve(presentationTimestamp, decodeTimestamp, out entry) &&
                entry != null)
            {
                absoluteFrameIndex = entry.AbsoluteFrameIndex;
                presentationTime = entry.PresentationTime;
                return true;
            }

            absoluteFrameIndex = 0L;
            presentationTime = TimeSpan.Zero;
            return false;
        }

        public double LastOpenTotalMilliseconds { get; private set; }

        public double LastOpenContainerProbeMilliseconds { get; private set; }

        public double LastOpenStreamDiscoveryMilliseconds { get; private set; }

        public double LastOpenAudioProbeMilliseconds { get; private set; }

        public double LastOpenVideoDecoderInitializationMilliseconds { get; private set; }

        public double LastOpenFirstFrameDecodeMilliseconds { get; private set; }

        public double LastOpenInitialCacheWarmMilliseconds { get; private set; }

        public double LastGlobalFrameIndexBuildMilliseconds { get; private set; }

        public double LastHardwareFrameTransferMilliseconds { get; private set; }

        public double LastBgraConversionMilliseconds { get; private set; }

        public double LastSeekTotalMilliseconds { get; private set; }

        public double LastSeekIndexWaitMilliseconds { get; private set; }

        public double LastSeekMaterializeMilliseconds { get; private set; }

        public double LastSeekForwardCacheWarmMilliseconds { get; private set; }

        public string LastSeekMode { get; private set; }

        public double LastCacheRefillMilliseconds { get; private set; }

        public string LastCacheRefillReason { get; private set; } = string.Empty;

        public string LastCacheRefillMode { get; private set; } = string.Empty;

        public bool LastCacheRefillWasSynchronous { get; private set; }

        public bool LastCacheRefillAfterLanding { get; private set; }

        public int LastCacheRefillStartingForwardCount { get; private set; }

        public int LastCacheRefillCompletedForwardCount { get; private set; }

        public int LastCacheRefillCompletedPreviousCount { get; private set; }

        public int MaxPreviousCachedFrameCount
        {
            get { return _maxPreviousCachedFrameCount; }
        }

        public int MaxForwardCachedFrameCount
        {
            get { return _maxForwardCachedFrameCount; }
        }

        public int PreviousCachedFrameCount
        {
            get { return _frameCache.PreviousCount; }
        }

        public bool LastOperationUsedGlobalIndex { get; private set; }

        public long? LastAnchorFrameIndex { get; private set; }

        public string LastAnchorStrategy { get; private set; } = string.Empty;

        public int CachedFrameCount
        {
            get { return _frameCache.Count; }
        }

        public int ForwardCachedFrameCount
        {
            get { return _frameCache.ForwardCount; }
        }

        public long ApproximateCachedFrameBytes
        {
            get { return _frameCache.ApproximatePixelBufferBytes; }
        }

        public string CurrentFilePath
        {
            get { return _currentFilePath; }
        }

        public string LastErrorMessage
        {
            get { return _lastErrorMessage; }
        }

        public VideoMediaInfo MediaInfo
        {
            get { return _mediaInfo; }
        }

        public ReviewPosition Position
        {
            get { return _position; }
        }

        public event EventHandler<VideoReviewEngineStateChangedEventArgs> StateChanged;

        public event EventHandler<FramePresentedEventArgs> FramePresented;

        public Task OpenAsync(string filePath, CancellationToken cancellationToken = default(CancellationToken))
        {
            ThrowIfDisposed();
            cancellationToken.ThrowIfCancellationRequested();

            if (string.IsNullOrWhiteSpace(filePath))
            {
                throw new ArgumentException("A media file path is required.", nameof(filePath));
            }

            if (!File.Exists(filePath))
            {
                throw new FileNotFoundException("The requested media file was not found.", filePath);
            }

            StopPlayback(raiseStateChanged: false);
            CloseCore(clearFilePath: true, clearErrorMessage: true);
            EnsureFfmpegRuntimePathConfigured();

            _currentFilePath = filePath;
            _lastErrorMessage = string.Empty;
            LastSeekLandedAtOrAfterTarget = true;
            LastFrameAdvanceWasCacheHit = false;
            LastFrameSeekWasCacheHit = false;
            LastOperationUsedGlobalIndex = false;
            LastPlaybackUsedAudioClock = false;
            _lastAudioSubmittedBytes = 0L;
            _lastAudioErrorMessage = string.Empty;
            LastAnchorFrameIndex = null;
            LastAnchorStrategy = string.Empty;
            _currentOpenOptions = GetCurrentOptions();
            ResetHardwareDecodeDiagnostics();
            ResetOpenPerformanceInstrumentation();

            try
            {
                var openStopwatch = Stopwatch.StartNew();
                var stepStopwatch = Stopwatch.StartNew();
                OpenContainer(filePath);
                LastOpenContainerProbeMilliseconds = stepStopwatch.Elapsed.TotalMilliseconds;

                stepStopwatch.Restart();
                FindPrimaryVideoStream();
                LastOpenStreamDiscoveryMilliseconds = stepStopwatch.Elapsed.TotalMilliseconds;

                stepStopwatch.Restart();
                ProbePrimaryAudioStream();
                LastOpenAudioProbeMilliseconds = stepStopwatch.Elapsed.TotalMilliseconds;

                stepStopwatch.Restart();
                InitializeVideoDecoder();
                LastOpenVideoDecoderInitializationMilliseconds = stepStopwatch.Elapsed.TotalMilliseconds;
                BeginDecodeSegment(frameIndexAbsolute: true);

                cancellationToken.ThrowIfCancellationRequested();
                stepStopwatch.Restart();
                var firstFrame = ReadNextDisplayableFrame(cancellationToken);
                LastOpenFirstFrameDecodeMilliseconds = stepStopwatch.Elapsed.TotalMilliseconds;
                if (firstFrame == null)
                {
                    throw new InvalidOperationException("No displayable video frame could be decoded from the selected stream.");
                }

                RefreshDecodedFrameBudgetState(
                    isOpen: true,
                    approximateFrameBytes: firstFrame.ApproximateByteCount,
                    gpuActive: IsGpuActive);
                _frameCache.Reset(firstFrame);
                SetCurrentFrame(firstFrame);
                RecordNoCacheRefill("open-landed-no-warm", afterLanding: false);
                LastOpenInitialCacheWarmMilliseconds = 0d;
                IsMediaOpen = true;
                IsPlaying = false;
                LastOperationUsedGlobalIndex = false;
                LastAnchorFrameIndex = 0L;
                LastAnchorStrategy = "stream-start-index-background";
                openStopwatch.Stop();
                LastOpenTotalMilliseconds = openStopwatch.Elapsed.TotalMilliseconds;
                StartGlobalFrameIndexBuild(filePath, _videoStreamIndex);

                OnStateChanged();
                OnFramePresented(_currentFrame);
                return Task.CompletedTask;
            }
            catch (OperationCanceledException)
            {
                CloseCore(clearFilePath: true, clearErrorMessage: true);
                throw;
            }
            catch (Exception ex)
            {
                _lastErrorMessage = ex.Message;
                CloseCore(clearFilePath: true, clearErrorMessage: false);
                OnStateChanged();
                throw;
            }
        }

        public Task CloseAsync()
        {
            ThrowIfDisposed();
            StopPlayback(raiseStateChanged: false);
            CloseCore(clearFilePath: true, clearErrorMessage: true);
            OnStateChanged();
            return Task.CompletedTask;
        }

        public Task PlayAsync()
        {
            ThrowIfDisposed();

            if (!IsMediaOpen || _currentFrame == null)
            {
                return Task.CompletedTask;
            }

            lock (_playbackSync)
            {
                if (IsPlaying)
                {
                    return Task.CompletedTask;
                }
            }

            RealignPlaybackStartStateIfNeeded();

            lock (_playbackSync)
            {
                if (IsPlaying)
                {
                    return Task.CompletedTask;
                }

                _lastErrorMessage = string.Empty;
                LastFrameSeekWasCacheHit = false;
                LastOperationUsedGlobalIndex = false;
                LastAnchorStrategy = "playback-start";
                LastAnchorFrameIndex = GetAbsoluteFrameIndex(_currentFrame);
                LastPlaybackUsedAudioClock = false;

                var cancellationSource = new CancellationTokenSource();
                _lastAudioSubmittedBytes = 0L;
                _lastAudioErrorMessage = string.Empty;
                _playbackCancellationSource = cancellationSource;
                _audioPlaybackSession = TryStartAudioPlayback(_currentFrame.Descriptor.PresentationTime, cancellationSource.Token);
                _playbackTask = Task.Run(() => PlaybackLoop(cancellationSource.Token, cancellationSource));
                IsPlaying = true;
            }

            OnStateChanged();
            return Task.CompletedTask;
        }

        public Task PauseAsync()
        {
            ThrowIfDisposed();

            if (!IsMediaOpen)
            {
                return Task.CompletedTask;
            }

            StopPlayback(raiseStateChanged: false);
            LastFrameSeekWasCacheHit = false;
            LastOperationUsedGlobalIndex = false;
            LastAnchorFrameIndex = null;
            LastAnchorStrategy = string.Empty;
            OnStateChanged();
            return Task.CompletedTask;
        }

        public Task<FrameStepResult> StepForwardAsync(CancellationToken cancellationToken = default(CancellationToken))
        {
            ThrowIfDisposed();
            cancellationToken.ThrowIfCancellationRequested();

            if (!IsMediaOpen)
            {
                return Task.FromResult(FrameStepResult.Failed(+1, ReviewPosition.Empty, "No media is open.", false));
            }

            StopPlayback(raiseStateChanged: false);

            DecodedFrameBuffer nextFrame;
            if (_frameCache.TryMoveNext(out nextFrame))
            {
                LastFrameAdvanceWasCacheHit = true;
                IsPlaying = false;
                SetCurrentFrame(nextFrame);
                RecordNoCacheRefill("step-forward-cache-hit", afterLanding: true);
                SetOperationInstrumentation(
                    IsGlobalFrameIndexAvailable && nextFrame.Descriptor.IsFrameIndexAbsolute,
                    "cache",
                    nextFrame.Descriptor.IsFrameIndexAbsolute ? nextFrame.Descriptor.FrameIndex : null);
                OnStateChanged();
                OnFramePresented(_currentFrame);
                return Task.FromResult(FrameStepResult.Succeeded(
                    +1,
                    _position,
                    true,
                    "Advanced to the next cached decoded frame."));
            }

            nextFrame = ReadNextDisplayableFrame(cancellationToken);
            if (nextFrame == null)
            {
                LastFrameAdvanceWasCacheHit = false;
                return Task.FromResult(FrameStepResult.Failed(
                    +1,
                    _position,
                    "No later displayable frame is available.",
                    false));
            }

            LastFrameAdvanceWasCacheHit = false;
            IsPlaying = false;
            _frameCache.AppendForwardAndAdvance(nextFrame);
            SetCurrentFrame(_frameCache.Current);
            PrimeForwardCache(cancellationToken, "step-forward-decode", afterLanding: true);
            SetOperationInstrumentation(
                IsGlobalFrameIndexAvailable && _currentFrame.Descriptor.IsFrameIndexAbsolute,
                "decode-forward",
                _currentFrame.Descriptor.IsFrameIndexAbsolute ? _currentFrame.Descriptor.FrameIndex : null);
            OnStateChanged();
            OnFramePresented(_currentFrame);
            return Task.FromResult(FrameStepResult.Succeeded(
                +1,
                _position,
                false,
                "Advanced by decoding the next displayable frame."));
        }

        public Task<FrameStepResult> StepBackwardAsync(CancellationToken cancellationToken = default(CancellationToken))
        {
            ThrowIfDisposed();
            cancellationToken.ThrowIfCancellationRequested();

            if (!IsMediaOpen)
            {
                return Task.FromResult(FrameStepResult.Failed(-1, ReviewPosition.Empty, "No media is open.", false, false));
            }

            StopPlayback(raiseStateChanged: false);

            if (_currentFrame == null)
            {
                return Task.FromResult(FrameStepResult.Failed(
                    -1,
                    _position,
                    "No current decoded frame is available.",
                    false,
                    false));
            }

            DecodedFrameBuffer previousFrame;
            if (_frameCache.TryMovePrevious(out previousFrame))
            {
                LastFrameAdvanceWasCacheHit = true;
                SetCurrentFrame(previousFrame);
                IsPlaying = false;
                RecordNoCacheRefill("step-backward-cache-hit", afterLanding: true);
                SetOperationInstrumentation(
                    IsGlobalFrameIndexAvailable && previousFrame.Descriptor.IsFrameIndexAbsolute,
                    "cache",
                    previousFrame.Descriptor.IsFrameIndexAbsolute ? previousFrame.Descriptor.FrameIndex : null);
                OnStateChanged();
                OnFramePresented(_currentFrame);
                return Task.FromResult(FrameStepResult.Succeeded(
                    -1,
                    _position,
                    true,
                    false,
                    "Moved to the previous cached decoded frame."));
            }

            var currentAbsoluteFrameIndex = GetAbsoluteFrameIndex(_currentFrame);
            if (IsGlobalFrameIndexAvailable && currentAbsoluteFrameIndex.HasValue)
            {
                if (currentAbsoluteFrameIndex.Value <= 0L)
                {
                    SetOperationInstrumentation(true, "global-index-first-frame", 0L);
                    return Task.FromResult(FrameStepResult.Failed(
                        -1,
                        _position,
                        "Already at the first displayable frame.",
                        false,
                        false));
                }

                FfmpegGlobalFrameIndexEntry previousEntry;
                if (_globalFrameIndex.TryGetByAbsoluteFrameIndex(currentAbsoluteFrameIndex.Value - 1L, out previousEntry))
                {
                    var indexedWindow = MaterializeIndexedFrameWindow(previousEntry, cancellationToken);
                    if (indexedWindow != null)
                    {
                        _frameCache.LoadWindow(indexedWindow.WindowFrames, indexedWindow.CurrentIndex);
                        SetCurrentFrame(_frameCache.Current);
                        LastFrameAdvanceWasCacheHit = false;
                        IsPlaying = false;
                        RecordNoCacheRefill("step-backward-indexed-window", afterLanding: true);
                        SetOperationInstrumentation(true, previousEntry.SeekAnchorStrategy, previousEntry.SeekAnchorFrameIndex);
                        OnStateChanged();
                        OnFramePresented(_currentFrame);
                        return Task.FromResult(FrameStepResult.Succeeded(
                            -1,
                            _position,
                            false,
                            true,
                            "Moved to the previous decoded frame using the global frame index anchor."));
                    }
                }
            }

            var originalCurrentFrame = _currentFrame;
            var reconstructionSeekTimestamp = FindBackwardReconstructionSeekTimestamp(originalCurrentFrame);
            var reconstruction = ReconstructPreviousFrameWindow(
                originalCurrentFrame,
                reconstructionSeekTimestamp,
                cancellationToken);

            // Some indexed formats can seek back to the current keyframe when we ask for the
            // timestamp immediately before the current frame. Retry from stream start before
            // reporting "already at first frame" so the result stays truthful.
            if ((reconstruction == null || !reconstruction.HasPreviousFrame) && reconstructionSeekTimestamp > 0L)
            {
                reconstruction = ReconstructPreviousFrameWindow(originalCurrentFrame, 0L, cancellationToken);
            }

            if (reconstruction == null)
            {
                throw new InvalidOperationException(
                    "The custom FFmpeg review engine could not reconstruct the previous-frame window.");
            }

            _frameCache.LoadWindow(reconstruction.WindowFrames, reconstruction.CurrentIndex);
            SetCurrentFrame(_frameCache.Current);
            LastFrameAdvanceWasCacheHit = false;
            IsPlaying = false;
            RecordNoCacheRefill("step-backward-reconstruction", afterLanding: true);
            SetOperationInstrumentation(false, reconstructionSeekTimestamp > 0L ? "timestamp-backtrack" : StreamStartAnchorStrategy, null);

            if (!reconstruction.HasPreviousFrame)
            {
                return Task.FromResult(FrameStepResult.Failed(
                    -1,
                    _position,
                    "Already at the first displayable frame.",
                    false,
                    true));
            }

            OnStateChanged();
            OnFramePresented(_currentFrame);
            return Task.FromResult(FrameStepResult.Succeeded(
                -1,
                _position,
                false,
                true,
                "Moved to the previous decoded frame by seeking backward and reconstructing forward."));
        }

        public Task SeekToTimeAsync(TimeSpan position, CancellationToken cancellationToken = default(CancellationToken))
        {
            ThrowIfDisposed();
            cancellationToken.ThrowIfCancellationRequested();

            if (!IsMediaOpen)
            {
                return Task.CompletedTask;
            }

            StopPlayback(raiseStateChanged: false);
            ResetSeekPerformanceInstrumentation();
            var seekStopwatch = Stopwatch.StartNew();
            var stepStopwatch = Stopwatch.StartNew();

            var clampedTarget = ClampSeekTarget(position);
            LastSeekIndexWaitMilliseconds = stepStopwatch.Elapsed.TotalMilliseconds;

            var targetTimestamp = FfmpegNativeHelpers.ToStreamTimestamp(clampedTarget, _videoStreamTimeBase);
            FfmpegGlobalFrameIndexEntry indexedTargetEntry;
            bool landedAtOrAfterTarget;
            if (TryResolveIndexedSeekTarget(targetTimestamp, out indexedTargetEntry, out landedAtOrAfterTarget))
            {
                DecodedFrameBuffer cachedSeekFrame;
                if (_frameCache.TryMoveToAbsoluteFrameIndex(indexedTargetEntry.AbsoluteFrameIndex, out cachedSeekFrame))
                {
                    SetCurrentFrame(cachedSeekFrame);
                    _playbackStartNeedsDecoderRealignment = true;
                    LastSeekLandedAtOrAfterTarget = landedAtOrAfterTarget;
                    LastFrameAdvanceWasCacheHit = false;
                    LastFrameSeekWasCacheHit = false;
                    IsPlaying = false;
                    RecordNoCacheRefill("seek-time-cache-hit", afterLanding: true);
                    SetOperationInstrumentation(true, "cache-absolute-frame", indexedTargetEntry.AbsoluteFrameIndex);
                    seekStopwatch.Stop();
                    LastSeekMode = "cache-absolute-frame";
                    LastSeekTotalMilliseconds = seekStopwatch.Elapsed.TotalMilliseconds;
                    OnStateChanged();
                    OnFramePresented(_currentFrame);
                    return Task.CompletedTask;
                }

                stepStopwatch.Restart();
                var indexedWindow = MaterializeIndexedFrameWindow(indexedTargetEntry, cancellationToken, primeForwardFrames: false);
                LastSeekMaterializeMilliseconds = stepStopwatch.Elapsed.TotalMilliseconds;
                if (indexedWindow != null)
                {
                    _frameCache.LoadWindow(indexedWindow.WindowFrames, indexedWindow.CurrentIndex);
                    SetCurrentFrame(_frameCache.Current);
                    _playbackStartNeedsDecoderRealignment = false;
                    LastSeekLandedAtOrAfterTarget = landedAtOrAfterTarget;
                    LastFrameAdvanceWasCacheHit = false;
                    LastFrameSeekWasCacheHit = false;
                    IsPlaying = false;
                    RecordNoCacheRefill("seek-time-indexed-window", afterLanding: true);
                    SetOperationInstrumentation(true, indexedTargetEntry.SeekAnchorStrategy, indexedTargetEntry.SeekAnchorFrameIndex);
                    seekStopwatch.Stop();
                    LastSeekMode = "global-index-landed-no-forward-prime";
                    LastSeekTotalMilliseconds = seekStopwatch.Elapsed.TotalMilliseconds;
                    OnStateChanged();
                    OnFramePresented(_currentFrame);
                    return Task.CompletedTask;
                }
            }

            // Without a global index, this path lands the visible frame promptly and marks
            // its index as segment-local instead of blocking the UI on a full-file scan.
            // The UI must not display that segment-local number as a global frame number.
            var fallbackSeekTimestamp = targetTimestamp;
            var fallbackFrameIndexAbsolute = fallbackSeekTimestamp <= 0L;
            stepStopwatch.Restart();
            SeekToStreamTimestamp(fallbackSeekTimestamp, fallbackFrameIndexAbsolute);

            var landedFrame = DecodeFrameAtOrAfterTarget(clampedTarget, targetTimestamp, cancellationToken, out landedAtOrAfterTarget);
            LastSeekMaterializeMilliseconds = stepStopwatch.Elapsed.TotalMilliseconds;
            if (landedFrame == null)
            {
                throw new InvalidOperationException("No displayable frame could be decoded after seeking.");
            }

            _frameCache.Reset(landedFrame);
            SetCurrentFrame(landedFrame);
            _playbackStartNeedsDecoderRealignment = false;
            LastSeekForwardCacheWarmMilliseconds = 0d;
            LastSeekLandedAtOrAfterTarget = landedAtOrAfterTarget;
            LastFrameAdvanceWasCacheHit = false;
            LastFrameSeekWasCacheHit = false;
            IsPlaying = false;
            RecordNoCacheRefill("seek-time-pending-index", afterLanding: true);
            SetOperationInstrumentation(
                false,
                fallbackFrameIndexAbsolute
                    ? StreamStartAnchorStrategy
                    : "timestamp-seek-pending-index",
                null);
            seekStopwatch.Stop();
            LastSeekMode = fallbackFrameIndexAbsolute
                ? StreamStartAnchorStrategy
                : "timestamp-seek-pending-index";
            LastSeekTotalMilliseconds = seekStopwatch.Elapsed.TotalMilliseconds;
            OnStateChanged();
            OnFramePresented(_currentFrame);
            return Task.CompletedTask;
        }

        public Task SeekToFrameAsync(long frameIndex, CancellationToken cancellationToken = default(CancellationToken))
        {
            ThrowIfDisposed();
            cancellationToken.ThrowIfCancellationRequested();

            if (!IsMediaOpen)
            {
                return Task.CompletedTask;
            }

            StopPlayback(raiseStateChanged: false);

            var targetFrameIndex = Math.Max(0L, frameIndex);
            var requestedFrameIndex = targetFrameIndex;
            FfmpegGlobalFrameIndexEntry indexedTargetEntry = null;
            bool wasClampedToLastIndexedFrame = false;

            if (IsGlobalFrameIndexAvailable)
            {
                if (!_globalFrameIndex.TryGetByAbsoluteFrameIndex(targetFrameIndex, out indexedTargetEntry))
                {
                    FfmpegGlobalFrameIndexEntry lastIndexedFrame;
                    if (_globalFrameIndex.TryGetLastEntry(out lastIndexedFrame))
                    {
                        indexedTargetEntry = lastIndexedFrame;
                        targetFrameIndex = lastIndexedFrame.AbsoluteFrameIndex;
                        wasClampedToLastIndexedFrame = requestedFrameIndex > targetFrameIndex;
                    }
                }
            }

            DecodedFrameBuffer cachedFrame;
            if (_frameCache.TryMoveToAbsoluteFrameIndex(targetFrameIndex, out cachedFrame))
            {
                LastFrameSeekWasCacheHit = true;
                LastFrameAdvanceWasCacheHit = false;
                IsPlaying = false;
                SetCurrentFrame(cachedFrame);
                _playbackStartNeedsDecoderRealignment = true;
                RecordNoCacheRefill("seek-frame-cache-hit", afterLanding: true);
                SetOperationInstrumentation(
                    IsGlobalFrameIndexAvailable,
                    wasClampedToLastIndexedFrame ? "cache-absolute-frame-clamped" : "cache-absolute-frame",
                    cachedFrame.Descriptor.FrameIndex);
                OnStateChanged();
                OnFramePresented(_currentFrame);
                return Task.CompletedTask;
            }

            var reconstruction = indexedTargetEntry != null
                ? MaterializeIndexedFrameWindow(indexedTargetEntry, cancellationToken)
                : ReconstructAbsoluteFrameWindow(targetFrameIndex, cancellationToken);
            if (reconstruction == null)
            {
                throw new InvalidOperationException(string.Format(
                    System.Globalization.CultureInfo.InvariantCulture,
                    "The requested absolute frame index {0} is not available in this media.",
                    targetFrameIndex));
            }

            _frameCache.LoadWindow(reconstruction.WindowFrames, reconstruction.CurrentIndex);
            SetCurrentFrame(_frameCache.Current);
            _playbackStartNeedsDecoderRealignment = false;
            LastFrameSeekWasCacheHit = false;
            LastFrameAdvanceWasCacheHit = false;
            IsPlaying = false;
            RecordNoCacheRefill(
                indexedTargetEntry != null ? "seek-frame-indexed-window" : "seek-frame-stream-start",
                afterLanding: true);
            SetOperationInstrumentation(
                indexedTargetEntry != null,
                indexedTargetEntry != null
                    ? (wasClampedToLastIndexedFrame
                        ? indexedTargetEntry.SeekAnchorStrategy + "-clamped"
                        : indexedTargetEntry.SeekAnchorStrategy)
                    : StreamStartAnchorStrategy,
                indexedTargetEntry != null
                    ? (long?)indexedTargetEntry.SeekAnchorFrameIndex
                    : null);
            OnStateChanged();
            OnFramePresented(_currentFrame);
            return Task.CompletedTask;
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            StopPlaybackForDispose();
            CloseCore(clearFilePath: true, clearErrorMessage: true);
            _budgetCoordinator.AllocationChanged -= BudgetCoordinator_AllocationChanged;
            _budgetCoordinator.UnregisterPane(_paneId);
            _disposed = true;
        }

        private void StartGlobalFrameIndexBuild(string filePath, int videoStreamIndex)
        {
            if (string.IsNullOrWhiteSpace(filePath) || videoStreamIndex < 0)
            {
                return;
            }

            var cancellationSource = new CancellationTokenSource();
            int generation;
            lock (_indexBuildSync)
            {
                generation = ++_indexBuildGeneration;
                _indexBuildCancellationSource = cancellationSource;
                _isGlobalFrameIndexBuildInProgress = true;
                _globalFrameIndexStatus = "building";
                LastGlobalFrameIndexBuildMilliseconds = 0d;
            }

            var stopwatch = Stopwatch.StartNew();
            var buildTask = Task.Run(
                () => FfmpegGlobalFrameIndex.Build(filePath, videoStreamIndex, cancellationSource.Token),
                cancellationSource.Token);

            lock (_indexBuildSync)
            {
                if (generation == _indexBuildGeneration)
                {
                    _indexBuildTask = buildTask;
                }
            }

            buildTask.ContinueWith(
                task => CompleteGlobalFrameIndexBuild(generation, filePath, stopwatch, cancellationSource, task),
                CancellationToken.None,
                TaskContinuationOptions.ExecuteSynchronously,
                TaskScheduler.Default);
        }

        private void CompleteGlobalFrameIndexBuild(
            int generation,
            string filePath,
            Stopwatch stopwatch,
            CancellationTokenSource cancellationSource,
            Task<FfmpegGlobalFrameIndex> buildTask)
        {
            bool raiseStateChanged = false;
            try
            {
                stopwatch.Stop();
                lock (_indexBuildSync)
                {
                    if (generation != _indexBuildGeneration)
                    {
                        return;
                    }

                    _indexBuildTask = null;
                    _indexBuildCancellationSource = null;
                    _isGlobalFrameIndexBuildInProgress = false;
                    LastGlobalFrameIndexBuildMilliseconds = stopwatch.Elapsed.TotalMilliseconds;

                    if (buildTask.IsCanceled)
                    {
                        _globalFrameIndexStatus = "cancelled";
                    }
                    else if (buildTask.IsFaulted)
                    {
                        var exception = buildTask.Exception != null
                            ? buildTask.Exception.GetBaseException()
                            : null;
                        _globalFrameIndexStatus = "failed: " + (exception != null ? exception.Message : "unknown error");
                    }
                    else
                    {
                        _globalFrameIndex = buildTask.Result;
                        _globalFrameIndexStatus = IsGlobalFrameIndexAvailable
                            ? "ready"
                            : "empty";
                    }

                    raiseStateChanged = IsMediaOpen &&
                        string.Equals(filePath, _currentFilePath, StringComparison.OrdinalIgnoreCase);
                }
            }
            finally
            {
                cancellationSource.Dispose();
            }

            if (raiseStateChanged)
            {
                var normalizedCurrentFrame = TryNormalizeCachedFramesWithGlobalIndex();
                OnStateChanged();
                if (normalizedCurrentFrame)
                {
                    OnFramePresented(_currentFrame);
                }
            }
        }

        private void CancelGlobalFrameIndexBuild(bool resetStatus)
        {
            CancellationTokenSource cancellationSource;
            Task buildTask;
            lock (_indexBuildSync)
            {
                _indexBuildGeneration++;
                cancellationSource = _indexBuildCancellationSource;
                buildTask = _indexBuildTask;
                _indexBuildCancellationSource = null;
                _indexBuildTask = null;
                _isGlobalFrameIndexBuildInProgress = false;
                if (resetStatus)
                {
                    _globalFrameIndexStatus = "not-started";
                    LastGlobalFrameIndexBuildMilliseconds = 0d;
                }
            }

            if (cancellationSource == null)
            {
                return;
            }

            cancellationSource.Cancel();
            if (buildTask == null || buildTask.IsCompleted)
            {
                cancellationSource.Dispose();
                return;
            }

            buildTask.ContinueWith(
                _ => cancellationSource.Dispose(),
                CancellationToken.None,
                TaskContinuationOptions.ExecuteSynchronously,
                TaskScheduler.Default);
        }

        private bool TryNormalizeCachedFramesWithGlobalIndex()
        {
            if (!IsGlobalFrameIndexAvailable || !_frameCache.HasCurrent)
            {
                return false;
            }

            var changed = _frameCache.ReplaceFrames(frame =>
            {
                if (frame == null || frame.Descriptor == null || frame.Descriptor.IsFrameIndexAbsolute)
                {
                    return frame;
                }

                var indexedEntry = ResolveIndexedFrameIdentity(
                    frame.Descriptor.PresentationTimestamp,
                    frame.Descriptor.DecodeTimestamp);
                return indexedEntry != null
                    ? NormalizeFrameToIndexedEntry(frame, indexedEntry)
                    : frame;
            });

            if (changed && _frameCache.Current != null)
            {
                SetCurrentFrame(_frameCache.Current);
                if (_position.IsFrameIndexAbsolute)
                {
                    SetOperationInstrumentation(true, "background-index-normalized", _position.FrameIndex);
                }
            }

            return changed;
        }

        private void ResetOpenPerformanceInstrumentation()
        {
            LastOpenTotalMilliseconds = 0d;
            LastOpenContainerProbeMilliseconds = 0d;
            LastOpenStreamDiscoveryMilliseconds = 0d;
            LastOpenAudioProbeMilliseconds = 0d;
            LastOpenVideoDecoderInitializationMilliseconds = 0d;
            LastOpenFirstFrameDecodeMilliseconds = 0d;
            LastOpenInitialCacheWarmMilliseconds = 0d;
            LastGlobalFrameIndexBuildMilliseconds = 0d;
            _globalFrameIndexStatus = "not-started";
            ResetFrameTransferInstrumentation();
            ResetSeekPerformanceInstrumentation();
        }

        private void ResetSeekPerformanceInstrumentation()
        {
            LastSeekTotalMilliseconds = 0d;
            LastSeekIndexWaitMilliseconds = 0d;
            LastSeekMaterializeMilliseconds = 0d;
            LastSeekForwardCacheWarmMilliseconds = 0d;
            LastSeekMode = string.Empty;
            ResetCacheRefillInstrumentation();
        }

        private void ResetFrameTransferInstrumentation()
        {
            LastHardwareFrameTransferMilliseconds = 0d;
            LastBgraConversionMilliseconds = 0d;
        }

        private void PlaybackLoop(
            CancellationToken cancellationToken,
            CancellationTokenSource cancellationSource)
        {
            try
            {
                while (true)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    var currentFrame = _currentFrame;
                    if (currentFrame == null)
                    {
                        CompletePlaybackNaturally("playback-no-current-frame", cancellationSource);
                        return;
                    }

                    DecodedFrameBuffer nextFrame;
                    bool wasCacheHit;
                    if (!TryPreparePlaybackFrame(cancellationToken, out nextFrame, out wasCacheHit))
                    {
                        CompletePlaybackNaturally("playback-end-of-stream", cancellationSource);
                        return;
                    }

                    WaitForPlaybackFrameDue(currentFrame, nextFrame, cancellationToken);

                    PresentPlaybackFrame(nextFrame, wasCacheHit);
                    OnStateChanged();
                    OnFramePresented(_currentFrame);
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
            }
            catch (Exception ex)
            {
                _lastErrorMessage = ex.Message;
                lock (_playbackSync)
                {
                    IsPlaying = false;
                }

                OnStateChanged();
            }
            finally
            {
                ReleasePlaybackLoopState(cancellationSource);
            }
        }

        private void StopPlayback(bool raiseStateChanged)
        {
            Task playbackTask;
            CancellationTokenSource cancellationSource;
            FfmpegAudioPlaybackSession audioSession;
            bool wasPlaying;

            lock (_playbackSync)
            {
                playbackTask = _playbackTask;
                cancellationSource = _playbackCancellationSource;
                audioSession = _audioPlaybackSession;
                _audioPlaybackSession = null;
                wasPlaying = IsPlaying || (playbackTask != null && !playbackTask.IsCompleted);
                IsPlaying = false;

                if (cancellationSource != null && !cancellationSource.IsCancellationRequested)
                {
                    cancellationSource.Cancel();
                }
            }

            StopAudioPlaybackSession(audioSession);

            if (playbackTask != null)
            {
                try
                {
                    playbackTask.GetAwaiter().GetResult();
                }
                catch (OperationCanceledException)
                {
                }
            }

            lock (_playbackSync)
            {
                if (ReferenceEquals(_playbackTask, playbackTask))
                {
                    _playbackTask = null;
                }

                if (ReferenceEquals(_playbackCancellationSource, cancellationSource))
                {
                    _playbackCancellationSource = null;
                }
            }

            if (cancellationSource != null)
            {
                cancellationSource.Dispose();
            }

            if (raiseStateChanged && wasPlaying)
            {
                OnStateChanged();
            }
        }

        private void StopPlaybackForDispose()
        {
            Task playbackTask;
            CancellationTokenSource cancellationSource;
            FfmpegAudioPlaybackSession audioSession;

            lock (_playbackSync)
            {
                playbackTask = _playbackTask;
                cancellationSource = _playbackCancellationSource;
                audioSession = _audioPlaybackSession;
                _audioPlaybackSession = null;
                IsPlaying = false;

                if (cancellationSource != null && !cancellationSource.IsCancellationRequested)
                {
                    cancellationSource.Cancel();
                }
            }

            StopAudioPlaybackSession(audioSession);

            if (playbackTask != null)
            {
                try
                {
                    playbackTask.GetAwaiter().GetResult();
                }
                catch (OperationCanceledException)
                {
                }
            }

            lock (_playbackSync)
            {
                if (ReferenceEquals(_playbackTask, playbackTask))
                {
                    _playbackTask = null;
                }

                if (ReferenceEquals(_playbackCancellationSource, cancellationSource))
                {
                    _playbackCancellationSource = null;
                }
            }

            if (cancellationSource != null)
            {
                cancellationSource.Dispose();
            }
        }

        private void CompletePlaybackNaturally(string anchorStrategy, CancellationTokenSource cancellationSource)
        {
            FfmpegAudioPlaybackSession audioSession;
            lock (_playbackSync)
            {
                audioSession = _audioPlaybackSession;
                _audioPlaybackSession = null;
                IsPlaying = false;

                if (cancellationSource != null && !cancellationSource.IsCancellationRequested)
                {
                    cancellationSource.Cancel();
                }
            }

            StopAudioPlaybackSession(audioSession);
            SetOperationInstrumentation(
                IsGlobalFrameIndexAvailable && _position.IsFrameIndexAbsolute,
                anchorStrategy,
                _position.IsFrameIndexAbsolute ? _position.FrameIndex : null);
            OnStateChanged();
        }

        private void ReleasePlaybackLoopState(CancellationTokenSource cancellationSource)
        {
            lock (_playbackSync)
            {
                if (ReferenceEquals(_playbackCancellationSource, cancellationSource))
                {
                    _playbackCancellationSource = null;
                    _playbackTask = null;
                }
            }

            if (cancellationSource != null)
            {
                cancellationSource.Dispose();
            }
        }

        private FfmpegAudioPlaybackSession TryStartAudioPlayback(TimeSpan startPosition, CancellationToken cancellationToken)
        {
            if (_audioStreamInfo == null || !_audioStreamInfo.HasAudioStream || !_audioStreamInfo.DecoderAvailable)
            {
                LastPlaybackUsedAudioClock = false;
                return null;
            }

            try
            {
                var session = FfmpegAudioPlaybackSession.Start(_currentFilePath, startPosition, cancellationToken);
                LastPlaybackUsedAudioClock = session.IsActive;
                return session;
            }
            catch (Exception ex)
            {
                _lastAudioErrorMessage = ex.Message;
                LastPlaybackUsedAudioClock = false;
                return null;
            }
        }

        private void StopAudioPlaybackSession(FfmpegAudioPlaybackSession audioSession)
        {
            if (audioSession == null)
            {
                return;
            }

            _lastAudioSubmittedBytes = audioSession.SubmittedAudioBytes;
            audioSession.Dispose();
            _lastAudioSubmittedBytes = Math.Max(_lastAudioSubmittedBytes, audioSession.SubmittedAudioBytes);
            if (_lastAudioSubmittedBytes > 0L)
            {
                LastPlaybackUsedAudioClock = true;
            }

            if (!string.IsNullOrWhiteSpace(audioSession.LastErrorMessage))
            {
                _lastAudioErrorMessage = audioSession.LastErrorMessage;
            }
        }

        private void RealignPlaybackStartStateIfNeeded()
        {
            if (!_playbackStartNeedsDecoderRealignment || !IsMediaOpen || _currentFrame == null)
            {
                return;
            }

            var currentFrame = _currentFrame;
            var cancellationToken = CancellationToken.None;

            if (currentFrame.Descriptor != null &&
                currentFrame.Descriptor.IsFrameIndexAbsolute &&
                currentFrame.Descriptor.FrameIndex.HasValue &&
                IsGlobalFrameIndexAvailable)
            {
                FfmpegGlobalFrameIndexEntry indexedEntry;
                if (_globalFrameIndex.TryGetByAbsoluteFrameIndex(currentFrame.Descriptor.FrameIndex.Value, out indexedEntry))
                {
                    var indexedWindow = MaterializeIndexedFrameWindow(indexedEntry, cancellationToken);
                    if (indexedWindow != null)
                    {
                        _frameCache.LoadWindow(indexedWindow.WindowFrames, indexedWindow.CurrentIndex);
                        SetCurrentFrame(_frameCache.Current);
                        _playbackStartNeedsDecoderRealignment = false;
                        return;
                    }
                }
            }

            var target = currentFrame.Descriptor != null
                ? currentFrame.Descriptor.PresentationTime
                : _position.PresentationTime;
            var targetTimestamp = FfmpegNativeHelpers.ToStreamTimestamp(target, _videoStreamTimeBase);
            SeekToStreamTimestamp(targetTimestamp, targetTimestamp <= 0L);

            bool landedAtOrAfterTarget;
            var landedFrame = DecodeFrameAtOrAfterTarget(target, targetTimestamp, cancellationToken, out landedAtOrAfterTarget);
            if (landedFrame == null)
            {
                throw new InvalidOperationException("No displayable frame could be decoded while realigning playback state.");
            }

            _frameCache.Reset(landedFrame);
            SetCurrentFrame(landedFrame);
            PrimeForwardCache(cancellationToken, "playback-start-realign", afterLanding: true);
            _playbackStartNeedsDecoderRealignment = false;
        }

        private bool TryPreparePlaybackFrame(
            CancellationToken cancellationToken,
            out DecodedFrameBuffer frame,
            out bool wasCacheHit)
        {
            if (_frameCache.TryPeekNext(out frame))
            {
                wasCacheHit = true;
                return true;
            }

            frame = ReadNextDisplayableFrame(cancellationToken);
            wasCacheHit = false;
            return frame != null;
        }

        private void PresentPlaybackFrame(DecodedFrameBuffer preparedFrame, bool wasCacheHit)
        {
            if (preparedFrame == null)
            {
                throw new ArgumentNullException(nameof(preparedFrame));
            }

            DecodedFrameBuffer frameToPresent;
            if (wasCacheHit)
            {
                if (!_frameCache.TryMoveNext(out frameToPresent))
                {
                    throw new InvalidOperationException("The decoded frame cache could not advance during playback.");
                }
            }
            else
            {
                frameToPresent = _frameCache.AppendForwardAndAdvance(preparedFrame);
            }

            SetCurrentFrame(frameToPresent);
            LastFrameAdvanceWasCacheHit = wasCacheHit;
            LastFrameSeekWasCacheHit = false;
            RecordNoCacheRefill(wasCacheHit ? "playback-cache-hit" : "playback-decode", afterLanding: true);
            SetOperationInstrumentation(
                IsGlobalFrameIndexAvailable && frameToPresent.Descriptor.IsFrameIndexAbsolute,
                wasCacheHit ? "playback-cache" : "playback-decode",
                frameToPresent.Descriptor.IsFrameIndexAbsolute ? frameToPresent.Descriptor.FrameIndex : null);
        }

        private TimeSpan CalculatePlaybackDelay(
            DecodedFrameBuffer currentFrame,
            DecodedFrameBuffer nextFrame)
        {
            if (currentFrame != null && nextFrame != null)
            {
                var presentationDelta = nextFrame.Descriptor.PresentationTime - currentFrame.Descriptor.PresentationTime;
                if (presentationDelta > TimeSpan.Zero)
                {
                    return presentationDelta;
                }

                var currentDuration = GetFrameDuration(currentFrame.Descriptor);
                if (currentDuration > TimeSpan.Zero)
                {
                    return currentDuration;
                }

                var nextDuration = GetFrameDuration(nextFrame.Descriptor);
                if (nextDuration > TimeSpan.Zero)
                {
                    return nextDuration;
                }
            }

            var fallbackDelay = _mediaInfo.PositionStep > TimeSpan.Zero
                ? _mediaInfo.PositionStep
                : FfmpegNativeHelpers.GetPositionStep(_nominalFrameRate);
            return fallbackDelay > TimeSpan.Zero ? fallbackDelay : MinimumPlaybackDelay;
        }

        private void WaitForPlaybackFrameDue(
            DecodedFrameBuffer currentFrame,
            DecodedFrameBuffer nextFrame,
            CancellationToken cancellationToken)
        {
            if (TryWaitForPlaybackFrameDueUsingAudioClock(nextFrame, cancellationToken))
            {
                return;
            }

            LastPlaybackUsedAudioClock = LastPlaybackUsedAudioClock && _lastAudioSubmittedBytes > 0L;
            var delay = CalculatePlaybackDelay(currentFrame, nextFrame);
            if (cancellationToken.WaitHandle.WaitOne(delay))
            {
                cancellationToken.ThrowIfCancellationRequested();
            }
        }

        private bool TryWaitForPlaybackFrameDueUsingAudioClock(
            DecodedFrameBuffer nextFrame,
            CancellationToken cancellationToken)
        {
            var audioSession = _audioPlaybackSession;
            if (audioSession == null || !audioSession.IsActive || nextFrame == null)
            {
                return false;
            }

            LastPlaybackUsedAudioClock = true;
            _lastAudioSubmittedBytes = Math.Max(_lastAudioSubmittedBytes, audioSession.SubmittedAudioBytes);
            var waitStopwatch = Stopwatch.StartNew();
            var lastObservedAudioPosition = audioSession.PlaybackPosition;
            var lastObservedAudioAdvance = waitStopwatch.Elapsed;
            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();
                _lastAudioSubmittedBytes = Math.Max(_lastAudioSubmittedBytes, audioSession.SubmittedAudioBytes);

                var audioPosition = audioSession.PlaybackPosition;
                if (audioPosition > lastObservedAudioPosition + AudioClockProgressTolerance)
                {
                    lastObservedAudioPosition = audioPosition;
                    lastObservedAudioAdvance = waitStopwatch.Elapsed;
                }

                var remaining = nextFrame.Descriptor.PresentationTime - audioPosition;
                if (remaining <= TimeSpan.Zero)
                {
                    return true;
                }

                if (waitStopwatch.Elapsed - lastObservedAudioAdvance >= AudioClockStallTolerance)
                {
                    return false;
                }

                var wait = remaining > TimeSpan.FromMilliseconds(10d)
                    ? TimeSpan.FromMilliseconds(10d)
                    : remaining;
                if (cancellationToken.WaitHandle.WaitOne(wait))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                }
            }
        }

        private TimeSpan GetFrameDuration(FrameDescriptor descriptor)
        {
            if (descriptor == null || !descriptor.DurationTimestamp.HasValue || descriptor.DurationTimestamp.Value <= 0L)
            {
                return TimeSpan.Zero;
            }

            var duration = FfmpegNativeHelpers.ToTimeSpan(descriptor.DurationTimestamp.Value, _videoStreamTimeBase);
            return duration > TimeSpan.Zero ? duration : TimeSpan.Zero;
        }

        private void OpenContainer(string filePath)
        {
            var formatContext = _formatContext;
            FfmpegNativeHelpers.ThrowIfError(
                FfmpegNativeHelpers.OpenInput(&formatContext, filePath, null, null),
                "Open media container");
            _formatContext = formatContext;
            FfmpegNativeHelpers.ThrowIfError(
                ffmpeg.avformat_find_stream_info(_formatContext, null),
                "Probe media streams");
        }

        private void FindPrimaryVideoStream()
        {
            AVCodec* decoder = null;
            var bestStreamIndex = ffmpeg.av_find_best_stream(
                _formatContext,
                AVMediaType.AVMEDIA_TYPE_VIDEO,
                -1,
                -1,
                &decoder,
                0);
            FfmpegNativeHelpers.ThrowIfError(bestStreamIndex, "Select primary video stream");

            _videoStreamIndex = bestStreamIndex;
            _videoStream = _formatContext->streams[_videoStreamIndex];
            _videoStreamTimeBase = _videoStream->time_base;
            _nominalFrameRate = FfmpegNativeHelpers.GetNominalFrameRate(_formatContext, _videoStream, null);

            if (decoder == null)
            {
                decoder = ffmpeg.avcodec_find_decoder(_videoStream->codecpar->codec_id);
            }

            if (decoder == null)
            {
                throw new InvalidOperationException("No decoder is available for the selected video stream.");
            }

            _videoDecoder = decoder;
            CreateCodecContext();
        }

        private void CreateCodecContext()
        {
            ReleaseCodecContext();

            if (_videoDecoder == null)
            {
                throw new InvalidOperationException("No decoder is available for the selected video stream.");
            }

            _codecContext = ffmpeg.avcodec_alloc_context3(_videoDecoder);
            if (_codecContext == null)
            {
                throw new InvalidOperationException("Could not allocate the FFmpeg codec context.");
            }

            FfmpegNativeHelpers.ThrowIfError(
                ffmpeg.avcodec_parameters_to_context(_codecContext, _videoStream->codecpar),
                "Copy codec parameters");

            _codecContext->pkt_timebase = _videoStreamTimeBase;
            _codecContext->framerate = _nominalFrameRate;
        }

        private void ProbePrimaryAudioStream()
        {
            _audioStreamInfo = FfmpegAudioStreamInfo.None;
            _lastAudioErrorMessage = string.Empty;

            AVCodec* decoder = null;
            var bestStreamIndex = ffmpeg.av_find_best_stream(
                _formatContext,
                AVMediaType.AVMEDIA_TYPE_AUDIO,
                -1,
                _videoStreamIndex,
                &decoder,
                0);
            if (bestStreamIndex < 0)
            {
                return;
            }

            var audioStream = _formatContext->streams[bestStreamIndex];
            if (decoder == null)
            {
                decoder = ffmpeg.avcodec_find_decoder(audioStream->codecpar->codec_id);
            }

            _audioStreamInfo = new FfmpegAudioStreamInfo(
                true,
                decoder != null,
                bestStreamIndex,
                FfmpegNativeHelpers.GetCodecName(audioStream->codecpar->codec_id),
                audioStream->codecpar->sample_rate,
                audioStream->codecpar->ch_layout.nb_channels,
                audioStream->codecpar->bit_rate > 0 ? (long?)audioStream->codecpar->bit_rate : null,
                FfmpegNativeHelpers.GetBitDepth(null, audioStream->codecpar, AVPixelFormat.AV_PIX_FMT_NONE));

            if (decoder == null)
            {
                _lastAudioErrorMessage = "No decoder is available for the selected audio stream.";
            }
        }

        private void InitializeVideoDecoder()
        {
            var attemptedHardwareDecode = TryConfigureHardwareDecode();
            var openDecoderResult = ffmpeg.avcodec_open2(_codecContext, _videoDecoder, null);
            if (openDecoderResult < 0 && attemptedHardwareDecode)
            {
                GpuFallbackReason = "Vulkan decode setup failed during decoder open: " +
                    FfmpegNativeHelpers.GetErrorMessage(openDecoderResult);
                GpuCapabilityStatus = "fallback-cpu";
                CreateCodecContext();
                ResetHardwareDecodeState(clearFallbackReason: false);
                openDecoderResult = ffmpeg.avcodec_open2(_codecContext, _videoDecoder, null);
            }

            FfmpegNativeHelpers.ThrowIfError(openDecoderResult, "Open video decoder");
            _packet = ffmpeg.av_packet_alloc();
            if (_packet == null)
            {
                throw new InvalidOperationException("Could not allocate an FFmpeg packet.");
            }

            _decodedFrame = ffmpeg.av_frame_alloc();
            if (_decodedFrame == null)
            {
                throw new InvalidOperationException("Could not allocate an FFmpeg frame.");
            }

            _softwareFrame = ffmpeg.av_frame_alloc();
            if (_softwareFrame == null)
            {
                throw new InvalidOperationException("Could not allocate an FFmpeg software transfer frame.");
            }

            _frameConverter = new FfmpegFrameConverter();
        }

        private FfmpegReviewEngineOptions GetCurrentOptions()
        {
            return _optionsProvider != null
                ? _optionsProvider.GetCurrent()
                : FfmpegReviewEngineOptions.Default;
        }

        private void ResetHardwareDecodeDiagnostics()
        {
            ResetHardwareDecodeState(clearFallbackReason: true);
            ActiveDecodeBackend = "ffmpeg-cpu";
            GpuCapabilityStatus = _currentOpenOptions != null &&
                _currentOpenOptions.GpuBackendPreference == GpuBackendPreference.Disabled
                ? "disabled"
                : "not-requested";
            OperationalQueueDepth = CpuOperationalQueueDepth;
        }

        private void ResetHardwareDecodeState(bool clearFallbackReason)
        {
            _hardwareDecodeFormatSelected = false;
            IsGpuActive = false;
            _hardwarePixelFormat = AVPixelFormat.AV_PIX_FMT_NONE;

            if (_codecContext != null)
            {
                if (_codecContext->hw_device_ctx != null)
                {
                    var deviceContext = _codecContext->hw_device_ctx;
                    ffmpeg.av_buffer_unref(&deviceContext);
                    _codecContext->hw_device_ctx = null;
                }
            }

            if (_hardwareDeviceContext != null)
            {
                var hardwareDeviceContext = _hardwareDeviceContext;
                ffmpeg.av_buffer_unref(&hardwareDeviceContext);
                _hardwareDeviceContext = null;
            }

            if (clearFallbackReason)
            {
                GpuFallbackReason = string.Empty;
            }
        }

        private bool TryConfigureHardwareDecode()
        {
            ResetHardwareDecodeState(clearFallbackReason: true);

            if (_currentOpenOptions == null ||
                _currentOpenOptions.GpuBackendPreference == GpuBackendPreference.Disabled)
            {
                GpuCapabilityStatus = "disabled";
                return false;
            }

            if (_videoDecoder == null || _codecContext == null)
            {
                GpuCapabilityStatus = "unavailable";
                return false;
            }

            AVCodecHWConfig* hardwareConfiguration = null;
            for (var configIndex = 0; ; configIndex++)
            {
                var candidate = ffmpeg.avcodec_get_hw_config(_videoDecoder, configIndex);
                if (candidate == null)
                {
                    break;
                }

                if (candidate->device_type != AVHWDeviceType.AV_HWDEVICE_TYPE_VULKAN)
                {
                    continue;
                }

                if ((candidate->methods & (AvCodecHwConfigMethodHwDeviceContext | AvCodecHwConfigMethodHwFramesContext)) == 0)
                {
                    continue;
                }

                hardwareConfiguration = candidate;
                break;
            }

            if (hardwareConfiguration == null)
            {
                GpuCapabilityStatus = "no-vulkan-config";
                GpuFallbackReason = "FFmpeg does not advertise a Vulkan hardware decode path for this codec on the current runtime.";
                return false;
            }

            AVBufferRef* hardwareDeviceContext = null;
            string hardwareDeviceErrorMessage;
            if (!FfmpegHardwareDeviceCache.TryAcquireVulkanDevice(out hardwareDeviceContext, out hardwareDeviceErrorMessage))
            {
                GpuCapabilityStatus = "probe-failed";
                GpuFallbackReason = "Could not acquire a Vulkan FFmpeg device: " + hardwareDeviceErrorMessage;
                return false;
            }

            _hardwareDeviceContext = hardwareDeviceContext;
            _hardwarePixelFormat = hardwareConfiguration->pix_fmt;
            _codecContext->get_format = _hardwareGetFormatCallback;
            _codecContext->hw_device_ctx = ffmpeg.av_buffer_ref(_hardwareDeviceContext);
            GpuCapabilityStatus = "probe-ready";
            return true;
        }

        private AVPixelFormat SelectHardwarePixelFormat(AVCodecContext* codecContext, AVPixelFormat* pixelFormats)
        {
            if (pixelFormats == null)
            {
                GpuCapabilityStatus = "fallback-cpu";
                if (string.IsNullOrWhiteSpace(GpuFallbackReason))
                {
                    GpuFallbackReason = "The decoder did not expose any usable output pixel formats.";
                }

                return AVPixelFormat.AV_PIX_FMT_NONE;
            }

            for (var candidate = pixelFormats; *candidate != AVPixelFormat.AV_PIX_FMT_NONE; candidate++)
            {
                if (*candidate == _hardwarePixelFormat)
                {
                    _hardwareDecodeFormatSelected = true;
                    GpuCapabilityStatus = "surface-selected";
                    return *candidate;
                }
            }

            GpuCapabilityStatus = "fallback-cpu";
            if (string.IsNullOrWhiteSpace(GpuFallbackReason))
            {
                GpuFallbackReason = "The decoder did not expose the Vulkan surface format for this stream, so Frame Player stayed on CPU decode.";
            }

            return *pixelFormats;
        }

        private void RefreshDecodedFrameBudgetState(bool isOpen, int approximateFrameBytes, bool gpuActive)
        {
            var allocation = _budgetCoordinator.UpdatePaneState(
                _paneId,
                isOpen,
                gpuActive,
                ResolveActualDecodeBackend(gpuActive),
                approximateFrameBytes,
                ResolveOperationalQueueDepth(gpuActive),
                _currentOpenOptions != null ? _currentOpenOptions.CacheBudgetOverrideMegabytes : null);
            ApplyDecodedFrameBudget(allocation);
        }

        private void ApplyDecodedFrameBudget(PaneBudgetAllocation allocation)
        {
            if (allocation == null)
            {
                return;
            }

            _budgetBand = allocation.BudgetBand;
            _hostResourceClass = allocation.HostResourceClass;
            _sessionDecodedFrameCacheBudgetBytes = allocation.SessionBudgetBytes;
            _configuredCacheBudgetBytes = allocation.PaneBudgetBytes;
            _maxPreviousCachedFrameCount = allocation.PreviousFrameTarget;
            _maxForwardCachedFrameCount = allocation.ForwardFrameTarget;
            OperationalQueueDepth = allocation.QueueDepth;
            if (!string.IsNullOrWhiteSpace(allocation.ActualDecodeBackend))
            {
                ActiveDecodeBackend = allocation.ActualDecodeBackend;
            }

            _frameCache.UpdateLimits(_maxPreviousCachedFrameCount, _maxForwardCachedFrameCount);
        }

        private void BudgetCoordinator_AllocationChanged(object sender, PaneBudgetAllocationChangedEventArgs e)
        {
            if (e == null ||
                e.Allocation == null ||
                !string.Equals(e.Allocation.PaneId, _paneId, StringComparison.Ordinal))
            {
                return;
            }

            ApplyDecodedFrameBudget(e.Allocation);
        }

        private static int ResolveOperationalQueueDepth(bool gpuActive)
        {
            return gpuActive ? GpuOperationalQueueDepth : CpuOperationalQueueDepth;
        }

        private static string ResolveActualDecodeBackend(bool gpuActive)
        {
            return gpuActive ? "ffmpeg-vulkan" : "ffmpeg-cpu";
        }

        private void BeginDecodeSegment(bool frameIndexAbsolute)
        {
            _segmentFrameIndexAbsolute = frameIndexAbsolute;
            _decodedSegmentFrameCount = 0L;
            _frameCache.Clear();
            _currentFrame = null;
            ResetDecodeReadState();
        }

        private void SeekToStreamTimestamp(long targetTimestamp, bool frameIndexAbsolute)
        {
            FfmpegNativeHelpers.ThrowIfError(
                ffmpeg.av_seek_frame(
                    _formatContext,
                    _videoStreamIndex,
                    targetTimestamp,
                    ffmpeg.AVSEEK_FLAG_BACKWARD),
                "Seek video stream");

            ffmpeg.avcodec_flush_buffers(_codecContext);
            BeginDecodeSegment(frameIndexAbsolute);
        }

        private void ResetDecodeReadState()
        {
            _hasPendingVideoPacket = false;
            _inputExhausted = false;
            _flushPacketSent = false;

            if (_packet != null)
            {
                ffmpeg.av_packet_unref(_packet);
            }

            if (_decodedFrame != null)
            {
                ffmpeg.av_frame_unref(_decodedFrame);
            }

            if (_softwareFrame != null)
            {
                ffmpeg.av_frame_unref(_softwareFrame);
            }
        }

        private DecodedFrameBuffer DecodeFrameAtOrAfterTarget(
            TimeSpan requestedPosition,
            long requestedTimestamp,
            CancellationToken cancellationToken,
            out bool landedAtOrAfterTarget)
        {
            DecodedFrameBuffer fallbackFrame = null;

            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var frame = ReadNextDisplayableFrame(cancellationToken);
                if (frame == null)
                {
                    landedAtOrAfterTarget = fallbackFrame != null && IsAtOrAfterRequestedTarget(fallbackFrame, requestedPosition, requestedTimestamp);
                    return fallbackFrame;
                }

                fallbackFrame = frame;
                if (IsAtOrAfterRequestedTarget(frame, requestedPosition, requestedTimestamp))
                {
                    landedAtOrAfterTarget = true;
                    return frame;
                }
            }
        }

        private void PrimeForwardCache(CancellationToken cancellationToken, string reason, bool afterLanding)
        {
            var startingForwardCount = _frameCache.ForwardCount;
            var stopwatch = Stopwatch.StartNew();
            while (_frameCache.ForwardCount < _maxForwardCachedFrameCount)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var frame = ReadNextDisplayableFrame(cancellationToken);
                if (frame == null)
                {
                    return;
                }

                _frameCache.AppendForward(frame);
            }

            stopwatch.Stop();
            LastCacheRefillMilliseconds = stopwatch.Elapsed.TotalMilliseconds;
            LastCacheRefillReason = reason ?? string.Empty;
            LastCacheRefillMode = LastCacheRefillMilliseconds > 0d ? "sync" : "none";
            LastCacheRefillWasSynchronous = LastCacheRefillMilliseconds > 0d;
            LastCacheRefillAfterLanding = afterLanding;
            LastCacheRefillStartingForwardCount = startingForwardCount;
            LastCacheRefillCompletedForwardCount = _frameCache.ForwardCount;
            LastCacheRefillCompletedPreviousCount = _frameCache.PreviousCount;
        }

        private void ResetCacheRefillInstrumentation()
        {
            LastCacheRefillMilliseconds = 0d;
            LastCacheRefillReason = string.Empty;
            LastCacheRefillMode = string.Empty;
            LastCacheRefillWasSynchronous = false;
            LastCacheRefillAfterLanding = false;
            LastCacheRefillStartingForwardCount = 0;
            LastCacheRefillCompletedForwardCount = 0;
            LastCacheRefillCompletedPreviousCount = 0;
        }

        private void RecordNoCacheRefill(string reason, bool afterLanding)
        {
            LastCacheRefillMilliseconds = 0d;
            LastCacheRefillReason = reason ?? string.Empty;
            LastCacheRefillMode = "none";
            LastCacheRefillWasSynchronous = false;
            LastCacheRefillAfterLanding = afterLanding;
            LastCacheRefillStartingForwardCount = _frameCache.ForwardCount;
            LastCacheRefillCompletedForwardCount = _frameCache.ForwardCount;
            LastCacheRefillCompletedPreviousCount = _frameCache.PreviousCount;
        }

        private bool TryResolveIndexedSeekTarget(
            long requestedTimestamp,
            out FfmpegGlobalFrameIndexEntry targetEntry,
            out bool landedAtOrAfterTarget)
        {
            landedAtOrAfterTarget = false;
            targetEntry = null;

            if (!IsGlobalFrameIndexAvailable)
            {
                return false;
            }

            if (_globalFrameIndex.TryGetFirstAtOrAfterTimestamp(requestedTimestamp, out targetEntry))
            {
                landedAtOrAfterTarget = true;
                return true;
            }

            if (_globalFrameIndex.TryGetLastEntry(out targetEntry))
            {
                landedAtOrAfterTarget = targetEntry.SearchTimestamp.HasValue &&
                    targetEntry.SearchTimestamp.Value >= requestedTimestamp;
                return true;
            }

            return false;
        }

        private FrameSeekWindowResult MaterializeIndexedFrameWindow(
            FfmpegGlobalFrameIndexEntry targetEntry,
            CancellationToken cancellationToken,
            bool primeForwardFrames = true)
        {
            if (targetEntry == null)
            {
                throw new ArgumentNullException(nameof(targetEntry));
            }

            var anchorEntry = ResolveIndexedAnchorEntry(targetEntry);
            SeekToStreamTimestamp(targetEntry.SeekAnchorTimestamp, targetEntry.SeekAnchorTimestamp <= 0L);

            var framesBeforeTarget = new List<DecodedFrameBuffer>(_maxPreviousCachedFrameCount);
            var anchorReached = anchorEntry != null &&
                anchorEntry.AbsoluteFrameIndex == 0L &&
                targetEntry.SeekAnchorTimestamp <= 0L;
            var nextAbsoluteFrameIndex = anchorReached && anchorEntry != null
                ? anchorEntry.AbsoluteFrameIndex
                : -1L;

            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var decodedFrame = ReadNextDisplayableFrame(cancellationToken);
                if (decodedFrame == null)
                {
                    return null;
                }

                if (!anchorReached)
                {
                    if (anchorEntry == null || !FrameMatchesIndexEntry(decodedFrame, anchorEntry))
                    {
                        continue;
                    }

                    anchorReached = true;
                    nextAbsoluteFrameIndex = anchorEntry.AbsoluteFrameIndex;
                }

                FfmpegGlobalFrameIndexEntry currentEntry;
                if (!_globalFrameIndex.TryGetByAbsoluteFrameIndex(nextAbsoluteFrameIndex, out currentEntry))
                {
                    return null;
                }

                var normalizedFrame = NormalizeFrameToIndexedEntry(decodedFrame, currentEntry);
                if (currentEntry.AbsoluteFrameIndex == targetEntry.AbsoluteFrameIndex)
                {
                    return BuildFrameSeekWindowResult(framesBeforeTarget, normalizedFrame, cancellationToken, primeForwardFrames);
                }

                framesBeforeTarget.Add(normalizedFrame);
                while (framesBeforeTarget.Count > _maxPreviousCachedFrameCount)
                {
                    framesBeforeTarget.RemoveAt(0);
                }

                nextAbsoluteFrameIndex++;
            }
        }

        private BackwardReconstructionResult ReconstructPreviousFrameWindow(
            DecodedFrameBuffer originalCurrentFrame,
            long seekTimestamp,
            CancellationToken cancellationToken)
        {
            SeekToStreamTimestamp(seekTimestamp, seekTimestamp <= 0L);

            var framesBeforeOriginal = new List<DecodedFrameBuffer>(_maxPreviousCachedFrameCount + 1);
            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var frame = ReadNextDisplayableFrame(cancellationToken);
                if (frame == null)
                {
                    return null;
                }

                if (FramesReferToSameDisplayFrame(frame, originalCurrentFrame))
                {
                    return BuildBackwardReconstructionResult(
                        framesBeforeOriginal,
                        frame,
                        cancellationToken);
                }

                framesBeforeOriginal.Add(frame);
                while (framesBeforeOriginal.Count > _maxPreviousCachedFrameCount + 1)
                {
                    framesBeforeOriginal.RemoveAt(0);
                }
            }
        }

        private FrameSeekWindowResult ReconstructAbsoluteFrameWindow(long targetFrameIndex, CancellationToken cancellationToken)
        {
            SeekToStreamTimestamp(0L, frameIndexAbsolute: true);

            var framesBeforeTarget = new List<DecodedFrameBuffer>(_maxPreviousCachedFrameCount);
            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var frame = ReadNextDisplayableFrame(cancellationToken);
                if (frame == null)
                {
                    return null;
                }

                if (frame.Descriptor.IsFrameIndexAbsolute &&
                    frame.Descriptor.FrameIndex.HasValue &&
                    frame.Descriptor.FrameIndex.Value == targetFrameIndex)
                {
                    return BuildFrameSeekWindowResult(framesBeforeTarget, frame, cancellationToken);
                }

                framesBeforeTarget.Add(frame);
                while (framesBeforeTarget.Count > _maxPreviousCachedFrameCount)
                {
                    framesBeforeTarget.RemoveAt(0);
                }
            }
        }

        private BackwardReconstructionResult BuildBackwardReconstructionResult(
            IList<DecodedFrameBuffer> framesBeforeOriginal,
            DecodedFrameBuffer matchedCurrentFrame,
            CancellationToken cancellationToken)
        {
            var windowFrames = new List<DecodedFrameBuffer>(framesBeforeOriginal.Count + _maxForwardCachedFrameCount + 1);
            int currentIndex;
            bool hasPreviousFrame = framesBeforeOriginal.Count > 0;
            var nextAbsoluteFrameIndex = GetAbsoluteFrameIndex(matchedCurrentFrame);

            if (hasPreviousFrame)
            {
                windowFrames.AddRange(framesBeforeOriginal);
                currentIndex = windowFrames.Count - 1;
                windowFrames.Add(matchedCurrentFrame);
            }
            else
            {
                windowFrames.Add(matchedCurrentFrame);
                currentIndex = 0;
            }

            while (windowFrames.Count - currentIndex - 1 < _maxForwardCachedFrameCount)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var nextFrame = ReadNextDisplayableFrame(cancellationToken);
                if (nextFrame == null)
                {
                    break;
                }

                if (nextAbsoluteFrameIndex.HasValue && IsGlobalFrameIndexAvailable)
                {
                    nextAbsoluteFrameIndex++;
                    FfmpegGlobalFrameIndexEntry indexedEntry;
                    if (_globalFrameIndex.TryGetByAbsoluteFrameIndex(nextAbsoluteFrameIndex.Value, out indexedEntry))
                    {
                        nextFrame = NormalizeFrameToIndexedEntry(nextFrame, indexedEntry);
                    }
                }

                windowFrames.Add(nextFrame);
            }

            return new BackwardReconstructionResult(windowFrames, currentIndex, hasPreviousFrame);
        }

        private FrameSeekWindowResult BuildFrameSeekWindowResult(
            IList<DecodedFrameBuffer> framesBeforeTarget,
            DecodedFrameBuffer targetFrame,
            CancellationToken cancellationToken,
            bool primeForwardFrames = true)
        {
            var windowFrames = new List<DecodedFrameBuffer>(framesBeforeTarget.Count + _maxForwardCachedFrameCount + 1);
            windowFrames.AddRange(framesBeforeTarget);
            windowFrames.Add(targetFrame);
            var currentIndex = windowFrames.Count - 1;
            var nextAbsoluteFrameIndex = GetAbsoluteFrameIndex(targetFrame);

            while (primeForwardFrames && windowFrames.Count - currentIndex - 1 < _maxForwardCachedFrameCount)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var nextFrame = ReadNextDisplayableFrame(cancellationToken);
                if (nextFrame == null)
                {
                    break;
                }

                if (nextAbsoluteFrameIndex.HasValue && IsGlobalFrameIndexAvailable)
                {
                    nextAbsoluteFrameIndex++;
                    FfmpegGlobalFrameIndexEntry indexedEntry;
                    if (_globalFrameIndex.TryGetByAbsoluteFrameIndex(nextAbsoluteFrameIndex.Value, out indexedEntry))
                    {
                        nextFrame = NormalizeFrameToIndexedEntry(nextFrame, indexedEntry);
                    }
                }

                windowFrames.Add(nextFrame);
            }

            return new FrameSeekWindowResult(windowFrames, currentIndex);
        }

        private FfmpegGlobalFrameIndexEntry ResolveIndexedAnchorEntry(FfmpegGlobalFrameIndexEntry targetEntry)
        {
            if (targetEntry == null || !IsGlobalFrameIndexAvailable)
            {
                return null;
            }

            FfmpegGlobalFrameIndexEntry anchorEntry;
            if (_globalFrameIndex.TryGetByAbsoluteFrameIndex(targetEntry.SeekAnchorFrameIndex, out anchorEntry))
            {
                return anchorEntry;
            }

            if (_globalFrameIndex.TryGetByAbsoluteFrameIndex(0L, out anchorEntry))
            {
                return anchorEntry;
            }

            return null;
        }

        private DecodedFrameBuffer NormalizeFrameToIndexedEntry(
            DecodedFrameBuffer frame,
            FfmpegGlobalFrameIndexEntry indexedEntry)
        {
            if (frame == null || indexedEntry == null)
            {
                return frame;
            }

            if (frame.Descriptor.IsFrameIndexAbsolute &&
                frame.Descriptor.FrameIndex.HasValue &&
                frame.Descriptor.FrameIndex.Value == indexedEntry.AbsoluteFrameIndex)
            {
                return frame;
            }

            var descriptor = new FrameDescriptor(
                indexedEntry.AbsoluteFrameIndex,
                indexedEntry.PresentationTime != TimeSpan.Zero
                    ? indexedEntry.PresentationTime
                    : frame.Descriptor.PresentationTime,
                frame.Descriptor.IsKeyFrame || indexedEntry.IsKeyFrame,
                true,
                frame.Descriptor.PixelWidth,
                frame.Descriptor.PixelHeight,
                frame.Descriptor.PixelFormatName,
                frame.Descriptor.SourcePixelFormatName,
                indexedEntry.PresentationTimestamp ?? frame.Descriptor.PresentationTimestamp,
                indexedEntry.DecodeTimestamp ?? frame.Descriptor.DecodeTimestamp,
                frame.Descriptor.DurationTimestamp,
                frame.Descriptor.DisplayWidth,
                frame.Descriptor.DisplayHeight);

            return frame.WithDescriptor(descriptor);
        }

        private DecodedFrameBuffer ReadNextDisplayableFrame(CancellationToken cancellationToken)
        {
            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var bufferedFrame = TryReceiveDecodedFrame(cancellationToken);
                if (bufferedFrame != null)
                {
                    return bufferedFrame;
                }

                if (_hasPendingVideoPacket)
                {
                    var sendPendingResult = ffmpeg.avcodec_send_packet(_codecContext, _packet);
                    if (sendPendingResult == ffmpeg.AVERROR(ffmpeg.EAGAIN))
                    {
                        continue;
                    }

                    FfmpegNativeHelpers.ThrowIfError(sendPendingResult, "Submit packet to decoder");
                    _hasPendingVideoPacket = false;
                    ffmpeg.av_packet_unref(_packet);
                    continue;
                }

                if (_inputExhausted)
                {
                    if (_flushPacketSent)
                    {
                        return null;
                    }

                    var flushResult = ffmpeg.avcodec_send_packet(_codecContext, null);
                    if (flushResult == ffmpeg.AVERROR(ffmpeg.EAGAIN))
                    {
                        continue;
                    }

                    if (flushResult == ffmpeg.AVERROR_EOF)
                    {
                        _flushPacketSent = true;
                        return null;
                    }

                    FfmpegNativeHelpers.ThrowIfError(flushResult, "Flush decoder at end of stream");
                    _flushPacketSent = true;
                    continue;
                }

                var readResult = ffmpeg.av_read_frame(_formatContext, _packet);
                if (readResult == ffmpeg.AVERROR_EOF)
                {
                    _inputExhausted = true;
                    continue;
                }

                FfmpegNativeHelpers.ThrowIfError(readResult, "Read encoded packet");
                if (_packet->stream_index != _videoStreamIndex)
                {
                    ffmpeg.av_packet_unref(_packet);
                    continue;
                }

                _hasPendingVideoPacket = true;
            }
        }

        private DecodedFrameBuffer TryReceiveDecodedFrame(CancellationToken cancellationToken)
        {
            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var receiveResult = ffmpeg.avcodec_receive_frame(_codecContext, _decodedFrame);
                if (receiveResult == ffmpeg.AVERROR(ffmpeg.EAGAIN) || receiveResult == ffmpeg.AVERROR_EOF)
                {
                    return null;
                }

                FfmpegNativeHelpers.ThrowIfError(receiveResult, "Decode video frame");

                try
                {
                    if (_decodedFrame->width <= 0 || _decodedFrame->height <= 0)
                    {
                        continue;
                    }

                    return BuildDecodedFrameBuffer(_decodedFrame);
                }
                finally
                {
                    ffmpeg.av_frame_unref(_decodedFrame);
                }
            }
        }

        private DecodedFrameBuffer BuildDecodedFrameBuffer(AVFrame* decodedFrame)
        {
            var sourceFrame = decodedFrame;
            LastHardwareFrameTransferMilliseconds = 0d;
            if (_hardwareDecodeFormatSelected &&
                _hardwarePixelFormat != AVPixelFormat.AV_PIX_FMT_NONE &&
                (AVPixelFormat)decodedFrame->format == _hardwarePixelFormat)
            {
                var transferStopwatch = Stopwatch.StartNew();
                ffmpeg.av_frame_unref(_softwareFrame);
                FfmpegNativeHelpers.ThrowIfError(
                    ffmpeg.av_hwframe_transfer_data(_softwareFrame, decodedFrame, 0),
                    "Transfer GPU frame to system memory");
                transferStopwatch.Stop();
                LastHardwareFrameTransferMilliseconds = transferStopwatch.Elapsed.TotalMilliseconds;
                sourceFrame = _softwareFrame;
                IsGpuActive = true;
                ActiveDecodeBackend = "ffmpeg-vulkan";
                GpuCapabilityStatus = "active";
            }

            ApplyVisibleFrameCrop(sourceFrame);

            var presentationTimestamp = GetPresentationTimestamp(decodedFrame);
            var decodeTimestamp = FfmpegNativeHelpers.AsNullableTimestamp(decodedFrame->pkt_dts);
            var durationTimestamp = decodedFrame->duration > 0
                ? (long?)decodedFrame->duration
                : FfmpegNativeHelpers.AsNullableTimestamp(decodedFrame->duration);
            _nominalFrameRate = FfmpegNativeHelpers.GetNominalFrameRate(_formatContext, _videoStream, decodedFrame);
            var indexedEntry = ResolveIndexedFrameIdentity(presentationTimestamp, decodeTimestamp);
            var resolvedPresentationTime = indexedEntry != null
                ? indexedEntry.PresentationTime
                : presentationTimestamp.HasValue
                    ? FfmpegNativeHelpers.ToTimeSpan(presentationTimestamp.Value, _videoStreamTimeBase)
                    : TimeSpan.Zero;
            var resolvedFrameIndex = indexedEntry != null
                ? (long?)indexedEntry.AbsoluteFrameIndex
                : _decodedSegmentFrameCount;
            var isFrameIndexAbsolute = indexedEntry != null || _segmentFrameIndexAbsolute;
            int displayWidth;
            int displayHeight;
            FfmpegNativeHelpers.GetDisplayDimensions(_formatContext, _videoStream, sourceFrame, out displayWidth, out displayHeight);

            var descriptor = new FrameDescriptor(
                resolvedFrameIndex,
                resolvedPresentationTime,
                indexedEntry != null ? indexedEntry.IsKeyFrame : (decodedFrame->flags & ffmpeg.AV_FRAME_FLAG_KEY) != 0,
                isFrameIndexAbsolute,
                sourceFrame->width,
                sourceFrame->height,
                OutputPixelFormatName,
                FfmpegNativeHelpers.GetPixelFormatName((AVPixelFormat)sourceFrame->format),
                indexedEntry != null ? indexedEntry.PresentationTimestamp : presentationTimestamp,
                indexedEntry != null ? indexedEntry.DecodeTimestamp : decodeTimestamp,
                durationTimestamp,
                displayWidth,
                displayHeight);

            var conversionStopwatch = Stopwatch.StartNew();
            var convertedFrame = _frameConverter.Convert(sourceFrame, descriptor);
            conversionStopwatch.Stop();
            LastBgraConversionMilliseconds = conversionStopwatch.Elapsed.TotalMilliseconds;
            _decodedSegmentFrameCount++;
            return convertedFrame;
        }

        private static void ApplyVisibleFrameCrop(AVFrame* sourceFrame)
        {
            if (sourceFrame == null ||
                (sourceFrame->crop_left | sourceFrame->crop_top | sourceFrame->crop_right | sourceFrame->crop_bottom) == 0)
            {
                return;
            }

            var cropResult = ffmpeg.av_frame_apply_cropping(sourceFrame, 0);
            if (cropResult < 0)
            {
                // Keep the uncropped frame if FFmpeg cannot apply the visible-frame crop safely.
                return;
            }
        }

        private void SetCurrentFrame(DecodedFrameBuffer frame)
        {
            _currentFrame = frame ?? throw new ArgumentNullException(nameof(frame));
            _mediaInfo = BuildMediaInfo(frame.Descriptor);
            _position = BuildReviewPosition(frame.Descriptor);
        }

        private VideoMediaInfo BuildMediaInfo(FrameDescriptor descriptor)
        {
            var duration = FfmpegNativeHelpers.GetDuration(_formatContext, _videoStream);
            var framesPerSecond = FfmpegNativeHelpers.ToDouble(_nominalFrameRate);
            var positionStep = FfmpegNativeHelpers.GetPositionStep(_nominalFrameRate);
            int displayAspectRatioNumerator;
            int displayAspectRatioDenominator;
            var hasDisplayAspectRatio = FfmpegNativeHelpers.TryReduceRatio(
                descriptor.DisplayWidth,
                descriptor.DisplayHeight,
                out displayAspectRatioNumerator,
                out displayAspectRatioDenominator);
            var sourcePixelFormat = GetInspectorSourcePixelFormat();
            var videoBitDepth = FfmpegNativeHelpers.GetBitDepth(
                _codecContext,
                _videoStream != null ? _videoStream->codecpar : null,
                sourcePixelFormat);

            return new VideoMediaInfo(
                _currentFilePath,
                duration,
                positionStep,
                framesPerSecond,
                descriptor.PixelWidth,
                descriptor.PixelHeight,
                FfmpegNativeHelpers.GetCodecName(_codecContext->codec_id),
                _videoStreamIndex,
                _nominalFrameRate.num,
                _nominalFrameRate.den,
                _videoStreamTimeBase.num,
                _videoStreamTimeBase.den,
                _audioStreamInfo != null && _audioStreamInfo.HasAudioStream,
                _audioStreamInfo != null && _audioStreamInfo.DecoderAvailable && string.IsNullOrWhiteSpace(_lastAudioErrorMessage),
                _audioStreamInfo != null ? _audioStreamInfo.CodecName : string.Empty,
                _audioStreamInfo != null ? _audioStreamInfo.StreamIndex : -1,
                _audioStreamInfo != null ? _audioStreamInfo.SampleRate : 0,
                _audioStreamInfo != null ? _audioStreamInfo.ChannelCount : 0,
                descriptor.DisplayWidth > 0 ? (int?)descriptor.DisplayWidth : null,
                descriptor.DisplayHeight > 0 ? (int?)descriptor.DisplayHeight : null,
                hasDisplayAspectRatio ? (int?)displayAspectRatioNumerator : null,
                hasDisplayAspectRatio ? (int?)displayAspectRatioDenominator : null,
                string.IsNullOrWhiteSpace(descriptor.SourcePixelFormatName) ? null : descriptor.SourcePixelFormatName,
                videoBitDepth,
                _videoStream != null && _videoStream->codecpar->bit_rate > 0 ? (long?)_videoStream->codecpar->bit_rate : null,
                GetColorMetadataValue(FfmpegNativeHelpers.GetColorSpaceName(_codecContext != null ? _codecContext->colorspace : default(AVColorSpace))),
                GetColorMetadataValue(FfmpegNativeHelpers.GetColorRangeName(_codecContext != null ? _codecContext->color_range : default(AVColorRange))),
                GetColorMetadataValue(FfmpegNativeHelpers.GetColorPrimariesName(_codecContext != null ? _codecContext->color_primaries : default(AVColorPrimaries))),
                GetColorMetadataValue(FfmpegNativeHelpers.GetColorTransferName(_codecContext != null ? _codecContext->color_trc : default(AVColorTransferCharacteristic))),
                _audioStreamInfo != null ? _audioStreamInfo.BitRate : null,
                _audioStreamInfo != null ? _audioStreamInfo.BitDepth : null);
        }

        private AVPixelFormat GetInspectorSourcePixelFormat()
        {
            if (_codecContext == null)
            {
                return AVPixelFormat.AV_PIX_FMT_NONE;
            }

            if (_hardwareDecodeFormatSelected && _codecContext->sw_pix_fmt != AVPixelFormat.AV_PIX_FMT_NONE)
            {
                return _codecContext->sw_pix_fmt;
            }

            if (_codecContext->pix_fmt != AVPixelFormat.AV_PIX_FMT_NONE)
            {
                return _codecContext->pix_fmt;
            }

            return _codecContext->sw_pix_fmt;
        }

        private static string GetColorMetadataValue(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return null;
            }

            var trimmedValue = value.Trim();
            if (trimmedValue.Equals("unknown", StringComparison.OrdinalIgnoreCase) ||
                trimmedValue.Equals("unspecified", StringComparison.OrdinalIgnoreCase) ||
                trimmedValue.StartsWith("reserved", StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            return trimmedValue;
        }

        private static ReviewPosition BuildReviewPosition(FrameDescriptor descriptor)
        {
            return new ReviewPosition(
                descriptor.PresentationTime,
                descriptor.FrameIndex,
                true,
                descriptor.IsFrameIndexAbsolute,
                descriptor.PresentationTimestamp,
                descriptor.DecodeTimestamp);
        }

        private static long? GetPresentationTimestamp(AVFrame* decodedFrame)
        {
            return FfmpegNativeHelpers.GetBestPresentationTimestamp(decodedFrame);
        }

        private FfmpegGlobalFrameIndexEntry ResolveIndexedFrameIdentity(long? presentationTimestamp, long? decodeTimestamp)
        {
            if (!IsGlobalFrameIndexAvailable)
            {
                return null;
            }

            FfmpegGlobalFrameIndexEntry indexedEntry;
            return _globalFrameIndex.TryResolve(presentationTimestamp, decodeTimestamp, out indexedEntry)
                ? indexedEntry
                : null;
        }

        private long FindBackwardReconstructionSeekTimestamp(DecodedFrameBuffer frame)
        {
            if (frame == null)
            {
                return 0L;
            }

            var referenceTimestamp = frame.Descriptor.PresentationTimestamp
                ?? frame.Descriptor.DecodeTimestamp
                ?? FfmpegNativeHelpers.ToStreamTimestamp(frame.Descriptor.PresentationTime, _videoStreamTimeBase);

            return referenceTimestamp > 0L
                ? referenceTimestamp - 1L
                : 0L;
        }

        private long? GetAbsoluteFrameIndex(DecodedFrameBuffer frame)
        {
            return frame != null &&
                frame.Descriptor.IsFrameIndexAbsolute &&
                frame.Descriptor.FrameIndex.HasValue
                ? frame.Descriptor.FrameIndex.Value
                : (long?)null;
        }

        private static bool FramesReferToSameDisplayFrame(DecodedFrameBuffer candidate, DecodedFrameBuffer original)
        {
            if (candidate == null || original == null)
            {
                return false;
            }

            if (candidate.Descriptor.PresentationTimestamp.HasValue && original.Descriptor.PresentationTimestamp.HasValue)
            {
                return candidate.Descriptor.PresentationTimestamp.Value ==
                    original.Descriptor.PresentationTimestamp.Value;
            }

            if (candidate.Descriptor.DecodeTimestamp.HasValue && original.Descriptor.DecodeTimestamp.HasValue)
            {
                return candidate.Descriptor.DecodeTimestamp.Value ==
                    original.Descriptor.DecodeTimestamp.Value;
            }

            if (candidate.Descriptor.PresentationTime != TimeSpan.Zero || original.Descriptor.PresentationTime != TimeSpan.Zero)
            {
                return candidate.Descriptor.PresentationTime == original.Descriptor.PresentationTime;
            }

            return candidate.Descriptor.IsFrameIndexAbsolute &&
                original.Descriptor.IsFrameIndexAbsolute &&
                candidate.Descriptor.FrameIndex.HasValue &&
                original.Descriptor.FrameIndex.HasValue &&
                candidate.Descriptor.FrameIndex.Value == original.Descriptor.FrameIndex.Value;
        }

        private bool FrameMatchesIndexEntry(DecodedFrameBuffer frame, FfmpegGlobalFrameIndexEntry indexedEntry)
        {
            if (frame == null || indexedEntry == null)
            {
                return false;
            }

            if (frame.Descriptor.IsFrameIndexAbsolute &&
                frame.Descriptor.FrameIndex.HasValue &&
                frame.Descriptor.FrameIndex.Value == indexedEntry.AbsoluteFrameIndex)
            {
                return true;
            }

            if (indexedEntry.PresentationTimestamp.HasValue &&
                frame.Descriptor.PresentationTimestamp.HasValue &&
                indexedEntry.PresentationTimestamp.Value == frame.Descriptor.PresentationTimestamp.Value)
            {
                return true;
            }

            if (indexedEntry.DecodeTimestamp.HasValue &&
                frame.Descriptor.DecodeTimestamp.HasValue &&
                indexedEntry.DecodeTimestamp.Value == frame.Descriptor.DecodeTimestamp.Value)
            {
                return true;
            }

            return indexedEntry.PresentationTime != TimeSpan.Zero &&
                frame.Descriptor.PresentationTime == indexedEntry.PresentationTime;
        }

        private bool IsAtOrAfterRequestedTarget(DecodedFrameBuffer frame, TimeSpan requestedPosition, long requestedTimestamp)
        {
            if (frame == null)
            {
                return false;
            }

            if (frame.Descriptor.PresentationTimestamp.HasValue)
            {
                return frame.Descriptor.PresentationTimestamp.Value >= requestedTimestamp;
            }

            return frame.Descriptor.PresentationTime >= requestedPosition;
        }

        private void SetOperationInstrumentation(bool usedGlobalIndex, string anchorStrategy, long? anchorFrameIndex)
        {
            LastOperationUsedGlobalIndex = usedGlobalIndex;
            LastAnchorStrategy = anchorStrategy ?? string.Empty;
            LastAnchorFrameIndex = anchorFrameIndex;
        }

        private TimeSpan ClampSeekTarget(TimeSpan requestedPosition)
        {
            if (requestedPosition < TimeSpan.Zero)
            {
                return TimeSpan.Zero;
            }

            var duration = _mediaInfo.Duration;
            if (duration <= TimeSpan.Zero)
            {
                return requestedPosition;
            }

            if (requestedPosition >= duration)
            {
                return duration > TimeSpan.FromTicks(1L)
                    ? duration - TimeSpan.FromTicks(1L)
                    : TimeSpan.Zero;
            }

            return requestedPosition;
        }

        private static void EnsureFfmpegRuntimePathConfigured()
        {
            if (!string.IsNullOrWhiteSpace(ffmpeg.RootPath))
            {
                return;
            }

            if (!string.IsNullOrWhiteSpace(App.RuntimeDirectory))
            {
                ffmpeg.RootPath = App.RuntimeDirectory;
                return;
            }

            ffmpeg.RootPath = AppDomain.CurrentDomain.BaseDirectory;
        }

        private void CloseCore(bool clearFilePath, bool clearErrorMessage)
        {
            CancelGlobalFrameIndexBuild(resetStatus: true);
            ReleaseNativeState();

            IsMediaOpen = false;
            IsPlaying = false;
            LastSeekLandedAtOrAfterTarget = false;
            LastFrameAdvanceWasCacheHit = false;
            LastFrameSeekWasCacheHit = false;
            LastOperationUsedGlobalIndex = false;
            LastAnchorFrameIndex = null;
            LastAnchorStrategy = string.Empty;
            _mediaInfo = VideoMediaInfo.Empty;
            _position = ReviewPosition.Empty;
            _currentFrame = null;
            _globalFrameIndex = null;
            _audioStreamInfo = FfmpegAudioStreamInfo.None;
            _decodedSegmentFrameCount = 0L;
            _videoStreamIndex = -1;
            _videoStreamTimeBase = default(AVRational);
            _nominalFrameRate = default(AVRational);
            _videoDecoder = null;
            _segmentFrameIndexAbsolute = false;
            _frameCache.Clear();
            _configuredCacheBudgetBytes = 0L;
            _sessionDecodedFrameCacheBudgetBytes = 0L;
            _maxPreviousCachedFrameCount = DefaultCachedPreviousFrameCount;
            _maxForwardCachedFrameCount = DefaultCachedForwardFrameCount;
            ResetDecodeReadState();
            ResetHardwareDecodeDiagnostics();
            ResetFrameTransferInstrumentation();

            if (clearFilePath)
            {
                _currentFilePath = string.Empty;
            }

            if (clearErrorMessage)
            {
                _lastErrorMessage = string.Empty;
                _lastAudioErrorMessage = string.Empty;
            }

            RefreshDecodedFrameBudgetState(isOpen: false, approximateFrameBytes: 0, gpuActive: false);
        }

        private void ReleaseNativeState()
        {
            StopAudioPlaybackSession(_audioPlaybackSession);
            _audioPlaybackSession = null;

            _frameConverter?.Dispose();
            _frameConverter = null;

            if (_decodedFrame != null)
            {
                var decodedFrame = _decodedFrame;
                ffmpeg.av_frame_free(&decodedFrame);
                _decodedFrame = null;
            }

            if (_softwareFrame != null)
            {
                var softwareFrame = _softwareFrame;
                ffmpeg.av_frame_free(&softwareFrame);
                _softwareFrame = null;
            }

            if (_packet != null)
            {
                var packet = _packet;
                ffmpeg.av_packet_free(&packet);
                _packet = null;
            }

            ResetHardwareDecodeState(clearFallbackReason: false);
            ReleaseCodecContext();

            if (_formatContext != null)
            {
                var formatContext = _formatContext;
                ffmpeg.avformat_close_input(&formatContext);
                _formatContext = null;
            }

            _videoStream = null;
        }

        private void ReleaseCodecContext()
        {
            if (_codecContext == null)
            {
                return;
            }

            var codecContext = _codecContext;
            ffmpeg.avcodec_free_context(&codecContext);
            _codecContext = null;
        }

        private void OnFramePresented(DecodedFrameBuffer frame)
        {
            if (frame == null)
            {
                return;
            }

            FramePresented?.Invoke(this, new FramePresentedEventArgs(frame));
        }

        private void OnStateChanged()
        {
            StateChanged?.Invoke(
                this,
                new VideoReviewEngineStateChangedEventArgs(
                    IsMediaOpen,
                    IsPlaying,
                    _currentFilePath,
                    _lastErrorMessage,
                    _mediaInfo,
                    _position));
        }

        private void ThrowIfDisposed()
        {
            if (_disposed)
            {
                throw new ObjectDisposedException(nameof(FfmpegReviewEngine));
            }
        }

        private sealed class BackwardReconstructionResult
        {
            public BackwardReconstructionResult(List<DecodedFrameBuffer> windowFrames, int currentIndex, bool hasPreviousFrame)
            {
                WindowFrames = windowFrames ?? throw new ArgumentNullException(nameof(windowFrames));
                CurrentIndex = currentIndex;
                HasPreviousFrame = hasPreviousFrame;
            }

            public List<DecodedFrameBuffer> WindowFrames { get; }

            public int CurrentIndex { get; }

            public bool HasPreviousFrame { get; }
        }

        private sealed class FrameSeekWindowResult
        {
            public FrameSeekWindowResult(List<DecodedFrameBuffer> windowFrames, int currentIndex)
            {
                WindowFrames = windowFrames ?? throw new ArgumentNullException(nameof(windowFrames));
                CurrentIndex = currentIndex;
            }

            public List<DecodedFrameBuffer> WindowFrames { get; }

            public int CurrentIndex { get; }
        }
    }
}
