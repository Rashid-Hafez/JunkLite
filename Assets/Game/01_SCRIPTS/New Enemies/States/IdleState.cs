using UnityEngine;

namespace junklite
{
    /// <summary>
    /// Idle state - enemy stands still, waiting.
    /// Used when no patrol path is set or as a temporary state.
    /// </summary>
    public class IdleState : EnemyStateBase
    {
        public IdleState(EnemyCharacter enemy) : base(enemy) { }

        public override void Enter()
        {
            // Stop any movement
            if (enemy is RobotEnemy robot)
            {
                robot.Movement?.Stop();
            }
        }

        public override void Update()
        {
            // Check for target - transition to combat when we add those states
            if (HasTarget)
            {
                // TODO: ChangeState<AlertState>();
                Debug.Log($"{enemy.gameObject.name}: I see the player but I don't know what to do yet!");
            }
        }
    }
}