using System;
using System.Diagnostics;
using System.Threading;

namespace FramePlayer.Engines.FFmpeg
{
    internal sealed class ManagedAudioClockOutput : IAudioOutput
    {
        private const int MaxQueuedMilliseconds = 700;
        private const int PollMilliseconds = 5;

        private readonly Stopwatch _clock = new Stopwatch();
        private readonly int _bytesPerSecond;
        private readonly int _maxQueuedBytes;
        private long _submittedBytes;
        private bool _disposed;

        public ManagedAudioClockOutput(int sampleRate, int channelCount, int bitsPerSample)
        {
            if (sampleRate <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(sampleRate));
            }

            if (channelCount <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(channelCount));
            }

            if (bitsPerSample <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(bitsPerSample));
            }

            var blockAlign = checked(channelCount * bitsPerSample / 8);
            _bytesPerSecond = checked(sampleRate * blockAlign);
            _maxQueuedBytes = Math.Max(_bytesPerSecond * MaxQueuedMilliseconds / 1000, blockAlign);
            _clock.Start();
        }

        public bool IsOpen
        {
            get { return !_disposed; }
        }

        public long SubmittedBytes
        {
            get { return Interlocked.Read(ref _submittedBytes); }
        }

        public TimeSpan PlayedDuration
        {
            get
            {
                if (!IsOpen)
                {
                    return TimeSpan.Zero;
                }

                return _clock.Elapsed < SubmittedDuration
                    ? _clock.Elapsed
                    : SubmittedDuration;
            }
        }

        private TimeSpan SubmittedDuration
        {
            get { return BytesToDuration(SubmittedBytes); }
        }

        public void Write(byte[] buffer, CancellationToken cancellationToken)
        {
            if (buffer == null)
            {
                throw new ArgumentNullException(nameof(buffer));
            }

            if (buffer.Length == 0)
            {
                return;
            }

            ThrowIfDisposed();
            WaitForQueueRoom(buffer.Length, cancellationToken);
            Interlocked.Add(ref _submittedBytes, buffer.Length);
        }

        public void Dispose()
        {
            _disposed = true;
            _clock.Stop();
        }

        private void WaitForQueueRoom(int nextBufferLength, CancellationToken cancellationToken)
        {
            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var queuedBytes = DurationToBytes(SubmittedDuration - _clock.Elapsed);
                if (queuedBytes + nextBufferLength <= _maxQueuedBytes || queuedBytes <= 0)
                {
                    return;
                }

                cancellationToken.WaitHandle.WaitOne(PollMilliseconds);
            }
        }

        private TimeSpan BytesToDuration(long byteCount)
        {
            if (byteCount <= 0L || _bytesPerSecond <= 0)
            {
                return TimeSpan.Zero;
            }

            var seconds = byteCount / (double)_bytesPerSecond;
            return TimeSpan.FromTicks((long)Math.Round(seconds * TimeSpan.TicksPerSecond));
        }

        private int DurationToBytes(TimeSpan duration)
        {
            if (duration <= TimeSpan.Zero)
            {
                return 0;
            }

            return (int)Math.Min(int.MaxValue, duration.TotalSeconds * _bytesPerSecond);
        }

        private void ThrowIfDisposed()
        {
            if (_disposed)
            {
                throw new ObjectDisposedException(nameof(ManagedAudioClockOutput));
            }
        }
    }
}
