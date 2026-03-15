using UnityEngine;

namespace junklite
{
    public class ChargeState : EnemyStateBase
    {
        private ICharger charger;
        private EnemyMovement movement;
        private float timer;
        private GameObject vfx;

        public ChargeState(EnemyCharacter enemy) : base(enemy) { }

        public override void Enter()
        {
            charger = GetCapability<ICharger>();
            if (charger == null)
            {
                Debug.LogError($"{enemy.gameObject.name}: ChargeState requires ICharger interface!");
                return;
            }

            movement = enemy.Movement;
            timer = charger.ChargeTime;
            vfx = charger.ChargeVFXPrefab;

            movement?.Stop();

            if (HasTarget)
                movement?.FaceTarget(Target.position);

            enemy.ShowAttackWarningImmediate();
            if (vfx != null) vfx.SetActive(true);
        }

        public override void Update()
        {
            if (charger == null) return;

            if (HasTarget)
                movement?.FaceTarget(Target.position);

            timer -= Time.deltaTime;

            if (timer <= 0f)
                charger.OnChargeComplete();
        }

        public override void Exit()
        {
            enemy.HideAttackWarning();
            if (vfx != null) vfx.SetActive(false);
        }
    }
}