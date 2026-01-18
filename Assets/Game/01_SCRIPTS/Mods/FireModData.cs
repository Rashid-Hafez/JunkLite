using UnityEngine;

namespace junklite
{
    [CreateAssetMenu(menuName = "Junklite/Mods/Fire")]
    public class FireModData : ModData
    {
        [Header("Burn Effect")]
        public float burnDamage = 5f;
        public float tickInterval = 0.5f;
        public float burnDuration = 3f;

        public override bool OnHit(WeaponInstance weapon, EnemyCharacter enemy, PlayerCharacter player)
        {
            if (enemy == null || enemy.StatusEffects == null)
                return false;

            var burn = new StatusEffectInstance(
                type: StatusEffectType.Burn,
                damagePerTick: burnDamage,
                tickInterval: tickInterval,
                duration: burnDuration,
                damageType: DamageType.Fire,
                source: weapon != null ? weapon.gameObject : null
            );

            enemy.StatusEffects.Apply(burn);
            return true;
        }

        public override void OnEquip(WeaponInstance weapon)
        {
            //Debug.Log($"[FireMod] Equipped - weapon now deals fire damage!");
        }

        public override void OnUnequip(WeaponInstance weapon)
        {
            //Debug.Log($"[FireMod] Unequipped");
        }
    }
}