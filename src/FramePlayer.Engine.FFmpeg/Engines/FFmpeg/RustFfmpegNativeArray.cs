using System;
using System.Runtime.InteropServices;

namespace FramePlayer.Engines.FFmpeg
{
    internal static class RustFfmpegNativeArray
    {
        public static T Read<T>(IntPtr elements, int index, int count)
            where T : unmanaged
        {
            return Marshal.PtrToStructure<T>(GetElementAddress<T>(elements, index, count));
        }

        public static void Write<T>(IntPtr elements, int index, int count, T value)
            where T : unmanaged
        {
            Marshal.StructureToPtr(value, GetElementAddress<T>(elements, index, count), false);
        }

        private static IntPtr GetElementAddress<T>(IntPtr elements, int index, int count)
            where T : unmanaged
        {
            if (elements == IntPtr.Zero)
            {
                throw new ArgumentException("Native array address cannot be zero.", nameof(elements));
            }

            if (index < 0 || index >= count)
            {
                throw new ArgumentOutOfRangeException(nameof(index));
            }

            var byteOffset = checked(index * NativeElementSize<T>.Value);
            return IntPtr.Add(elements, byteOffset);
        }

        private static class NativeElementSize<T>
            where T : unmanaged
        {
            public static readonly int Value = Marshal.SizeOf<T>();
        }
    }
}
