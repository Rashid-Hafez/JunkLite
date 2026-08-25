using System.Collections;
using System;
using System.IO;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;

namespace junklite
{
    /// <summary>
    /// Base class for all enemy characters.
    /// 
    /// ARCHITECTURE:
    /// - This base class contains ONLY universal enemy functionality
    /// - Capability-specific behavior is defined via interfaces (IDasher, IGrabber, IPatroller, etc.)
    /// - States check for interfaces to access capability-specific data
    /// - Enemy subclasses implement interfaces to declare their capabilities
    /// 
    /// UNIVERSAL (lives here):
    /// - Detection, target tracking
    /// - Combat state management
    /// - Death handling, VFX basics
    /// - State machine reference
    /// 
    /// CAPABILITY-SPECIFIC (lives in interfaces):
    /// - Patrol, Dash, Grab, Melee, Dodge, Chase, Ranged, Stun, etc.
    /// </summary>

    public enum EnemyType
    {
        Dummy,
        Robot,
        Hyena,
        FlyingDummy,
        Grunt
    }

    [RequireComponent(typeof(StateMachine))]
    [RequireComponent(typeof(EnemyMovement))]
    [RequireComponent(typeof(StatusEffectHandler))]
    public class EnemyCharacter : EnemyBase
    {
        [Header("Enemy Config")]
        [SerializeField] protected EnemyConfig config;
        [Header("Audio")]
        [SerializeField] private EnemySoundProfile soundProfile;

        [Header("Detection")]
        [SerializeField] protected DetectionZone detectionZone;
        [SerializeField] protected float attackRange = 2f;
        [SerializeField] protected LayerMask targetLayer;

        [Header("Knockback")]
        [Tooltip("If false, this enemy cannot be knocked back")]
        [SerializeField] protected bool canBeKnockedBack = true;

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

        [Header("Animation (Enemy)")]
        [SerializeField] private EnemyAnimationController enemyAnimation;
        public EnemyAnimationController Anim => enemyAnimation;

        [Header("Death VFX")]
        [SerializeField] protected GameObject deathParticlePrefab;
        [SerializeField] protected float deathParticleLifetime = 2f;
        [SerializeField] protected GameObject enemyVisual;

        [Header("Drops")]
        [SerializeField] protected DropTable customDropTable;
        [SerializeField][Range(0f, 1f)] protected float dropChance = 1f;

        protected EnemyType enemyType;

        // Damage flash state
        private Coroutine damageFlashCoroutine;
        private Coroutine attackNotifyCoroutine;
        private bool tutorialFrozen;
        private bool tutorialPreviousKinematic;
        private Rigidbody tutorialFrozenRigidbody;
        private EnemySpineAnimationController spineAnimationController;

        // Components
        protected StateMachine stateMachine;
        protected EnemyMovement movement;
        protected StatusEffectHandler statusEffects;
        private bool isParryStunned;

        public bool IsParryStunned => isParryStunned;

        // Target tracking
        protected Transform target;
        protected PlayerCharacter targetCharacter;

        // Combat state - prevents detection events from interrupting combat
        protected bool isInCombat = false;

        // Current state reference for quick access
        protected EnemyStateBase state => stateMachine?.CurrentState as EnemyStateBase;

        public bool IsInCombat => isInCombat;

        // Public accessors - Components
        public EnemyConfig Config => config;
        public StateMachine StateMachine => stateMachine;
        public EnemyMovement Movement => movement;
        public DetectionZone DetectionZone => detectionZone;
        public StatusEffectHandler StatusEffects => statusEffects;

        // Public accessors - Target
        public Transform Target => target;
        public PlayerCharacter TargetCharacter => targetCharacter;
        public float AttackRange => attackRange;
        public bool CanBeKnockedBack => canBeKnockedBack;

        // Computed properties - Target
        public bool HasTarget => target != null && targetCharacter != null && targetCharacter.IsAlive;
        public float DistanceToTarget => HasTarget ? Vector3.Distance(transform.position, target.position) : float.MaxValue;
        public bool IsTargetInAttackRange => DistanceToTarget <= attackRange;
        public Vector3 DirectionToTarget => HasTarget ? (target.position - transform.position).normalized : Vector3.zero;
        public EnemyType EnemyType => enemyType;
        public EnemySoundProfile SoundProfile => soundProfile;
        public bool IsTutorialFrozen => tutorialFrozen;

        public event Action<EnemyCharacter> OnAttackNotifyShown;

        protected override void Awake()
        {
            base.Awake();
            stateMachine = GetComponent<StateMachine>();
            movement = GetComponent<EnemyMovement>();
            statusEffects = GetComponent<StatusEffectHandler>();

            if (enemyAnimation == null)
                enemyAnimation = GetComponentInChildren<EnemyAnimationController>(true);

            if (damageFlashUniversal == null)
                damageFlashUniversal = GetComponentInChildren<DamageFlashUniversal>(true);
            if (spineAnimationController == null)
                spineAnimationController = GetComponentInChildren<EnemySpineAnimationController>(true);

            if (movement != null)
            {
                movement.IgnoreKnockback = !canBeKnockedBack;
                movement.OnKnockbackEnd += HandleKnockbackEnd;
            }

            if (detectionZone != null)
            {
                detectionZone.OnTargetEnter += OnDetectionZoneEnter;
                detectionZone.OnTargetExit += OnDetectionZoneExit;
            }

            if (damageable != null)
                damageable.OnDamageResolved += OnDamageResolvedVFX;
        }

        protected override void Start()
        {
            base.Start();
            InitializeStateMachine();
            TryIgnorePlayerBodyCollision();
        }

        protected virtual void OnEnable()
        {
            if (stateMachine != null)
                stateMachine.OnStateChanged += HandleAttackWarningStateChanged;
        }

        protected virtual void OnDisable()
        {
            if (stateMachine != null)
                stateMachine.OnStateChanged -= HandleAttackWarningStateChanged;
        }

        private void HandleAttackWarningStateChanged(IState from, IState to)
        {
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
            else if (from is MeleeAttackState)
            {
                HideAttackWarning();
            }
        }

        private void ShowAttackNotify()
        {
            if (attackWarningVfx != null)
                attackWarningVfx.SetActive(true);
            GetComponentInChildren<EnemyAudioHandler>()?.PlayAttackNotify();
            OnAttackNotifyShown?.Invoke(this);
        }

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

                spineAnimationController?.SetPlaybackPaused(true);
            }
            else
            {
                spineAnimationController?.SetPlaybackPaused(false);

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

        protected virtual void Update() { }

        protected virtual void InitializeStateMachine() { }

        #region Detection Zone Events

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
            Debug.Log($"{gameObject.name}: Player spotted but no behavior defined!");
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
            Debug.Log($"{gameObject.name}: Stun complete but no behavior defined!");
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

                var stunnable = this as IStunnable;
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
            isInCombat = true;
            PlayerCombatTracker.Instance?.NotifyEnemyEnteredCombat(this);
        }

        public virtual void ExitCombat()
        {
            isInCombat = false;
            PlayerCombatTracker.Instance?.NotifyEnemyExitedCombat(this);
        }

        #endregion

        #region Target Management

        public virtual void SetTarget(PlayerCharacter newTarget)
        {
            targetCharacter = newTarget;
            target = newTarget?.transform;
            TryIgnorePlayerBodyCollision();
            OnTargetAcquired();
        }

        public virtual void ClearTarget()
        {
            if (target == null) return;
            target = null;
            targetCharacter = null;
            OnTargetLost();
        }

        protected virtual void OnTargetAcquired() { }
        protected virtual void OnTargetLost() { }

        #endregion

        #region Collision Helpers

        private void TryIgnorePlayerBodyCollision()
        {
            var player = targetCharacter != null
                ? targetCharacter
                : FindObjectOfType<PlayerCharacter>(true);
            if (player == null) return;

            var enemyCols = GetComponentsInChildren<Collider>(includeInactive: true);
            var playerCols = player.GetComponentsInChildren<Collider>(includeInactive: true);

            foreach (var e in enemyCols)
            {
                if (e == null || e.isTrigger) continue;
                foreach (var p in playerCols)
                {
                    if (p == null || p.isTrigger) continue;
                    Physics.IgnoreCollision(e, p, true);
                }
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
            if (state != null && !state.CanTakeDamage)
                return DamageResult.Rejected(DamageOutcome.Invulnerable, request.Amount);

            DamageResult result = base.ReceiveDamage(request);

            if (result.WasApplied)
            {
                if (state == null || state.CanBeInterrupted)
                {
                    // 1. Knockback FIRST — starts the push immediately
                    ApplyKnockback(request);

                    // 2. Stagger SECOND — enters StunnedState (which preserves active knockback)
                    if (!request.IsTickDamage)
                        ApplyHitstun();
                }
            }

            return result;
        }

        public virtual void ApplyStun(float duration)
        {
            if (stateMachine == null) return;

            var stunnable = this as IStunnable;
            if (stunnable != null)
                stunnable.ForcedStunDuration = duration;

            stateMachine.ChangeState<StunnedState>();
        }

        protected virtual void ApplyHitstun()
        {
            if (!IsAlive) return;

            var stunnable = this as IStunnable;
            if (stunnable == null || stunnable.StaggerDuration <= 0f) return;

            // Already stunned? Reset timer for combo extension
            if (stateMachine.CurrentState is StunnedState stunned)
            {
                stunned.ResetTimer();
                return;
            }

            stateMachine.ChangeState<StunnedState>();
        }

        /// <summary>
        /// Knockback end callback. Skips while in StunnedState — StunnedState
        /// manages its own exit (waits for both timer AND knockback to finish).
        /// </summary>
        private void HandleKnockbackEnd()
        {
            if (stateMachine.CurrentState is StunnedState) return;
            OnStunComplete();
        }

        protected virtual void ApplyKnockback(DamageRequest request)
        {
            if (!canBeKnockedBack) return;
            if (request.KnockbackForce.sqrMagnitude <= 0f) return;

            Vector3 knockbackDir = Vector3.right;
            if (request.Source != null)
            {
                knockbackDir = (transform.position - request.Source.transform.position);
                knockbackDir.y = 0f;
                if (knockbackDir.sqrMagnitude > 0.001f)
                    knockbackDir.Normalize();
                else
                    knockbackDir = Vector3.right;
            }

            Vector3 knockback = knockbackDir * request.KnockbackForce.x
                              + Vector3.up * request.KnockbackForce.y;

            if (movement != null)
                movement.ApplyKnockback(knockback);
        }

        public virtual void Attack()
        {
            if (!HasTarget || !IsTargetInAttackRange) return;
            //Debug.Log($"{gameObject.name} attacks {target.name}!");
        }

        protected override void HandleDeath()
        {
            LevelStatsTracker.Instance?.NotifyEnemyKilled(this);

            SpawnDeathParticles();
            DisableEnemyVisual();
            DisablePhysics();

            if (DropManager.Instance != null)
            {
                if (customDropTable != null)
                    DropManager.Instance.RequestDrop(transform.position, customDropTable, dropChance);
                else
                    DropManager.Instance.RequestDrop(transform.position, dropChance);
            }

            if (statusEffects != null)
                statusEffects.ClearAllEffects();

            if (damageFlashCoroutine != null)
            {
                StopCoroutine(damageFlashCoroutine);
                damageFlashCoroutine = null;
            }

            if (movement != null)
                movement.Stop();

            if (detectionZone != null)
                detectionZone.enabled = false;

            PlayerCombatTracker.Instance?.NotifyEnemyExitedCombat(this);

            target = null;
            targetCharacter = null;
            isInCombat = false;

            if (stateMachine != null)
                stateMachine.ChangeState<DeadState>();

            base.HandleDeath();
            enabled = false;
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
            if (DamagePopupManager.Instance != null)
                DamagePopupManager.Instance.SpawnPopup(transform.position, result.AppliedDamage);

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

            GameObject go = Instantiate(deathParticlePrefab, transform.position, Quaternion.identity);
            if (deathParticleLifetime > 0f)
                Destroy(go, deathParticleLifetime);
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
            {
                detectionZone.OnTargetEnter -= OnDetectionZoneEnter;
                detectionZone.OnTargetExit -= OnDetectionZoneExit;
            }

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

            if (target != null)
            {
                Gizmos.color = IsTargetInAttackRange ? Color.red : Color.yellow;
                Gizmos.DrawLine(transform.position, target.position);
            }
        }

        #endregion
    }

}
