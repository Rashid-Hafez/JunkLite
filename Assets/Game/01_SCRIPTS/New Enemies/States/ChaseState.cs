using UnityEngine;

namespace junklite
{
    /// <summary>
    /// Chase state - enemy runs toward the player.
    /// REUSABLE - calls enemy.OnPlayerInAttackRange() when close enough.
    /// </summary>
    public class ChaseState : EnemyStateBase
    {
        private EnemyMovement movement;
        private EnemyConfig config;

        public ChaseState(EnemyCharacter enemy) : base(enemy) { }

        public override void Enter()
        {
            movement = enemy.Movement;
            config = enemy.Config;

            if (movement != null && config != null)
                movement.MoveSpeed = config.chaseSpeed;

            Debug.Log($"{enemy.gameObject.name}: Chasing player!");
        }

        public override void Update()
        {
            // Lost target
            if (!HasTarget)
            {
                enemy.OnPlayerLost();
                return;
            }

            // In attack range - let enemy decide what to do
            if (IsTargetInAttackRange)
            {
                enemy.OnPlayerInAttackRange();
                return;
            }

            // Keep chasing
            movement?.MoveTo(Target.position);
            movement?.FaceTarget(Target.position);
        }

        public override void Exit()
        {
            movement?.Stop();
        }
    }
}