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
        private float stunTimer = 0f;

        public StunnedState(EnemyCharacter enemy) : base(enemy) { }

        public override bool CanTakeDamage => true;

        public override void Enter()
        {
            movement = enemy.Movement;
            knockbackEnded = false;
            // read any externally-forced stun duration (e.g. from parry)
            stunTimer = enemy.ForcedStunDuration;

            // stop movement immediately
            movement?.Stop();

            Debug.Log($"{enemy.gameObject.name}: Entered StunnedState (timer={stunTimer})");
        }

        public override void Update()
        {
            // If a forced timer is present, wait for it to expire
            if (stunTimer > 0f)
            {
                stunTimer -= Time.deltaTime;
                if (stunTimer <= 0f)
                {
                    // clear forced stun so future entries don't reuse it
                    enemy.ForcedStunDuration = 0f;
                    enemy.OnStunComplete();
                }
                return;
            }

            // Otherwise, wait for knockback to finish as before
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