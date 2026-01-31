using UnityEngine;
using System;
using System.Collections.Generic;
using System.Collections;

namespace junklite
{
    [RequireComponent(typeof(Collider))]
    public class WeaponManager : MonoBehaviour
    {
        [Header("Weapon Holder")]
        public Transform weaponHolder;

        [Header("Attack Transforms (Scene Anchors)")]
        [SerializeField] private Transform sideAttack;
        [SerializeField] private Transform upAttack;
        [SerializeField] private Transform downAttack;

        [Header("Fallback Hit Radii")]
        [SerializeField] private float sideRadius = 1f;
        [SerializeField] private float upRadius = 1f;
        [SerializeField] private float downRadius = 1f;

        [Header("Hit Detection")]
        [SerializeField] private LayerMask enemyLayer;
        [SerializeField] private LayerMask environmentLayer;

        [Header("Knockback")]
        [SerializeField] private Vector2 defaultKnockback = new Vector2(8f, 4f);

        [Header("Feedback Settings")]
        [SerializeField] private Unity.Cinemachine.CinemachineImpulseSource impulseSource;
        [SerializeField] private float enemyHitHitstopDuration = 0.08f;
        [SerializeField] private float enemyHitShakeForce = 0.8f;
        [SerializeField] private float enemyHitDelay = 0.05f;

        [Header("Recoil")]
        [SerializeField] private float sideRecoil = 6f;

        [Header("Attack Settings")]
        [SerializeField] private float facingLockDuration = 0.25f;
        [SerializeField] private float inputThreshold = 0.5f;

        [Header("Slash Pooling")]
        [SerializeField] private int poolSizePerSlash = 5;
        [SerializeField] private Transform slashPoolRoot;
        [SerializeField] private float slashOffsetDirection = 0.5f;
        [SerializeField] private float slashOffsetDistance = 0.5f;
        [SerializeField] private float slashScale = 1f;
        [SerializeField] private float slashLifetime = 0.15f;

        [Header("Debug")]
        [SerializeField] private bool logAttacks = false;

        private readonly Dictionary<GameObject, Queue<GameObject>> slashPools = new();

        // Internal refs
        private Rigidbody playerRb;
        private Transform playerTransform;
        private PlayerState playerState;
        private PlayerCharacter playerCharacter;
        private SpineAnimationController spineAnimController;
        private Character2D5Controller controller;

        public WeaponInstance CurrentWeapon { get; private set; }
        private WorldWeaponPickup storedPickup;

        public event Action OnWeaponChanged;
        public event Action OnEnemyHit;

        public float Facing => Mathf.Sign(playerTransform.localScale.x);

        // =====================================================================
        // ATTACK STATE
        // =====================================================================

        private bool isAttacking;
        private AttackDirection currentAttackDir;
        private WeaponData.ComboStep currentStep;
        private int currentComboIndex;
        private Transform currentAttackAnchor;

        // Input buffer
        private bool hasBufferedInput;
        private Vector2 bufferedInput;
        private bool bufferedGrounded;
        private float bufferTimer;
        private const float BUFFER_DURATION = 0.3f;

        // Public state for external systems
        public bool IsAttacking => isAttacking;
        public AttackDirection CurrentAttackDirection => currentAttackDir;

        #region Unity

        private void Awake()
        {
            playerRb = GetComponentInParent<Rigidbody>();
            playerTransform = transform.parent ?? transform;
            playerState = GetComponentInParent<PlayerState>();
            playerCharacter = GetComponentInParent<PlayerCharacter>();
            spineAnimController = GetComponentInParent<SpineAnimationController>();
            controller = GetComponentInParent<Character2D5Controller>();

            if (impulseSource == null)
            {
                impulseSource = GetComponent<Unity.Cinemachine.CinemachineImpulseSource>()
                                ?? GetComponentInParent<Unity.Cinemachine.CinemachineImpulseSource>()
                                ?? GetComponentInChildren<Unity.Cinemachine.CinemachineImpulseSource>();
            }
        }

        private void Update()
        {
            // Tick buffer timer
            if (hasBufferedInput)
            {
                bufferTimer -= Time.deltaTime;
                if (bufferTimer <= 0f)
                {
                    hasBufferedInput = false;
                    Log("Buffer expired");
                }
                // Try to execute buffer if conditions are met
                else if (!isAttacking && CurrentWeapon != null && CurrentWeapon.CanAttack)
                {
                    hasBufferedInput = false;
                    Log("Executing buffered attack (cooldown cleared)");
                    AttackDirection dir = ResolveAttackDirection(bufferedInput, bufferedGrounded);
                    StartAttack(dir);
                }
            }
        }

        private void OnTriggerEnter(Collider other)
        {
            var weaponPickup = other.GetComponent<WorldWeaponPickup>();
            if (weaponPickup != null && CurrentWeapon == null)
            {
                PickupWeapon(weaponPickup);
                return;
            }

            var modPickup = other.GetComponent<WorldModPickup>();
            if (modPickup != null)
                PickupMod(modPickup);
        }

        #endregion Unity

        #region Public API

        /// <summary>
        /// Main attack entry point. Takes raw input - handles direction resolution internally.
        /// </summary>
        public void Attack(Vector2 moveInput, bool isGrounded)
        {
            if (CurrentWeapon == null)
                return;

            // If currently attacking, buffer the input
            if (isAttacking)
            {
                BufferAttack(moveInput, isGrounded);
                return;
            }

            // Check weapon cooldown
            if (!CurrentWeapon.CanAttack)
            {
                Log("Attack blocked - weapon on cooldown");
                BufferAttack(moveInput, isGrounded);
                return;
            }

            // Resolve direction and execute
            AttackDirection dir = ResolveAttackDirection(moveInput, isGrounded);
            StartAttack(dir);
        }

        /// <summary>
        /// Called by SpineAnimationController when attack animation completes.
        /// </summary>
        public void OnAttackAnimationComplete()
        {
            Log($"Attack complete - {currentAttackDir}, combo {currentComboIndex}");

            // Notify weapon to advance combo and start cooldown + combo window
            if (CurrentWeapon != null)
                CurrentWeapon.OnAttackComplete(currentAttackDir);

            isAttacking = false;

            if (playerState != null)
                playerState.SetAttacking(false);

            // Buffer will be executed by Update() when cooldown clears
        }

        /// <summary>
        /// Called when attack is interrupted (dash, stun, death, etc.)
        /// </summary>
        public void OnAttackInterrupted()
        {
            Log("Attack interrupted");

            if (CurrentWeapon != null)
                CurrentWeapon.OnAttackInterrupted();

            isAttacking = false;
            hasBufferedInput = false;

            if (playerState != null)
                playerState.SetAttacking(false);
        }

        public void DropWeapon()
        {
            if (CurrentWeapon == null || storedPickup == null)
                return;

            CurrentWeapon.transform.SetParent(storedPickup.transform, false);
            CurrentWeapon.gameObject.SetActive(false);
            CurrentWeapon = null;

            storedPickup.transform.position = transform.position + Vector3.right * Facing * 1.2f;
            storedPickup.gameObject.SetActive(true);
            storedPickup = null;

            OnWeaponChanged?.Invoke();
        }

        public void SetWeaponVisible(bool visible)
        {
            if (CurrentWeapon == null)
                return;

            var renderers = CurrentWeapon.GetComponentsInChildren<SpriteRenderer>(true);
            foreach (var sr in renderers)
            {
                if (sr != null)
                    sr.enabled = visible;
            }
        }

        public Transform GetAttackTransform(AttackDirection dir)
        {
            return dir switch
            {
                AttackDirection.Up => upAttack,
                AttackDirection.Down => downAttack,
                _ => sideAttack
            };
        }

        #endregion Public API

        #region Direction Resolution

        private AttackDirection ResolveAttackDirection(Vector2 moveInput, bool isGrounded)
        {
            // Up attack: pressing up
            if (moveInput.y > inputThreshold)
                return AttackDirection.Up;

            // Down attack: pressing down AND airborne
            if (moveInput.y < -inputThreshold && !isGrounded)
                return AttackDirection.Down;

            // Default: side attack
            return AttackDirection.Side;
        }

        #endregion Direction Resolution

        #region Attack Core

        private void BufferAttack(Vector2 moveInput, bool isGrounded)
        {
            hasBufferedInput = true;
            bufferedInput = moveInput;
            bufferedGrounded = isGrounded;
            bufferTimer = BUFFER_DURATION;
            Log($"Attack buffered");
        }

        private void StartAttack(AttackDirection dir)
        {
            // Get combo step and animation from weapon
            if (!CurrentWeapon.TryGetComboStep(dir, out var step, out int comboIndex, out string animName))
            {
                Log($"No combo step available for {dir}");
                return;
            }

            // Set state
            isAttacking = true;
            currentAttackDir = dir;
            currentStep = step;
            currentComboIndex = comboIndex;
            currentAttackAnchor = GetAttackTransform(dir);

            Log($"Attack: {dir}, combo {comboIndex}, anim '{animName}'");

            // Lock facing direction
            if (controller != null && facingLockDuration > 0f)
                controller.LockFacing(facingLockDuration);

            // Update player state
            if (playerState != null)
                playerState.SetAttacking(true);

            // Play animation - pass the animation name, SpineAnimationController just plays it
            if (spineAnimController != null && !string.IsNullOrEmpty(animName))
            {
                spineAnimController.PlayAttackAnimation(animName);
            }
            else
            {
                // No animation - complete immediately
                Log("No animation - completing immediately");
                OnAttackAnimationComplete();
            }

            // Execute attack (hit detection, damage, VFX)
            ExecuteAttack(dir, step);
        }

        private void ExecuteAttack(AttackDirection dir, WeaponData.ComboStep step)
        {
            Transform anchor = GetAttackTransform(dir);
            if (anchor == null)
                return;

            float radius = step.hitRadius > 0f ? step.hitRadius : GetFallbackRadius(dir);

            // Detect hits
            var hitResult = DetectHit(anchor.position, radius);

            // Handle hit
            if (hitResult.type == AttackHitResult.Enemy && hitResult.target != null)
            {
                StartCoroutine(DelayedDamage(hitResult.target, step));
            }

            // Spawn VFX
            SpawnAttackVFX(dir, step, anchor, hitResult);

            // Apply recoil
            if (hitResult.type != AttackHitResult.None)
                ApplyRecoil(dir);
        }

        #endregion Attack Core

        #region Hit Detection

        private struct HitDetectionResult
        {
            public AttackHitResult type;
            public Collider target;
            public Vector3 point;
        }

        private HitDetectionResult DetectHit(Vector3 origin, float radius)
        {
            var result = new HitDetectionResult { type = AttackHitResult.None };

            Collider[] hits = Physics.OverlapSphere(
                origin,
                radius,
                enemyLayer | environmentLayer,
                QueryTriggerInteraction.Ignore
            );

            Collider closestEnemy = null;
            float closestDist = float.MaxValue;
            bool hitEnvironment = false;

            for (int i = 0; i < hits.Length; i++)
            {
                int mask = 1 << hits[i].gameObject.layer;

                if ((mask & enemyLayer) != 0)
                {
                    float dist = Vector3.Distance(origin, hits[i].transform.position);
                    if (dist < closestDist)
                    {
                        closestDist = dist;
                        closestEnemy = hits[i];
                    }
                }
                else if ((mask & environmentLayer) != 0)
                {
                    hitEnvironment = true;
                }
            }

            if (closestEnemy != null)
            {
                result.type = AttackHitResult.Enemy;
                result.target = closestEnemy;
                result.point = closestEnemy.ClosestPoint(origin);
            }
            else if (hitEnvironment)
            {
                result.type = AttackHitResult.Environment;
            }

            return result;
        }

        #endregion Hit Detection

        #region Damage

        private IEnumerator DelayedDamage(Collider target, WeaponData.ComboStep step)
        {
            if (enemyHitDelay > 0f)
                yield return new WaitForSeconds(enemyHitDelay);

            if (target == null)
                yield break;

            PlayHitFeedback();
            DealDamage(target, step);
        }

        private void DealDamage(Collider target, WeaponData.ComboStep step)
        {
            var damageable = target.GetComponent<IDamageable>()
                          ?? target.GetComponentInParent<IDamageable>();

            if (damageable == null || !damageable.IsAlive)
                return;

            float damage = CurrentWeapon != null ? CurrentWeapon.baseDamage : 10f;
            if (step.damageMultiplier > 0f)
                damage *= step.damageMultiplier;

            var damageInfo = new DamageInfo(damage, playerTransform.gameObject, DamageType.Physical, defaultKnockback);
            bool damageDealt = damageable.TakeDamage(damageInfo);

            if (damageDealt)
            {
                OnEnemyHit?.Invoke();

                // Trigger weapon mods
                if (CurrentWeapon != null)
                {
                    var enemy = target.GetComponent<EnemyCharacter>()
                             ?? target.GetComponentInParent<EnemyCharacter>();
                    CurrentWeapon.TriggerModsOnHit(enemy, playerCharacter);
                }

                // Enemy hit VFX
                if (CombatEffectsManager.Instance != null)
                {
                    Vector3 originPoint = currentAttackAnchor != null
                        ? currentAttackAnchor.position
                        : playerTransform.position + Vector3.up;

                    Vector3 hitPoint = target.ClosestPoint(originPoint);
                    Vector3 hitDir = (target.transform.position - originPoint).normalized;

                    CombatEffectsManager.Instance.SpawnEnemyHitVFX(hitPoint, hitDir);
                    CombatEffectsManager.Instance.SpawnEnemyHurtParticle(hitPoint, hitDir);
                }
            }
        }

        private void PlayHitFeedback()
        {
            if (FeedbackManager.Instance != null)
                FeedbackManager.Instance.DoHitFeedback(impulseSource, enemyHitHitstopDuration, enemyHitShakeForce);
        }

        #endregion Damage

        #region VFX

        private void SpawnAttackVFX(AttackDirection dir, WeaponData.ComboStep step, Transform anchor, HitDetectionResult hit)
        {
            if (hit.type != AttackHitResult.None)
            {
                float radius = step.hitRadius > 0f ? step.hitRadius : GetFallbackRadius(dir);
                Vector3 impactPoint = ResolveImpactPoint(dir, anchor.position, radius);
                Vector3 attackDir = GetAttackDirection(dir);

                if (hit.type == AttackHitResult.Environment && CombatEffectsManager.Instance != null)
                {
                    CombatEffectsManager.Instance.SpawnEnvHitParticle(impactPoint, attackDir);
                    CombatEffectsManager.Instance.SpawnHitCross(impactPoint);
                }

                if (step.slashPrefab != null)
                    PlaySlashAt(step.slashPrefab, anchor, impactPoint);
            }
            else
            {
                if (step.slashPrefab != null)
                    PlaySlash(step.slashPrefab, anchor);
            }
        }

        private Vector3 ResolveImpactPoint(AttackDirection dir, Vector3 origin, float radius)
        {
            Vector3 rayDir = GetAttackDirection(dir);
            Vector3 rayStart = origin - rayDir * (radius + 0.25f);
            float rayLength = (radius + 0.25f) + (radius + 0.5f);

            Vector3 point = origin;
            Vector3 normal = -rayDir;

            if (Physics.Raycast(rayStart, rayDir, out RaycastHit hit, rayLength, enemyLayer | environmentLayer, QueryTriggerInteraction.Ignore))
            {
                point = hit.point;
                normal = hit.normal;
            }
            else
            {
                point = origin + rayDir * radius;
            }

            point += normal * 0.06f;

            Camera cam = Camera.main;
            if (cam != null)
                point += (-cam.transform.forward) * 0.1f;

            return point;
        }

        private Vector3 GetAttackDirection(AttackDirection dir)
        {
            return dir switch
            {
                AttackDirection.Up => Vector3.up,
                AttackDirection.Down => Vector3.down,
                _ => Vector3.right * Facing
            };
        }

        #endregion VFX

        #region Slash Pool

        private void InitializeSlashPools(WeaponData weaponData)
        {
            slashPools.Clear();

            void Register(GameObject prefab)
            {
                if (prefab == null || slashPools.ContainsKey(prefab))
                    return;

                Queue<GameObject> pool = new();
                for (int i = 0; i < poolSizePerSlash; i++)
                {
                    GameObject slash = Instantiate(prefab, slashPoolRoot);
                    slash.SetActive(false);
                    pool.Enqueue(slash);
                }
                slashPools.Add(prefab, pool);
            }

            if (weaponData.sideCombo != null)
            {
                foreach (var step in weaponData.sideCombo)
                    Register(step.slashPrefab);
            }

            Register(weaponData.upAttack.slashPrefab);
            Register(weaponData.downAttack.slashPrefab);
        }

        private GameObject GetSlash(GameObject prefab, Transform attackAnchor)
        {
            if (prefab == null || !slashPools.TryGetValue(prefab, out var pool))
                return null;

            GameObject slash = pool.Count > 0
                ? pool.Dequeue()
                : Instantiate(prefab, slashPoolRoot);

            slash.transform.SetParent(attackAnchor, false);

            Vector3 offsetToBody = new Vector3(-Facing * slashOffsetDirection, 0f, 0f);
            slash.transform.localPosition = offsetToBody * slashOffsetDistance;
            slash.transform.localRotation = Quaternion.identity;
            slash.transform.localScale = Vector3.one * slashScale;
            slash.SetActive(true);

            return slash;
        }

        private void ReturnSlash(GameObject prefab, GameObject slash)
        {
            slash.SetActive(false);
            slash.transform.SetParent(slashPoolRoot, false);

            if (slashPools.TryGetValue(prefab, out var pool))
                pool.Enqueue(slash);
        }

        private void PlaySlash(GameObject prefab, Transform attackAnchor)
        {
            GameObject slash = GetSlash(prefab, attackAnchor);
            if (slash == null) return;

            StartCoroutine(ReturnSlashAfterDelay(slash, prefab, slashLifetime));
        }

        private void PlaySlashAt(GameObject prefab, Transform attackAnchor, Vector3 worldContactPoint)
        {
            if (prefab == null || !slashPools.TryGetValue(prefab, out var pool))
                return;

            GameObject slash = pool.Count > 0
                ? pool.Dequeue()
                : Instantiate(prefab, slashPoolRoot);

            slash.transform.SetParent(slashPoolRoot, false);
            slash.transform.position = worldContactPoint;
            slash.transform.rotation = attackAnchor.rotation;

            Vector3 scale = Vector3.one * slashScale;
            scale.x *= Facing;
            slash.transform.localScale = scale;

            slash.SetActive(true);

            StartCoroutine(ReturnSlashAfterDelay(slash, prefab, slashLifetime));
        }

        private IEnumerator ReturnSlashAfterDelay(GameObject slash, GameObject prefab, float delay)
        {
            yield return new WaitForSeconds(delay);
            ReturnSlash(prefab, slash);
        }

        #endregion Slash Pool

        #region Pickups

        private void PickupWeapon(WorldWeaponPickup pickup)
        {
            storedPickup = pickup;
            pickup.gameObject.SetActive(false);

            CurrentWeapon = pickup.weaponInstance;
            CurrentWeapon.gameObject.SetActive(true);
            CurrentWeapon.transform.parent = weaponHolder;
            CurrentWeapon.transform.localPosition = Vector3.zero;
            CurrentWeapon.transform.localRotation = Quaternion.Euler(0, 0, -30f);
            CurrentWeapon.transform.localScale = Vector3.one;
            CurrentWeapon.GetComponent<SpriteRenderer>().sortingOrder = 11;
            CurrentWeapon.SetOwnerRigidbody(playerRb);

            var inventory = GetComponent<InventoryComponent>();
            if (inventory != null)
                inventory.EquipAllPossible();

            InitializeSlashPools(CurrentWeapon.weaponData);
            OnWeaponChanged?.Invoke();
        }

        private void PickupMod(WorldModPickup pickup)
        {
            if (pickup.modData == null)
                return;

            if (CurrentWeapon != null && CurrentWeapon.HasFreeSlot)
            {
                if (CurrentWeapon.TryAddMod(pickup.modData))
                {
                    Destroy(pickup.gameObject);
                    return;
                }
            }

            var inventory = GetComponent<InventoryComponent>();
            if (inventory != null)
            {
                inventory.PickupMod(pickup.modData);
                Destroy(pickup.gameObject);
            }
        }

        #endregion Pickups

        #region Helpers

        private float GetFallbackRadius(AttackDirection dir)
        {
            return dir switch
            {
                AttackDirection.Up => upRadius,
                AttackDirection.Down => downRadius,
                _ => sideRadius
            };
        }

        private void ApplyRecoil(AttackDirection dir)
        {
            if (playerRb == null)
                return;

            if (dir == AttackDirection.Side)
            {
                float recoilDir = -Facing;
                playerRb.AddForce(Vector3.right * recoilDir * sideRecoil, ForceMode.Impulse);
            }
        }

        private void Log(string message)
        {
            if (logAttacks)
                Debug.Log($"[WeaponManager] {message}", this);
        }

        #endregion Helpers

        #region Debug

        private void OnDrawGizmosSelected()
        {
            if (sideAttack == null || upAttack == null || downAttack == null)
                return;

            Gizmos.color = Color.red;
            Gizmos.DrawWireSphere(sideAttack.position, sideRadius);

            Gizmos.color = Color.green;
            Gizmos.DrawWireSphere(upAttack.position, upRadius);

            Gizmos.color = Color.cyan;
            Gizmos.DrawWireSphere(downAttack.position, downRadius);
        }

        #endregion Debug
    }

    public enum AttackDirection
    {
        Side,
        Up,
        Down
    }
}