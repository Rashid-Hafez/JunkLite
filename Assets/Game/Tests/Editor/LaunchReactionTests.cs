using NUnit.Framework;
using UnityEngine;

namespace junklite.Tests
{
    /// <summary>
    /// Contract checks for the launch reaction (combo finisher): the reaction kind
    /// rides on the hit request, defaults stay a flinch, and the launch settings
    /// cannot produce an infinite air or ground loop.
    /// </summary>
    public sealed class LaunchReactionTests
    {
        [Test]
        public void DefaultHitIsAFlinch()
        {
            var reaction = HitReactionRequest.DefaultHit(new Vector2(10f, 2f));
            Assert.That(reaction.Kind, Is.EqualTo(HitReactionKind.Flinch));
            Assert.That(reaction.IsLaunch, Is.False);
            Assert.That(reaction.HasKnockback, Is.True);
            Assert.That(reaction.HasHitstun, Is.True);
        }

        [Test]
        public void LaunchKindRidesOnTheRequest()
        {
            var reaction = HitReactionRequest.DefaultHit(new Vector2(4f, 9f), HitReactionKind.Launch);
            var request = new DamageRequest(10f, hitReaction: reaction);

            Assert.That(request.HitReaction.IsLaunch, Is.True);
            Assert.That(request.KnockbackForce, Is.EqualTo(new Vector2(4f, 9f)));
            // A launch still carries hitstun so receivers that cannot launch flinch normally.
            Assert.That(request.HitReaction.HasHitstun, Is.True);
        }

        [Test]
        public void PlainRequestsAndTickDamageAreNeverLaunches()
        {
            var plain = new DamageRequest(5f, knockback: new Vector2(3f, 1f));
            Assert.That(plain.HitReaction.IsLaunch, Is.False);

            var tick = new DamageRequest(1f, isTickDamage: true);
            Assert.That(tick.HitReaction.IsLaunch, Is.False);
            Assert.That(tick.HitReaction.HasAnyReaction, Is.False);

            Assert.That(HitReactionRequest.None.IsLaunch, Is.False);
        }

        [Test]
        public void ComboStepDefaultsToFlinch()
        {
            var step = new MeleeWeaponData.MeleeComboStep();
            Assert.That(step.reactionKind, Is.EqualTo(HitReactionKind.Flinch));
        }

        [Test]
        public void LaunchReachesApexHeightIncludingTheHang()
        {
            var settings = new EnemyLaunchSettings();
            float g = Physics.gravity.magnitude;
            float velocity = settings.LaunchUpSpeed(0f);
            float height = 0f;
            const float dt = 0.0005f;

            // Same two-regime gravity EnemyMovement applies while launched.
            while (velocity > 0f)
            {
                float scale = Mathf.Abs(velocity) < settings.ApexHangSpeed
                    ? settings.ApexHangGravityScale
                    : settings.GravityScale;
                velocity -= g * scale * dt;
                height += velocity * dt;
            }

            Assert.That(height, Is.EqualTo(settings.ApexHeight).Within(0.05f));
        }

        [Test]
        public void HitsWithoutASwingDirectionPushAwayFromTheSource()
        {
            // Zero = the receiver falls back to "away from the source": explosions,
            // enemy attacks and anything that is not a melee swing keep their old push.
            Assert.That(HitReactionRequest.DefaultHit(new Vector2(4f, 1f)).PushDirection, Is.EqualTo(Vector3.zero));
            Assert.That(new DamageRequest(5f, knockback: new Vector2(3f, 1f)).HitReaction.PushDirection, Is.EqualTo(Vector3.zero));
        }

        [Test]
        public void SpikeIsNotALaunch()
        {
            var reaction = HitReactionRequest.DefaultHit(new Vector2(4f, 14f), HitReactionKind.Spike);
            Assert.That(reaction.IsSpike, Is.True);
            Assert.That(reaction.IsLaunch, Is.False);
            Assert.That(reaction.HasAnyReaction, Is.True, "a spike must reach the launch state's hit handling");
        }

        [Test]
        public void LaunchSettingsCannotLoopForever()
        {
            var settings = new EnemyLaunchSettings();

            Assert.That(settings.BounceRestitution, Is.LessThan(1f), "a bounce must lose height");
            Assert.That(settings.MaxAirTime, Is.GreaterThan(0f), "a launch off a ledge must still end");
            Assert.That(settings.DownedMaxTotal, Is.GreaterThanOrEqualTo(settings.DownedDurationRange.y),
                "the cap must allow the longest authored lie-down");
            Assert.That(settings.MaxJuggleHits, Is.GreaterThanOrEqualTo(0));
            Assert.That(settings.GetUpDuration, Is.GreaterThan(0f), "the get-up is the punish window");
        }
    }
}
