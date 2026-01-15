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
        private float stopDistanceFromPlayer;
        private bool hasStarted;
        private bool dashComplete;
        private float dashStartTime;
        private float maxDashDuration = 2f; // Safety timeout

        // Cached VFX instance
        private GameObject activeVFX;

        // Default stop distance from player
        private const float DEFAULT_STOP_DISTANCE = 0.5f;

        public DashState(EnemyCharacter enemy) : base(enemy) { }

        public override void Enter()
        {
            // Reset state
            hasStarted = false;
            dashComplete = false;
            dashStartTime = Time.time;

            // Get capability interface
            dasher = enemy as IDasher;
            if (dasher == null)
            {
                Debug.LogError($"{enemy.gameObject.name}: DashState requires IDasher interface!");
                return;
            }

            movement = enemy.Movement;
            hitbox = dasher.DashHitbox;

            // Get stop distance - use interface property if available, otherwise default
            stopDistanceFromPlayer = GetStopDistance();

            // Capture target position NOW (at moment of dash start)
            if (HasTarget)
            {
                // Calculate dash target: stop short of player by stopDistance
                Vector3 toTarget = Target.position - Transform.position;
                toTarget.y = 0f; // Keep on same Y plane
                float distanceToTarget = toTarget.magnitude;

                if (distanceToTarget > stopDistanceFromPlayer)
                {
                    // Dash to a point that's stopDistance away from the target
                    Vector3 direction = toTarget.normalized;
                    dashTarget = Target.position - direction * stopDistanceFromPlayer;
                    dashTarget.y = Transform.position.y; // Keep Y position

                    activeVFX = VFXPool.Get(dasher.DashVFXPrefab, enemy.transform);
                    StartDash();
                }
                else
                {
                    // Already within stop distance - complete immediately
                    Debug.Log($"{enemy.gameObject.name}: Already within stop distance, skipping dash.");
                    dasher.OnDashComplete();
                }
            }
            else
            {
                // No target - can't dash, let enemy decide
                Debug.Log($"{enemy.gameObject.name}: No target for dash.");
                dasher.OnDashComplete();
            }
        }

        private float GetStopDistance()
        {
            // Use the interface property directly
            return dasher.DashStopDistance;
        }

        private void StartDash()
        {
            hasStarted = true;

            // Face target before dashing
            if (movement != null)
                movement.FaceTarget(dashTarget);

            // Activate hitbox
            hitbox?.Activate();

            // Use DashTo for proper dash movement (sets isDashing = true internally)
            movement?.DashTo(dashTarget, dasher.DashSpeed);

            Debug.Log($"{enemy.gameObject.name}: Dashing! (speed: {dasher.DashSpeed}, stopDistance: {stopDistanceFromPlayer})");
        }

        public override void Update()
        {
            if (dasher == null || !hasStarted || dashComplete) return;

            // Safety timeout
            if (Time.time - dashStartTime > maxDashDuration)
            {
                Debug.LogWarning($"{enemy.gameObject.name}: Dash timeout!");
                CompleteDash();
                return;
            }

            // Check if movement system says we've arrived
            if (movement != null && movement.HasReachedDestination)
            {
                CompleteDash();
                return;
            }

            // Check distance to actual target (player) - in case they moved
            if (HasTarget)
            {
                float distanceToPlayer = Vector3.Distance(Transform.position, Target.position);
                if (distanceToPlayer <= stopDistanceFromPlayer)
                {
                    CompleteDash();
                    return;
                }
            }

            // Check distance to our calculated dash target
            float distanceToDashTarget = Vector3.Distance(Transform.position, dashTarget);
            if (distanceToDashTarget <= 0.3f)
            {
                CompleteDash();
            }
        }

        private void CompleteDash()
        {
            if (dashComplete) return;
            dashComplete = true;

            // Deactivate hitbox first
            hitbox?.Deactivate();

            // Stop movement completely - this zeros velocity
            movement?.Stop();

            Debug.Log($"{enemy.gameObject.name}: Dash complete!");

            // Notify enemy AFTER stopping movement
            dasher.OnDashComplete();
        }

        public override void Exit()
        {
            // Ensure everything is cleaned up even if state is force-exited
            dashComplete = true;
            hitbox?.Deactivate();
            VFXPool.Release(ref activeVFX);
            movement?.Stop();
        }
    }
}