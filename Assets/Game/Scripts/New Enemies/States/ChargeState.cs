using UnityEngine;

namespace junklite
{
    public class ChargeState : EnemyStateBase
    {
        private ICharger charger;
        private ILockedDashAim aimLock;
        private IChargeArmor armor;
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
            armor = GetCapability<IChargeArmor>();
            movement = enemy.Movement;
            timer = charger.ChargeTime;
            duration = timer;
            vfx = charger.ChargeVFXPrefab;

            movement?.Stop();

            if (HasTarget)
                movement?.FaceTarget(Target.position);

            // The dash hits as the charge ends: the ring closes then.
            enemy.ShowAttackWarningImmediate(charger.ChargeTime);
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

        /// <summary>An armored charge (brain-decided) shrugs off hitstun and knockback; damage still applies.</summary>
        public override bool CanBeInterrupted => armor == null || !armor.ChargeIsArmored;

        public override void Exit()
        {
            enemy.HideAttackWarning();
            if (vfx != null) vfx.SetActive(false);
        }
    }
}