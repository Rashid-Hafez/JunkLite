using UnityEngine;

namespace junklite
{
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
                chaser?.UpdateLastKnownPosition(Target.position);

                if (chaser != null && chaser.ChaseStopDistance > 0f)
                {
                    float horizontalDist = movement.GetAbsAxisDistance(Transform.position, Target.position);
                    float verticalDist = Mathf.Abs(Transform.position.y - Target.position.y);

                    if (horizontalDist <= chaser.ChaseStopDistance && verticalDist <= enemy.AttackRange)
                    {
                        movement?.Stop();
                        movement?.FaceTarget(Target.position);
                        enemy.OnPlayerInAttackRange();
                        return;
                    }
                }
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
                float horizontalDist = enemy.Movement.GetAbsAxisDistance(Transform.position, chaser.LastKnownTargetPosition);

                if (horizontalDist <= 1f)
                {
                    chaser.OnReachedTarget();
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