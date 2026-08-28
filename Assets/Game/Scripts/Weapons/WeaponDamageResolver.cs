using System.Collections.Generic;
using UnityEngine;

namespace junklite
{
    internal readonly struct WeaponDamageResolution
    {
        public DamageResult Result { get; }
        public EnemyCharacter Enemy { get; }

        public WeaponDamageResolution(DamageResult result, EnemyCharacter enemy = null)
        {
            Result = result;
            Enemy = enemy;
        }
    }

    /// <summary>
    /// Converts a weapon collider hit into one DamageRequest. It owns receiver
    /// resolution and multi-collider deduplication, but no weapon or presentation state.
    /// </summary>
    internal sealed class WeaponDamageResolver
    {
        private readonly GameObject source;

        public WeaponDamageResolver(GameObject source)
        {
            this.source = source;
        }

        public WeaponDamageResolution Resolve(
            Collider target,
            float damage,
            Vector2 knockback,
            HashSet<int> processedReceivers)
        {
            if (!DamageReceiverUtility.TryGetReceiver(target, out IDamageReceiver receiver))
                return Rejected(DamageOutcome.Invalid, damage);

            if (receiver is not Component receiverComponent)
                return Rejected(DamageOutcome.Invalid, damage);

            if (processedReceivers != null &&
                !processedReceivers.Add(receiverComponent.GetInstanceID()))
            {
                return Rejected(DamageOutcome.Invalid, damage);
            }

            if (!receiver.IsAlive)
                return Rejected(DamageOutcome.Dead, damage);

            DamageResult result = receiver.ReceiveDamage(new DamageRequest(
                damage,
                source,
                DamageType.Physical,
                knockback));

            if (!result.WasApplied)
                return new WeaponDamageResolution(result);

            EnemyCharacter enemy = receiverComponent.GetComponent<EnemyCharacter>()
                                ?? receiverComponent.GetComponentInParent<EnemyCharacter>()
                                ?? target.GetComponentInParent<EnemyCharacter>();

            return new WeaponDamageResolution(result, enemy);
        }

        private static WeaponDamageResolution Rejected(DamageOutcome outcome, float damage)
        {
            return new WeaponDamageResolution(DamageResult.Rejected(outcome, damage));
        }
    }
}
