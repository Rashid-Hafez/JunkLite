using UnityEngine;

namespace junklite
{
    /// <summary>
    /// Base class for passive mods. Active while in Mod Combat mode — no manual activation needed.
    /// Override OnEquip/OnUnequip for stat buffs, and OnHitRegistered for hit-reactive passives.
    /// </summary>
    public abstract class PassiveModData : ModData
    {
        /// <summary>Called when entering Mod Combat (apply buffs, enable VFX).</summary>
        public virtual void OnEquip(PlayerCharacter player) { }

        /// <summary>Called when leaving Mod Combat or mod breaks (remove buffs, disable VFX).</summary>
        public virtual void OnUnequip(PlayerCharacter player) { }

        /// <summary>Called when player lands a hit on an enemy. Use for hit-reactive passives (e.g. lifesteal).</summary>
        public virtual void OnHitRegistered(ModInstance instance, PlayerCharacter player, EnemyCharacter enemy) { }
    }
}