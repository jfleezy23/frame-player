using System;
using System.Runtime.InteropServices;

namespace FramePlayer.Engines.FFmpeg
{
    internal static class AudioOutputFactory
    {
        public static IAudioOutput Create(int sampleRate, int channelCount, int bitsPerSample)
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                return new WinMmAudioOutput(sampleRate, channelCount, bitsPerSample);
            }

            if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            {
                return new MacAudioQueueOutput(sampleRate, channelCount, bitsPerSample);
            }

            return new ManagedAudioClockOutput(sampleRate, channelCount, bitsPerSample);
        }
    }
}
