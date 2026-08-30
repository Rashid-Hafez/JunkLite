using UnityEngine;

namespace junklite
{
    /// <summary>
    /// State when the enemy has died.
    /// 
    /// Universal state - works with any enemy.
    /// Handles death animation, cleanup, etc.
    /// </summary>
    public class DeadState : EnemyStateBase
    {
        public DeadState(EnemyCharacter enemy) : base(enemy) { }

        public override bool CanTakeDamage => false;

        public override void Enter()
        {
            enemy.Movement?.Stop();

            if (enemy.Perception != null)
                enemy.Perception.enabled = false;
        }

        public override void Update()
        {
            // Dead enemies don't do anything
        }
    }
}
