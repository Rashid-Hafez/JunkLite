using UnityEngine;
using System;
using System.Collections;

namespace junklite
{
    /// <summary>
    /// Runtime tracker for Phantom Strike mod. Attached to player.
    /// Tracks successful hits, resets on damage taken, and executes the special slam attack.
    /// </summary>
    public class PhantomStrikeTracker : MonoBehaviour
    {
        [Header("Debug")]
        [SerializeField] private bool debugMode = false;

        private PhantomStrikeMod modData;
        private PlayerCharacter player;
        private PlayerState playerState;
        private Damageable damageable;
        private Rigidbody rb;

        private int currentHits;
        private bool isActive;
        private bool isExecutingSpecial;

        // Events for UI
        public event Action<int, int> OnHitsChanged;
        public event Action OnSpecialReady;
        public event Action OnSpecialUsed;
        public event Action OnHitsReset;

        // Properties
        public int CurrentHits => currentHits;
        public int HitsRequired => modData != null ? modData.hitsRequired : 3;
        public bool IsSpecialReady => currentHits >= HitsRequired && !isExecutingSpecial;
        public bool IsActive => isActive;
        public bool IsExecutingSpecial => isExecutingSpecial;
        public PhantomStrikeMod ModData => modData;

        // Non-alloc buffer for overlap checks
        private static readonly Collider[] overlapBuffer = new Collider[32];

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
            if (isActive == active)
                return;

            isActive = active;

            if (active)
            {
                // Subscribe to damage events
                if (damageable != null)
                    damageable.OnDamaged += HandleDamageTaken;

                // Subscribe to special attack input
                if (GameInputManager.Instance != null)
                    GameInputManager.Instance.OnSpecialAttack += HandleSpecialAttackInput;

                OnHitsChanged?.Invoke(currentHits, HitsRequired);

                if (debugMode)
                    Debug.Log("[PhantomStrike] Activated");
            }
            else
            {
                // Unsubscribe
                if (damageable != null)
                    damageable.OnDamaged -= HandleDamageTaken;

                if (GameInputManager.Instance != null)
                    GameInputManager.Instance.OnSpecialAttack -= HandleSpecialAttackInput;

                if (debugMode)
                    Debug.Log("[PhantomStrike] Deactivated");
            }
        }

        public void AddHit()
        {
            if (!isActive || isExecutingSpecial)
                return;

            if (IsSpecialReady)
                return;

            currentHits++;
            OnHitsChanged?.Invoke(currentHits, HitsRequired);

            if (debugMode)
                Debug.Log($"[PhantomStrike] Hit count: {currentHits}/{HitsRequired}");

            if (IsSpecialReady)
            {
                OnSpecialReady?.Invoke();
                Debug.Log("[PhantomStrike] SPECIAL READY! Press Special Attack to execute.");
            }
        }

        public void ResetHits()
        {
            if (currentHits == 0)
                return;

            currentHits = 0;
            OnHitsReset?.Invoke();
            OnHitsChanged?.Invoke(currentHits, HitsRequired);

            if (debugMode)
                Debug.Log("[PhantomStrike] Hits reset");
        }

        private void HandleDamageTaken(float damage, GameObject source)
        {
            if (!isExecutingSpecial)
                ResetHits();
        }

        private void HandleSpecialAttackInput()
        {
            if (!isActive || !IsSpecialReady || isExecutingSpecial)
                return;

            StartCoroutine(ExecuteSpecialAttack());
        }

        // -----------------------------------------------------------------------
        // SPECIAL ATTACK EXECUTION
        // -----------------------------------------------------------------------

        private IEnumerator ExecuteSpecialAttack()
        {
            if (player == null || playerState == null || modData == null)
                yield break;

            isExecutingSpecial = true;

            // Store initial position for slam target
            Vector3 slamTarget = player.GetGroundPosition();
            Vector3 startPosition = player.transform.position;

            // ===== PHASE 1: VANISH =====
            if (debugMode) Debug.Log("[PhantomStrike] Phase 1: Vanish");

            // Lock input
            playerState.SetInputLocked(true);

            // Make invulnerable
            playerState.SetVulnerable(false);

            // Stop all velocity
            if (player.Controller != null)
            {
                player.Controller.StopAllVelocity();
                player.Controller.CanMove = false;
            }

            // Disable gravity
            bool wasKinematic = false;
            if (rb != null)
            {
                wasKinematic = rb.isKinematic;
                rb.isKinematic = true;
                rb.linearVelocity = Vector3.zero;
            }

            // Disconnect camera
            player.RequestCameraFollow(false);

            // Hide player
            player.SetVisible(false);

            // Spawn vanish VFX (optional)
            if (modData.vanishVFX != null)
                Instantiate(modData.vanishVFX, startPosition, Quaternion.identity);

            // ===== PHASE 2: HANG TIME =====
            if (debugMode) Debug.Log("[PhantomStrike] Phase 2: Hang Time");

            // Teleport player high above (off-screen)
            float spawnHeight = modData.spawnHeight;
            Vector3 airPosition = slamTarget + Vector3.up * spawnHeight;
            player.transform.position = airPosition;

            // Wait while invisible
            yield return new WaitForSeconds(modData.hangTime);

            // ===== PHASE 3: DESCEND =====
            if (debugMode) Debug.Log("[PhantomStrike] Phase 3: Descend");

            // Show player
            player.SetVisible(true);

            // Spawn descent VFX (optional)
            if (modData.descentVFX != null)
                Instantiate(modData.descentVFX, airPosition, Quaternion.identity);

            // Rapid descent to slam target
            float descentDuration = modData.descentDuration;
            float elapsed = 0f;

            while (elapsed < descentDuration)
            {
                elapsed += Time.deltaTime;
                float t = elapsed / descentDuration;

                // Ease-in for acceleration effect
                float easedT = t * t;

                player.transform.position = Vector3.Lerp(airPosition, slamTarget, easedT);
                yield return null;
            }

            // Snap to exact position
            player.transform.position = slamTarget;

            // ===== PHASE 4: IMPACT =====
            if (debugMode) Debug.Log("[PhantomStrike] Phase 4: Impact");

            // Deal damage to all enemies in radius
            DealSlamDamage(slamTarget);

            // Spawn impact VFX (optional)
            if (modData.impactVFX != null)
                Instantiate(modData.impactVFX, slamTarget, Quaternion.identity);

            // Camera shake (optional)
            if (modData.cameraShakeIntensity > 0f)
            {
                var impulse = player.GetComponent<Unity.Cinemachine.CinemachineImpulseSource>();
                if (impulse != null && FeedbackManager.Instance != null)
                    FeedbackManager.Instance.DoCameraShake(impulse, modData.cameraShakeIntensity);
            }

            // ===== PHASE 5: RECOVERY =====
            if (debugMode) Debug.Log("[PhantomStrike] Phase 5: Recovery");

            // Short recovery delay
            yield return new WaitForSeconds(modData.recoveryTime);

            // Re-enable physics
            if (rb != null)
                rb.isKinematic = wasKinematic;

            // Re-enable controller
            if (player.Controller != null)
                player.Controller.CanMove = true;

            // Reconnect camera
            player.RequestCameraFollow(true);

            // Make vulnerable again (with brief invulnerability)
            playerState.ApplyInvulnerability(0.2f);

            // Unlock input
            playerState.SetInputLocked(false);

            // Consume special
            currentHits = 0;
            OnSpecialUsed?.Invoke();
            OnHitsChanged?.Invoke(currentHits, HitsRequired);

            isExecutingSpecial = false;

            Debug.Log("[PhantomStrike] Special attack complete!");
        }

        private void DealSlamDamage(Vector3 position)
        {
            float radius = modData.slamRadius;
            float damage = modData.slamDamage * modData.criticalMultiplier;
            LayerMask enemyMask = modData.enemyLayerMask;

            int hitCount = Physics.OverlapSphereNonAlloc(position, radius, overlapBuffer, enemyMask);

            for (int i = 0; i < hitCount; i++)
            {
                var collider = overlapBuffer[i];

                // Skip self
                if (collider.gameObject == gameObject)
                    continue;

                // Try to deal damage
                var damageable = collider.GetComponentInParent<IDamageable>();
                if (damageable != null && damageable.IsAlive)
                {
                    var damageInfo = new DamageInfo(
                        damage,
                        gameObject,
                        DamageType.Physical,
                        Vector2.zero
                    );

                    damageable.TakeDamage(damageInfo);

                    if (debugMode)
                        Debug.Log($"[PhantomStrike] Hit {collider.name} for {damage} damage");
                }
            }

            if (debugMode)
                Debug.Log($"[PhantomStrike] Slam hit {hitCount} targets");
        }

        // -----------------------------------------------------------------------
        // CLEANUP
        // -----------------------------------------------------------------------

        private void OnDestroy()
        {
            if (damageable != null)
                damageable.OnDamaged -= HandleDamageTaken;

            if (GameInputManager.Instance != null)
                GameInputManager.Instance.OnSpecialAttack -= HandleSpecialAttackInput;
        }

        private void OnDisable()
        {
            // Safety: if disabled during special, reset state
            if (isExecutingSpecial)
            {
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
                        player.Controller.CanMove = true;
                }
            }
        }
    }
}