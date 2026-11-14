using UnityEngine;

namespace junklite
{
    public static class InputHelpers
    {
        /// <summary>
        /// Applies a simple deadzone + hard actuation threshold to a Vector2 stick input.
        /// If usingHardCut is true the output will be either zero or a normalized direction (no in-between magnitudes).
        /// </summary>
        public static Vector2 ApplyDeadzoneAndActuation(Vector2 raw, float deadzone, float actuation, bool usingHardCut)
        {
            float mag = raw.magnitude;
            if (mag <= deadzone) return Vector2.zero;

            if (!usingHardCut)
            {
                // simple rescaled deadzone -> 0..1
                float t = Mathf.InverseLerp(deadzone, 1f, mag);
                return raw.normalized * t;
            }

            // Hard cut: only actuate once magnitude >= actuation
            if (mag >= actuation)
                return raw.normalized;

            return Vector2.zero;
        }
    }
}
