using UnityEngine;

namespace junklite
{
    /// <summary>
    /// Reusable decision loop for enemies that wait or patrol, chase the player,
    /// perform one melee action, and then evaluate again.
    /// </summary>
    public class MeleeChaserBrain : EnemyBrain, IEnemyCapabilityProvider
    {
        [SerializeField, HideInInspector] private bool ownsSerializedConfiguration;

        [Header("Presentation")]
        [SerializeField] protected EnemySpineAnimationController spineController;

        [Header("Passive")]
        [SerializeField] protected bool patrolWhenPassive;
        [SerializeField] protected PatrolBehavior patrol = new();

        [Header("Chase")]
        [SerializeField] protected ChaseBehavior chase = new();
        [SerializeField] protected float pursuitRadius = 12f;

        [Header("Melee Attack")]
        [SerializeField] protected MeleeAttackBehavior melee = new();

        [Header("Stun")]
        [SerializeField] protected StunBehavior stun = new();

        private bool returningToPassive;

        public bool OwnsSerializedConfiguration => ownsSerializedConfiguration;

        protected override void Awake()
        {
            base.Awake();
            EnsureBehaviors();
            InitializeBaseCapabilities();
        }

        protected override void OnEnable()
        {
            base.OnEnable();
            InitializeBaseCapabilities();
        }

        protected override void OnDisable()
        {
            UninitializeBaseCapabilities();
            base.OnDisable();
        }

        protected override void InitializeStateMachine()
        {
            StateMachine.RegisterStates(
                new PatrolState(Actor),
                new IdleState(Actor),
                new ChaseState(Actor),
                new MeleeAttackState(Actor),
                new StunnedState(Actor),
                new ParriedState(Actor),
                new DeadState(Actor));

            if (patrolWhenPassive && patrol.HasPatrol)
                StateMachine.SetInitialState<PatrolState>();
            else
                StateMachine.SetInitialState<IdleState>();
        }

        protected override void TickBrain()
        {
            if (!Actor.IsInCombat || IsForcedState())
                return;

            if (Actor.HasTarget)
            {
                chase.UpdateLastKnownPosition(Actor.Target.position);

                float distance = Movement.GetAbsAxisDistance(transform.position, Actor.Target.position);
                if (distance > pursuitRadius)
                {
                    Perception?.ClearTarget();
                    return;
                }

                if (StateMachine.CurrentState is ChaseState && IsTargetInMeleeRange())
                    EvaluateNextAction();
            }
            else if (!chase.HasLastKnownPosition && !IsDecisionLocked())
            {
                ReturnToPassive();
            }
        }

        protected override void OnTargetChanged(PlayerCharacter previous, PlayerCharacter current)
        {
            if (current != null)
            {
                chase.UpdateLastKnownPosition(current.transform.position);
                Actor.EnterCombat();
                Perception?.SetRadius(pursuitRadius);

                if (!IsDecisionLocked())
                    EvaluateNextAction();
                return;
            }

            if (!IsDecisionLocked())
                EvaluateNextAction();
        }

        protected override void EvaluateNextAction(bool actionCompleted = false)
        {
            if (!Actor.IsAlive || StateMachine.CurrentState is DeadState)
                return;
            if (!actionCompleted && IsDecisionLocked())
                return;

            if (Actor.HasTarget)
            {
                if (IsTargetInMeleeRange())
                    ChangeState<MeleeAttackState>();
                else
                    ChangeState<ChaseState>();
                return;
            }

            if (chase.HasLastKnownPosition)
            {
                ChangeState<ChaseState>();
                return;
            }

            ReturnToPassive();
        }

        protected virtual bool IsDecisionLocked()
        {
            IState current = StateMachine.CurrentState;
            return current is MeleeAttackState || current is StunnedState || current is ParriedState;
        }

        protected bool IsTargetInMeleeRange()
        {
            if (!Actor.HasTarget)
                return false;

            float stopDistance = chase.ChaseStopDistance > 0f
                ? chase.ChaseStopDistance
                : Actor.AttackRange;
            float distance = Movement.GetAbsAxisDistance(transform.position, Actor.Target.position);
            return distance <= stopDistance;
        }

        protected void ReturnToPassive()
        {
            if (returningToPassive)
                return;

            returningToPassive = true;
            chase.ClearLastKnownPosition();
            Actor.ExitCombat();
            Perception?.ResetRadius();

            if (Perception != null && Perception.HasTarget)
                Perception.ClearTarget();

            if (patrolWhenPassive && patrol.HasPatrol)
                ChangeState<PatrolState>();
            else
                ChangeState<IdleState>();

            returningToPassive = false;
        }

        public virtual bool TryGetCapability<T>(out T capability) where T : class
        {
            if (patrol is T patrolCapability)
                capability = patrolCapability;
            else if (chase is T chaseCapability)
                capability = chaseCapability;
            else if (melee is T meleeCapability)
                capability = meleeCapability;
            else if (stun is T stunCapability)
                capability = stunCapability;
            else
                capability = null;

            return capability != null;
        }

        /// <summary>Runtime bridge for prefabs that have not yet serialized this brain.</summary>
        public void ApplyLegacyConfiguration(
            EnemySpineAnimationController animationController,
            bool usePatrol,
            PatrolBehavior patrolBehavior,
            ChaseBehavior chaseBehavior,
            float configuredPursuitRadius,
            MeleeAttackBehavior meleeBehavior,
            StunBehavior stunBehavior)
        {
            if (ownsSerializedConfiguration)
                return;

            UninitializeBaseCapabilities();
            spineController = animationController;
            patrolWhenPassive = usePatrol;
            patrol = patrolBehavior ?? new PatrolBehavior();
            chase = chaseBehavior ?? new ChaseBehavior();
            pursuitRadius = configuredPursuitRadius;
            melee = meleeBehavior ?? new MeleeAttackBehavior();
            stun = stunBehavior ?? new StunBehavior();
            ownsSerializedConfiguration = true;
            InitializeBaseCapabilities();
        }

        private void EnsureBehaviors()
        {
            patrol ??= new PatrolBehavior();
            chase ??= new ChaseBehavior();
            melee ??= new MeleeAttackBehavior();
            stun ??= new StunBehavior();
        }

        private void InitializeBaseCapabilities()
        {
            EnsureBehaviors();
            UninitializeBaseCapabilities();

            patrol.Initialize(transform);
            if (melee.MeleeHitbox == null)
            {
                Hitbox resolved = GetComponentInChildren<Hitbox>(true);
                if (resolved != null)
                    melee.AssignHitbox(resolved);
            }

            melee.Initialize(gameObject, spineController);
            chase.ReachedTarget += HandleReachedTarget;
            melee.Completed += HandleMeleeCompleted;
            stun.Completed += HandleStunCompleted;
        }

        private void UninitializeBaseCapabilities()
        {
            if (chase != null)
                chase.ReachedTarget -= HandleReachedTarget;
            if (melee != null)
            {
                melee.Completed -= HandleMeleeCompleted;
                melee.Dispose();
            }
            if (stun != null)
                stun.Completed -= HandleStunCompleted;
        }

        private void HandleReachedTarget()
        {
            chase.ClearLastKnownPosition();
            EvaluateNextAction(true);
        }

        private void HandleMeleeCompleted() => EvaluateNextAction(true);
        private void HandleStunCompleted() => EvaluateNextAction(true);

#if UNITY_EDITOR
        protected virtual void OnDrawGizmosSelected()
        {
            patrol?.DrawGizmos(transform);

            Gizmos.color = new Color(1f, 0.5f, 0f, 0.2f);
            Gizmos.DrawWireSphere(transform.position, pursuitRadius);
        }
#endif
    }
}
