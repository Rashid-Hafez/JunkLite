using UnityEngine;

namespace junklite
{
    /// <summary>
    /// The launch flight as pure maths. Gravity is a smooth function of vertical speed,
    /// the same rising and falling, so height over time is a symmetric bell with a soft top:
    ///     g(v) = base * lerp(hangScale, fullScale, smoothstep(0, hangSpeed, |v|))
    /// EnemyLaunchSettings solves the launch speed here; LaunchedState feeds the gravity
    /// to EnemyMovement every physics step. No state and no allocation.
    /// </summary>
    public static class LaunchArc
    {
        /// <summary>The numbers that define one arc.</summary>
        public readonly struct Shape
        {
            public readonly float BaseGravity;
            public readonly float HangScale;
            public readonly float FullScale;
            public readonly float HangSpeed;

            public Shape(float baseGravity, float hangScale, float fullScale, float hangSpeed)
            {
                BaseGravity = Mathf.Max(0.01f, baseGravity);
                HangScale = Mathf.Max(MinScale, hangScale);
                FullScale = Mathf.Max(MinScale, fullScale);
                HangSpeed = Mathf.Max(0f, hangSpeed);
            }
        }

        private const float MinScale = 0.05f;
        // Slices of the curved part of the speed range. Only used once per launch or hit.
        private const int Slices = 64;

        /// <summary>Gravity multiplier at a vertical speed. Depends only on |speed|, and is continuous.</summary>
        public static float GravityScaleAt(float verticalSpeed, in Shape shape)
        {
            if (shape.HangSpeed <= 0f)
                return shape.FullScale;

            float blend = Smooth01(Mathf.Abs(verticalSpeed) / shape.HangSpeed);
            return shape.HangScale + (shape.FullScale - shape.HangScale) * blend;
        }

        /// <summary>Height climbed from this upward speed to the apex: the integral of v / g(v) dv.</summary>
        public static float RiseFromSpeed(float speed, in Shape shape)
        {
            speed = Mathf.Abs(speed);
            float curvedTop = Mathf.Min(speed, shape.HangSpeed);
            float rise = 0f;
            float slice = curvedTop / Slices;

            for (int i = 0; curvedTop > 0f && i < Slices; i++)
            {
                float v = (i + 0.5f) * slice;
                rise += v / Gravity(v, shape) * slice;
            }

            // Above the hang speed gravity is constant.
            return rise + (speed * speed - curvedTop * curvedTop) / (2f * Gravity(speed, shape));
        }

        /// <summary>Seconds from this upward speed to the apex: the integral of 1 / g(v) dv.</summary>
        public static float TimeToApex(float speed, in Shape shape)
        {
            speed = Mathf.Abs(speed);
            float curvedTop = Mathf.Min(speed, shape.HangSpeed);
            float time = 0f;
            float slice = curvedTop / Slices;

            for (int i = 0; curvedTop > 0f && i < Slices; i++)
                time += slice / Gravity((i + 0.5f) * slice, shape);

            return time + (speed - curvedTop) / Gravity(speed, shape);
        }

        /// <summary>The upward speed that climbs exactly this height. Inverse of <see cref="RiseFromSpeed"/>.</summary>
        public static float SpeedForRise(float rise, in Shape shape)
        {
            if (rise <= 0f)
                return 0f;

            float climbed = 0f;
            float slice = shape.HangSpeed / Slices;

            for (int i = 0; shape.HangSpeed > 0f && i < Slices; i++)
            {
                float low = i * slice;
                float high = low + slice;
                float v = low + 0.5f * slice;
                float part = v / Gravity(v, shape) * slice;

                if (climbed + part >= rise)
                {
                    // Inside one thin slice the climb grows with speed squared.
                    float t = (rise - climbed) / part;
                    return Mathf.Sqrt(low * low + t * (high * high - low * low));
                }

                climbed += part;
            }

            float fullGravity = shape.BaseGravity * shape.FullScale;
            return Mathf.Sqrt(shape.HangSpeed * shape.HangSpeed + 2f * fullGravity * (rise - climbed));
        }

        /// <summary>
        /// The launch speed that peaks at this height in fixed-step physics. The rigidbody
        /// integrates velocity before position, which loses about speed * step / 2 of the
        /// climb (6 percent on a fast launch), so that much is added to the height solved for.
        /// Pass a step of 0 for the exact continuous answer.
        /// </summary>
        public static float LaunchSpeedForHeight(float height, in Shape shape, float stepSeconds)
        {
            float speed = SpeedForRise(height, shape);
            if (stepSeconds <= 0f)
                return speed;

            for (int i = 0; i < 2; i++)
                speed = SpeedForRise(height + speed * stepSeconds * 0.5f, shape);

            return speed;
        }

        /// <summary>
        /// Upward speed after a juggle hit. A hit only ever adds lift, so it cannot slow a
        /// climb, and the lift stops at the ceiling (roomToCeiling is the height left), so
        /// hits at the top cannot stack the body out of reach. A falling body at the ceiling stops falling.
        /// </summary>
        public static float JuggleLiftSpeed(float currentUp, float juggleLift, float roomToCeiling, in Shape shape)
        {
            float capped = Mathf.Min(juggleLift, SpeedForRise(roomToCeiling, shape));
            return Mathf.Max(currentUp, capped);
        }

        /// <summary>
        /// Gravity multiplier during the slow fall a mid-air hit earns: the full slow fall
        /// for the first half, fading back into the arc over the second, so it has no seam.
        /// </summary>
        public static float FallHangScale(float arcScale, float fallScale, float secondsLeft, float duration)
        {
            if (duration <= 0f || secondsLeft <= 0f)
                return arcScale;

            return arcScale + (fallScale - arcScale) * Smooth01(secondsLeft / (duration * 0.5f));
        }

        private static float Gravity(float speed, in Shape shape) =>
            shape.BaseGravity * GravityScaleAt(speed, shape);

        private static float Smooth01(float t)
        {
            t = Mathf.Clamp01(t);
            return t * t * (3f - 2f * t);
        }
    }
}
