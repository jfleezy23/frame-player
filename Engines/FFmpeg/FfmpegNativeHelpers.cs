using System;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using FFmpeg.AutoGen;

namespace FramePlayer.Engines.FFmpeg
{
    internal static unsafe class FfmpegNativeHelpers
    {
        private const string AvformatLibraryName = "avformat-62.dll";
        private static readonly AVRational AvTimeBaseRational = new AVRational
        {
            num = 1,
            den = ffmpeg.AV_TIME_BASE
        };
        private static readonly Lazy<avformat_open_input_utf8_delegate> AvformatOpenInputUtf8 =
            new Lazy<avformat_open_input_utf8_delegate>(LoadAvformatOpenInputUtf8);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate int avformat_open_input_utf8_delegate(
            AVFormatContext** ps,
            byte* url,
            AVInputFormat* fmt,
            AVDictionary** options);

        internal static void ThrowIfError(int errorCode, string operation)
        {
            if (errorCode >= 0)
            {
                return;
            }

            throw new InvalidOperationException(string.Format(
                CultureInfo.InvariantCulture,
                "{0} failed: {1}",
                operation,
                GetErrorMessage(errorCode)));
        }

        internal static string GetErrorMessage(int errorCode)
        {
            const int BufferSize = 1024;
            var buffer = stackalloc byte[BufferSize];
            ffmpeg.av_strerror(errorCode, buffer, (ulong)BufferSize);
            return PtrToString(buffer);
        }

        internal static string PtrToString(byte* value)
        {
            return value == null
                ? string.Empty
                : Marshal.PtrToStringAnsi((IntPtr)value) ?? string.Empty;
        }

        internal static string GetCodecName(AVCodecID codecId)
        {
            var name = ffmpeg.avcodec_get_name(codecId);
            return string.IsNullOrWhiteSpace(name)
                ? codecId.ToString()
                : name;
        }

        internal static string GetPixelFormatName(AVPixelFormat pixelFormat)
        {
            var name = ffmpeg.av_get_pix_fmt_name(pixelFormat);
            return string.IsNullOrWhiteSpace(name)
                ? pixelFormat.ToString()
                : name;
        }

        internal static int OpenInput(AVFormatContext** formatContext, string filePath, AVInputFormat* inputFormat, AVDictionary** options)
        {
            if (string.IsNullOrWhiteSpace(filePath))
            {
                throw new ArgumentException("A media file path is required.", nameof(filePath));
            }

            var encodedPath = Encoding.UTF8.GetBytes(filePath + "\0");
            fixed (byte* encodedPathPointer = encodedPath)
            {
                return AvformatOpenInputUtf8.Value(formatContext, encodedPathPointer, inputFormat, options);
            }
        }

        internal static bool IsValid(AVRational rational)
        {
            return rational.num != 0 && rational.den != 0;
        }

        internal static double ToDouble(AVRational rational)
        {
            return IsValid(rational)
                ? rational.num / (double)rational.den
                : 0d;
        }

        internal static TimeSpan ToTimeSpan(long timestamp, AVRational timeBase)
        {
            if (!IsValid(timeBase) || timestamp == ffmpeg.AV_NOPTS_VALUE)
            {
                return TimeSpan.Zero;
            }

            var seconds = timestamp * timeBase.num / (double)timeBase.den;
            if (double.IsNaN(seconds) || double.IsInfinity(seconds))
            {
                return TimeSpan.Zero;
            }

            return TimeSpan.FromTicks(checked((long)Math.Round(
                seconds * TimeSpan.TicksPerSecond,
                MidpointRounding.AwayFromZero)));
        }

        internal static TimeSpan ToTimeSpanFromAvTime(long timestamp)
        {
            if (timestamp == ffmpeg.AV_NOPTS_VALUE || timestamp <= 0)
            {
                return TimeSpan.Zero;
            }

            var seconds = timestamp / (double)ffmpeg.AV_TIME_BASE;
            return TimeSpan.FromTicks(checked((long)Math.Round(
                seconds * TimeSpan.TicksPerSecond,
                MidpointRounding.AwayFromZero)));
        }

        internal static TimeSpan GetDuration(AVFormatContext* formatContext, AVStream* stream)
        {
            if (stream != null && stream->duration > 0 && IsValid(stream->time_base))
            {
                return ToTimeSpan(stream->duration, stream->time_base);
            }

            if (formatContext != null && formatContext->duration > 0)
            {
                return ToTimeSpanFromAvTime(formatContext->duration);
            }

            return TimeSpan.Zero;
        }

        internal static AVRational GetNominalFrameRate(AVFormatContext* formatContext, AVStream* stream, AVFrame* frame)
        {
            var guessed = ffmpeg.av_guess_frame_rate(formatContext, stream, frame);
            if (IsValid(guessed))
            {
                return guessed;
            }

            if (stream != null)
            {
                if (IsValid(stream->avg_frame_rate))
                {
                    return stream->avg_frame_rate;
                }

                if (IsValid(stream->r_frame_rate))
                {
                    return stream->r_frame_rate;
                }
            }

            return default(AVRational);
        }

        internal static TimeSpan GetPositionStep(AVRational frameRate)
        {
            var framesPerSecond = ToDouble(frameRate);
            if (framesPerSecond <= 0d)
            {
                return TimeSpan.Zero;
            }

            return TimeSpan.FromTicks(checked((long)Math.Round(
                TimeSpan.TicksPerSecond / framesPerSecond,
                MidpointRounding.AwayFromZero)));
        }

        internal static long ToStreamTimestamp(TimeSpan position, AVRational timeBase)
        {
            if (!IsValid(timeBase))
            {
                return 0L;
            }

            var positionInAvTimeBase = checked((long)Math.Round(
                position.TotalSeconds * ffmpeg.AV_TIME_BASE,
                MidpointRounding.AwayFromZero));
            return ffmpeg.av_rescale_q(positionInAvTimeBase, AvTimeBaseRational, timeBase);
        }

        internal static long? GetBestPresentationTimestamp(AVFrame* decodedFrame)
        {
            if (decodedFrame == null)
            {
                return null;
            }

            var bestEffortTimestamp = AsNullableTimestamp(decodedFrame->best_effort_timestamp);
            if (bestEffortTimestamp.HasValue)
            {
                return bestEffortTimestamp;
            }

            var presentationTimestamp = AsNullableTimestamp(decodedFrame->pts);
            if (presentationTimestamp.HasValue)
            {
                return presentationTimestamp;
            }

            return AsNullableTimestamp(decodedFrame->pkt_dts);
        }

        internal static long? GetBestAvailableTimestamp(long? presentationTimestamp, long? decodeTimestamp)
        {
            return presentationTimestamp ?? decodeTimestamp;
        }

        internal static long? AsNullableTimestamp(long timestamp)
        {
            return timestamp == ffmpeg.AV_NOPTS_VALUE
                ? (long?)null
                : timestamp;
        }

        internal static bool TryGetAvailablePhysicalMemoryBytes(out long availablePhysicalMemoryBytes)
        {
            var memoryStatus = new MemoryStatusEx();
            memoryStatus.dwLength = (uint)Marshal.SizeOf(typeof(MemoryStatusEx));
            if (!GlobalMemoryStatusEx(ref memoryStatus))
            {
                availablePhysicalMemoryBytes = 0L;
                return false;
            }

            availablePhysicalMemoryBytes = (long)Math.Min(long.MaxValue, (double)memoryStatus.ullAvailPhys);
            return true;
        }

        private static avformat_open_input_utf8_delegate LoadAvformatOpenInputUtf8()
        {
            var runtimeDirectory = ffmpeg.RootPath;
            if (string.IsNullOrWhiteSpace(runtimeDirectory))
            {
                throw new InvalidOperationException("FFmpeg RootPath is not configured.");
            }

            var dependencies = new[]
            {
                "libwinpthread-1.dll",
                "avutil-60.dll",
                "swresample-6.dll",
                "swscale-9.dll",
                "avcodec-62.dll",
                AvformatLibraryName
            };

            foreach (var dependency in dependencies)
            {
                var dependencyPath = Path.Combine(runtimeDirectory, dependency);
                if (!File.Exists(dependencyPath))
                {
                    if (string.Equals(dependency, "libwinpthread-1.dll", StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    throw new InvalidOperationException("Could not find required FFmpeg runtime library " + dependency + ".");
                }

                if (GetModuleHandle(dependency) == IntPtr.Zero && LoadLibrary(dependencyPath) == IntPtr.Zero)
                {
                    throw new InvalidOperationException(
                        string.Format(
                            CultureInfo.InvariantCulture,
                            "Could not load {0} from the configured FFmpeg runtime path. Win32 error: {1}.",
                            dependency,
                            Marshal.GetLastWin32Error()));
                }
            }

            var moduleHandle = GetModuleHandle(AvformatLibraryName);
            if (moduleHandle == IntPtr.Zero)
            {
                throw new InvalidOperationException("Could not load avformat-62.dll from the configured FFmpeg runtime path.");
            }

            var exportAddress = GetProcAddress(moduleHandle, "avformat_open_input");
            if (exportAddress == IntPtr.Zero)
            {
                throw new InvalidOperationException("Could not resolve avformat_open_input from avformat-62.dll.");
            }

            return Marshal.GetDelegateForFunctionPointer<avformat_open_input_utf8_delegate>(exportAddress);
        }

        [DllImport("kernel32", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern IntPtr LoadLibrary(string lpFileName);

        [DllImport("kernel32", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern IntPtr GetModuleHandle(string lpModuleName);

        [DllImport("kernel32", CharSet = CharSet.Ansi, ExactSpelling = true, SetLastError = true)]
        private static extern IntPtr GetProcAddress(IntPtr hModule, string procName);

        [DllImport("kernel32", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GlobalMemoryStatusEx(ref MemoryStatusEx lpBuffer);

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
        private struct MemoryStatusEx
        {
            public uint dwLength;
            public uint dwMemoryLoad;
            public ulong ullTotalPhys;
            public ulong ullAvailPhys;
            public ulong ullTotalPageFile;
            public ulong ullAvailPageFile;
            public ulong ullTotalVirtual;
            public ulong ullAvailVirtual;
            public ulong ullAvailExtendedVirtual;
        }
    }
}
