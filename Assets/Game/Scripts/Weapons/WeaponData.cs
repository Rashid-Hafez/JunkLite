using UnityEngine;

namespace junklite
{
    /// <summary>
    /// Read-only configuration for one arm weapon (fists, blades). The player holds exactly
    /// one of these at a time (see <see cref="PlayerArmament"/>); weapons are permanent
    /// upgrades, not items, so there is no durability, rarity or slot data here.
    /// Subclasses define their own combo step structs and arrays.
    /// </summary>
    public abstract class WeaponData : ScriptableObject
    {
        [Header("Identity")]
        [Tooltip("Stable id written to the player profile when this weapon is acquired. Never change it on a shipped asset.")]
        public string weaponId;
        public string displayName;
        [Tooltip("Allows this weapon to be used for sword grappling.")]
        public bool enablesSwordGrapple;
        public Sprite icon;

        [Header("Description")]
        [TextArea(2, 5)]
        public string description;

        [Header("Base Stats")]
        public float baseDamage = 10f;
        public Vector2 knockbackForce = new Vector2(8f, 4f);

        [Header("Audio")]
        public SoundEntry attackSfx;
        public SoundEntryGroup attackVariants;

        /// <summary>The id used in the profile; falls back to the asset name for assets authored without one.</summary>
        public string ProfileId => string.IsNullOrEmpty(weaponId) ? name : weaponId;

        public abstract int GetComboLength(AttackDirection dir, bool isGrounded);
        public abstract bool TryGetAnimationName(AttackDirection dir, int comboIndex, bool isGrounded, out string animationName);
    }
}
