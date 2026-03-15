using Unity.VisualScripting;
using UnityEngine;

namespace junklite
{
    /// <summary>
    /// Phantom Strike Mod - Accumulate hits to unlock a devastating aerial slam attack.
    /// Hits build charges via the mod system. Taking damage resets charges.
    /// Activated via dedicated mod activation input.
    /// </summary>
    [CreateAssetMenu(fileName = "PhantomStrikeMod", menuName = "Junklite/Mods/Phantom Strike")]
    public class PhantomStrikeMod : ActiveModData
    {
        #region Config

        [Header("Slam Attack")]
        public string groundPoundAnimationName = "GroundPound";
        public float slamDamage = 50f;
        public float criticalMultiplier = 3f;
        public float slamRadius = 4f;
        public LayerMask enemyLayerMask = 1;
        public LayerMask groundLayerMask;

        [Header("Timing")]
        public float hangTime = 0.8f;
        public float descentSpeed = 10f;
        public float descentDuration = 0.25f;
        public float recoveryTime = 0.15f;

        [Header("Movement")]
        public float spawnHeight = 8f;
        public float driftUpHeight = 1f;
        public float driftUpDuration = 0.15f;
        public float slamDescentSpeed = 25f;

        [Header("VFX")]
        public GameObject vanishVFX;
        public GameObject descentVFX;
        public GameObject impactVFX;

        [Header("Camera")]
        public float cameraZoomOutValue = 55f;
        public float cameraShakeIntensity = 3f;

        #endregion

        #region ActiveModData Overrides

        public override void OnHitRegistered(ModInstance instance, PlayerCharacter player, EnemyCharacter enemy)
        {
            // Don't add charges if already ready or executing
            var tracker = GetTracker(player);
            if (tracker != null && tracker.IsExecutingSpecial) return;
            if (instance.CurrentCharges >= chargesRequired) return;

            base.OnHitRegistered(instance, player, enemy);

            tracker?.NotifyChargesChanged(instance.CurrentCharges, chargesRequired);
        }

        public override bool OnActivate(ModInstance instance, PlayerCharacter player)
        {
            var tracker = GetTracker(player);
            if (tracker == null || tracker.IsExecutingSpecial) return false;

            tracker.ExecuteSlam(instance);
            return true;
        }

        public override void OnEquip(PlayerCharacter player)
        {
            var tracker = GetOrCreateTracker(player);
            tracker.Initialize(this);
            tracker.SetActive(true);
        }

        public override void OnUnequip(PlayerCharacter player)
        {
            var tracker = GetTracker(player);
            if (tracker == null) return;

            tracker.SetActive(false);
        }

        #endregion

        #region Helpers

        private PhantomStrikeTracker GetTracker(PlayerCharacter player)
        {
            var parent = GetOrCreateTrackerParent(player);
            return parent.GetComponentInChildren<PhantomStrikeTracker>();
        }

        private PhantomStrikeTracker GetOrCreateTracker(PlayerCharacter player)
        {
            var parent = GetOrCreateTrackerParent(player);
            var tracker = parent.GetComponentInChildren<PhantomStrikeTracker>();
            if (tracker == null)
                tracker = parent.AddComponent<PhantomStrikeTracker>();
            return tracker;
        }

        private Transform GetOrCreateTrackerParent(PlayerCharacter player)
        {
            var existing = player.transform.Find("Mod Trackers");
            if (existing != null) return existing;

            var go = new GameObject("Mod Trackers");
            go.transform.SetParent(player.transform);
            go.transform.localPosition = Vector3.zero;
            return go.transform;
        }

        #endregion
    }
}