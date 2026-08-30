using UnityEngine;
using System;
using System.Collections;
using System.Collections.Generic;

namespace junklite
{
    /// <summary>
    /// Executes the Phantom Strike slam attack. Charge tracking is handled by ModInstance.
    /// Resets charges when player takes damage.
    /// </summary>
    [DefaultExecutionOrder(6)]
    public class PhantomStrikeTracker : MonoBehaviour
    {
        #region Fields

        [SerializeField] private Transform impactVFXSpawnPoint;

        private const string DEFAULT_GROUND_POUND_ANIM = "GroundPound";

        private PhantomStrikeMod modData;
        private PlayerCharacter player;
        private PlayerState playerState;
        private SpineAnimationController spineAnim;
        private Damageable damageable;

        private ModInstance currentModInstance;
        private ModExecutionRunner executionRunner;
        private bool isActive;
        private bool isExecutingSpecial;

        private readonly Collider[] overlapBuffer = new Collider[32];

        #endregion

        #region Properties

        public bool IsActive => isActive;
        public bool IsExecutingSpecial => isExecutingSpecial;
        public PhantomStrikeMod ModData => modData;

        // Events for UI
        public event Action<int, int> OnChargesChanged;
        public event Action OnSpecialReady;
        public event Action OnSpecialUsed;
        public event Action OnChargesReset;

        #endregion

        #region Lifecycle

        public void Initialize(PhantomStrikeMod mod)
        {
            modData = mod;
            player = GetComponentInParent<PlayerCharacter>();
            playerState = player?.GetComponent<PlayerState>();
            spineAnim = player?.GetComponent<SpineAnimationController>();
            damageable = player?.GetComponent<Damageable>();
        }

        public void SetActive(bool active)
        {
            if (isActive == active) return;
            isActive = active;

            if (active)
            {
                if (damageable != null)
                    damageable.OnDamageResolved += HandleDamageTaken;
            }
            else
            {
                if (damageable != null)
                    damageable.OnDamageResolved -= HandleDamageTaken;

                currentModInstance = null;
            }
        }

        private void OnDestroy()
        {
            if (damageable != null)
                damageable.OnDamageResolved -= HandleDamageTaken;
        }

        private void OnDisable()
        {
            if (isExecutingSpecial && currentModInstance != null)
                executionRunner?.Cancel(currentModInstance);
        }

        #endregion

        #region Charge Notifications

        /// <summary>
        /// Called by external systems to notify UI of charge changes.
        /// </summary>
        public void NotifyChargesChanged(int current, int required)
        {
            OnChargesChanged?.Invoke(current, required);
            if (current >= required)
                OnSpecialReady?.Invoke();
        }

        #endregion

        #region Damage Reset

        private void HandleDamageTaken(DamageResult result, DamageRequest request)
        {
            if (isExecutingSpecial) return;

            if (currentModInstance != null)
            {
                currentModInstance.ResetCharges();
                OnChargesReset?.Invoke();
                OnChargesChanged?.Invoke(0, modData?.chargesRequired ?? 3);
            }
        }

        #endregion

        #region Slam Execution

        public bool ExecuteSlam(ModInstance instance, ModExecutionRunner runner)
        {
            if (isExecutingSpecial || runner == null) return false;

            currentModInstance = instance;
            executionRunner = runner;
            return runner.TryStart(instance, CoExecuteSlam);
        }

        private IEnumerator CoExecuteSlam(ModExecutionContext context)
        {
            if (player == null || playerState == null || modData == null)
                yield break;

            isExecutingSpecial = true;
            Vector3 startPosition = player.transform.position;

            context.LockPlayerControl(overridePhysics: true);
            context.AddCleanup(() =>
            {
                if (CameraManager.Instance != null)
                    CameraManager.Instance.RequestZoomBackIn();

                if (player != null)
                    player.SetVisible(true);

                isExecutingSpecial = false;
                executionRunner = null;
            });

            // Camera zoom
            if (CameraManager.Instance != null)
            {
                if (modData.cameraZoomOutValue > 0f)
                    CameraManager.Instance.RequestZoomOut(modData.cameraZoomOutValue);
                else
                    CameraManager.Instance.RequestZoomOut();
            }

            if (modData.vanishVFX != null)
                Instantiate(modData.vanishVFX, startPosition, Quaternion.identity);

            // Start animation
            string animName = string.IsNullOrEmpty(modData.groundPoundAnimationName)
                ? DEFAULT_GROUND_POUND_ANIM
                : modData.groundPoundAnimationName;

            if (spineAnim != null)
                spineAnim.ForcePlayOverride(animName, false, () => { });

            // Teleport up
            Vector3 airPosition = new Vector3(startPosition.x, startPosition.y + modData.spawnHeight, startPosition.z);
            player.transform.position = airPosition;
            playerState.SetGrounded(false);

            // Drift up
            float driftEndY = airPosition.y + modData.driftUpHeight;
            float driftElapsed = 0f;
            while (driftElapsed < modData.driftUpDuration)
            {
                driftElapsed += Time.deltaTime;
                float t = Mathf.Clamp01(driftElapsed / modData.driftUpDuration);
                float y = Mathf.Lerp(airPosition.y, driftEndY, t);
                player.transform.position = new Vector3(startPosition.x, y, startPosition.z);
                yield return null;
            }

            // Raycast for ground
            float groundY = startPosition.y;
            if (modData.groundLayerMask != 0 &&
                Physics.Raycast(player.transform.position + Vector3.up * 0.5f, Vector3.down, out RaycastHit groundHit, 50f, modData.groundLayerMask))
            {
                groundY = groundHit.point.y;
            }

            // Slam down
            float currentY = player.transform.position.y;
            float speed = Mathf.Max(modData.slamDescentSpeed, 1f);
            while (currentY > groundY)
            {
                currentY -= speed * Time.deltaTime;
                if (currentY < groundY) currentY = groundY;
                player.transform.position = new Vector3(startPosition.x, currentY, startPosition.z);
                yield return null;
            }

            player.transform.position = new Vector3(startPosition.x, groundY, startPosition.z);
            playerState.SetGrounded(true);

            // Impact
            Vector3 impactPosition = player.transform.position;

            if (modData.impactVFX != null)
            {
                Vector3 vfxPos = impactVFXSpawnPoint != null ? impactVFXSpawnPoint.position : impactPosition;
                Quaternion vfxRot = impactVFXSpawnPoint != null ? impactVFXSpawnPoint.rotation : Quaternion.identity;
                Instantiate(modData.impactVFX, vfxPos, vfxRot);
            }

            DealSlamDamage(impactPosition);

            if (modData.cameraShakeIntensity > 0f)
            {
                var impulse = player.GetComponent<Unity.Cinemachine.CinemachineImpulseSource>();
                if (impulse != null && FeedbackManager.Instance != null)
                    FeedbackManager.Instance.DoCameraShake(impulse, modData.cameraShakeIntensity);
            }

            // Recovery
            yield return new WaitForSeconds(modData.recoveryTime);

            playerState.ApplyInvulnerability(0.2f);

            // Reset charges on the ModInstance
            if (currentModInstance != null)
                currentModInstance.ResetCharges();

            OnSpecialUsed?.Invoke();
            OnChargesChanged?.Invoke(0, modData.chargesRequired);

        }

        private void DealSlamDamage(Vector3 position)
        {
            float damage = modData.slamDamage * modData.criticalMultiplier;
            int hitCount = Physics.OverlapSphereNonAlloc(position, modData.slamRadius, overlapBuffer, modData.enemyLayerMask);
            var damagedReceivers = new HashSet<IDamageReceiver>();

            for (int i = 0; i < hitCount; i++)
            {
                var col = overlapBuffer[i];
                if (col.gameObject == player.gameObject) continue;

                if (!DamageReceiverUtility.TryGetReceiver(col, out var receiver)) continue;
                if (!damagedReceivers.Add(receiver) || !receiver.IsAlive) continue;

                receiver.ReceiveDamage(new DamageRequest(
                    damage,
                    player.gameObject,
                    DamageType.Physical,
                    Vector2.zero));
            }
        }

        #endregion
    }
}
