using UnityEngine;

namespace junklite
{
    /// <summary>
    /// Pulse Barrier Mod - When activated, protects Cas with a shield that absorbs damage.
    /// Shield lasts for a set duration or until its HP is depleted.
    /// Cooldown begins immediately on activation.
    /// No charges required - activate on demand.
    /// </summary>
    [CreateAssetMenu(fileName = "PulseBarrierMod", menuName = "Junklite/Mods/Pulse Barrier")]
    public class PulseBarrierMod : ActiveModData
    {
        #region Config

        [Header("Shield")]
        [Tooltip("Total damage the shield can absorb before breaking")]
        public float shieldHP = 50f;

        [Tooltip("How long the shield lasts in seconds (even if not hit)")]
        public float shieldDuration = 10f;

        [Header("VFX")]
        [Tooltip("VFX spawned on the player when shield activates (parent to player)")]
        public GameObject shieldActivateVFX;

        [Tooltip("Persistent VFX that stays active while shield is up (parent to player)")]
        public GameObject shieldLoopVFXPrefab;

        [Tooltip("VFX spawned when shield absorbs a hit")]
        public GameObject shieldAbsorbVFX;

        #endregion

        private bool isShieldActive;
        private GameObject activeActivateVFX;
        private GameObject activeLoopVFX;
        private PlayerCharacter cachedPlayer;

        #region Overrides

        public override bool CanActivate(ModInstance instance, PlayerCharacter player)
        {
            return base.CanActivate(instance, player) && !isShieldActive;
        }

        public override void OnHitRegistered(ModInstance instance, PlayerCharacter player, EnemyCharacter enemy)
        {
            // No charges - do nothing
        }

        protected override bool ExecuteAbility(ModInstance instance, PlayerCharacter player)
        {
            if (isShieldActive) return false;

            cachedPlayer = player;


            var shield = GetOrCreateShield(player);

            // Subscribe before activating so we catch immediate breaks
            shield.OnShieldDamaged += OnShieldDamaged;
            shield.OnShieldBroken += OnShieldBroken;

            shield.Activate(shieldHP, shieldDuration);
            isShieldActive = true;

            // Activation VFX (parented to player, destroyed when shield breaks)
            if (shieldActivateVFX != null)
            {
                activeActivateVFX = Instantiate(shieldActivateVFX, player.transform);
                activeActivateVFX.transform.localPosition = Vector3.zero;
            }

            // Loop VFX (parented to player, destroyed when shield breaks)
            if (shieldLoopVFXPrefab != null)
            {
                activeLoopVFX = Instantiate(shieldLoopVFXPrefab, player.transform);
                activeLoopVFX.transform.localPosition = Vector3.zero;
            }

            Debug.Log($"[PulseBarrier] Shield activated: {shieldHP} HP, {shieldDuration}s duration.");
            return true;
        }

        public override void OnEquip(PlayerCharacter player)
        {
            isShieldActive = false;
            activeActivateVFX = null;
            activeLoopVFX = null;
            cachedPlayer = null;
        }

        public override void OnUnequip(PlayerCharacter player)
        {
            if (isShieldActive)
            {
                var shield = player.GetComponent<DamageShield>();
                if (shield != null)
                {
                    shield.OnShieldDamaged -= OnShieldDamaged;
                    shield.OnShieldBroken -= OnShieldBroken;
                    shield.Deactivate();
                }
                CleanupShield();
            }
        }

        #endregion

        #region Shield Events

        private void OnShieldDamaged(float currentHP, float maxHP)
        {
            Debug.Log($"[PulseBarrier] Shield hit! Remaining: {currentHP}/{maxHP}");

            // Absorb VFX parented to player
            if (shieldAbsorbVFX != null && cachedPlayer != null)
            {
                var vfx = Instantiate(shieldAbsorbVFX, cachedPlayer.transform);
                vfx.transform.localPosition = Vector3.zero;
            }
        }

        private void OnShieldBroken()
        {
            Debug.Log("[PulseBarrier] Shield broken/expired.");

            // Unsubscribe
            if (cachedPlayer != null)
            {
                var shield = cachedPlayer.GetComponent<DamageShield>();
                if (shield != null)
                {
                    shield.OnShieldDamaged -= OnShieldDamaged;
                    shield.OnShieldBroken -= OnShieldBroken;
                }
            }

            CleanupShield();
        }

        private void CleanupShield()
        {
            isShieldActive = false;

            if (activeActivateVFX != null)
            {
                Destroy(activeActivateVFX);
                activeActivateVFX = null;
            }

            if (activeLoopVFX != null)
            {
                Destroy(activeLoopVFX);
                activeLoopVFX = null;
            }

            cachedPlayer = null;
        }

        #endregion

        #region Helpers

        private DamageShield GetOrCreateShield(PlayerCharacter player)
        {
            var shield = player.GetComponent<DamageShield>();
            if (shield == null)
                shield = player.gameObject.AddComponent<DamageShield>();
            return shield;
        }

        #endregion
    }
}