using NUnit.Framework;
using UnityEngine;

namespace junklite.Tests
{
    /// <summary>
    /// Pure-logic checks for the Hyena's dodge policy: fatigue, swing threat and
    /// side choice. No Play Mode, no prefab load.
    /// </summary>
    public sealed class HyenaTacticsTests
    {
        [Test]
        public void FatigueLowersTheDodgeChanceAndRecoversOverTime()
        {
            Assert.That(HyenaTactics.DodgeChance(0.6f, 0f), Is.EqualTo(0.6f).Within(0.0001f));
            Assert.That(HyenaTactics.DodgeChance(0.6f, 0.5f), Is.EqualTo(0.3f).Within(0.0001f));
            Assert.That(HyenaTactics.DodgeChance(0.6f, 1f), Is.EqualTo(0f).Within(0.0001f));

            // 1 -> 0 over the recovery time, never below zero.
            Assert.That(HyenaTactics.DecayFatigue(1f, 2f, 4f), Is.EqualTo(0.5f).Within(0.0001f));
            Assert.That(HyenaTactics.DecayFatigue(1f, 10f, 4f), Is.EqualTo(0f));
            Assert.That(HyenaTactics.DecayFatigue(0.7f, 0f, 4f), Is.EqualTo(0.7f).Within(0.0001f));
        }

        [Test]
        public void RepeatedDodgesWearItOut()
        {
            // Three quick dodges at 0.35 each leave almost no chance for a fourth.
            float fatigue = 0f;
            for (int i = 0; i < 3; i++)
                fatigue = Mathf.Clamp01(fatigue + 0.35f);

            Assert.That(HyenaTactics.DodgeChance(0.6f, fatigue), Is.LessThan(0.01f));
        }

        [Test]
        public void SwingThreatensOnlyInsideReachPlusMarginAndNeverPastTheCap()
        {
            const float reach = 1.3f;
            const float margin = 1.5f;
            const float cap = 4f;

            Assert.That(HyenaTactics.SwingThreatens(2.5f, reach, margin, cap), Is.True);
            Assert.That(HyenaTactics.SwingThreatens(3.2f, reach, margin, cap), Is.False, "out of reach");
            Assert.That(HyenaTactics.SwingThreatens(3.9f, 10f, margin, cap), Is.True, "long weapon, under the cap");
            Assert.That(HyenaTactics.SwingThreatens(4.5f, 10f, margin, cap), Is.False, "past the cap");
        }

        [Test]
        public void SwingReachIsRangePlusLargestComboRadius()
        {
            var weapon = ScriptableObject.CreateInstance<MeleeWeaponData>();
            try
            {
                weapon.attackRange = 0.1f;
                weapon.sideCombo = new[]
                {
                    new MeleeWeaponData.MeleeComboStep { hitRadius = 0.75f },
                    new MeleeWeaponData.MeleeComboStep { hitRadius = 1.2f },
                    new MeleeWeaponData.MeleeComboStep { hitRadius = 0.9f },
                };

                Assert.That(HyenaTactics.SwingReach(weapon), Is.EqualTo(1.3f).Within(0.0001f));
                Assert.That(HyenaTactics.SwingReach(null), Is.EqualTo(0f));
            }
            finally
            {
                Object.DestroyImmediate(weapon);
            }
        }

        [Test]
        public void DodgeSideNeverPicksABlockedSideWhileTheOtherIsOpen()
        {
            const float distance = 4f;

            // Wall behind: go through the player even with no through chance.
            Assert.That(HyenaTactics.ChooseDodgeSide(1f, 4f, distance, 0f, 0.9f, preferAway: true), Is.EqualTo(1));
            // Nothing behind the player: hop away even when the roll would say through.
            Assert.That(HyenaTactics.ChooseDodgeSide(4f, 0f, distance, 1f, 0.1f, preferAway: false), Is.EqualTo(-1));
        }

        [Test]
        public void DodgeSideWithBothOpenRollsUnlessAwayIsPreferred()
        {
            const float distance = 4f;

            Assert.That(HyenaTactics.ChooseDodgeSide(4f, 4f, distance, 0.35f, 0.2f, preferAway: false), Is.EqualTo(1), "roll under the through chance");
            Assert.That(HyenaTactics.ChooseDodgeSide(4f, 4f, distance, 0.35f, 0.8f, preferAway: false), Is.EqualTo(-1), "roll over it");
            Assert.That(HyenaTactics.ChooseDodgeSide(4f, 4f, distance, 1f, 0.0f, preferAway: true), Is.EqualTo(-1), "a retreat always goes away");
        }

        [Test]
        public void DodgeSideWithBothBlockedTakesTheRoomierOne()
        {
            Assert.That(HyenaTactics.ChooseDodgeSide(1f, 2.5f, 4f, 0f, 0.5f, preferAway: true), Is.EqualTo(1));
            Assert.That(HyenaTactics.ChooseDodgeSide(3f, 2.5f, 4f, 1f, 0.5f, preferAway: false), Is.EqualTo(-1));
        }

        [Test]
        public void ASideCountsAsOpenWithinTheTolerance()
        {
            float almost = 4f - HyenaTactics.OpenSideTolerance * 0.5f;
            Assert.That(HyenaTactics.ChooseDodgeSide(almost, 0f, 4f, 1f, 0f, preferAway: false), Is.EqualTo(-1));
        }
    }
}
