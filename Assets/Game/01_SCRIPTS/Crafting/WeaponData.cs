using UnityEngine;

namespace junklite
{
    [CreateAssetMenu(fileName = "WeaponData", menuName = "Junklite/Weapon Data")]
    public class WeaponData : ScriptableObject
    {
        // =====================================================================
        // COMBO STEP STRUCT
        // =====================================================================

        [System.Serializable]
        public struct ComboStep
        {
            [Header("Animation")]
            public string animationName;

            [Header("Combat")]
            public float damageMultiplier;
            public float hitRadius;

            [Header("VFX")]
            public GameObject slashPrefab;

            [Header("Attack Push")]
            [Tooltip("Forward impulse applied on attack (facing direction).")]
            public float forwardImpulse;
            [Tooltip("Vertical impulse applied on attack (up for Up, down for Down).")]
            public float verticalImpulse;
            [Tooltip("Override gravity multiplier during air attacks (1 = normal).")]
            public float airGravityMultiplier;
        }

        // =====================================================================
        // ENUMS
        // =====================================================================

        public enum Rarity
        {
            Common,
            Uncommon,
            Rare,
            Epic,
            Legendary
        }

        // =====================================================================
        // WEAPON IDENTITY
        // =====================================================================

        [Header("Identity")]
        public string weaponId;
        public string displayName;
        public WeaponType type;
        public Rarity rarity;
        public Sprite icon;

        // =====================================================================
        // BASE STATS
        // =====================================================================

        [Header("Base Stats")]
        public float baseDamage = 10f;
        public float baseAttackSpeed = 1f;
        public Vector2 knockbackForce = new Vector2(8f, 4f);
        public int maxWeaponDurability = 100;

        // =====================================================================
        // ATTACK TIMING
        // =====================================================================

        [Header("Attack Timing")]
        [Tooltip("Delay after attack animation ends before player can attack again")]
        public float attackCooldown = 0.2f;

        [Tooltip("Time after attack ends to continue combo (must be > attackCooldown)")]
        public float comboWindow = 0.5f;

        /// <summary>
        /// The actual input window for combos (comboWindow - attackCooldown).
        /// </summary>
        public float ComboInputWindow => Mathf.Max(0, comboWindow - attackCooldown);

        private void OnValidate()
        {
            // Ensure combo window is always greater than cooldown
            if (comboWindow <= attackCooldown)
            {
                Debug.LogWarning($"[{name}] comboWindow ({comboWindow}s) should be greater than attackCooldown ({attackCooldown}s). Combo input window is only {ComboInputWindow}s!");
            }
        }

        // =====================================================================
        // COMBO DATA
        // =====================================================================

        [Header("Side Combo (1 → 2 → 3...)")]
        public ComboStep[] sideCombo;

        [Header("Air Side Combo (1 → 2 → 3...)")]
        public ComboStep[] airSideCombo;

        [Header("Directional Attacks")]
        public ComboStep upAttack;
        public ComboStep downAttack;

        // =====================================================================
        // MOD SLOTS
        // =====================================================================

        [Header("Mod Slots")]
        public int maxActiveModSlots = 2;
        public int maxReserveSlots = 4;

        // =====================================================================
        // HELPER METHODS
        // =====================================================================

        /// <summary>
        /// Gets combo step for the given direction and index.
        /// </summary>
        public bool TryGetComboStep(AttackDirection dir, int comboIndex, out ComboStep step, out string animationName)
        {
            step = default;
            animationName = null;

            switch (dir)
            {
                case AttackDirection.Up:
                    step = upAttack;
                    animationName = upAttack.animationName;
                    return !string.IsNullOrEmpty(animationName);

                case AttackDirection.Down:
                    step = downAttack;
                    animationName = downAttack.animationName;
                    return !string.IsNullOrEmpty(animationName);

                case AttackDirection.Side:
                default:
                    if (sideCombo == null || sideCombo.Length == 0)
                        return false;

                    int idx = Mathf.Clamp(comboIndex, 0, sideCombo.Length - 1);
                    step = sideCombo[idx];
                    animationName = step.animationName;
                    return !string.IsNullOrEmpty(animationName);
            }
        }

        public int SideComboLength => sideCombo?.Length ?? 0;
    }
}