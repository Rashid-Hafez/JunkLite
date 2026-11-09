using UnityEngine;

namespace junklite
{
    /// <summary>
    /// Decision owner for a composed enemy. Perception reports facts, states execute
    /// actions, and the brain owns voluntary transitions between those actions.
    /// Ledges are handled here for every enemy: EnemyMovement brakes a step off a drop and
    /// reports it; while chasing, a crossable gap is jumped (GapJumpState) and an uncrossable
    /// one makes the enemy give the target up after a short stand at the edge, so it goes
    /// back to patrol or idle through its normal lost-target path.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(EnemyCharacter))]
    [RequireComponent(typeof(StateMachine))]
    [RequireComponent(typeof(EnemyMovement))]
    public abstract class EnemyBrain : MonoBehaviour, IGapJumper
    {
        [Header("Ledges")]
        [Tooltip("Jump a gap in the floor while chasing when EnemyMovement finds a floor within its Max Gap Width.")]
        [SerializeField] private bool jumpGaps = true;
        [Tooltip("Stand at an uncrossable edge this long before giving the target up (back to patrol / idle).")]
        [SerializeField, Min(0f)] private float ledgeGiveUpDelay = 0.8f;
        [Tooltip("After giving up at an edge the sensor ignores the player this long, so the chase does not restart on the spot.")]
        [SerializeField, Min(0f)] private float ledgeIgnoreTargetSeconds = 3f;

        protected EnemyCharacter Actor { get; private set; }
        protected StateMachine StateMachine { get; private set; }
        protected EnemyMovement Movement { get; private set; }
        protected EnemyPerception Perception { get; private set; }

        private bool initialized;
        private bool perceptionSubscribed;
        private bool movementSubscribed;
        private const float LedgeReprobeInterval = 0.25f;

        private Vector3 gapLanding;
        private Vector3 gapJumpDirection;
        private float ledgeGiveUpAt = -1f;
        private float nextLedgeProbeAt;
        // True from the decision to jump until GapJumpState reports back: voluntary
        // transitions are refused meanwhile, so nothing cuts the arc in mid-air.
        private bool gapJumpInProgress;

        public Vector3 GapLanding => gapLanding;
        public float GapJumpSpeed => Movement != null ? Movement.GapJumpSpeed : 9f;
        public float GapArcHeight(float distance) => Movement != null ? Movement.GapArcHeight(distance) : 1.2f;

        protected virtual void Awake()
        {
            Actor = GetComponent<EnemyCharacter>();
            StateMachine = GetComponent<StateMachine>();
            Movement = GetComponent<EnemyMovement>();
            Perception = Actor != null ? Actor.Perception : null;
            if (Perception == null)
                Perception = GetComponentInChildren<EnemyPerception>(true);
        }

        protected virtual void OnEnable()
        {
            SubscribeToPerception();
            SubscribeToMovement();
        }

        protected virtual void Start()
        {
            SubscribeToPerception();
            InitializeStateMachine();
            RegisterSharedReactionStates();
            initialized = true;

            if (Perception != null && Perception.HasTarget)
                OnTargetChanged(null, Perception.CurrentTarget);
        }

        protected virtual void Update()
        {
            if (!initialized || Actor == null || !Actor.IsAlive || Actor.IsTutorialFrozen)
                return;
            if (StateMachine == null || StateMachine.IsPaused)
                return;

            UpdateLedge();
            TickBrain();
        }

        protected virtual void OnDisable()
        {
            UnsubscribeFromPerception();
            UnsubscribeFromMovement();
            ledgeGiveUpAt = -1f;
            gapJumpInProgress = false;
        }

        protected abstract void InitializeStateMachine();

        /// <summary>
        /// Hit-reaction states every enemy can use, so a reaction is switched on per
        /// enemy in the Inspector (EnemyCharacter: Can Be Launched) instead of in code.
        /// </summary>
        private void RegisterSharedReactionStates()
        {
            if (StateMachine == null || Actor == null)
                return;

            if (!StateMachine.HasState<LaunchedState>())
                StateMachine.RegisterState(new LaunchedState(Actor));
            if (!StateMachine.HasState<GapJumpState>())
                StateMachine.RegisterState(new GapJumpState(Actor));
        }
        protected abstract void EvaluateNextAction(bool actionCompleted = false);

        protected virtual void TickBrain() { }

        protected virtual void OnTargetChanged(PlayerCharacter previous, PlayerCharacter current)
        {
            EvaluateNextAction();
        }

        protected void ChangeState<T>() where T : IState
        {
            if (StateMachine == null || StateMachine.IsInState<T>())
                return;
            // A voluntary decision never cuts a gap jump in mid-air (death still does).
            if (gapJumpInProgress && typeof(T) != typeof(DeadState))
                return;

            StateMachine.ChangeState<T>();
        }

        protected void RestartState<T>() where T : IState
        {
            if (gapJumpInProgress && typeof(T) != typeof(DeadState))
                return;

            StateMachine?.RestartState<T>();
        }

        protected bool IsForcedState()
        {
            IState current = StateMachine != null ? StateMachine.CurrentState : null;
            return current is DeadState || current is StunnedState || current is ParriedState
                || current is LaunchedState || gapJumpInProgress;
        }

        // ------------------------------------------------------------
        // Ledges
        // ------------------------------------------------------------

        private void SubscribeToMovement()
        {
            if (movementSubscribed || Movement == null)
                return;

            Movement.LedgeReached += HandleLedgeReached;
            movementSubscribed = true;
        }

        private void UnsubscribeFromMovement()
        {
            if (!movementSubscribed || Movement == null)
                return;

            Movement.LedgeReached -= HandleLedgeReached;
            movementSubscribed = false;
        }

        /// <summary>
        /// EnemyMovement stopped a step at a drop (from FixedUpdate). Patrol turns round. A chase
        /// jumps a crossable gap, waits if an ally stands on the landing, and otherwise starts the
        /// give-up timer. Every other state just stays braked at the edge.
        /// </summary>
        private void HandleLedgeReached(GapProbe probe)
        {
            if (!initialized || Actor == null || !Actor.IsAlive || Actor.IsTutorialFrozen)
                return;
            if (StateMachine == null || StateMachine.IsPaused || IsForcedState())
                return;

            IState current = StateMachine.CurrentState;
            if (current is PatrolState)
            {
                // The patrol's own ledge check runs a step behind the guard; turn it now.
                Actor.GetCapability<IPatroller>()?.ReverseDirection();
                RestartState<PatrolState>();
                return;
            }

            if (!(current is ChaseState))
                return;

            if (TryJumpGap(probe))
                return;

            nextLedgeProbeAt = Time.time + LedgeReprobeInterval;

            // An ally on the landing: wait for it to move, re-probing meanwhile.
            if (probe.LandingOccupied)
                return;

            if (ledgeGiveUpAt < 0f)
                ledgeGiveUpAt = Time.time + ledgeGiveUpDelay;
        }

        /// <summary>
        /// A chase has somewhere to go past the edge: the player, or the spot where it last saw
        /// them. The player usually leaves the sensor while jumping, so most gap jumps follow a
        /// last known position; the sensor re-acquires on the far side.
        /// </summary>
        private bool ChaseHasGoal()
        {
            if (Actor.HasTarget)
                return true;

            IChaser chaser = Actor.GetCapability<IChaser>();
            return chaser != null && chaser.HasLastKnownPosition;
        }

        private bool TryJumpGap(GapProbe probe)
        {
            if (!probe.CanCross || !jumpGaps || !ChaseHasGoal() || !StateMachine.HasState<GapJumpState>())
                return false;

            gapLanding = probe.Landing;
            gapJumpDirection = probe.Direction;
            ledgeGiveUpAt = -1f;
            ChangeState<GapJumpState>();
            gapJumpInProgress = StateMachine.CurrentState is GapJumpState;
            return gapJumpInProgress;
        }

        /// <summary>
        /// While a chase stands braked at a ledge: re-probe the gap every so often (an ally may have
        /// left the landing, the body may have settled) and jump when it becomes crossable;
        /// otherwise give up once the delay has run out.
        /// </summary>
        private void UpdateLedge()
        {
            if (Movement == null || !Movement.LedgeAhead || gapJumpInProgress
                || StateMachine == null || !(StateMachine.CurrentState is ChaseState))
            {
                ledgeGiveUpAt = -1f;
                return;
            }

            if (Time.time >= nextLedgeProbeAt)
            {
                nextLedgeProbeAt = Time.time + LedgeReprobeInterval;
                if (!IsForcedState() && !StateMachine.IsPaused && Actor != null && Actor.IsAlive
                    && TryJumpGap(Movement.ProbeGap(Movement.LedgeDirection)))
                    return;
            }

            if (ledgeGiveUpAt < 0f || Time.time < ledgeGiveUpAt)
                return;

            ledgeGiveUpAt = -1f;
            OnLedgeBlocked();
        }

        /// <summary>
        /// The chase cannot continue past the edge. Forgets the target (and keeps the sensor from
        /// re-acquiring it for a moment), which runs the brain's normal lost-target path: search,
        /// patrol or idle. Brains that remember a last known position must clear it too.
        /// </summary>
        protected virtual void OnLedgeBlocked()
        {
            if (Perception != null && Perception.HasTarget)
                Perception.ForgetTarget(ledgeIgnoreTargetSeconds);
            else
                EvaluateNextAction(true);
        }

        public void OnGapJumpFinished(bool landed)
        {
            gapJumpInProgress = false;
            if (Actor == null || !Actor.IsAlive || StateMachine == null)
                return;

            // Landed: the state is still GapJumpState until this decision leaves it.
            // Cut short (death, forced state): the new state already owns the body.
            if (!landed)
            {
                Actor.DiscardDeferredAirReactions();
                return;
            }

            if (!Actor.HasTarget)
                DropLastKnownPositionBehind();

            // A hit or a status stun that landed over the gap staggers now; StunnedState's
            // end hands the decision back.
            if (Actor.ApplyDeferredAirReactions())
                return;

            EvaluateNextAction(true);
        }

        /// <summary>
        /// After a jump toward a last known position that lay over the gap, the enemy is now past
        /// it: count it as reached instead of walking back to the edge for it.
        /// </summary>
        private void DropLastKnownPositionBehind()
        {
            IChaser chaser = Actor.GetCapability<IChaser>();
            if (chaser == null || !chaser.HasLastKnownPosition)
                return;

            Vector3 origin = Movement != null ? Movement.GetFeetPosition() : transform.position;
            if (Vector3.Dot(chaser.LastKnownTargetPosition - origin, gapJumpDirection) <= 0.5f)
                chaser.ClearLastKnownPosition();
        }

        private void SubscribeToPerception()
        {
            if (perceptionSubscribed || Perception == null)
                return;

            Perception.TargetChanged += HandleTargetChanged;
            perceptionSubscribed = true;
        }

        private void UnsubscribeFromPerception()
        {
            if (!perceptionSubscribed || Perception == null)
                return;

            Perception.TargetChanged -= HandleTargetChanged;
            perceptionSubscribed = false;
        }

        private void HandleTargetChanged(PlayerCharacter previous, PlayerCharacter current)
        {
            if (!isActiveAndEnabled || !initialized || Actor == null
                || !Actor.isActiveAndEnabled || !Actor.IsAlive
                || Perception == null || !Perception.isActiveAndEnabled)
                return;

            OnTargetChanged(previous, current);
        }
    }
}
