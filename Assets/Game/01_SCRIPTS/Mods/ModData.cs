using UnityEngine;

namespace junklite
{
    /// <summary>
    /// Base class for all mods. One ScriptableObject = one complete mod.
    /// Contains config, visuals, AND behavior - no separate logic class needed.
    /// 
    /// To create a new mod:
    /// 1. Create a new class extending ModData
    /// 2. Add [CreateAssetMenu] attribute
    /// 3. Override OnHit() for hit effects
    /// 4. Create asset in Unity (Right-click → Create → Junklite/Mods/YourMod)
    /// </summary>
    public abstract class ModData : ScriptableObject
    {
        [Header("Identity")]
        public string modName;
        public Sprite icon;
        public ModRarity rarity;
        public ModElement element;

        [Header("Visuals")]
        [Tooltip("Prefab spawned by WorldModPickup to represent this mod in the world")]
        public GameObject visualPrefab;

        [Header("Durability")]
        public float maxDurability = 20f;
        public float durabilityPerHit = 1f;

        [Header("Air Combat")]
        [Tooltip("Bonus air attacks allowed while airborne.")]
        public int bonusAirAttacks = 0;

        public enum ModRarity { Common, Uncommon, Rare, Legendary }
        public enum ModElement { None, Fire, Ice, Lightning, Poison }

        // === BEHAVIOR - Override in subclasses ===

        /// <summary>
        /// Called when weapon hits an enemy.
        /// Use for: status effects, player buffs, special hit behavior.
        /// </summary>
        /// <param name="weapon">The weapon that hit</param>
        /// <param name="enemy">The enemy that was hit (for status effects)</param>
        /// <param name="player">The player who attacked (for player buffs like pogo)</param>
        /// <returns>True if the effect was used (consumes durability), false otherwise</returns>
        public virtual bool OnHit(WeaponInstance weapon, EnemyCharacter enemy, PlayerCharacter player) => false;

        /// <summary>
        /// Called when mod is equipped to a weapon.
        /// Use for: enabling VFX, applying passive buffs.
        /// </summary>
        public virtual void OnEquip(WeaponInstance weapon) { }

        /// <summary>
        /// Called when mod is removed or breaks.
        /// Use for: disabling VFX, removing passive buffs.
        /// </summary>
        public virtual void OnUnequip(WeaponInstance weapon) { }
    }
}