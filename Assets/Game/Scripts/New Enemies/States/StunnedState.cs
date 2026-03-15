using UnityEngine;

namespace junklite
{
    public class StunnedState : EnemyStateBase
    {
        private EnemyMovement movement;
        private IStunnable stunnable;
        private float timer;
        private float duration;
        private bool complete;
        private GameObject stunVFX;

        public StunnedState(EnemyCharacter enemy) : base(enemy) { }

        public override bool CanTakeDamage => true;

        public override void Enter()
        {
            movement = enemy.Movement;
            stunnable = enemy as IStunnable;
            complete = false;

            if (stunnable == null)
            {
                Debug.LogWarning($"{enemy.name} entered StunnedState but does not implement IStunnable.");
                enemy.OnStunComplete();
                return;
            }

            stunVFX = stunnable.StunVFXObject;

            // ForcedStunDuration > 0 means parry/explicit stun — use that.
            // Otherwise use the enemy's stagger duration (normal hit).
            duration = stunnable.ForcedStunDuration > 0f
                ? stunnable.ForcedStunDuration
                : stunnable.StaggerDuration;

            timer = duration;

            // Only stop voluntary movement (chase/patrol). If knockback is
            // already in flight (applied in TakeDamage before entering this
            // state), preserve it — the Update loop waits for it to finish.
            if (movement != null && !movement.IsInKnockback)
                movement.Stop();

            if (stunVFX != null)
                stunVFX.SetActive(true);
        }

        public override void Update()
        {
            if (complete) return;

            timer -= Time.deltaTime;

            // Wait for both: timer expired AND knockback finished
            if (timer <= 0f && (movement == null || !movement.IsInKnockback))
            {
                Complete();
            }
        }

        public void ResetTimer()
        {
            timer = duration;
        }

        private void Complete()
        {
            if (complete) return;
            complete = true;

            if (stunnable != null)
                stunnable.ForcedStunDuration = 0f;

            stunnable?.OnStunComplete();
        }

        public override void Exit()
        {
            if (stunVFX != null)
                stunVFX.SetActive(false);

            enemy.ClearParryStunFlag();
        }
    }
}