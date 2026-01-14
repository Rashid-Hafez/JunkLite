using System.Collections;
using UnityEngine;

namespace junklite
{
    /// <summary>
    /// Base class for all enemy characters.
    /// 
    /// ARCHITECTURE:
    /// - This base class contains ONLY universal enemy functionality
    /// - Capability-specific behavior is defined via interfaces (IDasher, IGrabber, etc.)
    /// - States check for interfaces to access capability-specific data
    /// - Enemy subclasses implement interfaces to declare their capabilities
    /// 
    /// UNIVERSAL (lives here):
    /// - Detection, patrol, target tracking
    /// - Combat state management
    /// - Death handling, VFX basics
    /// - State machine reference
    /// 
    /// CAPABILITY-SPECIFIC (lives in interfaces):
    /// - Dash, Grab, Melee, Dodge, Chase, Ranged, etc.
    /// </summary>
    [RequireComponent(typeof(StateMachine))]
    [RequireComponent(typeof(EnemyMovement))]
    [RequireComponent(typeof(StatusEffectHandler))]
    public class EnemyCharacter : CharacterBase
    {
        [Header("Enemy Config")]
        [SerializeField] protected EnemyConfig config;

        [Header("Detection")]
        [SerializeField] protected DetectionZone detectionZone;
        [SerializeField] protected float attackRange = 2f;
        [SerializeField] protected LayerMask targetLayer;

        [Header("Patrol")]
        [SerializeField] protected float patrolDistance = 5f;
        [SerializeField] protected float wallCheckDistance = 0.5f;
        [SerializeField] protected LayerMask wallLayer;

        [Header("Knockback")]
        [Tooltip("If false, this enemy cannot be knocked back")]
        [SerializeField] protected bool canBeKnockedBack = true;

        [Header("Debug")]
        [SerializeField] protected bool showGizmos = true;

        [Header("Damage Flash VFX")]
        [SerializeField] protected DamageFlashUniversal damageFlashUniversal;
        private bool warnedMissingDamageFlash;

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

        // Components
        protected StateMachine stateMachine;
        protected EnemyMovement movement;
        protected StatusEffectHandler statusEffects;

        // Target tracking
        protected Transform target;
        protected PlayerCharacter targetCharacter;

        // Patrol state
        protected Vector3 spawnPosition;
        protected int patrolDirection = 1;

        // Combat state - prevents detection events from interrupting combat
        protected bool isInCombat = false;

        // Current state reference for quick access
        protected new EnemyStateBase state => stateMachine?.CurrentState as EnemyStateBase;

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

        // Public accessors - Patrol
        public float PatrolDistance => patrolDistance;
        public Vector3 SpawnPosition => spawnPosition;
        public int PatrolDirection { get => patrolDirection; set => patrolDirection = value; }
        public Vector3 PatrolLeftPoint => spawnPosition + Vector3.left * patrolDistance;
        public Vector3 PatrolRightPoint => spawnPosition + Vector3.right * patrolDistance;
        public bool HasPatrol => patrolDistance > 0f;
        public bool CanBeKnockedBack => canBeKnockedBack;

        // Computed properties - Target
        public bool HasTarget => target != null && targetCharacter != null && targetCharacter.IsAlive;
        public float DistanceToTarget => HasTarget ? Vector3.Distance(transform.position, target.position) : float.MaxValue;
        public bool IsTargetInAttackRange => DistanceToTarget <= attackRange;
        public Vector3 DirectionToTarget => HasTarget ? (target.position - transform.position).normalized : Vector3.zero;
        public EnemyType EnemyType => enemyType;

        protected override void Awake()
        {
            base.Awake();
            stateMachine = GetComponent<StateMachine>();
            movement = GetComponent<EnemyMovement>();
            statusEffects = GetComponent<StatusEffectHandler>();
            spawnPosition = transform.position;

            if (enemyAnimation == null)
                enemyAnimation = GetComponentInChildren<EnemyAnimationController>(true);

            // Auto-wire DamageFlashUniversal if not set in inspector
            if (damageFlashUniversal == null)
                damageFlashUniversal = GetComponentInChildren<DamageFlashUniversal>(true);

            // Sync knockback setting to movement component
            if (movement != null)
                movement.IgnoreKnockback = !canBeKnockedBack;

            // Setup detection zone events
            if (detectionZone != null)
            {
                detectionZone.OnTargetEnter += OnDetectionZoneEnter;
                detectionZone.OnTargetExit += OnDetectionZoneExit;
            }

            // Setup damage VFX event
            if (damageable != null)
                damageable.OnDamaged += OnDamagedVFX;
        }

        protected override void Start()
        {
            base.Start();
            InitializeStateMachine();
        }

        protected virtual void OnEnable() { }
        protected virtual void OnDisable() { }
        protected virtual void Update() { }

        /// <summary>
        /// Override to register states and set initial state.
        /// </summary>
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

        /// <summary>
        /// DECISION: Called when player is spotted. What should enemy do?
        /// </summary>
        public virtual void OnPlayerSpotted()
        {
            Debug.Log($"{gameObject.name}: Player spotted but no behavior defined!");
        }

        /// <summary>
        /// DECISION: Called when player leaves detection. What should enemy do?
        /// </summary>
        public virtual void OnPlayerLost()
        {
            if (!IsAlive) return;

            // Default: return to patrol or idle
            if (HasPatrol)
                stateMachine.ChangeState<PatrolState>();
            else
                stateMachine.ChangeState<IdleState>();
        }

        /// <summary>
        /// DECISION: Called when player enters attack range.
        /// </summary>
        public virtual void OnPlayerInAttackRange()
        {
            OnPlayerSpotted();
        }

        /// <summary>
        /// DECISION: Called when stun/knockback state completes.
        /// </summary>
        public virtual void OnStunComplete()
        {
            Debug.Log($"{gameObject.name}: Stun complete but no behavior defined!");
        }

        /// <summary>
        /// DECISION: Called when any attack finishes (legacy support).
        /// </summary>
        public virtual void OnAttackFinished()
        {
            if (HasTarget)
                OnPlayerSpotted();
            else
                OnPlayerLost();
        }

        #endregion

        #region Combat State

        public virtual void EnterCombat()
        {
            isInCombat = true;
        }

        public virtual void ExitCombat()
        {
            isInCombat = false;
        }

        #endregion

        #region Patrol Helpers

        public bool IsWallAhead()
        {
            Vector3 direction = patrolDirection > 0 ? Vector3.right : Vector3.left;
            return Physics.Raycast(transform.position, direction, wallCheckDistance, wallLayer);
        }

        public bool IsAtPatrolBoundary()
        {
            float distanceFromSpawn = transform.position.x - spawnPosition.x;

            if (patrolDirection > 0 && distanceFromSpawn >= patrolDistance)
                return true;
            if (patrolDirection < 0 && distanceFromSpawn <= -patrolDistance)
                return true;

            return false;
        }

        public void ReverseDirection()
        {
            patrolDirection *= -1;
        }

        #endregion

        #region Target Management

        public virtual void SetTarget(PlayerCharacter newTarget)
        {
            targetCharacter = newTarget;
            target = newTarget?.transform;
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

        #region Combat & Death

        public virtual void Attack()
        {
            if (!HasTarget || !IsTargetInAttackRange) return;
            Debug.Log($"{gameObject.name} attacks {target.name}!");
        }

        protected override void HandleDeath()
        {
            Debug.Log($"[{gameObject.name}] HandleDeath called!");

            SpawnDeathParticles();
            DisableEnemyVisual();
            DisablePhysics();

            // Request drop from DropManager
            if (DropManager.Instance != null)
            {
                if (customDropTable != null)
                    DropManager.Instance.RequestDrop(transform.position, customDropTable, dropChance);
                else
                    DropManager.Instance.RequestDrop(transform.position, dropChance);
            }

            // Clear status effects
            if (statusEffects != null)
                statusEffects.ClearAllEffects();

            // Stop damage flash if active
            if (damageFlashCoroutine != null)
            {
                StopCoroutine(damageFlashCoroutine);
                damageFlashCoroutine = null;
            }

            // Stop movement
            if (movement != null)
                movement.Stop();

            // Disable detection zone
            if (detectionZone != null)
                detectionZone.enabled = false;

            // Clear target references WITHOUT calling OnTargetLost
            target = null;
            targetCharacter = null;
            isInCombat = false;

            // Change to dead state
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

        protected virtual void OnDamagedVFX(float damage, GameObject source)
        {
            if (DamagePopupManager.Instance != null)
                DamagePopupManager.Instance.SpawnPopup(transform.position, damage);

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
                damageable.OnDamaged -= OnDamagedVFX;
        }

        #endregion

        #region Debug Gizmos

        protected virtual void OnDrawGizmosSelected()
        {
            if (!showGizmos) return;

            // Attack range
            Gizmos.color = new Color(1f, 0f, 0f, 0.3f);
            Gizmos.DrawWireSphere(transform.position, attackRange);

            // Line to target
            if (target != null)
            {
                Gizmos.color = IsTargetInAttackRange ? Color.red : Color.yellow;
                Gizmos.DrawLine(transform.position, target.position);
            }

            // Patrol range
            if (patrolDistance > 0f)
            {
                Vector3 origin = Application.isPlaying ? spawnPosition : transform.position;
                Vector3 leftPoint = origin + Vector3.left * patrolDistance;
                Vector3 rightPoint = origin + Vector3.right * patrolDistance;

                Gizmos.color = Color.cyan;
                Gizmos.DrawLine(leftPoint, rightPoint);
                Gizmos.DrawWireSphere(leftPoint, 0.3f);
                Gizmos.DrawWireSphere(rightPoint, 0.3f);

                Gizmos.color = Color.green;
                Gizmos.DrawWireCube(origin, Vector3.one * 0.2f);

                Gizmos.color = Color.red;
                Vector3 wallDir = patrolDirection > 0 ? Vector3.right : Vector3.left;
                Gizmos.DrawRay(transform.position, wallDir * wallCheckDistance);
            }
        }

        #endregion
    }

    public enum EnemyType
    {
        Dummy,
        Robot,
        Hyena
    }
}