using UnityEngine;

namespace junklite
{
    /// <summary>
    /// Brief hitstun on direct damage. Enables combos.
    /// 
    /// - Entered automatically by EnemyCharacter.TakeDamage (non-tick damage)
    /// - Stops movement on enter (interrupts chase, patrol, etc.)
    /// - Timer resets on each new hit (ResetTimer) so combos keep the enemy locked
    /// - Waits for BOTH timer AND knockback to finish before exiting
    /// - Calls enemy.OnHurtComplete() when done — enemy decides what to do next
    /// </summary>
    public class HurtState : EnemyStateBase
    {
        private float timer;
        private float duration;

        public HurtState(EnemyCharacter enemy) : base(enemy) { }

        public override bool CanTakeDamage => true;

        public override void Enter()
        {
            duration = enemy.HitstunDuration;
            timer = duration;

            // Stop active movement (chase, patrol, etc.)
            // Knockback is applied AFTER this by EnemyCharacter.TakeDamage,
            // so it won't be cancelled.
            enemy.Movement?.Stop();
        }

        public override void Update()
        {
            timer -= Time.deltaTime;

            // Wait for both: hitstun timer expired AND knockback finished
            if (timer <= 0f && (enemy.Movement == null || !enemy.Movement.IsInKnockback))
            {
                enemy.OnHurtComplete();
            }
        }

        /// <summary>
        /// Called by EnemyCharacter when taking another hit while already in HurtState.
        /// Resets the timer so the enemy stays locked during combos.
        /// </summary>
        public void ResetTimer()
        {
            timer = duration;
        }

        public override void Exit() { }
    }
}