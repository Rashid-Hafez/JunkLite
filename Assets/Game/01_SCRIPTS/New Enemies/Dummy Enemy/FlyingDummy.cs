using UnityEngine;

namespace junklite
{
    /// <summary>
    /// Flying dummy that patrols in the air and follows player when detected.
    /// No combat capabilities - just follows and takes damage.
    /// 
    /// CAPABILITIES: IPatroller, IChaser
    /// 
    /// BEHAVIOR:
    /// - Patrol in air until player spotted
    /// - Chase/follow player (no attack)
    /// - Return to patrol when player lost
    /// </summary>
    public class FlyingDummy : EnemyCharacter, IPatroller, IChaser
    {
        [Header("Patrol")]
        [SerializeField] private PatrolBehavior patrol = new PatrolBehavior();

        [Header("Chase")]
        [SerializeField] private ChaseBehavior chase = new ChaseBehavior();

        [Header("Flying Settings")]
        [SerializeField] private float hoverBobAmount = 0.2f;
        [SerializeField] private float hoverBobSpeed = 2f;

        private Rigidbody rb;
        private float spawnY;    // Original spawn height (never changes)
        private float baseY;     // Current target height for bobbing (returns to spawnY gradually)
        private float bobTimer;

        public bool HasPatrol => patrol.HasPatrol;

        // ============================================================
        // IPatroller
        // ============================================================

        public float PatrolDistance => patrol.PatrolDistance;
        public float PatrolSpeed => patrol.PatrolSpeed;
        public Vector3 SpawnPosition => patrol.SpawnPosition;
        public int PatrolDirection { get => patrol.PatrolDirection; set => patrol.PatrolDirection = value; }
        public bool IsWallAhead() => patrol.IsWallAhead();
        public bool IsAtPatrolBoundary() => patrol.IsAtPatrolBoundary();
        public void ReverseDirection() => patrol.ReverseDirection();

        // ============================================================
        // IChaser
        // ============================================================

        public float ChaseSpeed => chase.ChaseSpeed;
        public float ChaseStopDistance => chase.ChaseStopDistance;
        public Vector3 LastKnownTargetPosition => chase.LastKnownTargetPosition;
        public bool HasLastKnownPosition => chase.HasLastKnownPosition;

        public void UpdateLastKnownPosition(Vector3 pos) => chase.UpdateLastKnownPosition(pos);

        public void OnReachedTarget()
        {
            // Just hover near player - don't attack
            // ChaseState will keep updating position if player moves
        }

        // ============================================================
        // Lifecycle
        // ============================================================

        protected override void Awake()
        {
            base.Awake();
            enemyType = EnemyType.FlyingDummy;

            // Disable gravity for flying
            rb = GetComponent<Rigidbody>();
            if (rb != null)
            {
                rb.useGravity = false;
                rb.constraints = RigidbodyConstraints.FreezeRotation | RigidbodyConstraints.FreezePositionZ;
            }

            patrol.Initialize(transform);
            spawnY = transform.position.y;
            baseY = spawnY;
        }

        protected override void InitializeStateMachine()
        {
            stateMachine.RegisterStates(
                new PatrolState(this),
                new IdleState(this),
                new ChaseState(this),
                new DeadState(this)
            );

            if (HasPatrol)
                stateMachine.SetInitialState<PatrolState>();
            else
                stateMachine.SetInitialState<IdleState>();
        }

        protected override void Update()
        {
            base.Update();

            // Update last known position while chasing
            if (HasTarget && stateMachine.CurrentState is ChaseState)
                UpdateLastKnownPosition(Target.position);

            // Height control and hover bobbing (skip during knockback and chase)
            if (IsAlive && !Movement.IsInKnockback && !(stateMachine.CurrentState is ChaseState))
            {
                // Gradually return to spawn height
                baseY = Mathf.MoveTowards(baseY, spawnY, chase.ChaseSpeed * Time.deltaTime);

                // Calculate bob offset
                bobTimer += Time.deltaTime * hoverBobSpeed;
                float bobOffset = hoverBobAmount > 0f ? Mathf.Sin(bobTimer) * hoverBobAmount : 0f;

                // Apply height
                Vector3 pos = transform.position;
                pos.y = baseY + bobOffset;
                transform.position = pos;
            }
        }

        // ============================================================
        // Brain
        // ============================================================

        public override void OnPlayerSpotted()
        {
            if (!IsAlive) return;
            EnterCombat();
            if (HasTarget)
                UpdateLastKnownPosition(Target.position);
            stateMachine.ChangeState<ChaseState>();
        }

        public override void OnPlayerLost()
        {
            if (!IsAlive) return;
            chase.ClearLastKnownPosition();
            ExitCombat();
            ReturnToPatrol();
        }

        public override void OnPlayerInAttackRange()
        {
            // No attack - just keep following
        }

        public override void OnStunComplete()
        {
            if (!IsAlive) return;

            if (HasTarget)
                stateMachine.ChangeState<ChaseState>();
            else
                ReturnToPatrol();
        }

        private void ReturnToPatrol()
        {
            // Set baseY to current position - Update() will gradually lerp it back to spawnY
            baseY = transform.position.y;

            if (HasPatrol)
                stateMachine.ChangeState<PatrolState>();
            else
                stateMachine.ChangeState<IdleState>();
        }

        // ============================================================
        // Death
        // ============================================================

        protected override void HandleDeath()
        {
            // Re-enable gravity on death so it falls
            if (rb != null)
            {
                rb.useGravity = true;
                rb.constraints = RigidbodyConstraints.FreezeRotation;
            }

            base.HandleDeath();
        }

        // ============================================================
        // Gizmos
        // ============================================================

        protected override void OnDrawGizmosSelected()
        {
            base.OnDrawGizmosSelected();
            patrol.DrawGizmos(transform);

            // Chase stop distance
            if (chase.ChaseStopDistance > 0f)
            {
                Gizmos.color = new Color(0.5f, 1f, 0.5f, 0.3f);
                Gizmos.DrawWireSphere(transform.position, chase.ChaseStopDistance);
            }

            // Hover range
            if (hoverBobAmount > 0f)
            {
                Gizmos.color = new Color(0.5f, 0.5f, 1f, 0.5f);
                float gizmoY = Application.isPlaying ? spawnY : transform.position.y;
                Vector3 center = new Vector3(transform.position.x, gizmoY, transform.position.z);
                Gizmos.DrawLine(center + Vector3.up * hoverBobAmount, center + Vector3.down * hoverBobAmount);
            }
        }
    }
}