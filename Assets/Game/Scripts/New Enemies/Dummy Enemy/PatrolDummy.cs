using UnityEngine;

namespace junklite
{
    /// <summary>
    /// Basic patrol enemy that walks back and forth.
    /// No combat capabilities - just patrols and takes damage.
    /// 
    /// CAPABILITIES: IPatroller
    /// </summary>
    public class PatrolEnemy : EnemyCharacter, IPatroller
    {
        [Header("Patrol")]
        [SerializeField] private PatrolBehavior patrol = new PatrolBehavior();

        protected override void Awake()
        {
            base.Awake();
            enemyType = EnemyType.Dummy;
            patrol.Initialize(transform);
        }

        protected override void InitializeStateMachine()
        {
            stateMachine.RegisterStates(
                new PatrolState(this),
                new IdleState(this),
                new DeadState(this)
            );

            stateMachine.SetInitialState<PatrolState>();
        }

        // ============================================================
        // IPatroller - Delegates to PatrolBehavior
        // ============================================================

        public float PatrolDistance => patrol.PatrolDistance;
        public float PatrolSpeed => patrol.PatrolSpeed;
        public Vector3 SpawnPosition => patrol.SpawnPosition;
        public int PatrolDirection
        {
            get => patrol.PatrolDirection;
            set => patrol.PatrolDirection = value;
        }
        public bool IsWallAhead() => patrol.IsWallAhead();
        public bool IsAtPatrolBoundary() => patrol.IsAtPatrolBoundary();
        public void ReverseDirection() => patrol.ReverseDirection();

        // ============================================================
        // Behavior Overrides
        // ============================================================

        public override void OnPlayerSpotted()
        {
            // Do nothing - continue patrolling
        }

        public override void OnPlayerLost()
        {
            // Do nothing - continue patrolling
        }

        public override void OnStunComplete()
        {
            if (IsAlive)
                stateMachine.ChangeState<PatrolState>();
        }

        // ============================================================
        // Debug Gizmos
        // ============================================================

        protected override void OnDrawGizmosSelected()
        {
            base.OnDrawGizmosSelected();
            patrol.DrawGizmos(transform);
        }
    }
}