using UnityEngine;

namespace junklite
{
    /// <summary>
    /// Dash state - enemy dashes to a target position.
    /// 
    /// REQUIRES: Enemy must implement IDasher
    /// 
    /// Pure ACTION state: moves enemy, enables hitbox, ends when destination reached.
    /// Calls IDasher.OnDashComplete() when done - enemy decides what to do next.
    /// </summary>
    public class DashState : EnemyStateBase
    {
        private IDasher dasher;
        private EnemyMovement movement;
        private Hitbox hitbox;

        private Vector3 dashTarget;
        private bool hasStarted;
        private bool dashComplete;

        // Cached VFX instance
        private GameObject activeVFX;

        private const float STOP_THRESHOLD = 0.5f;

        public DashState(EnemyCharacter enemy) : base(enemy) { }

        public override void Enter()
        {
            // Get capability interface
            dasher = enemy as IDasher;
            if (dasher == null)
            {
                Debug.LogError($"{enemy.gameObject.name}: DashState requires IDasher interface!");
                return;
            }

            movement = enemy.Movement;
            hitbox = dasher.DashHitbox;
            hasStarted = false;
            dashComplete = false;

            // Capture target position NOW (at moment of dash start)
            if (HasTarget)
            {
                dashTarget = Target.position;
                activeVFX = VFXPool.Get(dasher.DashVFXPrefab, enemy.transform);
                StartDash();
            }
            else
            {
                // No target - can't dash, let enemy decide
                dasher.OnDashComplete();
            }
        }

        private void StartDash()
        {
            hasStarted = true;

            // Activate hitbox
            hitbox?.Activate();

            // Start dash movement
            movement?.DashTo(dashTarget, dasher.DashSpeed);

            Debug.Log($"{enemy.gameObject.name}: Dashing! (speed: {dasher.DashSpeed})");
        }

        public override void Update()
        {
            if (dasher == null || !hasStarted || dashComplete) return;

            // Check if reached destination
            float distance = Vector3.Distance(Transform.position, dashTarget);

            if (distance <= STOP_THRESHOLD || (movement != null && movement.HasReachedDestination))
            {
                dashComplete = true;
                hitbox?.Deactivate();
                dasher.OnDashComplete();
            }
        }

        public override void Exit()
        {
            hitbox?.Deactivate();
            VFXPool.Release(ref activeVFX);
            movement?.Stop();
        }
    }
}