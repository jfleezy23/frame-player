using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading;
using FramePlayer.Core.Abstractions;

namespace FramePlayer.Engines.FFmpeg
{
    internal sealed class WinMmAudioOutput : IAudioOutput
    {
        private const int WaveMapper = -1;
        private const int CallbackNull = 0;
        private const int WhdrDone = 0x00000001;
        private const int TimeBytes = 0x0004;
        private const int MaxQueuedMilliseconds = 700;
        private const int PollMilliseconds = 5;

        private readonly List<QueuedBuffer> _queuedBuffers = new List<QueuedBuffer>();
        private readonly int _bytesPerSecond;
        private readonly int _maxQueuedBytes;
        private IntPtr _waveOutHandle;
        private long _submittedBytes;
        private int _queuedBytes;
        private bool _disposed;

        public WinMmAudioOutput(int sampleRate, int channelCount, int bitsPerSample)
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

            var blockAlign = checked((ushort)(channelCount * bitsPerSample / 8));
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
            get { return _waveOutHandle != IntPtr.Zero && !_disposed; }
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

                var time = new MmTime
                {
                    wType = TimeBytes
                };
                var result = waveOutGetPosition(_waveOutHandle, ref time, Marshal.SizeOf(typeof(MmTime)));
                if (result != 0)
                {
                    return TimeSpan.Zero;
                }

                return BytesToDuration(time.u);
            }
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
            QueueBuffer(buffer);
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;

            if (_waveOutHandle != IntPtr.Zero)
            {
                waveOutReset(_waveOutHandle);
                ReleaseAllBuffers();
                waveOutClose(_waveOutHandle);
                _waveOutHandle = IntPtr.Zero;
            }
        }

        private void WaitForQueueRoom(int nextBufferLength, CancellationToken cancellationToken)
        {
            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();
                ReleaseCompletedBuffers();

                if (_queuedBytes + nextBufferLength <= _maxQueuedBytes || _queuedBuffers.Count == 0)
                {
                    return;
                }

                cancellationToken.WaitHandle.WaitOne(PollMilliseconds);
            }
        }

        private void QueueBuffer(byte[] buffer)
        {
            var dataPointer = Marshal.AllocHGlobal(buffer.Length);
            var headerPointer = IntPtr.Zero;
            try
            {
                Marshal.Copy(buffer, 0, dataPointer, buffer.Length);

                var header = new WaveHeader
                {
                    lpData = dataPointer,
                    dwBufferLength = buffer.Length
                };
                headerPointer = Marshal.AllocHGlobal(Marshal.SizeOf(typeof(WaveHeader)));
                Marshal.StructureToPtr(header, headerPointer, false);

                ThrowIfWaveError(
                    waveOutPrepareHeader(_waveOutHandle, headerPointer, Marshal.SizeOf(typeof(WaveHeader))),
                    "Prepare audio output buffer");
                ThrowIfWaveError(
                    waveOutWrite(_waveOutHandle, headerPointer, Marshal.SizeOf(typeof(WaveHeader))),
                    "Submit audio output buffer");

                _queuedBuffers.Add(new QueuedBuffer(headerPointer, dataPointer, buffer.Length));
                _queuedBytes += buffer.Length;
                Interlocked.Add(ref _submittedBytes, buffer.Length);
            }
            catch
            {
                if (headerPointer != IntPtr.Zero)
                {
                    Marshal.FreeHGlobal(headerPointer);
                }

                if (dataPointer != IntPtr.Zero)
                {
                    Marshal.FreeHGlobal(dataPointer);
                }

                throw;
            }
        }

        private void ReleaseCompletedBuffers()
        {
            for (var index = _queuedBuffers.Count - 1; index >= 0; index--)
            {
                var queuedBuffer = _queuedBuffers[index];
                var header = (WaveHeader)Marshal.PtrToStructure(queuedBuffer.HeaderPointer, typeof(WaveHeader));
                if ((header.dwFlags & WhdrDone) == 0)
                {
                    continue;
                }

                ReleaseBuffer(queuedBuffer);
                _queuedBuffers.RemoveAt(index);
            }
        }

        private void ReleaseAllBuffers()
        {
            for (var index = _queuedBuffers.Count - 1; index >= 0; index--)
            {
                ReleaseBuffer(_queuedBuffers[index]);
            }

            _queuedBuffers.Clear();
            _queuedBytes = 0;
        }

        private void ReleaseBuffer(QueuedBuffer queuedBuffer)
        {
            if (_waveOutHandle != IntPtr.Zero && queuedBuffer.HeaderPointer != IntPtr.Zero)
            {
                waveOutUnprepareHeader(_waveOutHandle, queuedBuffer.HeaderPointer, Marshal.SizeOf(typeof(WaveHeader)));
            }

            if (queuedBuffer.HeaderPointer != IntPtr.Zero)
            {
                Marshal.FreeHGlobal(queuedBuffer.HeaderPointer);
            }

            if (queuedBuffer.DataPointer != IntPtr.Zero)
            {
                Marshal.FreeHGlobal(queuedBuffer.DataPointer);
            }

            _queuedBytes = Math.Max(0, _queuedBytes - queuedBuffer.Length);
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
            if (_disposed)
            {
                throw new ObjectDisposedException(nameof(WinMmAudioOutput));
            }
        }

        [DllImport("winmm.dll", SetLastError = false)]
        private static extern int waveOutOpen(
            out IntPtr hWaveOut,
            int uDeviceID,
            ref WaveFormatEx lpFormat,
            IntPtr dwCallback,
            IntPtr dwInstance,
            int fdwOpen);

        [DllImport("winmm.dll", SetLastError = false)]
        private static extern int waveOutPrepareHeader(IntPtr hWaveOut, IntPtr waveHeader, int waveHeaderSize);

        [DllImport("winmm.dll", SetLastError = false)]
        private static extern int waveOutWrite(IntPtr hWaveOut, IntPtr waveHeader, int waveHeaderSize);

        [DllImport("winmm.dll", SetLastError = false)]
        private static extern int waveOutUnprepareHeader(IntPtr hWaveOut, IntPtr waveHeader, int waveHeaderSize);

        [DllImport("winmm.dll", SetLastError = false)]
        private static extern int waveOutReset(IntPtr hWaveOut);

        [DllImport("winmm.dll", SetLastError = false)]
        private static extern int waveOutClose(IntPtr hWaveOut);

        [DllImport("winmm.dll", SetLastError = false)]
        private static extern int waveOutGetPosition(IntPtr hWaveOut, ref MmTime time, int timeSize);

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
            public QueuedBuffer(IntPtr headerPointer, IntPtr dataPointer, int length)
            {
                HeaderPointer = headerPointer;
                DataPointer = dataPointer;
                Length = length;
            }

            public IntPtr HeaderPointer { get; }

            public IntPtr DataPointer { get; }

            public int Length { get; }
        }
    }
}
