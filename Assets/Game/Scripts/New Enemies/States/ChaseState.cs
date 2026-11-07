using UnityEngine;

namespace junklite
{
    public class ChaseState : EnemyStateBase
    {
        private IChaser chaser;
        private IChaseDestinationProvider destinationProvider;
        private ILaneSpacing spacing;
        private EnemyMovement movement;
        private EnemyConfig config;
        private bool destinationReported;

        public ChaseState(EnemyCharacter enemy) : base(enemy) { }

        public override void Enter()
        {
            movement = enemy.Movement;
            config = enemy.Config;
            chaser = GetCapability<IChaser>();
            destinationProvider = GetCapability<IChaseDestinationProvider>();
            spacing = GetCapability<ILaneSpacing>();
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
                chaser?.UpdateLastKnownPosition(Target.position);

                float stopDistance = chaser != null && chaser.ChaseStopDistance > 0f
                    ? chaser.ChaseStopDistance
                    : enemy.AttackRange;

                Vector3 destination = Target.position;
                bool hasAssignedDestination = destinationProvider != null
                    && destinationProvider.TryGetChaseDestination(
                        Target.position,
                        stopDistance,
                        out destination,
                        out stopDistance);

                // Lane spacing: never chase into the body ahead, and match its speed
                // when close instead of shoving it toward the player.
                float speed = movement != null ? movement.MoveSpeed : 0f;
                if (spacing != null)
                    spacing.ApplySpacing(ref destination, ref speed);

                if (stopDistance > 0f)
                {
                    float distanceToTarget = movement.GetAbsAxisDistance(Transform.position, destination);
                    if (distanceToTarget <= stopDistance)
                    {
                        movement?.Stop();
                        movement?.FaceTarget(Target.position);
                        SetSpacedLocomotion(0f);
                        if (hasAssignedDestination)
                            ReportAssignedDestinationReached();
                        else
                            ReportDestinationReached();
                        return;
                    }
                }

                destinationReported = false;

                if (spacing != null)
                {
                    if (speed < 0.05f)
                    {
                        movement?.Stop();
                        SetSpacedLocomotion(0f);
                    }
                    else
                    {
                        movement?.MoveTo(destination, speed);
                        SetSpacedLocomotion(speed);
                    }
                }
                else
                {
                    movement?.MoveTo(destination);
                }

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

        /// <summary>
        /// Only for spacing-aware enemies: they can be slowed or held by the body
        /// ahead while in this state, so the run clip must follow the real speed.
        /// </summary>
        private void SetSpacedLocomotion(float speed)
        {
            if (spacing == null)
                return;

            float reference = chaser != null && chaser.ChaseSpeed > 0f
                ? chaser.ChaseSpeed
                : Mathf.Max(0.01f, movement != null ? movement.MoveSpeed : 1f);
            enemy.AnimationPresenter?.SetLocomotionSpeed01(Mathf.Clamp01(speed / reference));
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

        private void ReportAssignedDestinationReached()
        {
            if (destinationReported)
                return;

            destinationReported = true;
            destinationProvider.OnChaseDestinationReached();
        }

        public override void Exit()
        {
            destinationReported = false;
            movement?.Stop();
        }
    }
}
