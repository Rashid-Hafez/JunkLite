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
                    {
                        if (HasTarget && IsTargetInAttackRange)
                            BeginWindUp();
                        else
                            meleeAttacker.OnMeleeComplete();
                    }
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

            // Tell the enemy a new attack cycle is starting (wind-up).
            // Animation controller can use this to play a telegraph/anticipation anim.
            meleeAttacker.OnMeleeWindUp();
        }

        private void BeginAttack()
        {
            phase = Phase.Attack;
            timer = 0f;
            attackDuration = meleeAttacker.MeleeAttackDuration;
            hitboxActivated = false;
            hitboxDeactivated = false;

            // Tell the enemy the actual swing is starting now.
            // Animation controller plays the attack clip at this point.
            meleeAttacker.OnMeleeAttack();
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
                    if (HasTarget && IsTargetInAttackRange)
                        BeginWindUp();
                    else
                        meleeAttacker.OnMeleeComplete();
                }
            }
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