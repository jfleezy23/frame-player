using System;
using System.Collections.Generic;
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
        private static readonly object NativeLibraryLoadLock = new object();
        private static readonly List<IntPtr> LoadedNativeLibraries = new List<IntPtr>();
        private static IntPtr AvformatModuleHandle;
        private static readonly AVRational AvTimeBaseRational = new AVRational
        {
            num = 1,
            den = ffmpeg.AV_TIME_BASE
        };
        private static readonly Lazy<avformat_open_input_utf8_delegate> AvformatOpenInputUtf8 =
            new Lazy<avformat_open_input_utf8_delegate>(LoadAvformatOpenInputUtf8);
        private static readonly Lazy<avformat_close_input_delegate> AvformatCloseInput =
            new Lazy<avformat_close_input_delegate>(LoadAvformatCloseInput);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate int avformat_open_input_utf8_delegate(
            AVFormatContext** ps,
            byte* url,
            AVInputFormat* fmt,
            AVDictionary** options);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate void avformat_close_input_delegate(AVFormatContext** ps);

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

        internal static string GetColorRangeName(AVColorRange colorRange)
        {
            return ffmpeg.av_color_range_name(colorRange) ?? string.Empty;
        }

        internal static string GetColorPrimariesName(AVColorPrimaries colorPrimaries)
        {
            return ffmpeg.av_color_primaries_name(colorPrimaries) ?? string.Empty;
        }

        internal static string GetColorTransferName(AVColorTransferCharacteristic colorTransfer)
        {
            return ffmpeg.av_color_transfer_name(colorTransfer) ?? string.Empty;
        }

        internal static string GetColorSpaceName(AVColorSpace colorSpace)
        {
            return ffmpeg.av_color_space_name(colorSpace) ?? string.Empty;
        }

        internal static int? GetPixelFormatBitDepth(AVPixelFormat pixelFormat)
        {
            if (pixelFormat == AVPixelFormat.AV_PIX_FMT_NONE)
            {
                return null;
            }

            var descriptor = ffmpeg.av_pix_fmt_desc_get(pixelFormat);
            if (descriptor == null || descriptor->nb_components <= 0)
            {
                return null;
            }

            var depth = descriptor->comp[0].depth;
            return depth > 0
                ? (int?)depth
                : null;
        }

        internal static int? GetBitDepth(AVCodecContext* codecContext, AVCodecParameters* codecParameters, AVPixelFormat pixelFormat)
        {
            if (codecContext != null)
            {
                if (codecContext->bits_per_raw_sample > 0)
                {
                    return codecContext->bits_per_raw_sample;
                }

                if (codecContext->bits_per_coded_sample > 0)
                {
                    return codecContext->bits_per_coded_sample;
                }
            }

            if (codecParameters != null)
            {
                if (codecParameters->bits_per_raw_sample > 0)
                {
                    return codecParameters->bits_per_raw_sample;
                }

                if (codecParameters->bits_per_coded_sample > 0)
                {
                    return codecParameters->bits_per_coded_sample;
                }
            }

            return GetPixelFormatBitDepth(pixelFormat);
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

        internal static void CloseInput(AVFormatContext** formatContext)
        {
            if (formatContext == null || *formatContext == null)
            {
                return;
            }

            AvformatCloseInput.Value(formatContext);
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

        internal static void GetDisplayDimensions(
            AVFormatContext* formatContext,
            AVStream* stream,
            AVFrame* frame,
            out int displayWidth,
            out int displayHeight)
        {
            displayWidth = frame != null ? Math.Max(0, frame->width) : 0;
            displayHeight = frame != null ? Math.Max(0, frame->height) : 0;
            if (frame == null || displayWidth <= 0 || displayHeight <= 0)
            {
                return;
            }

            var sampleAspectRatio = ffmpeg.av_guess_sample_aspect_ratio(formatContext, stream, frame);
            if (!IsValid(sampleAspectRatio) ||
                sampleAspectRatio.num <= 0 ||
                sampleAspectRatio.den <= 0 ||
                sampleAspectRatio.num == sampleAspectRatio.den)
            {
                return;
            }

            if (sampleAspectRatio.num > sampleAspectRatio.den)
            {
                displayWidth = Math.Max(
                    1,
                    (int)Math.Round(
                        displayWidth * sampleAspectRatio.num / (double)sampleAspectRatio.den,
                        MidpointRounding.AwayFromZero));
                return;
            }

            displayHeight = Math.Max(
                1,
                (int)Math.Round(
                    displayHeight * sampleAspectRatio.den / (double)sampleAspectRatio.num,
                    MidpointRounding.AwayFromZero));
        }

        internal static bool TryReduceRatio(int numerator, int denominator, out int reducedNumerator, out int reducedDenominator)
        {
            reducedNumerator = 0;
            reducedDenominator = 0;
            if (numerator <= 0 || denominator <= 0)
            {
                return false;
            }

            var divisor = GreatestCommonDivisor(numerator, denominator);
            reducedNumerator = numerator / divisor;
            reducedDenominator = denominator / divisor;
            return true;
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
            if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                availablePhysicalMemoryBytes = 0L;
                return false;
            }

            MemoryStatusEx memoryStatus;
            if (!TryGetMemoryStatus(out memoryStatus))
            {
                availablePhysicalMemoryBytes = 0L;
                return false;
            }

            availablePhysicalMemoryBytes = (long)Math.Min(long.MaxValue, (double)memoryStatus.ullAvailPhys);
            return true;
        }

        internal static bool TryGetTotalPhysicalMemoryBytes(out long totalPhysicalMemoryBytes)
        {
            if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                var memoryInfo = GC.GetGCMemoryInfo();
                totalPhysicalMemoryBytes = memoryInfo.TotalAvailableMemoryBytes > 0L
                    ? memoryInfo.TotalAvailableMemoryBytes
                    : 0L;
                return totalPhysicalMemoryBytes > 0L;
            }

            MemoryStatusEx memoryStatus;
            if (!TryGetMemoryStatus(out memoryStatus))
            {
                totalPhysicalMemoryBytes = 0L;
                return false;
            }

            totalPhysicalMemoryBytes = (long)Math.Min(long.MaxValue, (double)memoryStatus.ullTotalPhys);
            return true;
        }

        private static bool TryGetMemoryStatus(out MemoryStatusEx memoryStatus)
        {
            memoryStatus = new MemoryStatusEx();
            memoryStatus.dwLength = (uint)Marshal.SizeOf(typeof(MemoryStatusEx));
            return GlobalMemoryStatusEx(ref memoryStatus);
        }

        private static int GreatestCommonDivisor(int left, int right)
        {
            left = Math.Abs(left);
            right = Math.Abs(right);
            while (right != 0)
            {
                var remainder = left % right;
                left = right;
                right = remainder;
            }

            return left == 0
                ? 1
                : left;
        }

        private static avformat_open_input_utf8_delegate LoadAvformatOpenInputUtf8()
        {
            return LoadAvformatExport<avformat_open_input_utf8_delegate>("avformat_open_input");
        }

        private static avformat_close_input_delegate LoadAvformatCloseInput()
        {
            return LoadAvformatExport<avformat_close_input_delegate>("avformat_close_input");
        }

        private static TDelegate LoadAvformatExport<TDelegate>(string exportName)
            where TDelegate : Delegate
        {
            var moduleHandle = LoadAvformatModule();

            var exportAddress = RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
                ? GetProcAddress(moduleHandle, exportName)
                : NativeLibrary.GetExport(moduleHandle, exportName);
            if (exportAddress == IntPtr.Zero)
            {
                throw new InvalidOperationException("Could not resolve " + exportName + " from the loaded FFmpeg avformat runtime library.");
            }

            return Marshal.GetDelegateForFunctionPointer<TDelegate>(exportAddress);
        }

        private static IntPtr LoadAvformatModule()
        {
            lock (NativeLibraryLoadLock)
            {
                if (AvformatModuleHandle != IntPtr.Zero)
                {
                    return AvformatModuleHandle;
                }

                var runtimeDirectory = ffmpeg.RootPath;
                if (string.IsNullOrWhiteSpace(runtimeDirectory))
                {
                    throw new InvalidOperationException("FFmpeg RootPath is not configured.");
                }

                var dependencies = GetRuntimeLibraryLoadOrder();

                var moduleHandle = IntPtr.Zero;
                foreach (var dependency in dependencies)
                {
                    moduleHandle = LoadRuntimeLibrary(runtimeDirectory, dependency);
                }

                if (moduleHandle == IntPtr.Zero)
                {
                    throw new InvalidOperationException("Could not load " + dependencies[dependencies.Length - 1] + " from the configured FFmpeg runtime path.");
                }

                AvformatModuleHandle = moduleHandle;
                return AvformatModuleHandle;
            }
        }

        private static string[] GetRuntimeLibraryLoadOrder()
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            {
                return new[]
                {
                    "libavutil.60.dylib",
                    "libswresample.6.dylib",
                    "libswscale.9.dylib",
                    "libavfilter.11.dylib",
                    "libavcodec.62.dylib",
                    "libavformat.62.dylib"
                };
            }

            if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            {
                return new[]
                {
                    "libavutil.so.60",
                    "libswresample.so.6",
                    "libswscale.so.9",
                    "libavcodec.so.62",
                    "libavformat.so.62"
                };
            }

            return new[]
            {
                "libwinpthread-1.dll",
                "avutil-60.dll",
                "swresample-6.dll",
                "swscale-9.dll",
                "avcodec-62.dll",
                AvformatLibraryName
            };
        }

        private static IntPtr LoadRuntimeLibrary(string runtimeDirectory, string libraryName)
        {
            var dependencyPath = Path.Combine(runtimeDirectory, libraryName);
            if (!File.Exists(dependencyPath))
            {
                if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows) &&
                    string.Equals(libraryName, "libwinpthread-1.dll", StringComparison.OrdinalIgnoreCase))
                {
                    return IntPtr.Zero;
                }

                throw new InvalidOperationException("Could not find required FFmpeg runtime library " + libraryName + ".");
            }

            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                var existingHandle = GetModuleHandle(libraryName);
                if (existingHandle != IntPtr.Zero)
                {
                    return existingHandle;
                }

                var loadedHandle = LoadLibrary(dependencyPath);
                if (loadedHandle == IntPtr.Zero)
                {
                    throw new InvalidOperationException(
                        string.Format(
                            CultureInfo.InvariantCulture,
                            "Could not load {0} from the configured FFmpeg runtime path. Win32 error: {1}.",
                            libraryName,
                            Marshal.GetLastWin32Error()));
                }

                return loadedHandle;
            }

            var handle = NativeLibrary.Load(dependencyPath);
            LoadedNativeLibraries.Add(handle);
            return handle;
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
