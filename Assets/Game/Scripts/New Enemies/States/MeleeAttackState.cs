using UnityEngine;

namespace junklite
{
    public class MeleeAttackState : EnemyStateBase
    {
        private enum Phase { WindUp, Attack, Cooldown }

        private IMeleeAttacker meleeAttacker;
        private EnemyMovement movement;
        private Hitbox hitbox;

        private Phase phase;
        private float timer;
        private float attackDuration;
        private bool hitboxActivated;
        private bool hitboxDeactivated;
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
            hitbox = meleeAttacker.MeleeHitbox;
            isInitialized = true;

            movement?.Stop();
            BeginWindUp();
        }

        public override void Update()
        {
            if (!isInitialized) return;

            timer += Time.deltaTime;

            switch (phase)
            {
                case Phase.WindUp:
                    if (timer >= meleeAttacker.MeleeWindUpDuration)
                        BeginAttack();
                    break;

                case Phase.Attack:
                    UpdateAttack();
                    break;

                case Phase.Cooldown:
                    if (timer >= meleeAttacker.MeleeAttackSpeed)
                        CompleteAttack();
                    break;
            }
        }

        // =============================================================
        // PHASE TRANSITIONS
        // =============================================================

        private void BeginWindUp()
        {
            phase = Phase.WindUp;
            timer = 0f;
            FaceTarget();

            // Gameplay owns the duration. Presentation may fit its clip to this
            // phase, but it cannot change when the attack becomes active.
            enemy.AnimationPresenter?.PlayMeleeWindup(meleeAttacker.MeleeWindUpDuration);
        }

        private void BeginAttack()
        {
            phase = Phase.Attack;
            timer = 0f;
            attackDuration = meleeAttacker.MeleeAttackDuration;
            hitboxActivated = false;
            hitboxDeactivated = false;

            enemy.AnimationPresenter?.PlayMeleeAttack(attackDuration);
        }

        // =============================================================
        // ATTACK PHASE — normalized time is relative to this phase only
        // =============================================================

        private void UpdateAttack()
        {
            float progress = Mathf.Clamp01(timer / attackDuration);

            if (!hitboxActivated && progress >= meleeAttacker.MeleeHitStartNormalized)
            {
                hitboxActivated = true;
                hitbox?.Activate();
            }

            if (!hitboxDeactivated && hitboxActivated && progress >= meleeAttacker.MeleeHitEndNormalized)
            {
                hitboxDeactivated = true;
                hitbox?.Deactivate();
            }

            if (progress >= 1f)
            {
                hitbox?.Deactivate();
                FaceTarget();

                float cooldown = meleeAttacker.MeleeAttackSpeed;
                if (cooldown > 0f)
                {
                    phase = Phase.Cooldown;
                    timer = 0f;
                }
                else
                {
                    CompleteAttack();
                }
            }
        }

        private void CompleteAttack()
        {
            if (!isInitialized)
                return;

            isInitialized = false;
            hitbox?.Deactivate();
            meleeAttacker.OnMeleeComplete();
        }

        // =============================================================
        // HELPERS
        // =============================================================

        private void FaceTarget()
        {
            if (HasTarget && movement != null)
                movement.FaceTarget(Target.position);
        }

        public override void Exit()
        {
            hitbox?.Deactivate();
            isInitialized = false;
        }
    }
}
