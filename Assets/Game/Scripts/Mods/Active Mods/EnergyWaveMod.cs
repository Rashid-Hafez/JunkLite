using UnityEngine;
using System.Collections;

namespace junklite
{
    /// <summary>
    /// Energy Wave Mod - On activation, fires a series of energy wave pulses from Cas.
    /// Each wave travels in a straight line and damages all enemies in its path (once per enemy per wave).
    /// Durability is consumed once on activation, not per wave.
    /// Player is locked in place for the full duration of the sequence.
    /// </summary>
    [CreateAssetMenu(fileName = "EnergyWaveMod", menuName = "Junklite/Mods/Energy Wave")]
    public class EnergyWaveMod : ActiveModData
    {
        #region Config

        [Header("Wave Count")]
        [Tooltip("Total number of waves fired per activation")]
        public int waveCount = 5;

        [Tooltip("Time between each wave pulse")]
        public float timeBetweenWaves = 0.35f;

        [Header("Wave Prefab")]
        [Tooltip("Prefab with EnergyWavePulse component")]
        public GameObject wavePrefab;

        [Header("Wave Stats")]
        public float waveDamage = 20f;
        public float waveSpeed = 15f;
        public float waveMaxDistance = 20f;
        public float waveRadius = 1f;
        public LayerMask enemyLayerMask = 1;

        [Header("Spawn")]
        [Tooltip("Height offset from player position to spawn waves")]
        public float spawnHeightOffset = 0.5f;

        [Tooltip("Forward offset from player position to spawn waves")]
        public float spawnForwardOffset = 0.5f;

        [Header("Timing")]
        [Tooltip("Recovery time after last wave before controls are returned")]
        public float recoveryTime = 0.2f;

        [Tooltip("Brief invulnerability after the sequence completes")]
        public float recoveryInvulnerability = 0.2f;

        [Header("Animation")]
        [Tooltip("Animation to play during wave firing (leave empty for none)")]
        public string firingAnimationName = "";

        [Header("VFX")]
        public GameObject activationVFX;

        [Header("Hit Feedback")]
        public float hitShakeIntensity = 0.5f;

        #endregion

        private bool isFiring;
        private Coroutine firingCoroutine;
        private PlayerCharacter cachedPlayer;

        #region Overrides

        public override bool CanActivate(ModInstance instance, PlayerCharacter player)
        {
            return !instance.IsBroken && !isFiring;
        }

        public override void OnHitRegistered(ModInstance instance, PlayerCharacter player, EnemyCharacter enemy)
        {
            // No charges - do nothing
        }

        public override bool OnActivate(ModInstance instance, PlayerCharacter player)
        {
            if (isFiring) return false;

            isFiring = true;
            cachedPlayer = player;
            firingCoroutine = player.StartCoroutine(CoFireWaves(player));
            return true;
        }

        public override void OnEquip(PlayerCharacter player)
        {
            isFiring = false;
            firingCoroutine = null;
            cachedPlayer = null;
        }

        public override void OnUnequip(PlayerCharacter player)
        {
            StopFiring(player);
        }

        #endregion

        #region Wave Sequence

        private IEnumerator CoFireWaves(PlayerCharacter player)
        {
            var playerState = player.PlayerState;
            var controller = player.Controller;
            var rb = player.GetComponent<Rigidbody>();
            var spineAnim = player.GetComponent<SpineAnimationController>();

            // --- LOCK EVERYTHING ---
            if (playerState != null)
            {
                playerState.SetInputLocked(true);
                playerState.SetVulnerable(false);
            }

            if (controller != null)
            {
                controller.StopAllVelocity();
                controller.CanMove = false;
                controller.SetPhysicsOverride(true);
            }

            bool wasKinematic = false;
            if (rb != null)
            {
                wasKinematic = rb.isKinematic;
                rb.isKinematic = true;
                rb.linearVelocity = Vector3.zero;
            }

            // Activation VFX
            if (activationVFX != null)
                Instantiate(activationVFX, player.transform.position, Quaternion.identity);

            // Firing animation
            if (spineAnim != null && !string.IsNullOrEmpty(firingAnimationName))
                spineAnim.ForcePlayOverride(firingAnimationName, true, () => { });

            // --- FIRE WAVES ---
            for (int i = 0; i < waveCount; i++)
            {
                if (player == null || !player.IsAlive) break;

                SpawnWave(player);

                if (i < waveCount - 1)
                    yield return new WaitForSeconds(timeBetweenWaves);
            }

            // --- RECOVERY ---
            yield return new WaitForSeconds(recoveryTime);

            // --- RESTORE EVERYTHING ---
            RestorePhysics(player, playerState, controller, rb, wasKinematic);
        }

        private void SpawnWave(PlayerCharacter player)
        {
            if (wavePrefab == null)
            {
                Debug.LogWarning("[EnergyWave] No wave prefab assigned.");
                return;
            }

            float facing = Mathf.Sign(player.transform.localScale.x);
            Vector3 spawnPos = player.transform.position
                             + Vector3.up * spawnHeightOffset
                             + Vector3.right * (facing * spawnForwardOffset);

            Vector3 direction = Vector3.right * facing;

            var go = Instantiate(wavePrefab, spawnPos, Quaternion.identity);
            var pulse = go.GetComponent<EnergyWavePulse>();

            if (pulse != null)
            {
                pulse.Initialize(
                    direction,
                    waveSpeed,
                    waveMaxDistance,
                    waveDamage,
                    waveRadius,
                    enemyLayerMask,
                    player.gameObject,
                    hitShakeIntensity
                );
            }
            else
            {
                Debug.LogWarning("[EnergyWave] Wave prefab missing EnergyWavePulse component.");
                Destroy(go);
            }
        }

        private void RestorePhysics(
            PlayerCharacter player,
            PlayerState playerState,
            Character2D5Controller controller,
            Rigidbody rb,
            bool wasKinematic)
        {
            if (rb != null)
                rb.isKinematic = wasKinematic;

            if (controller != null)
            {
                controller.SetPhysicsOverride(false);
                controller.CanMove = true;
            }

            if (playerState != null)
            {
                playerState.SetInputLocked(false);
                playerState.SetVulnerable(true);
                playerState.ApplyInvulnerability(recoveryInvulnerability);
            }

            isFiring = false;
            firingCoroutine = null;
            cachedPlayer = null;
        }

        private void StopFiring(PlayerCharacter player)
        {
            if (firingCoroutine != null && cachedPlayer != null)
                cachedPlayer.StopCoroutine(firingCoroutine);

            // Restore if we were mid-sequence
            if (isFiring && player != null)
            {
                var playerState = player.PlayerState;
                var controller = player.Controller;
                var rb = player.GetComponent<Rigidbody>();
                RestorePhysics(player, playerState, controller, rb, false);
            }

            isFiring = false;
            firingCoroutine = null;
            cachedPlayer = null;
        }

        #endregion
    }
}