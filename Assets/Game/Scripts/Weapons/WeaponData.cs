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

        [Header("Description")]
        [TextArea(2, 5)]
        public string description;

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
        [Header("Audio")]
        public SoundEntry attackSfx;
        public SoundEntryGroup attackVariants;
        
        // =====================================================================
        // ABSTRACT INTERFACE
        // =====================================================================

        public abstract int GetComboLength(AttackDirection dir, bool isGrounded);
        public abstract bool TryGetAnimationName(AttackDirection dir, int comboIndex, bool isGrounded, out string animationName);
    }
}