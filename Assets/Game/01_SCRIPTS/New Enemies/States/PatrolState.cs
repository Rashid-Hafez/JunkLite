using UnityEngine;

namespace junklite
{
    /// <summary>
    /// Patrol state - enemy walks back and forth within patrol distance.
    /// Reverses direction at boundaries or when hitting walls.
    /// 
    /// REQUIRES: Enemy must implement IPatroller
    /// 
    /// Pure ACTION state: just handles movement.
    /// Detection is handled by DetectionZone trigger events.
    /// </summary>
    public class PatrolState : EnemyStateBase
    {
        private IPatroller patroller;
        private EnemyMovement movement;

        public PatrolState(EnemyCharacter enemy) : base(enemy) { }

        public override void Enter()
        {
            patroller = GetCapability<IPatroller>();
            if (patroller == null)
            {
                Debug.LogError($"{enemy.gameObject.name}: PatrolState requires IPatroller interface!");
                return;
            }

            movement = enemy.Movement;

            if (movement != null)
                movement.MoveSpeed = patroller.PatrolSpeed;

            StartMoving();
        }

        public override void Update()
        {
            if (patroller == null) return;

            if (patroller.IsWallAhead())
            {
                patroller.ReverseDirection();
                StartMoving();
                return;
            }

            if (patroller.IsAtPatrolBoundary())
            {
                patroller.ReverseDirection();
                StartMoving();
            }
        }

        public override void Exit()
        {
            movement?.Stop();
        }

        private void StartMoving()
        {
            if (movement == null || patroller == null) return;

            Vector3 direction = patroller.PatrolDirection > 0 ? Vector3.right : Vector3.left;
            movement.MoveInDirection(direction);
        }
    }
}