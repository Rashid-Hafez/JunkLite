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

        public override bool CanTakeDamage => true;

        public override void Enter()
        {
            movement = enemy.Movement;
            knockbackEnded = false;

            movement?.Stop();

            Debug.Log($"{enemy.gameObject.name}: Entered StunnedState");
        }

        public override void Update()
        {
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
        /// </summary>
        public void NotifyKnockbackEnded()
        {
            knockbackEnded = true;
        }
    }
}