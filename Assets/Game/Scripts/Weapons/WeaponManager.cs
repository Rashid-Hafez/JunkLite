using UnityEngine;
using System;
using System.Collections;
using System.Collections.Generic;

namespace junklite
{
    [RequireComponent(typeof(Collider))]
    public class WeaponManager : MonoBehaviour
    {
        #region Fields

        [Header("Fist Weapon")]
        [SerializeField] private WeaponData fistWeaponData;

        private CombatState fistCombat;

        [Header("Weapon Holder")]
        public Transform weaponHolder;

        [Header("Attack Transforms (Scene Anchors)")]
        [SerializeField] private Transform sideAttack;
        [SerializeField] private Transform upAttack;
        [SerializeField] private Transform downAttack;

        [Header("Ranged")]
        [SerializeField]
        [Tooltip("Spawn point for bullets. If unassigned, falls back to sideAttack.")]
        private Transform muzzlePoint;

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
        [SerializeField]
        [Tooltip("Time that the collision stays open to deliver the attack")]
        private float attackOpenWindow = 0.3f;
        [SerializeField] private float BUFFER_DURATION = 0.3f;

        [Header("Attack Settings")]
        [SerializeField] private float facingLockDuration = 0.25f;
        [SerializeField] private float inputThreshold = 0.5f;

        [Header("Attack Push")]
        [Tooltip("Fallback push duration when ComboStep.forwardImpulseDuration is 0")]
        [SerializeField] private float defaultPushDuration = 0.08f;

        [Header("Attack Input Lock")]
        [SerializeField] private bool lockMovementDuringAttack = true;

        [Header("Debug")]
        [SerializeField] private bool logAttacks = false;

        // Internal refs
        private Rigidbody playerRb;
        private Transform playerTransform;
        private PlayerState playerState;
        private PlayerCharacter playerCharacter;
        private Character2D5Controller controller;
        private SpineAnimationController spineController;

        // Weapon slots
        private WeaponInstance weaponSlot1;
        private WeaponInstance weaponSlot2;
        private WorldWeaponPickup storedPickup1;
        private WorldWeaponPickup storedPickup2;

        // Combat mode
        private bool isModCombat;

        // Attack state
        private bool isAttacking;
        private int activeWeaponSlot;           // 0=fists, 1=slot1, 2=slot2
        private WeaponInstance activeWeapon;     // null for fists
        private CombatState activeCombatState;   // always set during attack
        private WeaponData activeWeaponData;     // always set during attack
        private int lastAttackedSlot = -1;
        private AttackDirection currentAttackDir;
        private int currentComboIndex;
        private Transform currentAttackAnchor;
        private bool attackInputLockApplied;
        private bool currentAttackGrounded;

        // Input buffer
        private bool hasBufferedInput;
        private int bufferedWeaponSlot;
        private Vector2 bufferedInput;
        private bool bufferedGrounded;
        private float bufferTimer;

        // VFX pools
        private readonly Dictionary<GameObject, Queue<GameObject>> slashPools = new();

        #endregion

        #region Properties

        public bool IsModCombat => isModCombat;
        public bool IsAttacking => isAttacking;
        public AttackDirection CurrentAttackDirection => currentAttackDir;
        public float Facing => Mathf.Sign(playerTransform.localScale.x);

        public WeaponData FistWeaponData => fistWeaponData;
        public WeaponInstance WeaponSlot1 => weaponSlot1;
        public WeaponInstance WeaponSlot2 => weaponSlot2;
        public WeaponInstance ActiveWeapon => activeWeapon;

        public event Action OnWeaponChanged;
        public event Action OnCombatModeChanged;
        public event Action OnCombatModeRejected;
        public event Action<EnemyCharacter> OnEnemyHit;
        public event Action OnEnvironmentHit;

        #endregion

        #region Unity

        private void Awake()
        {
            playerRb = GetComponentInParent<Rigidbody>();
            playerTransform = transform.parent ?? transform;
            playerState = GetComponentInParent<PlayerState>();
            playerCharacter = GetComponentInParent<PlayerCharacter>();
            controller = GetComponentInParent<Character2D5Controller>();
            spineController = GetComponentInParent<SpineAnimationController>()
                           ?? GetComponentInChildren<SpineAnimationController>();

            if (impulseSource == null)
            {
                impulseSource = GetComponent<Unity.Cinemachine.CinemachineImpulseSource>()
                                ?? GetComponentInParent<Unity.Cinemachine.CinemachineImpulseSource>()
                                ?? GetComponentInChildren<Unity.Cinemachine.CinemachineImpulseSource>();
            }

            if (playerState != null)
            {
                playerState.OnAttackAnimationComplete += OnAttackAnimationComplete;
                playerState.OnAttackAnimationInterrupted += OnAttackInterrupted;
            }

            CreateFistCombatState();
        }

        private void OnDestroy()
        {
            if (playerState != null)
            {
                playerState.OnAttackAnimationComplete -= OnAttackAnimationComplete;
                playerState.OnAttackAnimationInterrupted -= OnAttackInterrupted;
            }

            ReleaseAttackInputLock();
        }

        private void Update()
        {
            fistCombat?.Tick(Time.deltaTime);

            if (!hasBufferedInput) return;

            bufferTimer -= Time.deltaTime;
            if (bufferTimer <= 0f)
            {
                hasBufferedInput = false;
                Log("Buffer expired");
                return;
            }

            var combat = GetCombatStateForSlot(bufferedWeaponSlot);
            if (!isAttacking && combat != null && combat.CanAttack)
            {
                hasBufferedInput = false;
                Log("Executing buffered attack");
                AttackDirection dir = ResolveAttackDirection(bufferedInput, bufferedGrounded);
                StartAttack(bufferedWeaponSlot, dir);
            }
        }

        private void OnTriggerEnter(Collider other)
        {
            var weaponPickup = other.GetComponent<WorldWeaponPickup>();
            if (weaponPickup != null)
            {
                TryPickupWeapon(weaponPickup);
                return;
            }

            var modPickup = other.GetComponent<WorldModPickup>();
            if (modPickup != null && modPickup.gameObject.activeSelf)
            {
                modPickup.gameObject.SetActive(false);

                var modInstance = new ModInstance(modPickup.modData);

                var modManager = GetComponent<ModManager>();
                if (modManager != null && modManager.TryEquipMod(modInstance))
                {
                    Destroy(modPickup.gameObject);
                    return;
                }

                var inventory = GetComponent<InventoryComponent>();
                if (inventory != null)
                {
                    inventory.AddMod(modInstance);
                    Destroy(modPickup.gameObject);
                    return;
                }

                modPickup.gameObject.SetActive(true);
            }
        }

        #endregion

        #region Combat Mode

        public bool TryToggleCombatMode()
        {
            if (isAttacking) return false;

            if (isModCombat)
            {
                ExitModCombat();
                return true;
            }

            if (weaponSlot1 == null && weaponSlot2 == null)
            {
                OnCombatModeRejected?.Invoke();
                return false;
            }

            EnterModCombat();
            return true;
        }

        private void EnterModCombat()
        {
            isModCombat = true;
            fistCombat?.ResetCombo();
            lastAttackedSlot = -1;
            hasBufferedInput = false;

            SetWeaponVisible(weaponSlot1, true);
            SetWeaponVisible(weaponSlot2, true);

            TryPrewarmRangedPool(weaponSlot1);
            TryPrewarmRangedPool(weaponSlot2);

            Log("Entered Mod Combat");
            OnCombatModeChanged?.Invoke();
        }

        private void ExitModCombat()
        {
            isModCombat = false;
            weaponSlot1?.ResetCombo();
            weaponSlot2?.ResetCombo();
            lastAttackedSlot = -1;
            hasBufferedInput = false;

            SetWeaponVisible(weaponSlot1, false);
            SetWeaponVisible(weaponSlot2, false);

            Log("Exited Mod Combat");
            OnCombatModeChanged?.Invoke();
        }

        private void TryPrewarmRangedPool(WeaponInstance weapon)
        {
            if (weapon == null || weapon.weaponData is not RangedWeaponData ranged) return;
            if (ranged.bulletPrefab == null) return;
            ProjectileManager.Instance?.PrewarmPool(ranged.bulletPrefab, 10);
        }

        #endregion

        #region Public API

        /// <summary>
        /// Backward-compatible attack for regular mode (fists).
        /// </summary>
        public void Attack(Vector2 moveInput, bool isGrounded)
        {
            Attack(0, moveInput, isGrounded);
        }

        /// <summary>
        /// Main attack entry point. weaponSlot: 0=fists, 1=slot1, 2=slot2.
        /// </summary>
        public void Attack(int weaponSlot, Vector2 moveInput, bool isGrounded)
        {
            var combat = GetCombatStateForSlot(weaponSlot);
            if (combat == null) return;

            if (weaponSlot != 0)
            {
                var weapon = GetWeaponForSlot(weaponSlot);
                if (weapon == null || weapon.IsBroken) return;
            }

            if (!isModCombat && weaponSlot != 0) return;
            if (isModCombat && weaponSlot == 0) return;

            if (isAttacking)
            {
                BufferAttack(weaponSlot, moveInput, isGrounded);
                return;
            }

            if (!combat.CanAttack)
            {
                BufferAttack(weaponSlot, moveInput, isGrounded);
                return;
            }

            AttackDirection dir = ResolveAttackDirection(moveInput, isGrounded);
            StartAttack(weaponSlot, dir);
        }

        public WeaponInstance GetWeaponForSlot(int slot)
        {
            return slot switch
            {
                1 => weaponSlot1,
                2 => weaponSlot2,
                _ => null
            };
        }

        private CombatState GetCombatStateForSlot(int slot)
        {
            return slot switch
            {
                0 => fistCombat,
                1 => weaponSlot1?.Combat,
                2 => weaponSlot2?.Combat,
                _ => null
            };
        }

        private WeaponData GetWeaponDataForSlot(int slot)
        {
            return slot switch
            {
                0 => fistWeaponData,
                1 => weaponSlot1?.weaponData,
                2 => weaponSlot2?.weaponData,
                _ => null
            };
        }

        public WeaponInstance GetWeaponInSlot(int slot)
        {
            return slot switch
            {
                1 => weaponSlot1,
                2 => weaponSlot2,
                _ => null
            };
        }

        public void DropWeapon(int slot)
        {
            var weapon = GetWeaponForSlot(slot);
            var pickup = slot == 1 ? storedPickup1 : storedPickup2;

            if (weapon == null || pickup == null) return;

            weapon.transform.SetParent(pickup.transform, false);
            weapon.gameObject.SetActive(false);

            pickup.transform.position = playerTransform.position + Vector3.right * Facing * 1.2f;
            pickup.gameObject.SetActive(true);

            RemoveWeaponFromSlot(slot);
        }

        public void SwapWeaponSlots()
        {
            if (isAttacking) return;

            var temp = weaponSlot1;
            weaponSlot1 = weaponSlot2;
            weaponSlot2 = temp;

            var tempPickup = storedPickup1;
            storedPickup1 = storedPickup2;
            storedPickup2 = tempPickup;

            weaponSlot1?.Combat?.ResetCombo();
            weaponSlot2?.Combat?.ResetCombo();
            lastAttackedSlot = -1;

            OnWeaponChanged?.Invoke();
            Log("Weapon slots swapped");
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

        public void SetWeaponVisible(bool visible)
        {
            SetWeaponVisible(weaponSlot1, visible);
            SetWeaponVisible(weaponSlot2, visible);
        }

        #endregion

        #region Direction Resolution

        private AttackDirection ResolveAttackDirection(Vector2 moveInput, bool isGrounded)
        {
            if (moveInput.y > inputThreshold)
                return AttackDirection.Up;

            if (moveInput.y < -inputThreshold && !isGrounded)
                return AttackDirection.Down;

            return AttackDirection.Side;
        }

        #endregion

        #region Attack Core

        private void BufferAttack(int weaponSlot, Vector2 moveInput, bool isGrounded)
        {
            hasBufferedInput = true;
            bufferedWeaponSlot = weaponSlot;
            bufferedInput = moveInput;
            bufferedGrounded = isGrounded;
            bufferTimer = BUFFER_DURATION;
            Log("Attack buffered");
        }

        private void StartAttack(int slot, AttackDirection dir)
        {
            var combat = GetCombatStateForSlot(slot);
            var data = GetWeaponDataForSlot(slot);
            if (combat == null || data == null) return;

            if (playerState != null && !playerState.IsGrounded && !playerState.CanAirAttack)
            {
                Log("Air attack blocked");
                return;
            }

            if (lastAttackedSlot >= 0 && lastAttackedSlot != slot)
            {
                GetCombatStateForSlot(lastAttackedSlot)?.ResetCombo();
                Log($"Weapon switch: reset combo on slot {lastAttackedSlot}");
            }

            bool isGrounded = playerState == null || playerState.IsGrounded;

            // CombatState resolves the combo index and animation name through the abstract
            // WeaponData interface — it never knows or cares about the concrete step type.
            if (!combat.TryBeginAttack(dir, isGrounded, data, out int comboIndex, out string animName))
            {
                Log($"No combo step for {dir}");
                return;
            }

            if (playerState != null)
            {
                playerState.SetDownAttackRequested(dir == AttackDirection.Down);
                if (!playerState.IsGrounded)
                    playerState.MarkAirAttackUsed();
            }

            isAttacking = true;
            activeWeaponSlot = slot;
            activeWeapon = GetWeaponForSlot(slot);
            activeCombatState = combat;
            activeWeaponData = data;
            lastAttackedSlot = slot;
            currentAttackDir = dir;
            currentComboIndex = comboIndex;
            currentAttackAnchor = GetAttackTransform(dir);
            currentAttackGrounded = isGrounded;

            Log($"Attack: slot {slot}, {dir}, combo {comboIndex}, anim '{animName}'");

            if (controller != null && facingLockDuration > 0f)
                controller.LockFacing(facingLockDuration);

            if (playerState != null)
                playerState.SetAttacking(true);

            ApplyAttackInputLock();

            if (playerState != null && !string.IsNullOrEmpty(animName))
                playerState.RequestAttackAnimation(animName);
            else
                OnAttackAnimationComplete();

            // Route to the correct coroutine based on weapon type.
            // Adding a future weapon type means adding a new branch here only.
            ExecuteAttack(dir, comboIndex, isGrounded);
        }

        /// <summary>
        /// Routes execution to the correct attack coroutine based on weapon type.
        /// WeaponManager fetches the typed step data here — after CombatState has resolved
        /// the index — so each coroutine only sees the fields relevant to its type.
        /// </summary>
        private void ExecuteAttack(AttackDirection dir, int comboIndex, bool isGrounded)
        {
            if (activeWeaponData is MeleeWeaponData meleeData)
            {
                if (meleeData.TryGetMeleeStep(dir, comboIndex, isGrounded, out var step))
                    StartCoroutine(CoMeleeAttack(dir, step));
                else
                    Log($"No melee step for {dir} combo {comboIndex}");
            }
            else if (activeWeaponData is RangedWeaponData rangedData)
            {
                if (rangedData.TryGetRangedStep(dir, comboIndex, isGrounded, out var step))
                    StartCoroutine(CoRangedAttack(dir, step, rangedData, activeWeapon, activeWeaponSlot));

                else
                    Log($"No ranged step for {dir} combo {comboIndex}");
            }
            else
            {
                Log($"Unknown weapon data type: {activeWeaponData?.GetType().Name}");
            }
        }

        private IEnumerator CoMeleeAttack(AttackDirection dir, MeleeWeaponData.MeleeComboStep step)
        {
            yield return new WaitForSeconds(delayBeforeAttack);

            // Push and hit detection run concurrently. The hit origin is snapshotted
            // before any push movement so lunging forward never awards a hit on an enemy
            // that was out of range when the swing started.
            StartCoroutine(CoApplyAttackPush(dir, step.forwardImpulse, step.verticalImpulse, step.forwardImpulseDuration));
            ApplyAttackGravityOverride(step.airGravityMultiplier);

            Transform anchor = GetAttackTransform(dir);
            Vector3 hitOrigin = ResolveHitOrigin(dir, anchor);

            bool isPiercing = step.piercing || (activeWeapon?.PiercingOverride ?? false);
            float radius = step.hitRadius > 0f ? step.hitRadius : GetFallbackRadius(dir);

            bool hasHitEnemy = false;
            bool hasHitEnvironment = false;
            float windowEnd = Time.time + attackOpenWindow;

            while (Time.time < windowEnd)
            {
                var hitResult = DetectHit(hitOrigin, radius, isPiercing);

                if (hitResult.type == AttackHitResult.Enemy && !hasHitEnemy)
                {
                    hasHitEnemy = true;
                    PlayHitFeedback();

                    if (isPiercing && hitResult.allTargets != null)
                        DealDamageToAll(hitResult.allTargets, step.damageMultiplier);
                    else if (hitResult.target != null)
                        DealDamage(hitResult.target, step.damageMultiplier);

                    ApplyRecoil(dir, step.hitRecoil);
                    break;
                }

                if (hitResult.type == AttackHitResult.Environment && !hasHitEnvironment)
                {
                    hasHitEnvironment = true;
                    float radiusForVfx = step.hitRadius > 0f ? step.hitRadius : GetFallbackRadius(dir);
                    Vector3 impactPoint = ResolveImpactPoint(dir, hitOrigin, radiusForVfx);
                    Vector3 attackDir = GetAttackDirection(dir);
                    if (CombatEffectsManager.Instance != null)
                    {
                        CombatEffectsManager.Instance.SpawnEnvHitParticle(impactPoint, attackDir);
                        CombatEffectsManager.Instance.SpawnHitCross(impactPoint);
                        OnEnvironmentHit?.Invoke();
                    }
                    ApplyRecoil(dir, step.hitRecoil);
                }

                yield return null;
            }
        }

        // =====================================================================
        // RANGED COROUTINE
        // =====================================================================

        /// <summary>
        /// Fires a burst of bullets synced to the animation's normalised time.
        /// Delegates to CoWaitForFirePose which polls SpineAnimationController.CurrentAttackEntry
        /// each frame until TrackTime / AnimationEnd >= step.fireAtNormalizedTime.
        /// Set fireAtNormalizedTime on the RangedComboStep SO by watching the animation
        /// and estimating what fraction of the clip has played at the shoot pose.
        /// Falls back to delayBeforeAttack if no SpineAnimationController is found.
        ///
        /// weapon and weaponSlot are captured explicitly — the coroutine outlives the attack
        /// state and OnAttackAnimationComplete may clear activeWeapon before the burst ends.
        /// Durability is consumed per bullet fired; a broken gun stops the burst immediately.
        /// </summary>
        private IEnumerator CoRangedAttack(
            AttackDirection dir,
            RangedWeaponData.RangedComboStep step,
            RangedWeaponData rangedData,
            WeaponInstance capturedWeapon,
            int capturedWeaponSlot)
        {
            // Wait until the animation reaches the shoot pose, then fire.
            yield return CoWaitForFirePose(step.fireAtNormalizedTime);

            // Muzzle kick — negative forwardImpulse pushes the player back for recoil feel
            StartCoroutine(CoApplyAttackPush(dir, step.forwardImpulse, 0f, step.forwardImpulseDuration));

            if (ProjectileManager.Instance == null)
            {
                Debug.LogWarning("[WeaponManager] No ProjectileManager in scene.");
                yield break;
            }

            if (rangedData.bulletPrefab == null)
            {
                Debug.LogWarning($"[WeaponManager] RangedWeaponData '{rangedData.displayName}' has no bullet prefab.");
                yield break;
            }

            // Muzzle position: dedicated point if assigned, falls back to sideAttack.
            // Reassign muzzlePoint in the inspector for new gun models — no code change needed.
            Transform muzzle = (muzzlePoint != null) ? muzzlePoint : sideAttack;

            // Capture damage values now — attack state may clear before bullets hit
            float damage = rangedData.baseDamage * (step.damageMultiplier > 0f ? step.damageMultiplier : 1f);
            Vector2 knockback = rangedData.knockbackForce;
            Vector3 fireDirection = new Vector3(Facing, 0f, 0f);
            int bulletCount = Mathf.Max(1, step.bulletCount);

            for (int i = 0; i < bulletCount; i++)
            {
                // Consume durability per bullet fired before spawning.
                // A broken gun stops the burst immediately rather than firing extra shots.
                if (capturedWeapon != null)
                {
                    if (capturedWeapon.ConsumeDurability())
                    {
                        HandleWeaponBroken(capturedWeaponSlot);
                        yield break;
                    }
                }

                var config = new BulletConfig
                {
                    spawnPosition = muzzle.position,
                    direction = fireDirection,
                    speed = step.bulletSpeed,
                    radius = step.bulletRadius,
                    damage = damage,
                    knockback = knockback,
                    owner = playerTransform.gameObject
                };

                ProjectileManager.Instance.FireBullet(
                    rangedData.bulletPrefab,
                    config,
                    enemyLayer,
                    environmentLayer,
                    hitCollider => OnBulletHitEnemy(hitCollider, damage, knockback)
                );

                Log($"Bullet {i + 1}/{bulletCount} fired");

                if (i < bulletCount - 1 && step.fireInterval > 0f)
                    yield return new WaitForSeconds(step.fireInterval);
            }
            ApplyRecoil(dir, step.hitRecoil);
        }

        /// <summary>
        /// Yields until the current attack animation reaches the given normalised time (0–1).
        /// Polls SpineAnimationController.CurrentAttackEntry each frame and returns as soon as
        /// TrackTime / AnimationEnd >= normalizedTime.
        /// Falls back to delayBeforeAttack if no SpineAnimationController or TrackEntry is found,
        /// so nothing breaks before fireAtNormalizedTime is dialled in on the SO.
        /// </summary>
        private IEnumerator CoWaitForFirePose(float normalizedTime)
        {
            if (spineController != null)
            {
                // Wait one frame before polling — gives Spine time to start the animation
                // when ExecuteAttack and PlayAttackAnimation run on the same frame.
                yield return null;

                float elapsed = 0f;
                float timeout = Mathf.Max(delayBeforeAttack * 4f, 0.5f);

                while (elapsed < timeout)
                {
                    var entry = spineController.CurrentAttackEntry;
                    if (entry != null && entry.AnimationEnd > 0f)
                    {
                        float t = entry.TrackTime / entry.AnimationEnd;
                        if (t >= normalizedTime)
                            yield break;
                    }
                    elapsed += Time.deltaTime;
                    yield return null;
                }

                Log($"CoWaitForFirePose timed out at normalizedTime={normalizedTime:F2} — fired on fallback.");
            }
            else
            {
                // No SpineAnimationController found — use the fixed inspector delay
                yield return new WaitForSeconds(delayBeforeAttack);
            }
        }

        /// <summary>
        /// Called by each bullet via closure when it detects an enemy collider.
        /// All state is captured at fire time — WeaponManager's attack state may have
        /// cleared by the time a bullet connects.
        /// </summary>
        private void OnBulletHitEnemy(Collider hitCollider, float damage, Vector2 knockback)
        {
            var damageable = hitCollider.GetComponent<IDamageable>()
                          ?? hitCollider.GetComponentInParent<IDamageable>();

            if (damageable == null || !damageable.IsAlive) return;

            var damageInfo = new DamageInfo(damage, playerTransform.gameObject, DamageType.Physical, knockback);
            bool damageDealt = damageable.TakeDamage(damageInfo);

            if (damageDealt)
            {
                PlayHitFeedback();
                var enemy = hitCollider.GetComponent<EnemyCharacter>()
                         ?? hitCollider.GetComponentInParent<EnemyCharacter>();
                OnEnemyHit?.Invoke(enemy);
            }
        }

        public void OnAttackAnimationComplete()
        {
            Log($"Attack complete - slot {activeWeaponSlot}, {currentAttackDir}, combo {currentComboIndex}");

            // Pass activeWeaponData before clearing so CombatState can advance the combo index
            activeCombatState?.OnAttackComplete(currentAttackDir, currentAttackGrounded, activeWeaponData);

            isAttacking = false;
            activeWeapon = null;
            activeCombatState = null;
            activeWeaponData = null;
            hasBufferedInput = false;

            if (playerState != null)
            {
                playerState.SetDownAttackRequested(false);
                playerState.SetAttacking(false);
            }

            ClearAttackGravityOverride();
            ReleaseAttackInputLock();
        }

        public void OnAttackInterrupted()
        {
            Log("Attack interrupted");

            activeCombatState?.OnAttackInterrupted();

            isAttacking = false;
            activeWeapon = null;
            activeCombatState = null;
            activeWeaponData = null;
            hasBufferedInput = false;

            if (playerState != null)
            {
                playerState.SetDownAttackRequested(false);
                playerState.SetAttacking(false);
            }

            ClearAttackGravityOverride();
            ReleaseAttackInputLock();
        }

        /// <summary>
        /// Applies a clean velocity lunge for a fixed duration then hard-stops it.
        /// Directly sets velocity so the lunge is always the same speed regardless of
        /// prior momentum. Duration is per ComboStep so each hit can have distinct character.
        /// </summary>
        private IEnumerator CoApplyAttackPush(AttackDirection dir, float forwardImpulse, float verticalImpulse, float impulseDuration)
        {
            if (playerRb == null) yield break;

            Vector3 pushVelocity = Vector3.zero;

            if (dir == AttackDirection.Side && Mathf.Abs(forwardImpulse) > 0f)
                pushVelocity.x = forwardImpulse * Facing;
            else if (dir != AttackDirection.Side && Mathf.Abs(verticalImpulse) > 0f)
                pushVelocity.y = dir == AttackDirection.Down ? -verticalImpulse : verticalImpulse;

            if (pushVelocity.sqrMagnitude <= 0f) yield break;

            float duration = impulseDuration > 0f ? impulseDuration : defaultPushDuration;

            var vel = playerRb.linearVelocity;
            if (pushVelocity.x != 0f) vel.x = pushVelocity.x;
            if (pushVelocity.y != 0f) vel.y = pushVelocity.y;
            playerRb.linearVelocity = vel;

            yield return new WaitForSeconds(duration);

            var endVel = playerRb.linearVelocity;
            if (pushVelocity.x != 0f) endVel.x = 0f;
            if (pushVelocity.y != 0f) endVel.y = 0f;
            playerRb.linearVelocity = endVel;
        }

        private void ApplyAttackGravityOverride(float airGravityMultiplier)
        {
            if (controller == null) return;
            if (playerState != null && !playerState.IsGrounded && airGravityMultiplier > 0f)
                controller.SetGravityMultiplierOverride(airGravityMultiplier);
        }

        private void ClearAttackGravityOverride()
        {
            controller?.ClearGravityMultiplierOverride();
        }

        #endregion

        #region Hit Detection

        private struct HitDetectionResult
        {
            public AttackHitResult type;
            public Collider target;           // single-target path
            public Collider[] allTargets;     // piercing path
            public Vector3 point;
        }

        /// <summary>
        /// Performs an OverlapSphere at the snapshotted origin.
        /// Single-target: returns closest enemy. Piercing: returns all enemies.
        /// </summary>
        private HitDetectionResult DetectHit(Vector3 origin, float radius, bool piercing)
        {
            var result = new HitDetectionResult { type = AttackHitResult.None };

            Collider[] hits = Physics.OverlapSphere(origin, radius, enemyLayer | environmentLayer, QueryTriggerInteraction.Ignore);

            Collider closestEnemy = null;
            float closestDist = float.MaxValue;
            bool hitEnvironment = false;

            List<Collider> enemyHits = piercing ? new List<Collider>() : null;

            for (int i = 0; i < hits.Length; i++)
            {
                int mask = 1 << hits[i].gameObject.layer;

                if ((mask & enemyLayer) != 0)
                {
                    if (piercing)
                    {
                        enemyHits.Add(hits[i]);
                    }
                    else
                    {
                        float dist = Vector3.Distance(origin, hits[i].transform.position);
                        if (dist < closestDist)
                        {
                            closestDist = dist;
                            closestEnemy = hits[i];
                        }
                    }
                }
                else if ((mask & environmentLayer) != 0)
                {
                    hitEnvironment = true;
                }
            }

            if (piercing && enemyHits != null && enemyHits.Count > 0)
            {
                result.type = AttackHitResult.Enemy;
                result.allTargets = enemyHits.ToArray();
                result.point = enemyHits[0].ClosestPoint(origin);
            }
            else if (!piercing && closestEnemy != null)
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

        #endregion

        #region Damage

        private void DealDamage(Collider target, float damageMultiplier)
        {
            var damageable = target.GetComponent<IDamageable>()
                          ?? target.GetComponentInParent<IDamageable>();

            if (damageable == null || !damageable.IsAlive) return;

            float damage = activeWeaponData != null ? activeWeaponData.baseDamage : 10f;
            if (damageMultiplier > 0f) damage *= damageMultiplier;

            var knockback = activeWeaponData != null ? activeWeaponData.knockbackForce : new Vector2(8f, 4f);
            var damageInfo = new DamageInfo(damage, playerTransform.gameObject, DamageType.Physical, knockback);
            bool damageDealt = damageable.TakeDamage(damageInfo);

            if (damageDealt)
            {
                var enemy = target.GetComponent<EnemyCharacter>() ?? target.GetComponentInParent<EnemyCharacter>();
                OnEnemyHit?.Invoke(enemy);

                if (activeWeaponSlot != 0 && activeWeapon != null)
                    if (activeWeapon.ConsumeDurability())
                        HandleWeaponBroken(activeWeaponSlot);

                SpawnEnemyHitVFX(target);
            }
        }

        /// <summary>
        /// Damages all enemies in the hit result (piercing path).
        /// Durability is consumed once for the whole swing, not per enemy.
        /// </summary>
        private void DealDamageToAll(Collider[] targets, float damageMultiplier)
        {
            bool anyHit = false;

            foreach (var target in targets)
            {
                var damageable = target.GetComponent<IDamageable>()
                              ?? target.GetComponentInParent<IDamageable>();

                if (damageable == null || !damageable.IsAlive) continue;

                float damage = activeWeaponData != null ? activeWeaponData.baseDamage : 10f;
                if (damageMultiplier > 0f) damage *= damageMultiplier;

                var knockback = activeWeaponData != null ? activeWeaponData.knockbackForce : new Vector2(8f, 4f);
                var damageInfo = new DamageInfo(damage, playerTransform.gameObject, DamageType.Physical, knockback);
                bool damageDealt = damageable.TakeDamage(damageInfo);

                if (damageDealt)
                {
                    anyHit = true;
                    var enemy = target.GetComponent<EnemyCharacter>() ?? target.GetComponentInParent<EnemyCharacter>();
                    OnEnemyHit?.Invoke(enemy);
                    SpawnEnemyHitVFX(target);
                }
            }

            // Single durability tick for the whole swing regardless of enemy count
            if (anyHit && activeWeaponSlot != 0 && activeWeapon != null)
                if (activeWeapon.ConsumeDurability())
                    HandleWeaponBroken(activeWeaponSlot);
        }

        private void PlayHitFeedback()
        {
            if (FeedbackManager.Instance != null)
                FeedbackManager.Instance.DoHitFeedback(impulseSource, enemyHitHitstopDuration, enemyHitShakeForce);
        }

        private void SpawnEnemyHitVFX(Collider target)
        {
            if (CombatEffectsManager.Instance == null) return;

            Vector3 originPoint = currentAttackAnchor != null
                ? currentAttackAnchor.position
                : playerTransform.position + Vector3.up;

            Vector3 hitPoint = target.ClosestPoint(originPoint);
            Vector3 hitDir = (hitPoint - originPoint).normalized;

            CombatEffectsManager.Instance.SpawnEnemyHitVFX(hitPoint, hitDir);
            CombatEffectsManager.Instance.SpawnEnemyHurtParticle(hitPoint, hitDir);
        }

        #endregion

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

        #endregion

        #region Pickups

        private void TryPickupWeapon(WorldWeaponPickup pickup)
        {
            int slot = GetFirstEmptyWeaponSlot();
            if (slot < 0) return;
            SetupWeaponInSlot(slot, pickup);
        }

        private int GetFirstEmptyWeaponSlot()
        {
            if (weaponSlot1 == null) return 1;
            if (weaponSlot2 == null) return 2;
            return -1;
        }

        private void SetupWeaponInSlot(int slot, WorldWeaponPickup pickup)
        {
            var weapon = pickup.weaponInstance;
            pickup.gameObject.SetActive(false);

            weapon.gameObject.SetActive(true);
            weapon.transform.parent = weaponHolder;
            weapon.GetComponent<SpriteRenderer>().sortingOrder = 11;
            weapon.SetOwnerRigidbody(playerRb);

            if (weapon.weaponData != null)
            {
                var socketOffset = weapon.weaponData.socketOffset;
                weapon.transform.localPosition = socketOffset.localPositionOffset;
                weapon.transform.localRotation = Quaternion.Euler(0, 0, -30f) * Quaternion.Euler(socketOffset.localRotationOffsetEuler);
                Vector3 weaponScale = Vector3.one;
                if (socketOffset.flipLocalScaleX) weaponScale.x = -1f;
                if (socketOffset.flipLocalScaleY) weaponScale.y = -1f;
                weapon.transform.localScale = weaponScale;
            }
            else
            {
                weapon.transform.localPosition = Vector3.zero;
                weapon.transform.localRotation = Quaternion.Euler(0, 0, -30f);
                weapon.transform.localScale = Vector3.one;
            }

            if (slot == 1) { weaponSlot1 = weapon; storedPickup1 = pickup; }
            else { weaponSlot2 = weapon; storedPickup2 = pickup; }

            SetWeaponVisible(weapon, isModCombat);
            OnWeaponChanged?.Invoke();
        }

        private void RemoveWeaponFromSlot(int slot)
        {
            if (slot == 1) { weaponSlot1 = null; storedPickup1 = null; }
            else if (slot == 2) { weaponSlot2 = null; storedPickup2 = null; }

            if (lastAttackedSlot == slot)
                lastAttackedSlot = -1;

            OnWeaponChanged?.Invoke();
        }

        private void HandleWeaponBroken(int slot)
        {
            var weapon = GetWeaponForSlot(slot);
            if (weapon != null)
                weapon.gameObject.SetActive(false);

            RemoveWeaponFromSlot(slot);
            Log($"Weapon in slot {slot} broke");

            if (isModCombat && weaponSlot1 == null && weaponSlot2 == null)
                ExitModCombat();
        }

        #endregion

        #region Helpers

        /// <summary>
        /// Resolves the hit sphere world position.
        /// The anchor contributes only the axis it owns (height for side, horizontal for up/down).
        /// attackRange on WeaponData is the sole driver of reach from the player center.
        /// </summary>
        private Vector3 ResolveHitOrigin(AttackDirection dir, Transform anchor)
        {
            float range = (activeWeaponData as MeleeWeaponData)?.attackRange ?? 1f;

            switch (dir)
            {
                case AttackDirection.Side:
                    return new Vector3(
                        playerTransform.position.x + Facing * range,
                        anchor.position.y,
                        playerTransform.position.z
                    );

                case AttackDirection.Up:
                    return new Vector3(
                        anchor.position.x,
                        playerTransform.position.y + range,
                        playerTransform.position.z
                    );

                case AttackDirection.Down:
                    return new Vector3(
                        anchor.position.x,
                        playerTransform.position.y - range,
                        playerTransform.position.z
                    );

                default:
                    return playerTransform.position;
            }
        }

        private void SetWeaponVisible(WeaponInstance weapon, bool visible)
        {
            if (weapon == null) return;
            var renderers = weapon.GetComponentsInChildren<SpriteRenderer>(true);
            foreach (var sr in renderers)
                if (sr != null) sr.enabled = visible;
        }

        private void CreateFistCombatState()
        {
            if (fistWeaponData == null)
            {
                Debug.LogWarning("[WeaponManager] No fist WeaponData assigned!");
                return;
            }
            fistCombat = new CombatState(fistWeaponData);
        }

        private float GetFallbackRadius(AttackDirection dir)
        {
            return dir switch
            {
                AttackDirection.Up => upRadius,
                AttackDirection.Down => downRadius,
                _ => sideRadius
            };
        }

        private void ApplyRecoil(AttackDirection dir, float hitRecoil)
        {
            if (playerRb == null || dir != AttackDirection.Side) return;
            if (Mathf.Abs(hitRecoil) <= 0f) return;
            playerRb.AddForce(Vector3.right * -Facing * hitRecoil, ForceMode.Impulse);
        }

        private void ApplyAttackInputLock()
        {
            if (!lockMovementDuringAttack || playerState == null || attackInputLockApplied) return;

            if (!playerState.IsInputLocked)
            {
                playerState.SetInputLocked(true);
                controller?.StopAllVelocity();
                attackInputLockApplied = true;
            }
        }

        private void ReleaseAttackInputLock()
        {
            if (!lockMovementDuringAttack || playerState == null || !attackInputLockApplied) return;
            playerState.SetInputLocked(false);
            attackInputLockApplied = false;
        }

        private void Log(string message)
        {
            if (logAttacks)
                Debug.Log($"[WeaponManager] {message}", this);
        }

        #endregion

        #region Debug

        private void OnDrawGizmosSelected()
        {
            if (sideAttack == null || upAttack == null || downAttack == null) return;

            Gizmos.color = Color.red;
            Gizmos.DrawWireSphere(sideAttack.position, sideRadius);

            Gizmos.color = Color.green;
            Gizmos.DrawWireSphere(upAttack.position, upRadius);

            Gizmos.color = Color.cyan;
            Gizmos.DrawWireSphere(downAttack.position, downRadius);

            if (muzzlePoint != null)
            {
                Gizmos.color = Color.yellow;
                Gizmos.DrawWireSphere(muzzlePoint.position, 0.08f);
            }
        }

        #endregion
    }

    public enum AttackDirection
    {
        Side,
        Up,
        Down
    }
}