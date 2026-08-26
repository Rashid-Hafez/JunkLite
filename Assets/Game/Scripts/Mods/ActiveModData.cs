using UnityEngine;

namespace junklite
{
    /// <summary>
    /// Base class for active mods. Requires manual activation via dedicated input combo.
    /// Builds charges on enemy hits, activates when ready.
    /// 
    /// Subclasses override ExecuteAbility(); cooldown is handled automatically by TryActivate().
    /// </summary>
    public abstract class ActiveModData : ModData
    {
        [Header("Activation")]
        [Tooltip("Number of hits required before mod can be activated (0 = no charges needed)")]
        public int chargesRequired = 0;

        [Header("Cooldown")]
        [Tooltip("Cooldown in seconds after activation before mod can be used again (0 = no cooldown)")]
        public float cooldown = 0f;

        [Header("Slot UI")]
        public GameObject modSlotUIPrefab;

        [Header("Audio")]
        public SoundEntry activationSfx;

        /// <summary>Called when player lands a hit. Update charges on the instance.</summary>
        public virtual void OnHitRegistered(ModInstance instance, PlayerCharacter player, EnemyCharacter enemy, float damageDealt)
        {
            instance.AddCharge(1);
        }

        /// <summary>Whether the mod can be activated right now.</summary>
        public virtual bool CanActivate(ModInstance instance, PlayerCharacter player)
        {
            if (instance == null || instance.IsExecuting) return false;
            if (instance.IsOnCooldown) return false;
            if (chargesRequired <= 0) return true;
            return instance.CurrentCharges >= chargesRequired;
        }

        /// <summary>
        /// Public entry point called by the mod system. Handles cooldown automatically.
        /// Do NOT override this; override ExecuteAbility instead.
        /// </summary>
        public bool TryActivate(ModInstance instance, PlayerCharacter player, ModExecutionRunner executionRunner)
        {
            if (executionRunner == null) return false;
            if (!CanActivate(instance, player)) return false;

            bool used = ExecuteAbility(instance, player, executionRunner);

            if (used && cooldown > 0f)
                instance.StartCooldown(cooldown);

            return used;
        }

        /// <summary>
        /// Execute the mod effect. Return true if effect was used (consumes durability).
        /// Cooldown is handled automatically by TryActivate; do NOT call StartCooldown here.
        /// </summary>
        protected abstract bool ExecuteAbility(
            ModInstance instance,
            PlayerCharacter player,
            ModExecutionRunner executionRunner);
    }
}
