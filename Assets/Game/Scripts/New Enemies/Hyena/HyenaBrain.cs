using UnityEngine;

namespace junklite
{
    /// <summary>
    /// Hyena decisions layered over the reusable melee-chaser loop. The Hyena is the
    /// fast, evasive fighter: it reads a swing and hops out of its reach, punishes a
    /// swing the player is still committed to with a charged dash, and opens with
    /// that dash from range so it can't be kited for free.
    ///
    /// Balance levers (all serialized here, runtime state on this component):
    /// - A dodge only answers a swing that can actually reach it (weapon reach + lunge
    ///   margin), started while the player faces it.
    /// - Dodge fatigue: every dodge lowers the next dodge's chance; it recovers while
    ///   it isn't dodging, so a player who keeps pressing gets through.
    /// - The dodge side is chosen (IDodgeAim): away when there is room, through the
    ///   player when the back is blocked by a wall or a lane-mate; landing behind a
    ///   player mid-swing earns a bite before she turns.
    /// - Every hop ends in the dash. The moment a dodge lands (reactive or retreat) she
    ///   winds up (ChargeState, charge.chargeTime, the warning and the charge pose: the
    ///   player's window to react) and then dashes, no queue lease, no roll by default
    ///   (counterChance), as long as the player is inside maxCounterDashRange. The dodge
    ///   has i-frames, the counter's wind-up is armored (IChargeArmor) and the dash
    ///   cannot be interrupted, so hop, wind-up and punish are one uninterruptible beat;
    ///   hits still deal damage throughout. A whiffed dash still stuns her (the punish
    ///   window) and leaves her winded; a landed dash restarts the shared dash cooldown,
    ///   which only gates the opener. Fatigue is what stops the hop itself from looping.
    /// - Hit pressure: a stagger that keeps getting refreshed by a combo is broken at
    ///   once after a couple of hits (the dodge has i-frames), and a stagger that ends
    ///   under pressure ends with a retreat hop. Both are hops, so both dash back.
    /// - Vault (ILeaper, LeapState): squared up to a planted player who faces her, she
    ///   sometimes jumps clean over them and lands just behind, then bites before they
    ///   turn. Same leap as the Grunt's vault (telegraph crouch, kinematic arc, landing),
    ///   aimed at the player's far side instead of the far front slot.
    /// </summary>
    public sealed class HyenaBrain : MeleeChaserBrain, IDodgeAim, IChargeArmor, ILeaper
    {
        private enum DodgePurpose { None, Reactive, Retreat }

        [Header("Hyena - Reactive Dodge")]
        [SerializeField] private DodgeBehavior dodge = new();
        [Tooltip("Base chance to dodge a swing that can reach it. Fatigue lowers it.")]
        [SerializeField, Range(0f, 1f)] private float dodgeChance = 0.7f;
        [Tooltip("Never reacts to a swing started further away than this (axis distance).")]
        [SerializeField] private float dodgeCheckRange = 4f;
        [Tooltip("Added to the player's weapon reach: a swing inside reach + margin is a threat (covers the attack lunge).")]
        [SerializeField, Min(0f)] private float dodgeReachMargin = 1.5f;
        [Tooltip("Weapon reach used when the player's weapon can't be read.")]
        [SerializeField, Min(0f)] private float fallbackSwingReach = 1.5f;
        [SerializeField] private float dodgeCooldown = 0.7f;
        [Tooltip("Each dodge adds this much fatigue (0-1). Chance = base x (1 - fatigue).")]
        [SerializeField, Range(0f, 1f)] private float dodgeFatiguePerDodge = 0.3f;
        [Tooltip("Seconds for fatigue to recover from 1 to 0 while it isn't dodging.")]
        [SerializeField, Min(0.1f)] private float dodgeFatigueRecovery = 4f;
        [Tooltip("Layers that block a dodge: walls, floor edges, other enemies. Empty = Default + Ground + Wall + Enemies.")]
        [SerializeField] private LayerMask dodgeObstacleLayers;
        [Tooltip("With both sides open, chance to dodge through the player and land behind her.")]
        [SerializeField, Range(0f, 1f)] private float throughDodgeChance = 0.35f;
        [Tooltip("A through-dodge must land at least this far past the player's centre.")]
        [SerializeField, Min(0f)] private float throughClearance = 1f;

        [Header("Hyena - Charge Dash")]
        [SerializeField] private ChargeBehavior charge = new();
        [SerializeField] private DashBehavior dash = new();
        [Tooltip("Chance that a landed hop (reactive dodge or retreat) is followed by the counter: the charge wind-up (charge.chargeTime, armored against hitstun) and then the dash. 1 = every hop is punished.")]
        [SerializeField, Range(0f, 1f)] private float counterChance = 1f;
        [Tooltip("Stun after a dash that hit nothing: the punish window.")]
        [SerializeField] private float whiffStunDuration = 1.5f;
        [Tooltip("Furthest the dash (counter or opener) is attempted from (axis distance).")]
        [SerializeField] private float maxCounterDashRange = 8f;
        [Tooltip("Seconds before the opener dash may be rolled again after any dash. The counter after a hop ignores it.")]
        [SerializeField, Min(0f)] private float dashCooldown = 4f;
        [Tooltip("Opener: while chasing from at least this far, roll for a charged dash instead of running in. 0 = never.")]
        [SerializeField, Min(0f)] private float openerDashMinRange = 5f;
        [SerializeField, Range(0f, 1f)] private float openerDashChance = 0.35f;
        [Tooltip("Seconds between opener rolls while chasing at range.")]
        [SerializeField, Min(0.1f)] private float openerDashRetry = 1.5f;

        [Header("Hyena - Hit Pressure")]
        [SerializeField, Min(0.1f)] private float pressureWindow = 2.5f;
        [SerializeField, Min(1)] private int pressureHitsToMax = 3;
        [Tooltip("Hits landed inside the pressure window that break a stagger at once: it hops out of the combo (i-frames) instead of eating the next swing. 0 = never.")]
        [SerializeField, Min(0)] private int breakStaggerHits = 2;
        [Tooltip("Seconds between stagger breaks. Parry and whiff stuns are never broken.")]
        [SerializeField, Min(0f)] private float breakStaggerCooldown = 1f;
        [Tooltip("Pressure (0-1) at which a stagger ends with a retreat hop instead of re-engaging.")]
        [SerializeField, Range(0f, 1f)] private float retreatPressure = 0.66f;
        [SerializeField, Min(0f)] private float retreatCooldown = 5f;
        [Header("Hyena - Vault Over The Player")]
        [SerializeField] private bool vaultEnabled = true;
        [Tooltip("Axis distance band the vault is attempted from: too close and the arc is a hop in place, too far and it reads as a lunge.")]
        [SerializeField, Min(0f)] private float vaultMinRange = 1.5f;
        [SerializeField, Min(0f)] private float vaultMaxRange = 5f;
        [Tooltip("Chance per roll (every vaultRetry seconds while the vault is open).")]
        [SerializeField, Range(0f, 1f)] private float vaultChance = 0.4f;
        [SerializeField, Min(0.1f)] private float vaultRetry = 0.8f;
        [SerializeField, Min(0f)] private float vaultCooldown = 5f;
        [Tooltip("Land this far behind the player's centre: inside bite range, so the landing bite connects.")]
        [SerializeField, Min(0f)] private float vaultLandBehind = 1.4f;
        [SerializeField, Min(0.5f)] private float vaultSpeed = 12f;
        [Tooltip("Arc height. Must clear the player (capsule ~2.4).")]
        [SerializeField, Min(0f)] private float vaultHeight = 2.2f;
        [Tooltip("Grounded crouch before leaving the ground. A hit during this cancels the vault.")]
        [SerializeField, Min(0f)] private float vaultTelegraph = 0.12f;
        [SerializeField, Min(0f)] private float vaultLandDuration = 0.1f;
        [Tooltip("Untouchable while airborne, like her dodge.")]
        [SerializeField] private bool vaultHasIFrames = true;
        [Tooltip("Call the vault off during the crouch if the player moves this far along the lane.")]
        [SerializeField, Min(0.1f)] private float vaultAbortDistance = 1.5f;
        [Tooltip("Never two enemies of the same group airborne within this many seconds.")]
        [SerializeField, Min(0f)] private float groupLeapCooldown = 1f;
        [Tooltip("After landing behind the player, chance to bite at once.")]
        [SerializeField, Range(0f, 1f)] private float vaultBiteChance = 0.9f;

        [Header("Hyena - Debug")]
        [Tooltip("Log stagger breaks and what each dodge ended in (counter, lease refused, chase).")]
        [SerializeField] private bool logDecisions;

        private const float ObstacleRayHeight = 0.5f;
        private const float ObstacleBuffer = 0.3f;

        // Runtime
        private readonly HitPressureWindow pressure = new(8);
        private Damageable damageable;
        private bool damageSubscribed;
        private LayerMask resolvedObstacleLayers;
        private float lastDodgeTime = float.NegativeInfinity;
        private float dodgeFatigue;
        private float fatigueStampTime;
        private float nextDashAllowed;
        private float nextOpenerRoll;
        private float nextRetreatAllowed;
        private float nextBreakAllowed;
        private bool wasPlayerAttacking;
        private DodgePurpose dodgePurpose;
        private int dodgeSign;
        private bool counterCharging;

        // Vault
        private readonly Collider[] overlapBuffer = new Collider[8];
        private Vector3 leapDestination;
        private Vector3 vaultAnchor;
        private float vaultGroundY;
        private int vaultSide;
        private float nextVaultAllowed;
        private float nextVaultRoll;

        // Target cache (refreshed when the target transform changes)
        private Transform cachedTarget;
        private PlayerState targetState;
        private Character2D5Controller targetController;
        private PlayerArmament targetArmament;
        private MeleeWeaponData cachedWeapon;
        private float cachedReach;

        /// <summary>IDodgeAim: set right before entering DodgeState, cleared when it ends.</summary>
        public int DodgeDirectionSign => dodgeSign;

        /// <summary>IChargeArmor: the wind-up of a counter out of a hop cannot be staggered; the opener's can.</summary>
        public bool ChargeIsArmored => counterCharging && StateMachine.CurrentState is ChargeState;

        // ILeaper
        public Vector3 LeapDestination => leapDestination;
        public float LeapSpeed => vaultSpeed;
        public float LeapHeight => vaultHeight;
        public float LeapTelegraph => vaultTelegraph;
        public float LeapLandDuration => vaultLandDuration;
        public bool LeapHasIFrames => vaultHasIFrames;
        public float LeapMaxCorrectionSpeed => 6f;

        /// <summary>0-1 how tired its legs are; 1 = will not dodge.</summary>
        public float DodgeFatigue => CurrentFatigue();

        protected override void Awake()
        {
            base.Awake();
            damageable = GetComponent<Damageable>();
            resolvedObstacleLayers = dodgeObstacleLayers.value != 0
                ? dodgeObstacleLayers
                : (LayerMask)LayerMask.GetMask("Default", "Ground", "Wall", "Enemies");
            EnsureHyenaBehaviors();
            InitializeHyenaCapabilities();
        }

        protected override void OnEnable()
        {
            base.OnEnable();
            InitializeHyenaCapabilities();
            if (!damageSubscribed && damageable != null)
            {
                damageable.OnDamageResolved += HandleDamageResolved;
                damageSubscribed = true;
            }
        }

        protected override void OnDisable()
        {
            if (damageSubscribed && damageable != null)
            {
                damageable.OnDamageResolved -= HandleDamageResolved;
                damageSubscribed = false;
            }

            dodgePurpose = DodgePurpose.None;
            dodgeSign = 0;
            counterCharging = false;
            UninitializeHyenaCapabilities();
            base.OnDisable();
        }

        protected override void InitializeStateMachine()
        {
            StateMachine.RegisterStates(
                new PatrolState(Actor),
                new IdleState(Actor),
                new ChaseState(Actor),
                new WaitForOpeningState(Actor),
                new MeleeAttackState(Actor),
                new DodgeState(Actor),
                new ChargeState(Actor),
                new DashState(Actor),
                new LeapState(Actor),
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
            base.TickBrain();

            if (!Actor.IsAlive || !Actor.IsInCombat || !Actor.HasTarget)
            {
                wasPlayerAttacking = false;
                return;
            }

            RefreshTargetCache();

            if (TryBreakStagger())
                return;

            if (TryOpenerDash())
                return;

            if (TryVault())
                return;

            CheckForDodgeOpportunity();
        }

        protected override void OnTargetChanged(PlayerCharacter previous, PlayerCharacter current)
        {
            base.OnTargetChanged(previous, current);

            if (current == null)
            {
                pressure.Clear();
                wasPlayerAttacking = false;
            }
        }

        protected override void EvaluateNextAction(bool actionCompleted = false)
        {
            // A stagger under pressure ends with a hop out, not a walk back into the combo.
            if (actionCompleted && TryRetreatHop())
                return;

            base.EvaluateNextAction(actionCompleted);
        }

        protected override bool IsDecisionLocked()
        {
            IState current = StateMachine.CurrentState;
            return base.IsDecisionLocked()
                || current is DodgeState
                || current is ChargeState
                || current is DashState
                || current is LeapState;
        }

        public override bool TryGetCapability<T>(out T capability)
        {
            if (dodge is T dodgeCapability)
                capability = dodgeCapability;
            else if (charge is T chargeCapability)
                capability = chargeCapability;
            else if (dash is T dashCapability)
                capability = dashCapability;
            else
                return base.TryGetCapability(out capability);

            return true;
        }

        // =============================================================
        // REACTIVE DODGE
        // =============================================================

        private void CheckForDodgeOpportunity()
        {
            bool attacking = targetState != null && targetState.IsAttacking;
            bool swingStarted = attacking && !wasPlayerAttacking;
            wasPlayerAttacking = attacking;

            if (!swingStarted || !CanDodgeNow())
                return;

            float axisDistance = AxisDistanceToTarget();
            if (!HyenaTactics.SwingThreatens(axisDistance, SwingReach(), dodgeReachMargin, dodgeCheckRange))
                return;

            if (!IsPlayerFacingMe())
                return;

            if (Random.value > HyenaTactics.DodgeChance(dodgeChance, CurrentFatigue()))
                return;

            StartDodge(DodgePurpose.Reactive, preferAway: false);
        }

        /// <summary>Free to hop: not already in a move, not forced, and not past the bite's wind-up.</summary>
        private bool CanDodgeNow()
        {
            if (Time.time - lastDodgeTime < dodgeCooldown || IsForcedState())
                return false;

            IState current = StateMachine.CurrentState;
            if (current is DodgeState || current is ChargeState || current is DashState || current is LeapState)
                return false;

            // The swing has started: commit to it instead of flickering out.
            if (current is MeleeAttackState melee && !melee.IsInWindUp)
                return false;

            return true;
        }

        private void StartDodge(DodgePurpose purpose, bool preferAway)
        {
            dodgeSign = ChooseDodgeSide(preferAway);
            dodgePurpose = purpose;
            lastDodgeTime = Time.time;
            AddFatigue(dodgeFatiguePerDodge);

            if (purpose == DodgePurpose.Retreat)
                LeaveStagger();

            ChangeState<DodgeState>();
        }

        /// <summary>
        /// A hop out of a stagger ends the stagger's side effects first: the knockback
        /// still in flight (cancelled while StunnedState is current, so its end callback
        /// is a no-op) and the hitstun status, so the next hit after the hop staggers
        /// her normally instead of refreshing a status she already escaped.
        /// </summary>
        private void LeaveStagger()
        {
            Movement?.CancelKnockback();
            Actor.StatusEffects?.Remove(StatusEffectType.Hitstun);
        }

        /// <summary>Picks the open side: never into a wall or a lane-mate when the other way is clear.</summary>
        private int ChooseDodgeSide(bool preferAway)
        {
            if (!Actor.HasTarget || Movement == null)
                return -1;

            float signed = Movement.GetSignedAxisDistance(transform.position, Actor.Target.position);
            Vector3 toward = Movement.MovementAxis * (signed >= 0f ? 1f : -1f);
            float axisDistance = Mathf.Abs(signed);
            float distance = dodge.DodgeDistance;

            float roomAway = ClearDistance(-toward, distance);
            float roomThrough = distance - axisDistance < throughClearance
                ? 0f
                : ClearDistance(toward, distance);

            return HyenaTactics.ChooseDodgeSide(
                roomAway, roomThrough, distance, throughDodgeChance, Random.value, preferAway);
        }

        /// <summary>Clear run along <paramref name="direction"/> before an obstacle, up to <paramref name="distance"/>. The player is not an obstacle.</summary>
        private float ClearDistance(Vector3 direction, float distance)
        {
            Vector3 origin = transform.position + Vector3.up * ObstacleRayHeight;
            if (Physics.Raycast(origin, direction, out RaycastHit hit, distance + ObstacleBuffer,
                    resolvedObstacleLayers, QueryTriggerInteraction.Ignore))
                return Mathf.Max(0f, hit.distance - ObstacleBuffer);

            return distance;
        }

        private void HandleDodgeCompleted()
        {
            if (!Actor.IsAlive)
                return;

            DodgePurpose purpose = dodgePurpose;
            dodgePurpose = DodgePurpose.None;
            dodgeSign = 0;

            if (!Actor.HasTarget)
            {
                EvaluateNextAction(true);
                return;
            }

            if (TryCounterAfterDodge(purpose))
                return;

            EvaluateNextAction(true);
        }

        /// <summary>
        /// The hop's second half: the armored wind-up and then the dash, while the
        /// player is inside <see cref="maxCounterDashRange"/>. Nothing else gates it
        /// (no queue lease, no dash cooldown), so a dodge the player sees is always
        /// followed by the punish. Returns false when the normal evaluation (chase,
        /// queue) should take over instead.
        /// </summary>
        private bool TryCounterAfterDodge(DodgePurpose purpose)
        {
            float axisDistance = AxisDistanceToTarget();
            if (axisDistance > maxCounterDashRange)
            {
                Log($"dodge ended ({purpose}); player out of dash range ({axisDistance:0.0}): re-engage");
                return false;
            }

            if (Random.value > counterChance)
            {
                Log($"dodge ended ({purpose}); counter roll failed: re-engage");
                return false;
            }

            Log($"counter dash after {purpose} hop (axis {axisDistance:0.0})");
            StartCounterDash();
            return true;
        }

        private void Log(string message)
        {
            if (logDecisions)
                Debug.Log($"[Hyena] {name}: {message}", this);
        }

        // =============================================================
        // CHARGE DASH (counter and opener)
        // =============================================================

        private bool CanDash() => Time.time >= nextDashAllowed;

        /// <summary>Opener: telegraphed (ChargeState shows the warning) then the dash.</summary>
        private void StartDash()
        {
            nextDashAllowed = Time.time + dashCooldown;
            counterCharging = false;
            dash.ResetHitResult();
            ChangeState<ChargeState>();
        }

        /// <summary>
        /// The armor flag lives only while the counter's ChargeState runs into its DashState. Any
        /// other way out of the charge (a status stun or a forced state ignores CanBeInterrupted,
        /// a target loss, death) drops it, or the next opener charge would run armored.
        /// </summary>
        protected override void OnStateChanged(IState from, IState to)
        {
            if (counterCharging && from is ChargeState && to is not DashState)
                counterCharging = false;
        }

        /// <summary>
        /// Counter out of a hop: the charge wind-up (charge.chargeTime; ChargeState shows
        /// the warning and the charge pose, the player's window to react) armored against
        /// hitstun through <see cref="IChargeArmor"/>, then the dash. The group lease is
        /// taken when free but never waited for, and a knockback that somehow outlived
        /// the hop is dropped so <see cref="EnemyMovement.DashTo"/> cannot refuse the
        /// run. DashState strikes in place when already in reach.
        /// </summary>
        private void StartCounterDash()
        {
            nextDashAllowed = Time.time + dashCooldown;
            EnemyEngagementDirector.TryAcquireAttack(Actor, requireFrontPosition: false);
            if (Movement != null && Movement.IsInKnockback)
                Movement.CancelKnockback();

            dash.ResetHitResult();
            counterCharging = true;
            ChangeState<ChargeState>();
        }

        /// <summary>Chasing from range at the front of the queue: sometimes charge in instead of running in.</summary>
        private bool TryOpenerDash()
        {
            if (openerDashMinRange <= 0f || openerDashChance <= 0f)
                return false;
            if (StateMachine.CurrentState is not ChaseState || Time.time < nextOpenerRoll)
                return false;

            nextOpenerRoll = Time.time + openerDashRetry;

            if (!CanDash() || Movement == null || !Movement.IsGrounded)
                return false;

            float axisDistance = AxisDistanceToTarget();
            if (axisDistance < openerDashMinRange || axisDistance > maxCounterDashRange)
                return false;

            if (!EnemyEngagementDirector.TryGetAssignment(Actor, out EnemyEngagementAssignment assignment)
                || !assignment.IsFront)
                return false;

            if (Random.value > openerDashChance || !TryAcquireAttackPermission())
                return false;

            StartDash();
            return true;
        }

        private void HandleChargeCompleted()
        {
            counterCharging = false;
            if (!Actor.IsAlive)
                return;

            dash.ResetHitResult();
            ChangeState<DashState>();
        }

        private void HandleDashCompleted()
        {
            if (!Actor.IsAlive)
                return;

            counterCharging = false;
            Actor.HideAttackWarning();
            ReleaseAttackPermission();

            if (!Actor.HasTarget)
            {
                ReturnToPassive();
                return;
            }

            if (!dash.LastHitApplied)
            {
                // The punish window: stunned, and too winded to dodge straight out of it.
                Log("dash whiffed: punish window");
                AddFatigue(1f);
                stun.ForcedStunDuration = whiffStunDuration;
                ChangeState<StunnedState>();
                return;
            }

            EvaluateNextAction(true);
        }

        // =============================================================
        // VAULT OVER THE PLAYER
        // =============================================================

        /// <summary>
        /// Closing in or waiting on a planted player who faces her: roll to jump clean
        /// over them and land behind. Only from Chase or the queue, never mid-action.
        /// </summary>
        private bool TryVault()
        {
            if (!vaultEnabled || Time.time < nextVaultAllowed || Time.time < nextVaultRoll)
                return false;

            IState current = StateMachine.CurrentState;
            if (current is not ChaseState && current is not WaitForOpeningState)
                return false;
            if (Movement == null || !Movement.IsGrounded)
                return false;

            nextVaultRoll = Time.time + vaultRetry;

            float axisDistance = AxisDistanceToTarget();
            bool grounded = targetState == null || targetState.IsGrounded;
            bool rolling = targetState != null && targetState.IsRolling;
            if (!HyenaTactics.VaultOpen(axisDistance, vaultMinRange, vaultMaxRange, IsPlayerFacingMe(), grounded, rolling))
                return false;

            float signed = Movement.GetSignedAxisDistance(transform.position, Actor.Target.position);
            vaultSide = signed >= 0f ? 1 : -1;
            vaultGroundY = transform.position.y;
            if (!TryComputeVaultLanding(out Vector3 destination) || !IsLeapLandingValid(destination))
                return false;

            if (Random.value > vaultChance)
                return false;
            if (!EnemyEngagementDirector.TryReserveLeap(Actor, groupLeapCooldown))
                return false;

            nextVaultAllowed = Time.time + vaultCooldown;
            vaultAnchor = Actor.Target.position;
            leapDestination = destination;
            ReleaseAttackPermission();
            Log($"vault over the player (axis {axisDistance:0.0})");
            ChangeState<LeapState>();
            return StateMachine.CurrentState is LeapState;
        }

        /// <summary>Landing point just behind the player (her far side), at take-off ground height and this lane depth.</summary>
        private bool TryComputeVaultLanding(out Vector3 landing)
        {
            landing = default;
            if (!Actor.HasTarget || Movement == null)
                return false;

            Vector3 laneAxis = Mathf.Abs(Movement.MovementAxis.x) >= Mathf.Abs(Movement.MovementAxis.z)
                ? Vector3.right
                : Vector3.forward;
            Vector3 depthAxis = Vector3.Cross(Vector3.up, laneAxis);

            landing = Actor.Target.position + laneAxis * (vaultSide * vaultLandBehind);
            landing.y = vaultGroundY;
            landing += depthAxis * (Vector3.Dot(transform.position, depthAxis) - Vector3.Dot(landing, depthAxis));
            return true;
        }

        public void RefreshLeapDestination()
        {
            if (!TryComputeVaultLanding(out Vector3 landing))
                return;

            // Keep the last good landing if the new one is behind a wall or over a gap.
            if (!IsSegmentClear(Actor.Target.position, landing) || !HasGroundAt(landing))
                return;

            leapDestination = landing;
        }

        public bool ShouldAbortLeap()
        {
            if (!Actor.HasTarget)
                return true;
            if (targetState != null && (targetState.IsRolling || !targetState.IsGrounded))
                return true;

            return Movement.GetAbsAxisDistance(Actor.Target.position, vaultAnchor) > vaultAbortDistance;
        }

        private bool IsLeapLandingValid(Vector3 destination)
        {
            Vector3 delta = destination - transform.position;
            delta.y = 0f;
            if (delta.magnitude <= 0.2f)
                return false;

            // Wall, door blocker, or ledge between her and the landing point.
            if (!IsSegmentClear(transform.position, destination) || !HasGroundAt(destination))
                return false;

            // Another enemy body already standing there.
            int count = Physics.OverlapSphereNonAlloc(
                destination + Vector3.up * 1f,
                0.45f,
                overlapBuffer,
                LayerMask.GetMask("Enemies"),
                QueryTriggerInteraction.Ignore);
            for (int i = 0; i < count; i++)
            {
                EnemyCharacter other = overlapBuffer[i].GetComponentInParent<EnemyCharacter>();
                overlapBuffer[i] = null;
                if (other != null && other != Actor)
                    return false;
            }

            return true;
        }

        private bool IsSegmentClear(Vector3 from, Vector3 to)
        {
            Vector3 origin = new(from.x, vaultGroundY + 0.9f, from.z);
            Vector3 delta = to - from;
            delta.y = 0f;
            float distance = delta.magnitude;
            if (distance <= 0.001f)
                return true;

            return !Physics.Raycast(origin, delta / distance, distance, resolvedObstacleLayers, QueryTriggerInteraction.Ignore);
        }

        private bool HasGroundAt(Vector3 point) =>
            Physics.Raycast(point + Vector3.up * 0.5f, Vector3.down, 2.5f, resolvedObstacleLayers, QueryTriggerInteraction.Ignore);

        /// <summary>Landed behind the player: bite before they turn, else re-engage normally.</summary>
        public void OnLeapComplete()
        {
            if (!Actor.IsAlive)
                return;

            if (Actor.HasTarget && IsTargetInMeleeRange() && Random.value <= vaultBiteChance
                && TryAcquireAttackPermission())
            {
                Log("vault landed behind the player: bite");
                ChangeState<MeleeAttackState>();
                return;
            }

            Log("vault landed: re-engage");
            EvaluateNextAction(true);
        }

        public void OnLeapAborted()
        {
            // The opening closed during the crouch: try again soon, not after the full cooldown.
            nextVaultAllowed = Time.time + 1f;
            Log("vault called off during the crouch");
            if (!Actor.IsAlive)
                return;

            EvaluateNextAction(true);
        }

        public void OnLeapCancelled()
        {
            // Hit during the crouch: the stun owns the body. Give the group window back.
            EnemyEngagementDirector.ReleaseLeap(Actor);
            nextVaultAllowed = Time.time + 1f;
        }

        // =============================================================
        // HIT PRESSURE
        // =============================================================

        private void HandleDamageResolved(DamageResult result, DamageRequest request)
        {
            if (!result.WasApplied || request.IsTickDamage)
                return;

            pressure.Record(Time.time);
            EnemyEngagementDirector.ReportHitTaken(Actor);
        }

        /// <summary>
        /// Being combo'd: every hit refreshes the stagger, so waiting for it to end never
        /// happens. After <see cref="breakStaggerHits"/> hits in the window she breaks the
        /// stagger herself with a hop (i-frames), then counters from <see cref="HandleDodgeCompleted"/>.
        /// Earned punish windows (parry stun, whiff stun) are never broken.
        /// </summary>
        private bool TryBreakStagger()
        {
            if (Time.time < nextBreakAllowed || StateMachine.CurrentState is not StunnedState)
                return false;
            if (Actor.IsParryStunned || stun.ForcedStunDuration > 0f)
                return false;
            if (!HyenaTactics.StaggerBreaks(pressure.CountWithin(Time.time, pressureWindow), breakStaggerHits))
                return false;
            if (AxisDistanceToTarget() > dodgeCheckRange)
                return false;

            nextBreakAllowed = Time.time + breakStaggerCooldown;
            pressure.Clear();
            StartDodge(DodgePurpose.Retreat, preferAway: true);
            bool broke = StateMachine.CurrentState is DodgeState;
            Log(broke ? "stagger broken: hop out of the combo" : "stagger break requested but the hop did not start");
            return broke;
        }

        /// <summary>Out of a stagger while being combo'd: hop away (or through, if the back is blocked).</summary>
        private bool TryRetreatHop()
        {
            if (!Actor.IsAlive || !Actor.HasTarget || Time.time < nextRetreatAllowed)
                return false;
            if (StateMachine.CurrentState is not StunnedState)
                return false;
            if (pressure.Pressure01(Time.time, pressureWindow, pressureHitsToMax) < retreatPressure)
                return false;
            if (AxisDistanceToTarget() > dodgeCheckRange)
                return false;

            nextRetreatAllowed = Time.time + retreatCooldown;
            pressure.Clear();
            Log("stagger ended under pressure: retreat hop");
            StartDodge(DodgePurpose.Retreat, preferAway: true);
            return true;
        }

        // =============================================================
        // FATIGUE
        // =============================================================

        private float CurrentFatigue() =>
            HyenaTactics.DecayFatigue(dodgeFatigue, Time.time - fatigueStampTime, dodgeFatigueRecovery);

        private void AddFatigue(float amount)
        {
            dodgeFatigue = Mathf.Clamp01(CurrentFatigue() + amount);
            fatigueStampTime = Time.time;
        }

        // =============================================================
        // TARGET READS
        // =============================================================

        private void RefreshTargetCache()
        {
            Transform target = Actor.Target;
            if (target == cachedTarget)
                return;

            cachedTarget = target;
            PlayerCharacter player = Actor.TargetCharacter;
            targetState = player != null ? player.State : null;
            targetController = target != null ? target.GetComponent<Character2D5Controller>() : null;
            targetArmament = player != null ? player.GetComponentInChildren<PlayerArmament>(true) : null;
            cachedWeapon = null;
            cachedReach = fallbackSwingReach;
            wasPlayerAttacking = false;
        }

        /// <summary>Reach of the player's current side swing before its lunge; re-read when the weapon changes.</summary>
        private float SwingReach()
        {
            MeleeWeaponData weapon = targetArmament != null ? targetArmament.Current as MeleeWeaponData : null;
            if (weapon != cachedWeapon)
            {
                cachedWeapon = weapon;
                cachedReach = weapon != null ? HyenaTactics.SwingReach(weapon) : fallbackSwingReach;
            }

            return cachedReach;
        }

        private float AxisDistanceToTarget() =>
            Actor.HasTarget && Movement != null
                ? Movement.GetAbsAxisDistance(transform.position, Actor.Target.position)
                : float.MaxValue;

        private bool IsPlayerFacingMe()
        {
            Transform target = Actor.Target;
            if (target == null)
                return false;

            float facingSign = targetController != null
                ? targetController.FacingSign
                : Mathf.Sign(target.localScale.x);
            Vector3 playerFacing = target.right * facingSign;
            Vector3 playerToEnemy = (transform.position - target.position).normalized;
            return Vector3.Dot(playerFacing, playerToEnemy) > 0f;
        }

        // =============================================================
        // CAPABILITY WIRING
        // =============================================================

        private void EnsureHyenaBehaviors()
        {
            dodge ??= new DodgeBehavior();
            charge ??= new ChargeBehavior();
            dash ??= new DashBehavior();
        }

        private void InitializeHyenaCapabilities()
        {
            EnsureHyenaBehaviors();
            UninitializeHyenaCapabilities();

            dodge.Completed += HandleDodgeCompleted;
            charge.Completed += HandleChargeCompleted;
            dash.Completed += HandleDashCompleted;
            dash.Initialize(gameObject);
        }

        private void UninitializeHyenaCapabilities()
        {
            if (dodge != null)
                dodge.Completed -= HandleDodgeCompleted;
            if (charge != null)
                charge.Completed -= HandleChargeCompleted;
            if (dash != null)
            {
                dash.Completed -= HandleDashCompleted;
                dash.Dispose();
            }
        }

#if UNITY_EDITOR
        protected override void OnDrawGizmosSelected()
        {
            base.OnDrawGizmosSelected();

            Gizmos.color = new Color(1f, 1f, 0f, 0.3f);
            Gizmos.DrawWireSphere(transform.position, dodgeCheckRange);

            if (openerDashMinRange > 0f)
            {
                Gizmos.color = new Color(1f, 0.3f, 0.3f, 0.3f);
                Gizmos.DrawWireSphere(transform.position, openerDashMinRange);
            }
        }
#endif
    }
}
