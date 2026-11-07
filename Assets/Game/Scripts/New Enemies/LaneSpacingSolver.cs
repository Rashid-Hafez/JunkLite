using UnityEngine;

namespace junklite
{
    /// <summary>
    /// Pure 1D spacing rules for enemies queued on one side of the player. All
    /// distances are measured from the player along the lane, on the enemy's own
    /// side (larger = further from the player). The enemy ahead is the "leader",
    /// the enemy behind is the "follower".
    ///
    /// Rule: never command a move into another body. Physics collision stays on
    /// only as a safety net, so enemies never need to shove each other.
    /// </summary>
    public static class LaneSpacingSolver
    {
        private const float MoveEpsilon = 0.01f;

        /// <summary>
        /// Clamps a desired distance so the enemy keeps a comfortable gap behind its
        /// leader (<paramref name="approachMin"/>), never presses into it
        /// (<paramref name="hardMin"/>), and never backs into its follower
        /// (<paramref name="hardMax"/>). When squeezed between both, it stays put.
        /// Use negative / positive infinity when there is no leader / follower.
        /// </summary>
        public static float ClampDistance(
            float desired,
            float current,
            float approachMin,
            float hardMin,
            float hardMax)
        {
            float lo = approachMin;
            if (lo > hardMax)
                lo = hardMin;
            if (lo > hardMax)
                return current;

            return Mathf.Clamp(desired, lo, hardMax);
        }

        /// <summary>
        /// Speed matching. Approaching the leader, the enemy may close no faster
        /// than the leader is moving away plus <paramref name="gain"/> times the
        /// remaining slack, so it decelerates smoothly instead of bumping. Backing
        /// toward the follower is limited the same way.
        /// </summary>
        /// <param name="leaderClosingSpeed">How fast the leader moves toward the player (opens the gap).</param>
        /// <param name="followerRecedingSpeed">How fast the follower moves away from the player (opens the gap).</param>
        public static float LimitSpeed(
            float desiredSpeed,
            float current,
            float clamped,
            float approachMin,
            float leaderClosingSpeed,
            float hardMax,
            float followerRecedingSpeed,
            float gain)
        {
            if (clamped < current - MoveEpsilon && !float.IsNegativeInfinity(approachMin))
            {
                float slack = Mathf.Max(0f, current - approachMin);
                return Mathf.Min(desiredSpeed, Mathf.Max(0f, leaderClosingSpeed) + gain * slack);
            }

            if (clamped > current + MoveEpsilon && !float.IsPositiveInfinity(hardMax))
            {
                float slack = Mathf.Max(0f, hardMax - current);
                return Mathf.Min(desiredSpeed, Mathf.Max(0f, followerRecedingSpeed) + gain * slack);
            }

            return desiredSpeed;
        }
    }
}
