using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.InteropServices;
using System.Threading;

namespace FramePlayer.Engines.FFmpeg
{
    [SuppressMessage("Interoperability", "SYSLIB1054:Use LibraryImportAttribute instead of DllImportAttribute", Justification = "AudioQueue interop stays on DllImport for this preview to avoid changing native callback marshalling during release stabilization.")]
    internal sealed class MacAudioQueueOutput : IAudioOutput
    {
        private const uint AudioFormatLinearPcm = 0x6C70636D;
        private const uint AudioFormatFlagIsSignedInteger = 0x4;
        private const uint AudioFormatFlagIsPacked = 0x8;
        private const int MaxQueuedMilliseconds = 700;
        private const int PollMilliseconds = 5;

        private readonly object _sync = new object();
        [SuppressMessage("Minor Code Smell", "S1450:Private fields only used as local variables in methods should become local variables", Justification = "The callback delegate is stored to keep it alive while AudioQueue owns the native function pointer.")]
        private readonly AudioQueueOutputCallback _callback;
        private readonly GCHandle _selfHandle;
        private readonly int _bytesPerSecond;
        private readonly int _maxQueuedBytes;
        private readonly HashSet<IntPtr> _queuedBuffers = new HashSet<IntPtr>();
        private IntPtr _queue;
        private long _submittedBytes;
        private long _playedBytes;
        private bool _disposed;

        public MacAudioQueueOutput(int sampleRate, int channelCount, int bitsPerSample)
        {
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(sampleRate);
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(channelCount);
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(bitsPerSample);

            var bytesPerFrame = checked(channelCount * bitsPerSample / 8);
            _bytesPerSecond = checked(sampleRate * bytesPerFrame);
            _maxQueuedBytes = Math.Max(_bytesPerSecond * MaxQueuedMilliseconds / 1000, bytesPerFrame);
            _callback = AudioQueueOutputCallbackTrampoline;
            _selfHandle = GCHandle.Alloc(this);

            var description = new AudioStreamBasicDescription
            {
                SampleRate = sampleRate,
                FormatId = AudioFormatLinearPcm,
                FormatFlags = AudioFormatFlagIsSignedInteger | AudioFormatFlagIsPacked,
                BytesPerPacket = (uint)bytesPerFrame,
                FramesPerPacket = 1,
                BytesPerFrame = (uint)bytesPerFrame,
                ChannelsPerFrame = (uint)channelCount,
                BitsPerChannel = (uint)bitsPerSample,
                Reserved = 0
            };

            var status = AudioQueueNewOutput(
                ref description,
                _callback,
                GCHandle.ToIntPtr(_selfHandle),
                IntPtr.Zero,
                IntPtr.Zero,
                0,
                out _queue);
            ThrowIfAudioQueueError(status, "Create macOS audio queue");

            status = AudioQueueStart(_queue, IntPtr.Zero);
            ThrowIfAudioQueueError(status, "Start macOS audio queue");
        }

        public bool IsOpen
        {
            get { return !_disposed && _queue != IntPtr.Zero; }
        }

        public long SubmittedBytes
        {
            get { return Interlocked.Read(ref _submittedBytes); }
        }

        public TimeSpan PlayedDuration
        {
            get { return BytesToDuration(Interlocked.Read(ref _playedBytes)); }
        }

        public void Write(byte[] buffer, CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(buffer);

            if (buffer.Length == 0)
            {
                return;
            }

            ThrowIfDisposed();
            WaitForQueueRoom(buffer.Length, cancellationToken);

            IntPtr audioBuffer;
            var status = AudioQueueAllocateBuffer(_queue, (uint)buffer.Length, out audioBuffer);
            ThrowIfAudioQueueError(status, "Allocate macOS audio buffer");

            try
            {
                var audioQueueBuffer = Marshal.PtrToStructure<AudioQueueBuffer>(audioBuffer);
                Marshal.Copy(buffer, 0, audioQueueBuffer.AudioData, buffer.Length);
                audioQueueBuffer.AudioDataByteSize = (uint)buffer.Length;
                Marshal.StructureToPtr(audioQueueBuffer, audioBuffer, false);

                lock (_sync)
                {
                    _queuedBuffers.Add(audioBuffer);
                    Interlocked.Add(ref _submittedBytes, buffer.Length);
                }

                status = AudioQueueEnqueueBuffer(_queue, audioBuffer, 0, IntPtr.Zero);
                ThrowIfAudioQueueError(status, "Enqueue macOS audio buffer");
                audioBuffer = IntPtr.Zero;
            }
            finally
            {
                if (audioBuffer != IntPtr.Zero)
                {
                    TraceAudioQueueStatus("Free macOS audio buffer", AudioQueueFreeBuffer(_queue, audioBuffer));
                }
            }
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            if (_queue != IntPtr.Zero)
            {
                TraceAudioQueueStatus("Stop macOS audio queue", AudioQueueStop(_queue, true));
                TraceAudioQueueStatus("Dispose macOS audio queue", AudioQueueDispose(_queue, true));
                _queue = IntPtr.Zero;
            }

            if (_selfHandle.IsAllocated)
            {
                _selfHandle.Free();
            }

            lock (_sync)
            {
                _queuedBuffers.Clear();
                Monitor.PulseAll(_sync);
            }
        }

        private void WaitForQueueRoom(int nextBufferLength, CancellationToken cancellationToken)
        {
            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();
                ThrowIfDisposed();

                var queuedBytes = SubmittedBytes - Interlocked.Read(ref _playedBytes);
                if (queuedBytes + nextBufferLength <= _maxQueuedBytes || queuedBytes <= 0)
                {
                    return;
                }

                lock (_sync)
                {
                    Monitor.Wait(_sync, PollMilliseconds);
                }
            }
        }

        private void OnBufferCompleted(IntPtr buffer)
        {
            if (buffer == IntPtr.Zero)
            {
                return;
            }

            var byteCount = 0L;
            try
            {
                var audioQueueBuffer = Marshal.PtrToStructure<AudioQueueBuffer>(buffer);
                byteCount = audioQueueBuffer.AudioDataByteSize;
            }
            finally
            {
                if (_queue != IntPtr.Zero)
                {
                    TraceAudioQueueStatus("Free completed macOS audio buffer", AudioQueueFreeBuffer(_queue, buffer));
                }
            }

            lock (_sync)
            {
                _queuedBuffers.Remove(buffer);
                if (byteCount > 0)
                {
                    Interlocked.Add(ref _playedBytes, byteCount);
                }

                Monitor.PulseAll(_sync);
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

        private void ThrowIfDisposed()
        {
            ObjectDisposedException.ThrowIf(_disposed || _queue == IntPtr.Zero, this);
        }

        private static void AudioQueueOutputCallbackTrampoline(
            IntPtr userData,
            IntPtr audioQueue,
            IntPtr audioQueueBuffer)
        {
            if (userData == IntPtr.Zero)
            {
                return;
            }

            var handle = GCHandle.FromIntPtr(userData);
            if (handle.Target is MacAudioQueueOutput output)
            {
                output.OnBufferCompleted(audioQueueBuffer);
            }
        }

        private static void ThrowIfAudioQueueError(int status, string operation)
        {
            if (status != 0)
            {
                throw new InvalidOperationException(operation + " failed with AudioQueue status " + status + ".");
            }
        }

        private static void TraceAudioQueueStatus(string operation, int status)
        {
            if (status != 0)
            {
                Debug.WriteLine(operation + " returned AudioQueue status " + status + ".");
            }
        }

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate void AudioQueueOutputCallback(IntPtr userData, IntPtr audioQueue, IntPtr audioQueueBuffer);

        [StructLayout(LayoutKind.Sequential)]
        private struct AudioStreamBasicDescription
        {
            public double SampleRate;
            public uint FormatId;
            public uint FormatFlags;
            public uint BytesPerPacket;
            public uint FramesPerPacket;
            public uint BytesPerFrame;
            public uint ChannelsPerFrame;
            public uint BitsPerChannel;
            public uint Reserved;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct AudioQueueBuffer
        {
            public uint AudioDataBytesCapacity;
            public IntPtr AudioData;
            public uint AudioDataByteSize;
            public IntPtr UserData;
            public uint PacketDescriptionCapacity;
            public IntPtr PacketDescriptions;
            public uint PacketDescriptionCount;
        }

        [DllImport("/System/Library/Frameworks/AudioToolbox.framework/AudioToolbox")]
        private static extern int AudioQueueNewOutput(
            ref AudioStreamBasicDescription format,
            AudioQueueOutputCallback callback,
            IntPtr userData,
            IntPtr callbackRunLoop,
            IntPtr callbackRunLoopMode,
            uint flags,
            out IntPtr audioQueue);

        [DllImport("/System/Library/Frameworks/AudioToolbox.framework/AudioToolbox")]
        private static extern int AudioQueueStart(IntPtr audioQueue, IntPtr startTime);

        [DllImport("/System/Library/Frameworks/AudioToolbox.framework/AudioToolbox")]
        private static extern int AudioQueueStop(IntPtr audioQueue, bool immediate);

        [DllImport("/System/Library/Frameworks/AudioToolbox.framework/AudioToolbox")]
        private static extern int AudioQueueDispose(IntPtr audioQueue, bool immediate);

        [DllImport("/System/Library/Frameworks/AudioToolbox.framework/AudioToolbox")]
        private static extern int AudioQueueAllocateBuffer(IntPtr audioQueue, uint bufferByteSize, out IntPtr audioQueueBuffer);

        [DllImport("/System/Library/Frameworks/AudioToolbox.framework/AudioToolbox")]
        private static extern int AudioQueueEnqueueBuffer(
            IntPtr audioQueue,
            IntPtr audioQueueBuffer,
            uint packetDescriptionCount,
            IntPtr packetDescriptions);

        [DllImport("/System/Library/Frameworks/AudioToolbox.framework/AudioToolbox")]
        private static extern int AudioQueueFreeBuffer(IntPtr audioQueue, IntPtr audioQueueBuffer);
    }
}
