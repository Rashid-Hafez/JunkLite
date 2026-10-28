using System.Collections;
using System.Collections.Generic;
using System;
using UnityEngine;
using UnityEngine.Serialization;

namespace junklite
{
    /// <summary>
    /// Base class for all enemy characters.
    /// 
    /// ARCHITECTURE:
    /// - This actor contains universal enemy identity, damage, death, and lifecycle
    /// - EnemyPerception reports target facts without choosing behavior
    /// - EnemyBrain owns voluntary decisions for migrated enemies
    /// - States execute actions using capabilities supplied by actors or composed brains
    /// 
    /// UNIVERSAL (lives here):
    /// - Perception reference and combat participation
    /// - Combat state management
    /// - Death handling, VFX basics
    /// - State machine reference
    /// 
    /// CAPABILITY-SPECIFIC (lives in composed behaviors or legacy enemy subclasses):
    /// - Patrol, Dash, Grab, Melee, Dodge, Chase, Ranged, Stun, etc.
    /// </summary>

    public enum EnemyType
    {
        Dummy,
        Robot,
        Hyena,
        FlyingDummy,
        Grunt,
        Brute
    }

    [RequireComponent(typeof(StateMachine))]
    [RequireComponent(typeof(EnemyMovement))]
    [RequireComponent(typeof(StatusEffectHandler))]
    public class EnemyCharacter : EnemyBase, IStatusEffectTarget
    {
        [Header("Enemy Config")]
        [SerializeField] protected EnemyConfig config;
        [Header("Audio")]
        [SerializeField] private EnemySoundProfile soundProfile;

        [Header("Detection")]
        [SerializeField] protected EnemyPerception detectionZone;
        [SerializeField] protected float attackRange = 2f;
        [SerializeField] protected LayerMask targetLayer;

        [Header("Knockback")]
        [Tooltip("If false, this enemy cannot be knocked back")]
        [SerializeField] protected bool canBeKnockedBack = true;

        [Header("Launch (heavy hits)")]
        [Tooltip("Heavy hits marked Launch (combo finishers) throw this enemy into the air: flight, air kick, bounce, knockdown, get-up. Off = those hits are a normal flinch. Works for any enemy with a ground body (dynamic Rigidbody with gravity); every enemy brain already has the launch behaviour, so this checkbox is all a new type needs.")]
        [SerializeField] protected bool canBeLaunched = false;
        [SerializeField] protected EnemyLaunchSettings launch = new();

        [Header("Body")]
        [Tooltip("Walk through other enemies instead of being blocked by them (either side having this on is enough). Weapon hits, sensors, walls and floors are unaffected. Grunts keep their spacing by behaviour; a type without spacing logic may stack while crowding the player.")]
        [SerializeField] protected bool passThroughEnemies = false;

        [Header("Debug")]
        [SerializeField] protected bool showGizmos = true;

        [Header("Damage Flash VFX")]
        [SerializeField] protected DamageFlashUniversal damageFlashUniversal;
        private bool warnedMissingDamageFlash;

        [Header("Attack Warning")]
        [Tooltip("Optional GameObject to activate when entering melee attack (warning signal). Deactivated when leaving attack state.")]
        [SerializeField] protected GameObject attackWarningVfx;
        [Tooltip("Delay in seconds after entering melee attack before showing the notification. 0 = show immediately.")]
        [SerializeField] protected float attackNotifyDelay = 0f;

        [Header("Animation Presentation")]
        [FormerlySerializedAs("enemyAnimation")]
        [SerializeField] private EnemyAnimationPresenter animationPresenter;

        [Header("Death VFX")]
        [SerializeField] protected GameObject deathParticlePrefab;
        [SerializeField] protected float deathParticleLifetime = 2f;
        [SerializeField] protected GameObject enemyVisual;

        [Header("Drops")]
        [SerializeField] protected DropTable customDropTable;
        [SerializeField][Range(0f, 1f)] protected float dropChance = 1f;

        [Header("Death Sequence")]
        [Tooltip("Max seconds to wait for the corpse to touch the ground before forcing cleanup. Prevents a stuck corpse from lingering forever.")]
        [SerializeField] protected float deathGroundTimeout = 3f;
        [Tooltip("Seconds the corpse stays visible after it lands, so the death animation can finish playing before the body is removed.")]
        [SerializeField] protected float deathLinger = 1.25f;

        protected EnemyType enemyType;

        // Damage flash state
        private Coroutine attackNotifyCoroutine;
        private bool tutorialFrozen;
        private bool tutorialPreviousKinematic;
        private Rigidbody tutorialFrozenRigidbody;

        // Components
        protected StateMachine stateMachine;
        protected EnemyMovement movement;
        protected StatusEffectHandler statusEffects;
        private EnemyBrain brain;
        private bool isParryStunned;
        private bool statusCrowdControlled;
        private bool deathHandled;

        // Launch handoff: ReceiveDamage stores the throw, LaunchedState.Enter applies it.
        private bool hasPendingLaunch;
        private Vector3 pendingLaunchVelocity;
        private GameObject pendingLaunchSource;

        public bool IsParryStunned => isParryStunned;

        // Combat state - prevents detection events from interrupting combat
        protected bool isInCombat = false;

        // Current state reference for quick access
        protected EnemyStateBase state => stateMachine?.CurrentState as EnemyStateBase;

        public bool IsInCombat => isInCombat;

        // Public accessors - Components
        public EnemyConfig Config => config;
        public StateMachine StateMachine => stateMachine;
        public EnemyMovement Movement => movement;
        public EnemyPerception Perception => detectionZone;
        public EnemyPerception DetectionZone => detectionZone;
        public StatusEffectHandler StatusEffects => statusEffects;
        public EnemyBrain Brain => brain != null ? brain : brain = GetComponent<EnemyBrain>();
        public EnemyAnimationPresenter AnimationPresenter => animationPresenter;

        // Public accessors - Target
        public Transform Target => detectionZone != null ? detectionZone.TargetTransform : null;
        public PlayerCharacter TargetCharacter => detectionZone != null ? detectionZone.CurrentTarget : null;
        public float AttackRange => attackRange;
        public bool CanBeKnockedBack => canBeKnockedBack;
        public bool CanBeLaunched => canBeLaunched;
        public EnemyLaunchSettings LaunchSettings => launch;
        /// <summary>Airborne, bouncing, lying down or getting up after a launching hit.</summary>
        public bool IsLaunched => stateMachine != null && stateMachine.CurrentState is LaunchedState;

        // Computed properties - Target
        public bool HasTarget => detectionZone != null && detectionZone.HasTarget;
        public float DistanceToTarget => HasTarget ? Vector3.Distance(transform.position, Target.position) : float.MaxValue;
        public bool IsTargetInAttackRange => DistanceToTarget <= attackRange;
        public Vector3 DirectionToTarget => HasTarget ? (Target.position - transform.position).normalized : Vector3.zero;
        public EnemyType EnemyType => enemyType;
        public EnemySoundProfile SoundProfile => soundProfile;
        public bool IsTutorialFrozen => tutorialFrozen;

        public event Action<EnemyCharacter> OnAttackNotifyShown;
        public event Action<EnemyCharacter> Died;

        private static readonly List<EnemyCharacter> ActiveList = new();
        /// <summary>Every enabled enemy in the scene (for pack alerts and other cheap scans). Read only.</summary>
        public static IReadOnlyList<EnemyCharacter> ActiveEnemies => ActiveList;

        protected override void Awake()
        {
            base.Awake();
            stateMachine = GetComponent<StateMachine>();
            movement = GetComponent<EnemyMovement>();
            statusEffects = GetComponent<StatusEffectHandler>();
            statusEffects?.BindTarget(this);
            if (statusEffects != null)
            {
                var statusVisuals = GetComponent<StatusEffectVisuals>();
                if (statusVisuals == null) statusVisuals = gameObject.AddComponent<StatusEffectVisuals>();
                statusVisuals.Initialize(statusEffects, enemyVisual != null ? enemyVisual.transform : transform,
                    attackWarningVfx != null ? attackWarningVfx.transform : null);
            }
            brain = GetComponent<EnemyBrain>();

            if (detectionZone == null)
                detectionZone = GetComponentInChildren<EnemyPerception>(true);

            EnemyAnimationPresenter[] presenters = GetComponentsInChildren<EnemyAnimationPresenter>(true);
            if (animationPresenter == null && presenters.Length > 0)
                animationPresenter = presenters[0];

            if (presenters.Length > 1)
            {
                Debug.LogError(
                    $"[{gameObject.name}] Multiple enemy animation presenters found. Assign exactly one active presentation backend.",
                    this);
            }

            if (damageFlashUniversal == null)
                damageFlashUniversal = GetComponentInChildren<DamageFlashUniversal>(true);

            if (movement != null)
            {
                movement.IgnoreKnockback = !canBeKnockedBack;
                movement.OnKnockbackEnd += HandleKnockbackEnd;
            }

            if (detectionZone != null)
                detectionZone.TargetChanged += HandlePerceptionTargetChanged;

            if (damageable != null)
                damageable.OnDamageResolved += OnDamageResolvedVFX;

            RefreshEnemyBodyPassThrough();
        }

        protected override void Start()
        {
            base.Start();
            InitializeStateMachine();
        }

        protected virtual void OnEnable()
        {
            if (!ActiveList.Contains(this))
                ActiveList.Add(this);
            if (stateMachine != null)
                stateMachine.OnStateChanged += HandleAttackWarningStateChanged;
        }

        protected virtual void OnDisable()
        {
            ActiveList.Remove(this);
            if (stateMachine != null)
                stateMachine.OnStateChanged -= HandleAttackWarningStateChanged;
        }

        private void HandleAttackWarningStateChanged(IState from, IState to)
        {
            if (from is MeleeAttackState)
                HideAttackWarning();

            if (to is MeleeAttackState)
            {
                if (attackNotifyCoroutine != null)
                {
                    StopCoroutine(attackNotifyCoroutine);
                    attackNotifyCoroutine = null;
                }
                if (attackNotifyDelay <= 0f)
                    ShowAttackNotify();
                else
                    attackNotifyCoroutine = StartCoroutine(AttackNotifyAfterDelay());
            }
        }

        private void ShowAttackNotify()
        {
            if (attackWarningVfx != null)
                attackWarningVfx.SetActive(true);
            GetComponentInChildren<EnemyAudioHandler>()?.PlayAttackNotify();
            OnAttackNotifyShown?.Invoke(this);
        }

        /// <summary>
        /// Tutorial gate: while set, normal hits are rejected as Invulnerable (forced/bypass requests still land).
        /// </summary>
        public bool IsTutorialInvulnerable { get; private set; }

        public void SetTutorialInvulnerable(bool invulnerable) => IsTutorialInvulnerable = invulnerable;

        public void SetTutorialFrozen(bool frozen)
        {
            if (tutorialFrozen == frozen)
                return;

            tutorialFrozen = frozen;

            if (frozen)
            {
                stateMachine?.Pause();
                movement?.Stop();

                tutorialFrozenRigidbody = GetComponent<Rigidbody>();
                if (tutorialFrozenRigidbody != null)
                {
                    tutorialPreviousKinematic = tutorialFrozenRigidbody.isKinematic;
                    tutorialFrozenRigidbody.linearVelocity = Vector3.zero;
                    tutorialFrozenRigidbody.angularVelocity = Vector3.zero;
                    tutorialFrozenRigidbody.isKinematic = true;
                }

                animationPresenter?.SetPlaybackPaused(true);
            }
            else
            {
                animationPresenter?.SetPlaybackPaused(false);

                if (tutorialFrozenRigidbody != null)
                {
                    tutorialFrozenRigidbody.isKinematic = tutorialPreviousKinematic;
                    tutorialFrozenRigidbody = null;
                }

                stateMachine?.Resume();
            }
        }

        public void ShowAttackWarningImmediate()
        {
            if (attackNotifyCoroutine != null)
            {
                StopCoroutine(attackNotifyCoroutine);
                attackNotifyCoroutine = null;
            }
            ShowAttackNotify();
        }

        public void HideAttackWarning()
        {
            if (attackNotifyCoroutine != null)
            {
                StopCoroutine(attackNotifyCoroutine);
                attackNotifyCoroutine = null;
            }
            if (attackWarningVfx != null)
                attackWarningVfx.SetActive(false);
        }

        private System.Collections.IEnumerator AttackNotifyAfterDelay()
        {
            yield return new WaitForSeconds(attackNotifyDelay);
            attackNotifyCoroutine = null;
            if (stateMachine != null && stateMachine.CurrentState is MeleeAttackState)
                ShowAttackNotify();
        }

        protected virtual void InitializeStateMachine() { }

        #region Detection Zone Events

        private void HandlePerceptionTargetChanged(PlayerCharacter previous, PlayerCharacter current)
        {
            if (!isActiveAndEnabled || !IsAlive)
                return;

            if (current != null)
            {
                OnTargetAcquired();
            }
            else if (previous != null)
            {
                OnTargetLost();
            }

            // Migrated enemies let their brain consume perception directly. These
            // callbacks remain only for Robot/flying/dummy compatibility.
            if (Brain != null)
                return;

            if (current != null)
                OnDetectionZoneEnter(current);
            else if (previous != null)
                OnDetectionZoneExit(previous);
        }

        protected virtual void OnDetectionZoneEnter(PlayerCharacter player)
        {
            if (!IsAlive) return;
            OnPlayerSpotted();
        }

        protected virtual void OnDetectionZoneExit(PlayerCharacter player)
        {
            if (!IsAlive) return;
            OnPlayerLost();
        }

        #endregion

        #region Core Behavior Decisions - Override in subclasses

        public virtual void OnPlayerSpotted()
        {
        }

        public virtual void OnPlayerLost()
        {
            if (!IsAlive) return;
            stateMachine.ChangeState<IdleState>();
        }

        public virtual void OnPlayerInAttackRange()
        {
            OnPlayerSpotted();
        }

        public virtual void OnStunComplete()
        {
        }

        public virtual void OnAttackFinished()
        {
            if (HasTarget)
                OnPlayerSpotted();
            else
                OnPlayerLost();
        }

        /// <summary>
        /// Notifies the enemy that they were struck by a successful parry. Duration is the
        /// length of the stun effect the parry wants to enforce.
        ///
        /// Default behavior: enter ParriedState which locks the enemy for the duration.
        /// Subclasses can override to add VFX/animation but should call base.OnParryStunned
        /// so the state transition actually happens.
        /// </summary>
        public virtual void OnParryStunned(float duration)
        {
            if (duration > 0f)
            {
                isParryStunned = true;

                IStunnable stunnable = GetCapability<IStunnable>();
                if (stunnable != null)
                    stunnable.ForcedStunDuration = duration;

                stateMachine.ChangeState<ParriedState>();
            }
        }

        /// <summary>
        /// Called when the parry stun duration expires.
        /// Default behavior: return to normal behavior via OnStunComplete.
        /// Override in subclasses for custom post-parry behavior.
        /// </summary>
        public virtual void OnParryComplete()
        {
            IStunnable stunnable = GetCapability<IStunnable>();
            if (stunnable != null)
                stunnable.OnStunComplete();
            else
                OnStunComplete();
        }

        public void ClearParryStunFlag()
        {
            isParryStunned = false;
        }

        /// <summary>
        /// Apply a gradual push from a parry deflection. The raw world-space direction
        /// is projected onto the enemy's movement plane by EnemyMovement.
        /// </summary>
        public virtual void ApplyParryPush(Vector3 direction, float force, float upwardForce, float duration)
        {
            if (!canBeKnockedBack) return;
            if (movement != null)
                movement.ApplyPushOverTime(direction, force, upwardForce, duration);
        }

        #endregion

        #region Combat State

        public virtual void EnterCombat()
        {
            if (isInCombat)
                return;

            isInCombat = true;
            PlayerCombatTracker.Instance?.NotifyEnemyEnteredCombat(this);
        }

        public virtual void ExitCombat()
        {
            if (!isInCombat)
                return;

            isInCombat = false;
            PlayerCombatTracker.Instance?.NotifyEnemyExitedCombat(this);
        }

        #endregion

        #region Target Management

        public virtual void SetTarget(PlayerCharacter newTarget)
        {
            if (newTarget == null)
                ClearTarget();
        }

        public virtual void ClearTarget()
        {
            detectionZone?.ClearTarget();
        }

        protected virtual void OnTargetAcquired() { }
        protected virtual void OnTargetLost() { }

        /// <summary>
        /// Resolves a reusable capability from either the legacy enemy subclass or
        /// a composed brain component. Lookup happens only at action boundaries.
        /// </summary>
        public T GetCapability<T>() where T : class
        {
            if (this is T legacyCapability)
                return legacyCapability;

            MonoBehaviour[] components = GetComponents<MonoBehaviour>();
            foreach (MonoBehaviour component in components)
            {
                if (component is T capability)
                    return capability;

                if (component is IEnemyCapabilityProvider provider
                    && provider.TryGetCapability(out T nestedCapability))
                {
                    return nestedCapability;
                }
            }

            return null;
        }

        #endregion

        #region Collision Helpers

        // Pass-through: the colliders this enemy excluded the Enemies layer on, so
        // restoring never clears a bit someone else set. On while the Body toggle is
        // on, or while launched.
        private readonly List<Collider> passThroughColliders = new();
        private bool launchPassThrough;

        public bool PassesThroughEnemies => passThroughEnemies;

        /// <summary>
        /// Launch-time pass-through (a launched body flies through the enemy behind it).
        /// The Body toggle keeps it on permanently. A corpse keeps its own death exclusion.
        /// </summary>
        public void SetEnemyBodyPassThrough(bool passThrough)
        {
            launchPassThrough = passThrough;
            RefreshEnemyBodyPassThrough();
        }

        private void RefreshEnemyBodyPassThrough()
        {
            bool passThrough = passThroughEnemies || launchPassThrough;
            if (passThrough)
            {
                if (passThroughColliders.Count > 0)
                    return;

                int enemyLayers = LayerMask.GetMask("Enemies");
                Collider[] colliders = GetComponentsInChildren<Collider>(true);
                foreach (Collider bodyCollider in colliders)
                {
                    if (bodyCollider == null || bodyCollider.isTrigger)
                        continue;
                    if ((bodyCollider.excludeLayers.value & enemyLayers) == enemyLayers)
                        continue;

                    bodyCollider.excludeLayers |= enemyLayers;
                    passThroughColliders.Add(bodyCollider);
                }
                return;
            }

            if (deathHandled)
            {
                passThroughColliders.Clear();
                return;
            }

            int mask = LayerMask.GetMask("Enemies");
            for (int i = 0; i < passThroughColliders.Count; i++)
            {
                if (passThroughColliders[i] != null)
                    passThroughColliders[i].excludeLayers &= ~mask;
            }
            passThroughColliders.Clear();
        }

        private void IgnoreEnemyBodyCollisionsOnDeath()
        {
            int enemyLayers = LayerMask.GetMask("Enemies");
            Collider[] colliders = GetComponentsInChildren<Collider>(true);
            foreach (Collider bodyCollider in colliders)
            {
                if (bodyCollider == null || bodyCollider.isTrigger)
                    continue;

                // Keep the corpse solid against the level while other enemies
                // can pass through it during the landing/death animation.
                bodyCollider.excludeLayers |= enemyLayers;
            }
        }

        #endregion

        #region Combat & Death

        /// <summary>
        /// Knockback and stagger are applied together so the enemy is visually
        /// pushed back at the same moment it enters hitstun.
        /// Knockback is applied FIRST so the velocity is already active when
        /// StunnedState.Enter() runs — StunnedState skips Stop() while knockback
        /// is in flight, preserving the push.
        /// </summary>
        public override DamageResult ReceiveDamage(DamageRequest request)
        {
            if ((state != null && !state.CanTakeDamage) ||
                (IsTutorialInvulnerable && !request.BypassesDefenses))
                return DamageResult.Rejected(DamageOutcome.Invulnerable, request.Amount);

            // Being attacked draws attention before the hit resolves, so an unaware
            // enemy acquires its target (and the brain can skip its alert beat) before
            // the damage callbacks and the launch run.
            if (IsAlive && !request.IsTickDamage && detectionZone != null)
                detectionZone.Alert();

            DamageResult result = base.ReceiveDamage(request);

            if (result.WasApplied)
            {
                if (state is LaunchedState launched && !launched.CanBeInterrupted)
                {
                    // Airborne or downed: the launch owns the reaction (juggle, stay down).
                    // Only real hits move the body: damage-over-time ticks (burn, shock)
                    // and reaction-less hits must not juggle or cut the arc.
                    if (!request.IsTickDamage && request.HitReaction.HasAnyReaction)
                        launched.OnHitWhileLaunched(request, ResolveLaunchVelocity(request));
                }
                else if (state == null || state.CanBeInterrupted)
                {
                    if (request.HitReaction.IsLaunch && canBeLaunched && movement != null && movement.CanBeLaunched)
                    {
                        if (!IsAlive)
                        {
                            // A launching kill sends the corpse flying; DeathSequence waits for the ground.
                            Vector3 corpseVelocity = ResolveLaunchVelocity(request);
                            corpseVelocity.y = launch.LaunchUpSpeed(corpseVelocity.y);
                            movement.Launch(corpseVelocity, launch.GravityScale, endOnLanding: true);
                            return result;
                        }

                        if (TryBeginLaunch(request))
                            return result;
                    }

                    // A spike on a grounded enemy, or a launch on one that cannot be launched
                    // (Hyena, Robot, Brute, dummies), is a normal hit pushed sideways only: the
                    // launch's knockback is mostly upward and would throw it into the air.
                    if (request.HitReaction.IsSpike || request.HitReaction.IsLaunch)
                        request.KnockbackForce = new Vector2(request.KnockbackForce.x, 0f);

                    // 1. Knockback FIRST — starts the push immediately
                    if (request.HitReaction.HasKnockback)
                        ApplyKnockback(request);

                    // 2. Stagger SECOND — enters StunnedState (which preserves active knockback)
                    if (request.HitReaction.HasHitstun)
                        ApplyHitstun(request.Source, request.HitReaction);
                }
            }

            return result;
        }

        /// <summary>
        /// Launch velocity for a hit: x along the movement axis away from the source,
        /// y up. With no source the enemy flies away from where it is facing.
        /// </summary>
        private Vector3 ResolveLaunchVelocity(DamageRequest request)
        {
            Vector3 axis = movement != null ? movement.MovementAxis : Vector3.right;
            float sign = PushSign(request, axis);

            Vector2 force = request.KnockbackForce;
            return axis * (sign * force.x) + Vector3.up * force.y;
        }

        /// <summary>
        /// Which way along the lane a hit pushes: the attacker's swing when the hit
        /// carries one (melee), otherwise away from the source's position, otherwise
        /// away from where this enemy faces.
        /// </summary>
        private float PushSign(DamageRequest request, Vector3 axis)
        {
            float along = Vector3.Dot(request.HitReaction.PushDirection, axis);
            if (Mathf.Abs(along) > 0.01f)
                return Mathf.Sign(along);

            if (request.Source != null)
            {
                along = Vector3.Dot(transform.position - request.Source.transform.position, axis);
                if (Mathf.Abs(along) > 0.001f)
                    return Mathf.Sign(along);
            }

            return movement != null ? -movement.FacingDirection : 1f;
        }

        /// <summary>Stores the throw and enters LaunchedState. False when this enemy has no launch state.</summary>
        private bool TryBeginLaunch(DamageRequest request)
        {
            if (stateMachine == null || !stateMachine.HasState<LaunchedState>())
                return false;

            hasPendingLaunch = true;
            pendingLaunchVelocity = ResolveLaunchVelocity(request);
            pendingLaunchSource = request.Source;
            stateMachine.ChangeState<LaunchedState>();
            return true;
        }

        public bool TryConsumePendingLaunch(out Vector3 velocity, out GameObject source)
        {
            velocity = pendingLaunchVelocity;
            source = pendingLaunchSource;
            if (!hasPendingLaunch)
                return false;

            hasPendingLaunch = false;
            pendingLaunchSource = null;
            return true;
        }

        /// <summary>LaunchedState finished its get-up: hand the decision back to the brain.</summary>
        public void OnLaunchFinished()
        {
            if (!IsAlive || stateMachine == null)
                return;

            // A stun that landed while airborne was deferred; apply it now.
            if (statusCrowdControlled && stateMachine.HasState<StunnedState>() && GetCapability<IStunnable>() != null)
            {
                stateMachine.ChangeState<StunnedState>();
                return;
            }

            IStunnable stunnable = GetCapability<IStunnable>();
            if (stunnable != null)
                stunnable.OnStunComplete();
            else
                OnStunComplete();
        }

        public virtual void ApplyStun(float duration)
        {
            if (statusEffects != null)
            {
                if (duration <= 0f)
                    statusEffects.Remove(StatusEffectType.Stun);
                else
                    statusEffects.ApplyStun(duration, gameObject);
                return;
            }

            if (stateMachine == null) return;

            IStunnable stunnable = GetCapability<IStunnable>();
            if (stunnable != null)
                stunnable.ForcedStunDuration = duration;

            stateMachine.ChangeState<StunnedState>();
        }

        protected virtual void ApplyHitstun(GameObject source, HitReactionRequest reaction)
        {
            if (!IsAlive) return;

            IStunnable stunnable = GetCapability<IStunnable>();
            if (stunnable == null) return;

            float duration = reaction.ResolveHitstunDuration(stunnable.StaggerDuration);
            if (duration <= 0f) return;

            if (statusEffects != null)
            {
                statusEffects.ApplyHitstun(duration, source);
                return;
            }

            // Already stunned? Reset timer for combo extension
            if (stateMachine.CurrentState is StunnedState stunned)
            {
                stunned.ResetTimer();
                return;
            }

            stateMachine.ChangeState<StunnedState>();
        }

        public void ApplyStatusEffectSnapshot(StatusEffectSnapshot snapshot)
        {
            movement?.SetStatusMoveSpeedMultiplier(snapshot.MoveSpeedMultiplier);

            bool shouldCrowdControl = snapshot.IsCrowdControlled;
            bool crowdControlStarted = shouldCrowdControl && !statusCrowdControlled;
            statusCrowdControlled = shouldCrowdControl;

            if (!crowdControlStarted || !IsAlive || stateMachine == null)
                return;

            // Airborne or downed: the launch finishes first, then OnLaunchFinished applies the stun.
            if (state is LaunchedState launched && !launched.CanBeInterrupted)
                return;

            movement?.Stop();

            if (stateMachine.HasState<StunnedState>() && GetCapability<IStunnable>() != null)
                stateMachine.ChangeState<StunnedState>();
        }

        /// <summary>
        /// Knockback end callback. Skips while in StunnedState — StunnedState
        /// manages its own exit (waits for both timer AND knockback to finish).
        /// </summary>
        private void HandleKnockbackEnd()
        {
            if (stateMachine.CurrentState is StunnedState) return;
            // A spiked body skids along the floor while lying down: the launch owns
            // what happens next, not the end of that slide.
            if (stateMachine.CurrentState is LaunchedState) return;

            IStunnable stunnable = GetCapability<IStunnable>();
            if (stunnable != null)
                stunnable.OnStunComplete();
            else
                OnStunComplete();
        }

        protected virtual void ApplyKnockback(DamageRequest request)
        {
            if (!canBeKnockedBack) return;
            if (request.KnockbackForce.sqrMagnitude <= 0f) return;

            Vector3 knockbackDir = Vector3.right;
            Vector3 swing = request.HitReaction.PushDirection;
            swing.y = 0f;
            if (swing.sqrMagnitude > 0.0001f)
            {
                // Along the attacker's swing: stays correct when the bodies overlap.
                knockbackDir = swing.normalized;
            }
            else if (request.Source != null)
            {
                knockbackDir = (transform.position - request.Source.transform.position);
                knockbackDir.y = 0f;
                if (knockbackDir.sqrMagnitude > 0.001f)
                    knockbackDir.Normalize();
                else
                    knockbackDir = Vector3.right;
            }

            // Only launchable enemies leave the ground: heavies and bosses (Brute) never float.
            float lift = canBeLaunched ? request.KnockbackForce.y : 0f;
            Vector3 knockback = knockbackDir * request.KnockbackForce.x
                              + Vector3.up * lift;

            if (movement != null)
                movement.ApplyKnockback(knockback);
        }

        public virtual void Attack()
        {
            if (!HasTarget || !IsTargetInAttackRange) return;
        }

        protected override void HandleDeath()
        {
            if (deathHandled)
                return;

            deathHandled = true;
            IgnoreEnemyBodyCollisionsOnDeath();
            LevelStatsTracker.Instance?.NotifyEnemyKilled(this);

            if (DropManager.Instance != null)
            {
                if (customDropTable != null)
                    DropManager.Instance.RequestDrop(transform.position, customDropTable, dropChance);
                else
                    DropManager.Instance.RequestDrop(transform.position, dropChance);
            }

            if (statusEffects != null)
                statusEffects.ClearAllEffects();

            // Stop AI decision-making immediately, but leave the Rigidbody physics
            // active so the corpse keeps falling under gravity and lands naturally.
            if (detectionZone != null)
            {
                detectionZone.ClearTarget();
                detectionZone.enabled = false;
            }

            PlayerCombatTracker.Instance?.NotifyEnemyExitedCombat(this);

            isInCombat = false;

            // Entering DeadState makes the animation presenter play the death
            // animation. The visual and physics stay active so the animation is
            // actually rendered and the body can settle on the ground.
            if (stateMachine != null)
                stateMachine.ChangeState<DeadState>();

            Died?.Invoke(this);
            base.HandleDeath();

            // Defer the visual/physics teardown until the corpse has landed and the
            // death animation has had time to play. Teardown happens in DeathSequence.
            StartCoroutine(DeathSequence());
        }

        /// <summary>
        /// Waits for the corpse to touch the ground (or a timeout), lets the death
        /// animation linger, then tears down visuals, physics and deactivates the
        /// enemy root. Runs on the still-active enemy so the animation is visible.
        /// </summary>
        protected virtual IEnumerator DeathSequence()
        {
            // Wait for the body to hit the ground before removing it. Flying enemies
            // (no EnemyMovement) skip straight to the linger.
            float elapsed = 0f;
            while (movement != null && !movement.IsGrounded && elapsed < deathGroundTimeout)
            {
                elapsed += Time.deltaTime;
                yield return null;
            }

            if (deathLinger > 0f)
                yield return new WaitForSeconds(deathLinger);

            SpawnDeathParticles();
            DisableEnemyVisual();
            DisablePhysics();

            if (movement != null)
                movement.Stop();

            enabled = false;

            // Now that the death presentation has finished, keeping the enemy root
            // active only leaves its remaining MonoBehaviours receiving no-op
            // Update/FixedUpdate callbacks, so it is deactivated.
            gameObject.SetActive(false);
        }

        protected virtual void DisablePhysics()
        {
            var rb = GetComponent<Rigidbody>();
            if (rb != null)
            {
                rb.useGravity = false;
                rb.linearVelocity = Vector3.zero;
                rb.isKinematic = true;
            }

            var colliders = GetComponentsInChildren<Collider>();
            foreach (var col in colliders)
                col.enabled = false;
        }

        #endregion

        #region VFX - Universal

        protected virtual void OnDamageResolvedVFX(DamageResult result, DamageRequest request)
        {
            if (result.WasApplied && DamagePopupManager.Instance != null)
                DamagePopupManager.Instance.SpawnPopup(transform.position, result.AppliedDamage,
                    request.Type, request.IsTickDamage);

            if (damageFlashUniversal != null)
            {
                damageFlashUniversal.Flash();
                return;
            }

            if (!warnedMissingDamageFlash)
            {
                warnedMissingDamageFlash = true;
                Debug.LogWarning($"[{gameObject.name}] Missing DamageFlashUniversal; skipping damage flash.", this);
            }
        }

        protected virtual void SpawnDeathParticles()
        {
            if (deathParticlePrefab == null) return;

            if (deathParticleLifetime > 0f)
                VFXPool.SpawnTimed(
                    deathParticlePrefab,
                    transform.position,
                    Quaternion.identity,
                    deathParticleLifetime);
            else
                Instantiate(deathParticlePrefab, transform.position, Quaternion.identity);
        }

        protected virtual void DisableEnemyVisual()
        {
            if (enemyVisual != null)
                enemyVisual.SetActive(false);
        }

        #endregion

        #region Lifecycle

        public override void Activate()
        {
            base.Activate();
            enabled = true;
            stateMachine?.Resume();
        }

        public override void Deactivate()
        {
            base.Deactivate();
            enabled = false;
            stateMachine?.Pause();
        }

        protected override void OnDestroy()
        {
            base.OnDestroy();

            if (detectionZone != null)
                detectionZone.TargetChanged -= HandlePerceptionTargetChanged;

            if (damageable != null)
                damageable.OnDamageResolved -= OnDamageResolvedVFX;

            if (movement != null)
                movement.OnKnockbackEnd -= HandleKnockbackEnd;
        }

        #endregion

        #region Debug Gizmos

        protected virtual void OnDrawGizmosSelected()
        {
            if (!showGizmos) return;

            Gizmos.color = new Color(1f, 0f, 0f, 0.3f);
            Gizmos.DrawWireSphere(transform.position, attackRange);

            if (Target != null)
            {
                Gizmos.color = IsTargetInAttackRange ? Color.red : Color.yellow;
                Gizmos.DrawLine(transform.position, Target.position);
            }
        }

        #endregion
    }

}
