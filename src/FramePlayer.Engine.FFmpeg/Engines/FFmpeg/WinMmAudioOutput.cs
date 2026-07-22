using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading;

namespace FramePlayer.Engines.FFmpeg
{
    internal sealed partial class WinMmAudioOutput : IAudioOutput
    {
        private const int WaveMapper = -1;
        private const int CallbackNull = 0;
        private const int WhdrDone = 0x00000001;
        private const int TimeMilliseconds = 0x0001;
        private const int TimeSamples = 0x0002;
        private const int TimeBytes = 0x0004;
        private const int MaxQueuedMilliseconds = 700;
        private const int PollMilliseconds = 5;
        private const long CounterRolloverSpan = (long)uint.MaxValue + 1L;
        private const uint CounterRolloverHighWatermark = 0xC0000000;
        private const uint CounterRolloverLowWatermark = 0x40000000;

        private readonly List<QueuedBuffer> _queuedBuffers = new List<QueuedBuffer>();
        private readonly object _sync = new object();
        private readonly object _positionSync = new object();
        private readonly int _sampleRate;
        private readonly int _bytesPerSecond;
        private readonly int _maxQueuedBytes;
        private IntPtr _waveOutHandle;
        private long _submittedBytes;
        private uint _lastPositionValue;
        private uint _lastPositionType;
        private long _positionRolloverOffset;
        private int _queuedBytes;
        private bool _disposed;

        public WinMmAudioOutput(int sampleRate, int channelCount, int bitsPerSample)
        {
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(sampleRate);
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(channelCount);
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(bitsPerSample);

            var blockAlign = checked((ushort)(channelCount * bitsPerSample / 8));
            _sampleRate = sampleRate;
            _bytesPerSecond = checked(sampleRate * blockAlign);
            _maxQueuedBytes = Math.Max(_bytesPerSecond * MaxQueuedMilliseconds / 1000, blockAlign);

            var waveFormat = new WaveFormatEx
            {
                wFormatTag = 1,
                nChannels = checked((ushort)channelCount),
                nSamplesPerSec = checked((uint)sampleRate),
                nAvgBytesPerSec = checked((uint)_bytesPerSecond),
                nBlockAlign = blockAlign,
                wBitsPerSample = checked((ushort)bitsPerSample),
                cbSize = 0
            };

            var openResult = waveOutOpen(
                out _waveOutHandle,
                WaveMapper,
                ref waveFormat,
                IntPtr.Zero,
                IntPtr.Zero,
                CallbackNull);
            ThrowIfWaveError(openResult, "Open Windows audio output");
        }

        public bool IsOpen
        {
            get
            {
                lock (_sync)
                {
                    return _waveOutHandle != IntPtr.Zero && !_disposed;
                }
            }
        }

        public long SubmittedBytes
        {
            get { return Interlocked.Read(ref _submittedBytes); }
        }

        public TimeSpan PlayedDuration
        {
            get
            {
                lock (_sync)
                {
                    if (_waveOutHandle == IntPtr.Zero || _disposed)
                    {
                        return TimeSpan.Zero;
                    }

                    var time = new MmTime
                    {
                        wType = TimeBytes
                    };
                    var result = waveOutGetPosition(_waveOutHandle, ref time, Marshal.SizeOf<MmTime>());
                    if (result != 0)
                    {
                        return TimeSpan.Zero;
                    }

                    return MmTimeToDuration(time.wType, ExtendPositionCounter(time));
                }
            }
        }

        public void Write(byte[] buffer, CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(buffer);

            if (buffer.Length == 0)
            {
                return;
            }

            lock (_sync)
            {
                ThrowIfDisposed();
            }

            WaitForQueueRoom(buffer.Length, cancellationToken);

            lock (_sync)
            {
                cancellationToken.ThrowIfCancellationRequested();
                ThrowIfDisposed();
                QueueBuffer(buffer);
            }
        }

        public void Dispose()
        {
            bool released;
            lock (_sync)
            {
                _disposed = true;
                released = TryDisposeNativeState();
            }

            if (released)
            {
                GC.SuppressFinalize(this);
            }
        }

        ~WinMmAudioOutput()
        {
            lock (_sync)
            {
                _disposed = true;
                _ = TryDisposeNativeState();
            }
        }

        private bool TryDisposeNativeState()
        {
            if (_waveOutHandle == IntPtr.Zero)
            {
                return true;
            }

            var resetResult = waveOutReset(_waveOutHandle);
            if (resetResult != 0)
            {
                // The driver may still own queued buffers. Retain them and the handle for a later retry.
                return false;
            }

            ReleaseAllBuffers();
            if (_queuedBuffers.Count != 0)
            {
                // A header that could not be unprepared must not be freed or detached from its device handle.
                return false;
            }

            var closeResult = waveOutClose(_waveOutHandle);
            if (closeResult != 0)
            {
                return false;
            }

            _waveOutHandle = IntPtr.Zero;
            return true;
        }

        private void WaitForQueueRoom(int nextBufferLength, CancellationToken cancellationToken)
        {
            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();

                lock (_sync)
                {
                    ThrowIfDisposed();
                    ReleaseCompletedBuffers();

                    if (_queuedBytes + nextBufferLength <= _maxQueuedBytes || _queuedBuffers.Count == 0)
                    {
                        return;
                    }
                }

                cancellationToken.WaitHandle.WaitOne(PollMilliseconds);
            }
        }

        private void QueueBuffer(byte[] buffer)
        {
            var dataPointer = Marshal.AllocHGlobal(buffer.Length);
            var headerPointer = IntPtr.Zero;
            var headerPrepared = false;
            var headerSubmitted = false;
            var bufferTracked = false;
            QueuedBuffer queuedBuffer = null;
            var headerSize = Marshal.SizeOf<WaveHeader>();
            try
            {
                Marshal.Copy(buffer, 0, dataPointer, buffer.Length);

                var header = new WaveHeader
                {
                    lpData = dataPointer,
                    dwBufferLength = buffer.Length
                };
                headerPointer = Marshal.AllocHGlobal(headerSize);
                Marshal.StructureToPtr(header, headerPointer, false);

                ThrowIfWaveError(
                    waveOutPrepareHeader(_waveOutHandle, headerPointer, headerSize),
                    "Prepare audio output buffer");
                headerPrepared = true;

                queuedBuffer = new QueuedBuffer(headerPointer, dataPointer);
                _queuedBuffers.Add(queuedBuffer);
                bufferTracked = true;

                ThrowIfWaveError(
                    waveOutWrite(_waveOutHandle, headerPointer, headerSize),
                    "Submit audio output buffer");
                headerSubmitted = true;

                queuedBuffer.MarkSubmitted(buffer.Length);
                _queuedBytes += buffer.Length;
                Interlocked.Add(ref _submittedBytes, buffer.Length);
            }
            catch
            {
                var canFreePointers = !headerPrepared;
                if (headerPrepared && !headerSubmitted && _waveOutHandle != IntPtr.Zero)
                {
                    var unprepareResult = waveOutUnprepareHeader(_waveOutHandle, headerPointer, headerSize);
                    canFreePointers = unprepareResult == 0;
                }

                if (canFreePointers)
                {
                    if (bufferTracked)
                    {
                        _queuedBuffers.Remove(queuedBuffer);
                    }

                    if (headerPointer != IntPtr.Zero)
                    {
                        Marshal.FreeHGlobal(headerPointer);
                    }

                    if (dataPointer != IntPtr.Zero)
                    {
                        Marshal.FreeHGlobal(dataPointer);
                    }
                }

                throw;
            }
        }

        private void ReleaseCompletedBuffers()
        {
            for (var index = _queuedBuffers.Count - 1; index >= 0; index--)
            {
                var queuedBuffer = _queuedBuffers[index];
                var header = Marshal.PtrToStructure<WaveHeader>(queuedBuffer.HeaderPointer);
                if ((header.dwFlags & WhdrDone) == 0)
                {
                    continue;
                }

                if (TryReleaseBuffer(queuedBuffer))
                {
                    _queuedBuffers.RemoveAt(index);
                }
            }
        }

        private void ReleaseAllBuffers()
        {
            for (var index = _queuedBuffers.Count - 1; index >= 0; index--)
            {
                if (TryReleaseBuffer(_queuedBuffers[index]))
                {
                    _queuedBuffers.RemoveAt(index);
                }
            }
        }

        private bool TryReleaseBuffer(QueuedBuffer queuedBuffer)
        {
            if (queuedBuffer.HeaderPointer != IntPtr.Zero)
            {
                if (_waveOutHandle == IntPtr.Zero)
                {
                    return false;
                }

                var unprepareResult = waveOutUnprepareHeader(
                    _waveOutHandle,
                    queuedBuffer.HeaderPointer,
                    Marshal.SizeOf<WaveHeader>());
                if (unprepareResult != 0)
                {
                    return false;
                }
            }

            if (queuedBuffer.HeaderPointer != IntPtr.Zero)
            {
                Marshal.FreeHGlobal(queuedBuffer.HeaderPointer);
            }

            if (queuedBuffer.DataPointer != IntPtr.Zero)
            {
                Marshal.FreeHGlobal(queuedBuffer.DataPointer);
            }

            _queuedBytes = Math.Max(0, _queuedBytes - queuedBuffer.AccountedLength);
            return true;
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

        private long ExtendPositionCounter(MmTime time)
        {
            lock (_positionSync)
            {
                if (_lastPositionType != time.wType)
                {
                    _lastPositionType = time.wType;
                    _lastPositionValue = time.u;
                    _positionRolloverOffset = 0L;
                    return time.u;
                }

                if (time.u < _lastPositionValue)
                {
                    if (_lastPositionValue >= CounterRolloverHighWatermark && time.u <= CounterRolloverLowWatermark)
                    {
                        _positionRolloverOffset += CounterRolloverSpan;
                    }
                    else
                    {
                        _positionRolloverOffset = 0L;
                    }
                }

                _lastPositionValue = time.u;
                return _positionRolloverOffset + time.u;
            }
        }

        private TimeSpan MmTimeToDuration(uint timeType, long timeValue)
        {
            switch (timeType)
            {
                case TimeBytes:
                    return BytesToDuration(timeValue);
                case TimeSamples:
                    return SamplesToDuration(timeValue);
                case TimeMilliseconds:
                    return TimeSpan.FromMilliseconds(timeValue);
                default:
                    return TimeSpan.Zero;
            }
        }

        private TimeSpan SamplesToDuration(long sampleCount)
        {
            if (sampleCount <= 0L || _sampleRate <= 0)
            {
                return TimeSpan.Zero;
            }

            var seconds = sampleCount / (double)_sampleRate;
            return TimeSpan.FromTicks((long)Math.Round(seconds * TimeSpan.TicksPerSecond));
        }

        private static void ThrowIfWaveError(int result, string operation)
        {
            if (result == 0)
            {
                return;
            }

            throw new InvalidOperationException(operation + " failed with WinMM error " + result + ".");
        }

        private void ThrowIfDisposed()
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
        }

        [LibraryImport("winmm.dll", SetLastError = false)]
        private static partial int waveOutOpen(
            out IntPtr hWaveOut,
            int uDeviceID,
            ref WaveFormatEx lpFormat,
            IntPtr dwCallback,
            IntPtr dwInstance,
            int fdwOpen);

        [LibraryImport("winmm.dll", SetLastError = false)]
        private static partial int waveOutPrepareHeader(IntPtr hWaveOut, IntPtr waveHeader, int waveHeaderSize);

        [LibraryImport("winmm.dll", SetLastError = false)]
        private static partial int waveOutWrite(IntPtr hWaveOut, IntPtr waveHeader, int waveHeaderSize);

        [LibraryImport("winmm.dll", SetLastError = false)]
        private static partial int waveOutUnprepareHeader(IntPtr hWaveOut, IntPtr waveHeader, int waveHeaderSize);

        [LibraryImport("winmm.dll", SetLastError = false)]
        private static partial int waveOutReset(IntPtr hWaveOut);

        [LibraryImport("winmm.dll", SetLastError = false)]
        private static partial int waveOutClose(IntPtr hWaveOut);

        [LibraryImport("winmm.dll", SetLastError = false)]
        private static partial int waveOutGetPosition(IntPtr hWaveOut, ref MmTime time, int timeSize);

        [StructLayout(LayoutKind.Sequential)]
        private struct WaveFormatEx
        {
            public ushort wFormatTag;
            public ushort nChannels;
            public uint nSamplesPerSec;
            public uint nAvgBytesPerSec;
            public ushort nBlockAlign;
            public ushort wBitsPerSample;
            public ushort cbSize;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct WaveHeader
        {
            public IntPtr lpData;
            public int dwBufferLength;
            public int dwBytesRecorded;
            public IntPtr dwUser;
            public int dwFlags;
            public int dwLoops;
            public IntPtr lpNext;
            public IntPtr reserved;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct MmTime
        {
            public uint wType;
            public uint u;
            public uint pad;
        }

        private sealed class QueuedBuffer
        {
            public QueuedBuffer(IntPtr headerPointer, IntPtr dataPointer)
            {
                HeaderPointer = headerPointer;
                DataPointer = dataPointer;
            }

            public IntPtr HeaderPointer { get; }

            public IntPtr DataPointer { get; }

            public int AccountedLength { get; private set; }

            public void MarkSubmitted(int length)
            {
                AccountedLength = length;
            }
        }
    }
}
