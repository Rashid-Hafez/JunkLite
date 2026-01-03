using UnityEngine;

namespace junklite
{
    /// <summary>
    /// Dash state - enemy dashes to a target position.
    /// Pure ACTION state: moves enemy, enables hitbox, ends when destination reached.
    /// Calls enemy.OnDashComplete() when done - enemy DECIDES what to do next.
    /// 
    /// Requires a Hitbox component reference for damage (set via enemy.DashHitbox).
    /// 
    /// NOTE: The hitbox is activated on dash start. If the enemy's OnDashHitboxHit
    /// handler wants single-hit behavior (e.g., for grabs), it should deactivate
    /// the hitbox in that handler to prevent multiple damage ticks.
    /// </summary>
    public class DashState : EnemyStateBase
    {
        private EnemyMovement movement;
        private Hitbox hitbox;

        private Vector3 dashTarget;
        private bool hasStarted;
        private bool dashComplete;

        private const float STOP_THRESHOLD = 0.5f;

        public DashState(EnemyCharacter enemy) : base(enemy) { }

        public override void Enter()
        {
            movement = enemy.Movement;
            hitbox = enemy.DashHitbox;
            hasStarted = false;
            dashComplete = false;

            // Capture target position NOW (at moment of dash start)
            if (HasTarget)
            {
                dashTarget = Target.position;
                enemy.SpawnDashVFX();
                StartDash();
            }
            else
            {
                // No target - can't dash, let enemy decide
                enemy.OnDashComplete();
            }
        }

        private void StartDash()
        {
            hasStarted = true;

            // Activate hitbox - enemy's OnDashHitboxHit handler decides what happens on hit
            // Handler can deactivate hitbox early if needed (e.g., for grab attacks)
            hitbox?.Activate();

            // Start dash movement
            movement?.DashTo(dashTarget, enemy.DashSpeed);

            // TODO: Play dash animation
            // enemy.Animator?.SetTrigger("Dash");

            Debug.Log($"{enemy.gameObject.name}: Dashing! (speed: {enemy.DashSpeed})");
        }

        public override void Update()
        {
            if (!hasStarted || dashComplete) return;

            // Check if reached destination
            float distance = Vector3.Distance(Transform.position, dashTarget);

            if (distance <= STOP_THRESHOLD || (movement != null && movement.HasReachedDestination))
            {
                dashComplete = true;

                // Deactivate hitbox immediately when dash ends
                hitbox?.Deactivate();

                // Dash complete - let enemy decide what to do
                enemy.OnDashComplete();
            }
        }

        public override void Exit()
        {
            // Ensure hitbox is disabled (safety, in case we exit early)
            hitbox?.Deactivate();

            // Stop dash VFX
            enemy.DestroyDashVFX();

            // Stop movement
            movement?.Stop();
        }
    }
}