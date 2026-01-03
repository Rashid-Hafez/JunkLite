using UnityEngine;

namespace junklite
{
    /// <summary>
    /// Stunned state - enemy is being knocked back and cannot act.
    /// Pure REACTION state: waits for knockback to complete, then notifies enemy.
    /// Calls enemy.OnStunComplete() when done - enemy DECIDES what to do next.
    /// 
    /// This state is entered when EnemyMovement fires OnKnockbackStart.
    /// It exits when EnemyMovement fires OnKnockbackEnd (grounded + velocity decayed).
    /// 
    /// NOTE: The enemy class should subscribe to movement knockback events and
    /// transition to this state when knockback starts. The state itself just waits.
    /// </summary>
    public class StunnedState : EnemyStateBase
    {
        private EnemyMovement movement;
        private bool knockbackEnded;

        public StunnedState(EnemyCharacter enemy) : base(enemy) { }

        /// <summary>
        /// Whether the enemy can take damage while stunned.
        /// Set to true to allow stun-locking, false for stun immunity.
        /// </summary>
        public override bool CanTakeDamage => true;

        public override void Enter()
        {
            movement = enemy.Movement;
            knockbackEnded = false;

            // Stop any active movement commands (knockback will override)
            movement?.Stop();

            // Deactivate any active hitboxes (safety measure)
            enemy.DashHitbox?.Deactivate();

            // TODO: Play stun/hit animation
            // enemy.Animator?.SetTrigger("Stunned");
            // enemy.Animator?.SetBool("IsStunned", true);

            Debug.Log($"{enemy.gameObject.name}: Entered StunnedState");
        }

        public override void Update()
        {
            // The state mostly just waits for knockback to end.
            // EnemyMovement handles the physics and fires OnKnockbackEnd
            // which the enemy class catches and calls OnStunComplete().

            // Safety check: if knockback ended but we're still in this state
            if (!knockbackEnded && movement != null && !movement.IsInKnockback)
            {
                knockbackEnded = true;
                enemy.OnStunComplete();
            }
        }

        public override void FixedUpdate()
        {
            // Physics handled entirely by EnemyMovement
        }

        public override void Exit()
        {
            // TODO: Clear stun animation state
            // enemy.Animator?.SetBool("IsStunned", false);

            Debug.Log($"{enemy.gameObject.name}: Exited StunnedState");
        }

        /// <summary>
        /// Called by the enemy when knockback ends (from OnKnockbackEnd event).
        /// Marks the knockback as complete so Update can trigger OnStunComplete if needed.
        /// </summary>
        public void NotifyKnockbackEnded()
        {
            knockbackEnded = true;
        }
    }
}