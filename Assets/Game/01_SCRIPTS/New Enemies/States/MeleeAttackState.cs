using UnityEngine;

namespace junklite
{
    /// <summary>
    /// Melee Attack state - enemy performs a quick close-range attack.
    /// 
    /// REQUIRES: Enemy must implement IMeleeAttacker
    /// 
    /// Pure ACTION state: activates hitbox, waits for duration, then completes.
    /// Calls IMeleeAttacker.OnMeleeComplete() when done - enemy decides what to do next.
    /// </summary>
    public class MeleeAttackState : EnemyStateBase
    {
        private IMeleeAttacker meleeAttacker;
        private EnemyMovement movement;
        private Hitbox hitbox;

        private float timer;
        private bool hasStarted;
        private bool attackComplete;

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
            hasStarted = false;
            attackComplete = false;
            timer = 0f;

            // Face the target before attacking
            if (HasTarget && movement != null)
                movement.FaceTarget(Target.position);

            StartAttack();
        }

        private void StartAttack()
        {
            hasStarted = true;

            // Stop movement during attack
            movement?.Stop();

            // Activate hitbox
            hitbox?.Activate();

            // Spawn VFX
            activeVFX = VFXPool.Get(meleeAttacker.MeleeVFXPrefab, enemy.transform);

            Debug.Log($"{enemy.gameObject.name}: MELEE ATTACK! (duration: {meleeAttacker.MeleeAttackDuration}s)");
        }

        public override void Update()
        {
            if (meleeAttacker == null || !hasStarted || attackComplete) return;

            timer += Time.deltaTime;

            if (timer >= meleeAttacker.MeleeAttackDuration)
            {
                attackComplete = true;
                hitbox?.Deactivate();
                meleeAttacker.OnMeleeComplete();
            }
        }

        public override void Exit()
        {
            hitbox?.Deactivate();
            VFXPool.Release(ref activeVFX);
            timer = 0f;
        }
    }
}