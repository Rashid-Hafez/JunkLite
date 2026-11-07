using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace junklite
{
    /// <summary>
    /// Grunt decisions layered over the reusable melee-chaser loop, the same way
    /// HyenaBrain layers dodge and counter-dash. Everything here is group policy:
    ///
    /// - Two modes from what the player is doing. Pursue (player running): chase in
    ///   single file at the leader's speed, no tricks. Engage (player planted,
    ///   attacking or parrying): hold slots, shuffle, feint, vault, give ground.
    /// - Lane spacing (ILaneSpacing): every voluntary move keeps a gap from the real
    ///   bodies ahead and behind and matches the leader's speed, so grunts never
    ///   shove each other. Physics collision stays on only as a safety net.
    /// - Ally-hit ripple: when a grunt ahead takes a real hit, the ones behind flinch
    ///   back one after another, stay back while the combo lasts, then creep in.
    /// - Second in the queue with an empty far side: vault over the line to the
    ///   player's other side. The landing re-aims at the player during the crouch
    ///   and flight, and the vault is called off if the player runs.
    /// - Front grunt under hit pressure: give ground, or hop out of the line so the
    ///   next grunt steps up.
    /// - Group mood from the director's hit pressure: wary packs hang back, quiet
    ///   packs press in and attack faster.
    ///
    /// All tunables are serialized here. Runtime state lives on this component or
    /// in the director's runtime entries, never on an asset.
    /// </summary>
    public sealed class GruntBrain : MeleeChaserBrain,
        IHoldPositioner, ILeaper, IBackOff, ILaneSpacing, IAllyHitListener,
        IPatrolRhythm, IAlerter, ISearcher
    {
        [Header("Grunt - Patrol & Idle")]
        [Tooltip("Min/max pause at each end of a patrol leg.")]
        [SerializeField] private Vector2 patrolEndPauseRange = new(0.4f, 1f);
        [Tooltip("Chance per leg to stop mid-way and look back for a moment.")]
        [SerializeField, Range(0f, 1f)] private float patrolLookBackChance = 0f;
        [SerializeField, Min(0.1f)] private float patrolLookBackDuration = 0.6f;
        [Tooltip("Idle guards occasionally turn to look the other way.")]
        [SerializeField] private bool idleFidget = true;
        [SerializeField] private Vector2 fidgetInterval = new(3f, 6f);
        [SerializeField, Range(0f, 1f)] private float fidgetChance = 0.5f;
        [SerializeField] private Vector2 fidgetHoldRange = new(1f, 2f);

        [Header("Grunt - Alert & Search")]
        [Tooltip("On first sight: stop and face the player for a beat before chasing. A hit skips it.")]
        [SerializeField] private bool alertEnabled = true;
        [Tooltip("Only idle guards take this beat; a patrolling grunt goes straight into the chase.")]
        [SerializeField] private Vector2 alertDurationRange = new(0.15f, 0.25f);
        [Tooltip("Idle grunts within this distance are alerted one after another when this grunt notices the player (they join only if the player is inside their own sensor).")]
        [SerializeField, Min(0f)] private float packAlertRadius = 6f;
        [SerializeField, Min(0f)] private float packAlertStep = 0.12f;
        [Tooltip("After losing the player: walk to where it was last seen and look both ways before going back to patrol.")]
        [SerializeField] private bool searchEnabled = true;
        [SerializeField, Min(0.05f)] private float searchLookDuration = 0.5f;

        [Header("Grunt - Decision Cadence")]
        [Tooltip("Seconds between group-aware re-evaluations while chasing or holding.")]
        [SerializeField, Min(0.02f)] private float decisionInterval = 0.15f;
        [SerializeField, Min(0f)] private float decisionJitter = 0.05f;

        [Header("Grunt - Per-Instance Variance")]
        [Tooltip("Chase speed is multiplied by 1 ± this on Awake so a crowd does not move in lockstep. Lane spacing caps followers at their leader's speed.")]
        [SerializeField, Range(0f, 0.5f)] private float chaseSpeedVariance = 0.1f;
        [Tooltip("Pause rhythm is multiplied by 1 ± this on Awake so neighbours shuffle out of phase.")]
        [SerializeField, Range(0f, 0.5f)] private float rhythmVariance = 0.2f;

        [Header("Grunt - Lane Spacing")]
        [Tooltip("Smallest edge-to-edge gap ever allowed to the grunt ahead or behind.")]
        [SerializeField, Min(0f)] private float contactGap = 0.05f;
        [Tooltip("How sharply a follower slows as it nears its leader. Higher = later, harder braking.")]
        [SerializeField, Min(0.5f)] private float spacingGain = 6f;

        [Header("Grunt - Body Ahead Probe")]
        [Tooltip("Looks this far along the lane toward the player for a wall or door blocker and brakes behind it. Enemy bodies are walked through; lane-mates that hold their place come from the engagement director.")]
        [SerializeField, Min(0f)] private float probeDistance = 1.5f;
        [SerializeField, Min(0.02f)] private float probeInterval = 0.1f;

        [Header("Grunt - Blocked")]
        [Tooltip("Commanded to move but not moving for this long = blocked: re-evaluate at once and wake the grunt in the way.")]
        [SerializeField, Min(0.05f)] private float blockedAfter = 0.25f;
        [Tooltip("Lane speed below which a commanded move counts as not moving.")]
        [SerializeField, Min(0f)] private float blockedSpeed = 0.3f;

        [Header("Grunt - Pursue / Engage")]
        [Tooltip("Player speed along the lane at or below which the player counts as planted.")]
        [SerializeField, Min(0f)] private float plantedSpeed = 1.5f;
        [Tooltip("Seconds the player must stay planted before grunts switch to Engage.")]
        [SerializeField, Min(0f)] private float engageDelay = 0.35f;
        [Tooltip("Seconds the player must keep moving before grunts switch to Pursue.")]
        [SerializeField, Min(0f)] private float pursueDelay = 0.2f;

        [Header("Grunt - Hold & Pace")]
        [Tooltip("Fraction of chase speed used to walk back to the slot after drifting.")]
        [SerializeField, Range(0.1f, 1f)] private float holdSpeedFraction = 0.6f;
        [SerializeField, Min(0.05f)] private float holdTolerance = 0.35f;
        [Tooltip("Largest shuffle away from the player.")]
        [SerializeField, Min(0f)] private float paceAmplitude = 0.4f;
        [Tooltip("Largest shuffle toward the player.")]
        [SerializeField, Min(0f)] private float paceTowardLimit = 0.15f;
        [SerializeField, Min(0.1f)] private float paceSpeed = 2f;
        [SerializeField] private Vector2 pacePauseRange = new(0.4f, 1.2f);
        [Tooltip("Comfortable gap kept behind the grunt in front.")]
        [SerializeField, Min(0f)] private float queueGap = 0.5f;
        [Tooltip("Extra slot distance when the pack is wary (being hit a lot).")]
        [SerializeField] private float waryHoldOffset = 0.3f;
        [Tooltip("Slot distance change when the pack is bold (player passive). Negative crowds in.")]
        [SerializeField] private float boldHoldOffset = -0.2f;

        [Header("Grunt - Feint")]
        [SerializeField, Range(0f, 1f)] private float feintChance = 0.25f;
        [SerializeField, Min(0f)] private float feintDistance = 0.6f;
        [SerializeField, Min(0.05f)] private float feintDuration = 0.15f;

        [Header("Grunt - Ally Hit Reaction")]
        [SerializeField] private bool allyHitReaction = true;
        [Tooltip("Only grunts this many places behind the hit grunt react.")]
        [SerializeField, Min(1)] private int allyHitMaxPlacesBehind = 3;
        [Tooltip("Delay per place in line, so the flinch travels down the line like a wave.")]
        [SerializeField, Min(0f)] private float rippleDelay = 0.08f;
        [SerializeField, Min(0f)] private float rippleJitter = 0.04f;
        [SerializeField, Min(0f)] private float recoilDistance = 0.5f;
        [SerializeField, Min(0.1f)] private float recoilSpeed = 4.5f;
        [Tooltip("Seconds to stay back after the last hit on an ally. Each new hit extends it.")]
        [SerializeField] private Vector2 recoilHoldRange = new(0.35f, 0.6f);
        [Tooltip("Slow speed for stepping back in afterwards.")]
        [SerializeField, Min(0.1f)] private float creepSpeed = 1.2f;

        [Header("Grunt - Vault To Open Side")]
        [SerializeField] private bool vaultEnabled = true;
        [Tooltip("Only the second grunt in a side's queue vaults, and only when this close (axis distance) to the player.")]
        [SerializeField, Min(0f)] private float vaultMaxStartDistance = 4.5f;
        [Tooltip("0 = vault only when the far side is empty. 1 = allow a 2/2 split with four grunts.")]
        [SerializeField, Min(0)] private int vaultMaxFarSideCount = 0;
        [Tooltip("Land this far outside attack range so the grunt takes one step in instead of landing on the player.")]
        [SerializeField, Min(0f)] private float vaultOvershoot = 0.3f;
        [SerializeField, Min(0.5f)] private float vaultSpeed = 9f;
        [Tooltip("Arc height. Must clear the grunt in front (capsule height ~2.3).")]
        [SerializeField, Min(0f)] private float vaultHeight = 1.6f;
        [Tooltip("Grounded crouch before leaving the ground. A hit during this cancels the vault.")]
        [SerializeField, Min(0f)] private float vaultTelegraph = 0.2f;
        [SerializeField, Min(0f)] private float leapLandDuration = 0.12f;
        [SerializeField] private bool leapHasIFrames;
        [SerializeField, Range(0f, 1f)] private float vaultChance = 0.85f;
        [SerializeField, Min(0f)] private float vaultCooldown = 3f;
        [Tooltip("Never two grunts of the same group airborne within this many seconds.")]
        [SerializeField, Min(0f)] private float groupLeapCooldown = 1f;
        [Tooltip("Call the vault off during the crouch if the player moves this far along the lane.")]
        [SerializeField, Min(0.1f)] private float vaultAbortDistance = 1.5f;
        [SerializeField, Min(0f)] private float vaultAbortCooldown = 1f;
        [Tooltip("How fast (units/s) the landing point may follow the player while airborne. 0 = fixed landing.")]
        [SerializeField, Min(0f)] private float vaultMaxCorrectionSpeed = 8f;
        [Tooltip("Walls, floors, door blockers. Empty = Default + Ground + Wall.")]
        [SerializeField] private LayerMask obstacleLayers;

        [Header("Grunt - Get-Up Recovery")]
        [Tooltip("After getting up from a launch, stand facing the player this long before choosing a move. Without it the grunt starts running the instant it stands, and the next hit cuts that run off (a flash of the run clip).")]
        [SerializeField, Min(0f)] private float getUpRecoverTime = 0.3f;

        [Header("Grunt - Hit Pressure")]
        [SerializeField, Min(0.1f)] private float pressureWindow = 2.5f;
        [SerializeField, Min(1)] private int pressureHitsToMax = 3;
        [Tooltip("Pressure (0..1) at which the front grunt gives ground.")]
        [SerializeField, Range(0f, 1f)] private float backOffPressure = 0.66f;
        [Tooltip("Retreat to attack distance + this.")]
        [SerializeField, Min(0f)] private float backOffDistance = 1.2f;
        [SerializeField, Min(0.1f)] private float backOffSpeed = 3.5f;
        [SerializeField] private Vector2 backOffHoldRange = new(0.6f, 1f);
        [SerializeField, Min(0f)] private float backOffCooldown = 2f;
        [SerializeField, Min(0.1f)] private float backOffTimeout = 1.5f;
        [Tooltip("At full pressure with an ally queued behind, hop out of the line. Cooldown stops yo-yoing.")]
        [SerializeField, Min(0f)] private float fallBackCooldown = 6f;

        [Header("Grunt - Group Mood")]
        [Tooltip("Group pressure (0..1) above which the pack hangs back and stops feinting.")]
        [SerializeField, Range(0f, 1f)] private float groupWaryPressure = 0.5f;
        [Tooltip("Seconds without any grunt taking a hit before the pack presses in.")]
        [SerializeField, Min(0f)] private float boldAfterQuietSeconds = 3f;
        [SerializeField, Min(0f)] private float normalHandoffDelay = 0.25f;
        [SerializeField, Min(0f)] private float boldHandoffDelay = 0.1f;

        private const float PursueChaseSlack = 0.25f;
        private const float StaleRecoilWindow = 0.3f;

        // Runtime
        private readonly HitPressureWindow pressure = new(8);
        private readonly Collider[] overlapBuffer = new Collider[8];
        private Damageable damageable;
        private PlayerState targetState;
        private Character2D5Controller targetController;
        private bool damageSubscribed;
        private float nextDecisionTime;
        private float nextVaultAllowed;
        private float nextBackOffAllowed;
        private float nextFallBackAllowed;
        private float combatStartTime;
        private float rhythmScale = 1f;
        private float paceBias;
        private float holdOffset;
        private bool moodAllowsFeint = true;
        private bool laneHasActiveAttacker;
        private LayerMask resolvedObstacleLayers;

        // Pursue / Engage
        private bool engaged;
        private float modeTimer;

        // Lane spacing (cached once per frame)
        private int neighborsFrame = -1;
        private bool neighborsValid;
        private EnemyLaneNeighbors neighbors;

        // Body-ahead probe (cached per probeInterval) and blocked detection
        private int probeMask;
        private float myHalfWidth = 0.5f;
        private float nextProbeTime;
        private float probeTime = -10f;
        private bool probeHasHit;
        private float probeEdgeDistance;
        private Vector3 probeVelocity;
        private EnemyCharacter probeEnemy;
        private float blockedTimer;
        private bool isBlocked;
        private float nextPokeTime;

        // Ally-hit reaction
        private float pendingRecoilTime = -1f;
        private float recoilHoldUntil;

        // Leap
        private Vector3 leapDestination;
        private bool leapIsFallBack;
        private int vaultSide;
        private Vector3 vaultAnchor;
        private float vaultGroundY;

        // Back-off
        private float pendingBackOffDistance;

        // Idle fidget, alert, search
        private static readonly List<EnemyCharacter> PackAlertBuffer = new();
        private float nextFidgetTime;
        private bool fidgetFlipped;
        private float getUpRecoverUntil;
        private bool alertPending;
        private bool alertResolving;
        private bool searchPending;

        // ------------------------------------------------------------
        // Capability surface
        // ------------------------------------------------------------

        public float HoldSpeed => chase.ChaseSpeed * holdSpeedFraction;
        public float HoldTolerance => holdTolerance;
        public float PaceAmplitude => paceAmplitude;
        public float PaceTowardLimit => paceTowardLimit;
        public float PaceSpeed => paceSpeed;
        public Vector2 PacePauseRange => pacePauseRange;
        public float PaceBias => paceBias;
        public float HoldOffset => holdOffset;
        public float RhythmScale => rhythmScale;
        public bool FeintAllowed => engaged && moodAllowsFeint && !laneHasActiveAttacker;
        public float FeintChance => feintChance;
        public float FeintDistance => feintDistance;
        public float FeintDuration => feintDuration;
        public LayerMask ObstacleLayers => resolvedObstacleLayers;
        public bool PaceEnabled => engaged;
        public float RecoilHoldUntil => recoilHoldUntil;
        public float RecoilSpeed => recoilSpeed;
        public float CreepSpeed => creepSpeed;

        public Vector3 LeapDestination => leapDestination;
        public float LeapSpeed => vaultSpeed;
        public float LeapHeight => leapIsFallBack ? vaultHeight * 0.7f : vaultHeight;
        public float LeapTelegraph => vaultTelegraph;
        public float LeapLandDuration => leapLandDuration;
        public bool LeapHasIFrames => leapHasIFrames;
        public float LeapMaxCorrectionSpeed => leapIsFallBack ? 0f : vaultMaxCorrectionSpeed;

        public float BackOffTargetDistance => pendingBackOffDistance;
        public float BackOffSpeed => backOffSpeed;
        public Vector2 BackOffHoldRange => backOffHoldRange;
        public float BackOffTimeout => backOffTimeout;
        public LayerMask BackOffObstacleLayers => resolvedObstacleLayers;

        public Vector2 PatrolEndPauseRange => patrolEndPauseRange;
        public float LookBackChance => patrolLookBackChance;
        public float LookBackDuration => patrolLookBackDuration;
        public float AlertDuration => Random.Range(
            Mathf.Min(alertDurationRange.x, alertDurationRange.y),
            Mathf.Max(alertDurationRange.x, alertDurationRange.y)) * rhythmScale;
        public float SearchLookDuration => searchLookDuration;

        /// <summary>0..1 how hard this grunt has been hit inside the pressure window.</summary>
        public float Pressure01 => pressure.Pressure01(Time.time, pressureWindow, pressureHitsToMax);

        /// <summary>True while the player is planted (fighting), false while running.</summary>
        public bool IsEngaged => engaged;

        /// <summary>Commanded to move but held in place by a body or wall for longer than blockedAfter.</summary>
        public bool IsBlocked => isBlocked;

        private bool IsRecoiling => pendingRecoilTime >= 0f || Time.time < recoilHoldUntil;

        // ------------------------------------------------------------
        // Lifecycle
        // ------------------------------------------------------------

        protected override void Awake()
        {
            base.Awake();
            damageable = GetComponent<Damageable>();
            resolvedObstacleLayers = obstacleLayers.value != 0
                ? obstacleLayers
                : (LayerMask)LayerMask.GetMask("Default", "Ground", "Wall");

            chase.SetSpeedMultiplier(Random.Range(1f - chaseSpeedVariance, 1f + chaseSpeedVariance));
            rhythmScale = Random.Range(1f - rhythmVariance, 1f + rhythmVariance);
            nextDecisionTime = Time.time + Random.value * decisionInterval;

            // Walls and door blockers only: enemy bodies are passed through, and the
            // ones that matter (lane-mates holding their place) come from the director.
            probeMask = resolvedObstacleLayers.value;
            CapsuleCollider body = GetComponent<CapsuleCollider>();
            if (body != null)
                myHalfWidth = body.radius * Mathf.Max(Mathf.Abs(transform.lossyScale.x), Mathf.Abs(transform.lossyScale.z));
        }

        protected override void OnEnable()
        {
            base.OnEnable();
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

            ClearReactions();
            EnemyEngagementDirector.SetYielding(Actor, false);
            base.OnDisable();
        }

        protected override void InitializeStateMachine()
        {
            StateMachine.RegisterStates(
                new PatrolState(Actor),
                new IdleState(Actor),
                new ChaseState(Actor),
                new HoldPositionState(Actor),
                new MeleeAttackState(Actor),
                new LeapState(Actor),
                new BackOffState(Actor),
                new AlertState(Actor),
                new SearchState(Actor),
                new StunnedState(Actor),
                new ParriedState(Actor),
                new DeadState(Actor));

            if (patrolWhenPassive && patrol.HasPatrol)
                StateMachine.SetInitialState<PatrolState>();
            else
                StateMachine.SetInitialState<IdleState>();
        }

        protected override void OnTargetChanged(PlayerCharacter previous, PlayerCharacter current)
        {
            ClearReactions();
            engaged = false;
            modeTimer = 0f;

            bool firstSight = current != null && previous == null && !Actor.IsInCombat && !alertResolving;
            if (firstSight)
                PackAlert();

            // Noticing: a visible beat before the chase, for idle guards only. A grunt
            // already walking its patrol breaks straight into the run. A hit skips the
            // beat too (HandleDamageResolved).
            if (firstSight && alertEnabled && !(StateMachine.CurrentState is PatrolState)
                && !IsForcedState() && !IsDecisionLocked())
            {
                alertPending = true;
                ChangeState<AlertState>();
                return;
            }

            alertPending = false;
            if (current == null && previous != null && Actor.IsInCombat)
                searchPending = searchEnabled;

            if (current != null)
            {
                combatStartTime = Time.time;
                targetController = current.Controller;
                targetState = current.GetComponentInParent<PlayerState>();
                if (targetState == null)
                    targetState = current.GetComponent<PlayerState>();

                // Register first so the gap is in place before the base loop evaluates.
                if (EnemyEngagementDirector.Register(Actor))
                    EnemyEngagementDirector.SetQueueGap(Actor, queueGap);
            }
            else
            {
                targetState = null;
                targetController = null;
                pressure.Clear();
                EnemyEngagementDirector.SetYielding(Actor, false);
            }

            base.OnTargetChanged(previous, current);
        }

        protected override bool IsDecisionLocked()
        {
            IState current = StateMachine.CurrentState;
            return base.IsDecisionLocked()
                || current is LeapState
                || current is BackOffState
                || current is LaunchedState;
        }

        /// <summary>The alert beat is over (or was skipped by a hit): enter combat for real.</summary>
        public void OnAlertComplete()
        {
            if (!alertPending)
                return;

            alertPending = false;
            if (!Actor.IsAlive)
                return;

            PlayerCharacter target = Actor.TargetCharacter;
            if (target == null)
            {
                ReturnToPassive();
                return;
            }

            alertResolving = true;
            try
            {
                OnTargetChanged(null, target);
            }
            finally
            {
                alertResolving = false;
            }
        }

        public void OnSearchComplete()
        {
            searchPending = false;
            if (!Actor.IsAlive)
                return;

            if (Actor.HasTarget)
            {
                EvaluateNextAction(true);
                return;
            }

            // The patrol resumes from here, not from the original spawn.
            patrol.Initialize(transform);
            ReturnToPassive();
        }

        /// <summary>
        /// Wakes idle grunts nearby one after another, nearest first. Each one
        /// re-scans its own sensor (facing ignored); those that cannot see the
        /// player only turn toward the noise.
        /// </summary>
        private void PackAlert()
        {
            if (packAlertRadius <= 0f)
                return;

            PackAlertBuffer.Clear();
            Vector3 here = transform.position;
            float radiusSq = packAlertRadius * packAlertRadius;
            IReadOnlyList<EnemyCharacter> enemies = EnemyCharacter.ActiveEnemies;
            for (int i = 0; i < enemies.Count; i++)
            {
                EnemyCharacter other = enemies[i];
                if (other == null || other == Actor || !other.IsAlive || other.IsInCombat || other.HasTarget)
                    continue;
                if (other.Perception == null || other.IsTutorialFrozen)
                    continue;

                Vector3 delta = other.transform.position - here;
                if (Mathf.Abs(delta.y) > 1.5f)
                    continue;
                delta.y = 0f;
                if (delta.sqrMagnitude > radiusSq)
                    continue;

                PackAlertBuffer.Add(other);
            }

            if (PackAlertBuffer.Count == 0)
                return;

            var ordered = new List<EnemyCharacter>(PackAlertBuffer);
            ordered.Sort((a, b) =>
                (a.transform.position - here).sqrMagnitude.CompareTo((b.transform.position - here).sqrMagnitude));
            StartCoroutine(PackAlertRoutine(ordered));
        }

        private IEnumerator PackAlertRoutine(List<EnemyCharacter> others)
        {
            for (int i = 0; i < others.Count; i++)
            {
                if (packAlertStep > 0f)
                    yield return new WaitForSeconds(packAlertStep);

                EnemyCharacter other = others[i];
                if (other == null || !other.IsAlive || other.IsInCombat || other.Perception == null)
                    continue;

                other.Perception.Alert(2f);
                if (!other.HasTarget && other.Movement != null
                    && other.StateMachine != null && other.StateMachine.CurrentState is IdleState)
                {
                    other.Movement.FaceTarget(transform.position);
                }
            }
        }

        /// <summary>Idle guards turn to look the other way now and then.</summary>
        private void UpdateIdleFidget()
        {
            if (!idleFidget || !(StateMachine.CurrentState is IdleState))
            {
                fidgetFlipped = false;
                return;
            }

            if (Time.time < nextFidgetTime)
                return;

            if (fidgetFlipped)
            {
                Movement.FlipFacing();
                fidgetFlipped = false;
                nextFidgetTime = Time.time + RandomRange(fidgetInterval) * rhythmScale;
                return;
            }

            if (Random.value <= fidgetChance)
            {
                Movement.FlipFacing();
                fidgetFlipped = true;
                nextFidgetTime = Time.time + RandomRange(fidgetHoldRange);
                return;
            }

            nextFidgetTime = Time.time + RandomRange(fidgetInterval) * rhythmScale;
        }

        private static float RandomRange(Vector2 range)
        {
            return Random.Range(Mathf.Min(range.x, range.y), Mathf.Max(range.x, range.y));
        }

        // ------------------------------------------------------------
        // Decisions
        // ------------------------------------------------------------

        protected override void TickBrain()
        {
            if (Actor.HasTarget)
                UpdateEngagement();

            if (!Actor.IsInCombat)
            {
                UpdateIdleFidget();
                return;
            }

            if (IsForcedState())
                return;

            if (!Actor.HasTarget)
            {
                // Walking to the last known position, or already looking around: wait.
                if (!chase.HasLastKnownPosition && !IsDecisionLocked()
                    && !(StateMachine.CurrentState is SearchState))
                {
                    EvaluateNextAction();
                }
                return;
            }

            chase.UpdateLastKnownPosition(Actor.Target.position);

            float distance = Movement.GetAbsAxisDistance(transform.position, Actor.Target.position);
            if (distance > pursuitRadius)
            {
                Perception?.ClearTarget();
                return;
            }

            // Just got up: stand and face the player, then decide.
            if (StateMachine.CurrentState is IdleState)
            {
                if (Time.time < getUpRecoverUntil)
                {
                    Movement.FaceTarget(Actor.Target.position);
                    return;
                }

                EvaluateNextAction(true);
                return;
            }

            UpdatePendingRecoil();
            UpdateBlocked();

            if (Time.time < nextDecisionTime)
                return;

            nextDecisionTime = Time.time
                + (decisionInterval + Random.Range(-decisionJitter, decisionJitter)) * rhythmScale;

            if (IsDecisionLocked())
                return;

            IState current = StateMachine.CurrentState;
            if (current is HoldPositionState || current is ChaseState)
                EvaluateNextAction();
        }

        protected override void EvaluateNextAction(bool actionCompleted = false)
        {
            if (!Actor.IsAlive || StateMachine.CurrentState is DeadState)
                return;
            if (!actionCompleted && IsDecisionLocked())
                return;

            // The launch just finished its get-up (called from inside LaunchedState):
            // a short standing recovery before any move, so a follow-up hit does not
            // cut into a run that had only just started.
            if (StateMachine.CurrentState is LaunchedState && Actor.HasTarget && getUpRecoverTime > 0f)
            {
                getUpRecoverUntil = Time.time + getUpRecoverTime;
                ChangeState<IdleState>();
                return;
            }

            if (!Actor.HasTarget)
            {
                if (chase.HasLastKnownPosition)
                {
                    ChangeState<ChaseState>();
                    return;
                }

                if (StateMachine.CurrentState is SearchState)
                    return;

                if (searchPending)
                {
                    searchPending = false;
                    ChangeState<SearchState>();
                    return;
                }

                ReturnToPassive();
                return;
            }

            EnemyEngagementDirector.Register(Actor);
            if (!EnemyEngagementDirector.TryGetAssignment(Actor, out EnemyEngagementAssignment assignment)
                || !EnemyEngagementDirector.TryGetLaneSnapshot(Actor, out EnemyLaneSnapshot lane))
            {
                ChangeState<ChaseState>();
                return;
            }

            UpdateGroupMood(lane);

            float distance = Movement.GetAbsAxisDistance(transform.position, Actor.Target.position);
            bool holding = StateMachine.CurrentState is HoldPositionState;

            if (lane.IsFront)
                EvaluateFront(lane, distance, holding);
            else
                EvaluateQueued(lane, assignment, distance, holding);
        }

        private void EvaluateFront(EnemyLaneSnapshot lane, float distance, bool holding)
        {
            float stopDistance = StopDistance;

            if (distance <= stopDistance)
            {
                if (TryStartPressureResponse(lane, distance))
                    return;

                if (TryAcquireAttackPermission())
                    ChangeState<MeleeAttackState>();
                else
                    ChangeState<HoldPositionState>();
                return;
            }

            // Pursue: the player is running, so close in directly.
            if (!engaged && distance > stopDistance + PursueChaseSlack)
            {
                ChangeState<ChaseState>();
                return;
            }

            // Pacing just outside reach: only step in once the lease is ours, so the
            // step reads as intent and the swing follows on arrival.
            float paceSlack = Mathf.Max(0f, holdOffset) + holdTolerance + paceAmplitude + 0.2f;
            if (holding && distance <= stopDistance + paceSlack)
            {
                if (TryAcquireAttackPermission())
                    ChangeState<ChaseState>();
                return;
            }

            ChangeState<ChaseState>();
        }

        private void EvaluateQueued(
            EnemyLaneSnapshot lane,
            EnemyEngagementAssignment assignment,
            float distance,
            bool holding)
        {
            // A flinch in progress (or about to start) owns the slot behaviour.
            if (IsRecoiling)
            {
                ChangeState<HoldPositionState>();
                return;
            }

            if (TryStartVault(lane, distance))
                return;

            // Measure against where the body ahead really is, not its ideal slot,
            // or a lagging leader leaves this grunt "not arrived" forever.
            Vector3 slot = assignment.Destination;
            float ignoredSpeed = 0f;
            ApplySpacing(ref slot, ref ignoredSpeed);

            float slotDistance = Movement.GetAbsAxisDistance(transform.position, slot);
            float leaveThreshold = holding && engaged
                ? holdTolerance + paceAmplitude + Mathf.Abs(holdOffset) + 0.6f
                : holdTolerance;

            if (slotDistance <= leaveThreshold)
                ChangeState<HoldPositionState>();
            else
                ChangeState<ChaseState>();
        }

        private void UpdateGroupMood(EnemyLaneSnapshot lane)
        {
            laneHasActiveAttacker = lane.HasActiveAttacker;

            float quiet = Mathf.Min(lane.GroupQuietSeconds, Time.time - combatStartTime);
            float handoff = normalHandoffDelay;

            if (lane.GroupPressure >= groupWaryPressure)
            {
                paceBias = -1f;
                holdOffset = waryHoldOffset;
                moodAllowsFeint = false;
            }
            else if (quiet >= boldAfterQuietSeconds)
            {
                paceBias = 1f;
                holdOffset = boldHoldOffset;
                moodAllowsFeint = true;
                handoff = boldHandoffDelay;
            }
            else
            {
                paceBias = 0f;
                holdOffset = 0f;
                moodAllowsFeint = true;
            }

            EnemyEngagementDirector.SetAttackHandoffDelay(Actor, handoff);
        }

        /// <summary>
        /// Engage while the player is planted (slow along the lane), attacking or
        /// parrying; Pursue while it keeps moving. Timers give hysteresis so a
        /// single step does not flip the whole pack.
        /// </summary>
        private void UpdateEngagement()
        {
            if (targetController == null && targetState == null)
            {
                engaged = true;
                return;
            }

            float speed = targetController != null
                ? Mathf.Abs(Vector3.Dot(targetController.Velocity, Movement.MovementAxis))
                : 0f;
            bool busy = targetState != null && (targetState.IsAttacking || targetState.IsParrying);
            bool wantsEngaged = busy || speed <= plantedSpeed;

            if (wantsEngaged == engaged)
            {
                modeTimer = 0f;
                return;
            }

            modeTimer += Time.deltaTime;
            if (modeTimer >= (wantsEngaged ? engageDelay : pursueDelay))
            {
                engaged = wantsEngaged;
                modeTimer = 0f;
            }
        }

        // ------------------------------------------------------------
        // Lane spacing
        // ------------------------------------------------------------

        public void ApplySpacing(ref Vector3 destination, ref float speed)
        {
            if (!TryGetNeighbors(out EnemyLaneNeighbors n))
                return;

            float desired = n.Side * Vector3.Dot(destination - n.TargetPosition, n.Axis);
            float approachMin = n.HasLeader ? n.LeaderDistance + n.LeaderComfortSpacing : float.NegativeInfinity;
            float hardMin = n.HasLeader ? n.LeaderDistance + n.LeaderHardSpacing : float.NegativeInfinity;
            float hardMax = n.HasFollower ? n.FollowerDistance - n.FollowerHardSpacing : float.PositiveInfinity;

            float clamped = LaneSpacingSolver.ClampDistance(desired, n.MyDistance, approachMin, hardMin, hardMax);

            float leaderClosing = n.HasLeader ? -n.Side * Vector3.Dot(n.LeaderVelocity, n.Axis) : 0f;
            float followerReceding = n.HasFollower ? n.Side * Vector3.Dot(n.FollowerVelocity, n.Axis) : 0f;
            speed = LaneSpacingSolver.LimitSpeed(
                speed,
                n.MyDistance,
                clamped,
                approachMin,
                leaderClosing,
                hardMax,
                followerReceding,
                spacingGain);

            destination += n.Axis * (n.Side * (clamped - desired));
        }

        private bool TryGetNeighbors(out EnemyLaneNeighbors result)
        {
            if (neighborsFrame != Time.frameCount)
            {
                neighborsFrame = Time.frameCount;
                neighborsValid = EnemyEngagementDirector.TryGetLaneNeighbors(Actor, contactGap, out neighbors);
                if (neighborsValid)
                {
                    UpdateProbe(neighbors);
                    neighbors = WithProbeLeader(neighbors);
                }
            }

            result = neighbors;
            return neighborsValid;
        }

        /// <summary>
        /// One overlap box in front of the body, toward the player, at the probe
        /// cadence. Finds the nearest edge of anything solid the director cannot
        /// see: an idle, stunned or downed enemy, a wall, a door blocker.
        /// </summary>
        private void UpdateProbe(in EnemyLaneNeighbors n)
        {
            if (Time.time < nextProbeTime)
                return;

            nextProbeTime = Time.time + probeInterval;
            probeTime = Time.time;
            probeHasHit = false;
            probeEnemy = null;
            if (probeDistance <= 0f)
                return;

            Vector3 toward = n.Axis * -n.Side;
            Vector3 eye = transform.position; // the root sits at the body centre
            Vector3 center = eye + toward * (myHalfWidth + probeDistance * 0.5f);
            bool alongX = Mathf.Abs(n.Axis.x) >= Mathf.Abs(n.Axis.z);
            Vector3 half = alongX
                ? new Vector3(probeDistance * 0.5f, 0.6f, 0.4f)
                : new Vector3(0.4f, 0.6f, probeDistance * 0.5f);

            int count = Physics.OverlapBoxNonAlloc(center, half, overlapBuffer, Quaternion.identity, probeMask, QueryTriggerInteraction.Ignore);
            float best = float.NegativeInfinity;
            for (int i = 0; i < count; i++)
            {
                Collider hit = overlapBuffer[i];
                EnemyCharacter other = hit.GetComponentInParent<EnemyCharacter>();
                if (other == Actor)
                    continue;
                if (other != null)
                {
                    if (!other.IsAlive)
                        continue;
                    IState otherState = other.StateMachine != null ? other.StateMachine.CurrentState : null;
                    if (otherState is LeapState || otherState is LaunchedState)
                        continue;
                }

                Vector3 edge = hit.ClosestPoint(eye);
                float edgeDistance = n.Side * Vector3.Dot(edge - n.TargetPosition, n.Axis);
                if (edgeDistance > n.MyDistance || edgeDistance <= best)
                    continue;

                best = edgeDistance;
                probeEnemy = other;
                probeVelocity = other != null && other.Movement != null ? other.Movement.Velocity : Vector3.zero;
            }

            probeHasHit = !float.IsNegativeInfinity(best);
            probeEdgeDistance = best;
        }

        /// <summary>The probe hit becomes the leader when it is nearer than the one the director knows.</summary>
        private EnemyLaneNeighbors WithProbeLeader(in EnemyLaneNeighbors n)
        {
            if (!probeHasHit)
                return n;

            float comfort = myHalfWidth + queueGap;
            float hard = myHalfWidth + contactGap;
            if (n.HasLeader && n.LeaderDistance + n.LeaderComfortSpacing >= probeEdgeDistance + comfort)
                return n;

            return new EnemyLaneNeighbors(
                n.Side, n.Axis, n.TargetPosition, n.MyDistance,
                true, probeEdgeDistance, comfort, hard, probeVelocity,
                n.HasFollower, n.FollowerDistance, n.FollowerHardSpacing, n.FollowerVelocity);
        }

        /// <summary>
        /// Commanded to move but not moving: something the spacing could not see is
        /// in the way. Re-evaluate now and wake the grunt standing there (an idle
        /// grunt only joins once its own sensor sees the player).
        /// </summary>
        private void UpdateBlocked()
        {
            IState current = StateMachine.CurrentState;
            bool wantsMove = (current is ChaseState || current is HoldPositionState)
                && Movement.IsMoving
                && Movement.CurrentSpeed > 1f;
            float axisSpeed = Mathf.Abs(Vector3.Dot(Movement.Velocity, Movement.MovementAxis));

            if (!wantsMove || axisSpeed >= blockedSpeed)
            {
                blockedTimer = 0f;
                isBlocked = false;
                return;
            }

            blockedTimer += Time.deltaTime;
            if (blockedTimer < blockedAfter)
                return;

            if (!isBlocked)
            {
                isBlocked = true;
                nextDecisionTime = Time.time;
            }

            if (Time.time >= nextPokeTime)
            {
                nextPokeTime = Time.time + 0.5f;
                EnemyCharacter other = Time.time - probeTime <= 0.5f ? probeEnemy : null;
                if (other != null && !other.IsInCombat && other.Perception != null)
                    other.Perception.RefreshOverlap();
            }
        }

        // ------------------------------------------------------------
        // Ally-hit reaction
        // ------------------------------------------------------------

        public void OnAllyHit(EnemyCharacter ally, int placesBehind)
        {
            // Called from inside the director: record only, no state changes here.
            if (!allyHitReaction || placesBehind <= 0 || placesBehind > allyHitMaxPlacesBehind)
                return;
            if (!Actor.IsAlive || IsForcedState() || IsDecisionLocked())
                return;

            float now = Time.time;
            float delay = placesBehind * rippleDelay + Random.Range(0f, rippleJitter);
            float hold = Random.Range(
                Mathf.Min(recoilHoldRange.x, recoilHoldRange.y),
                Mathf.Max(recoilHoldRange.x, recoilHoldRange.y));

            bool alreadyBack = IsRecoiling;
            recoilHoldUntil = Mathf.Max(recoilHoldUntil, now + delay + hold);

            // During a combo, stay back instead of flinching again on every hit.
            if (!alreadyBack)
                pendingRecoilTime = now + delay;
        }

        public bool TryConsumeRecoil(out float distance)
        {
            if (pendingRecoilTime >= 0f && Time.time >= pendingRecoilTime)
            {
                pendingRecoilTime = -1f;
                distance = recoilDistance;
                return true;
            }

            distance = 0f;
            return false;
        }

        private void UpdatePendingRecoil()
        {
            if (pendingRecoilTime < 0f || Time.time < pendingRecoilTime)
                return;

            IState current = StateMachine.CurrentState;
            if (current is ChaseState)
            {
                // Still walking up: stop and flinch from here.
                ChangeState<HoldPositionState>();
            }
            else if (!(current is HoldPositionState) && Time.time > pendingRecoilTime + StaleRecoilWindow)
            {
                // Busy (attacking, stunned) when it was due: the moment has passed.
                pendingRecoilTime = -1f;
            }
        }

        private void ClearReactions()
        {
            pendingRecoilTime = -1f;
            recoilHoldUntil = 0f;
        }

        // ------------------------------------------------------------
        // Vault
        // ------------------------------------------------------------

        private bool TryStartVault(EnemyLaneSnapshot lane, float distance)
        {
            if (!vaultEnabled || !engaged || IsRecoiling)
                return false;
            if (lane.QueueIndex != 1 || lane.CountFarSide > vaultMaxFarSideCount)
                return false;
            if (distance > vaultMaxStartDistance || Time.time < nextVaultAllowed)
                return false;
            if (targetState != null && (targetState.IsRolling || !targetState.IsGrounded))
                return false;

            vaultSide = -lane.Side;
            vaultGroundY = transform.position.y;
            if (!TryComputeVaultLanding(out Vector3 destination) || !IsLeapLandingValid(destination))
                return false;

            if (Random.value > vaultChance)
            {
                // Hesitate briefly instead of re-rolling every tick.
                nextVaultAllowed = Time.time + 0.5f;
                return false;
            }

            if (!EnemyEngagementDirector.TryReserveLeap(Actor, groupLeapCooldown))
                return false;

            nextVaultAllowed = Time.time + vaultCooldown;
            vaultAnchor = Actor.Target.position;
            StartLeap(destination, false);
            return true;
        }

        /// <summary>
        /// Landing point just outside attack range on the vault side of the player's
        /// current position, at take-off ground height and this grunt's lane depth.
        /// </summary>
        private bool TryComputeVaultLanding(out Vector3 landing)
        {
            landing = default;
            if (!Actor.HasTarget)
                return false;

            Vector3 laneAxis = Mathf.Abs(Movement.MovementAxis.x) >= Mathf.Abs(Movement.MovementAxis.z)
                ? Vector3.right
                : Vector3.forward;
            Vector3 depthAxis = Vector3.Cross(Vector3.up, laneAxis);

            landing = Actor.Target.position + laneAxis * (vaultSide * (StopDistance + vaultOvershoot));
            landing.y = vaultGroundY;
            landing += depthAxis * (Vector3.Dot(transform.position, depthAxis) - Vector3.Dot(landing, depthAxis));
            return true;
        }

        private bool TryStartFallBack(EnemyLaneSnapshot lane)
        {
            if (lane.CountThisSide <= 1 || Time.time < nextFallBackAllowed)
                return false;
            if (!EnemyEngagementDirector.TryGetBackSlot(Actor, lane.Side, out Vector3 slot))
                return false;

            Vector3 away = slot - Actor.Target.position;
            away.y = 0f;
            if (away.sqrMagnitude > 0.0001f)
                away.Normalize();

            Vector3 destination = slot + away * 0.2f;
            if (!IsLeapLandingValid(destination))
                return false;
            if (!EnemyEngagementDirector.TryReserveLeap(Actor, groupLeapCooldown))
                return false;

            nextFallBackAllowed = Time.time + fallBackCooldown;
            StartLeap(destination, true);
            return true;
        }

        private void StartLeap(Vector3 destination, bool fallBack)
        {
            leapDestination = destination;
            leapIsFallBack = fallBack;
            if (fallBack)
                EnemyEngagementDirector.SetYielding(Actor, true);

            ReleaseAttackPermission();
            ChangeState<LeapState>();
        }

        public void RefreshLeapDestination()
        {
            if (leapIsFallBack || !TryComputeVaultLanding(out Vector3 landing))
                return;

            // Keep the last good landing if the new one is behind a wall or over a gap.
            Vector3 from = Actor.Target.position;
            if (!IsSegmentClear(from, landing) || !HasGroundAt(landing))
                return;

            leapDestination = landing;
        }

        public bool ShouldAbortLeap()
        {
            if (!Actor.HasTarget)
                return true;
            if (leapIsFallBack)
                return false;
            if (!engaged)
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

            // Wall, door blocker, or ledge between me and the landing point.
            if (!IsSegmentClear(transform.position, destination) || !HasGroundAt(destination))
                return false;

            // Another enemy body already standing there.
            int enemiesMask = LayerMask.GetMask("Enemies");
            int count = Physics.OverlapSphereNonAlloc(
                destination + Vector3.up * 1f,
                0.45f,
                overlapBuffer,
                enemiesMask,
                QueryTriggerInteraction.Ignore);
            for (int i = 0; i < count; i++)
            {
                EnemyCharacter other = overlapBuffer[i].GetComponentInParent<EnemyCharacter>();
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

        private bool HasGroundAt(Vector3 point)
        {
            return Physics.Raycast(point + Vector3.up * 0.5f, Vector3.down, 2.5f, resolvedObstacleLayers, QueryTriggerInteraction.Ignore);
        }

        public void OnLeapComplete()
        {
            EnemyEngagementDirector.SetYielding(Actor, false);
            if (!Actor.IsAlive)
                return;

            EvaluateNextAction(true);
        }

        public void OnLeapAborted()
        {
            EnemyEngagementDirector.SetYielding(Actor, false);
            nextVaultAllowed = Time.time + vaultAbortCooldown;
            if (!Actor.IsAlive)
                return;

            EvaluateNextAction(true);
        }

        public void OnLeapCancelled()
        {
            // Called from LeapState.Exit inside a state change: bookkeeping only.
            EnemyEngagementDirector.SetYielding(Actor, false);
        }

        // ------------------------------------------------------------
        // Pressure
        // ------------------------------------------------------------

        private bool TryStartPressureResponse(EnemyLaneSnapshot lane, float distance)
        {
            float current = Pressure01;
            if (current <= 0f)
                return false;

            if (current >= 1f && TryStartFallBack(lane))
                return true;

            if (current < backOffPressure || Time.time < nextBackOffAllowed)
                return false;

            float limit = EnemyEngagementDirector.GetBackOffLimit(Actor);
            float desired = Mathf.Min(StopDistance + backOffDistance, limit);
            if (desired - distance < 0.3f)
                return false;

            pendingBackOffDistance = desired;
            nextBackOffAllowed = Time.time + backOffCooldown;
            ReleaseAttackPermission();
            ChangeState<BackOffState>();
            return true;
        }

        public void OnBackOffComplete()
        {
            if (!Actor.IsAlive)
                return;

            EvaluateNextAction(true);
        }

        private void HandleDamageResolved(DamageResult result, DamageRequest request)
        {
            if (!result.WasApplied || request.IsTickDamage)
                return;

            // Struck while still noticing: no beat, straight to combat (before the stun lands).
            if (alertPending)
                OnAlertComplete();

            // The grunt being hit reacts through stun and pressure, not a flinch.
            ClearReactions();
            pressure.Record(Time.time);
            EnemyEngagementDirector.ReportHitTaken(Actor);
            EnemyEngagementDirector.NotifyAllyHit(Actor);
        }

        // ------------------------------------------------------------

        private float StopDistance => chase.ChaseStopDistance > 0f
            ? chase.ChaseStopDistance
            : Actor.AttackRange;

#if UNITY_EDITOR
        protected override void OnDrawGizmosSelected()
        {
            base.OnDrawGizmosSelected();

            Gizmos.color = new Color(0.4f, 0.8f, 1f, 0.25f);
            Gizmos.DrawWireSphere(transform.position, vaultMaxStartDistance);

            if (!Application.isPlaying || StateMachine == null)
                return;

            if (StateMachine.CurrentState is LeapState)
            {
                Gizmos.color = Color.cyan;
                Gizmos.DrawLine(transform.position, leapDestination);
                Gizmos.DrawWireSphere(leapDestination, 0.25f);
            }

            Gizmos.color = engaged ? Color.red : Color.yellow;
            Gizmos.DrawWireCube(transform.position + Vector3.up * 2.6f, Vector3.one * 0.2f);
        }
#endif
    }
}
