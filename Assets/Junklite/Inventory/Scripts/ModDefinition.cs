using UnityEngine;

namespace Junklite.Inventory
{
    public enum ModRarity
    {
        Junk = 0,
        Standard = 1,
        Classified = 2,
        Elite = 3,
    }

    public enum ModType
    {
        Active,
        Passive,
    }

    /// <summary>
    /// One mod from the GDD mod table. Create more with Assets > Create > Junklite > Mod.
    /// </summary>
    [CreateAssetMenu(menuName = "Junklite/Mod", fileName = "NewMod", order = 0)]
    public class ModDefinition : ScriptableObject
    {
        [Tooltip("Stable id for save files, e.g. dont-blink.")]
        [SerializeField] string id;
        [SerializeField] string displayName;
        [SerializeField] ModRarity rarity;
        [SerializeField] ModType type;

        [Tooltip("Listed as both active and passive in the GDD. It still goes in an active slot.")]
        [SerializeField] bool hybrid;

        [Tooltip("Seconds. 0 means no cooldown.")]
        [Min(0)] [SerializeField] float cooldown;

        [Tooltip("As written in the GDD, e.g. x1.5. Leave empty if it doesn't change damage.")]
        [SerializeField] string damage;

        [Tooltip("Tick if the damage change only applies while the mod is active.")]
        [SerializeField] bool damageOnlyWhenActive;

        [TextArea(3, 6)] [SerializeField] string description;
        [SerializeField] Texture2D icon;

        [Tooltip("Shows the orange dot in storage until the player has looked at it.")]
        [SerializeField] bool markAsNew;

        public string Id => string.IsNullOrEmpty(id) ? name : id;
        public string DisplayName => string.IsNullOrEmpty(displayName) ? name : displayName;
        public ModRarity Rarity => rarity;
        public int Tier => (int)rarity;
        public ModType Type => type;
        public bool Hybrid => hybrid;
        public float Cooldown => cooldown;
        public bool HasCooldown => cooldown > 0f;
        public string Damage => damage;
        public bool DamageOnlyWhenActive => damageOnlyWhenActive;
        public string Description => description;
        public Texture2D Icon => icon;
        public bool MarkAsNew => markAsNew;

        public string TypeLabel => hybrid ? "Active / Passive" : type == ModType.Active ? "Active" : "Passive";

        /// <summary>Makes a mod in code, for mods that don't exist as assets (from a server or a save file, say).</summary>
        public static ModDefinition Create(string id, string displayName, ModRarity rarity, ModType type,
            float cooldown = 0f, string damage = null, string description = null, Texture2D icon = null)
        {
            var mod = CreateInstance<ModDefinition>();
            mod.name = id;
            mod.id = id;
            mod.displayName = displayName;
            mod.rarity = rarity;
            mod.type = type;
            mod.cooldown = cooldown;
            mod.damage = damage;
            mod.description = description;
            mod.icon = icon;
            return mod;
        }
    }
}
