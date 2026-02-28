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
            public bool piercing;

            [Header("VFX")]
            public GameObject slashPrefab;

            [Header("Attack Push")]
            [Tooltip("Forward velocity applied at swing start.")]
            public float forwardImpulse;
            [Tooltip("Vertical velocity applied at swing start.")]
            public float verticalImpulse;
            [Tooltip("How long the push velocity is held before stopping.")]
            public float forwardImpulseDuration;
            [Tooltip("Override gravity multiplier during air attacks. 1 = normal.")]
            public float airGravityMultiplier;

            [Header("On Hit")]
            [Tooltip("Horizontal recoil applied to the player on a successful hit.")]
            public float hitRecoil;
        }

        [Tooltip("Distance from the player center where the hit sphere or projectile origin is placed.")]
        public float attackRange = 1f;

        // =====================================================================
        // COMBO ARRAYS
        // =====================================================================

        [Header("Side Combo (1 → 2 → 3...)")]
        public MeleeComboStep[] sideCombo;

        [Header("Air Side Combo (1 → 2 → 3...)")]
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