using UnityEngine;

namespace junklite
{
    /// <summary>
    /// Pure decision maths for <see cref="HyenaBrain"/>: dodge fatigue, swing threat,
    /// which side to dodge to, when a stagger breaks and when a vault is open.
    /// No Unity lifecycle, so it is unit-testable.
    /// </summary>
    public static class HyenaTactics
    {
        /// <summary>Side tolerance: a side counts as open when its room is within this of the full dodge.</summary>
        public const float OpenSideTolerance = 0.05f;

        /// <summary>
        /// Whether a vault over the player is on: she is squared up to a grounded,
        /// planted player (facing her, not rolling) inside the vault band. A player who
        /// has turned away is already flanked, so there is nothing to vault for.
        /// </summary>
        public static bool VaultOpen(
            float axisDistance,
            float minRange,
            float maxRange,
            bool playerFacingHer,
            bool playerGrounded,
            bool playerRolling) =>
            playerFacingHer && playerGrounded && !playerRolling
            && axisDistance >= minRange && axisDistance <= maxRange;

        /// <summary>
        /// The vault for a Hyena stuck behind another enemy: someone stands between her and
        /// the player (that includes whoever the player is swinging at in front of her), so
        /// she goes over to the far side instead of waiting. The player's facing does not
        /// matter here, because the wait is what she is escaping. The far side must hold
        /// fewer enemies than are ahead of her, so she never hops to a side as crowded as the
        /// one she left and hops back (no ping-pong).
        /// </summary>
        public static bool QueuedVaultOpen(
            float axisDistance,
            float minRange,
            float maxRange,
            int enemiesAhead,
            int enemiesFarSide,
            bool playerGrounded,
            bool playerRolling) =>
            enemiesAhead > 0 && enemiesFarSide < enemiesAhead
            && playerGrounded && !playerRolling
            && axisDistance >= minRange && axisDistance <= maxRange;

        /// <summary>
        /// Another enemy stands between her and the player: same side of the player, closer.
        /// Both values are signed axis distances to the player.
        /// </summary>
        public static bool StandsBetween(float mySignedDistance, float otherSignedDistance) =>
            mySignedDistance * otherSignedDistance > 0f
            && Mathf.Abs(otherSignedDistance) < Mathf.Abs(mySignedDistance);

        /// <summary>
        /// Hits landed inside the pressure window that break a stagger at once:
        /// true when <paramref name="hitsToBreak"/> is enabled (&gt; 0) and reached.
        /// </summary>
        public static bool StaggerBreaks(int recentHits, int hitsToBreak) =>
            hitsToBreak > 0 && recentHits >= hitsToBreak;

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
