using UnityEngine;
using System.Collections.Generic;

namespace junklite
{
    [CreateAssetMenu(fileName = "ElectricMod", menuName = "Junklite/Mods/Electric")]
    public class ElectricModData : PassiveModData
    {
        [Header("Zap Effect")]
        [SerializeField] private StatusEffectDefinition electricStatusEffect;
        public float zapDamage = 3f;
        public float tickInterval = 0.3f;
        public float zapDuration = 2f;

        [Header("Area Zap")]
        [Tooltip("Radius to chain zap to nearby enemies (0 = single target)")]
        public float zapRadius = 0f;
        [Range(0f, 1f)]
        [Tooltip("Damage multiplier for chained enemies")]
        public float areaDamageMultiplier = 0.5f;

        #region Hooks

        public override void OnHitRegistered(ModInstance instance, PlayerCharacter player, EnemyCharacter enemy, float damageDealt)
        {
            if (instance.IsBroken) return;
            if (enemy == null || !enemy.IsAlive || enemy.StatusEffects == null) return;

            ApplyZap(enemy, zapDamage, player.gameObject);

            if (zapRadius > 0f)
                ZapNearbyEnemies(enemy, player.gameObject);

            instance.ConsumeDurability();
        }

        #endregion

        #region Helpers

        private void ApplyZap(EnemyCharacter enemy, float damage, GameObject source)
        {
            StatusEffectApplication zap = electricStatusEffect != null
                ? new StatusEffectApplication(
                    electricStatusEffect.BuildSpec().WithDamagePerTick(damage),
                    source,
                    strengthOverride: damage,
                    definition: electricStatusEffect)
                : StatusEffectApplication.DamageOverTime(
                    StatusEffectType.Electric,
                    damage,
                    tickInterval,
                    zapDuration,
                    DamageType.Electric,
                    source);

            enemy.StatusEffects.Apply(zap);
        }

        private void ZapNearbyEnemies(EnemyCharacter origin, GameObject source)
        {
            float areaDamage = zapDamage * areaDamageMultiplier;
            Collider[] hits = Physics.OverlapSphere(origin.transform.position, zapRadius);
            var affectedEnemies = new HashSet<EnemyCharacter>();

            foreach (var hit in hits)
            {
                var nearbyEnemy = hit.GetComponentInParent<EnemyCharacter>();
                if (nearbyEnemy == null || nearbyEnemy == origin) continue;
                if (!nearbyEnemy.IsAlive || nearbyEnemy.StatusEffects == null) continue;
                if (!affectedEnemies.Add(nearbyEnemy)) continue;

                ApplyZap(nearbyEnemy, areaDamage, source);
            }
        }

        #endregion
    }
}
