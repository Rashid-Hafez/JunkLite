using UnityEngine;

namespace junklite
{
    /// <summary>
    /// The beat between noticing the player and chasing: stop, face the player,
    /// hold for a moment, then hand over to the brain (IAlerter.OnAlertComplete),
    /// which enters combat. No i-frames; the brain skips the alert when hit.
    /// </summary>
    public sealed class AlertState : EnemyStateBase
    {
        private IAlerter alerter;
        private EnemyMovement movement;
        private float timer;
        private bool finished;

        public AlertState(EnemyCharacter enemy) : base(enemy) { }

        public override void Enter()
        {
            alerter = GetCapability<IAlerter>();
            movement = enemy.Movement;
            finished = false;

            movement?.Stop();
            if (HasTarget)
                movement?.FaceTarget(Target.position);

            timer = alerter != null ? Mathf.Max(0f, alerter.AlertDuration) : 0f;
            if (timer <= 0f)
                Finish();
        }

        public override void Update()
        {
            if (finished)
                return;

            if (HasTarget)
                movement?.FaceTarget(Target.position);

            timer -= Time.deltaTime;
            if (timer <= 0f)
                Finish();
        }

        public override void Exit()
        {
            finished = true;
        }

        private void Finish()
        {
            if (finished)
                return;

            finished = true;
            alerter?.OnAlertComplete();
        }
    }
}
