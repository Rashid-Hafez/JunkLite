using UnityEngine;

namespace junklite
{
    /// <summary>
    /// Recovery state - enemy recovers after an action (cooldown/stagger).
    /// 
    /// REQUIRES: Enemy must implement IRecoverer
    /// 
    /// Pure ACTION state: waits for recovery duration.
    /// Calls IRecoverer.OnRecoveryComplete() when done - enemy decides what to do next.
    /// </summary>
    public class RecoverState : EnemyStateBase
    {
        private IRecoverer recoverer;
        private float timer;
        private GameObject activeVFX;

        public RecoverState(EnemyCharacter enemy) : base(enemy) { }

        public override void Enter()
        {
            recoverer = GetCapability<IRecoverer>();
            if (recoverer == null)
            {
                Debug.LogError($"{enemy.gameObject.name}: RecoverState requires IRecoverer interface!");
                return;
            }

            timer = recoverer.RecoveryTime;

            enemy.Movement?.Stop();

            activeVFX = VFXPool.Get(recoverer.RecoveryVFXPrefab, enemy.transform);
        }

        public override void Update()
        {
            if (recoverer == null) return;

            timer -= Time.deltaTime;

            if (timer <= 0f)
                recoverer.OnRecoveryComplete();
        }

        public override void Exit()
        {
            VFXPool.Release(ref activeVFX);
        }
    }
}
