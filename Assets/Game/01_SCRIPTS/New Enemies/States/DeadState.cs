using UnityEngine;

namespace junklite
{
    /// <summary>
    /// State when the enemy has died.
    /// Handles death animation, loot drops, cleanup, etc.
    /// </summary>
    public class DeadState : EnemyStateBase
    {
        public DeadState(EnemyCharacter enemy) : base(enemy) { }

        public override void Enter()
        {
            Debug.Log($"[DeadState] {enemy.gameObject.name} entered DeadState");

            // Stop all movement
            enemy.Movement?.Stop();

            // Disable detection
            if (enemy.DetectionZone != null)
                enemy.DetectionZone.enabled = false;

            // Disable hitbox
            enemy.DashHitbox?.Deactivate();

            // Play death animation if available
            // enemy.AnimationController?.Play("Death");

            // Optional: Disable colliders so player can walk through
            // DisableColliders();

            // Optional: Start death sequence (fade out, loot drop, etc.)
            // enemy.StartCoroutine(DeathSequence());
        }

        public override void Update()
        {
            // Dead enemies don't do anything
            // Could wait for animation to complete before despawning
        }

        public override void Exit()
        {
            // Typically won't exit this state unless respawning
            Debug.Log($"[DeadState] {enemy.gameObject.name} exiting DeadState (respawn?)");
        }

        // Optional helper methods
        private void DisableColliders()
        {
            var colliders = enemy.GetComponentsInChildren<Collider>();
            foreach (var col in colliders)
            {
                col.enabled = false;
            }
        }
    }
}