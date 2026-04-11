using System;
using System.Collections.Generic;
using FramePlayer.Core.Models;

namespace FramePlayer.Engines.FFmpeg
{
    internal sealed class FfmpegDecodedFrameCache
    {
        private readonly object _sync = new object();
        private readonly List<DecodedFrameBuffer> _frames = new List<DecodedFrameBuffer>();
        private int _maxPreviousFrames;
        private int _maxForwardFrames;
        private int _currentIndex = -1;

        public FfmpegDecodedFrameCache(int maxPreviousFrames, int maxForwardFrames)
        {
            _maxPreviousFrames = Math.Max(0, maxPreviousFrames);
            _maxForwardFrames = Math.Max(0, maxForwardFrames);
        }

        public void UpdateLimits(int maxPreviousFrames, int maxForwardFrames)
        {
            lock (_sync)
            {
                _maxPreviousFrames = Math.Max(0, maxPreviousFrames);
                _maxForwardFrames = Math.Max(0, maxForwardFrames);
                TrimPreviousFrames();
                TrimForwardFrames();
            }
        }

        public int Count
        {
            get
            {
                lock (_sync)
                {
                    return _frames.Count;
                }
            }
        }

        public int ForwardCount
        {
            get
            {
                lock (_sync)
                {
                    return HasCurrent ? _frames.Count - _currentIndex - 1 : 0;
                }
            }
        }

        public int PreviousCount
        {
            get
            {
                lock (_sync)
                {
                    return HasCurrent ? _currentIndex : 0;
                }
            }
        }

        public bool HasCurrent
        {
            get
            {
                lock (_sync)
                {
                    return _currentIndex >= 0 && _currentIndex < _frames.Count;
                }
            }
        }

        public long ApproximatePixelBufferBytes
        {
            get
            {
                lock (_sync)
                {
                    long totalBytes = 0L;
                    for (var index = 0; index < _frames.Count; index++)
                    {
                        var pixelBuffer = _frames[index] != null ? _frames[index].PixelBuffer : null;
                        if (pixelBuffer == null)
                        {
                            continue;
                        }

                        totalBytes += pixelBuffer.LongLength;
                    }

                    return totalBytes;
                }
            }
        }

        public DecodedFrameBuffer Current
        {
            get
            {
                lock (_sync)
                {
                    return HasCurrent ? _frames[_currentIndex] : null;
                }
            }
        }

        public void Clear()
        {
            lock (_sync)
            {
                _frames.Clear();
                _currentIndex = -1;
            }
        }

        public void Reset(DecodedFrameBuffer currentFrame)
        {
            if (currentFrame == null)
            {
                throw new ArgumentNullException(nameof(currentFrame));
            }

            lock (_sync)
            {
                _frames.Clear();
                _frames.Add(currentFrame);
                _currentIndex = 0;
            }
        }

        public void LoadWindow(IList<DecodedFrameBuffer> frames, int currentIndex)
        {
            if (frames == null)
            {
                throw new ArgumentNullException(nameof(frames));
            }

            if (frames.Count == 0)
            {
                throw new ArgumentException("The cache window must contain at least one decoded frame.", nameof(frames));
            }

            if (currentIndex < 0 || currentIndex >= frames.Count)
            {
                throw new ArgumentOutOfRangeException(nameof(currentIndex));
            }

            lock (_sync)
            {
                _frames.Clear();
                _frames.AddRange(frames);
                _currentIndex = currentIndex;
            }
        }

        public void AppendForward(DecodedFrameBuffer frame)
        {
            if (frame == null)
            {
                throw new ArgumentNullException(nameof(frame));
            }

            lock (_sync)
            {
                _frames.Add(frame);
                TrimForwardFrames();
            }
        }

        public DecodedFrameBuffer AppendForwardAndAdvance(DecodedFrameBuffer frame)
        {
            if (frame == null)
            {
                throw new ArgumentNullException(nameof(frame));
            }

            lock (_sync)
            {
                _frames.Add(frame);
                if (!HasCurrent)
                {
                    _currentIndex = _frames.Count - 1;
                }
                else
                {
                    _currentIndex = Math.Min(_frames.Count - 1, _currentIndex + 1);
                }

                TrimPreviousFrames();
                TrimForwardFrames();

                if (!HasCurrent)
                {
                    throw new InvalidOperationException("The decoded frame cache could not advance to the appended frame.");
                }

                return _frames[_currentIndex];
            }
        }

        public bool TryMoveNext(out DecodedFrameBuffer frame)
        {
            lock (_sync)
            {
                if (!HasCurrent || _currentIndex + 1 >= _frames.Count)
                {
                    frame = null;
                    return false;
                }

                _currentIndex++;
                TrimPreviousFrames();
                frame = _frames[_currentIndex];
                return true;
            }
        }

        public bool TryPeekNext(out DecodedFrameBuffer frame)
        {
            lock (_sync)
            {
                if (!HasCurrent || _currentIndex + 1 >= _frames.Count)
                {
                    frame = null;
                    return false;
                }

                frame = _frames[_currentIndex + 1];
                return true;
            }
        }

        public bool TryMovePrevious(out DecodedFrameBuffer frame)
        {
            lock (_sync)
            {
                if (!HasCurrent || _currentIndex <= 0)
                {
                    frame = null;
                    return false;
                }

                _currentIndex--;
                frame = _frames[_currentIndex];
                return true;
            }
        }

        public bool TryMoveToAbsoluteFrameIndex(long frameIndex, out DecodedFrameBuffer frame)
        {
            if (frameIndex < 0)
            {
                frame = null;
                return false;
            }

            lock (_sync)
            {
                for (var index = 0; index < _frames.Count; index++)
                {
                    var candidate = _frames[index];
                    if (candidate == null ||
                        !candidate.Descriptor.IsFrameIndexAbsolute ||
                        !candidate.Descriptor.FrameIndex.HasValue ||
                        candidate.Descriptor.FrameIndex.Value != frameIndex)
                    {
                        continue;
                    }

                    _currentIndex = index;
                    TrimPreviousFrames();
                    TrimForwardFrames();
                    frame = _frames[_currentIndex];
                    return true;
                }

                frame = null;
                return false;
            }
        }

        public bool ReplaceFrames(Func<DecodedFrameBuffer, DecodedFrameBuffer> replaceFrame)
        {
            if (replaceFrame == null)
            {
                throw new ArgumentNullException(nameof(replaceFrame));
            }

            lock (_sync)
            {
                var changed = false;
                for (var index = 0; index < _frames.Count; index++)
                {
                    var existingFrame = _frames[index];
                    var replacementFrame = replaceFrame(existingFrame);
                    if (replacementFrame == null)
                    {
                        replacementFrame = existingFrame;
                    }

                    if (!ReferenceEquals(existingFrame, replacementFrame))
                    {
                        _frames[index] = replacementFrame;
                        changed = true;
                    }
                }

                return changed;
            }
        }

        private void TrimPreviousFrames()
        {
            while (_currentIndex > _maxPreviousFrames)
            {
                _frames.RemoveAt(0);
                _currentIndex--;
            }
        }

        private void TrimForwardFrames()
        {
            while (HasCurrent && ForwardCount > _maxForwardFrames)
            {
                _frames.RemoveAt(_frames.Count - 1);
            }
        }
    }
}
