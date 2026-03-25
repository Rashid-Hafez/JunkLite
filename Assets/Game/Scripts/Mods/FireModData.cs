using UnityEngine;

namespace junklite
{
    [CreateAssetMenu(fileName = "FireMod", menuName = "Junklite/Mods/Fire")]
    public class FireModData : PassiveModData
    {
        [Header("Burn Effect")]
        public float burnDamage = 5f;
        public float tickInterval = 0.5f;
        public float burnDuration = 3f;

        #region Hooks

        public override void OnHitRegistered(ModInstance instance, PlayerCharacter player, EnemyCharacter enemy, float damageDealt)
        {
            if (instance.IsBroken) return;
            if (enemy == null || !enemy.IsAlive || enemy.StatusEffects == null) return;

            var burn = new StatusEffectInstance(
                type: StatusEffectType.Burn,
                damagePerTick: burnDamage,
                tickInterval: tickInterval,
                duration: burnDuration,
                damageType: DamageType.Fire,
                source: null
            );

            enemy.StatusEffects.Apply(burn);
            instance.ConsumeDurability();
        }

        public override void OnEquip(PlayerCharacter player) { }

        public override void OnUnequip(PlayerCharacter player) { }

        #endregion
    }
}