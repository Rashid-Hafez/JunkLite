using UnityEngine;

namespace junklite
{
    /// <summary>
    /// Base class for passive mods. Active while in Mod Combat mode; no manual activation needed.
    /// Override the explicit lifecycle hooks on ModData for setup/cleanup, and
    /// OnHitRegistered for hit-reactive passives.
    /// </summary>
    public abstract class PassiveModData : ModData
    {
        /// <summary>Called when player lands a hit on an enemy. damageDealt is the actual damage applied.</summary>
        public virtual void OnHitRegistered(ModInstance instance, PlayerCharacter player, EnemyCharacter enemy, float damageDealt) { }
    }
}
