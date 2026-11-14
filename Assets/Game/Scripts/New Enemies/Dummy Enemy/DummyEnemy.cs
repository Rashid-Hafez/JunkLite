using UnityEngine;

namespace junklite
{
    /// <summary>
    /// Damage-test Dummy identity. PassiveEnemyBrain supplies its idle lifecycle;
    /// this component keeps only the Dummy-specific damage options.
    /// </summary>
    [RequireComponent(typeof(PassiveEnemyBrain))]
    public sealed class DummyEnemy : EnemyCharacter
    {
        [Header("Dummy Settings")]
        [SerializeField] private bool invincible;
        [SerializeField] private bool resetHealthOnHit;

        protected override void Awake()
        {
            base.Awake();
            enemyType = EnemyType.Dummy;

            PassiveEnemyBrain brain = GetComponent<PassiveEnemyBrain>();
            if (brain == null)
                brain = gameObject.AddComponent<PassiveEnemyBrain>();

            brain.ApplyLegacyConfiguration(false, null);
        }

        public override DamageResult ReceiveDamage(DamageRequest request)
        {
            if (invincible)
                return DamageResult.Rejected(DamageOutcome.Invulnerable, request.Amount);

            DamageResult result = base.ReceiveDamage(request);
            if (result.WasApplied && resetHealthOnHit && IsAlive && attributes?.Health != null)
                attributes.RestoreHealthToMax();

            return result;
        }
    }
}
