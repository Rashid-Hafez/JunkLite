using UnityEngine;

namespace junklite
{
    /// <summary>
    /// Simple dummy enemy that only takes damage. Used for testing.
    /// No AI, no states, no movement - just stands there and gets hit.
    /// 
    /// CAPABILITIES: None - it's a dummy!
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
            stateMachine.RegisterStates(
                new IdleState(this),
                new DeadState(this)
            );

            stateMachine.SetInitialState<IdleState>();
        }

        protected override void Update()
        {
            // Dummy just stands there
        }

        // BEFORE: 15+ lines of knockback code
        // AFTER: Just invincibility check + health reset
        public override bool TakeDamage(DamageInfo info)
        {
            if (invincible)
                return false;

            bool damageDealt = base.TakeDamage(info);  // Base handles knockback!

            if (damageDealt && resetHealthOnHit && IsAlive && attributes?.Health != null)
                attributes.RestoreHealthToMax();

            return damageDealt;
        }

        // Dummy does nothing - disable all behavior responses
        public override void OnPlayerSpotted() { }
        public override void OnPlayerLost() { }
        public override void OnStunComplete() { }
        public override void OnAttackFinished() { }
        public override void OnPlayerInAttackRange() { }
    }
}