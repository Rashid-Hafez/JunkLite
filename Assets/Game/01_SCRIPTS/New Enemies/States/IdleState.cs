using UnityEngine;

namespace junklite
{
    /// <summary>
    /// Idle state - enemy stands still, waiting.
    /// Used when no patrol path is set or as a temporary state.
    /// 
    /// Universal state - works with any enemy.
    /// </summary>
    public class IdleState : EnemyStateBase
    {
        public IdleState(EnemyCharacter enemy) : base(enemy) { }

        public override void Enter()
        {
            enemy.Movement?.Stop();
        }

        public override void Update()
        {
            // Detection is handled by DetectionZone events
            // This state just waits
        }
    }
}