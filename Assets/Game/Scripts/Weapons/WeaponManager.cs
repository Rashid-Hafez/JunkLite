using UnityEngine;
using System;

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

        // Internal refs
        private Transform playerTransform;
        private PlayerState playerState;
        private Character2D5Controller controller;
        private SpineAnimationController spineController;
        private PlayerWeaponLoadout weaponLoadout;
        private WeaponHitResolver hitResolver;
        private WeaponDamageResolver damageResolver;
        private WeaponAttackMotion attackMotion;
        private WeaponAttackExecutor attackExecutor;

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
        private bool attackInputLockApplied;
        private bool currentAttackGrounded;
        private int nextAttackExecutionId;
        private int activeAttackExecutionId;
        private WeaponAttackExecutor.Execution activeAttackExecution;

        // Input buffer
        private bool hasBufferedInput;
        private int bufferedWeaponSlot;
        private Vector2 bufferedInput;
        private bool bufferedGrounded;
        private float bufferTimer;

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
            playerTransform = transform.parent ?? transform;
            playerState = GetComponentInParent<PlayerState>();
            controller = GetComponentInParent<Character2D5Controller>();
            hitResolver = new WeaponHitResolver(enemyLayer, environmentLayer);
            damageResolver = new WeaponDamageResolver(
                playerTransform != null ? playerTransform.gameObject : gameObject);
            spineController = GetComponentInParent<SpineAnimationController>()
                           ?? GetComponentInChildren<SpineAnimationController>();
            weaponLoadout = GetComponent<PlayerWeaponLoadout>()
                         ?? gameObject.AddComponent<PlayerWeaponLoadout>();
            weaponLoadout.ApplyDefaultsIfMissing(weaponHolder);
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

            attackMotion = new WeaponAttackMotion(controller, defaultPushDuration);
            attackExecutor = new WeaponAttackExecutor(
                this,
                attackMotion,
                hitResolver,
                damageResolver,
                spineController,
                playerTransform,
                muzzlePoint,
                enemyLayer,
                environmentLayer,
                RequestAttackAnimation,
                CompleteAttackWithoutAnimation,
                (enemy, damage) => OnEnemyHit?.Invoke(enemy, damage),
                () => OnEnvironmentHit?.Invoke(),
                PlayHitFeedback,
                Log);

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

            CancelActiveAttackExecution();
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
                    return;
                }
            }

            var combat = GetCombatStateForSlot(bufferedWeaponSlot);
            if (!isAttacking && combat != null && combat.CanAttack)
            {
                hasBufferedInput = false;
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

            weaponLoadout?.TrySwapSlots();
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
        }

        private void StartAttack(int slot, AttackDirection dir)
        {
            var combat = GetCombatStateForSlot(slot);
            var data = GetWeaponDataForSlot(slot);
            if (combat == null || data == null) return;

            /* if (playerState != null && !playerState.IsGrounded && !playerState.CanAirAttack)
             {
                 return;
             }*/ // Why are we blocking air attack??

            if (lastAttackedSlot >= 0 && lastAttackedSlot != slot)
            {
                GetCombatStateForSlot(lastAttackedSlot)?.ResetCombo();
            }

            bool isGrounded = playerState == null || playerState.IsGrounded;

            if (!combat.TryBeginAttack(dir, isGrounded, data, out int comboIndex, out string animName))
            {
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
            currentAttackGrounded = isGrounded;

            int executionId = ++nextAttackExecutionId;
            var executionRequest = new WeaponAttackExecutionRequest(
                executionId,
                slot,
                comboIndex,
                dir,
                isGrounded,
                animName,
                data,
                activeWeapon,
                GetAttackTransform(dir),
                GetFallbackRadius(dir),
                Facing,
                FacingAxis,
                new WeaponAttackExecutionSettings(
                    delayBeforeAttack,
                    animationLeadTime,
                    attackOpenWindow,
                    downAttackFloatNormalized,
                    enemyHitHitstopDuration));

            activeAttackExecution = attackExecutor?.Prepare(executionRequest);
            if (activeAttackExecution == null)
            {
                OnAttackInterrupted();
                return;
            }
            activeAttackExecutionId = executionId;

            if (controller != null && facingLockDuration > 0f)
                controller.LockFacing(facingLockDuration);

            if (playerState != null)
                playerState.SetAttacking(true);

            // Play weapon-specific attack sound (if assigned on the WeaponData)
            PlayWeaponAttackSfx(data);

            ApplyAttackInputLock();

            activeAttackExecution.Start();
        }

        #endregion

        #region Attack State

        public void OnAttackAnimationComplete()
        {
            if (!isAttacking)
                return;

            CombatState completedCombatState = activeCombatState;
            WeaponData completedWeaponData = activeWeaponData;
            AttackDirection completedDirection = currentAttackDir;
            bool completedGrounded = currentAttackGrounded;

            CancelActiveAttackExecution();
            completedCombatState?.OnAttackComplete(
                completedDirection,
                completedGrounded,
                completedWeaponData);

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

            ReleaseAttackInputLock();
        }

        public void OnAttackInterrupted()
        {
            if (!isAttacking && activeAttackExecution == null)
                return;

            CancelActiveAttackExecution();
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

            ReleaseAttackInputLock();
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
            if (isModCombat && (weaponLoadout == null || !weaponLoadout.HasAnyWeapon))
                ExitModCombat();
        }

        #endregion

        #region Helpers

        private void RequestAttackAnimation(int executionId, string animationName)
        {
            if (!IsCurrentAttackExecution(executionId))
                return;

            if (playerState != null && !string.IsNullOrEmpty(animationName))
                playerState.RequestAttackAnimation(animationName);
            else
                CompleteAttackWithoutAnimation(executionId);
        }

        private void CompleteAttackWithoutAnimation(int executionId)
        {
            if (IsCurrentAttackExecution(executionId))
                OnAttackAnimationComplete();
        }

        private bool IsCurrentAttackExecution(int executionId)
        {
            return isAttacking &&
                   activeAttackExecution != null &&
                   activeAttackExecutionId == executionId;
        }

        private void CancelActiveAttackExecution()
        {
            WeaponAttackExecutor.Execution execution = activeAttackExecution;
            activeAttackExecution = null;
            activeAttackExecutionId = 0;
            execution?.Cancel();
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

        private void PlayHitFeedback()
        {
            if (FeedbackManager.Instance != null)
            {
                FeedbackManager.Instance.DoHitFeedback(
                    impulseSource,
                    enemyHitHitstopDuration,
                    enemyHitShakeForce);
            }
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
