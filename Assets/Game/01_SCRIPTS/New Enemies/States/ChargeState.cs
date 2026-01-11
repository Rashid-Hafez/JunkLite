using UnityEngine;

namespace junklite
{
    /// <summary>
    /// Charge state - enemy charges up before attacking.
    /// Pure ACTION state: plays animation, faces target, waits for timer.
    /// Calls enemy.OnChargeComplete() when done - enemy DECIDES what to do next.
    /// </summary>
    public class ChargeState : EnemyStateBase
    {
        private EnemyMovement movement;
        private float timer;

        public ChargeState(EnemyCharacter enemy) : base(enemy) { }

        public override void Enter()
        {
            movement = enemy.Movement;
            timer = enemy.DashChargeTime;

            // Stop moving
            movement?.Stop();

            // Face target at start
            if (HasTarget)
                movement?.FaceTarget(Target.position);

            // Play charge VFX
            enemy.SpawnChargeVFX();

            // Animation is driven via EnemyAnimationController subscribing to StateMachine.OnStateChanged.

            Debug.Log($"{enemy.gameObject.name}: Charging! ({timer}s)");
        }

        public override void Update()
        {
            // Keep facing target during charge
            if (HasTarget)
                movement?.FaceTarget(Target.position);

            timer -= Time.deltaTime;

            if (timer <= 0f)
            {
                // Charge complete - let enemy decide what to do
                enemy.OnChargeComplete();
            }
        }

        public override void Exit()
        {
            // Stop charge VFX
            enemy.DestroyChargeVFX();
        }
    }
}