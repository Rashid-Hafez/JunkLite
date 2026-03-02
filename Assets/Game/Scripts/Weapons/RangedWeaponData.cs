using UnityEngine;

namespace junklite
{
    [CreateAssetMenu(fileName = "RangedWeaponData", menuName = "Junklite/Ranged Weapon Data")]
    public class RangedWeaponData : WeaponData
    {
        [System.Serializable]
        public struct RangedComboStep
        {
            [Header("Animation")]
            public string animationName;
            [Tooltip("0-1 normalized time in the animation when the bullet spawns.")]
            [Range(0f, 1f)]
            public float fireAtNormalizedTime;

            [Header("Projectile")]
            public int bulletCount;
            public float bulletRadius;
            public float damageMultiplier;
            [Tooltip("Delay between bullets in a burst. 0 = instant.")]
            public float fireInterval;
            public float bulletSpeed;

            [Header("Arc Spread")]
            [Tooltip("Total cone angle in degrees for bullet fan. 0 = straight line.")]
            public float arcSpreadAngle;
            [Tooltip("Radius of the OverlapSphere for instant damage on directional attacks (down/up). " +
                     "Bullets are cosmetic for directional — this sphere does the actual hit. 0 = fallback 1.5.")]
            public float blastDamageRadius;

            [Header("Attack Push")]
            public float forwardImpulse;
            public float forwardImpulseDuration;

            [Header("Hover")]
            [Tooltip("Gravity multiplier during attack. 0 = full freeze. -1 = no override.")]
            public float hoverGravityMultiplier;

            [Header("Bullet Time")]
            [Tooltip("TimeScale during burst. 0 or 1 = disabled. 0.2 = dramatic slow-mo.")]
            [Range(0f, 1f)]
            public float bulletTimeScale;
            [Tooltip("How long slow-mo holds after burst, in REAL seconds.")]
            public float bulletTimeDuration;
            [Tooltip("How long timeScale lerps back to 1.0, in REAL seconds.")]
            public float bulletTimeRestoreDuration;

            [Header("Smooth Recoil")]
            [Tooltip("Recoil push magnitude. Direction auto-resolves opposite to attack.")]
            public float hitRecoil;
            [Tooltip("Recoil curve duration. 0.08-0.12 = snappy. 0.2+ = floaty.")]
            public float recoilDuration;
        }

        [Header("Projectile")]
        public GameObject bulletPrefab;

        [Header("Side Combo (1 -> 2 -> 3...)")]
        public RangedComboStep[] sideCombo;

        [Header("Air Side Combo")]
        public RangedComboStep[] airSideCombo;

        [Header("Directional Attacks")]
        public RangedComboStep upAttack;
        public RangedComboStep downAttack;

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