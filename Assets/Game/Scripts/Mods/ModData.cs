using UnityEngine;

namespace junklite
{
    /// <summary>
    /// Base class for all mods. Subclass PassiveModData or ActiveModData to create mods.
    /// 
    /// To create a new mod:
    /// 1. Extend PassiveModData (for always-on effects) or ActiveModData (for manual activation)
    /// 2. Add [CreateAssetMenu] attribute
    /// 3. Override the relevant hooks
    /// 4. Create asset in Unity (Right-click → Create → Junklite/Mods/YourMod)
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

        [Header("Visuals")]
        [Tooltip("Prefab spawned by WorldModPickup to represent this mod in the world")]
        public GameObject visualPrefab;

        [Header("Durability")]
        public float maxDurability = 20f;
        public float durabilityPerUse = 1f;
    }
}