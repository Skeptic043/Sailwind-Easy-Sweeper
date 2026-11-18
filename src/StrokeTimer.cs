using System;

namespace EasySweeper
{
    /// <summary>Decides when a held Use button asks the broom for its next stroke.</summary>
    internal static class StrokeTimer
    {
        /// <summary>The native stroke eases toward its end at Lerp(deltaTime * 5).</summary>
        internal const float NativeRate = 5f;

        /// <summary>
        /// Seconds until the broom has covered <paramref name="lengthPercent"/> of the way to the end of
        /// its stroke, when it swings <paramref name="speed"/> times the native rate. Reversing there keeps
        /// the broom moving instead of waiting out the slow tail of the ease.
        /// </summary>
        internal static float StrokeSeconds(float lengthPercent, float speed) =>
            (float)(-Math.Log(1.0 - lengthPercent / 100.0) / (NativeRate * speed));

        /// <summary>
        /// Returns true when a new stroke should be requested. A stroke that is already queued
        /// (the native press, or a request the game hasn't consumed yet) restarts the timer instead.
        /// The length is checked against the current stroke's start, so a live settings change applies to it.
        /// </summary>
        internal static bool ShouldRequest(bool strokeQueued, float now, float strokeSeconds, ref float strokeStart)
        {
            if (!strokeQueued && now < strokeStart + strokeSeconds)
                return false;

            strokeStart = now;
            return !strokeQueued;
        }
    }
}
