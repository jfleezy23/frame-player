using System;
using System.Runtime.InteropServices;
using System.Threading;
using FramePlayer.Core.Abstractions;

namespace FramePlayer.Engines.FFmpeg
{
    internal sealed class SdlAudioOutput : IAudioOutput
    {
        private const uint SdlInitAudio = 0x00000010;
        private const ushort AudioS16Sys = 0x8010;
        private const int MaxQueuedMilliseconds = 700;
        private const int PollMilliseconds = 5;

        private readonly int _bytesPerSecond;
        private readonly int _maxQueuedBytes;
        private uint _deviceId;
        private long _submittedBytes;
        private bool _disposed;
        private bool _audioInitialized;

        public SdlAudioOutput(int sampleRate, int channelCount, int bitsPerSample)
        {
            if (sampleRate <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(sampleRate));
            }

            if (channelCount <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(channelCount));
            }

            if (bitsPerSample != 16)
            {
                throw new NotSupportedException("The SDL audio bridge currently supports 16-bit PCM output only.");
            }

            var blockAlign = checked(channelCount * bitsPerSample / 8);
            _bytesPerSecond = checked(sampleRate * blockAlign);
            _maxQueuedBytes = Math.Max(_bytesPerSecond * MaxQueuedMilliseconds / 1000, blockAlign);

            ThrowIfNegative(SdlInitSubSystem(SdlInitAudio), "Initialize SDL audio");
            _audioInitialized = true;

            var desired = new SdlAudioSpec
            {
                Frequency = sampleRate,
                Format = AudioS16Sys,
                Channels = (byte)channelCount,
                Samples = 4096
            };
            _deviceId = SdlOpenAudioDevice(IntPtr.Zero, 0, ref desired, IntPtr.Zero, 0);
            if (_deviceId == 0)
            {
                throw new InvalidOperationException("Open SDL audio output failed: " + GetSdlErrorMessage());
            }

            SdlPauseAudioDevice(_deviceId, 0);
        }

        public bool IsOpen
        {
            get { return !_disposed && _deviceId != 0; }
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

                var queuedBytes = (long)SdlGetQueuedAudioSize(_deviceId);
                return BytesToDuration(Math.Max(0L, SubmittedBytes - queuedBytes));
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

            var handle = GCHandle.Alloc(buffer, GCHandleType.Pinned);
            try
            {
                ThrowIfNegative(
                    SdlQueueAudio(_deviceId, handle.AddrOfPinnedObject(), (uint)buffer.Length),
                    "Queue SDL audio");
                Interlocked.Add(ref _submittedBytes, buffer.Length);
            }
            finally
            {
                handle.Free();
            }
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;

            if (_deviceId != 0)
            {
                SdlClearQueuedAudio(_deviceId);
                SdlCloseAudioDevice(_deviceId);
                _deviceId = 0;
            }

            if (_audioInitialized)
            {
                SdlQuitSubSystem(SdlInitAudio);
                _audioInitialized = false;
            }
        }

        private void WaitForQueueRoom(int nextBufferLength, CancellationToken cancellationToken)
        {
            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var queuedBytes = (int)SdlGetQueuedAudioSize(_deviceId);
                if (queuedBytes + nextBufferLength <= _maxQueuedBytes || queuedBytes == 0)
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

        private static void ThrowIfNegative(int result, string operation)
        {
            if (result >= 0)
            {
                return;
            }

            throw new InvalidOperationException(operation + " failed: " + GetSdlErrorMessage());
        }

        private static string GetSdlErrorMessage()
        {
            var errorPointer = SdlGetError();
            return errorPointer != IntPtr.Zero
                ? Marshal.PtrToStringAnsi(errorPointer) ?? "unknown SDL error"
                : "unknown SDL error";
        }

        private void ThrowIfDisposed()
        {
            if (_disposed)
            {
                throw new ObjectDisposedException(nameof(SdlAudioOutput));
            }
        }

        [DllImport("SDL2", CallingConvention = CallingConvention.Cdecl, EntryPoint = "SDL_InitSubSystem")]
        private static extern int SdlInitSubSystem(uint flags);

        [DllImport("SDL2", CallingConvention = CallingConvention.Cdecl, EntryPoint = "SDL_QuitSubSystem")]
        private static extern void SdlQuitSubSystem(uint flags);

        [DllImport("SDL2", CallingConvention = CallingConvention.Cdecl, EntryPoint = "SDL_OpenAudioDevice")]
        private static extern uint SdlOpenAudioDevice(
            IntPtr device,
            int isCapture,
            ref SdlAudioSpec desired,
            IntPtr obtained,
            int allowedChanges);

        [DllImport("SDL2", CallingConvention = CallingConvention.Cdecl, EntryPoint = "SDL_PauseAudioDevice")]
        private static extern void SdlPauseAudioDevice(uint deviceId, int pauseOn);

        [DllImport("SDL2", CallingConvention = CallingConvention.Cdecl, EntryPoint = "SDL_QueueAudio")]
        private static extern int SdlQueueAudio(uint deviceId, IntPtr buffer, uint length);

        [DllImport("SDL2", CallingConvention = CallingConvention.Cdecl, EntryPoint = "SDL_GetQueuedAudioSize")]
        private static extern uint SdlGetQueuedAudioSize(uint deviceId);

        [DllImport("SDL2", CallingConvention = CallingConvention.Cdecl, EntryPoint = "SDL_ClearQueuedAudio")]
        private static extern void SdlClearQueuedAudio(uint deviceId);

        [DllImport("SDL2", CallingConvention = CallingConvention.Cdecl, EntryPoint = "SDL_CloseAudioDevice")]
        private static extern void SdlCloseAudioDevice(uint deviceId);

        [DllImport("SDL2", CallingConvention = CallingConvention.Cdecl, EntryPoint = "SDL_GetError")]
        private static extern IntPtr SdlGetError();

        [StructLayout(LayoutKind.Sequential)]
        private struct SdlAudioSpec
        {
            public int Frequency;
            public ushort Format;
            public byte Channels;
            public byte Silence;
            public ushort Samples;
            public uint Size;
            public IntPtr Callback;
            public IntPtr UserData;
        }
    }
}
