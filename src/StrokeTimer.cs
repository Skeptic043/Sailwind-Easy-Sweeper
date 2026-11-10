namespace EasySweeper
{
    /// <summary>Decides when a held Use button asks the broom for its next stroke.</summary>
    internal static class StrokeTimer
    {
        /// <summary>
        /// The native stroke eases toward its end at Lerp(deltaTime * 5), so it covers 75% of the way
        /// in ln(4) / 5 seconds. Reversing then keeps the broom moving instead of waiting out the slow tail.
        /// </summary>
        internal const float StrokeSeconds = 0.28f;

        /// <summary>
        /// Returns true when a new stroke should be requested. A stroke that is already queued
        /// (the native press, or a request the game hasn't consumed yet) restarts the timer instead.
        /// </summary>
        internal static bool ShouldRequest(bool strokeQueued, float now, ref float nextStroke)
        {
            if (!strokeQueued && now < nextStroke)
                return false;

            nextStroke = now + StrokeSeconds;
            return !strokeQueued;
        }
    }
}
