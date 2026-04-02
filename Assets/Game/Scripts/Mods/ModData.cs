using UnityEngine;

namespace junklite
{
    /// <summary>
    /// Base class for all mods. Subclass PassiveModData or ActiveModData to create mods.
    /// </summary>
    public abstract class ModData : ScriptableObject
    {
        public enum ModType { Active, Passive }
        public enum ModRarity { Common, Uncommon, Rare, Legendary }
        public enum ModElement { None, Fire, Ice, Lightning, Poison }

        [Header("Identity")]
        public string modName;
        public Sprite icon;
        public ModType modType;
        public ModRarity rarity;
        public ModElement element;

        [Header("Description")]
        [TextArea(2, 5)]
        public string description;

        [Tooltip("Representative damage value shown in the UI. For active mods this is the base hit damage; " +
                 "for passive mods it is the damage per tick / proc.")]
        public float baseDamage;

        [Header("Visuals")]
        [Tooltip("Prefab spawned by WorldModPickup to represent this mod in the world")]
        public GameObject visualPrefab;

        [Header("Durability")]
        public float maxDurability = 20f;
        public float durabilityPerUse = 1f;
    }
}