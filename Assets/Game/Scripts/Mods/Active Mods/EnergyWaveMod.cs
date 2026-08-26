using UnityEngine;
using System.Collections;

namespace junklite
{
    /// <summary>
    /// Energy Wave Mod - On activation, fires a single slow-moving energy pulse.
    /// The pulse damages and drags all enemies caught in its path.
    /// Player is briefly locked during the cast animation, then free to move
    /// while the pulse travels independently.
    /// 
    /// No mutable state on the ScriptableObject; all runtime state lives on
    /// ModInstance (cooldown) and the spawned pulse GameObject.
    /// </summary>
    [CreateAssetMenu(fileName = "EnergyWaveMod", menuName = "Junklite/Mods/Energy Wave")]
    public class EnergyWaveMod : ActiveModData
    {
        #region Config

        [Header("Pulse Prefab")]
        [Tooltip("Prefab with EnergyWavePulse component")]
        public GameObject wavePrefab;

        [Header("Pulse Stats")]
        public float tickDamage = 10f;
        public float tickInterval = 0.3f;
        public float pulseSpeed = 4f;
        public float pulseLifetime = 5f;
        public float pulseRadius = 1.5f;
        public LayerMask enemyLayerMask = 1;

        [Header("Drag")]
        [Tooltip("What fraction of pulseLifetime is spent dragging (0-1)")]
        [Range(0f, 1f)]
        public float dragDurationRatio = 0.7f;

        [Header("Spawn")]
        [Tooltip("Height offset from player position")]
        public float spawnHeightOffset = 0.5f;
        [Tooltip("Forward offset from player position")]
        public float spawnForwardOffset = 0.5f;

        [Header("Cast Timing")]
        [Tooltip("How long the player is locked during the cast wind-up")]
        public float castLockDuration = 0.3f;
        [Tooltip("Brief invulnerability after cast lock ends")]
        public float castInvulnerability = 0.2f;

        [Header("Animation")]
        [Tooltip("Animation to play during cast (leave empty for none)")]
        public string firingAnimationName = "";

        [Header("VFX")]
        public GameObject activationVFX;

        [Header("Hit Feedback")]
        public float hitShakeIntensity = 0.5f;

        #endregion

        #region Overrides

        public override void OnHitRegistered(ModInstance instance, PlayerCharacter player, EnemyCharacter enemy, float damageDealt)
        {
            // No charges needed; cooldown only.
        }

        protected override bool ExecuteAbility(
            ModInstance instance,
            PlayerCharacter player,
            ModExecutionRunner executionRunner)
        {
            if (wavePrefab == null)
            {
                Debug.LogWarning("[EnergyWave] No wave prefab assigned.");
                return false;
            }

            return executionRunner.TryStart(
                instance,
                context => CoCastAndFire(context, player));
        }

        #endregion

        #region Cast Sequence

        private IEnumerator CoCastAndFire(ModExecutionContext context, PlayerCharacter player)
        {
            var playerState = player.PlayerState;
            var spineAnim = player.GetComponent<SpineAnimationController>();

            context.LockPlayerControl();

            // VFX + animation
            if (activationVFX != null)
                Object.Instantiate(activationVFX, player.transform.position, Quaternion.identity);

            if (spineAnim != null && !string.IsNullOrEmpty(firingAnimationName))
                spineAnim.ForcePlayOverride(firingAnimationName, true, () => { });

            // Spawn the pulse
            SpawnPulse(player);

            // Hold the lock for cast duration
            yield return new WaitForSeconds(castLockDuration);

            if (playerState != null)
                playerState.ApplyInvulnerability(castInvulnerability);
        }

        private void SpawnPulse(PlayerCharacter player)
        {
            float facing = Mathf.Sign(player.transform.localScale.x);
            Vector3 spawnPos = player.transform.position
                             + Vector3.up * spawnHeightOffset
                             + Vector3.right * (facing * spawnForwardOffset);

            Vector3 direction = player.transform.right * facing;

            var go = Object.Instantiate(wavePrefab, spawnPos, Quaternion.identity);
            go.transform.Rotate(0, (facing < 0 ? 0 : 180) + player.transform.rotation.eulerAngles.y, 0);
            var pulse = go.GetComponent<EnergyWavePulse>();

            if (pulse != null)
            {
                pulse.Initialize(
                    direction,
                    pulseSpeed,
                    pulseLifetime,
                    tickDamage,
                    tickInterval,
                    pulseRadius,
                    enemyLayerMask,
                    player.gameObject,
                    hitShakeIntensity,
                    dragDurationRatio
                );
            }
            else
            {
                Debug.LogWarning("[EnergyWave] Wave prefab missing EnergyWavePulse component.");
                Object.Destroy(go);
            }
        }

        #endregion
    }
}
