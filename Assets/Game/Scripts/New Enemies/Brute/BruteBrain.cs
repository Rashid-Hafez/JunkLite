using UnityEngine;

namespace junklite
{
    /// <summary>
    /// Brute boss decisions. Close range: telegraphed unparryable grab. Otherwise a
    /// weighted roll between Taunt+Attack (melee wind-up is the taunt) and
    /// Charge+Dash (armored charge, parryable dash). Phase two speeds everything up.
    /// </summary>
    public sealed class BruteBrain : EnemyBrain, IEnemyCapabilityProvider
    {
        private enum Move { None, TauntAttack, ChargeDash }

        [Header("Brute - Move Choice")]
        [SerializeField] private BruteMoveWeight tauntAttack = new() { weight = 2f, cooldown = 2.5f };
        [SerializeField] private BruteMoveWeight chargeDash = new() { weight = 1f, cooldown = 6f };
        [Tooltip("Charge+Dash is only picked when the player is at least this far away (axis distance).")]
        [SerializeField] private float chargeDashMinDistance = 3.5f;
        [Tooltip("Pause between moves when nothing is ready.")]
        [SerializeField] private float idleBetweenMoves = 0.4f;

        [Header("Brute - Chase")]
        [SerializeField] private ChaseBehavior chase = new();
        [Tooltip("Detection radius once the fight starts, so the boss doesn't lose the player mid-fight.")]
        [SerializeField] private float pursuitRadius = 25f;

        [Header("Brute - Taunt + Attack")]
        [Tooltip("Wind-up plays the taunt (Point); attack plays Attack Melee.")]
        [SerializeField] private MeleeAttackBehavior melee = new();
        [SerializeField] private float lungeSpeed = 4f;
        [SerializeField] private float lungeDuration = 0.25f;

        [Header("Brute - Charge + Dash")]
        [SerializeField] private ChargeBehavior charge = new();
        [SerializeField] private DashBehavior dash = new();

        [Header("Brute - Grab")]
        [SerializeField] private BruteGrabBehavior grab = new();

        [Header("Brute - Recovery / Stun")]
        [SerializeField] private RecoveryBehavior recovery = new();
        [Tooltip("Stagger 0 = ordinary hits never interrupt him. Parry stun uses ForcedStunDuration.")]
        [SerializeField] private StunBehavior stun = new();

        [Header("Brute - Phase 2")]
        [SerializeField] private BrutePhaseSettings phaseTwo = BrutePhaseSettings.Default;

        private BruteScaledCapabilities scaled;
        private Move pendingMove;
        private float nextDecisionTime;
        private bool phaseTwoActive;

        public bool IsPhaseTwo => phaseTwoActive;

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
                new IdleState(Actor),
                new ChaseState(Actor),
                new MeleeAttackState(Actor),
                new ChargeState(Actor),
                new DashState(Actor),
                new BruteGrabState(Actor),
                new RecoverState(Actor),
                new StunnedState(Actor),
                new ParriedState(Actor),
                new DeadState(Actor));

            StateMachine.SetInitialState<IdleState>();
        }

        protected override void OnTargetChanged(PlayerCharacter previous, PlayerCharacter current)
        {
            if (current != null)
            {
                Actor.EnterCombat();
                Perception?.SetRadius(pursuitRadius);
                chase.UpdateLastKnownPosition(current.transform.position);
            }

            if (!IsDecisionLocked())
                EvaluateNextAction();
        }

        protected override void TickBrain()
        {
            UpdatePhase();

            if (!Actor.HasTarget || IsDecisionLocked())
                return;

            chase.UpdateLastKnownPosition(Actor.Target.position);

            // Grab interrupts idling/chasing whenever the player gets close.
            if (CanGrabNow())
            {
                StartGrab();
                return;
            }

            if (StateMachine.CurrentState is ChaseState && pendingMove == Move.TauntAttack && IsInMeleeRange())
            {
                StartTauntAttack();
                return;
            }

            if (StateMachine.CurrentState is IdleState && Time.time >= nextDecisionTime)
                EvaluateNextAction();
        }

        protected override void EvaluateNextAction(bool actionCompleted = false)
        {
            if (!Actor.IsAlive || StateMachine.CurrentState is DeadState)
                return;
            if (!actionCompleted && IsDecisionLocked())
                return;

            if (!Actor.HasTarget)
            {
                pendingMove = Move.None;
                ChangeState<IdleState>();
                return;
            }

            if (CanGrabNow())
            {
                StartGrab();
                return;
            }

            if (pendingMove == Move.None)
                pendingMove = RollMove();

            switch (pendingMove)
            {
                case Move.ChargeDash:
                    pendingMove = Move.None;
                    chargeDash.StartCooldown(CooldownMultiplier);
                    ChangeState<ChargeState>();
                    break;

                case Move.TauntAttack:
                    if (IsInMeleeRange())
                        StartTauntAttack();
                    else
                        ChangeState<ChaseState>();
                    break;

                default:
                    nextDecisionTime = Time.time + idleBetweenMoves;
                    ChangeState<IdleState>();
                    break;
            }
        }

        public bool TryGetCapability<T>(out T capability) where T : class
        {
            if (scaled is T scaledCapability)
                capability = scaledCapability;
            else if (chase is T chaseCapability)
                capability = chaseCapability;
            else if (grab is T grabCapability)
                capability = grabCapability;
            else if (recovery is T recoveryCapability)
                capability = recoveryCapability;
            else if (stun is T stunCapability)
                capability = stunCapability;
            else
                capability = null;

            return capability != null;
        }

        // =============================================================
        // MOVE CHOICE
        // =============================================================

        private Move RollMove()
        {
            float distance = AxisDistanceToTarget();
            float tauntWeight = tauntAttack.IsReady ? tauntAttack.weight : 0f;
            float dashWeight = chargeDash.IsReady && distance >= chargeDashMinDistance ? chargeDash.weight : 0f;

            float total = tauntWeight + dashWeight;
            if (total <= 0f)
                return Move.None;

            return Random.value * total < tauntWeight ? Move.TauntAttack : Move.ChargeDash;
        }

        private void StartTauntAttack()
        {
            pendingMove = Move.None;
            tauntAttack.StartCooldown(CooldownMultiplier);
            ChangeState<MeleeAttackState>();
        }

        private void StartGrab()
        {
            pendingMove = Move.None;
            grab.StartCooldown(CooldownMultiplier);
            ChangeState<BruteGrabState>();
        }

        private bool CanGrabNow() =>
            Actor.HasTarget && grab.IsReady && AxisDistanceToTarget() <= grab.GrabRange;

        private bool IsInMeleeRange()
        {
            float stopDistance = chase.ChaseStopDistance > 0f ? chase.ChaseStopDistance : Actor.AttackRange;
            return AxisDistanceToTarget() <= stopDistance;
        }

        private float AxisDistanceToTarget() =>
            Actor.HasTarget ? Movement.GetAbsAxisDistance(transform.position, Actor.Target.position) : float.MaxValue;

        private bool IsDecisionLocked()
        {
            IState current = StateMachine.CurrentState;
            return IsForcedState()
                || current is MeleeAttackState
                || current is ChargeState
                || current is DashState
                || current is BruteGrabState
                || current is RecoverState;
        }

        // =============================================================
        // PHASE TWO
        // =============================================================

        private float CooldownMultiplier => phaseTwoActive ? phaseTwo.cooldownMultiplier : 1f;

        private void UpdatePhase()
        {
            if (phaseTwoActive || Actor.Health == null)
                return;

            if (Actor.Health.Percentage <= phaseTwo.healthThreshold)
            {
                phaseTwoActive = true;
                scaled.SetPhaseTwo(phaseTwo);
            }
        }

        // =============================================================
        // CAPABILITY WIRING
        // =============================================================

        private void EnsureBehaviors()
        {
            chase ??= new ChaseBehavior();
            melee ??= new MeleeAttackBehavior();
            charge ??= new ChargeBehavior();
            dash ??= new DashBehavior();
            grab ??= new BruteGrabBehavior();
            recovery ??= new RecoveryBehavior();
            stun ??= new StunBehavior();
            tauntAttack ??= new BruteMoveWeight();
            chargeDash ??= new BruteMoveWeight();
            scaled ??= new BruteScaledCapabilities(charge, dash, melee, () => lungeSpeed, () => lungeDuration);
        }

        private void InitializeCapabilities()
        {
            EnsureBehaviors();
            UninitializeCapabilities();

            melee.Initialize(gameObject);
            dash.Initialize(gameObject);
            grab.Initialize(gameObject);

            chase.ReachedTarget += HandleReachedTarget;
            melee.Completed += HandleActionCompleted;
            charge.Completed += HandleChargeCompleted;
            dash.Completed += HandleDashCompleted;
            grab.Completed += HandleGrabCompleted;
            recovery.Completed += HandleActionCompleted;
            stun.Completed += HandleStunCompleted;
        }

        private void UninitializeCapabilities()
        {
            if (chase != null) chase.ReachedTarget -= HandleReachedTarget;
            if (melee != null) { melee.Completed -= HandleActionCompleted; melee.Dispose(); }
            if (charge != null) charge.Completed -= HandleChargeCompleted;
            if (dash != null) { dash.Completed -= HandleDashCompleted; dash.Dispose(); }
            if (grab != null) { grab.Completed -= HandleGrabCompleted; grab.Dispose(); }
            if (recovery != null) recovery.Completed -= HandleActionCompleted;
            if (stun != null) stun.Completed -= HandleStunCompleted;
        }

        private void HandleReachedTarget() => EvaluateNextAction(true);
        private void HandleActionCompleted() => EvaluateNextAction(true);

        private void HandleChargeCompleted()
        {
            if (Actor.IsAlive)
            {
                dash.ResetHitResult();
                ChangeState<DashState>();
            }
        }

        private void HandleDashCompleted()
        {
            if (Actor.IsAlive)
                ChangeState<RecoverState>();
        }

        private void HandleGrabCompleted() => EvaluateNextAction(true);

        // Stun and parry both end in the StandUp recovery.
        private void HandleStunCompleted()
        {
            if (Actor.IsAlive)
                ChangeState<RecoverState>();
        }

        /// <summary>
        /// Exposes charge, dash, melee, and lunge through phase-aware wrappers so
        /// phase two can scale timings without mutating the serialized tuning.
        /// </summary>
        private sealed class BruteScaledCapabilities : ICharger, IDasher, IMeleeAttacker, IMeleeLunge
        {
            private readonly ChargeBehavior charge;
            private readonly DashBehavior dash;
            private readonly MeleeAttackBehavior melee;
            private readonly System.Func<float> lungeSpeed;
            private readonly System.Func<float> lungeDuration;
            private float chargeTimeMultiplier = 1f;
            private float tauntMultiplier = 1f;
            private float dashSpeedMultiplier = 1f;

            public BruteScaledCapabilities(
                ChargeBehavior charge,
                DashBehavior dash,
                MeleeAttackBehavior melee,
                System.Func<float> lungeSpeed,
                System.Func<float> lungeDuration)
            {
                this.charge = charge;
                this.dash = dash;
                this.melee = melee;
                this.lungeSpeed = lungeSpeed;
                this.lungeDuration = lungeDuration;
            }

            public void SetPhaseTwo(BrutePhaseSettings settings)
            {
                chargeTimeMultiplier = settings.chargeTimeMultiplier;
                tauntMultiplier = settings.tauntMultiplier;
                dashSpeedMultiplier = settings.dashSpeedMultiplier;
            }

            // ICharger
            public float ChargeTime => charge.ChargeTime * chargeTimeMultiplier;
            public GameObject ChargeVFXPrefab => charge.ChargeVFXPrefab;
            public void OnChargeComplete() => charge.OnChargeComplete();

            // IDasher
            public float DashSpeed => dash.DashSpeed * dashSpeedMultiplier;
            public float DashDamage => dash.DashDamage;
            public Vector2 DashKnockback => dash.DashKnockback;
            public Hitbox DashHitbox => dash.DashHitbox;
            public float DashStopDistance => dash.DashStopDistance;
            public GameObject DashVFXPrefab => dash.DashVFXPrefab;
            public bool DashCanBeInterrupted => dash.DashCanBeInterrupted;
            public float DashAttackStartNormalized => dash.DashAttackStartNormalized;
            public float DashAttackActiveDuration => dash.DashAttackActiveDuration;
            public float DashWhiffResolveDelay => dash.DashWhiffResolveDelay;
            public void OnDashComplete() => dash.OnDashComplete();

            // IMeleeAttacker (wind-up is the taunt)
            public float MeleeWindUpDuration => melee.MeleeWindUpDuration * tauntMultiplier;
            public float MeleeAttackDuration => melee.MeleeAttackDuration;
            public float MeleeHitStartNormalized => melee.MeleeHitStartNormalized;
            public float MeleeHitEndNormalized => melee.MeleeHitEndNormalized;
            public float MeleeAttackSpeed => melee.MeleeAttackSpeed;
            public float MeleeDamage => melee.MeleeDamage;
            public Vector2 MeleeKnockback => melee.MeleeKnockback;
            public Hitbox MeleeHitbox => melee.MeleeHitbox;
            public GameObject MeleeVFXPrefab => melee.MeleeVFXPrefab;
            public void OnMeleeComplete() => melee.OnMeleeComplete();

            // IMeleeLunge
            public float LungeSpeed => lungeSpeed();
            public float LungeDuration => lungeDuration();
        }
    }
}
