using System;

namespace IslandAirport
{
    /// <summary>Fixed-rate export of the complete recorded timeline plus a one-second final hold.</summary>
    public sealed class ReplayExportTimeline
    {
        public const int FramesPerSecond = 24;
        public const float Speed = 12f;
        public readonly float Duration;
        public readonly int FrameCount;
        public ReplayExportTimeline(float duration)
        {
            if (float.IsNaN(duration) || float.IsInfinity(duration) || duration <= 0 || duration > 330)
                throw new ArgumentOutOfRangeException("duration");
            Duration = duration;
            FrameCount = (int)Math.Ceiling(duration / Speed * FramesPerSecond) + FramesPerSecond;
        }
        public float TimeAt(int frame)
        {
            if (frame < 0 || frame >= FrameCount) throw new ArgumentOutOfRangeException("frame");
            return Math.Min(Duration, frame * Speed / FramesPerSecond);
        }
    }
}
