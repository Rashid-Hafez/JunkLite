using UnityEngine;
using System;
using System.Collections;
using System.Collections.Generic;

namespace junklite
{
    [RequireComponent(typeof(Collider))]
    [RequireComponent(typeof(PlayerWeaponLoadout))]
    public class WeaponManager : MonoBehaviour
    {
        #region Fields

        [Header("Fist Weapon")]
        [SerializeField] private WeaponData fistWeaponData;

        private CombatState fistCombat;

        [Header("Legacy Loadout Migration")]
        [Tooltip("Temporary fallback for player prefabs not yet serialized with PlayerWeaponLoadout.")]
        [SerializeField, HideInInspector] private Transform weaponHolder;

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

        [Header("Down Attack Float")]
        [SerializeField]
        [Range(0f, 1f)]
        [Tooltip("Fraction of the total attack duration the player floats when performing an airborne down attack. 0 = no float, 1 = full window.")]
        private float downAttackFloatNormalized = 0.4f;

        [Header("Attack Settings")]
        [SerializeField] private float facingLockDuration = 0.25f;
        [SerializeField] private float inputThreshold = 0.5f;

        [Header("Attack Push")]
        [Tooltip("Fallback push duration when ComboStep.forwardImpulseDuration is 0")]
        [SerializeField] private float defaultPushDuration = 0.08f;

        [Header("Combat Mode Audio")]
        [SerializeField] private SoundEntry modCombatEnterSfx;

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
        private PlayerWeaponLoadout weaponLoadout;

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
        public PlayerWeaponLoadout Loadout => weaponLoadout;
        public WeaponInstance ActiveWeapon => activeWeapon;

        public event Action OnCombatModeChanged;
        public event Action OnCombatModeRejected;
        /// <summary>Raised only after damage is applied. The float is the actual applied damage.</summary>
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
            weaponLoadout = GetComponent<PlayerWeaponLoadout>()
                         ?? gameObject.AddComponent<PlayerWeaponLoadout>();
            weaponLoadout.ApplyDefaultsIfMissing(weaponHolder);
            weaponLoadout.Initialize(playerRb);
            weaponLoadout.WeaponChanged += HandleLoadoutChanged;
            weaponLoadout.WeaponBroken += HandleLoadoutWeaponBroken;

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

            if (weaponLoadout != null)
            {
                weaponLoadout.WeaponChanged -= HandleLoadoutChanged;
                weaponLoadout.WeaponBroken -= HandleLoadoutWeaponBroken;
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
                AttackDirection dir = ResolveAttackDirection(bufferedInput, bufferedGrounded, IsRangedSlot(bufferedWeaponSlot, bufferedGrounded));
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

            if (weaponLoadout == null || !weaponLoadout.HasAnyWeapon)
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

            weaponLoadout.SetWeaponsVisible(true);

            TryPrewarmRangedPool(weaponLoadout.WeaponSlot1);
            TryPrewarmRangedPool(weaponLoadout.WeaponSlot2);
            PlaySfxAtPlayer(modCombatEnterSfx);

            OnCombatModeChanged?.Invoke();
        }

        private void ExitModCombat()
        {
            isModCombat = false;
            weaponLoadout?.WeaponSlot1?.ResetCombo();
            weaponLoadout?.WeaponSlot2?.ResetCombo();
            lastAttackedSlot = -1;
            hasBufferedInput = false;

            weaponLoadout?.SetWeaponsVisible(false);

            OnCombatModeChanged?.Invoke();
        }

        private void TryPrewarmRangedPool(WeaponInstance weapon)
        {
            if (weapon == null || weapon.weaponData is not RangedWeaponData ranged) return;
            if (ranged.tracerPrefab == null) return;
            ProjectileManager.Instance?.PrewarmPool(ranged.tracerPrefab, 10);
        }

        private void PlaySfxAtPlayer(SoundEntry entry)
        {
            if (AudioManager.Instance == null || entry == null || !entry.IsValid) return;

            Vector3 position = playerTransform != null ? playerTransform.position : transform.position;
            AudioManager.Instance.PlaySpatialAtPosition(entry, position, spatialBlend: 0f);
        }

        private void PlayWeaponAttackSfx(WeaponData data)
        {
            if (data == null) return;

            SoundEntry entry = null;
            if (data.attackVariants != null && data.attackVariants.HasEntries)
                entry = data.attackVariants.GetRandomEntry();
            else
                entry = data.attackSfx;

            PlaySfxAtPlayer(entry);
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

            AttackDirection dir = ResolveAttackDirection(moveInput, isGrounded, IsRangedSlot(weaponSlot, isGrounded));
            StartAttack(weaponSlot, dir);
        }

        private WeaponInstance GetWeaponForSlot(int slot)
        {
            return weaponLoadout?.GetWeapon(slot);
        }

        private CombatState GetCombatStateForSlot(int slot)
        {
            return slot switch
            {
                0 => fistCombat,
                1 => weaponLoadout?.WeaponSlot1?.Combat,
                2 => weaponLoadout?.WeaponSlot2?.Combat,
                _ => null
            };
        }

        private WeaponData GetWeaponDataForSlot(int slot)
        {
            return slot switch
            {
                0 => fistWeaponData,
                1 => weaponLoadout?.WeaponSlot1?.weaponData,
                2 => weaponLoadout?.WeaponSlot2?.weaponData,
                _ => null
            };
        }

        public void DropWeapon(int slot)
        {
            if (isAttacking || weaponLoadout == null || playerTransform == null)
                return;

            Vector3 dropPosition = playerTransform.position + FacingAxis * Facing * 1.2f;
            weaponLoadout.TryDropWeapon(slot, dropPosition);
        }

        public void SwapWeaponSlots()
        {
            if (isAttacking) return;

            if (weaponLoadout != null && weaponLoadout.TrySwapSlots())
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
            weaponLoadout?.SetWeaponsVisible(visible);
        }

        #endregion

        #region Direction Resolution

        private AttackDirection ResolveAttackDirection(Vector2 moveInput, bool isGrounded, bool preferDownWhenAirborne = false)
        {
            if (moveInput.y > inputThreshold)
                return AttackDirection.Up;

            // Ranged weapons default to Down when airborne unless the player pushes up.
            if (!isGrounded && preferDownWhenAirborne)
                return AttackDirection.Down;

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

            // Play weapon-specific attack sound (if assigned on the WeaponData)
            PlayWeaponAttackSfx(data);

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
                    StartCoroutine(CoRangedAttack(dir, step, rangedData, activeWeapon));
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

            // Float: only fires on airborne down attacks.
            // Duration is computed from this step's actual timing so the normalized
            // slider always maps to a consistent fraction of the real attack window.
            if (dir == AttackDirection.Down && !currentAttackGrounded && downAttackFloatNormalized > 0f)
            {
                float hitDelay = step.hitDelay > 0f ? step.hitDelay : Mathf.Max(0f, delayBeforeAttack - animationLeadTime);
                float totalDuration = animationLeadTime + hitDelay + attackOpenWindow;
                StartCoroutine(CoDownAttackFloat(downAttackFloatNormalized * totalDuration));
            }

            if (animationLeadTime > 0f)
                yield return new WaitForSeconds(animationLeadTime);

            if (playerState != null && !string.IsNullOrEmpty(animName))
                playerState.RequestAttackAnimation(animName);
            else
                OnAttackAnimationComplete();

            float hitDelay2 = step.hitDelay > 0f ? step.hitDelay : Mathf.Max(0f, delayBeforeAttack - animationLeadTime);
            if (hitDelay2 > 0f)
                yield return new WaitForSeconds(hitDelay2);

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
                    bool damageApplied = false;

                    if (isPiercing && hitResult.allTargets != null)
                        damageApplied = DealDamageToAll(hitResult.allTargets, step.damageMultiplier, knockback);
                    else if (hitResult.target != null)
                        damageApplied = DealDamage(hitResult.target, step.damageMultiplier, knockback).WasApplied;

                    if (damageApplied)
                    {
                        PlayHitFeedback();
                        StartCoroutine(CoHitstop(enemyHitHitstopDuration));
                        ApplyRecoil(dir, step.hitRecoil);
                    }

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
            WeaponInstance capturedWeapon)
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
                yield return CoDirectionalBlast(dir, step, rangedData, capturedWeapon);
            else
                yield return CoSideHitscan(step, rangedData, capturedWeapon);

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
            WeaponInstance capturedWeapon)
        {
            // DURABILITY — scaled by durabilityMultiplier
            int durabilityCost = Mathf.Max(1, Mathf.RoundToInt(step.durabilityMultiplier > 0f ? step.durabilityMultiplier : 1f));
            if (capturedWeapon != null)
            {
                for (int d = 0; d < durabilityCost; d++)
                {
                    if (capturedWeapon.ConsumeDurability())
                    {
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
            var processedReceivers = new HashSet<int>();

            if (enemyHits.Length > 0)
            {
                foreach (var hit in enemyHits)
                {
                    DamageResult result = ApplyWeaponDamage(
                        hit,
                        damage,
                        knockback,
                        processedReceivers);

                    if (result.WasApplied)
                    {
                        hitAnyEnemy = true;

                        if (CombatEffectsManager.Instance != null)
                        {
                            Vector3 hitPoint = hit.ClosestPoint(blastOrigin);
                            Vector3 hitDir = (hitPoint - blastOrigin).normalized;
                            CombatEffectsManager.Instance.SpawnEnemyHitVFX(hitPoint, hitDir);
                            CombatEffectsManager.Instance.SpawnEnemyHurtParticle(hitPoint, hitDir);
                        }
                    }
                }

                if (hitAnyEnemy)
                {
                    PlayHitFeedback();
                    StartCoroutine(CoHitstop(enemyHitHitstopDuration));
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
            WeaponInstance capturedWeapon)
        {
            // Durability
            int durabilityCost = Mathf.Max(1, Mathf.RoundToInt(step.durabilityMultiplier > 0f ? step.durabilityMultiplier : 1f));
            if (capturedWeapon != null)
            {
                for (int d = 0; d < durabilityCost; d++)
                {
                    if (capturedWeapon.ConsumeDurability())
                    {
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
                var processedReceivers = new HashSet<int>();

                foreach (var hit in allHits)
                {
                    int hitMask = 1 << hit.collider.gameObject.layer;

                    if ((hitMask & enemyLayer) != 0)
                    {
                        DamageResult result = ApplyWeaponDamage(
                            hit.collider,
                            damage,
                            knockback,
                            processedReceivers);

                        if (result.WasApplied)
                        {
                            hitAnyEnemy = true;

                            if (CombatEffectsManager.Instance != null)
                            {
                                CombatEffectsManager.Instance.SpawnEnemyHitVFX(hit.point, -dir);
                                CombatEffectsManager.Instance.SpawnEnemyHurtParticle(hit.point, -dir);
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
                        DamageResult result = ApplyWeaponDamage(
                            hit.collider,
                            damage,
                            knockback,
                            null);

                        if (result.WasApplied)
                        {
                            hitAnyEnemy = true;
                            PlayHitFeedback();
                            StartCoroutine(CoHitstop(enemyHitHitstopDuration));

                            if (CombatEffectsManager.Instance != null)
                            {
                                CombatEffectsManager.Instance.SpawnEnemyHitVFX(hit.point, -dir);
                                CombatEffectsManager.Instance.SpawnEnemyHurtParticle(hit.point, -dir);
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
            Vector3 right = controller != null ? controller.transform.right : Vector3.right;
            Vector3 up = controller != null ? controller.transform.up : Vector3.up;

            Vector3 peakVelocity = Vector3.zero;

            if (dir == AttackDirection.Side)
            {
                if (Mathf.Abs(forwardImpulse) > 0f)
                    peakVelocity += right * (forwardImpulse * Facing);
                if (Mathf.Abs(verticalImpulse) > 0f)
                    peakVelocity += up * verticalImpulse;
            }
            else
            {
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

                float currentRight = Vector3.Dot(vel, right);
                float currentUp = Vector3.Dot(vel, up);
                float targetRight = currentRight;
                float targetUp = currentUp;

                float peakRight = Vector3.Dot(peakVelocity, right);
                float peakUp = Vector3.Dot(peakVelocity, up);

                if (!Mathf.Approximately(peakRight, 0f)) targetRight = peakRight * multiplier;
                if (!Mathf.Approximately(peakUp, 0f)) targetUp = peakUp * multiplier;

                Vector3 forward = controller != null ? controller.transform.forward : Vector3.forward;
                Vector3 forwardComponent = Vector3.Project(vel, forward);

                playerRb.linearVelocity = right * targetRight + up * targetUp + forwardComponent;
                yield return null;
            }

            // After the lunge, clear only the components this attack was controlling.
            {
                Vector3 vel = playerRb.linearVelocity;
                float peakRight = Vector3.Dot(peakVelocity, right);
                float peakUp = Vector3.Dot(peakVelocity, up);

                if (!Mathf.Approximately(peakRight, 0f)) vel -= right * Vector3.Dot(vel, right);
                if (!Mathf.Approximately(peakUp, 0f)) vel -= up * Vector3.Dot(vel, up);

                playerRb.linearVelocity = vel;
            }
        }

        /// <summary>
        /// Zeroes Y velocity and holds gravity at zero for <paramref name="duration"/> seconds.
        /// Only fires on airborne down attacks. ClearAttackGravityOverride() in both attack-end
        /// callbacks acts as a safety net if the attack is interrupted before this finishes.
        /// </summary>
        private IEnumerator CoDownAttackFloat(float duration)
        {
            if (controller == null || playerRb == null || duration <= 0f) yield break;

            playerRb.linearVelocity = new Vector3(
                playerRb.linearVelocity.x, 0f, playerRb.linearVelocity.z);

            controller.SetGravityMultiplierOverride(0f);
            yield return new WaitForSeconds(duration);
            controller.ClearGravityMultiplierOverride();
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

        private DamageResult DealDamage(Collider target, float damageMultiplier, Vector2 knockback)
        {
            float damage = CalculateActiveWeaponDamage(damageMultiplier);
            DamageResult result = ApplyWeaponDamage(target, damage, knockback, null);

            if (result.WasApplied)
            {
                if (activeWeaponSlot != 0 && activeWeapon != null)
                    activeWeapon.ConsumeDurability();

                SpawnEnemyHitVFX(target);
            }

            return result;
        }

        private bool DealDamageToAll(Collider[] targets, float damageMultiplier, Vector2 knockback)
        {
            bool anyHit = false;
            float damage = CalculateActiveWeaponDamage(damageMultiplier);
            var processedReceivers = new HashSet<int>();

            foreach (var target in targets)
            {
                DamageResult result = ApplyWeaponDamage(
                    target,
                    damage,
                    knockback,
                    processedReceivers);

                if (result.WasApplied)
                {
                    anyHit = true;
                    SpawnEnemyHitVFX(target);
                }
            }

            if (anyHit && activeWeaponSlot != 0 && activeWeapon != null)
                activeWeapon.ConsumeDurability();

            return anyHit;
        }

        private float CalculateActiveWeaponDamage(float damageMultiplier)
        {
            float damage = activeWeaponData != null ? activeWeaponData.baseDamage : 10f;
            return damageMultiplier > 0f ? damage * damageMultiplier : damage;
        }

        /// <summary>
        /// Resolves one weapon hit and publishes the actual applied amount. A shared
        /// processed-receiver set prevents multi-collider actors from taking the same
        /// piercing or area hit more than once.
        /// </summary>
        private DamageResult ApplyWeaponDamage(
            Collider target,
            float damage,
            Vector2 knockback,
            HashSet<int> processedReceivers)
        {
            if (!DamageReceiverUtility.TryGetReceiver(target, out var receiver))
                return DamageResult.Rejected(DamageOutcome.Invalid, damage);

            if (receiver is not Component receiverComponent)
                return DamageResult.Rejected(DamageOutcome.Invalid, damage);

            if (processedReceivers != null && !processedReceivers.Add(receiverComponent.GetInstanceID()))
                return DamageResult.Rejected(DamageOutcome.Invalid, damage);

            if (!receiver.IsAlive)
                return DamageResult.Rejected(DamageOutcome.Dead, damage);

            GameObject source = playerTransform != null ? playerTransform.gameObject : gameObject;
            DamageResult result = receiver.ReceiveDamage(new DamageRequest(
                damage,
                source,
                DamageType.Physical,
                knockback));

            if (!result.WasApplied)
                return result;

            EnemyCharacter enemy = receiverComponent.GetComponent<EnemyCharacter>()
                                ?? receiverComponent.GetComponentInParent<EnemyCharacter>()
                                ?? target.GetComponentInParent<EnemyCharacter>();

            OnEnemyHit?.Invoke(enemy, result.AppliedDamage);
            return result;
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
            if (isAttacking || weaponLoadout == null || playerTransform == null)
                return;

            Vector3 displacedWeaponDropPosition =
                playerTransform.position + FacingAxis * Facing * 1.2f;
            weaponLoadout.TryEquipWeapon(
                slot,
                pickup,
                displacedWeaponDropPosition,
                isModCombat);
        }

        private void HandleLoadoutChanged()
        {
            lastAttackedSlot = -1;
        }

        private void HandleLoadoutWeaponBroken(int slot)
        {
            Log($"Weapon in slot {slot} broke");

            if (isModCombat && (weaponLoadout == null || !weaponLoadout.HasAnyWeapon))
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

        /// <summary>
        /// Returns true if the given slot holds a ranged weapon AND the player is airborne.
        /// Used to default ranged air attacks to Down when no directional input is held.
        /// </summary>
        private bool IsRangedSlot(int slot, bool isGrounded)
        {
            if (isGrounded) return false;
            return GetWeaponDataForSlot(slot) is RangedWeaponData;
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
