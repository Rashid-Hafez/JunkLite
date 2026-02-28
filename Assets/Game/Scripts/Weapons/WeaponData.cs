using UnityEngine;

namespace junklite
{
    /// <summary>
    /// Abstract base for all weapon types. Holds only universal data — identity, stats,
    /// timing, and socket offsets. Subclasses define their own ComboStep structs and arrays
    /// containing only fields relevant to their weapon type.
    /// </summary>
    public abstract class WeaponData : ScriptableObject
    {
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
        [Tooltip("Durability consumed per confirmed hit")]
        public float durabilityPerHit = 1f;

        // =====================================================================
        // ATTACK TIMING
        // =====================================================================

        [Header("Attack Timing")]
        [Tooltip("Delay after attack animation ends before player can attack again")]
        public float attackCooldown = 0.2f;
        [Tooltip("Time after attack ends to continue combo (must be > attackCooldown)")]
        public float comboWindow = 0.5f;

        public float ComboInputWindow => Mathf.Max(0, comboWindow - attackCooldown);

        // =====================================================================
        // SOCKET OFFSETS
        // =====================================================================

        [System.Serializable]
        public struct WeaponSocketOffset
        {
            public Vector3 localPositionOffset;
            public Vector3 localRotationOffsetEuler;
            public bool flipLocalScaleX;
            public bool flipLocalScaleY;
        }

        [Header("Weapon Socket Offsets")]
        public WeaponSocketOffset socketOffset;

        // =====================================================================
        // ABSTRACT INTERFACE — implemented by each subclass
        // CombatState uses these to stay type-agnostic.
        // =====================================================================

        /// <summary>
        /// Returns how many combo steps exist for the given direction and grounded state.
        /// CombatState uses this to wrap the combo index correctly without knowing the step type.
        /// </summary>
        public abstract int GetComboLength(AttackDirection dir, bool isGrounded);

        /// <summary>
        /// Returns the animation name for a given direction, combo index, and grounded state.
        /// CombatState uses this to trigger animations without knowing the step type.
        /// </summary>
        public abstract bool TryGetAnimationName(AttackDirection dir, int comboIndex, bool isGrounded, out string animationName);

        // =====================================================================
        // VALIDATION
        // =====================================================================

        protected void OnValidate()
        {
            if (comboWindow <= attackCooldown)
            {
                Debug.LogWarning($"[{name}] comboWindow ({comboWindow}s) should be greater than " +
                                 $"attackCooldown ({attackCooldown}s). Combo input window is only {ComboInputWindow}s!");
            }
        }
    }
}