using UnityEngine;

namespace junklite
{
    /// <summary>
    /// Base class for active mods. Requires manual activation via dedicated input combo.
    /// Builds charges on enemy hits, activates when ready.
    /// 
    /// Example: PhantomStrike requires 3 hits, then player presses combo to slam.
    /// </summary>
    public abstract class ActiveModData : ModData
    {
        [Header("Activation")]
        [Tooltip("Number of hits required before mod can be activated")]
        public int chargesRequired = 3;

        /// <summary>Called when player lands a hit. Update charges on the instance.</summary>
        public virtual void OnHitRegistered(ModInstance instance, PlayerCharacter player, EnemyCharacter enemy)
        {
            instance.AddCharge(1);
        }

        /// <summary>Whether the mod can be activated right now.</summary>
        public virtual bool CanActivate(ModInstance instance, PlayerCharacter player)
        {
            return instance.CurrentCharges >= chargesRequired;
        }

        /// <summary>Execute the mod effect. Return true if effect was used (consumes durability).</summary>
        public abstract bool OnActivate(ModInstance instance, PlayerCharacter player);

        /// <summary>Called when mod is equipped to a slot.</summary>
        public virtual void OnEquip(PlayerCharacter player) { }

        /// <summary>Called when mod is removed or breaks.</summary>
        public virtual void OnUnequip(PlayerCharacter player) { }
    }
}