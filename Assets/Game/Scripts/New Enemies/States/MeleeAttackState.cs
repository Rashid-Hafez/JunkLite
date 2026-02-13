using UnityEngine;

namespace junklite
{
    /// <summary>
    /// Simple melee attack state.
    /// Animation controller handles:
    /// - Playing animation (via state change detection)
    /// - Hitbox enable/disable via Spine events or timer
    /// - Calling OnMeleeComplete() when animation finishes
    /// 
    /// This state just enters, waits, and lets animation controller do the work.
    /// </summary>
    public class MeleeAttackState : EnemyStateBase
    {
        private IMeleeAttacker meleeAttacker;
        private EnemyMovement movement;
        private bool isInitialized;

        public MeleeAttackState(EnemyCharacter enemy) : base(enemy) { }

        public override void Enter()
        {
            meleeAttacker = GetCapability<IMeleeAttacker>();
            if (meleeAttacker == null)
            {
                Debug.LogError($"{enemy.gameObject.name}: MeleeAttackState requires IMeleeAttacker!");
                return;
            }

            movement = enemy.Movement;
            isInitialized = true;

            movement?.Stop();
            FaceTarget();

            // Animation controller detects state change and plays attack animation
            // It handles hitbox timing and calls OnMeleeComplete when done
            meleeAttacker.OnMeleeAttack();
        }

        public override void Update()
        {
            // Animation controller handles timing
            if (!isInitialized) return;
            FaceTarget();
        }

        /// <summary>
        /// Called by animation controller to restart attack (when looping)
        /// </summary>
        public void RestartAttack()
        {
            FaceTarget();
            meleeAttacker?.OnMeleeAttack();
        }

        private void FaceTarget()
        {
            if (HasTarget && movement != null)
                movement.FaceTarget(Target.position);
        }

        public override void Exit()
        {
            isInitialized = false;
        }
    }
}