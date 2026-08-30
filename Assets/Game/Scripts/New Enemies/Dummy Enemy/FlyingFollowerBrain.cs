using UnityEngine;

namespace junklite
{
    /// <summary>
    /// Non-attacking flying follower: patrols while passive and chases the player
    /// while detected. FlyingHoverController owns the physical hover behavior.
    /// </summary>
    [RequireComponent(typeof(FlyingHoverController))]
    public sealed class FlyingFollowerBrain : EnemyBrain, IEnemyCapabilityProvider
    {
        [SerializeField, HideInInspector] private bool ownsSerializedConfiguration;
        [SerializeField] private PatrolBehavior patrol = new();
        [SerializeField] private ChaseBehavior chase = new();

        private FlyingHoverController hoverController;
        private NoHitstunBehavior interruptRecovery = new();
        private bool returningToPassive;

        public bool OwnsSerializedConfiguration => ownsSerializedConfiguration;

        protected override void Awake()
        {
            base.Awake();
            EnsureBehaviors();
            InitializeCapabilities();
        }

        protected override void OnEnable()
        {
            base.OnEnable();
            InitializeCapabilities();
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
                new ChaseState(Actor),
                new StunnedState(Actor),
                new ParriedState(Actor),
                new DeadState(Actor));

            if (patrol.HasPatrol)
                StateMachine.SetInitialState<PatrolState>();
            else
                StateMachine.SetInitialState<IdleState>();
        }

        protected override void OnTargetChanged(PlayerCharacter previous, PlayerCharacter current)
        {
            if (current != null)
            {
                chase.UpdateLastKnownPosition(current.transform.position);
                Actor.EnterCombat();
                hoverController?.SetFollowingTarget(true);
                ChangeState<ChaseState>();
                return;
            }

            ReturnToPassive();
        }

        protected override void EvaluateNextAction(bool actionCompleted = false)
        {
            if (!Actor.IsAlive || StateMachine.CurrentState is DeadState)
                return;

            if (Actor.HasTarget)
                ChangeState<ChaseState>();
            else
                ReturnToPassive();
        }

        public bool TryGetCapability<T>(out T capability) where T : class
        {
            if (patrol is T patrolCapability)
                capability = patrolCapability;
            else if (chase is T chaseCapability)
                capability = chaseCapability;
            else if (interruptRecovery is T noHitstun)
                capability = noHitstun;
            else
                capability = null;

            return capability != null;
        }

        public void ApplyLegacyConfiguration(PatrolBehavior patrolBehavior, ChaseBehavior chaseBehavior)
        {
            if (ownsSerializedConfiguration)
                return;

            patrol = patrolBehavior ?? new PatrolBehavior();
            chase = chaseBehavior ?? new ChaseBehavior();
            ownsSerializedConfiguration = true;
            InitializeCapabilities();
        }

        private void ReturnToPassive()
        {
            if (returningToPassive)
                return;

            returningToPassive = true;
            chase.ClearLastKnownPosition();
            Actor.ExitCombat();
            hoverController?.SetFollowingTarget(false);

            if (patrol.HasPatrol)
                ChangeState<PatrolState>();
            else
                ChangeState<IdleState>();

            returningToPassive = false;
        }

        private void EnsureBehaviors()
        {
            patrol ??= new PatrolBehavior();
            chase ??= new ChaseBehavior();
            interruptRecovery ??= new NoHitstunBehavior();
        }

        private void InitializeCapabilities()
        {
            EnsureBehaviors();
            patrol.Initialize(transform);
            hoverController = GetComponent<FlyingHoverController>();
            interruptRecovery.Completed -= HandleInterruptCompleted;
            interruptRecovery.Completed += HandleInterruptCompleted;
        }

        private void HandleInterruptCompleted() => EvaluateNextAction(true);

#if UNITY_EDITOR
        private void OnDrawGizmosSelected()
        {
            patrol?.DrawGizmos(transform);

            if (chase != null && chase.ChaseStopDistance > 0f)
            {
                Gizmos.color = new Color(0.5f, 1f, 0.5f, 0.3f);
                Gizmos.DrawWireSphere(transform.position, chase.ChaseStopDistance);
            }
        }
#endif
    }
}
