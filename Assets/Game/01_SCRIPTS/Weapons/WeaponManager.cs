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

        [Header("Feedback Settings")]
        [SerializeField] private Unity.Cinemachine.CinemachineImpulseSource impulseSource;
        [SerializeField] private float enemyHitHitstopDuration = 0.08f;
        [SerializeField] private float enemyHitShakeForce = 0.8f;

        [Header("Attack Hit Window")]
        [SerializeField] private float delayBeforeAttack = 0.1f;
        [SerializeField] private float attackOpenWindow = 0.3f;

        [Header("Recoil")]
        [SerializeField] private float sideRecoil = 6f;

        [Header("Attack Settings")]
        [SerializeField] private float facingLockDuration = 0.25f;
        [SerializeField] private float inputThreshold = 0.5f;

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
        public event Action OnEnvironmentHit;

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

            StartCoroutine(CoAttackDelay(dir, step, anchor));
        }

        private IEnumerator CoAttackDelay(AttackDirection dir, WeaponData.ComboStep step, Transform anchor)
        {
            yield return new WaitForSeconds(delayBeforeAttack);

            float radius = step.hitRadius > 0f ? step.hitRadius : GetFallbackRadius(dir);
            bool hasHitEnemy = false;
            bool hasHitEnvironment = false;
            float windowEnd = Time.time + attackOpenWindow;

            while (Time.time < windowEnd)
            {
                var hitResult = DetectHit(anchor.position, radius);

                if (hitResult.type == AttackHitResult.Enemy && hitResult.target != null && !hasHitEnemy)
                {
                    hasHitEnemy = true;
                    PlayHitFeedback();
                    DealDamage(hitResult.target, step);
                    ApplyRecoil(dir);
                    break;
                }

                if (hitResult.type == AttackHitResult.Environment && !hasHitEnvironment)
                {
                    hasHitEnvironment = true;
                    float radiusForVfx = step.hitRadius > 0f ? step.hitRadius : GetFallbackRadius(dir);
                    Vector3 impactPoint = ResolveImpactPoint(dir, anchor.position, radiusForVfx);
                    Vector3 attackDir = GetAttackDirection(dir);
                    if (CombatEffectsManager.Instance != null)
                    {
                        CombatEffectsManager.Instance.SpawnEnvHitParticle(impactPoint, attackDir);
                        CombatEffectsManager.Instance.SpawnHitCross(impactPoint);

                        OnEnvironmentHit?.Invoke();
                    }
                    ApplyRecoil(dir);
                }

                yield return null;
            }
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

        private void DealDamage(Collider target, WeaponData.ComboStep step)
        {
            var damageable = target.GetComponent<IDamageable>()
                          ?? target.GetComponentInParent<IDamageable>();

            if (damageable == null || !damageable.IsAlive)
                return;

            float damage = CurrentWeapon != null ? CurrentWeapon.baseDamage : 10f;
            if (step.damageMultiplier > 0f)
                damage *= step.damageMultiplier;

            var damageInfo = new DamageInfo(damage, playerTransform.gameObject, DamageType.Physical, CurrentWeapon.weaponData.knockbackForce);
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
                    Vector3 hitDir = GetAttackDirection(currentAttackDir);

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