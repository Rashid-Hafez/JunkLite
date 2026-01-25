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
            chaser = GetCapability<IChaser>();

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
                chaser?.UpdateLastKnownPosition(Target.position);

                // Check chase stop distance first (if set)
                if (chaser != null && chaser.ChaseStopDistance > 0f)
                {
                    float distanceToTarget = Vector3.Distance(Transform.position, Target.position);
                    if (distanceToTarget <= chaser.ChaseStopDistance)
                    {
                        movement?.Stop();
                        movement?.FaceTarget(Target.position);
                        enemy.OnPlayerInAttackRange();  // Let enemy decide what to do
                        return;
                    }
                }
                // Then check attack range
                else if (IsTargetInAttackRange)
                {
                    enemy.OnPlayerInAttackRange();
                    return;
                }

                movement?.MoveTo(Target.position);
                movement?.FaceTarget(Target.position);
            }
            else if (chaser != null && chaser.HasLastKnownPosition)
            {
                float distanceToLastKnown = Vector3.Distance(Transform.position, chaser.LastKnownTargetPosition);

                if (distanceToLastKnown <= 1f)
                {
                    enemy.OnPlayerLost();
                    return;
                }

                movement?.MoveTo(chaser.LastKnownTargetPosition);
                movement?.FaceTarget(chaser.LastKnownTargetPosition);
            }
            else
            {
                enemy.OnPlayerLost();
            }
        }

        public override void Exit()
        {
            movement?.Stop();
        }
    }
}