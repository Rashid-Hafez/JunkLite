using UnityEngine;

namespace junklite
{
    /// <summary>
    /// Stunned state - enemy is being knocked back and cannot act.
    /// 
    /// Universal state - works with any enemy.
    /// 
    /// Pure REACTION state: waits for knockback to complete, then notifies enemy.
    /// Calls enemy.OnStunComplete() when done - enemy decides what to do next.
    /// 
    /// This state is entered when EnemyMovement fires OnKnockbackStart.
    /// It exits when EnemyMovement fires OnKnockbackEnd (grounded + velocity decayed).
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

            Debug.Log($"{enemy.gameObject.name}: Entered StunnedState");
        }

        public override void Update()
        {
            // Safety check: if knockback ended but we're still in this state
            if (!knockbackEnded && movement != null && !movement.IsInKnockback)
            {
                knockbackEnded = true;
                enemy.OnStunComplete();
            }
        }

        public override void Exit()
        {
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