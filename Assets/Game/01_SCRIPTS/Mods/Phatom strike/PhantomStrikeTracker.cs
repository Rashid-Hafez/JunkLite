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

        private PhantomStrikeMod modData;
        private PlayerCharacter player;
        private PlayerState playerState;
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

            // Phase 1: Vanish
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

            player.RequestCameraFollow(false);
            player.SetVisible(false);

            if (modData.vanishVFX != null)
                Instantiate(modData.vanishVFX, startPosition, Quaternion.identity);

            // Phase 2: Hang Time
            Vector3 airPosition = new Vector3(startPosition.x, startPosition.y + modData.spawnHeight, startPosition.z);
            player.transform.position = airPosition;
            playerState.SetGrounded(false);

            yield return new WaitForSeconds(modData.hangTime);

            // Phase 3: Descend
            player.SetVisible(true);

            if (modData.descentVFX != null)
                Instantiate(modData.descentVFX, player.transform.position, Quaternion.identity);

            float elapsed = 0f;
            float speed = modData.descentSpeed;

            while (!playerState.IsGrounded && elapsed < MAX_DESCENT_TIME)
            {
                player.transform.position += Vector3.down * speed * Time.deltaTime;
                elapsed += Time.deltaTime;
                yield return null;
            }

            // Phase 4: Impact
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

            // Phase 5: Recovery
            yield return new WaitForSeconds(modData.recoveryTime);

            if (rb != null)
                rb.isKinematic = wasKinematic;

            if (player.Controller != null)
            {
                player.Controller.SetPhysicsOverride(false);
                player.Controller.CanMove = true;
            }

            player.RequestCameraFollow(true);
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

            if (playerState != null)
            {
                playerState.SetInputLocked(false);
                playerState.SetVulnerable(true);
            }

            if (player != null)
            {
                player.SetVisible(true);
                player.RequestCameraFollow(true);

                if (player.Controller != null)
                {
                    player.Controller.SetPhysicsOverride(false);
                    player.Controller.CanMove = true;
                }
            }
        }
    }
}