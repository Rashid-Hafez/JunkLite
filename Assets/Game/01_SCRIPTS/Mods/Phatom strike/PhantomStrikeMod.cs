using UnityEngine;

namespace junklite
{
    /// <summary>
    /// Phantom Strike Mod - Accumulate hits to unlock a devastating aerial slam attack.
    /// 3 successful strikes = special move ready. Taking damage resets the counter.
    /// </summary>
    [CreateAssetMenu(fileName = "PhantomStrikeMod", menuName = "Junklite/Mods/Phantom Strike")]
    public class PhantomStrikeMod : ModData
    {
        [Header("Hit Tracking")]
        [Tooltip("Number of hits required to charge special attack")]
        public int hitsRequired = 3;

        [Header("Slam Attack")]
        [Tooltip("Base damage of the slam attack")]
        public float slamDamage = 50f;

        [Tooltip("Critical damage multiplier for the slam attack")]
        public float criticalMultiplier = 3f;

        [Tooltip("Radius of the ground slam damage")]
        public float slamRadius = 4f;

        [Tooltip("Layer mask for detecting enemies")]
        public LayerMask enemyLayerMask = 1;

        [Header("Timing")]
        [Tooltip("Time spent invisible before descending")]
        public float hangTime = 0.8f;

        [Tooltip("Duration of the descent (slam) animation")]
        public float descentDuration = 0.25f;

        [Tooltip("Recovery time after impact before input is restored")]
        public float recoveryTime = 0.15f;

        [Header("Movement")]
        [Tooltip("How high above the slam target the player spawns")]
        public float spawnHeight = 8f;

        [Header("VFX (Optional)")]
        [Tooltip("VFX spawned when player vanishes")]
        public GameObject vanishVFX;

        [Tooltip("VFX spawned during descent")]
        public GameObject descentVFX;

        [Tooltip("VFX spawned on impact")]
        public GameObject impactVFX;

        [Header("Camera")]
        [Tooltip("Camera shake intensity on impact (0 = no shake)")]
        public float cameraShakeIntensity = 3f;

        [Header("Durability")]
        [Tooltip("Durability consumed when special attack is used")]
        public float durabilityPerSpecial = 5f;

        // -----------------------------------------------------------------------
        // BEHAVIOR
        // -----------------------------------------------------------------------

        public override bool OnHit(WeaponInstance weapon, EnemyCharacter enemy, PlayerCharacter player)
        {
            if (player == null)
                return false;

            var tracker = GetTracker(player);
            if (tracker == null || !tracker.IsActive)
                return false;

            // Don't add hits if special is already ready or executing
            if (tracker.IsSpecialReady || tracker.IsExecutingSpecial)
                return false;

            tracker.AddHit();

            return true; // Consumes durability on hit
        }

        public override void OnEquip(WeaponInstance weapon)
        {
            var player = weapon?.GetComponentInParent<PlayerCharacter>();
            if (player == null)
                return;

            var tracker = GetOrCreateTracker(player);
            tracker.Initialize(this);
            tracker.SetActive(true);

            Debug.Log($"[PhantomStrike] Equipped - tracking hits (need {hitsRequired})");
        }

        public override void OnUnequip(WeaponInstance weapon)
        {
            var player = weapon?.GetComponentInParent<PlayerCharacter>();
            if (player == null)
                return;

            var tracker = GetTracker(player);
            if (tracker != null)
            {
                tracker.SetActive(false);
                tracker.ResetHits();
            }

            Debug.Log("[PhantomStrike] Unequipped");
        }

        // -----------------------------------------------------------------------
        // HELPERS
        // -----------------------------------------------------------------------

        private PhantomStrikeTracker GetTracker(PlayerCharacter player)
        {
            return player.GetComponent<PhantomStrikeTracker>();
        }

        private PhantomStrikeTracker GetOrCreateTracker(PlayerCharacter player)
        {
            var tracker = player.GetComponent<PhantomStrikeTracker>();
            if (tracker == null)
                tracker = player.gameObject.AddComponent<PhantomStrikeTracker>();

            return tracker;
        }
    }
}