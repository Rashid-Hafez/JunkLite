using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using Object = UnityEngine.Object;

namespace junklite.Tests
{
    /// <summary>The pure rules behind the player's locomotion clips, attack holds, stop slide, push and wall-jump walls.</summary>
    public sealed class PlayerAnimationRulesTests
    {
        private const float HoldDuration = 0.12f;
        private const float ReleaseSpeed = 1.35f;
        private const float FistScale = 1.62f;

        private readonly List<Object> owned = new();

        [TearDown]
        public void TearDown()
        {
            for (int i = owned.Count - 1; i >= 0; i--) if (owned[i] != null) Object.DestroyImmediate(owned[i]);
            owned.Clear();
        }

        // ----- Turn clip guard -----

        [Test]
        public void BodiesPushOnlyAFullSpeedOrLongRun()
        {
            Assert.That(EnemyContactDamage.ShouldPush(10f, 10f, 0f, 0.95f, 1.5f), Is.True, "full speed");
            Assert.That(EnemyContactDamage.ShouldPush(5f, 10f, 1.5f, 0.95f, 1.5f), Is.True, "long run");
            Assert.That(EnemyContactDamage.ShouldPush(5f, 10f, 1f, 0.95f, 1.5f), Is.False, "slow, short");
            Assert.That(EnemyContactDamage.ShouldPush(0f, 10f, 3f, 0.95f, 1.5f), Is.False, "not moving in");
        }

        [Test]
        public void AReversalSlidesAfterALongRunOrFromHighSpeed()
        {
            Assert.That(Character2D5Controller.ShouldTurnSlide(0.2f, 10f, 0.5f, 12f), Is.False);
            Assert.That(Character2D5Controller.ShouldTurnSlide(0.5f, 10f, 0.5f, 12f), Is.True);
            Assert.That(Character2D5Controller.ShouldTurnSlide(0.25f, 12f, 0.5f, 12f), Is.True);
            Assert.That(Character2D5Controller.ShouldTurnSlide(0.1f, 12f, 0.5f, 12f), Is.False, "spam at high speed");
        }

        [Test]
        public void TurnClipPlaysAfterARealRunAndOutsideTheCooldown()
        {
            Assert.That(SpineAnimationController.ShouldPlayTurnClip(0.15f, 0.35f, 0.15f, 0.35f), Is.True);
            Assert.That(SpineAnimationController.ShouldPlayTurnClip(1f, float.PositiveInfinity, 0.15f, 0.35f), Is.True, "first turn ever");
        }

        [Test]
        public void TurnClipStaysOffDuringDirectionSpam()
        {
            Assert.That(SpineAnimationController.ShouldPlayTurnClip(0.1f, 5f, 0.15f, 0.35f), Is.False, "barely running");
            Assert.That(SpineAnimationController.ShouldPlayTurnClip(1f, 0.2f, 0.15f, 0.35f), Is.False, "inside the cooldown");
        }

        // ----- Hold / Release timing (Attack_Punch_Combo_3: Hold 0, Release 0.133, length 0.5) -----

        [Test]
        public void ClipWithoutEventsKeepsItsDelay()
        {
            Assert.That(AttackHoldTiming.WarpDelay(0.14f, FistScale, -1f, -1f, HoldDuration, ReleaseSpeed), Is.EqualTo(0.14f).Within(1e-5f));
        }

        [Test]
        public void HitAfterTheHoldWaitsForTheHold()
        {
            // The authored hit lands just before Release, so only the pause moves it.
            float delay = AttackHoldTiming.WarpDelay(0.08f, FistScale, 0f, 0.1333f, HoldDuration, ReleaseSpeed);
            Assert.That(delay, Is.EqualTo(0.08f + HoldDuration).Within(1e-5f));
        }

        [Test]
        public void HitAfterReleaseAlsoComesSoonerByTheFasterClip()
        {
            float hitClipTime = 0.3f;
            float delay = AttackHoldTiming.WarpDelay(hitClipTime / FistScale, FistScale, 0f, 0.1333f, HoldDuration, ReleaseSpeed);
            float expected = HoldDuration + 0.1333f / FistScale + (hitClipTime - 0.1333f) / (FistScale * ReleaseSpeed);
            Assert.That(delay, Is.EqualTo(expected).Within(1e-5f));
        }

        [Test]
        public void HitBeforeTheHoldIsUntouched()
        {
            // Attack_Gun_1: Hold 0.233, Release 0.3.
            Assert.That(AttackHoldTiming.WarpDelay(0.1f, 1f, 0.2333f, 0.3f, HoldDuration, ReleaseSpeed), Is.EqualTo(0.1f).Within(1e-5f));
        }

        [Test]
        public void WholeSwingStaysCloseToItsPlainLength()
        {
            float plain = 0.5f / FistScale;
            float warped = AttackHoldTiming.WarpDelay(plain, FistScale, 0f, 0.1333f, HoldDuration, ReleaseSpeed);
            Assert.That(warped, Is.GreaterThan(plain));
            Assert.That(warped - plain, Is.LessThan(0.08f), "the hold is mostly paid back by the faster finish");
        }

        // ----- Stop slide -----

        [Test]
        public void StopSlideEasesFromFullSpeedToZero()
        {
            Assert.That(Character2D5Controller.EaseStopSlide(5f, 5f, 1f), Is.EqualTo(5f).Within(1e-5f));
            Assert.That(Character2D5Controller.EaseStopSlide(5f, 5f, 0f), Is.EqualTo(0f).Within(1e-5f));
            Assert.That(Character2D5Controller.EaseStopSlide(-5f, 5f, 1f), Is.EqualTo(-5f).Within(1e-5f), "keeps the direction");
        }

        [Test]
        public void StopSlideNeverRegainsSpeedAWallTook()
        {
            Assert.That(Character2D5Controller.EaseStopSlide(0f, 5f, 0.9f), Is.EqualTo(0f).Within(1e-5f));
            Assert.That(Character2D5Controller.EaseStopSlide(1f, 5f, 0.9f), Is.EqualTo(1f).Within(1e-5f));
        }

        // ----- Push -----

        [Test]
        public void PushOnlyReachesTheFacingSide()
        {
            Assert.That(PlayerPush.IsInFront(Vector3.zero, Vector3.right, new Vector3(1f, 0f, 0f)), Is.True);
            Assert.That(PlayerPush.IsInFront(Vector3.zero, Vector3.right, new Vector3(-1f, 0f, 0f)), Is.False);
            Assert.That(PlayerPush.IsInFront(Vector3.zero, Vector3.left, new Vector3(-1f, 0f, 0f)), Is.True);
        }

        // ----- Wall-jump walls -----

        private bool ProbeWallAtRight(bool marked, bool require)
        {
            var player = new GameObject("Probe Player");
            owned.Add(player);
            player.SetActive(false);
            var capsule = player.AddComponent<CapsuleCollider>();
            capsule.height = 2f;
            capsule.radius = 0.5f;
            var rb = player.AddComponent<Rigidbody>();
            rb.useGravity = false;
            var controller = player.AddComponent<Character2D5Controller>();
            player.SetActive(true);

            const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
            typeof(Character2D5Controller).GetField("rb", flags).SetValue(controller, rb);
            typeof(Character2D5Controller).GetField("col", flags).SetValue(controller, capsule);
            typeof(Character2D5Controller).GetField("requireWallJumpSurface", flags).SetValue(controller, require);

            var wall = GameObject.CreatePrimitive(PrimitiveType.Cube);
            owned.Add(wall);
            wall.layer = 12;
            wall.transform.position = new Vector3(1f, 0f, 0f);
            wall.transform.localScale = new Vector3(1f, 4f, 4f);
            if (marked) wall.AddComponent<WallJumpSurface>();

            Physics.SyncTransforms();
            object[] args = { 1f, 0f };
            return (bool)typeof(Character2D5Controller).GetMethod("ProbeWall", flags).Invoke(controller, args);
        }

        [Test]
        public void MarkedWallCanBeSlidOn() => Assert.That(ProbeWallAtRight(marked: true, require: true), Is.True);

        [Test]
        public void PlainWallOnlyBlocks() => Assert.That(ProbeWallAtRight(marked: false, require: true), Is.False);

        [Test]
        public void TurningTheRequirementOffAcceptsAnyWallLayerWall() =>
            Assert.That(ProbeWallAtRight(marked: false, require: false), Is.True);
    }
}
