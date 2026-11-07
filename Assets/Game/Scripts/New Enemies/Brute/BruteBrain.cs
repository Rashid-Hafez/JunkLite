using UnityEngine;

namespace junklite
{
    /// <summary>
    /// Brute boss decisions. Touching his body while he is idle or walking grabs at
    /// once (the first moments of each wait after a move are safe; a brush-past is
    /// not a touch). Close range: telegraphed unparryable grab. Otherwise a weighted roll
    /// between Taunt+Attack (melee wind-up is the taunt) and Charge+Dash (armored
    /// charge whose aim locks part-way, then a parryable dash along that direction).
    /// After every move he waits, idle and facing the player, before the next one;
    /// each hit he takes cuts the wait short. A knockdown (BruteEnemy) ends with a
    /// stand-up and a roar that pushes the player away. Phase two speeds everything up.
    /// </summary>
    public sealed class BruteBrain : EnemyBrain, IEnemyCapabilityProvider
    {
        private enum Move { None, TauntAttack, ChargeDash, Grab }

        [Header("Brute - Move Choice")]
        [SerializeField] private BruteMoveWeight tauntAttack = new() { weight = 1.2f, cooldown = 3.5f };
        [SerializeField] private BruteMoveWeight chargeDash = new() { weight = 1.2f, cooldown = 5f };
        [Tooltip("Charge+Dash is only picked when the player is at least this far away (axis distance). Up close the charge ends in the blast, not the dash.")]
        [SerializeField] private float chargeDashMinDistance = 1.5f;
        [Tooltip("Grab joins the weighted roll only when the player is within grab range.")]
        [SerializeField, Min(0f)] private float grabWeight = 1.5f;
        [Tooltip("Never the same move twice in a row while another is ready.")]
        [SerializeField] private bool avoidRepeatingMove = true;
        [Tooltip("Pause between moves when nothing is ready.")]
        [SerializeField] private float idleBetweenMoves = 0.4f;

        [Header("Brute - Wait Between Moves")]
        [Tooltip("Idle, facing the player, after each move: the player's opening. Hits cut it short.")]
        [SerializeField] private BruteBreatherSettings breather = BruteBreatherSettings.Default;

        [Header("Brute - Chase")]
        [SerializeField] private ChaseBehavior chase = new();
        [Tooltip("Detection radius once the fight starts, so the boss doesn't lose the player mid-fight.")]
        [SerializeField] private float pursuitRadius = 25f;

        [Header("Brute - Taunt + Attack")]
        [Tooltip("Wind-up plays the taunt (Point); attack plays Attack Melee.")]
        [SerializeField] private MeleeAttackBehavior melee = new();
        [SerializeField] private float lungeSpeed = 4f;
        [SerializeField] private float lungeDuration = 0.25f;
        [Tooltip("No lunge when the player is already within this axis distance (on top of him).")]
        [SerializeField, Min(0f)] private float lungeStopDistance = 1.5f;

        [Header("Brute - Charge + Dash")]
        [SerializeField] private ChargeBehavior charge = new();
        [SerializeField] private DashBehavior dash = new();
        [Tooltip("Point in the charge (0-1) where he stops turning toward the player. The dash then goes that way wherever she moves. 0 = locks when the charge starts.")]
        [SerializeField, Range(0f, 1f)] private float aimLockNormalized = 0.5f;
        [Tooltip("How far the dash runs along the locked direction (it stops early at a wall).")]
        [SerializeField, Min(0f)] private float lockedDashDistance = 12f;

        [Header("Brute - Close Blast")]
        [Tooltip("Player this close (axis distance) when the charge ends: energy blast instead of the dash.")]
        [SerializeField] private float blastRange = 2.5f;
        [SerializeField] private float blastDamage = 30f;
        [Tooltip("Launch applied to the player: X away from the boss, Y up.")]
        [SerializeField] private Vector2 blastForce = new Vector2(14f, 9f);
        [SerializeField] private GameObject blastVfxPrefab;
        [Tooltip("Child burst on the boss: expanding shield bubble, ring and sparks.")]
        [SerializeField] private ParticleSystem blastShieldBurst;
        [SerializeField] private float blastVfxLifetime = 2f;
        [SerializeField] private float blastShakeForce = 1.5f;

        [Header("Brute - Grab")]
        [SerializeField] private BruteGrabBehavior grab = new();
        [Tooltip("Hits taken during one wait that trigger a grab at once (she is in his face). 0 = off.")]
        [SerializeField, Min(0)] private int grabAfterHits = 3;
        [Tooltip("Seconds between those punish grabs.")]
        [SerializeField, Min(0f)] private float punishGrabCooldown = 6f;

        [Header("Brute - Recovery / Stun")]
        [SerializeField] private RecoveryBehavior recovery = new();
        [Tooltip("Stagger 0 = ordinary hits never interrupt him. Parry stun uses ForcedStunDuration.")]
        [SerializeField] private StunBehavior stun = new();

        [Header("Brute - Get-Up Roar (after a knockdown)")]
        [Tooltip("Pushes the player away (no damage) when she is this close (axis distance), so a knockdown always ends cleanly.")]
        [SerializeField, Min(0f)] private float roarRadius = 4f;
        [Tooltip("x = lane speed away from him, y = minimum upward speed.")]
        [SerializeField] private Vector2 roarPush = new(12f, 4f);
        [Tooltip("Seconds the player loses control so the push can't be cancelled.")]
        [SerializeField, Min(0f)] private float roarLockSeconds = 0.3f;
        [SerializeField] private float roarShakeForce = 1f;
        [Tooltip("Optional burst played on the roar.")]
        [SerializeField] private ParticleSystem roarBurst;

        [Header("Brute - Phase 2")]
        [SerializeField] private BrutePhaseSettings phaseTwo = BrutePhaseSettings.Default;

        private BruteScaledCapabilities scaled;
        private Move pendingMove;
        private Move lastMove;
        private float nextDecisionTime;
        private bool phaseTwoActive;
        private float nextPerceptionRefresh;
        private Collider bodyCollider;
        private Collider targetBody;
        private BruteEnemy brute;
        private bool breatherActive;
        private float breatherStartTime;
        private int breatherHits;
        private float nextPunishGrabAllowed;
        private float touchOverlapSince = -1f;
        private bool roarPending;

        public bool IsPhaseTwo => phaseTwoActive;

        /// <summary>Idle in the wait after a move (the player's opening).</summary>
        private bool IsInBreather => breatherActive && StateMachine.CurrentState is IdleState;

        protected override void Awake()
        {
            base.Awake();
            brute = GetComponent<BruteEnemy>();
            bodyCollider = BodyContact.FindSolidCollider(this);
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
            targetBody = current != null ? current.GetComponent<Collider>() : null;

            if (current != null)
            {
                Actor.EnterCombat();
                Perception?.SetRadius(pursuitRadius);
                chase.UpdateLastKnownPosition(current.transform.position);
            }
            else
            {
                // Target lost (death, respawn): shrink back so re-entering the
                // detection zone re-acquires the player.
                Actor.ExitCombat();
                Perception?.ResetRadius();
                pendingMove = Move.None;
            }

            // Respect the wait between moves even if the target is re-acquired.
            if (!IsDecisionLocked() && Time.time >= nextDecisionTime)
                EvaluateNextAction();
        }

        protected override void TickBrain()
        {
            UpdatePhase();

            if (!Actor.HasTarget)
            {
                // The player may already be inside the sensor (released from a
                // grab, revived in place) where no trigger-enter will fire.
                if (Time.time >= nextPerceptionRefresh)
                {
                    nextPerceptionRefresh = Time.time + 0.25f;
                    Perception?.RefreshOverlap();
                }
                return;
            }

            if (IsDecisionLocked())
                return;

            chase.UpdateLastKnownPosition(Actor.Target.position);

            if (IsInBreather)
                Movement?.FaceTarget(Actor.Target.position);

            if (TryGrabOnTouch())
                return;

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

            breatherActive = false;

            if (!Actor.HasTarget)
            {
                pendingMove = Move.None;
                ChangeState<IdleState>();
                return;
            }

            if (pendingMove == Move.None)
                pendingMove = RollMove();

            switch (pendingMove)
            {
                case Move.ChargeDash:
                    pendingMove = Move.None;
                    lastMove = Move.ChargeDash;
                    chargeDash.StartCooldown(CooldownMultiplier);
                    ChangeState<ChargeState>();
                    break;

                case Move.Grab:
                    StartGrab();
                    break;

                case Move.TauntAttack:
                    if (IsInMeleeRange())
                        StartTauntAttack();
                    else
                        ChangeState<ChaseState>();
                    break;

                default:
                    // Nothing ready: close the distance instead of standing still.
                    if (!IsInMeleeRange())
                    {
                        ChangeState<ChaseState>();
                        break;
                    }

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

            float grabW = CanGrabNow() ? grabWeight : 0f;

            // Variety: drop the move he just did when anything else is on the table.
            if (avoidRepeatingMove)
            {
                float repeatWeight = lastMove == Move.TauntAttack ? tauntWeight
                    : lastMove == Move.ChargeDash ? dashWeight
                    : lastMove == Move.Grab ? grabW : 0f;
                if (tauntWeight + dashWeight + grabW - repeatWeight > 0f)
                {
                    if (lastMove == Move.TauntAttack) tauntWeight = 0f;
                    else if (lastMove == Move.ChargeDash) dashWeight = 0f;
                    else if (lastMove == Move.Grab) grabW = 0f;
                }
            }

            float total = tauntWeight + dashWeight + grabW;
            if (total <= 0f)
                return Move.None;

            float roll = Random.value * total;
            if (roll < tauntWeight) return Move.TauntAttack;
            return roll < tauntWeight + dashWeight ? Move.ChargeDash : Move.Grab;
        }

        private void StartTauntAttack()
        {
            pendingMove = Move.None;
            lastMove = Move.TauntAttack;
            tauntAttack.StartCooldown(CooldownMultiplier);
            ChangeState<MeleeAttackState>();
        }

        private void StartGrab()
        {
            pendingMove = Move.None;
            lastMove = Move.Grab;
            grab.StartCooldown(CooldownMultiplier);
            ChangeState<BruteGrabState>();
        }

        /// <summary>
        /// Idle or walking (the caller skips every locked state): a body kept against his
        /// for TouchHoldTime grabs her at once. The start of every wait after a move is
        /// safe (touchGrabDelay), so each opening is real. A dash or roll passes through;
        /// TryCatchOnTouch rejects it.
        /// </summary>
        private bool TryGrabOnTouch()
        {
            if (!grab.CanGrabOnTouch
                || (breatherActive && Time.time < breatherStartTime + breather.touchGrabDelay)
                || !BodyContact.IsOverlapping(bodyCollider, targetBody, grab.TouchMinOverlap))
            {
                touchOverlapSince = -1f;
                return false;
            }

            if (touchOverlapSince < 0f)
                touchOverlapSince = Time.time;
            if (Time.time - touchOverlapSince < grab.TouchHoldTime)
                return false;

            if (!grab.TryCatchOnTouch(targetBody))
                return false;

            touchOverlapSince = -1f;
            breatherActive = false;
            StartGrab();
            return true;
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
        private float BreatherMultiplier => phaseTwoActive ? phaseTwo.breatherMultiplier : 1f;
        private float BreatherCutPerHit => phaseTwoActive ? phaseTwo.breatherCutPerHit : breather.cutPerHit;

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
            scaled ??= new BruteScaledCapabilities(charge, dash, melee, () => lungeSpeed, () => lungeDuration,
                () => lungeStopDistance, () => aimLockNormalized, () => lockedDashDistance);
        }

        private void InitializeCapabilities()
        {
            EnsureBehaviors();
            UninitializeCapabilities();

            melee.Initialize(gameObject);
            dash.Initialize(gameObject);
            grab.Initialize(gameObject);

            chase.ReachedTarget += HandleReachedTarget;
            melee.Completed += HandlePunchCompleted;
            charge.Completed += HandleChargeCompleted;
            dash.Completed += HandleDashCompleted;
            grab.Completed += HandleGrabCompleted;
            recovery.Completed += HandleStandUpCompleted;
            stun.Completed += HandleStunCompleted;
            if (brute != null) brute.HitLanded += HandleHitLanded;
        }

        private void UninitializeCapabilities()
        {
            if (chase != null) chase.ReachedTarget -= HandleReachedTarget;
            if (melee != null) { melee.Completed -= HandlePunchCompleted; melee.Dispose(); }
            if (charge != null) charge.Completed -= HandleChargeCompleted;
            if (dash != null) { dash.Completed -= HandleDashCompleted; dash.Dispose(); }
            if (grab != null) { grab.Completed -= HandleGrabCompleted; grab.Dispose(); }
            if (recovery != null) recovery.Completed -= HandleStandUpCompleted;
            if (stun != null) stun.Completed -= HandleStunCompleted;
            if (brute != null) brute.HitLanded -= HandleHitLanded;
        }

        private void HandleReachedTarget() => EvaluateNextAction(true);
        private void HandlePunchCompleted() => StartBreather(breather.afterPunch);

        // =============================================================
        // WAIT BETWEEN MOVES
        // =============================================================

        /// <summary>Idle, facing the player, for the move's wait; TickBrain decides once it runs out.</summary>
        private void StartBreather(float seconds)
        {
            if (!Actor.IsAlive)
                return;

            pendingMove = Move.None;
            breatherActive = true;
            breatherStartTime = Time.time;
            breatherHits = 0;
            touchOverlapSince = -1f;
            nextDecisionTime = Time.time + seconds * BreatherMultiplier;
            ChangeState<IdleState>();
        }

        private void HandleHitLanded()
        {
            if (!IsInBreather)
                return;

            float remaining = BruteBreatherSettings.Shorten(nextDecisionTime - Time.time, BreatherCutPerHit, breather.minRemaining);
            nextDecisionTime = Time.time + remaining;

            breatherHits++;
            TryPunishGrab();
        }

        /// <summary>Enough hits in one wait with her inside grab range: the wait ends in a grab, cooldown or not.</summary>
        private void TryPunishGrab()
        {
            if (grabAfterHits <= 0 || breatherHits < grabAfterHits || Time.time < nextPunishGrabAllowed)
                return;
            if (!Actor.HasTarget || AxisDistanceToTarget() > grab.GrabRange)
                return;

            nextPunishGrabAllowed = Time.time + punishGrabCooldown;
            breatherActive = false;
            StartGrab();
        }

        private void HandleChargeCompleted()
        {
            if (!Actor.IsAlive)
                return;

            // Dash can't come out when the player is on top of him: blast instead.
            if (Actor.HasTarget && AxisDistanceToTarget() <= blastRange)
            {
                Blast();
                StartBreather(breather.afterBlast);
                return;
            }

            dash.ResetHitResult();
            ChangeState<DashState>();
        }

        private void Blast()
        {
            Vector3 center = transform.position + Vector3.up;
            if (blastVfxPrefab != null)
                VFXPool.SpawnTimed(blastVfxPrefab, center, Quaternion.identity, blastVfxLifetime);
            if (blastShieldBurst != null)
                blastShieldBurst.Play(true);
            FeedbackManager.Instance?.DoCameraShake(blastShakeForce);

            PlayerCharacter player = Actor.TargetCharacter;
            if (player == null)
                return;

            // Dash i-frames still dodge it; a parry can't catch an explosion.
            DamageResult result = DamageReceiverUtility.Receive(player,
                new DamageRequest(blastDamage, gameObject, DamageType.Electric)
                    .WithHitReaction(HitReactionRequest.None)
                    .AsUnparryable());
            if (result.WasApplied)
                player.KnockDown(transform.position, blastForce);
        }

        private void HandleDashCompleted()
        {
            // Still in DashState here: a run that ended against a wall leaves a longer opening.
            bool hitWall = StateMachine.CurrentState is DashState dashState && dashState.EndedAtWall;
            StartBreather(hitWall ? breather.afterDashIntoWall : breather.afterDash);
        }

        private void HandleGrabCompleted(bool caught)
        {
            if (grab.LastGrabEscaped)
            {
                // She broke free: he reels, then stands up into the usual wait.
                grab.LastGrabEscaped = false;
                if (Actor.IsAlive && grab.EscapeStunDuration > 0f)
                {
                    stun.ForcedStunDuration = grab.EscapeStunDuration;
                    ChangeState<StunnedState>();
                    return;
                }
            }

            StartBreather(caught ? breather.afterGrab : breather.afterMissedGrab);
        }

        // Stun, parry and knockdown all end in the StandUp recovery.
        private void HandleStunCompleted()
        {
            if (!Actor.IsAlive)
                return;

            // Read while still in StunnedState; leaving it clears the knockdown.
            roarPending = brute != null && brute.IsKnockedDown;
            ChangeState<RecoverState>();
        }

        private void HandleStandUpCompleted()
        {
            if (roarPending)
            {
                roarPending = false;
                Roar();
            }

            StartBreather(breather.afterGetUp);
        }

        /// <summary>Get-up roar: shoves a close player away (no damage) so a knockdown ends cleanly.</summary>
        private void Roar()
        {
            FeedbackManager.Instance?.DoCameraShake(roarShakeForce);
            if (roarBurst != null)
                roarBurst.Play(true);

            PlayerCharacter player = Actor.TargetCharacter;
            if (player == null || Movement == null)
                return;

            Vector3 axis = Movement.MovementAxis;
            float offset = Vector3.Dot(player.transform.position - transform.position, axis);
            if (Mathf.Abs(offset) > roarRadius)
                return;

            float side = Mathf.Abs(offset) > 0.05f ? Mathf.Sign(offset) : Movement.FacingDirection;
            player.ApplyBodyShove(axis * side, roarPush, roarLockSeconds, gameObject);
        }

        /// <summary>
        /// Exposes charge, dash, melee, and lunge through phase-aware wrappers so
        /// phase two can scale timings without mutating the serialized tuning.
        /// </summary>
        private sealed class BruteScaledCapabilities : ICharger, IDasher, ILockedDashAim, IMeleeAttacker, IMeleeLunge
        {
            private readonly ChargeBehavior charge;
            private readonly DashBehavior dash;
            private readonly MeleeAttackBehavior melee;
            private readonly System.Func<float> lungeSpeed;
            private readonly System.Func<float> lungeDuration;
            private readonly System.Func<float> lungeStopDistance;
            private readonly System.Func<float> aimLockNormalized;
            private readonly System.Func<float> lockedDashDistance;
            private float chargeTimeMultiplier = 1f;
            private float tauntMultiplier = 1f;
            private float minTauntDuration;
            private float dashSpeedMultiplier = 1f;

            public BruteScaledCapabilities(
                ChargeBehavior charge,
                DashBehavior dash,
                MeleeAttackBehavior melee,
                System.Func<float> lungeSpeed,
                System.Func<float> lungeDuration,
                System.Func<float> lungeStopDistance,
                System.Func<float> aimLockNormalized,
                System.Func<float> lockedDashDistance)
            {
                this.charge = charge;
                this.dash = dash;
                this.melee = melee;
                this.lungeSpeed = lungeSpeed;
                this.lungeDuration = lungeDuration;
                this.lungeStopDistance = lungeStopDistance;
                this.aimLockNormalized = aimLockNormalized;
                this.lockedDashDistance = lockedDashDistance;
            }

            public void SetPhaseTwo(BrutePhaseSettings settings)
            {
                chargeTimeMultiplier = settings.chargeTimeMultiplier;
                tauntMultiplier = settings.tauntMultiplier;
                minTauntDuration = settings.minTauntDuration;
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

            // ILockedDashAim
            public float AimLockNormalized => aimLockNormalized();
            public float LockedDashDistance => lockedDashDistance();

            // IMeleeAttacker (wind-up is the taunt)
            public float MeleeWindUpDuration => Mathf.Max(minTauntDuration, melee.MeleeWindUpDuration * tauntMultiplier);
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
            public float LungeStopDistance => lungeStopDistance();
        }
    }
}
