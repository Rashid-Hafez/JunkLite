using UnityEngine;
using System.Collections.Generic;
using System.Buffers;
using UnityEngine.Pool;

namespace junklite
{
    [CreateAssetMenu(fileName = "ElectricMod", menuName = "Junklite/Mods/Electric")]
    public class ElectricModData : PassiveModData
    {
        private const int MaxOverlapCapacity = 256;

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
            Collider[] overlapBuffer = ArrayPool<Collider>.Shared.Rent(64);
            HashSet<EnemyCharacter> affectedEnemies = HashSetPool<EnemyCharacter>.Get();

            try
            {
                int hitCount;
                while (true)
                {
                    hitCount = Physics.OverlapSphereNonAlloc(
                        origin.transform.position,
                        zapRadius,
                        overlapBuffer);
                    if (hitCount < overlapBuffer.Length || overlapBuffer.Length >= MaxOverlapCapacity)
                        break;

                    Collider[] largerBuffer = ArrayPool<Collider>.Shared.Rent(
                        Mathf.Min(overlapBuffer.Length * 2, MaxOverlapCapacity));
                    ArrayPool<Collider>.Shared.Return(overlapBuffer, clearArray: true);
                    overlapBuffer = largerBuffer;
                }

                for (int i = 0; i < hitCount; i++)
                {
                    Collider hit = overlapBuffer[i];
                    if (hit == null) continue;
                    var nearbyEnemy = hit.GetComponentInParent<EnemyCharacter>();
                    if (nearbyEnemy == null || nearbyEnemy == origin) continue;
                    if (!nearbyEnemy.IsAlive || nearbyEnemy.StatusEffects == null) continue;
                    if (!affectedEnemies.Add(nearbyEnemy)) continue;

                    ApplyZap(nearbyEnemy, areaDamage, source);
                }
            }
            finally
            {
                ArrayPool<Collider>.Shared.Return(overlapBuffer, clearArray: true);
                HashSetPool<EnemyCharacter>.Release(affectedEnemies);
            }
        }

        #endregion
    }
}
