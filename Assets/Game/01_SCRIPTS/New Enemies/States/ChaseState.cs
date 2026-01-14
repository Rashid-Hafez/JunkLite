using UnityEngine;

namespace junklite
{
    /// <summary>
    /// Chase state - enemy runs toward the player.
    /// 
    /// OPTIONAL: Enemy can implement IChaser for custom chase speed and last known position tracking.
    /// If not implemented, falls back to EnemyConfig.chaseSpeed.
    /// 
    /// Calls enemy.OnPlayerInAttackRange() when close enough.
    /// Calls enemy.OnPlayerLost() if target is lost.
    /// </summary>
    public class ChaseState : EnemyStateBase
    {
        private IChaser chaser;
        private EnemyMovement movement;
        private EnemyConfig config;

        public ChaseState(EnemyCharacter enemy) : base(enemy) { }

        public override void Enter()
        {
            movement = enemy.Movement;
            config = enemy.Config;
            chaser = enemy as IChaser;

            // Set chase speed - prefer IChaser, fallback to config
            if (movement != null)
            {
                if (chaser != null)
                    movement.MoveSpeed = chaser.ChaseSpeed;
                else if (config != null)
                    movement.MoveSpeed = config.chaseSpeed;
            }

            Debug.Log($"{enemy.gameObject.name}: Chasing player!");
        }

        public override void Update()
        {
            if (HasTarget)
            {
                // Update last known position if enemy supports it
                chaser?.UpdateLastKnownPosition(Target.position);

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
            else if (chaser != null && chaser.HasLastKnownPosition)
            {
                // No target but have last known position - move there
                float distanceToLastKnown = Vector3.Distance(Transform.position, chaser.LastKnownTargetPosition);

                if (distanceToLastKnown <= 1f)
                {
                    // Reached last known position
                    enemy.OnPlayerLost();
                    return;
                }

                movement?.MoveTo(chaser.LastKnownTargetPosition);
                movement?.FaceTarget(chaser.LastKnownTargetPosition);
            }
            else
            {
                // Lost target completely
                enemy.OnPlayerLost();
            }
        }

        public override void Exit()
        {
            movement?.Stop();
        }
    }
}