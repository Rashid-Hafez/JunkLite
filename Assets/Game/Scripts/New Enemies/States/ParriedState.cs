using UnityEngine;

namespace junklite
{
    /// <summary>
    /// Parried state - enemy was successfully parried by the player.
    /// 
    /// Universal state - works with any enemy.
    /// 
    /// This state:
    /// - Cannot be interrupted by reactive behaviors (dodge, chase tracking, etc.)
    /// - Respects the ForcedStunDuration timer set by ParryHandler
    /// - Calls enemy.OnParryComplete() when done - enemy decides what to do next
    /// - Is scalable for future features (execution prompts, special animations)
    /// </summary>
    public class ParriedState : EnemyStateBase
    {
        private float timer;

        public ParriedState(EnemyCharacter enemy) : base(enemy) { }

        public override bool CanTakeDamage => true;
        public override bool CanBeInterrupted => false;

        public override void Enter()
        {
            timer = enemy.ForcedStunDuration;
            enemy.Movement?.Stop();
            enemy.Movement?.CancelKnockback();
        }

        public override void Update()
        {
            timer -= Time.deltaTime;
            if (timer <= 0f)
            {
                enemy.ForcedStunDuration = 0f;
                enemy.OnParryComplete();
            }
        }

        public override void Exit()
        {
            enemy.ClearParryStunFlag();
        }
    }
}
