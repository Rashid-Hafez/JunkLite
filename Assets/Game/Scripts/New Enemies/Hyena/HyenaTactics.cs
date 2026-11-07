using UnityEngine;

namespace junklite
{
    /// <summary>
    /// Pure decision maths for <see cref="HyenaBrain"/>: dodge fatigue, swing threat,
    /// and which side to dodge to. No Unity lifecycle, so it is unit-testable.
    /// </summary>
    public static class HyenaTactics
    {
        /// <summary>Side tolerance: a side counts as open when its room is within this of the full dodge.</summary>
        public const float OpenSideTolerance = 0.05f;

        /// <summary>Fatigue left after <paramref name="elapsed"/> seconds of not dodging (1 → 0 over <paramref name="recoverySeconds"/>).</summary>
        public static float DecayFatigue(float fatigue, float elapsed, float recoverySeconds)
        {
            if (recoverySeconds <= 0f)
                return 0f;

            return Mathf.Clamp01(fatigue - Mathf.Max(0f, elapsed) / recoverySeconds);
        }

        /// <summary>Chance to dodge right now: the base chance scaled down by fatigue.</summary>
        public static float DodgeChance(float baseChance, float fatigue) =>
            Mathf.Clamp01(baseChance) * (1f - Mathf.Clamp01(fatigue));

        /// <summary>
        /// How far in front of the player a side swing reaches before the lunge:
        /// the hit sphere's centre offset plus its largest radius over the combo.
        /// </summary>
        public static float SwingReach(MeleeWeaponData weapon)
        {
            if (weapon == null)
                return 0f;

            float radius = 0f;
            var combo = weapon.sideCombo;
            if (combo != null)
            {
                for (int i = 0; i < combo.Length; i++)
                    radius = Mathf.Max(radius, combo[i].hitRadius);
            }

            return Mathf.Max(0f, weapon.attackRange) + radius;
        }

        /// <summary>A swing started at <paramref name="axisDistance"/> can reach the Hyena (reach + lunge margin, never past the cap).</summary>
        public static bool SwingThreatens(float axisDistance, float reach, float margin, float cap) =>
            axisDistance <= Mathf.Min(cap, reach + margin);

        /// <summary>
        /// Which way to dodge. +1 = through the player (landing behind her), -1 = away.
        /// A blocked side is never chosen while the other is open; both blocked picks
        /// the roomier one; both open picks away when <paramref name="preferAway"/>,
        /// otherwise rolls <paramref name="throughChance"/> against <paramref name="roll"/>.
        /// </summary>
        public static int ChooseDodgeSide(
            float roomAway,
            float roomThrough,
            float dodgeDistance,
            float throughChance,
            float roll,
            bool preferAway)
        {
            float needed = dodgeDistance - OpenSideTolerance;
            bool awayOpen = roomAway >= needed;
            bool throughOpen = roomThrough >= needed;

            if (awayOpen && !throughOpen) return -1;
            if (throughOpen && !awayOpen) return 1;
            if (!awayOpen && !throughOpen) return roomThrough > roomAway ? 1 : -1;
            if (preferAway) return -1;
            return roll < throughChance ? 1 : -1;
        }
    }
}
