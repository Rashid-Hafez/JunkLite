using UnityEngine;

namespace junklite
{
    /// <summary>
    /// Dash state - enemy dashes toward a target, stopping at a safe distance.
    /// 
    /// REQUIRES: Enemy must implement IDasher
    /// 
    /// Pure ACTION state: moves enemy, enables hitbox, ends when close enough to target.
    /// Calls IDasher.OnDashComplete() when done - enemy decides what to do next.
    /// 
    /// Axis-agnostic: uses EnemyMovement.MovementAxis and helpers for all distance checks.
    /// </summary>
    public class DashState : EnemyStateBase
    {
        private IDasher dasher;
        private EnemyMovement movement;
        private Hitbox hitbox;

        private Vector3 dashTarget;
        private float stopDistance;
        private bool hasStarted;
        private bool dashComplete;
        private float dashStartTime;
        private GameObject activeVFX;

        private const float MAX_DASH_DURATION = 2f;

        public DashState(EnemyCharacter enemy) : base(enemy) { }

        public override void Enter()
        {
            hasStarted = false;
            dashComplete = false;
            dashStartTime = Time.time;

            dasher = GetCapability<IDasher>();
            if (dasher == null)
            {
                Debug.LogError($"{enemy.gameObject.name}: DashState requires IDasher interface!");
                return;
            }

            movement = enemy.Movement;
            hitbox = dasher.DashHitbox;
            stopDistance = dasher.DashStopDistance;

            if (HasTarget)
            {
                // Horizontal distance along the movement axis
                float distanceToTarget = movement.GetAbsAxisDistance(Transform.position, Target.position);

                if (distanceToTarget > stopDistance)
                {
                    // Direction along movement axis toward target
                    float sign = Mathf.Sign(movement.GetSignedAxisDistance(Transform.position, Target.position));
                    Vector3 horizontalDir = movement.MovementAxis * sign;

                    // Stop short of the target by stopDistance
                    dashTarget = Target.position - horizontalDir * stopDistance;

                    // Keep our Y and lock depth to our current position
                    dashTarget.y = Transform.position.y;
                    // Strip any depth-axis offset (keep enemy on its movement plane)
                    Vector3 depthAxis = Vector3.Cross(Vector3.up, movement.MovementAxis).normalized;
                    float depthOffset = Vector3.Dot(dashTarget - Transform.position, depthAxis);
                    dashTarget -= depthAxis * depthOffset;

                    activeVFX = VFXPool.Get(dasher.DashVFXPrefab, enemy.transform);
                    StartDash();
                }
                else
                {
                    dasher.OnDashComplete();
                }
            }
            else
            {
                dasher.OnDashComplete();
            }
        }

        private void StartDash()
        {
            hasStarted = true;

            movement?.FaceTarget(dashTarget);
            hitbox?.Activate();
            movement?.DashTo(dashTarget, dasher.DashSpeed);
        }

        public override void Update()
        {
            if (dasher == null || !hasStarted || dashComplete) return;

            // Safety timeout
            if (Time.time - dashStartTime > MAX_DASH_DURATION)
            {
                CompleteDash();
                return;
            }

            // Check if movement says we've arrived
            if (movement != null && movement.HasReachedDestination)
            {
                CompleteDash();
                return;
            }

            // Check distance to player (horizontal only, along movement axis)
            if (HasTarget)
            {
                float distanceToPlayer = movement.GetAbsAxisDistance(Transform.position, Target.position);
                if (distanceToPlayer <= stopDistance)
                {
                    CompleteDash();
                    return;
                }
            }

            // Check distance to dash target (horizontal only, along movement axis)
            float distanceToDashTarget = movement.GetAbsAxisDistance(Transform.position, dashTarget);
            if (distanceToDashTarget <= 0.3f)
                CompleteDash();
        }

        private void CompleteDash()
        {
            if (dashComplete) return;
            dashComplete = true;

            hitbox?.Deactivate();
            movement?.Stop();

            dasher.OnDashComplete();
        }

        public override void Exit()
        {
            dashComplete = true;
            hitbox?.Deactivate();
            VFXPool.Release(ref activeVFX);
            movement?.Stop();
        }
    }
}