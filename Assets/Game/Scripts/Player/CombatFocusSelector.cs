using UnityEngine;

namespace junklite
{
    /// <summary>Tuning for <see cref="CombatFocusSelector"/>. Distances are world units.</summary>
    public readonly struct CombatFocusQuery
    {
        /// <summary>Furthest gap (player centre to the enemy's near side) that can be focused.</summary>
        public readonly float Range;
        /// <summary>Furthest an enemy may sit off the lane line (into/out of the screen).</summary>
        public readonly float LaneTolerance;
        public readonly float MaxAbove;
        public readonly float MaxBelow;
        /// <summary>A new enemy must be this much closer than the current focus to steal it.</summary>
        public readonly float SwitchMargin;
        /// <summary>Enemies whose centre is up to this far behind still count (overlapping the player).</summary>
        public readonly float BehindTolerance;

        public CombatFocusQuery(float range, float laneTolerance, float maxAbove, float maxBelow,
            float switchMargin, float behindTolerance)
        {
            Range = Mathf.Max(0f, range);
            LaneTolerance = Mathf.Max(0f, laneTolerance);
            MaxAbove = Mathf.Max(0f, maxAbove);
            MaxBelow = Mathf.Max(0f, maxBelow);
            SwitchMargin = Mathf.Max(0f, switchMargin);
            BehindTolerance = Mathf.Max(0f, behindTolerance);
        }
    }

    /// <summary>
    /// Pure focus rules, kept apart from scene queries so they can be tested. Only the side the
    /// player attacks toward is considered: there is no auto-turn to an enemy behind.
    /// </summary>
    public static class CombatFocusSelector
    {
        /// <summary>
        /// Gap along <paramref name="side"/> from <paramref name="origin"/> to the near side of a body
        /// centred at <paramref name="target"/> with <paramref name="halfWidth"/> along the lane.
        /// False when the body is behind, off the lane, out of the height band or beyond range.
        /// </summary>
        public static bool TryGetGap(Vector3 origin, Vector3 laneAxis, float side, Vector3 target,
            float halfWidth, in CombatFocusQuery query, out float gap)
        {
            gap = float.MaxValue;
            laneAxis.y = 0f;
            if (laneAxis.sqrMagnitude < 1e-6f || side == 0f) return false;
            laneAxis.Normalize();
            float sign = Mathf.Sign(side);

            Vector3 delta = target - origin;
            float along = Vector3.Dot(delta, laneAxis) * sign;
            if (along < -query.BehindTolerance) return false;

            Vector3 laneNormal = Vector3.Cross(laneAxis, Vector3.up);
            if (Mathf.Abs(Vector3.Dot(delta, laneNormal)) > query.LaneTolerance) return false;
            if (delta.y > query.MaxAbove || delta.y < -query.MaxBelow) return false;

            gap = Mathf.Max(0f, along - Mathf.Max(0f, halfWidth));
            return gap <= query.Range;
        }

        /// <summary>
        /// Picks the closest gap. The current focus is kept unless another is closer by more than
        /// SwitchMargin. Gaps of invalid entries must be float.MaxValue (or negative counts as invalid).
        /// Returns the chosen index, or -1.
        /// </summary>
        public static int Choose(float[] gaps, int count, int currentIndex, in CombatFocusQuery query)
        {
            int best = -1;
            float bestGap = float.MaxValue;
            for (int i = 0; i < count; i++)
            {
                float gap = gaps[i];
                if (gap < 0f || gap > query.Range || gap >= bestGap) continue;
                best = i;
                bestGap = gap;
            }

            if (currentIndex >= 0 && currentIndex < count)
            {
                float currentGap = gaps[currentIndex];
                bool currentValid = currentGap >= 0f && currentGap <= query.Range;
                if (currentValid && (best < 0 || bestGap >= currentGap - query.SwitchMargin))
                    return currentIndex;
            }
            return best;
        }
    }
}
