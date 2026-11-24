using NUnit.Framework;
using UnityEngine;

namespace junklite.Tests
{
    /// <summary>
    /// The launch arc: a smooth gravity curve that reaches the authored height, flies
    /// up and down in the same time, and keeps juggles and the slow fall on the curve.
    /// </summary>
    public sealed class LaunchArcTests
    {
        private const float Gravity = 9.81f;
        private const float PhysicsStep = 0.02f;

        // The recommended Grunt and Hyena values, and the code defaults.
        private static readonly LaunchArc.Shape Grunt = new(Gravity, 4f, 9f, 10f);
        private static readonly LaunchArc.Shape Defaults = new(Gravity, 1.2f, 3f, 12f);

        private struct Flight
        {
            public float Apex;
            public float RiseTime;
            public float FallTime;
        }

        /// <summary>
        /// Flies with the rigidbody's stepping: velocity first, then position. Script order
        /// decides whether EnemyMovement sees this step's gravity or the previous step's
        /// (staleGravity: the movement ran before the state). Crossing times are interpolated
        /// inside a step, so the result is the arc and not the step count.
        /// </summary>
        private static Flight Fly(float launchSpeed, LaunchArc.Shape shape, float dt, bool staleGravity = false)
        {
            float y = 0f;
            float v = launchSpeed;
            float t = 0f;
            float pendingScale = LaunchArc.GravityScaleAt(v, shape);
            bool pastApex = false;
            var flight = new Flight();

            for (int i = 0; i < 100000; i++)
            {
                float scale = staleGravity ? pendingScale : LaunchArc.GravityScaleAt(v, shape);
                pendingScale = LaunchArc.GravityScaleAt(v, shape);

                float nextV = v - shape.BaseGravity * scale * dt;
                float nextY = y + nextV * dt;

                if (!pastApex && nextV <= 0f)
                {
                    pastApex = true;
                    flight.Apex = y;
                    flight.RiseTime = t + dt * v / (v - nextV);
                }

                if (pastApex && nextY <= 0f)
                {
                    flight.FallTime = t + dt * y / (y - nextY) - flight.RiseTime;
                    return flight;
                }

                y = nextY;
                v = nextV;
                t += dt;
            }

            Assert.Fail("The body never landed.");
            return flight;
        }

        /// <summary>Time to apex under the old two-zone gravity, with the old launch speed.</summary>
        private static float OldTimeToApex(float height, float hangSpeed, float hangScale, float fullScale)
        {
            float full = Gravity * fullScale;
            if (hangSpeed <= 0f)
                return Mathf.Sqrt(2f * full * height) / full;

            float hang = Gravity * hangScale;
            float fastRise = height - hangSpeed * hangSpeed / (2f * hang);
            float launchSpeed = Mathf.Sqrt(2f * full * fastRise + hangSpeed * hangSpeed);
            return (launchSpeed - hangSpeed) / full + hangSpeed / hang;
        }

        [TestCase(4f, PhysicsStep)]
        [TestCase(4f, 1f / 60f)]
        [TestCase(4f, 0.01f)]
        [TestCase(6f, PhysicsStep)]
        public void GruntArcReachesApexWithinTwoPercent(float height, float dt)
        {
            float speed = LaunchArc.LaunchSpeedForHeight(height, Grunt, dt);
            Assert.That(Fly(speed, Grunt, dt).Apex, Is.EqualTo(height).Within(height * 0.02f));
        }

        [TestCase(6.5f, PhysicsStep)]
        [TestCase(6.5f, 1f / 60f)]
        [TestCase(10f, PhysicsStep)]
        public void DefaultArcReachesApexWithinTwoPercent(float height, float dt)
        {
            float speed = LaunchArc.LaunchSpeedForHeight(height, Defaults, dt);
            Assert.That(Fly(speed, Defaults, dt).Apex, Is.EqualTo(height).Within(height * 0.02f));
        }

        [Test]
        public void RiseAndFallTakeTheSameTimeWithinFivePercent()
        {
            Flight grunt = Fly(LaunchArc.LaunchSpeedForHeight(4f, Grunt, PhysicsStep), Grunt, PhysicsStep);
            Assert.That(grunt.FallTime, Is.EqualTo(grunt.RiseTime).Within(grunt.RiseTime * 0.05f));

            Flight defaults = Fly(LaunchArc.LaunchSpeedForHeight(6.5f, Defaults, PhysicsStep), Defaults, PhysicsStep);
            Assert.That(defaults.FallTime, Is.EqualTo(defaults.RiseTime).Within(defaults.RiseTime * 0.05f));
        }

        [Test]
        public void ScriptOrderOnlyCostsAboutOnePhysicsStep()
        {
            float speed = LaunchArc.LaunchSpeedForHeight(4f, Grunt, PhysicsStep);
            Flight stale = Fly(speed, Grunt, PhysicsStep, staleGravity: true);

            Assert.That(stale.Apex, Is.EqualTo(4f).Within(4f * 0.03f));
            Assert.That(stale.FallTime, Is.EqualTo(stale.RiseTime).Within(PhysicsStep * 1.2f));
        }

        [Test]
        public void GravityIsContinuousAcrossTheHangSpeed()
        {
            const float Sweep = 0.005f;
            // A smoothstep's steepest slope is 1.5 times the straight line between its ends.
            float steepest = 1.5f * (Grunt.FullScale - Grunt.HangScale) / Grunt.HangSpeed;
            float previous = LaunchArc.GravityScaleAt(0f, Grunt);

            for (float v = Sweep; v < Grunt.HangSpeed * 2f; v += Sweep)
            {
                float scale = LaunchArc.GravityScaleAt(v, Grunt);
                Assert.That(Mathf.Abs(scale - previous), Is.LessThanOrEqualTo(steepest * Sweep + 0.0001f), $"seam at {v} u/s");
                previous = scale;
            }

            Assert.That(LaunchArc.GravityScaleAt(0f, Grunt), Is.EqualTo(Grunt.HangScale).Within(0.0001f));
            Assert.That(LaunchArc.GravityScaleAt(Grunt.HangSpeed, Grunt), Is.EqualTo(Grunt.FullScale).Within(0.0001f));
            Assert.That(LaunchArc.GravityScaleAt(Grunt.HangSpeed * 3f, Grunt), Is.EqualTo(Grunt.FullScale).Within(0.0001f));
        }

        [Test]
        public void GravityIsTheSameRisingAndFalling()
        {
            for (float v = 0f; v < 20f; v += 0.5f)
                Assert.That(LaunchArc.GravityScaleAt(-v, Grunt), Is.EqualTo(LaunchArc.GravityScaleAt(v, Grunt)));
        }

        [Test]
        public void NoHangSpeedIsAPlainParabola()
        {
            var flat = new LaunchArc.Shape(Gravity, 2.5f, 9f, 0f);
            float gravity = Gravity * 9f;
            float speed = LaunchArc.SpeedForRise(4f, flat);

            Assert.That(LaunchArc.GravityScaleAt(0f, flat), Is.EqualTo(9f));
            Assert.That(speed, Is.EqualTo(Mathf.Sqrt(2f * gravity * 4f)).Within(0.001f));
            Assert.That(LaunchArc.TimeToApex(speed, flat), Is.EqualTo(speed / gravity).Within(0.001f));
        }

        [Test]
        public void LaunchSpeedRisesWithApexHeight()
        {
            float previous = 0f;
            for (float height = 0.1f; height < 20f; height += 0.1f)
            {
                float speed = LaunchArc.LaunchSpeedForHeight(height, Grunt, PhysicsStep);
                Assert.That(speed, Is.GreaterThan(previous), $"at {height} u");
                previous = speed;
            }
        }

        [TestCase(1f)]
        [TestCase(5f)]
        [TestCase(9.9f)]
        [TestCase(10.1f)]
        [TestCase(26f)]
        public void SpeedAndRiseAreInverses(float speed)
        {
            float rise = LaunchArc.RiseFromSpeed(speed, Grunt);
            Assert.That(LaunchArc.SpeedForRise(rise, Grunt), Is.EqualTo(speed).Within(speed * 0.01f));
        }

        [Test]
        public void PredictedTimeToApexMatchesTheFlight()
        {
            float speed = LaunchArc.LaunchSpeedForHeight(4f, Grunt, 0.001f);
            Assert.That(LaunchArc.TimeToApex(speed, Grunt),
                Is.EqualTo(Fly(speed, Grunt, 0.001f).RiseTime).Within(0.01f));
        }

        [Test]
        public void TheArcIsFifteenToTwentyPercentSlowerThanTheOldOne()
        {
            float grunt = LaunchArc.TimeToApex(LaunchArc.SpeedForRise(4f, Grunt), Grunt);
            Assert.That(grunt / OldTimeToApex(4f, 0f, 2.5f, 9f), Is.InRange(1.15f, 1.2f));

            float defaults = LaunchArc.TimeToApex(LaunchArc.SpeedForRise(6.5f, Defaults), Defaults);
            Assert.That(defaults / OldTimeToApex(6.5f, 4f, 1.5f, 4f), Is.InRange(1.15f, 1.2f));
        }

        [Test]
        public void SettingsDefaultsUseTheTestedShape()
        {
            var settings = new EnemyLaunchSettings();
            LaunchArc.Shape arc = settings.Arc;

            Assert.That(arc.HangScale, Is.EqualTo(Defaults.HangScale));
            Assert.That(arc.FullScale, Is.EqualTo(Defaults.FullScale));
            Assert.That(arc.HangSpeed, Is.EqualTo(Defaults.HangSpeed));
            Assert.That(settings.ApexRise(0f), Is.EqualTo(settings.ApexHeight));
        }

        [Test]
        public void JuggleAtTheApexCannotCarryTheBodyFarHigher()
        {
            const float Headroom = 0.5f;
            float height = 4f;
            float ceiling = 4f + Headroom;

            // Hit after hit at the top, each from where the last one peaked.
            for (int hit = 0; hit < 3; hit++)
            {
                float lift = LaunchArc.JuggleLiftSpeed(0f, 12f, ceiling - height, Grunt);
                height += LaunchArc.RiseFromSpeed(lift, Grunt);
                Assert.That(height, Is.LessThanOrEqualTo(ceiling + 0.02f), $"after hit {hit + 1}");
            }

            Assert.That(LaunchArc.JuggleLiftSpeed(0f, 12f, 0f, Grunt), Is.EqualTo(0f));
        }

        [Test]
        public void JuggleOnlyEverAddsLift()
        {
            // Still climbing faster than the lift: the climb is left alone.
            Assert.That(LaunchArc.JuggleLiftSpeed(20f, 12f, 5f, Grunt), Is.EqualTo(20f));
            // Falling with plenty of room: the full lift.
            Assert.That(LaunchArc.JuggleLiftSpeed(-10f, 12f, 3f, Grunt), Is.EqualTo(12f));
            // Falling past the ceiling: it stops falling and does not rise.
            Assert.That(LaunchArc.JuggleLiftSpeed(-3f, 12f, -0.2f, Grunt), Is.EqualTo(0f));
        }

        [Test]
        public void FallHangFadesBackIntoTheArc()
        {
            const float ArcScale = 7f;
            const float FallScale = 1.5f;
            const float Duration = 0.5f;

            Assert.That(LaunchArc.FallHangScale(ArcScale, FallScale, Duration, Duration), Is.EqualTo(FallScale).Within(0.0001f));
            Assert.That(LaunchArc.FallHangScale(ArcScale, FallScale, Duration * 0.5f, Duration), Is.EqualTo(FallScale).Within(0.0001f));
            Assert.That(LaunchArc.FallHangScale(ArcScale, FallScale, 0f, Duration), Is.EqualTo(ArcScale));
            Assert.That(LaunchArc.FallHangScale(ArcScale, FallScale, 0.0001f, Duration), Is.EqualTo(ArcScale).Within(0.01f));
            Assert.That(LaunchArc.FallHangScale(ArcScale, FallScale, 1f, 0f), Is.EqualTo(ArcScale), "0 seconds = no slow fall");

            // A step of the countdown never jumps.
            float previous = LaunchArc.FallHangScale(ArcScale, FallScale, Duration, Duration);
            for (float left = Duration; left > 0f; left -= PhysicsStep)
            {
                float scale = LaunchArc.FallHangScale(ArcScale, FallScale, left, Duration);
                Assert.That(Mathf.Abs(scale - previous), Is.LessThan(1f), $"at {left} s left");
                previous = scale;
            }
        }
    }
}
