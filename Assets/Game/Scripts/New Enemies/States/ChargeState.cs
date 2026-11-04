using UnityEngine;

namespace junklite
{
    public class ChargeState : EnemyStateBase
    {
        private ICharger charger;
        private ILockedDashAim aimLock;
        private EnemyMovement movement;
        private float timer;
        private float duration;
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

            aimLock = GetCapability<ILockedDashAim>();
            movement = enemy.Movement;
            timer = charger.ChargeTime;
            duration = timer;
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

            if (HasTarget && !IsAimLocked())
                movement?.FaceTarget(Target.position);

            timer -= Time.deltaTime;

            if (timer <= 0f)
                charger.OnChargeComplete();
        }

        // Past the lock point the facing stays put; the dash follows it.
        private bool IsAimLocked() =>
            aimLock != null && (duration <= 0f || 1f - timer / duration >= aimLock.AimLockNormalized);

        public override void Exit()
        {
            enemy.HideAttackWarning();
            if (vfx != null) vfx.SetActive(false);
        }
    }
}