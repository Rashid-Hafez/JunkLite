using UnityEngine;

namespace junklite
{
    /// <summary>
    /// Reusable brain for test actors that either remain idle or patrol forever.
    /// Perception is intentionally ignored and no combat decisions are made.
    /// </summary>
    public sealed class PassiveEnemyBrain : EnemyBrain, IEnemyCapabilityProvider
    {
        [SerializeField, HideInInspector] private bool ownsSerializedConfiguration;
        [SerializeField] private bool patrolWhenPassive;
        [SerializeField] private PatrolBehavior patrol = new();

        private NoHitstunBehavior interruptRecovery = new();

        public bool OwnsSerializedConfiguration => ownsSerializedConfiguration;

        protected override void Awake()
        {
            base.Awake();
            EnsureBehavior();
            patrol.Initialize(transform);
        }

        protected override void OnEnable()
        {
            base.OnEnable();
            EnsureBehavior();
            interruptRecovery.Completed -= HandleInterruptCompleted;
            interruptRecovery.Completed += HandleInterruptCompleted;
        }

        protected override void OnDisable()
        {
            if (interruptRecovery != null)
                interruptRecovery.Completed -= HandleInterruptCompleted;

            base.OnDisable();
        }

        protected override void InitializeStateMachine()
        {
            StateMachine.RegisterStates(
                new PatrolState(Actor),
                new IdleState(Actor),
                new StunnedState(Actor),
                new ParriedState(Actor),
                new DeadState(Actor));

            if (patrolWhenPassive && patrol.HasPatrol)
                StateMachine.SetInitialState<PatrolState>();
            else
                StateMachine.SetInitialState<IdleState>();
        }

        protected override void OnTargetChanged(PlayerCharacter previous, PlayerCharacter current) { }
        protected override void EvaluateNextAction(bool actionCompleted = false) { }

        public bool TryGetCapability<T>(out T capability) where T : class
        {
            if (patrol is T patrolCapability)
                capability = patrolCapability;
            else if (interruptRecovery is T noHitstun)
                capability = noHitstun;
            else
                capability = null;

            return capability != null;
        }

        public void ApplyLegacyConfiguration(bool usePatrol, PatrolBehavior patrolBehavior)
        {
            if (ownsSerializedConfiguration)
                return;

            patrolWhenPassive = usePatrol;
            patrol = patrolBehavior ?? new PatrolBehavior();
            ownsSerializedConfiguration = true;
            patrol.Initialize(transform);
        }

        private void EnsureBehavior()
        {
            patrol ??= new PatrolBehavior();
            interruptRecovery ??= new NoHitstunBehavior();
        }

        private void HandleInterruptCompleted()
        {
            if (!Actor.IsAlive)
                return;

            if (patrolWhenPassive && patrol.HasPatrol)
                ChangeState<PatrolState>();
            else
                ChangeState<IdleState>();
        }

#if UNITY_EDITOR
        private void OnDrawGizmosSelected()
        {
            if (patrolWhenPassive)
                patrol?.DrawGizmos(transform);
        }
#endif
    }
}
