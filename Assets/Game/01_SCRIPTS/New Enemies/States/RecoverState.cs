using UnityEngine;

namespace junklite
{
    /// <summary>
    /// Recover state - enemy recovers after an attack.
    /// Pure ACTION state: plays animation, waits for timer.
    /// Calls enemy.OnRecoveryComplete() when done - enemy DECIDES what to do next.
    /// </summary>
    public class RecoverState : EnemyStateBase
    {
        private float timer;

        public RecoverState(EnemyCharacter enemy) : base(enemy) { }

        public override void Enter()
        {
            timer = enemy.DashRecoveryTime;

            // Stop all movement
            enemy.Movement?.Stop();

            // Animation is driven via EnemyAnimationController subscribing to StateMachine.OnStateChanged.

            Debug.Log($"{enemy.gameObject.name}: Recovering ({timer}s)");
        }

        public override void Update()
        {
            timer -= Time.deltaTime;

            if (timer <= 0f)
            {
                // Recovery complete - let enemy decide what to do
                enemy.OnRecoveryComplete();
            }
        }

        public override void Exit()
        {
            // Cleanup if needed
        }
    }
}