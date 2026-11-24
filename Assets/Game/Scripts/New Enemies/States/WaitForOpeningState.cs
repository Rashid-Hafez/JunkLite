using UnityEngine;

namespace junklite
{
    /// <summary>
    /// Holds an engagement position while another enemy owns the attack opening.
    /// The brain continues evaluating the queue and decides when to resume.
    /// </summary>
    public sealed class WaitForOpeningState : EnemyStateBase
    {
        public WaitForOpeningState(EnemyCharacter enemy) : base(enemy) { }

        public override void Enter()
        {
            enemy.Movement?.Stop();
            FaceTarget();
        }

        public override void Update()
        {
            FaceTarget();
        }

        public override void Exit()
        {
            enemy.Movement?.Stop();
        }

        private void FaceTarget()
        {
            if (HasTarget)
                enemy.Movement?.FaceTarget(Target.position);
        }
    }
}
