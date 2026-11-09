using NUnit.Framework;
using UnityEngine;

namespace junklite.Tests
{
    /// <summary>
    /// Pure-logic checks for the grunt group tactics: hit pressure windows and
    /// the engagement director's lane queries on real components in an EditMode
    /// scene. No Play Mode, no prefab load.
    /// </summary>
    public sealed class GruntTacticsTests
    {
        [Test]
        public void PressureWindowCountsOnlyHitsInsideTheWindow()
        {
            var window = new HitPressureWindow(4);
            window.Record(0f);
            window.Record(1f);
            window.Record(2f);

            Assert.That(window.CountWithin(2.5f, 2.5f), Is.EqualTo(3));
            Assert.That(window.CountWithin(4f, 2.5f), Is.EqualTo(1));
            Assert.That(window.CountWithin(10f, 2.5f), Is.EqualTo(0));
        }

        [Test]
        public void PressureWindowSaturatesAtHitsToMax()
        {
            var window = new HitPressureWindow(4);
            window.Record(0f);
            Assert.That(window.Pressure01(0.1f, 2.5f, 3), Is.EqualTo(1f / 3f).Within(0.001f));

            window.Record(0.2f);
            window.Record(0.3f);
            window.Record(0.4f);
            Assert.That(window.Pressure01(0.5f, 2.5f, 3), Is.EqualTo(1f));
        }

        [Test]
        public void PressureWindowOverwritesOldestWhenFull()
        {
            var window = new HitPressureWindow(2);
            window.Record(0f);
            window.Record(1f);
            window.Record(2f);

            // Capacity 2: the hit at t=0 was overwritten, so a window covering
            // everything still reports only two hits.
            Assert.That(window.CountWithin(2f, 100f), Is.EqualTo(2));
            Assert.That(window.LastHitTime, Is.EqualTo(2f));
        }

        [Test]
        public void PressureWindowReportsQuietTime()
        {
            var window = new HitPressureWindow(2);
            Assert.That(float.IsPositiveInfinity(window.QuietSeconds(5f)), Is.True);

            window.Record(3f);
            Assert.That(window.QuietSeconds(5f), Is.EqualTo(2f));

            window.Clear();
            Assert.That(float.IsPositiveInfinity(window.QuietSeconds(5f)), Is.True);
        }

        [Test]
        public void HoldSpeedFollowsChaseSpeedVariance()
        {
            var chase = new ChaseBehavior();
            float authored = chase.ChaseSpeed;

            chase.SetSpeedMultiplier(1.1f);
            Assert.That(chase.ChaseSpeed, Is.EqualTo(authored * 1.1f).Within(0.0001f));

            chase.SetSpeedMultiplier(0f);
            Assert.That(chase.ChaseSpeed, Is.GreaterThan(0f), "multiplier is clamped above zero");
        }

        // ---------------- Lane spacing ----------------
        // Distances are measured from the player on the enemy's side.
        // Leader at 2.0, comfort spacing 1.3, hard spacing 0.85 -> approachMin 3.3, hardMin 2.85.

        [Test]
        public void FollowerCannotAimIntoTheLeader()
        {
            float clamped = LaneSpacingSolver.ClampDistance(
                desired: 2.8f, current: 5f, approachMin: 3.3f, hardMin: 2.85f, hardMax: float.PositiveInfinity);
            Assert.That(clamped, Is.EqualTo(3.3f).Within(0.0001f));
        }

        [Test]
        public void FrontEnemyIsNotClampedTowardThePlayer()
        {
            float clamped = LaneSpacingSolver.ClampDistance(
                desired: 0f, current: 3f, approachMin: float.NegativeInfinity,
                hardMin: float.NegativeInfinity, hardMax: float.PositiveInfinity);
            Assert.That(clamped, Is.EqualTo(0f));
        }

        [Test]
        public void RetreatStopsShortOfTheFollower()
        {
            float clamped = LaneSpacingSolver.ClampDistance(
                desired: 4.5f, current: 3.3f, approachMin: 3.3f, hardMin: 2.85f, hardMax: 3.7f);
            Assert.That(clamped, Is.EqualTo(3.7f).Within(0.0001f));
        }

        [Test]
        public void SqueezedEnemyStaysPut()
        {
            // Leader and follower both too close: no legal spot, so do not move at all.
            float clamped = LaneSpacingSolver.ClampDistance(
                desired: 2f, current: 3f, approachMin: 3.3f, hardMin: 3.2f, hardMax: 3.1f);
            Assert.That(clamped, Is.EqualTo(3f));
        }

        [Test]
        public void ComfortGapYieldsToHardGapWhenTight()
        {
            // Follower allows up to 3.0; comfort (3.3) would push into it, hard (2.85) still fits.
            float clamped = LaneSpacingSolver.ClampDistance(
                desired: 2f, current: 2.9f, approachMin: 3.3f, hardMin: 2.85f, hardMax: 3f);
            Assert.That(clamped, Is.EqualTo(2.85f).Within(0.0001f));
        }

        [Test]
        public void FollowerFarBehindKeepsFullSpeed()
        {
            float speed = LaneSpacingSolver.LimitSpeed(
                desiredSpeed: 7f, current: 6f, clamped: 3.3f, approachMin: 3.3f,
                leaderClosingSpeed: 0f, hardMax: float.PositiveInfinity, followerRecedingSpeed: 0f, gain: 6f);
            Assert.That(speed, Is.EqualTo(7f));
        }

        [Test]
        public void FollowerBrakesNearAStandingLeader()
        {
            // 0.2 slack to the comfort gap: 6 * 0.2 = 1.2.
            float speed = LaneSpacingSolver.LimitSpeed(
                desiredSpeed: 7f, current: 3.5f, clamped: 3.3f, approachMin: 3.3f,
                leaderClosingSpeed: 0f, hardMax: float.PositiveInfinity, followerRecedingSpeed: 0f, gain: 6f);
            Assert.That(speed, Is.EqualTo(1.2f).Within(0.0001f));
        }

        [Test]
        public void FollowerMatchesAMovingLeader()
        {
            // No slack, leader closing on the player at 5: follower may only match it.
            float speed = LaneSpacingSolver.LimitSpeed(
                desiredSpeed: 7.7f, current: 3.3f, clamped: 3.29f - 0.5f, approachMin: 3.3f,
                leaderClosingSpeed: 5f, hardMax: float.PositiveInfinity, followerRecedingSpeed: 0f, gain: 6f);
            Assert.That(speed, Is.EqualTo(5f).Within(0.0001f));
        }

        [Test]
        public void BackingUpBrakesNearTheFollower()
        {
            float speed = LaneSpacingSolver.LimitSpeed(
                desiredSpeed: 4.5f, current: 3.6f, clamped: 3.7f, approachMin: 3.3f,
                leaderClosingSpeed: 0f, hardMax: 3.7f, followerRecedingSpeed: 0f, gain: 6f);
            Assert.That(speed, Is.EqualTo(0.6f).Within(0.0001f));
        }

        [Test]
        public void LaneSnapshotDefaultsAreInert()
        {
            var snapshot = default(EnemyLaneSnapshot);
            Assert.That(snapshot.IsFront, Is.True);
            Assert.That(snapshot.FarFrontOccupied, Is.False);
            Assert.That(snapshot.HasActiveAttacker, Is.False);
            Assert.That(Mathf.Approximately(snapshot.GroupPressure, 0f), Is.True);
            Assert.That(snapshot.BodiesAhead, Is.EqualTo(0));
            Assert.That(snapshot.PlaceInLine, Is.EqualTo(0));
        }

        [Test]
        public void AStaggeredBodyAheadCountsAsAPlaceInLine()
        {
            // Promoted to the front because the grunt ahead is stunned: still second in
            // line for the vault, and still behind a body.
            var promoted = new EnemyLaneSnapshot(1, 0, 1, 0, 0f, 0f, false, bodiesAhead: 1);
            Assert.That(promoted.IsFront, Is.True);
            Assert.That(promoted.PlaceInLine, Is.EqualTo(1));

            var queued = new EnemyLaneSnapshot(1, 1, 2, 0, 0f, 0f, false);
            Assert.That(queued.PlaceInLine, Is.EqualTo(1));

            var thirdBehindAStaggeredBody = new EnemyLaneSnapshot(1, 1, 2, 0, 0f, 0f, false, bodiesAhead: 1);
            Assert.That(thirdBehindAStaggeredBody.PlaceInLine, Is.EqualTo(2));
        }

        private static EnemyLaneNeighbors NeighborsBehindLeaderAt(float leaderDistance, float myDistance) =>
            new EnemyLaneNeighbors(
                side: 1,
                axis: Vector3.right,
                targetPosition: Vector3.zero,
                myDistance: myDistance,
                hasLeader: true,
                leaderDistance: leaderDistance,
                leaderComfortSpacing: 1.6f,
                leaderHardSpacing: 1.2f,
                leaderVelocity: Vector3.zero,
                hasFollower: false,
                followerDistance: 0f,
                followerHardSpacing: 0f,
                followerVelocity: Vector3.zero);

        [Test]
        public void SpacingPullsTheGoalBackToTheComfortGapBehindTheBodyAhead()
        {
            // Heading for the attack distance (2) with a body standing at 2.3.
            Vector3 destination = Vector3.right * 2f;
            float speed = 7f;
            LaneSpacingSolver.Apply(NeighborsBehindLeaderAt(2.3f, 5f), ref destination, ref speed, 6f);

            Assert.That(destination.x, Is.EqualTo(3.9f).Within(0.0001f));
            Assert.That(speed, Is.EqualTo(6.6f).Within(0.0001f), "brakes on the way in");
        }

        [Test]
        public void ABodyKnockedIntoTheGapPushesTheEnemyOutGently()
        {
            // The body ahead was knocked back to 2.8: I stand at 3.7, 0.7 inside the gap.
            Vector3 destination = Vector3.right * 2f;
            float speed = 7f;
            LaneSpacingSolver.Apply(NeighborsBehindLeaderAt(2.8f, 3.7f), ref destination, ref speed, 6f);

            Assert.That(destination.x, Is.EqualTo(4.4f).Within(0.0001f));
            Assert.That(speed, Is.EqualTo(4.2f).Within(0.0001f), "eases out in proportion to the overlap");
        }

        [Test]
        public void ARetreatThatWasNotPushedKeepsItsSpeed()
        {
            float speed = LaneSpacingSolver.LimitSettleSpeed(4.5f, desired: 4.2f, current: 3.7f, clamped: 4.2f, gain: 6f);
            Assert.That(speed, Is.EqualTo(4.5f).Within(0.0001f));
        }

        [Test]
        public void SpacingWithNoBodiesLeavesTheMoveAlone()
        {
            var alone = new EnemyLaneNeighbors(
                1, Vector3.right, Vector3.zero, 5f,
                false, 0f, 0f, 0f, Vector3.zero,
                false, 0f, 0f, Vector3.zero);

            Vector3 destination = Vector3.right * 2f;
            float speed = 7f;
            LaneSpacingSolver.Apply(alone, ref destination, ref speed, 6f);

            Assert.That(destination.x, Is.EqualTo(2f).Within(0.0001f));
            Assert.That(speed, Is.EqualTo(7f).Within(0.0001f));
        }

        [Test]
        public void OnlyRealHitsStaggerAnEnemyAlreadyInAStun()
        {
            var swing = new DamageRequest(10f, knockback: new Vector2(5f, 2f));
            Assert.That(EnemyCharacter.IsStaggeringHit(swing), Is.True);

            var tick = new DamageRequest(1f, isTickDamage: true);
            Assert.That(EnemyCharacter.IsStaggeringHit(tick), Is.False, "burn and shock ticks do not restart the clip");

            var silent = new DamageRequest(10f, hitReaction: HitReactionRequest.None);
            Assert.That(EnemyCharacter.IsStaggeringHit(silent), Is.False, "a reaction-less hit shows nothing");
        }
    }
}
