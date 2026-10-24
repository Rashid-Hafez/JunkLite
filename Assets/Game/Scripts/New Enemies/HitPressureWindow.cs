using UnityEngine;

namespace junklite
{
    /// <summary>
    /// Allocation-free sliding window of hit timestamps. Used by enemy brains
    /// ("how hard am I being hit right now") and by the engagement director
    /// ("how hard is the group being hit"). Pure C#, no Unity lifecycle.
    /// </summary>
    public sealed class HitPressureWindow
    {
        private readonly float[] times;
        private int head;
        private int count;
        private float lastHitTime = float.NegativeInfinity;

        public HitPressureWindow(int capacity = 8)
        {
            times = new float[Mathf.Max(1, capacity)];
        }

        public float LastHitTime => lastHitTime;

        public void Record(float time)
        {
            times[head] = time;
            head = (head + 1) % times.Length;
            if (count < times.Length)
                count++;
            lastHitTime = time;
        }

        public void Clear()
        {
            head = 0;
            count = 0;
            lastHitTime = float.NegativeInfinity;
        }

        /// <summary>Number of recorded hits newer than <paramref name="now"/> - <paramref name="window"/>.</summary>
        public int CountWithin(float now, float window)
        {
            float cutoff = now - window;
            int hits = 0;
            for (int i = 0; i < count; i++)
            {
                if (times[i] >= cutoff)
                    hits++;
            }

            return hits;
        }

        /// <summary>0 when no recent hits, 1 when <paramref name="hitsToMax"/> or more landed inside the window.</summary>
        public float Pressure01(float now, float window, int hitsToMax)
        {
            if (hitsToMax <= 0)
                return 0f;

            return Mathf.Clamp01(CountWithin(now, window) / (float)hitsToMax);
        }

        /// <summary>Seconds since the last recorded hit. Infinite when nothing was recorded.</summary>
        public float QuietSeconds(float now)
        {
            return float.IsNegativeInfinity(lastHitTime) ? float.PositiveInfinity : now - lastHitTime;
        }
    }
}
