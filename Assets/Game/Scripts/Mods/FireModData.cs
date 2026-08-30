using UnityEngine;

namespace junklite
{
    [CreateAssetMenu(fileName = "FireMod", menuName = "Junklite/Mods/Fire")]
    public class FireModData : PassiveModData
    {
        [Header("Burn Effect")]
        [SerializeField] private StatusEffectDefinition burnStatusEffect;
        public float burnDamage = 5f;
        public float tickInterval = 0.5f;
        public float burnDuration = 3f;

        #region Hooks

        public override void OnHitRegistered(ModInstance instance, PlayerCharacter player, EnemyCharacter enemy, float damageDealt)
        {
            if (instance.IsBroken) return;
            if (enemy == null || !enemy.IsAlive || enemy.StatusEffects == null) return;

            StatusEffectApplication burn = burnStatusEffect != null
                ? burnStatusEffect.CreateApplication(player.gameObject)
                : StatusEffectApplication.DamageOverTime(
                    StatusEffectType.Burn,
                    burnDamage,
                    tickInterval,
                    burnDuration,
                    DamageType.Fire,
                    player.gameObject);

            enemy.StatusEffects.Apply(burn);
            instance.ConsumeDurability();
        }

        #endregion
    }
}
