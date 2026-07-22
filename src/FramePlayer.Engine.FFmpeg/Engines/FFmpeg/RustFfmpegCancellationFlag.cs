using System;
using System.Runtime.InteropServices;
using System.Threading;

namespace FramePlayer.Engines.FFmpeg
{
    internal sealed class RustFfmpegCancellationFlag : IDisposable
    {
        private readonly int[] _state = new int[1];
        private GCHandle _pinnedState;

        public RustFfmpegCancellationFlag()
        {
            _pinnedState = GCHandle.Alloc(_state, GCHandleType.Pinned);
        }

        ~RustFfmpegCancellationFlag()
        {
            ReleasePinnedState();
        }

        public IntPtr Pointer
        {
            get
            {
                ObjectDisposedException.ThrowIf(!_pinnedState.IsAllocated, this);
                return _pinnedState.AddrOfPinnedObject();
            }
        }

        public CancellationTokenRegistration Register(CancellationToken cancellationToken)
        {
            return cancellationToken.Register(
                static state => ((RustFfmpegCancellationFlag)state).Signal(),
                this);
        }

        public void Dispose()
        {
            ReleasePinnedState();
            GC.SuppressFinalize(this);
        }

        private void Signal()
        {
            Interlocked.Exchange(ref _state[0], 1);
        }

        private void ReleasePinnedState()
        {
            if (_pinnedState.IsAllocated)
            {
                _pinnedState.Free();
            }
        }
    }
}
