using System;

namespace FramePlayer.Core.Models
{
    public sealed class FrameStepResult
    {
        public FrameStepResult(bool success, int direction, ReviewPosition position, string message)
            : this(success, direction, position, message, false, false)
        {
        }

        public FrameStepResult(bool success, int direction, ReviewPosition position, string message, bool wasCacheHit)
            : this(success, direction, position, message, wasCacheHit, false)
        {
        }

        public FrameStepResult(
            bool success,
            int direction,
            ReviewPosition position,
            string message,
            bool wasCacheHit,
            bool requiredReconstruction)
        {
            Success = success;
            Direction = direction;
            Position = position ?? ReviewPosition.Empty;
            Message = message ?? string.Empty;
            WasCacheHit = wasCacheHit;
            RequiredReconstruction = requiredReconstruction;
        }

        public bool Success { get; }

        public int Direction { get; }

        public ReviewPosition Position { get; }

        public string Message { get; }

        public bool WasCacheHit { get; }

        public bool RequiredReconstruction { get; }

        public static FrameStepResult Succeeded(int direction, ReviewPosition position)
        {
            return new FrameStepResult(true, direction, position, string.Empty);
        }

        public static FrameStepResult Succeeded(int direction, ReviewPosition position, bool wasCacheHit, string message)
        {
            return new FrameStepResult(true, direction, position, message, wasCacheHit, false);
        }

        public static FrameStepResult Succeeded(
            int direction,
            ReviewPosition position,
            bool wasCacheHit,
            bool requiredReconstruction,
            string message)
        {
            return new FrameStepResult(true, direction, position, message, wasCacheHit, requiredReconstruction);
        }

        public static FrameStepResult Failed(int direction, ReviewPosition position, string message)
        {
            return new FrameStepResult(false, direction, position, message);
        }

        public static FrameStepResult Failed(int direction, ReviewPosition position, string message, bool wasCacheHit)
        {
            return new FrameStepResult(false, direction, position, message, wasCacheHit, false);
        }

        public static FrameStepResult Failed(
            int direction,
            ReviewPosition position,
            string message,
            bool wasCacheHit,
            bool requiredReconstruction)
        {
            return new FrameStepResult(false, direction, position, message, wasCacheHit, requiredReconstruction);
        }
    }
}
