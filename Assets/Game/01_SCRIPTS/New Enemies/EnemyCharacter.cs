using System.Collections;
using UnityEngine;
namespace junklite
{
    /// <summary>
    /// Base class for all enemy characters.
    /// 
    /// ARCHITECTURE:
    /// - States handle ACTIONS (move, animate, enable hitboxes)
    /// - Enemy handles DECISIONS (what state to go to next)
    /// 
    /// States call transition methods (OnChargeComplete, OnDashComplete, etc.)
    /// Enemy overrides these to define behavior/personality.
    /// </summary>
    [RequireComponent(typeof(StateMachine))]
    [RequireComponent(typeof(EnemyMovement))]
    public class EnemyCharacter : CharacterBase
    {
        [Header("Enemy Config")]
        [SerializeField] protected EnemyConfig config;

        [Header("Detection")]
        [SerializeField] protected DetectionZone detectionZone;
        [SerializeField] protected float attackRange = 2f;
        [SerializeField] protected LayerMask targetLayer;

        [Header("Combat")]
        [SerializeField] protected Hitbox dashHitbox;

        [Header("Patrol")]
        [SerializeField] protected float patrolDistance = 5f;
        [SerializeField] protected float wallCheckDistance = 0.5f;
        [SerializeField] protected LayerMask wallLayer;

        [Header("Stats")]
        [SerializeField] public float speed = 60f;
        [SerializeField] public float life = 100f;

        [Header("Debug")]
        [SerializeField] protected bool showGizmos = true;

        [Header("status effect system")]
        private Coroutine statusCoroutine;
        protected SpriteRenderer activeVFX;

        // Components
        protected StateMachine stateMachine;
        protected EnemyMovement movement;

        // Target tracking
        protected Transform target;
        protected PlayerCharacter targetCharacter;

        // Patrol state
        protected Vector3 spawnPosition;
        protected int patrolDirection = 1;

        // Combat state - prevents detection events from interrupting combat
        protected bool isInCombat = false;

        public bool IsInCombat => isInCombat;

        // Public accessors - Components
        public EnemyConfig Config => config;
        public StateMachine StateMachine => stateMachine;
        public EnemyMovement Movement => movement;
        public DetectionZone DetectionZone => detectionZone;
        public Hitbox DashHitbox => dashHitbox;

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

        // Virtual properties for attacks - override in subclasses
        public virtual float DashChargeTime => 1f;
        public virtual float DashSpeed => 15f;
        public virtual float DashRecoveryTime => 0.3f;
        public virtual float DashDamage => 10f;
        public virtual Vector2 DashKnockback => new Vector2(15f, 5f); // x = horizontal, y = vertical
        public virtual float DashKnockbackUpward => 5f;

        // Computed properties - Target
        public bool HasTarget => target != null && targetCharacter != null && targetCharacter.IsAlive;
        public float DistanceToTarget => HasTarget ? Vector3.Distance(transform.position, target.position) : float.MaxValue;
        public bool IsTargetInAttackRange => DistanceToTarget <= attackRange;
        public Vector3 DirectionToTarget => HasTarget ? (target.position - transform.position).normalized : Vector3.zero;

        protected override void Awake()
        {
            base.Awake();
            stateMachine = GetComponent<StateMachine>();
            movement = GetComponent<EnemyMovement>();
            spawnPosition = transform.position;

            // Setup detection zone events
            if (detectionZone != null)
            {
                detectionZone.OnTargetEnter += OnDetectionZoneEnter;
                detectionZone.OnTargetExit += OnDetectionZoneExit;
            }

            // Setup hitbox events - enemy decides what happens on hit
            if (dashHitbox != null)
            {
                dashHitbox.OnHit += OnDashHitboxHit;
                dashHitbox.Deactivate();
            }
        }

        protected override void Start()
        {
            base.Start();
            InitializeStateMachine();
        }

        /// <summary>
        /// Override to register states and set initial state.
        /// </summary>
        protected virtual void InitializeStateMachine() { }

        protected virtual void Update()
        {
            if (!IsAlive) return;
            // Detection is now handled by DetectionZone trigger events
            // No per-frame scanning needed
        }

        #region Detection Zone Events

        /// <summary>
        /// Called when DetectionZone detects a player entering.
        /// </summary>
        protected virtual void OnDetectionZoneEnter(PlayerCharacter player)
        {
            if (!IsAlive) return;
            OnPlayerSpotted();
        }

        /// <summary>
        /// Called when DetectionZone detects a player leaving.
        /// </summary>
        protected virtual void OnDetectionZoneExit(PlayerCharacter player)
        {
            if (!IsAlive) return;
            OnPlayerLost();
        }

        #endregion

        #region Behavior Decisions - Override these in subclasses

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
        /// DECISION: Called when charge state completes. What should enemy do?
        /// </summary>
        public virtual void OnChargeComplete()
        {
            // Default: no behavior defined
            Debug.Log($"{gameObject.name}: Charge complete but no behavior defined!");
        }

        /// <summary>
        /// DECISION: Called when dash state completes. What should enemy do?
        /// </summary>
        public virtual void OnDashComplete()
        {
            // Default: no behavior defined
            Debug.Log($"{gameObject.name}: Dash complete but no behavior defined!");
        }

        /// <summary>
        /// DECISION: Called when grab state completes (after throw). What should enemy do?
        /// </summary>
        public virtual void OnGrabComplete()
        {
            // Default: no behavior defined
            Debug.Log($"{gameObject.name}: Grab complete but no behavior defined!");
        }

        /// <summary>
        /// DECISION: Called when recovery state completes. What should enemy do?
        /// </summary>
        public virtual void OnRecoveryComplete()
        {
            // Default: no behavior defined
            Debug.Log($"{gameObject.name}: Recovery complete but no behavior defined!");
        }

        /// <summary>
        /// DECISION: Called when any attack finishes (legacy support).
        /// </summary>
        public virtual void OnAttackFinished()
        {
            // Default: if player still there, attack again; else patrol
            if (HasTarget)
                OnPlayerSpotted();
            else
                OnPlayerLost();
        }

        /// <summary>
        /// DECISION: Called when player enters attack range.
        /// </summary>
        public virtual void OnPlayerInAttackRange()
        {
            OnPlayerSpotted();
        }

        #endregion

        #region Combat State

        /// <summary>
        /// Call when entering a combat sequence (charge, dash, grab, etc.)
        /// Prevents detection events from interrupting.
        /// </summary>
        public virtual void EnterCombat()
        {
            isInCombat = true;
        }

        /// <summary>
        /// Call when combat sequence ends (recovery complete, player lost, etc.)
        /// Allows detection events to trigger again.
        /// </summary>
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

        #region Combat

        public virtual void Attack()
        {
            if (!HasTarget || !IsTargetInAttackRange) return;
            Debug.Log($"{gameObject.name} attacks {target.name}!");
        }

        protected override void HandleDeath()
        {
            Debug.Log($"[{gameObject.name}] HandleDeath called!");

            // Disable hitbox first
            if (dashHitbox != null)
                dashHitbox.Deactivate();

            // Stop movement
            if (movement != null)
                movement.Stop();

            // Disable detection zone to prevent further events
            if (detectionZone != null)
                detectionZone.enabled = false;

            // Disable all colliders and physics so player can't interact with dead enemy
            DisablePhysics();

            // Clear target references WITHOUT calling OnTargetLost
            // (OnTargetLost would trigger state changes which we don't want)
            target = null;
            targetCharacter = null;
            isInCombat = false;

            // Change to dead state
            if (stateMachine != null)
            {
                stateMachine.ChangeState<DeadState>();
                Debug.Log($"[{gameObject.name}] Changed to DeadState");
            }

            // Call base implementation
            base.HandleDeath();

            // Disable this component
            enabled = false;
        }

        /// <summary>
        /// Disables all colliders and gravity so enemy can't be interacted with and won't fall.
        /// </summary>
        protected virtual void DisablePhysics()
        {
            // Disable gravity and freeze rigidbody
            var rb = GetComponent<Rigidbody>();
            if (rb != null)
            {
                rb.useGravity = false;
                rb.linearVelocity = Vector3.zero;
                rb.isKinematic = true;
            }

            // Disable all colliders
            var colliders = GetComponentsInChildren<Collider>();
            foreach (var col in colliders)
            {
                col.enabled = false;
            }
        }

        #endregion

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

        protected virtual void OnDrawGizmosSelected()
        {
            if (!showGizmos) return;

            // Attack range (red)
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

        protected virtual void OnDestroy()
        {
            // Unsubscribe from detection zone events
            if (detectionZone != null)
            {
                detectionZone.OnTargetEnter -= OnDetectionZoneEnter;
                detectionZone.OnTargetExit -= OnDetectionZoneExit;
            }

            // Unsubscribe from hitbox events
            if (dashHitbox != null)
            {
                dashHitbox.OnHit -= OnDashHitboxHit;
            }
        }

        #region Hitbox Events

        /// <summary>
        /// Called when dash hitbox hits something. Override to define damage behavior.
        /// Default: applies DashDamage + DashKnockback.
        /// </summary>
        protected virtual void OnDashHitboxHit(Collider other, Hitbox hitbox)
        {
            var damageable = other.GetComponent<IDamageable>();
            if (damageable == null)
                damageable = other.GetComponentInParent<IDamageable>();

            if (damageable == null || !damageable.IsAlive)
                return;

            // Default behavior: simple damage + knockback
            var info = new DamageInfo(DashDamage, gameObject, DamageType.Physical, DashKnockback);
            damageable.TakeDamage(info);

            Debug.Log($"{gameObject.name} hit {other.name} for {DashDamage} damage");
        }

        #endregion

        #region status effect system
	/// <summary>
	/// Applies a status effect to the enemy.
	public void ApplyStatusEffect(StatusEffect effect, SpriteRenderer VFX)
	{
		if (effect == null) return;

        // stop any existing status coroutine (optional behaviour)
        if (statusCoroutine != null)
        {
            StopCoroutine(statusCoroutine);
            statusCoroutine = null;
        }

        // spawn VFX (if provided) as child so it follows the enemy
        if (VFX != null)
        {
            activeVFX = Instantiate(VFX, transform);
            activeVFX.transform.localPosition = Vector3.zero;
        }

        statusCoroutine = StartCoroutine(RunStatus(effect, activeVFX));
	}

	 private IEnumerator RunStatus(StatusEffect effect, SpriteRenderer vfx)
    {
        yield return StartCoroutine(effect.Apply(this));

        if (vfx != null)
        {
            Destroy(vfx.gameObject);
                activeVFX = null;
        }

        statusCoroutine = null;
    }
#endregion status effect system

    }
}