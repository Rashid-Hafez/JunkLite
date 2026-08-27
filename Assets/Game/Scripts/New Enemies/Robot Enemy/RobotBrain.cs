using System;
using UnityEngine;

namespace junklite
{
    /// <summary>
    /// Decision policy for the Robot charge, dash, optional grab, and recovery loop.
    /// The nested runtime capability owns hitbox damage and grab mechanics.
    /// </summary>
    public sealed class RobotBrain : EnemyBrain, IEnemyCapabilityProvider
    {
        [SerializeField, HideInInspector] private bool ownsSerializedConfiguration;

        [Header("Robot - Patrol")]
        [SerializeField] private PatrolBehavior patrol = new();

        [Header("Robot - Charge")]
        [SerializeField] private ChargeBehavior charge = new();

        [Header("Robot - Dash Attack")]
        [SerializeField] private DashBehavior dash = new();

        [Header("Robot - Grab Attack")]
        [SerializeField] private GrabBehavior grab = new();

        [Header("Robot - Recovery")]
        [SerializeField] private RecoveryBehavior recovery = new();

        private RobotDashGrabCapability dashGrabCapability;
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
            UninitializeCapabilities();
            base.OnDisable();
        }

        protected override void InitializeStateMachine()
        {
            StateMachine.RegisterStates(
                new PatrolState(Actor),
                new IdleState(Actor),
                new ChargeState(Actor),
                new DashState(Actor),
                new GrabState(Actor),
                new RecoverState(Actor),
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
                bool wasInCombat = Actor.IsInCombat;
                Actor.EnterCombat();

                if (!wasInCombat && !IsDecisionLocked())
                    ChangeState<ChargeState>();

                return;
            }

            if (!Actor.IsInCombat && !IsDecisionLocked())
                ReturnToPassive();
        }

        protected override void EvaluateNextAction(bool actionCompleted = false)
        {
            if (!Actor.IsAlive || StateMachine.CurrentState is DeadState)
                return;
            if (!actionCompleted && IsDecisionLocked())
                return;

            if (Actor.HasTarget)
                ChangeState<ChargeState>();
            else
                ReturnToPassive();
        }

        public bool TryGetCapability<T>(out T capability) where T : class
        {
            if (patrol is T patrolCapability)
                capability = patrolCapability;
            else if (charge is T chargeCapability)
                capability = chargeCapability;
            else if (dashGrabCapability is T dashGrab)
                capability = dashGrab;
            else if (recovery is T recoveryCapability)
                capability = recoveryCapability;
            else if (interruptRecovery is T noHitstun)
                capability = noHitstun;
            else
                capability = null;

            return capability != null;
        }

        /// <summary>Runtime bridge for older Robot prefab and scene data.</summary>
        public void ApplyLegacyConfiguration(
            PatrolBehavior patrolBehavior,
            ChargeBehavior chargeBehavior,
            DashBehavior dashBehavior,
            GrabBehavior grabBehavior,
            RecoveryBehavior recoveryBehavior)
        {
            if (ownsSerializedConfiguration)
                return;

            UninitializeCapabilities();
            patrol = patrolBehavior ?? new PatrolBehavior();
            charge = chargeBehavior ?? new ChargeBehavior();
            dash = dashBehavior ?? new DashBehavior();
            grab = grabBehavior ?? new GrabBehavior();
            recovery = recoveryBehavior ?? new RecoveryBehavior();
            ownsSerializedConfiguration = true;
            InitializeCapabilities();
        }

        private bool IsDecisionLocked()
        {
            IState current = StateMachine.CurrentState;
            return IsForcedState()
                || current is ChargeState
                || current is DashState
                || current is GrabState
                || current is RecoverState;
        }

        private void ReturnToPassive()
        {
            if (returningToPassive)
                return;

            returningToPassive = true;
            Actor.ExitCombat();

            if (patrol.HasPatrol)
                ChangeState<PatrolState>();
            else
                ChangeState<IdleState>();

            returningToPassive = false;
        }

        private void EnsureBehaviors()
        {
            patrol ??= new PatrolBehavior();
            charge ??= new ChargeBehavior();
            dash ??= new DashBehavior();
            grab ??= new GrabBehavior();
            recovery ??= new RecoveryBehavior();
            dashGrabCapability ??= new RobotDashGrabCapability();
            interruptRecovery ??= new NoHitstunBehavior();
        }

        private void InitializeCapabilities()
        {
            EnsureBehaviors();
            UninitializeCapabilities();

            patrol.Initialize(transform);
            dashGrabCapability.Initialize(gameObject, Movement, dash, grab);

            charge.Completed += HandleChargeCompleted;
            dashGrabCapability.DashCompleted += HandleDashCompleted;
            dashGrabCapability.GrabStarted += HandleGrabStarted;
            dashGrabCapability.GrabCompleted += HandleGrabCompleted;
            recovery.Completed += HandleRecoveryCompleted;
            interruptRecovery.Completed += HandleForcedInterruptCompleted;
        }

        private void UninitializeCapabilities()
        {
            if (charge != null)
                charge.Completed -= HandleChargeCompleted;
            if (dashGrabCapability != null)
            {
                dashGrabCapability.DashCompleted -= HandleDashCompleted;
                dashGrabCapability.GrabStarted -= HandleGrabStarted;
                dashGrabCapability.GrabCompleted -= HandleGrabCompleted;
                dashGrabCapability.Dispose();
            }
            if (recovery != null)
                recovery.Completed -= HandleRecoveryCompleted;
            if (interruptRecovery != null)
                interruptRecovery.Completed -= HandleForcedInterruptCompleted;
        }

        private void HandleChargeCompleted()
        {
            if (Actor.IsAlive)
                ChangeState<DashState>();
        }

        private void HandleDashCompleted()
        {
            if (Actor.IsAlive)
                ChangeState<RecoverState>();
        }

        private void HandleGrabStarted()
        {
            if (Actor.IsAlive)
                ChangeState<GrabState>();
        }

        private void HandleGrabCompleted()
        {
            if (Actor.IsAlive)
                ChangeState<RecoverState>();
        }

        private void HandleRecoveryCompleted() => EvaluateNextAction(true);
        private void HandleForcedInterruptCompleted() => EvaluateNextAction(true);

#if UNITY_EDITOR
        private void OnDrawGizmosSelected() => patrol?.DrawGizmos(transform);
#endif
    }

    /// <summary>
    /// Robot-specific composition of the reusable dash and grab tuning. It owns
    /// contact damage and the optional transition into a successful grab.
    /// </summary>
    internal sealed class RobotDashGrabCapability : IDasher, IGrabber
    {
        private GameObject owner;
        private EnemyMovement movement;
        private DashBehavior dash;
        private GrabBehavior grab;
        private Hitbox subscribedHitbox;

        public event Action DashCompleted;
        public event Action GrabStarted;
        public event Action GrabCompleted;

        public float DashSpeed => dash.DashSpeed;
        public float DashDamage => dash.DashDamage;
        public Vector2 DashKnockback => dash.DashKnockback;
        public Hitbox DashHitbox => dash.DashHitbox;
        public float DashStopDistance => dash.DashStopDistance;
        public GameObject DashVFXPrefab => dash.DashVFXPrefab;
        public bool DashCanBeInterrupted => dash.DashCanBeInterrupted;
        public float DashAttackStartNormalized => dash.DashAttackStartNormalized;
        public float DashAttackActiveDuration => dash.DashAttackActiveDuration;
        public float DashWhiffResolveDelay => dash.DashWhiffResolveDelay;

        public bool CanGrab => grab.CanGrab;
        public float GrabChance => grab.GrabChance;
        public float GrabDuration => grab.GrabDuration;
        public Vector3 GrabOffset => grab.GrabOffset;
        public Vector2 ThrowForce => grab.ThrowForce;
        public float ThrowDamage => grab.ThrowDamage;
        public GameObject GrabVFXPrefab => grab.GrabVFXPrefab;

        public void Initialize(
            GameObject damageOwner,
            EnemyMovement enemyMovement,
            DashBehavior dashBehavior,
            GrabBehavior grabBehavior)
        {
            Dispose();
            owner = damageOwner;
            movement = enemyMovement;
            dash = dashBehavior;
            grab = grabBehavior;
            subscribedHitbox = dash?.DashHitbox;

            if (subscribedHitbox != null)
            {
                subscribedHitbox.OnHit += HandleHit;
                subscribedHitbox.Deactivate();
            }
        }

        public void Dispose()
        {
            if (subscribedHitbox != null)
                subscribedHitbox.OnHit -= HandleHit;

            subscribedHitbox = null;
            owner = null;
            movement = null;
            dash = null;
            grab = null;
        }

        public void OnDashComplete() => DashCompleted?.Invoke();
        public void OnGrabComplete() => GrabCompleted?.Invoke();

        private void HandleHit(Collider other, Hitbox sourceHitbox)
        {
            sourceHitbox?.Deactivate();
            if (owner == null || !DamageReceiverUtility.IsAlive(other))
                return;

            int throwDirection = movement != null ? movement.FacingDirection : 1;
            if (grab != null && grab.RollForGrab())
            {
                IGrabbable grabbable = other.GetComponent<IGrabbable>()
                    ?? other.GetComponentInParent<IGrabbable>();

                if (grabbable != null && grabbable.CanBeGrabbed)
                {
                    DamageReceiverUtility.Receive(other, new DamageRequest(
                        dash.DashDamage,
                        owner,
                        DamageType.Physical));

                    grabbable.GetGrabbed(new GrabInfo(
                        owner,
                        grab.GrabDuration,
                        grab.GrabOffset,
                        grab.ThrowForce,
                        grab.ThrowDamage,
                        throwDirection));

                    GrabStarted?.Invoke();
                    return;
                }
            }

            DamageReceiverUtility.Receive(other, new DamageRequest(
                dash.DashDamage,
                owner,
                DamageType.Physical,
                dash.DashKnockback));
        }
    }

}
