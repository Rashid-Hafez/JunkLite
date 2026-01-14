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
            Debug.Log($"[DeadState] {enemy.gameObject.name} entered DeadState");

            // Stop all movement
            enemy.Movement?.Stop();

            // Disable detection
            if (enemy.DetectionZone != null)
                enemy.DetectionZone.enabled = false;
        }

        public override void Update()
        {
            // Dead enemies don't do anything
        }

        public override void Exit()
        {
            // Typically won't exit this state unless respawning
            Debug.Log($"[DeadState] {enemy.gameObject.name} exiting DeadState (respawn?)");
        }
    }
}