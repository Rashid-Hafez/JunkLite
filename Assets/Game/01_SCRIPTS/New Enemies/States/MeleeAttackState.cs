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
        private GameObject activeVFX;

        public MeleeAttackState(EnemyCharacter enemy) : base(enemy) { }

        public override void Enter()
        {
            meleeAttacker = GetCapability<IMeleeAttacker>();
            if (meleeAttacker == null)
            {
                Debug.LogError($"{enemy.gameObject.name}: MeleeAttackState requires IMeleeAttacker interface!");
                return;
            }

            movement = enemy.Movement;
            hitbox = meleeAttacker.MeleeHitbox;
            isInitialized = true;

            movement?.Stop();

            if (HasTarget && movement != null)
                movement.FaceTarget(Target.position);

            StartWindUp();
        }

        private void StartWindUp()
        {
            currentPhase = Phase.WindUp;
            timer = 0f;

            if (HasTarget && movement != null)
                movement.FaceTarget(Target.position);

            hitbox?.Deactivate();

            Debug.Log($"{enemy.gameObject.name}: Winding up... (duration: {meleeAttacker.AttackCooldown}s)");
        }

        private void StartSlash()
        {
            currentPhase = Phase.Slashing;
            timer = 0f;

            if (HasTarget && movement != null)
                movement.FaceTarget(Target.position);

            hitbox?.Activate();

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
                    if (!IsTargetInAttackRange)
                    {
                        Debug.Log($"{enemy.gameObject.name}: Target out of range during wind-up → exiting attack state");
                        meleeAttacker.OnMeleeComplete();
                        return;
                    }

                    if (timer >= meleeAttacker.AttackCooldown)
                        StartSlash();
                    break;

                case Phase.Slashing:
                    if (timer >= meleeAttacker.MeleeAttackDuration)
                    {
                        hitbox?.Deactivate();
                        meleeAttacker.OnMeleeComplete();

                        // If still in this state, loop back to wind-up
                        if (enemy.StateMachine.CurrentState == this)
                            StartWindUp();
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