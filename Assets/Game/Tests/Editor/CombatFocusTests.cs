using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace junklite.Tests
{
    /// <summary>Combat focus: which enemy a side swing locks onto, and how its lunge is warped.</summary>
    public sealed class CombatFocusTests
    {
        private static readonly CombatFocusQuery Query = new(range: 4.5f, laneTolerance: 1.25f, maxAbove: 2.5f,
            maxBelow: 1.5f, switchMargin: 0.75f, behindTolerance: 0.25f);

        private static bool Gap(Vector3 target, float side, out float gap, Vector3? axis = null, float halfWidth = 0.5f) =>
            CombatFocusSelector.TryGetGap(Vector3.zero, axis ?? Vector3.right, side, target, halfWidth, Query, out gap);

        [Test]
        public void EnemyInFrontWithinRangeGivesGapToItsNearSide()
        {
            Assert.That(Gap(new Vector3(3f, 0f, 0f), +1f, out float gap), Is.True);
            Assert.That(gap, Is.EqualTo(2.5f).Within(1e-4f));
        }

        [Test]
        public void OnlyTheAttackSideCountsSoThereIsNoAutoTurn()
        {
            Assert.That(Gap(new Vector3(-2f, 0f, 0f), +1f, out _), Is.False, "enemy behind when attacking right");
            Assert.That(Gap(new Vector3(-2f, 0f, 0f), -1f, out float gap), Is.True, "same enemy when attacking left");
            Assert.That(gap, Is.EqualTo(1.5f).Within(1e-4f));
        }

        [Test]
        public void OverlappingEnemyJustBehindTheCentreStillCounts()
        {
            Assert.That(Gap(new Vector3(-0.2f, 0f, 0f), +1f, out float gap), Is.True);
            Assert.That(gap, Is.EqualTo(0f));
            Assert.That(Gap(new Vector3(-0.4f, 0f, 0f), +1f, out _), Is.False);
        }

        [Test]
        public void RangeLaneAndHeightBandsReject()
        {
            Assert.That(Gap(new Vector3(5.5f, 0f, 0f), +1f, out _), Is.False, "beyond range");
            Assert.That(Gap(new Vector3(2f, 0f, 2f), +1f, out _), Is.False, "off the lane line");
            Assert.That(Gap(new Vector3(2f, 3f, 0f), +1f, out _), Is.False, "too high");
            Assert.That(Gap(new Vector3(2f, -2f, 0f), +1f, out _), Is.False, "too low");
            Assert.That(Gap(new Vector3(2f, 2f, 0f), +1f, out _), Is.True, "launched enemy above");
        }

        [Test]
        public void WorksOnARotatedZLane()
        {
            // A ZY lane: transform.right = (0, 0, -1).
            Vector3 axis = new(0f, 0f, -1f);
            Assert.That(Gap(new Vector3(0f, 0f, -3f), +1f, out float gap, axis), Is.True);
            Assert.That(gap, Is.EqualTo(2.5f).Within(1e-4f));
            Assert.That(Gap(new Vector3(2f, 0f, -3f), +1f, out _, axis), Is.False, "off the Z lane");
        }

        [Test]
        public void ChoosePicksTheNearestAndKeepsTheCurrentFocusUnlessClearlyBeaten()
        {
            float[] gaps = { 2.0f, 1.5f, float.MaxValue };
            Assert.That(CombatFocusSelector.Choose(gaps, 3, -1, Query), Is.EqualTo(1), "nearest with no focus");
            Assert.That(CombatFocusSelector.Choose(gaps, 3, 0, Query), Is.EqualTo(0), "0.5 closer is not enough to switch");

            gaps[1] = 1.0f;
            Assert.That(CombatFocusSelector.Choose(gaps, 3, 0, Query), Is.EqualTo(1), "1.0 closer switches");
        }

        [Test]
        public void ChooseChainsToTheNextWhenTheFocusIsGone()
        {
            float[] gaps = { float.MaxValue, 3f, 4f };
            Assert.That(CombatFocusSelector.Choose(gaps, 3, 0, Query), Is.EqualTo(1));
            float[] none = { float.MaxValue, float.MaxValue };
            Assert.That(CombatFocusSelector.Choose(none, 2, -1, Query), Is.EqualTo(-1));
        }

        [Test]
        public void ShorterLungeKeepsTheCurveAndDurationAtALowerPeak()
        {
            float authored = LungeWarp.AuthoredDistance(9f, 0.3f, null); // 9 * 0.3 * 2/3 = 1.8
            Assert.That(authored, Is.EqualTo(1.8f).Within(1e-3f));

            float travelled = LungeWarp.Solve(9f, 0.3f, null, 0.6f, 24f, 1f, out float peak, out float duration);
            Assert.That(travelled, Is.EqualTo(0.6f).Within(1e-4f));
            Assert.That(duration, Is.EqualTo(0.3f));
            Assert.That(peak, Is.EqualTo(3f).Within(1e-3f));
        }

        [Test]
        public void ZeroDistanceStandsStill()
        {
            Assert.That(LungeWarp.Solve(9f, 0.3f, null, 0f, 24f, 1f, out float peak, out _), Is.EqualTo(0f));
            Assert.That(peak, Is.EqualTo(0f));
        }

        [Test]
        public void GapCloseSpeedsUpThenStretchesUntilTheTimeCap()
        {
            // 3 m with a 9 m/s, 0.3 s authored lunge: faster, same time.
            float travelled = LungeWarp.Solve(9f, 0.3f, null, 3f, 24f, 1f, out float peak, out float duration);
            Assert.That(travelled, Is.EqualTo(3f).Within(1e-3f));
            Assert.That(duration, Is.EqualTo(0.3f));
            Assert.That(peak, Is.EqualTo(15f).Within(1e-3f));

            // 6 m: capped at 24 m/s, so it takes longer (0.375 s).
            travelled = LungeWarp.Solve(9f, 0.3f, null, 6f, 24f, 1f, out peak, out duration);
            Assert.That(peak, Is.EqualTo(24f));
            Assert.That(duration, Is.EqualTo(0.375f).Within(1e-3f));
            Assert.That(travelled, Is.EqualTo(6f).Within(1e-3f));

            // Same, but it must arrive within 0.32 s: it gets as far as it can.
            travelled = LungeWarp.Solve(9f, 0.3f, null, 6f, 24f, 0.32f, out peak, out duration);
            Assert.That(duration, Is.EqualTo(0.32f).Within(1e-4f));
            Assert.That(travelled, Is.EqualTo(24f * 0.32f * 2f / 3f).Within(1e-3f));
        }

        [Test]
        public void StepWithNoAuthoredLungeCanStillCloseAGap()
        {
            float travelled = LungeWarp.Solve(0f, 0f, null, 1f, 24f, 1f, out float peak, out float duration);
            Assert.That(duration, Is.EqualTo(LungeWarp.FallbackDuration));
            Assert.That(peak, Is.GreaterThan(0f));
            Assert.That(travelled, Is.EqualTo(1f).Within(1e-3f));
        }

        [Test]
        public void ShippedPlayerPrefabsHaveFocusWithAMarker()
        {
            foreach (string path in new[] { "Assets/Game/Prefabs/PLAYER/Player_Cass.prefab", "Assets/Game/Prefabs/PLAYER/Player_2.2.prefab" })
            {
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                Assert.That(prefab, Is.Not.Null, path);
                var focus = prefab.GetComponent<PlayerCombatFocus>();
                Assert.That(focus, Is.Not.Null, path);
                var marker = new SerializedObject(focus).FindProperty("markerPrefab").objectReferenceValue;
                Assert.That(marker, Is.Not.Null, path);
                Assert.That(prefab.GetComponent<WeaponManager>(), Is.Not.Null, path);
            }
        }
    }
}
