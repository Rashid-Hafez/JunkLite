using UnityEngine;

namespace junklite
{
    /// <summary>
    /// Rescales an authored attack lunge (peak speed × duration × curve) so it travels a chosen
    /// distance. Shorter: the same curve at a lower peak. Longer (gap close): a higher peak up to
    /// a speed cap, then a longer duration up to a time cap. Pure math, no scene access.
    /// </summary>
    public static class LungeWarp
    {
        public const float FallbackDuration = 0.15f;
        private const int Samples = 24;

        /// <summary>Mean of the velocity curve over 0..1 (distance = peak × duration × this).</summary>
        public static float CurveMean(AnimationCurve curve)
        {
            if (curve == null || curve.length == 0) return 2f / 3f; // default 1 - t²
            float sum = 0f;
            for (int i = 0; i < Samples; i++)
                sum += Mathf.Max(0f, curve.Evaluate((i + 0.5f) / Samples));
            float mean = sum / Samples;
            return mean > 0.01f ? mean : 2f / 3f;
        }

        public static float AuthoredDistance(float peak, float duration, AnimationCurve curve) =>
            Mathf.Max(0f, peak) * Mathf.Max(0f, duration) * CurveMean(curve);

        /// <summary>
        /// Peak speed and duration that travel <paramref name="distance"/> with this curve.
        /// Returns the distance actually achievable inside the caps.
        /// </summary>
        public static float Solve(float authoredPeak, float authoredDuration, AnimationCurve curve,
            float distance, float maxSpeed, float maxDuration, out float peak, out float duration)
        {
            float mean = CurveMean(curve);
            duration = authoredDuration > 0f ? authoredDuration : FallbackDuration;
            peak = 0f;
            if (distance <= 0f) return 0f;

            float authored = Mathf.Max(0f, authoredPeak) * duration * mean;
            if (distance <= authored)
            {
                peak = distance / (duration * mean);
                return distance;
            }

            // Gap close: speed up first (never slower than authored), then stretch the time.
            float speedCap = Mathf.Max(maxSpeed, authoredPeak);
            peak = distance / (duration * mean);
            if (peak > speedCap)
            {
                peak = speedCap;
                duration = distance / (peak * mean);
                if (maxDuration > 0f && duration > maxDuration) duration = Mathf.Max(authoredDuration, maxDuration);
            }
            return peak * duration * mean;
        }
    }
}
