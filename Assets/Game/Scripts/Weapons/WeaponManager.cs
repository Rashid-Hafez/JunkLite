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
        [Tooltip("Spawn point for hitscan rays and tracer origin. If unassigned, falls back to sideAttack.")]
        private Transform muzzlePoint;

        [Header("Hit Leniency")]
        [Tooltip("Added on top of each step's hitRadius for all attacks. Tune this for forgiveness.")]
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
        [Tooltip("How long after impulse fires before the attack animation plays.")]
        [SerializeField] private float animationLeadTime = 0.05f;
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
        private int activeWeaponSlot;
        private WeaponInstance activeWeapon;
        private CombatState activeCombatState;
        private WeaponData activeWeaponData;
        private int lastAttackedSlot = -1;
        private AttackDirection currentAttackDir;
        private int currentComboIndex;
        private Transform currentAttackAnchor;
        private bool attackInputLockApplied;
        private bool currentAttackGrounded;

        // Ranged state
        private bool isRangedHovering;
        private Coroutine activeTimeScaleRestore;

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

        /// <summary>
        /// The horizontal movement axis for the current 2.5D lane.
        /// XY lane (Y rot = 0°)  → Vector3.right   (1, 0, 0)
        /// ZY lane (Y rot = 90°) → controller.transform.right = (0, 0, -1)
        /// Matches EnemyMovement.horizontalAxis so attacks fire along the correct world axis.
        /// </summary>
        private Vector3 FacingAxis => controller != null ? controller.transform.right : Vector3.right;

        public WeaponData FistWeaponData => fistWeaponData;
        public WeaponInstance WeaponSlot1 => weaponSlot1;
        public WeaponInstance WeaponSlot2 => weaponSlot2;
        public WeaponInstance ActiveWeapon => activeWeapon;

        public event Action OnWeaponChanged;
        public event Action OnCombatModeChanged;
        public event Action OnCombatModeRejected;
        public event Action<EnemyCharacter, float> OnEnemyHit;
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

            // Only count down the buffer after the current attack ends.
            // While attacking, the buffer is "held" indefinitely so it can't
            // expire before the animation finishes.
            if (!isAttacking)
            {
                bufferTimer -= Time.deltaTime;
                if (bufferTimer <= 0f)
                {
                    hasBufferedInput = false;
                    Log("Buffer expired");
                    return;
                }
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
            if (spineController != null && spineController.IsForceOverrideActive) return false;

            if (isModCombat)
            {
                ExitModCombat();
                return true;
            }

            if (playerState != null && playerState.IsInputLocked) return false;

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

            OnCombatModeChanged?.Invoke();
        }

        private void TryPrewarmRangedPool(WeaponInstance weapon)
        {
            if (weapon == null || weapon.weaponData is not RangedWeaponData ranged) return;
            if (ranged.tracerPrefab == null) return;
            ProjectileManager.Instance?.PrewarmPool(ranged.tracerPrefab, 10);
        }

        #endregion

        #region Public API

        public void Attack(Vector2 moveInput, bool isGrounded)
        {
            Attack(0, moveInput, isGrounded);
        }

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

            pickup.transform.position = playerTransform.position + FacingAxis * Facing * 1.2f;
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

            /* if (playerState != null && !playerState.IsGrounded && !playerState.CanAirAttack)
             {
                 Log("Air attack blocked");
                 return;
             }*/ // Why are we blocking air attack??

            if (lastAttackedSlot >= 0 && lastAttackedSlot != slot)
            {
                GetCombatStateForSlot(lastAttackedSlot)?.ResetCombo();
                Log($"Weapon switch: reset combo on slot {lastAttackedSlot}");
            }

            bool isGrounded = playerState == null || playerState.IsGrounded;

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

            if (activeWeaponData is not MeleeWeaponData)
            {
                if (playerState != null && !string.IsNullOrEmpty(animName))
                    playerState.RequestAttackAnimation(animName);
                else
                    OnAttackAnimationComplete();
            }

            ExecuteAttack(dir, comboIndex, isGrounded, animName);
        }

        private void ExecuteAttack(AttackDirection dir, int comboIndex, bool isGrounded, string animName)
        {
            if (activeWeaponData is MeleeWeaponData meleeData)
            {
                if (meleeData.TryGetMeleeStep(dir, comboIndex, isGrounded, out var step))
                    StartCoroutine(CoMeleeAttack(dir, step, animName));
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

        #endregion

        #region Melee Attack

        private IEnumerator CoMeleeAttack(AttackDirection dir, MeleeWeaponData.MeleeComboStep step, string animName)
        {
            StartCoroutine(CoApplyAttackPush(dir, step.forwardImpulse, step.verticalImpulse, step.forwardImpulseDuration, step.lungeCurve));
            ApplyAttackGravityOverride(dir, step.airGravityMultiplier);

            if (animationLeadTime > 0f)
                yield return new WaitForSeconds(animationLeadTime);

            if (playerState != null && !string.IsNullOrEmpty(animName))
                playerState.RequestAttackAnimation(animName);
            else
                OnAttackAnimationComplete();

            float hitDelay = step.hitDelay > 0f ? step.hitDelay : Mathf.Max(0f, delayBeforeAttack - animationLeadTime);
            if (hitDelay > 0f)
                yield return new WaitForSeconds(hitDelay);

            Transform anchor = GetAttackTransform(dir);
            bool isPiercing = step.piercing || (activeWeapon?.PiercingOverride ?? false);
            float radius = step.hitRadius + GetFallbackRadius(dir);
            Vector2 knockback = step.overrideKnockback ? step.knockback : activeWeaponData.knockbackForce;

            bool hasHitEnemy = false;
            bool hasHitEnvironment = false;
            float windowEnd = Time.time + attackOpenWindow;

            // Snapshot hit origin once so the hitbox represents where the swing landed,
            // not where the player drifts to over the window duration.
            Vector3 hitOrigin = ResolveHitOrigin(dir, anchor);

            while (Time.time < windowEnd)
            {
                var hitResult = DetectHit(hitOrigin, radius, isPiercing);

                if (hitResult.type == AttackHitResult.Enemy && !hasHitEnemy)
                {
                    hasHitEnemy = true;
                    PlayHitFeedback();
                    StartCoroutine(CoHitstop(enemyHitHitstopDuration));

                    if (isPiercing && hitResult.allTargets != null)
                        DealDamageToAll(hitResult.allTargets, step.damageMultiplier, knockback);
                    else if (hitResult.target != null)
                        DealDamage(hitResult.target, step.damageMultiplier, knockback);

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

        private IEnumerator CoHitstop(float duration)
        {
            if (playerRb == null || duration <= 0f) yield break;
            playerRb.linearVelocity = Vector3.zero;
            yield return new WaitForSecondsRealtime(duration);
        }

        #endregion

        #region Ranged Attack

        private IEnumerator CoRangedAttack(
            AttackDirection dir,
            RangedWeaponData.RangedComboStep step,
            RangedWeaponData rangedData,
            WeaponInstance capturedWeapon,
            int capturedWeaponSlot)
        {
            // HOVER
            bool useHover = step.hoverGravityMultiplier >= 0f;
            if (useHover)
            {
                isRangedHovering = true;
                controller?.SetGravityMultiplierOverride(step.hoverGravityMultiplier);
                if (playerRb != null)
                    playerRb.linearVelocity = new Vector3(playerRb.linearVelocity.x, 0f, playerRb.linearVelocity.z);
            }

            yield return CoWaitForFirePose(step.fireAtNormalizedTime);

            if (Mathf.Abs(step.forwardImpulse) > 0f)
                StartCoroutine(CoApplyAttackPush(dir, step.forwardImpulse, 0f, step.forwardImpulseDuration));

            // BULLET TIME
            bool useBulletTime = step.bulletTimeScale > 0f && step.bulletTimeScale < 1f;
            const float savedTimeScale = 1f;
            const float savedFixedDelta = 0.02f;

            if (useBulletTime)
            {
                if (activeTimeScaleRestore != null)
                    StopCoroutine(activeTimeScaleRestore);
                Time.timeScale = step.bulletTimeScale;
                Time.fixedDeltaTime = savedFixedDelta * step.bulletTimeScale;
            }

            bool isDirectional = (dir == AttackDirection.Down || dir == AttackDirection.Up);

            if (isDirectional)
            {
                yield return CoDirectionalBlast(dir, step, rangedData, capturedWeapon, capturedWeaponSlot);
            }
            else
            {
                yield return CoSideHitscan(step, rangedData, capturedWeapon, capturedWeaponSlot);
            }

            // HOLD BULLET TIME
            if (useBulletTime && step.bulletTimeDuration > 0f)
                yield return new WaitForSecondsRealtime(step.bulletTimeDuration);

            // SMOOTH RECOIL
            if (Mathf.Abs(step.hitRecoil) > 0f)
                StartCoroutine(CoSmoothRangedRecoil(dir, step.hitRecoil, step.recoilDuration));

            // RESTORE TIMESCALE
            if (useBulletTime)
            {
                float restoreDur = step.bulletTimeRestoreDuration > 0f ? step.bulletTimeRestoreDuration : 0.1f;
                activeTimeScaleRestore = StartCoroutine(CoRestoreTimeScale(savedTimeScale, savedFixedDelta, restoreDur));
            }

            CleanupRangedHover(useHover);
        }

        /// <summary>
        /// Directional blast (down/up). The blast sphere is placed FORWARD in the
        /// facing direction (offset by blastForwardOffset) so it covers the area
        /// in front of the player. Damages ALL enemies in the sphere, consumes
        /// scaled durability, and casts multiple environment rays for ground + wall VFX.
        /// </summary>
        private IEnumerator CoDirectionalBlast(
            AttackDirection dir,
            RangedWeaponData.RangedComboStep step,
            RangedWeaponData rangedData,
            WeaponInstance capturedWeapon,
            int capturedWeaponSlot)
        {
            // DURABILITY — scaled by durabilityMultiplier
            int durabilityCost = Mathf.Max(1, Mathf.RoundToInt(step.durabilityMultiplier > 0f ? step.durabilityMultiplier : 1f));
            if (capturedWeapon != null)
            {
                for (int d = 0; d < durabilityCost; d++)
                {
                    if (capturedWeapon.ConsumeDurability())
                    {
                        HandleWeaponBroken(capturedWeaponSlot);
                        yield break;
                    }
                }
            }

            float damage = rangedData.baseDamage * (step.damageMultiplier > 0f ? step.damageMultiplier : 1f);
            Vector2 knockback = rangedData.knockbackForce;
            float blastRadius = step.blastDamageRadius > 0f ? step.blastDamageRadius : 1.5f;
            Vector3 blastOrigin = ResolveBlastOrigin(dir, blastRadius, step.blastForwardOffset);

            // ENEMY DAMAGE — OverlapSphere hits ALL enemies in the blast zone
            Collider[] enemyHits = Physics.OverlapSphere(blastOrigin, blastRadius, enemyLayer, QueryTriggerInteraction.Ignore);
            bool hitAnyEnemy = false;

            if (enemyHits.Length > 0)
            {
                PlayHitFeedback();
                StartCoroutine(CoHitstop(enemyHitHitstopDuration));

                foreach (var hit in enemyHits)
                {
                    var damageable = hit.GetComponent<IDamageable>()
                                  ?? hit.GetComponentInParent<IDamageable>();
                    if (damageable == null || !damageable.IsAlive) continue;

                    var damageInfo = new DamageInfo(damage, playerTransform.gameObject, DamageType.Physical, knockback);
                    bool dealt = damageable.TakeDamage(damageInfo);

                    if (dealt)
                    {
                        hitAnyEnemy = true;
                        var enemy = hit.GetComponent<EnemyCharacter>() ?? hit.GetComponentInParent<EnemyCharacter>();
                        OnEnemyHit?.Invoke(enemy, damage);

                        if (CombatEffectsManager.Instance != null)
                        {
                            Vector3 hitPoint = hit.ClosestPoint(blastOrigin);
                            Vector3 hitDir = (hitPoint - blastOrigin).normalized;
                            CombatEffectsManager.Instance.SpawnEnemyHitVFX(hitPoint, hitDir);
                            CombatEffectsManager.Instance.SpawnEnemyHurtParticle(hitPoint, hitDir);
                        }
                    }
                }
            }

            // ENVIRONMENT HIT — multi-ray for ground + wall VFX
            if (CombatEffectsManager.Instance != null)
            {
                float envRayLength = blastRadius * 2f;
                bool hitAnyEnv = false;

                // Ground
                if (Physics.Raycast(blastOrigin, Vector3.down, out RaycastHit groundHit, envRayLength, environmentLayer, QueryTriggerInteraction.Ignore))
                {
                    CombatEffectsManager.Instance.SpawnEnvHitParticle(groundHit.point, groundHit.normal);
                    CombatEffectsManager.Instance.SpawnHitCross(groundHit.point);
                    hitAnyEnv = true;
                }

                // Wall (facing direction)
                Vector3 forwardDir = FacingAxis * Facing;
                if (Physics.Raycast(blastOrigin, forwardDir, out RaycastHit wallHit, envRayLength, environmentLayer, QueryTriggerInteraction.Ignore))
                {
                    CombatEffectsManager.Instance.SpawnEnvHitParticle(wallHit.point, wallHit.normal);
                    CombatEffectsManager.Instance.SpawnHitCross(wallHit.point);
                    hitAnyEnv = true;
                }

                // Attack vector (up/down ceiling/floor)
                Vector3 attackVector = dir == AttackDirection.Down ? Vector3.down : Vector3.up;
                if (attackVector != Vector3.down || !hitAnyEnv)
                {
                    if (Physics.Raycast(playerTransform.position, attackVector, out RaycastHit dirHit, envRayLength, environmentLayer, QueryTriggerInteraction.Ignore))
                    {
                        CombatEffectsManager.Instance.SpawnEnvHitParticle(dirHit.point, dirHit.normal);
                        CombatEffectsManager.Instance.SpawnHitCross(dirHit.point);
                        hitAnyEnv = true;
                    }
                }

                if (hitAnyEnv && !hitAnyEnemy)
                    OnEnvironmentHit?.Invoke();
            }

            Log($"Directional blast ({dir}): {enemyHits.Length} enemies, radius={blastRadius}, " +
                $"offset={step.blastForwardOffset}, durability={durabilityCost}");
            yield break;
        }

        /// <summary>
        /// Side attack: single hitscan ray from muzzle in facing direction.
        /// Supports piercing (collateral damage) via SphereCastAll.
        /// </summary>
        private IEnumerator CoSideHitscan(
            RangedWeaponData.RangedComboStep step,
            RangedWeaponData rangedData,
            WeaponInstance capturedWeapon,
            int capturedWeaponSlot)
        {
            // Durability
            int durabilityCost = Mathf.Max(1, Mathf.RoundToInt(step.durabilityMultiplier > 0f ? step.durabilityMultiplier : 1f));
            if (capturedWeapon != null)
            {
                for (int d = 0; d < durabilityCost; d++)
                {
                    if (capturedWeapon.ConsumeDurability())
                    {
                        HandleWeaponBroken(capturedWeaponSlot);
                        yield break;
                    }
                }
            }

            Transform muzzle = (muzzlePoint != null) ? muzzlePoint : sideAttack;
            Vector3 origin = muzzle.position;
            Vector3 dir = FacingAxis * Facing;
            float maxRange = step.maxRange > 0f ? step.maxRange : 50f;
            float castRadius = step.bulletRadius;
            float tracerDuration = step.tracerDuration > 0f ? step.tracerDuration : 0.06f;
            float damage = rangedData.baseDamage * (step.damageMultiplier > 0f ? step.damageMultiplier : 1f);
            Vector2 knockback = rangedData.knockbackForce;
            bool piercing = rangedData.piercing;

            Vector3 tracerEnd = origin + dir * maxRange;
            bool hitAnyEnemy = false;

            if (piercing)
            {
                // PIERCING: cast through everything, damage all enemies, stop at first wall
                RaycastHit[] allHits = (castRadius > 0f)
                    ? Physics.SphereCastAll(origin, castRadius, dir, maxRange, enemyLayer | environmentLayer, QueryTriggerInteraction.Ignore)
                    : Physics.RaycastAll(origin, dir, maxRange, enemyLayer | environmentLayer, QueryTriggerInteraction.Ignore);

                System.Array.Sort(allHits, (a, b) => a.distance.CompareTo(b.distance));

                bool hitEnv = false;

                foreach (var hit in allHits)
                {
                    int hitMask = 1 << hit.collider.gameObject.layer;

                    if ((hitMask & enemyLayer) != 0)
                    {
                        var damageable = hit.collider.GetComponent<IDamageable>()
                                      ?? hit.collider.GetComponentInParent<IDamageable>();

                        if (damageable != null && damageable.IsAlive)
                        {
                            var damageInfo = new DamageInfo(damage, playerTransform.gameObject, DamageType.Physical, knockback);
                            bool dealt = damageable.TakeDamage(damageInfo);

                            if (dealt)
                            {
                                hitAnyEnemy = true;

                                var enemy = hit.collider.GetComponent<EnemyCharacter>()
                                         ?? hit.collider.GetComponentInParent<EnemyCharacter>();
                                OnEnemyHit?.Invoke(enemy, damage);

                                if (CombatEffectsManager.Instance != null)
                                {
                                    CombatEffectsManager.Instance.SpawnEnemyHitVFX(hit.point, -dir);
                                    CombatEffectsManager.Instance.SpawnEnemyHurtParticle(hit.point, -dir);
                                }
                            }
                        }
                    }
                    else if ((hitMask & environmentLayer) != 0 && !hitEnv)
                    {
                        hitEnv = true;
                        tracerEnd = hit.point;

                        if (CombatEffectsManager.Instance != null)
                        {
                            CombatEffectsManager.Instance.SpawnEnvHitParticle(hit.point, hit.normal);
                            CombatEffectsManager.Instance.SpawnHitCross(hit.point);
                        }

                        if (!hitAnyEnemy)
                            OnEnvironmentHit?.Invoke();
                    }
                }

                if (hitAnyEnemy)
                {
                    PlayHitFeedback();
                    StartCoroutine(CoHitstop(enemyHitHitstopDuration));
                }
            }
            else
            {
                // NON-PIERCING: first hit stops the ray
                bool hitSomething;
                RaycastHit hit;

                if (castRadius > 0f)
                    hitSomething = Physics.SphereCast(origin, castRadius, dir, out hit, maxRange, enemyLayer | environmentLayer, QueryTriggerInteraction.Ignore);
                else
                    hitSomething = Physics.Raycast(origin, dir, out hit, maxRange, enemyLayer | environmentLayer, QueryTriggerInteraction.Ignore);

                if (hitSomething)
                {
                    tracerEnd = hit.point;
                    int hitMask = 1 << hit.collider.gameObject.layer;

                    if ((hitMask & enemyLayer) != 0)
                    {
                        var damageable = hit.collider.GetComponent<IDamageable>()
                                      ?? hit.collider.GetComponentInParent<IDamageable>();

                        if (damageable != null && damageable.IsAlive)
                        {
                            var damageInfo = new DamageInfo(damage, playerTransform.gameObject, DamageType.Physical, knockback);
                            bool dealt = damageable.TakeDamage(damageInfo);

                            if (dealt)
                            {
                                hitAnyEnemy = true;
                                PlayHitFeedback();
                                StartCoroutine(CoHitstop(enemyHitHitstopDuration));

                                var enemy = hit.collider.GetComponent<EnemyCharacter>()
                                         ?? hit.collider.GetComponentInParent<EnemyCharacter>();
                                OnEnemyHit?.Invoke(enemy, damage);

                                if (CombatEffectsManager.Instance != null)
                                {
                                    CombatEffectsManager.Instance.SpawnEnemyHitVFX(hit.point, -dir);
                                    CombatEffectsManager.Instance.SpawnEnemyHurtParticle(hit.point, -dir);
                                }
                            }
                        }
                    }
                    else if ((hitMask & environmentLayer) != 0)
                    {
                        if (CombatEffectsManager.Instance != null)
                        {
                            CombatEffectsManager.Instance.SpawnEnvHitParticle(hit.point, hit.normal);
                            CombatEffectsManager.Instance.SpawnHitCross(hit.point);
                        }
                        OnEnvironmentHit?.Invoke();
                    }
                }
            }

            // TRACER
            if (rangedData.tracerPrefab != null && ProjectileManager.Instance != null)
            {
                ProjectileManager.Instance.FireTracer(
                    rangedData.tracerPrefab, origin, tracerEnd, tracerDuration);
            }

            Log($"Hitscan | hit={hitAnyEnemy} | piercing={piercing}");
            yield break;
        }

        /// <summary>
        /// Yields until the current attack animation reaches the given normalised time.
        /// Falls back to delayBeforeAttack if no SpineAnimationController is found.
        /// </summary>
        private IEnumerator CoWaitForFirePose(float normalizedTime)
        {
            if (spineController != null)
            {
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

                Log($"CoWaitForFirePose timed out at normalizedTime={normalizedTime:F2}");
            }
            else
            {
                yield return new WaitForSeconds(delayBeforeAttack);
            }
        }

        #endregion

        #region Ranged Helpers

        /// <summary>
        /// Resolves where the blast sphere center should be.
        /// Uses blastForwardOffset to push the center forward in the facing direction.
        /// </summary>
        private Vector3 ResolveBlastOrigin(AttackDirection dir, float radius, float forwardOffset)
        {
            Vector3 origin = playerTransform.position;

            if (forwardOffset > 0f)
                origin += FacingAxis * Facing * forwardOffset;

            switch (dir)
            {
                case AttackDirection.Down: origin.y -= radius * 0.5f; break;
                case AttackDirection.Up: origin.y += radius * 0.5f; break;
            }

            return origin;
        }

        private IEnumerator CoSmoothRangedRecoil(AttackDirection dir, float recoilMagnitude, float duration)
        {
            if (playerRb == null || Mathf.Abs(recoilMagnitude) <= 0f) yield break;

            float dur = duration > 0f ? duration : 0.1f;

            Vector3 recoilDir;
            switch (dir)
            {
                case AttackDirection.Down: recoilDir = Vector3.up; break;
                case AttackDirection.Up: recoilDir = Vector3.down; break;
                default: recoilDir = FacingAxis * -Facing; break;
            }

            Vector3 peakVelocity = recoilDir * recoilMagnitude;
            float elapsed = 0f;

            while (elapsed < dur)
            {
                elapsed += Time.deltaTime;
                float t = Mathf.Clamp01(elapsed / dur);
                float multiplier = 1f - (t * t);

                Vector3 vel = playerRb.linearVelocity;
                // Project onto recoilDir so we correctly handle both X and Z lanes
                float currentAlong = Vector3.Dot(vel, recoilDir.normalized);
                float targetAlong = peakVelocity.magnitude * multiplier;
                vel += recoilDir.normalized * (targetAlong - currentAlong);
                playerRb.linearVelocity = vel;

                yield return null;
            }
        }

        private IEnumerator CoRestoreTimeScale(float targetTimeScale, float targetFixedDelta, float realDuration)
        {
            float startScale = Time.timeScale;
            float startFixed = Time.fixedDeltaTime;
            float elapsed = 0f;

            while (elapsed < realDuration)
            {
                elapsed += Time.unscaledDeltaTime;
                float t = Mathf.Clamp01(elapsed / realDuration);
                Time.timeScale = Mathf.Lerp(startScale, targetTimeScale, t);
                Time.fixedDeltaTime = Mathf.Lerp(startFixed, targetFixedDelta, t);
                yield return null;
            }

            Time.timeScale = targetTimeScale;
            Time.fixedDeltaTime = targetFixedDelta;
            activeTimeScaleRestore = null;
        }

        private void CleanupRangedHover(bool wasHovering)
        {
            if (!wasHovering) return;
            isRangedHovering = false;
            controller?.ClearGravityMultiplierOverride();
        }

        private void RestoreTimeScaleImmediate(bool wasBulletTime, float savedScale, float savedFixed)
        {
            if (!wasBulletTime) return;
            if (activeTimeScaleRestore != null)
                StopCoroutine(activeTimeScaleRestore);
            activeTimeScaleRestore = null;
            Time.timeScale = savedScale;
            Time.fixedDeltaTime = savedFixed;
        }

        #endregion

        #region Attack State

        public void OnAttackAnimationComplete()
        {
            Log($"Attack complete - slot {activeWeaponSlot}, {currentAttackDir}, combo {currentComboIndex}");

            activeCombatState?.OnAttackComplete(currentAttackDir, currentAttackGrounded, activeWeaponData);

            isAttacking = false;
            activeWeapon = null;
            activeCombatState = null;
            activeWeaponData = null;
            // Don't clear hasBufferedInput here — let Update() consume it
            // so buffered attacks fire immediately after this attack ends.

            if (playerState != null)
            {
                playerState.SetDownAttackRequested(false);
                playerState.SetAttacking(false);
            }

            if (!isRangedHovering)
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

            isRangedHovering = false;
            ClearAttackGravityOverride();
            ReleaseAttackInputLock();

            if (activeTimeScaleRestore != null)
                StopCoroutine(activeTimeScaleRestore);
            activeTimeScaleRestore = null;
            Time.timeScale = 1f;
            Time.fixedDeltaTime = 0.02f;
        }

        #endregion

        #region Attack Push

        private IEnumerator CoApplyAttackPush(AttackDirection dir, float forwardImpulse, float verticalImpulse, float impulseDuration, AnimationCurve lungeCurve = null)
        {
            if (playerRb == null) yield break;

            // Resolve local axes so attack pushes respect the current 2.5D lane orientation.
            // Default to world axes if we don't have a controller reference.
            Vector3 right = controller != null ? controller.transform.right : Vector3.right;
            Vector3 up = controller != null ? controller.transform.up : Vector3.up;

            Vector3 peakVelocity = Vector3.zero;

            if (dir == AttackDirection.Side)
            {
                // Side attacks: allow both forward (along lane) and optional vertical impulse.
                if (Mathf.Abs(forwardImpulse) > 0f)
                    peakVelocity += right * (forwardImpulse * Facing);

                if (Mathf.Abs(verticalImpulse) > 0f)
                    peakVelocity += up * verticalImpulse;
            }
            else
            {
                // Non-side (Up/Down) attacks: keep existing vertical-only behaviour.
                if (Mathf.Abs(verticalImpulse) > 0f)
                {
                    float signedVertical = (dir == AttackDirection.Down ? -verticalImpulse : verticalImpulse);
                    peakVelocity += up * signedVertical;
                }
            }

            if (peakVelocity.sqrMagnitude <= 0f) yield break;

            float duration = impulseDuration > 0f ? impulseDuration : defaultPushDuration;
            float elapsed = 0f;

            while (elapsed < duration)
            {
                elapsed += Time.deltaTime;
                float t = Mathf.Clamp01(elapsed / duration);

                float multiplier = (lungeCurve != null && lungeCurve.length > 0)
                    ? lungeCurve.Evaluate(t)
                    : (1f - t * t);

                Vector3 vel = playerRb.linearVelocity;

                // Project current velocity onto local axes and apply the scaled peakVelocity along those axes
                // so we don't accidentally write into the wrong world component when the lane is rotated.
                float currentRight = Vector3.Dot(vel, right);
                float currentUp = Vector3.Dot(vel, up);

                float targetRight = currentRight;
                float targetUp = currentUp;

                // Only override components that this attack actually uses.
                float peakRight = Vector3.Dot(peakVelocity, right);
                float peakUp = Vector3.Dot(peakVelocity, up);

                if (!Mathf.Approximately(peakRight, 0f))
                    targetRight = peakRight * multiplier;

                if (!Mathf.Approximately(peakUp, 0f))
                    targetUp = peakUp * multiplier;

                // Rebuild velocity from modified local components plus any remaining component along the forward axis.
                Vector3 forward = controller != null ? controller.transform.forward : Vector3.forward;
                Vector3 forwardComponent = Vector3.Project(vel, forward);

                vel = right * targetRight + up * targetUp + forwardComponent;
                playerRb.linearVelocity = vel;

                yield return null;
            }

            // After the lunge, clear only the components this attack was controlling, leave others intact.
            {
                Vector3 vel = playerRb.linearVelocity;

                float peakRight = Vector3.Dot(peakVelocity, right);
                float peakUp = Vector3.Dot(peakVelocity, up);

                if (!Mathf.Approximately(peakRight, 0f))
                    vel -= right * Vector3.Dot(vel, right);

                if (!Mathf.Approximately(peakUp, 0f))
                    vel -= up * Vector3.Dot(vel, up);

                playerRb.linearVelocity = vel;
            }
        }

        private void ApplyAttackGravityOverride(AttackDirection dir, float airGravityMultiplier)
        {
            if (controller == null) return;
            if (playerState == null || playerState.IsGrounded) return;
            if (airGravityMultiplier < 0f) return; // negative = disabled

            // For down attacks, zero out Y velocity so the float starts cleanly
            if (dir == AttackDirection.Down && playerRb != null)
                playerRb.linearVelocity = new Vector3(playerRb.linearVelocity.x, 0f, playerRb.linearVelocity.z);

            controller.SetGravityMultiplierOverride(airGravityMultiplier);
        }

        private void ApplyAttackGravityOverride(float airGravityMultiplier)
        {
            ApplyAttackGravityOverride(currentAttackDir, airGravityMultiplier);
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
            public Collider target;
            public Collider[] allTargets;
            public Vector3 point;
        }

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

        private void DealDamage(Collider target, float damageMultiplier, Vector2 knockback)
        {
            var damageable = target.GetComponent<IDamageable>()
                          ?? target.GetComponentInParent<IDamageable>();

            if (damageable == null || !damageable.IsAlive) return;

            float damage = activeWeaponData != null ? activeWeaponData.baseDamage : 10f;
            if (damageMultiplier > 0f) damage *= damageMultiplier;

            var damageInfo = new DamageInfo(damage, playerTransform.gameObject, DamageType.Physical, knockback);
            bool damageDealt = damageable.TakeDamage(damageInfo);

            if (damageDealt)
            {
                var enemy = target.GetComponent<EnemyCharacter>() ?? target.GetComponentInParent<EnemyCharacter>();
                OnEnemyHit?.Invoke(enemy, damage);

                if (activeWeaponSlot != 0 && activeWeapon != null)
                    if (activeWeapon.ConsumeDurability())
                        HandleWeaponBroken(activeWeaponSlot);

                SpawnEnemyHitVFX(target);
            }
        }

        private void DealDamageToAll(Collider[] targets, float damageMultiplier, Vector2 knockback)
        {
            bool anyHit = false;

            foreach (var target in targets)
            {
                var damageable = target.GetComponent<IDamageable>()
                              ?? target.GetComponentInParent<IDamageable>();

                if (damageable == null || !damageable.IsAlive) continue;

                float damage = activeWeaponData != null ? activeWeaponData.baseDamage : 10f;
                if (damageMultiplier > 0f) damage *= damageMultiplier;

                var damageInfo = new DamageInfo(damage, playerTransform.gameObject, DamageType.Physical, knockback);
                bool damageDealt = damageable.TakeDamage(damageInfo);

                if (damageDealt)
                {
                    anyHit = true;
                    var enemy = target.GetComponent<EnemyCharacter>() ?? target.GetComponentInParent<EnemyCharacter>();
                    OnEnemyHit?.Invoke(enemy, damage);
                    SpawnEnemyHitVFX(target);
                }
            }

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
                _ => FacingAxis * Facing
            };
        }

        #endregion

        #region Pickups

        public void PickupWeaponToSlot(int slot, WorldWeaponPickup pickup)
        {
            if (pickup == null || (slot != 1 && slot != 2)) return;

            var existing = GetWeaponForSlot(slot);
            if (existing != null)
                DropWeapon(slot);

            SetupWeaponInSlot(slot, pickup);
        }


        public bool HasEmptyWeaponSlot()
        {
            return weaponSlot1 == null || weaponSlot2 == null;
        }

        private void SetupWeaponInSlot(int slot, WorldWeaponPickup pickup)
        {
            var weapon = pickup.weaponInstance;
            bool isRanged = weapon.weaponData is RangedWeaponData;
            pickup.gameObject.SetActive(false);

            weapon.gameObject.SetActive(true);
            // Keep ranged weapons out of the hand socket; they attack from data/muzzle logic.
            weapon.transform.SetParent(isRanged ? transform : weaponHolder, false);

            var rootRenderer = weapon.GetComponent<SpriteRenderer>();
            if (rootRenderer != null)
                rootRenderer.sortingOrder = 11;
            weapon.SetOwnerRigidbody(playerRb);

            if (isRanged)
            {
                weapon.transform.localPosition = Vector3.zero;
                weapon.transform.localRotation = Quaternion.identity;
                weapon.transform.localScale = Vector3.one;
            }
            else if (weapon.weaponData != null)
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

        private Vector3 ResolveHitOrigin(AttackDirection dir, Transform anchor)
        {
            float range = (activeWeaponData as MeleeWeaponData)?.attackRange ?? 1f;

            switch (dir)
            {
                case AttackDirection.Side:
                    return anchor.position + FacingAxis * (Facing * range);

                case AttackDirection.Up:
                    return new Vector3(
                        anchor.position.x,
                        playerTransform.position.y + range,
                        playerTransform.position.z);

                case AttackDirection.Down:
                    return new Vector3(
                        anchor.position.x,
                        playerTransform.position.y - range,
                        playerTransform.position.z);

                default:
                    return playerTransform.position;
            }
        }

        private void SetWeaponVisible(WeaponInstance weapon, bool visible)
        {
            if (weapon == null) return;
            if (weapon.weaponData is RangedWeaponData)
                visible = false;

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
            playerRb.AddForce(FacingAxis * -Facing * hitRecoil, ForceMode.Impulse);
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