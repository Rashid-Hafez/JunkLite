using UnityEngine;

namespace junklite
{
    /// <summary>
    /// WeaponData for ranged weapons (guns, etc).
    /// Defines RangedComboStep which contains only ranged-relevant fields.
    /// No hitRadius, no piercing toggle, no melee push — none of it bleeds in.
    /// </summary>
    [CreateAssetMenu(fileName = "RangedWeaponData", menuName = "Junklite/Ranged Weapon Data")]
    public class RangedWeaponData : WeaponData
    {
        // =====================================================================
        // RANGED COMBO STEP
        // Only fields that make sense for a ranged weapon.
        // =====================================================================

        [System.Serializable]
        public struct RangedComboStep
        {
            [Header("Animation")]
            public string animationName;
            [Tooltip("0–1 normalized time in the animation when the bullet spawns. " +
                     "0 = first frame, 1 = last frame. Tune by watching the shoot pose in the animation preview.")]
            [Range(0f, 1f)]
            public float fireAtNormalizedTime;

            [Header("Projectile")]
            [Tooltip("How many bullets this step fires.")]
            public int bulletCount;
            public float bulletRadius;
            public float damageMultiplier;
            [Tooltip("Delay in seconds between bullets in a burst. 0 = instant.")]
            public float fireInterval;
            public float bulletSpeed;

            [Header("Attack Push")]
            [Tooltip("Recoil kick forward/back on fire.")]
            public float forwardImpulse;
            public float forwardImpulseDuration;

            [Header("On Hit")]
            [Tooltip("Recoil applied to the player on a confirmed hit.")]
            public float hitRecoil;
        }

        // =====================================================================
        // SHARED PROJECTILE SETTINGS
        // Bullet prefab lives here — not per step — since all combos use the same gun.
        // =====================================================================

        [Header("Projectile")]
        public GameObject bulletPrefab;

        // =====================================================================
        // COMBO ARRAYS
        // =====================================================================

        [Header("Side Combo (1 → 2 → 3...)")]
        public RangedComboStep[] sideCombo;

        [Header("Air Side Combo")]
        public RangedComboStep[] airSideCombo;

        [Header("Directional Attacks")]
        public RangedComboStep upAttack;
        public RangedComboStep downAttack;

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

        public bool TryGetRangedStep(AttackDirection dir, int comboIndex, bool isGrounded, out RangedComboStep step)
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