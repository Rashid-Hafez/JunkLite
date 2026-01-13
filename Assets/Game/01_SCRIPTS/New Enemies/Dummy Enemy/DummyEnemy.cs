using UnityEngine;

namespace junklite
{
    /// <summary>
    /// Simple dummy enemy that only takes damage. Used for testing.
    /// No AI, no states, no movement - just stands there and gets hit.
    /// </summary>
    public class DummyEnemy : EnemyCharacter
    {
        [Header("Dummy Settings")]
        [SerializeField] private bool invincible = false;
        [SerializeField] private bool resetHealthOnHit = false;

        private Rigidbody rb;

        protected override void Awake()
        {
            base.Awake();
            rb = GetComponent<Rigidbody>();

            enemyType = EnemyType.Dummy;
        }

        protected override void InitializeStateMachine()
        {
            // Register only idle and dead states
            stateMachine.RegisterStates(
                new IdleState(this),
                new DeadState(this)
            );

            stateMachine.SetInitialState<IdleState>();
        }

        protected override void Update()
        {
            // Do nothing - dummy just stands there
        }

        public override void TakeDamage(DamageInfo info)
        {
            if (invincible)
                return;

            base.TakeDamage(info);

            // Apply knockback - KnockbackForce.x is already signed for direction
            if (rb != null && info.KnockbackForce.sqrMagnitude > 0f)
            {
                Vector3 knockback = new Vector3(
                    info.KnockbackForce.x,
                    info.KnockbackForce.y,
                    0f
                );
                rb.AddForce(knockback, ForceMode.Impulse);
            }

            // Reset health after hit if enabled
            if (resetHealthOnHit && IsAlive && attributes != null && attributes.Health != null)
                attributes.RestoreHealthToMax();
        }

        // Disable all behavior responses
        public override void OnPlayerSpotted() { }
        public override void OnPlayerLost() { }
        public override void OnChargeComplete() { }
        public override void OnDashComplete() { }
        public override void OnGrabComplete() { }
        public override void OnRecoveryComplete() { }
        public override void OnStunComplete() { }
        public override void OnAttackFinished() { }
        public override void OnPlayerInAttackRange() { }
    }
}