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
                Vector3 toTarget = Target.position - Transform.position;
                toTarget.y = 0f;
                toTarget.z = 0f; // 2.5D: only horizontal distance matters
                float distanceToTarget = toTarget.magnitude;

                if (distanceToTarget > stopDistance)
                {
                    Vector3 direction = toTarget.normalized;
                    dashTarget = Target.position - direction * stopDistance;
                    dashTarget.y = Transform.position.y;
                    dashTarget.z = Transform.position.z; // Lock Z depth

                    activeVFX = VFXPool.Get(dasher.DashVFXPrefab, enemy.transform);
                    StartDash();
                }
                else
                {
                    // Debug.Log($"{enemy.gameObject.name}: Already within stop distance, skipping dash.");
                    dasher.OnDashComplete();
                }
            }
            else
            {
                // Debug.Log($"{enemy.gameObject.name}: No target for dash.");
                dasher.OnDashComplete();
            }
        }

        private void StartDash()
        {
            hasStarted = true;

            movement?.FaceTarget(dashTarget);
            hitbox?.Activate();
            movement?.DashTo(dashTarget, dasher.DashSpeed);

            //Debug.Log($"{enemy.gameObject.name}: Dashing! (speed: {dasher.DashSpeed}, stopDistance: {stopDistance})");
        }

        public override void Update()
        {
            if (dasher == null || !hasStarted || dashComplete) return;

            // Safety timeout
            if (Time.time - dashStartTime > MAX_DASH_DURATION)
            {
                // Debug.LogWarning($"{enemy.gameObject.name}: Dash timeout!");
                CompleteDash();
                return;
            }

            // Check if movement says we've arrived
            if (movement != null && movement.HasReachedDestination)
            {
                CompleteDash();
                return;
            }

            // Check distance to player (horizontal only)
            if (HasTarget)
            {
                float distanceToPlayer = Mathf.Abs(Transform.position.x - Target.position.x);
                if (distanceToPlayer <= stopDistance)
                {
                    CompleteDash();
                    return;
                }
            }

            // Check distance to dash target (horizontal only)
            float distanceToDashTarget = Mathf.Abs(Transform.position.x - dashTarget.x);
            if (distanceToDashTarget <= 0.3f)
                CompleteDash();
        }

        private void CompleteDash()
        {
            if (dashComplete) return;
            dashComplete = true;

            hitbox?.Deactivate();
            movement?.Stop();

            //Debug.Log($"{enemy.gameObject.name}: Dash complete!");
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