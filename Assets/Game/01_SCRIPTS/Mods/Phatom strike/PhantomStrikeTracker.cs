using UnityEngine;
using System;
using System.Collections;

namespace junklite
{
    /// <summary>
    /// Tracks hits for Phantom Strike mod and executes the special slam attack.
    /// </summary>
    [DefaultExecutionOrder(6)]
    public class PhantomStrikeTracker : MonoBehaviour
    {
        [SerializeField] private Transform impactVFXSpawnPoint;

        private const float MAX_DESCENT_TIME = 5f;

        private const string GROUND_POUND_ANIM = "GroundPound";

        private PhantomStrikeMod modData;
        private PlayerCharacter player;
        private PlayerState playerState;
        private SpineAnimationController spineAnim;
        private Damageable damageable;
        private Rigidbody rb;

        private int currentHits;
        private bool isActive;
        private bool isExecutingSpecial;

        private static readonly Collider[] overlapBuffer = new Collider[32];

        public event Action<int, int> OnHitsChanged;
        public event Action OnSpecialReady;
        public event Action OnSpecialUsed;
        public event Action OnHitsReset;

        public int CurrentHits => currentHits;
        public int HitsRequired => modData != null ? modData.hitsRequired : 3;
        public bool IsSpecialReady => currentHits >= HitsRequired && !isExecutingSpecial;
        public bool IsActive => isActive;
        public bool IsExecutingSpecial => isExecutingSpecial;
        public PhantomStrikeMod ModData => modData;

        public void Initialize(PhantomStrikeMod mod)
        {
            modData = mod;
            player = GetComponent<PlayerCharacter>();
            playerState = GetComponent<PlayerState>();
            spineAnim = GetComponent<SpineAnimationController>();
            damageable = GetComponent<Damageable>();
            rb = GetComponent<Rigidbody>();
        }

        public void SetActive(bool active)
        {
            if (isActive == active) return;

            isActive = active;

            if (active)
            {
                if (damageable != null)
                    damageable.OnDamaged += HandleDamageTaken;

                if (GameInputManager.Instance != null)
                    GameInputManager.Instance.OnSpecialAttack += HandleSpecialAttackInput;

                OnHitsChanged?.Invoke(currentHits, HitsRequired);
            }
            else
            {
                if (damageable != null)
                    damageable.OnDamaged -= HandleDamageTaken;

                if (GameInputManager.Instance != null)
                    GameInputManager.Instance.OnSpecialAttack -= HandleSpecialAttackInput;
            }
        }

        public void AddHit()
        {
            if (!isActive || isExecutingSpecial || IsSpecialReady) return;

            currentHits++;
            OnHitsChanged?.Invoke(currentHits, HitsRequired);

            if (IsSpecialReady)
                OnSpecialReady?.Invoke();
        }

        public void ResetHits()
        {
            if (currentHits == 0) return;

            currentHits = 0;
            OnHitsReset?.Invoke();
            OnHitsChanged?.Invoke(currentHits, HitsRequired);
        }

        private void HandleDamageTaken(float damage, GameObject source)
        {
            if (!isExecutingSpecial)
                ResetHits();
        }

        private void HandleSpecialAttackInput()
        {
            if (!isActive || !IsSpecialReady || isExecutingSpecial) return;
            StartCoroutine(ExecuteSpecialAttack());
        }

        private IEnumerator ExecuteSpecialAttack()
        {
            if (player == null || playerState == null || modData == null)
                yield break;

            isExecutingSpecial = true;
            Vector3 startPosition = player.transform.position;

            // Phase 1: Lock input and physics for full anim-driven move
            playerState.SetInputLocked(true);
            playerState.SetVulnerable(false);

            if (player.Controller != null)
            {
                player.Controller.StopAllVelocity();
                player.Controller.CanMove = false;
                player.Controller.SetPhysicsOverride(true);
            }

            bool wasKinematic = false;
            if (rb != null)
            {
                wasKinematic = rb.isKinematic;
                rb.isKinematic = true;
                rb.linearVelocity = Vector3.zero;
            }

            // Zoom out smooth and fast; camera keeps following player
            if (CameraManager.Instance != null)
                CameraManager.Instance.RequestZoomOut();

            // Optional: uncomment for vanish VFX at start
            // if (modData.vanishVFX != null)
            //     Instantiate(modData.vanishVFX, startPosition, Quaternion.identity);

            // Phase 2: Play full GroundPound animation (anim drives height and ground pound; no manual teleport/hang/descend)
            // Commented out: manual vanish, hang time, descend - we use the animation purely.
            // player.SetVisible(false);
            // Vector3 airPosition = new Vector3(startPosition.x, startPosition.y + modData.spawnHeight, startPosition.z);
            // player.transform.position = airPosition;
            // playerState.SetGrounded(false);
            // yield return new WaitForSeconds(modData.hangTime);
            // player.SetVisible(true);
            // ... manual descent loop ...

            bool animFinished = false;
            if (spineAnim != null && spineAnim.ForcePlayOverride(GROUND_POUND_ANIM, false, () => animFinished = true))
            {
                while (!animFinished)
                    yield return null;
            }
            else
            {
                // Fallback if no GroundPound anim: short wait then impact at current position
                yield return new WaitForSeconds(0.5f);
            }

            // Phase 3: Impact at current position (after anim lands)
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

            // Phase 4: Recovery
            yield return new WaitForSeconds(modData.recoveryTime);

            if (CameraManager.Instance != null)
                CameraManager.Instance.RequestZoomBackIn();

            if (rb != null)
                rb.isKinematic = wasKinematic;

            if (player.Controller != null)
            {
                player.Controller.SetPhysicsOverride(false);
                player.Controller.CanMove = true;
            }

            playerState.ApplyInvulnerability(0.2f);
            playerState.SetInputLocked(false);

            currentHits = 0;
            OnSpecialUsed?.Invoke();
            OnHitsChanged?.Invoke(currentHits, HitsRequired);

            isExecutingSpecial = false;
        }

        private void DealSlamDamage(Vector3 position)
        {
            float damage = modData.slamDamage * modData.criticalMultiplier;
            int hitCount = Physics.OverlapSphereNonAlloc(position, modData.slamRadius, overlapBuffer, modData.enemyLayerMask);

            for (int i = 0; i < hitCount; i++)
            {
                var col = overlapBuffer[i];
                if (col.gameObject == gameObject) continue;

                var target = col.GetComponentInParent<IDamageable>();
                if (target != null && target.IsAlive)
                    target.TakeDamage(new DamageInfo(damage, gameObject, DamageType.Physical, Vector2.zero));
            }
        }

        private void OnDestroy()
        {
            if (damageable != null)
                damageable.OnDamaged -= HandleDamageTaken;

            if (GameInputManager.Instance != null)
                GameInputManager.Instance.OnSpecialAttack -= HandleSpecialAttackInput;
        }

        private void OnDisable()
        {
            if (!isExecutingSpecial) return;

            isExecutingSpecial = false;

            if (CameraManager.Instance != null)
                CameraManager.Instance.RequestZoomBackIn();

            if (playerState != null)
            {
                playerState.SetInputLocked(false);
                playerState.SetVulnerable(true);
            }

            if (player != null)
            {
                player.SetVisible(true);

                if (player.Controller != null)
                {
                    player.Controller.SetPhysicsOverride(false);
                    player.Controller.CanMove = true;
                }
            }
        }
    }
}