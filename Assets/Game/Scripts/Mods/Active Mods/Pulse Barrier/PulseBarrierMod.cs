using UnityEngine;

namespace junklite
{
    /// <summary>
    /// Pulse Barrier Mod - When activated, protects Cas with a shield that absorbs damage.
    /// Shield lasts for a set duration or until its HP is depleted.
    /// Cooldown begins when the shield expires, breaks, or is cancelled.
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
        [Tooltip("Custom pulse arcs. When assigned, replaces all legacy sphere/absorb prefabs below.")]
        public PulseBarrierVisualProfile pulseArcVisuals;

        [Tooltip("VFX spawned on the player when shield activates (parent to player)")]
        public GameObject shieldActivateVFX;

        [Tooltip("Persistent VFX that stays active while shield is up (parent to player)")]
        public GameObject shieldLoopVFXPrefab;

        [Tooltip("VFX spawned when shield absorbs a hit")]
        public GameObject shieldAbsorbVFX;

        #endregion

        #region Overrides

        public override void OnHitRegistered(ModInstance instance, PlayerCharacter player, EnemyCharacter enemy, float damageDealt)
        {
            // No charges - do nothing
        }

        protected override bool ExecuteAbility(
            ModInstance instance,
            PlayerCharacter player,
            ModExecutionRunner executionRunner)
        {
            var shield = GetOrCreateShield(player);
            if (shield.IsActive)
                return false;

            return executionRunner.TryStart(
                instance,
                context => MaintainShield(context, player, shield));
        }

        #endregion

        #region Shield Execution

        private System.Collections.IEnumerator MaintainShield(
            ModExecutionContext context,
            PlayerCharacter player,
            DamageShield shield)
        {
            context.ShowActiveDuration(shieldDuration);
            bool shieldEnded = false;
            GameObject activateVFX = null;
            GameObject loopVFX = null;
            PulseBarrierVisual arcs = null;

            System.Action<float, float> onShieldDamaged = (_, _) =>
            {
                if (arcs == null) SpawnVFX(shieldAbsorbVFX, player);
            };
            System.Action onShieldBroken = () =>
            {
                shieldEnded = true;
            };

            shield.OnShieldDamaged += onShieldDamaged;
            shield.OnShieldBroken += onShieldBroken;

            context.AddCleanup(() =>
            {
                if (arcs != null)
                    arcs.Stop(context.WasCancelled || player == null || !player.IsAlive);
                if (shield != null)
                {
                    shield.OnShieldDamaged -= onShieldDamaged;
                    shield.OnShieldBroken -= onShieldBroken;

                    if (shield.IsActive)
                        shield.Deactivate();
                }

                if (activateVFX != null)
                    Destroy(activateVFX);
                if (loopVFX != null)
                    Destroy(loopVFX);
            });

            shield.Activate(shieldHP, shieldDuration);
            if (pulseArcVisuals != null)
            {
                arcs = player.GetComponent<PulseBarrierVisual>();
                if (arcs == null) arcs = player.gameObject.AddComponent<PulseBarrierVisual>();
                Transform body = player.transform.Find("BODY SPINE");
                if (!arcs.Show(shield, pulseArcVisuals, body, player.VFXCenter - player.transform.position))
                    arcs = null;
            }
            if (arcs == null)
            {
                activateVFX = SpawnVFX(shieldActivateVFX, player);
                loopVFX = SpawnVFX(shieldLoopVFXPrefab, player);
            }

            while (!shieldEnded && shield.IsActive && player != null && player.IsAlive)
                yield return null;
        }

        #endregion

        #region Helpers

        /// <summary>
        /// Spawns a VFX prefab parented to the player, centered using vfxCenter if available,
        /// otherwise falls back to the collider bounds center, then a manual height offset.
        /// </summary>
        private GameObject SpawnVFX(GameObject prefab, PlayerCharacter player)
        {
            if (prefab == null || player == null) return null;

            var vfx = Instantiate(prefab, player.transform);
            vfx.transform.position = player.VFXCenter;
            vfx.transform.localRotation = Quaternion.identity;
            vfx.transform.localScale = Vector3.one;
            return vfx;
        }

       
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
