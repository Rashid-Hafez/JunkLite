using UnityEngine;

namespace junklite
{
    /// <summary>
    /// Lifesteal Mod - Heals the player for a percentage of damage dealt to enemies.
    /// Consumes durability on each proc.
    /// </summary>
    [CreateAssetMenu(fileName = "LifestealMod", menuName = "Junklite/Mods/Lifesteal")]
    public class LifestealMod : PassiveModData
    {
        [Header("Lifesteal")]
        [Tooltip("Percentage of outgoing damage healed back (0-1)")]
        [Range(0f, 1f)]
        public float healPercent = 0.15f;

        public override void OnHitRegistered(ModInstance instance, PlayerCharacter player, EnemyCharacter enemy, float damageDealt)
        {
            if (player == null || !player.IsAlive) return;
            if (damageDealt <= 0f) return;

            float healAmount = damageDealt * healPercent;
            player.Heal(healAmount);

            instance.ConsumeDurability();
        }
    }
}