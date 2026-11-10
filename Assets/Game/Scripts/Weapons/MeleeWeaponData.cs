using UnityEngine;

namespace junklite
{
    /// <summary>
    /// WeaponData for melee weapons (fists, swords, etc).
    /// Defines MeleeComboStep which contains only melee-relevant fields.
    /// </summary>
    [CreateAssetMenu(fileName = "MeleeWeaponData", menuName = "Junklite/Melee Weapon Data")]
    public class MeleeWeaponData : WeaponData
    {
        // =====================================================================
        // MELEE COMBO STEP
        // Only fields that make sense for melee. No bullet counts, no fire rates.
        // =====================================================================

        [System.Serializable]
        public struct MeleeComboStep
        {
            [Header("Animation")]
            public string animationName;

            [Header("Combat")]
            public float damageMultiplier;
            public float hitRadius;
            [Tooltip("Off (default): the swing hits only the enemy nearest the player. On: every enemy inside the hit sphere, for collateral / sweep attacks.")]
            public bool piercing;

            [Header("VFX")]
            public GameObject slashPrefab;

            [Header("Attack Push")]
            [Tooltip("Peak velocity at lunge start.")]
            public float forwardImpulse;
            [Tooltip("Vertical velocity applied at swing start.")]
            public float verticalImpulse;
            [Tooltip("How long the lunge lasts.")]
            public float forwardImpulseDuration;
            [Tooltip("Velocity curve over the lunge duration. Left = start, Right = end. Default ease-out if left empty.")]
            public AnimationCurve lungeCurve;
            [Tooltip("Wind-up: seconds the clip pauses at its Hold event, with the player planted. The lunge fires on release. " +
                     "0 = the player's default hold (SpineAnimationController.attackHoldDuration). Clips without a Hold event ignore it.")]
            [Min(0f)] public float windupHold;
            [Tooltip("Clips with no Hold event: clip seconds where the wind-up pause happens (the pulled-back pose). " +
                     "After it the rest of the swing runs at SpineAnimationController.attackReleaseSpeed. Needs windupHold.")]
            [Min(0f)] public float windupAt;
            [Tooltip("How far the combat-focus slide goes, times the usual (no target: noTargetLungeDistance; focused: up to hitting range). 0 or 1 = usual. 1.1 = 10% further.")]
            [Min(0f)] public float lungeDistanceScale;
            [Tooltip("Focused only: she slides at least this far (m) on this hit, even when already in reach, and keeps " +
                     "following the enemy as the hit knocks it back, stopping 0.3 m from its body. 0 = plain focus lunge.")]
            [Min(0f)] public float focusedSlide;

            /// <summary>lungeDistanceScale, with 0 (unset) read as 1.</summary>
            public float LungeScale => lungeDistanceScale > 0f ? lungeDistanceScale : 1f;

            [Header("On Hit")]
            [Tooltip("Delay after lunge starts before the hitbox opens. Tune this to match when the sword visually connects. Measure on the plain clip: a clip's Hold and Release events shift it automatically.")]
            public float hitDelay;
            [Tooltip("Horizontal recoil applied to the player on a successful hit.")]
            public float hitRecoil;
            [Tooltip("Override the weapon's base knockback for this step. Useful for combo finishers.")]
            public bool overrideKnockback;
            public Vector2 knockback;
            [Tooltip("Flinch = knockback + hitstun. Launch = lift the enemy off the ground; the knockback above is then the launch velocity (x away from the player, y up). Launch needs overrideKnockback.")]
            public HitReactionKind reactionKind;
        }

        [Tooltip("Distance from the player center where the hit sphere or projectile origin is placed.")]
        public float attackRange = 1f;

        [Header("Combat Focus (side attacks)")]
        [Tooltip("How far a side swing steps when no enemy is focused (m). The authored lunge is the cap. " +
                 "With a focus, the lunge instead closes exactly to hitting distance (further than authored if needed).")]
        [Min(0f)] public float noTargetLungeDistance = 0.2f;
        [Tooltip("How deep into the hit sphere the focused enemy's near side should end up, 0..1 of the sphere radius. " +
                 "Higher = stops closer to the enemy.")]
        [Range(0f, 1f)] public float focusSpacing = 0.7f;

        // =====================================================================
        // COMBO ARRAYS
        // =====================================================================

        [Header("Side Combo (1 → 2 → 3...)")]
        public MeleeComboStep[] sideCombo;

        [Header("Air Side Combo (1 → 2 → 3...)")]
        [Tooltip("Side attacks in the air. Each step can be used once per jump, in order, and landing resets the chain: one step = one air attack per jump, three steps = an air 1-2-3 string.")]
        public MeleeComboStep[] airSideCombo;

        [Header("Directional Attacks")]
        public MeleeComboStep upAttack;
        public MeleeComboStep downAttack;

        // =====================================================================
        // ABSTRACT IMPLEMENTATIONS
        // =====================================================================

        public override int GetComboLength(AttackDirection dir, bool isGrounded)
        {
            switch (dir)
            {
                case AttackDirection.Side:
                    return isGrounded ? (sideCombo?.Length ?? 0) : (airSideCombo?.Length ?? 0);
                default:
                    return 1;
            }
        }

        public override bool TryGetAnimationName(AttackDirection dir, int comboIndex, bool isGrounded, out string animationName)
        {
            animationName = null;
            switch (dir)
            {
                case AttackDirection.Side:
                    var combo = isGrounded ? sideCombo : airSideCombo;
                    if (combo == null || combo.Length == 0) return false;
                    animationName = combo[Mathf.Clamp(comboIndex, 0, combo.Length - 1)].animationName;
                    break;

                case AttackDirection.Up:
                    animationName = upAttack.animationName;
                    break;

                case AttackDirection.Down:
                    animationName = downAttack.animationName;
                    break;
            }
            return !string.IsNullOrEmpty(animationName);
        }

        // =====================================================================
        // STEP ACCESS — called by WeaponManager after CombatState resolves the index
        // =====================================================================

        public bool TryGetMeleeStep(AttackDirection dir, int comboIndex, bool isGrounded, out MeleeComboStep step)
        {
            step = default;
            switch (dir)
            {
                case AttackDirection.Side:
                    var combo = isGrounded ? sideCombo : airSideCombo;
                    if (combo == null || combo.Length == 0) return false;
                    step = combo[Mathf.Clamp(comboIndex, 0, combo.Length - 1)];
                    return true;

                case AttackDirection.Up:
                    step = upAttack;
                    return !string.IsNullOrEmpty(upAttack.animationName);

                case AttackDirection.Down:
                    step = downAttack;
                    return !string.IsNullOrEmpty(downAttack.animationName);

                default:
                    return false;
            }
        }
    }
}