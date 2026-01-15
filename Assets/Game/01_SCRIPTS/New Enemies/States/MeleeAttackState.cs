using UnityEngine;

namespace junklite
{
    /// <summary>
    /// Melee Attack state - enemy performs close-range slashes with wind-up.
    /// 
    /// REQUIRES: Enemy must implement IMeleeAttacker
    /// 
    /// FLOW:
    /// 1. Enter → Face target, stop moving
    /// 2. Cooldown/Wind-up (wait before attacking - gives player time to react)
    /// 3. Slash (activate hitbox for duration)
    /// 4. Call OnMeleeComplete() → Enemy decides: stay (loop back to step 2) or transition out
    /// </summary>
    public class MeleeAttackState : EnemyStateBase
    {
        private enum Phase { WindUp, Slashing }

        private IMeleeAttacker meleeAttacker;
        private EnemyMovement movement;
        private Hitbox hitbox;

        private Phase currentPhase;
        private float timer;
        private bool isInitialized;

        // Cached VFX instance
        private GameObject activeVFX;

        public MeleeAttackState(EnemyCharacter enemy) : base(enemy) { }

        public override void Enter()
        {
            // Get capability interface
            meleeAttacker = enemy as IMeleeAttacker;
            if (meleeAttacker == null)
            {
                Debug.LogError($"{enemy.gameObject.name}: MeleeAttackState requires IMeleeAttacker interface!");
                return;
            }

            movement = enemy.Movement;
            hitbox = meleeAttacker.MeleeHitbox;
            isInitialized = true;

            // Stop movement during attack
            movement?.Stop();

            // Face the target before attacking
            if (HasTarget && movement != null)
                movement.FaceTarget(Target.position);

            // Start with wind-up (cooldown before first attack)
            StartWindUp();
        }

        private void StartWindUp()
        {
            currentPhase = Phase.WindUp;
            timer = 0f;

            // Face target during wind-up
            if (HasTarget && movement != null)
                movement.FaceTarget(Target.position);

            // Hitbox stays OFF during wind-up
            hitbox?.Deactivate();

            Debug.Log($"{enemy.gameObject.name}: Winding up... (duration: {meleeAttacker.AttackCooldown}s)");
        }

        private void StartSlash()
        {
            currentPhase = Phase.Slashing;
            timer = 0f;

            // Face target at moment of slash
            if (HasTarget && movement != null)
                movement.FaceTarget(Target.position);

            // Activate hitbox
            hitbox?.Activate();

            // Spawn VFX
            VFXPool.Release(ref activeVFX);
            activeVFX = VFXPool.Get(meleeAttacker.MeleeVFXPrefab, enemy.transform);

            Debug.Log($"{enemy.gameObject.name}: SLASH! (duration: {meleeAttacker.MeleeAttackDuration}s)");
        }

        public override void Update()
        {
            if (!isInitialized || meleeAttacker == null) return;

            timer += Time.deltaTime;

            switch (currentPhase)
            {
                case Phase.WindUp:
                    // During wind-up, check if player went out of range - exit early
                    if (!IsTargetInAttackRange)
                    {
                        Debug.Log($"{enemy.gameObject.name}: Target out of range during wind-up → exiting attack state");
                        meleeAttacker.OnMeleeComplete();
                        return;
                    }

                    if (timer >= meleeAttacker.AttackCooldown)
                    {
                        // Wind-up complete - ATTACK!
                        StartSlash();
                    }
                    break;

                case Phase.Slashing:
                    if (timer >= meleeAttacker.MeleeAttackDuration)
                    {
                        // Slash complete - deactivate hitbox
                        hitbox?.Deactivate();

                        // Let enemy decide what to do next
                        meleeAttacker.OnMeleeComplete();

                        // Check if we're still in this state (enemy didn't transition)
                        if (enemy.StateMachine.CurrentState == this)
                        {
                            // Loop back to wind-up for next attack
                            StartWindUp();
                        }
                    }
                    break;
            }
        }

        public override void Exit()
        {
            hitbox?.Deactivate();
            VFXPool.Release(ref activeVFX);
            timer = 0f;
            isInitialized = false;
        }
    }
}