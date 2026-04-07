using UnityEngine;

namespace junklite
{
    [CreateAssetMenu(fileName = "ElectricMod", menuName = "Junklite/Mods/Electric")]
    public class ElectricModData : PassiveModData
    {
        [Header("Zap Effect")]
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

            ApplyZap(enemy, zapDamage);

            if (zapRadius > 0f)
                ZapNearbyEnemies(enemy);

            instance.ConsumeDurability();
        }

        public override void OnEquip(PlayerCharacter player) { }

        public override void OnUnequip(PlayerCharacter player) { }

        #endregion

        #region Helpers

        private void ApplyZap(EnemyCharacter enemy, float damage)
        {
            var zap = new StatusEffectInstance(
                type: StatusEffectType.Electric,
                damagePerTick: damage,
                tickInterval: tickInterval,
                duration: zapDuration,
                damageType: DamageType.Electric,
                source: null
            );

            enemy.StatusEffects.Apply(zap);
        }

        private void ZapNearbyEnemies(EnemyCharacter origin)
        {
            float areaDamage = zapDamage * areaDamageMultiplier;
            Collider[] hits = Physics.OverlapSphere(origin.transform.position, zapRadius);

            foreach (var hit in hits)
            {
                var nearbyEnemy = hit.GetComponent<EnemyCharacter>();
                if (nearbyEnemy == null || nearbyEnemy == origin) continue;
                if (!nearbyEnemy.IsAlive || nearbyEnemy.StatusEffects == null) continue;

                ApplyZap(nearbyEnemy, areaDamage);
            }
        }

        #endregion
    }
}