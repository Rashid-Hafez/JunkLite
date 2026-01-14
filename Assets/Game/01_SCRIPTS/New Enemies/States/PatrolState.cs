using UnityEngine;

namespace junklite
{
    /// <summary>
    /// Patrol state - enemy walks back and forth within patrol distance.
    /// Reverses direction at boundaries or when hitting walls.
    /// 
    /// Universal state - works with any enemy that has patrol distance set.
    /// Detection is handled by DetectionZone trigger events.
    /// </summary>
    public class PatrolState : EnemyStateBase
    {
        private EnemyMovement movement;
        private EnemyConfig config;

        public PatrolState(EnemyCharacter enemy) : base(enemy) { }

        public override void Enter()
        {
            movement = enemy.Movement;
            config = enemy.Config;

            if (movement != null && config != null)
                movement.MoveSpeed = config.patrolSpeed;

            StartMoving();
        }

        public override void Update()
        {
            if (enemy.IsWallAhead())
            {
                enemy.ReverseDirection();
                StartMoving();
                return;
            }

            if (enemy.IsAtPatrolBoundary())
            {
                enemy.ReverseDirection();
                StartMoving();
            }
        }

        public override void Exit()
        {
            movement?.Stop();
        }

        private void StartMoving()
        {
            if (movement == null) return;

            Vector3 direction = enemy.PatrolDirection > 0 ? Vector3.right : Vector3.left;
            movement.MoveInDirection(direction);
        }
    }
}