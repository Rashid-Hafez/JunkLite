using UnityEngine;

namespace junklite
{
    public class ChaseState : EnemyStateBase
    {
        private IChaser chaser;
        private EnemyMovement movement;
        private EnemyConfig config;
        private bool destinationReported;

        public ChaseState(EnemyCharacter enemy) : base(enemy) { }

        public override void Enter()
        {
            movement = enemy.Movement;
            config = enemy.Config;
            chaser = GetCapability<IChaser>();
            destinationReported = false;

            if (movement != null)
            {
                if (chaser != null)
                    movement.MoveSpeed = chaser.ChaseSpeed;
                else if (config != null)
                    movement.MoveSpeed = config.chaseSpeed;
            }
        }

        public override void Update()
        {
            if (HasTarget)
            {
                destinationReported = false;
                chaser?.UpdateLastKnownPosition(Target.position);

                float stopDistance = chaser != null && chaser.ChaseStopDistance > 0f
                    ? chaser.ChaseStopDistance
                    : enemy.AttackRange;

                if (stopDistance > 0f)
                {
                    float distanceToTarget = movement.GetAbsAxisDistance(Transform.position, Target.position);
                    if (distanceToTarget <= stopDistance)
                    {
                        movement?.Stop();
                        movement?.FaceTarget(Target.position);
                        ReportDestinationReached();
                        return;
                    }
                }

                movement?.MoveTo(Target.position);
                movement?.FaceTarget(Target.position);
            }
            else if (chaser != null && chaser.HasLastKnownPosition)
            {
                float horizontalDist = enemy.Movement.GetAbsAxisDistance(Transform.position, chaser.LastKnownTargetPosition);

                if (horizontalDist <= 1f)
                {
                    ReportDestinationReached();
                    return;
                }

                movement?.MoveTo(chaser.LastKnownTargetPosition);
                movement?.FaceTarget(chaser.LastKnownTargetPosition);
            }
            else
            {
                ReportDestinationReached();
            }
        }

        private void ReportDestinationReached()
        {
            if (destinationReported)
                return;

            destinationReported = true;

            if (chaser != null)
                chaser.OnReachedTarget();
            else
                enemy.OnPlayerInAttackRange();
        }

        public override void Exit()
        {
            destinationReported = false;
            movement?.Stop();
        }
    }
}
