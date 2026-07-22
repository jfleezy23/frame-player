using System;
using System.Globalization;

namespace FramePlayer.Engines.FFmpeg
{
    internal static class FfmpegMediaResourceLimits
    {
        private const long MiB = 1024L * 1024L;

        // Supports 8K BGRA while preventing a single decoded frame from consuming the process budget.
        internal const long AbsoluteDecodedFrameByteLimit = 256L * MiB;
        internal const long AbsoluteDecodedFramePixelLimit = AbsoluteDecodedFrameByteLimit / 4L;
        internal const long ManagedDecodedFrameOverheadBytes = 512L;

        // A global index is optional. Beyond these bounds the engine keeps sequential decode available.
        internal const int GlobalFrameIndexEntryLimit = 500_000;
        internal const long GlobalFrameIndexNativeByteLimit = 64L * MiB;
        internal const long GlobalFrameIndexManagedByteLimit = 256L * MiB;
        internal const long EstimatedManagedGlobalFrameIndexBytesPerEntry = 512L;
        internal static readonly TimeSpan GlobalFrameIndexTimeLimit = TimeSpan.FromMinutes(2d);

        internal static long ResolveDecodedFrameByteLimit(long paneBudgetBytes)
        {
            return paneBudgetBytes > 0L
                ? Math.Min(paneBudgetBytes, AbsoluteDecodedFrameByteLimit)
                : AbsoluteDecodedFrameByteLimit;
        }

        internal static long EnsureBgraFrameWithinLimit(int width, int height, long maxBytes)
        {
            if (width <= 0 || height <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(width), "Decoded frame dimensions must be positive.");
            }

            long byteCount;
            try
            {
                byteCount = checked((long)width * height * 4L);
            }
            catch (OverflowException ex)
            {
                throw new FfmpegMediaResourceLimitException("Decoded BGRA frame dimensions overflow the supported byte count.", ex);
            }

            if (maxBytes <= 0L || byteCount > maxBytes)
            {
                throw new FfmpegMediaResourceLimitException(string.Format(
                    CultureInfo.InvariantCulture,
                    "Decoded BGRA frame requires {0:N0} bytes, exceeding the {1:N0}-byte frame limit.",
                    byteCount,
                    Math.Max(0L, maxBytes)));
            }

            return byteCount;
        }

        internal static bool TryReserveBytes(long currentBytes, long requestedBytes, long maxBytes, out long totalBytes)
        {
            totalBytes = 0L;
            if (currentBytes < 0L || requestedBytes < 0L || maxBytes < 0L || currentBytes > maxBytes)
            {
                return false;
            }

            if (requestedBytes > maxBytes - currentBytes)
            {
                return false;
            }

            totalBytes = currentBytes + requestedBytes;
            return true;
        }

        internal static bool TryReserveDecodedFrameBytes(
            long currentBytes,
            long pixelBytes,
            long additionalMetadataBytes,
            long maxBytes,
            out long totalBytes)
        {
            totalBytes = 0L;
            if (!TryReserveBytes(
                currentBytes,
                ManagedDecodedFrameOverheadBytes,
                maxBytes,
                out var bytesWithManagedOverhead) ||
                !TryReserveBytes(
                    bytesWithManagedOverhead,
                    additionalMetadataBytes,
                    maxBytes,
                    out var bytesWithAllOverhead))
            {
                return false;
            }

            return TryReserveBytes(bytesWithAllOverhead, pixelBytes, maxBytes, out totalBytes);
        }

        internal static void EnsureGlobalFrameIndexCapacity(int currentEntryCount, TimeSpan elapsed)
        {
            ArgumentOutOfRangeException.ThrowIfNegative(currentEntryCount);

            // The entry limit is the managed-memory boundary: its worst-case estimate is
            // kept within GlobalFrameIndexManagedByteLimit by a locked test invariant.
            if (currentEntryCount >= GlobalFrameIndexEntryLimit)
            {
                throw new FfmpegMediaResourceLimitException(
                    "Exact frame indexing reached its retained-entry limit; sequential decode remains available.");
            }

            if (elapsed > GlobalFrameIndexTimeLimit)
            {
                throw new FfmpegMediaResourceLimitException(
                    "Exact frame indexing reached its processing-time limit; sequential decode remains available.");
            }
        }
    }

    internal sealed class FfmpegMediaResourceLimitException : InvalidOperationException
    {
        internal FfmpegMediaResourceLimitException(string message)
            : base(message)
        {
        }

        internal FfmpegMediaResourceLimitException(string message, Exception innerException)
            : base(message, innerException)
        {
        }
    }
}
