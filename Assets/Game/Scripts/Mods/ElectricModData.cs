using UnityEngine;

namespace junklite
{
    /*
    [CreateAssetMenu(menuName = "Junklite/Mods/Electric")]
    public class ElectricModData : ModData
    {
        [Header("Zap Effect")]
        public float zapDamage = 3f;
        public float tickInterval = 0.3f;
        public float zapDuration = 2f;

        [Header("Area Zap")]
        [Tooltip("Radius around hit enemy to zap other enemies (0 = single target only)")]
        public float zapRadius = 0f;
        [Range(0f, 1f)]
        [Tooltip("Damage multiplier for nearby enemies (e.g., 0.5 = 50% damage)")]
        public float areaDamageMultiplier = 0.5f;

        public override bool OnHit(WeaponInstance weapon, EnemyCharacter enemy, PlayerCharacter player)
        {
            if (enemy == null || enemy.StatusEffects == null)
                return false;

            // Zap the main target with full damage
            ApplyZap(enemy, zapDamage, weapon);

            // Zap all nearby enemies if radius > 0
            if (zapRadius > 0f)
            {
                ZapNearbyEnemies(enemy, weapon);
            }

            return true;
        }

        private void ApplyZap(EnemyCharacter enemy, float damage, WeaponInstance weapon)
        {
            var zap = new StatusEffectInstance(
                type: StatusEffectType.Electric,
                damagePerTick: damage,
                tickInterval: tickInterval,
                duration: zapDuration,
                damageType: DamageType.Electric,
                source: weapon != null ? weapon.gameObject : null
            );

            enemy.StatusEffects.Apply(zap);
        }

        private void ZapNearbyEnemies(EnemyCharacter origin, WeaponInstance weapon)
        {
            float areaDamage = zapDamage * areaDamageMultiplier;

            Collider[] hits = Physics.OverlapSphere(origin.transform.position, zapRadius);

            foreach (var hit in hits)
            {
                var nearbyEnemy = hit.GetComponent<EnemyCharacter>();

                // Skip if same enemy, dead, or no status effects
                if (nearbyEnemy == null || nearbyEnemy == origin || !nearbyEnemy.IsAlive)
                    continue;

                if (nearbyEnemy.StatusEffects == null)
                    continue;

                ApplyZap(nearbyEnemy, areaDamage, weapon);
            }
        }

        public override void OnEquip(WeaponInstance weapon)
        {
            Debug.Log($"[ElectricMod] Equipped - weapon now deals electric damage!");
        }

        public override void OnUnequip(WeaponInstance weapon)
        {
            Debug.Log($"[ElectricMod] Unequipped");
        }
    }
    */
}