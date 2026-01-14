using UnityEngine;

namespace junklite
{
    /// <summary>
    /// Charge state - enemy charges up before attacking.
    /// 
    /// REQUIRES: Enemy must implement ICharger
    /// 
    /// Pure ACTION state: plays animation, faces target, waits for timer.
    /// Calls ICharger.OnChargeComplete() when done - enemy decides what to do next.
    /// </summary>
    public class ChargeState : EnemyStateBase
    {
        private ICharger charger;
        private EnemyMovement movement;
        private float timer;

        // Cached VFX instance
        private GameObject activeVFX;

        public ChargeState(EnemyCharacter enemy) : base(enemy) { }

        public override void Enter()
        {
            // Get capability interface
            charger = enemy as ICharger;
            if (charger == null)
            {
                Debug.LogError($"{enemy.gameObject.name}: ChargeState requires ICharger interface!");
                return;
            }

            movement = enemy.Movement;
            timer = charger.ChargeTime;

            // Stop moving
            movement?.Stop();

            // Face target at start
            if (HasTarget)
                movement?.FaceTarget(Target.position);

            // Spawn VFX
            activeVFX = VFXPool.Get(charger.ChargeVFXPrefab, enemy.transform);

            Debug.Log($"{enemy.gameObject.name}: Charging! ({timer}s)");
        }

        public override void Update()
        {
            if (charger == null) return;

            // Keep facing target during charge
            if (HasTarget)
                movement?.FaceTarget(Target.position);

            timer -= Time.deltaTime;

            if (timer <= 0f)
            {
                charger.OnChargeComplete();
            }
        }

        public override void Exit()
        {
            VFXPool.Release(ref activeVFX);
        }
    }
}