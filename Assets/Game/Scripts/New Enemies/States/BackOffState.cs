using UnityEngine;

namespace junklite
{
    /// <summary>
    /// Gives ground: backpedals along the lane to a brain-chosen distance from
    /// the player while facing the player, holds there for a moment, then
    /// reports completion through IBackOff. The brain decides what comes next.
    /// </summary>
    public sealed class BackOffState : EnemyStateBase
    {
        private enum Phase
        {
            Retreat,
            Hold
        }

        private const float ArriveTolerance = 0.1f;

        private IBackOff backOff;
        private IChaser chaser;
        private ILaneSpacing spacing;
        private EnemyMovement movement;

        private Phase phase;
        private float timer;
        private Vector3 retreatPoint;
        private bool finished;

        public BackOffState(EnemyCharacter enemy) : base(enemy) { }

        public override void Enter()
        {
            backOff = GetCapability<IBackOff>();
            chaser = GetCapability<IChaser>();
            spacing = GetCapability<ILaneSpacing>();
            movement = enemy.Movement;
            finished = false;

            if (backOff == null || movement == null || !HasTarget)
            {
                Debug.LogWarning($"{enemy.gameObject.name}: BackOffState needs IBackOff and a target.");
                Finish();
                return;
            }

            Vector3 targetPosition = Target.position;
            float signed = movement.GetSignedAxisDistance(Transform.position, targetPosition);
            int towardSign = signed >= 0f ? 1 : -1;
            float currentDistance = Mathf.Abs(signed);
            float retreat = Mathf.Max(0f, backOff.BackOffTargetDistance - currentDistance);

            Vector3 axis = movement.MovementAxis;
            Vector3 direction = axis * -towardSign;
            retreat = ClampToObstacles(direction, retreat);

            retreatPoint = Transform.position + direction * retreat;
            phase = Phase.Retreat;
            timer = Mathf.Max(0.1f, backOff.BackOffTimeout);

            if (retreat <= ArriveTolerance)
                BeginHold();
        }

        public override void Update()
        {
            if (finished)
                return;

            timer -= Time.deltaTime;

            if (phase == Phase.Retreat)
            {
                // Never back into the enemy behind: spacing may shorten the retreat
                // or slow it down while that enemy is still moving out of the way.
                Vector3 point = retreatPoint;
                float speed = backOff.BackOffSpeed;
                spacing?.ApplySpacing(ref point, ref speed);

                bool arrived = movement.GetAbsAxisDistance(Transform.position, point) <= ArriveTolerance;
                if (timer <= 0f || arrived || speed < 0.05f)
                {
                    BeginHold();
                    return;
                }

                movement.MoveTo(point, speed);
                FaceTarget();
                enemy.AnimationPresenter?.SetLocomotionSpeed01(Normalize(speed));
                return;
            }

            FaceTarget();
            if (timer <= 0f)
                Finish();
        }

        public override void Exit()
        {
            movement?.Stop();
            enemy.AnimationPresenter?.SetLocomotionSpeed01(0f);
        }

        // ------------------------------------------------------------

        private void BeginHold()
        {
            phase = Phase.Hold;
            Vector2 range = backOff.BackOffHoldRange;
            timer = Random.Range(Mathf.Min(range.x, range.y), Mathf.Max(range.x, range.y));
            movement.Stop();
            FaceTarget();
            enemy.AnimationPresenter?.SetLocomotionSpeed01(0f);
        }

        private void Finish()
        {
            if (finished)
                return;

            finished = true;
            backOff?.OnBackOffComplete();
        }

        private float ClampToObstacles(Vector3 direction, float distance)
        {
            LayerMask mask = backOff.BackOffObstacleLayers;
            if (mask.value == 0 || distance <= 0f)
                return distance;

            Vector3 origin = Transform.position + Vector3.up * 0.5f;
            if (Physics.Raycast(origin, direction, out RaycastHit hit, distance + 0.3f, mask, QueryTriggerInteraction.Ignore))
                return Mathf.Max(0f, hit.distance - 0.3f);

            return distance;
        }

        private float Normalize(float speed)
        {
            float reference = chaser != null && chaser.ChaseSpeed > 0f
                ? chaser.ChaseSpeed
                : Mathf.Max(0.01f, movement.MoveSpeed);
            return Mathf.Clamp01(speed / reference);
        }

        private void FaceTarget()
        {
            if (HasTarget)
                movement?.FaceTarget(Target.position);
        }
    }
}
