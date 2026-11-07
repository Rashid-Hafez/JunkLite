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
            stunnable = GetCapability<IStunnable>();
            complete = false;

            if (stunnable == null)
            {
                Debug.LogWarning($"{enemy.name} entered StunnedState but does not implement IStunnable.");
                enemy.OnStunComplete();
                return;
            }

            stunVFX = stunnable.StunVFXObject;

            // Status-driven control owns its absolute expiry. Legacy/direct FSM
            // stuns retain the enemy capability duration as a fallback.
            bool statusControlsDuration = enemy.StatusEffects != null &&
                                          enemy.StatusEffects.IsCrowdControlled;
            duration = statusControlsDuration
                ? 0f
                : stunnable.ForcedStunDuration > 0f
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

            // Status-driven crowd control owns its own absolute expiry. Normal
            // hitstun also waits for knockback so voluntary movement cannot erase it.
            bool statusStillControls = enemy.StatusEffects != null &&
                                       enemy.StatusEffects.IsCrowdControlled;
            if (timer <= 0f &&
                !statusStillControls &&
                (movement == null || !movement.IsInKnockback))
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
