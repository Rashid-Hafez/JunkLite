using UnityEngine;
using System;

namespace junklite
{
    /// <summary>
    /// Player combat: attack input and buffering, combo timing, melee execution, hit detection,
    /// damage requests/results and attack feedback for the player's current weapon
    /// (<see cref="PlayerArmament.Current"/>). There is no combat mode: the player is always armed.
    /// </summary>
    [RequireComponent(typeof(Collider))]
    [RequireComponent(typeof(PlayerArmament))]
    public class WeaponManager : MonoBehaviour
    {
        #region Fields

        [Header("Legacy Migration")]
        [Tooltip("Fallback default weapon for player prefabs that predate PlayerArmament.defaultWeapon.")]
        [SerializeField, HideInInspector] private WeaponData fistWeaponData;
        [SerializeField, HideInInspector] private Transform weaponHolder;

        [Header("Attack Transforms (Scene Anchors)")]
        [SerializeField] private Transform sideAttack;
        [SerializeField] private Transform upAttack;
        [SerializeField] private Transform downAttack;

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
        [Tooltip("Hit-stop on a launching hit (combo finisher). Longer than a normal hit so the launch lands.")]
        [SerializeField] private float launchHitstopDuration = 0.12f;
        [SerializeField] private float enemyHitShakeForce = 0.8f;

        [Header("Up Attack")]
        [Tooltip("Seconds after an up attack starts before the next one. Up + attack inside it is a side swing.")]
        [SerializeField, Min(0f)] private float upAttackCooldown = 1f;
        private float lastUpAttackTime = float.NegativeInfinity;

        [Header("Attack Timing & Combo Windows")]
        [Tooltip("Delay after attack animation ends before player can attack again.")]
        [SerializeField] private float attackCooldown = 0.15f;
        [Tooltip("Time window after attack ends to continue combo string.")]
        [SerializeField] private float comboWindow = 0.6f;
        [Tooltip("How long an early attack input remains valid in buffer.")]
        [SerializeField] private float bufferDuration = 0.35f;

        [Header("Attack Hit Window")]
        [SerializeField] private float delayBeforeAttack = 0.1f;
        [Tooltip("How long after impulse fires before the attack animation plays.")]
        [SerializeField] private float animationLeadTime = 0.05f;
        [SerializeField]
        [Tooltip("Time that the collision stays open to deliver the attack")]
        private float attackOpenWindow = 0.3f;

        [Header("Down Attack Float")]
        [SerializeField]
        [Range(0f, 1f)]
        [Tooltip("Fraction of the total attack duration the player floats when performing an airborne down attack. 0 = no float, 1 = full window.")]
        private float downAttackFloatNormalized = 0.4f;

        [SerializeField]
        [Range(0f, 1f)]
        [Tooltip("Fraction of the total attack duration the player hangs in place when performing an airborne side or up attack. 0 = keep falling, 1 = hang for the whole swing, then fall.")]
        private float airAttackFloatNormalized = 1f;

        [Header("Attack Settings")]
        [SerializeField] private float facingLockDuration = 0.25f;
        [SerializeField] private float inputThreshold = 0.5f;

        [Header("Attack Push")]
        [Tooltip("Fallback push duration when ComboStep.forwardImpulseDuration is 0")]
        [SerializeField] private float defaultPushDuration = 0.08f;

        [Header("Air Hit Hang")]
        [Tooltip("After a hit lands while the player is in the air, the player falls slowly for this long (the air time earned by hitting). 0 = off. Down attacks are excluded: they have their own float and pogo.")]
        [SerializeField, Min(0f)] private float airHitHangDuration = 0.5f;
        [Tooltip("Falling gravity multiplier during the air-hit hang. 0.22 of the normal ~6.9 g fall matches a launched enemy's air-hit fall (Launch > Air Hit Fall Gravity Scale 1.5).")]
        [SerializeField, Range(0f, 1f)] private float airHitHangGravityScale = 0.22f;

        [Header("Attack Input Lock")]
        [SerializeField] private bool lockMovementDuringAttack = true;

        [Header("Combat Focus")]
        [Tooltip("Soft lock-on for side attacks. Found on the player when empty; none = authored lunges, no focus.")]
        [SerializeField] private PlayerCombatFocus combatFocus;

        // Internal refs
        private Transform playerTransform;
        private PlayerState playerState;
        private Character2D5Controller controller;
        private SpineAnimationController spineController;
        private PlayerArmament armament;
        private WeaponHitResolver hitResolver;
        private WeaponDamageResolver damageResolver;
        private WeaponAttackMotion attackMotion;
        private WeaponAttackExecutor attackExecutor;

        // One combo/cooldown tracker for the current weapon. Rebuilt when the weapon changes.
        private CombatState combat;
        private WeaponData combatWeapon;

        // Attack state
        private bool isAttacking;
        private WeaponData activeWeaponData;
        private AttackDirection currentAttackDir;
        private int currentComboIndex;
        private bool attackInputLockApplied;
        private bool currentAttackGrounded;
        // A clip that is not this swing's (the Push clip, during this swing's lead time) must not finish it.
        private bool attackAnimationRequested;
        private int nextAttackExecutionId;
        private int activeAttackExecutionId;
        private WeaponAttackExecutor.Execution activeAttackExecution;

        // Input buffer
        private bool hasBufferedInput;
        private Vector2 bufferedInput;
        private bool bufferedGrounded;
        private float bufferTimer;

        // Recovery cancel: the swing can be cut short by a jump or dash once it has
        // connected, or once its hit window has passed if it missed. Startup and the
        // active frames stay committed; the player buffers the press until then.
        private float attackCancelableTime;

        // Upgrade-driven multipliers (1 = authored values). Set by PlayerCharacter from runtime stats.
        private float attackSpeedMultiplier = 1f;
        private float finisherForceMultiplier = 1f;

        #endregion

        #region Properties

        public bool IsAttacking => isAttacking;

        /// <summary>True while attacking and past the committed part of the swing.</summary>
        public bool IsAttackCancelable => isAttacking && Time.time >= attackCancelableTime;

        public AttackDirection CurrentAttackDirection => currentAttackDir;
        public float Facing => controller != null ? controller.FacingSign : Mathf.Sign(playerTransform.localScale.x);

        /// <summary>
        /// The horizontal movement axis for the current 2.5D lane.
        /// XY lane (Y rot = 0°)  → Vector3.right   (1, 0, 0)
        /// ZY lane (Y rot = 90°) → controller.transform.right = (0, 0, -1)
        /// Matches EnemyMovement.horizontalAxis so attacks fire along the correct world axis.
        /// </summary>
        private Vector3 FacingAxis => controller != null ? controller.transform.right : Vector3.right;

        public PlayerArmament Armament => armament;
        /// <summary>The weapon every attack uses right now.</summary>
        public WeaponData CurrentWeapon => armament != null ? armament.Current : fistWeaponData;
        public LayerMask EnemyLayer => enemyLayer;
        public LayerMask EnvironmentLayer => environmentLayer;

        /// <summary>
        /// Multiplies attack animation speed and divides cooldown/combo timing. 1 is authored speed.
        /// Owned by the upgrade system; weapons never set it.
        /// </summary>
        public float AttackSpeedMultiplier
        {
            get => attackSpeedMultiplier;
            set
            {
                float next = Mathf.Max(0.1f, value);
                if (Mathf.Approximately(next, attackSpeedMultiplier)) return;
                attackSpeedMultiplier = next;
                SyncCombatStateTimings();
            }
        }

        /// <summary>Multiplies the knockback of launching finishers (Heavy Finisher upgrade). 1 is authored force.</summary>
        public float FinisherForceMultiplier
        {
            get => finisherForceMultiplier;
            set => finisherForceMultiplier = Mathf.Max(0f, value);
        }

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
            if (combatFocus == null)
                combatFocus = GetComponentInParent<PlayerCombatFocus>();
            spineController = GetComponentInParent<SpineAnimationController>()
                           ?? GetComponentInChildren<SpineAnimationController>();
            armament = GetComponent<PlayerArmament>() ?? gameObject.AddComponent<PlayerArmament>();
            armament.ApplyDefaultsIfMissing(fistWeaponData, weaponHolder);
            armament.WeaponChanged += HandleWeaponChanged;

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
                playerState.OnGroundedChanged += HandleGroundedChanged;
            }

            attackMotion = new WeaponAttackMotion(controller, defaultPushDuration);
            attackExecutor = new WeaponAttackExecutor(
                this,
                attackMotion,
                hitResolver,
                damageResolver,
                playerTransform,
                environmentLayer,
                RequestAttackAnimation,
                CompleteAttackWithoutAnimation,
                (enemy, damage) => OnEnemyHit?.Invoke(enemy, damage),
                () => OnEnvironmentHit?.Invoke(),
                PlayHitFeedback,
                WarpHitDelay);

            EnsureCombatState();
        }

        private void OnDestroy()
        {
            if (playerState != null)
            {
                playerState.OnAttackAnimationComplete -= OnAttackAnimationComplete;
                playerState.OnAttackAnimationInterrupted -= OnAttackInterrupted;
                playerState.OnGroundedChanged -= HandleGroundedChanged;
            }

            if (armament != null)
                armament.WeaponChanged -= HandleWeaponChanged;

            CancelActiveAttackExecution();
            ReleaseAttackInputLock();
        }

        private void Update()
        {
            combat?.Tick(Time.deltaTime);

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

            if (!isAttacking && combat != null && combat.CanAttack)
            {
                hasBufferedInput = false;
                // Side comes from the stick now, not when the press was buffered: spamming attack
                // and flipping mid-combo turns the next swing around.
                float liveX = GameInputManager.Instance != null ? GameInputManager.Instance.MoveDirection.x : 0f;
                StartAttack(ResolveAttackDirection(bufferedInput, bufferedGrounded),
                    Mathf.Abs(liveX) > inputThreshold ? liveX : bufferedInput.x);
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
                if (modPickup.autoEquip && modManager != null && modManager.TryEquipMod(modInstance))
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

        #region Audio

        private void PlaySfxAtPlayer(SoundEntry entry)
        {
            if (AudioManager.Instance == null || entry == null || !entry.IsValid) return;

            Vector3 position = playerTransform != null ? playerTransform.position : transform.position;
            AudioManager.Instance.PlaySpatialAtPosition(entry, position, spatialBlend: 0f);
        }

        private void PlayWeaponAttackSfx(WeaponData data)
        {
            if (data == null) return;

            SoundEntry entry;
            if (data.attackVariants != null && data.attackVariants.HasEntries)
                entry = data.attackVariants.GetRandomEntry();
            else
                entry = data.attackSfx;

            PlaySfxAtPlayer(entry);
        }

        #endregion

        #region Public API

        /// <summary>Attack with the current weapon. Buffers the press while a swing or cooldown is running.</summary>
        public void Attack(Vector2 moveInput, bool isGrounded)
        {
            if (!EnsureCombatState()) return;

            if (isAttacking || !combat.CanAttack)
            {
                BufferAttack(moveInput, isGrounded);
                return;
            }

            StartAttack(ResolveAttackDirection(moveInput, isGrounded), moveInput.x);
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
            armament?.SetVisible(visible);
        }

        /// <summary>
        /// Lets another player-owned damage source (the dash strike) report an applied hit so mods
        /// charge and react exactly as they do for weapon hits. Call only with applied damage.
        /// </summary>
        public void ReportExternalHit(EnemyCharacter enemy, float appliedDamage)
        {
            if (appliedDamage <= 0f) return;
            OnEnemyHit?.Invoke(enemy, appliedDamage);
        }

        #endregion

        #region Direction Resolution

        private AttackDirection ResolveAttackDirection(Vector2 moveInput, bool isGrounded)
        {
            // The launcher is a ground move: up in the air is just an air attack. On cooldown it is a side swing.
            if (moveInput.y > inputThreshold && isGrounded && Time.time >= lastUpAttackTime + upAttackCooldown)
                return AttackDirection.Up;

            if (moveInput.y < -inputThreshold && !isGrounded)
                return AttackDirection.Down;

            return AttackDirection.Side;
        }

        #endregion

        #region Attack Core

        private void BufferAttack(Vector2 moveInput, bool isGrounded)
        {
            hasBufferedInput = true;
            bufferedInput = moveInput;
            bufferedGrounded = isGrounded;
            bufferTimer = bufferDuration;
        }

        private void StartAttack(AttackDirection dir, float horizontalInput)
        {
            if (!EnsureCombatState()) return;
            WeaponData data = combatWeapon;

            bool isGrounded = playerState == null || playerState.IsGrounded;

            if (!combat.TryBeginAttack(dir, isGrounded, data, out int comboIndex, out string animName))
                return;

            if (playerState != null)
            {
                playerState.SetDownAttackRequested(dir == AttackDirection.Down);
                if (!playerState.IsGrounded)
                    playerState.MarkAirAttackUsed();
            }

            float windupRelease = 0f;
            float clipDuration = 0f;
            if (spineController != null)
            {
                MeleeWeaponData.MeleeComboStep holdStep = default;
                bool hasStep = data is MeleeWeaponData melee && melee.TryGetMeleeStep(dir, comboIndex, isGrounded, out holdStep);
                spineController.SetAttackWindup(hasStep ? holdStep.windupHold : 0f, hasStep ? holdStep.windupAt : 0f, animName);
                if (hasStep && holdStep.windupHold > 0f)
                    windupRelease = spineController.GetWindupReleaseTime(animName);
                clipDuration = spineController.GetAttackRealDuration(animName);
            }
            if (dir == AttackDirection.Up)
                lastUpAttackTime = Time.time;

            isAttacking = true;
            attackAnimationRequested = false;
            attackCancelableTime = Time.time + animationLeadTime + delayBeforeAttack + attackOpenWindow +
                                   (spineController != null ? spineController.GetAttackHoldDuration(animName) : 0f);
            activeWeaponData = data;
            currentAttackDir = dir;
            currentComboIndex = comboIndex;
            currentAttackGrounded = isGrounded;

            // Side swings go where the stick points (else where she faces): turn first, so
            // flipping mid-combo hits the enemy on the new side.
            if (dir == AttackDirection.Side && controller != null && Mathf.Abs(horizontalInput) > inputThreshold &&
                Mathf.Sign(horizontalInput) != Facing)
                controller.FaceForTraversal(horizontalInput > 0f);

            ResolveFocusLunge(dir, comboIndex, isGrounded, data,
                out float lungeDistance, out Collider focusCollider, out Func<bool> stopLunge);

            int executionId = ++nextAttackExecutionId;
            var executionRequest = new WeaponAttackExecutionRequest(
                executionId,
                comboIndex,
                dir,
                isGrounded,
                animName,
                data,
                GetAttackTransform(dir),
                GetFallbackRadius(dir),
                Facing,
                FacingAxis,
                new WeaponAttackExecutionSettings(
                    delayBeforeAttack,
                    animationLeadTime,
                    attackOpenWindow,
                    downAttackFloatNormalized,
                    airAttackFloatNormalized,
                    enemyHitHitstopDuration,
                    launchHitstopDuration,
                    finisherForceMultiplier),
                lungeDistance,
                combatFocus != null ? combatFocus.GapCloseMaxSpeed : 0f,
                focusCollider,
                stopLunge,
                windupRelease,
                clipDuration);

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

            PlayWeaponAttackSfx(data);

            ApplyAttackInputLock();

            activeAttackExecution.Start();
        }

        #endregion

        #region Attack State

        public void OnAttackAnimationComplete()
        {
            if (!isAttacking || !attackAnimationRequested)
                return;

            CombatState completedCombatState = combat;
            WeaponData completedWeaponData = activeWeaponData;
            AttackDirection completedDirection = currentAttackDir;
            bool completedGrounded = currentAttackGrounded;

            CancelActiveAttackExecution();
            if (completedCombatState != null && completedCombatState.Data == completedWeaponData)
                completedCombatState.OnAttackComplete(completedDirection, completedGrounded, completedWeaponData);

            isAttacking = false;
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
            combat?.OnAttackInterrupted();

            isAttacking = false;
            activeWeaponData = null;
            hasBufferedInput = false;

            if (playerState != null)
            {
                playerState.SetDownAttackRequested(false);
                playerState.SetAttacking(false);
            }

            ReleaseAttackInputLock();
        }

        /// <summary>
        /// Ends the current attack so a jump or dash can start. True when there is no
        /// attack or it is in recovery (then it is cancelled and input is released);
        /// false during startup or the hit window (the caller should buffer the press).
        /// </summary>
        public bool TryCancelAttackForMovement()
        {
            if (!isAttacking)
                return true;
            if (Time.time < attackCancelableTime)
                return false;

            OnAttackInterrupted();
            return true;
        }

        #endregion

        #region Weapon Changes

        /// <summary>Touching the ground gives the air side chain back.</summary>
        private void HandleGroundedChanged(bool grounded)
        {
            if (grounded) combat?.ResetAirChain();
        }

        private void HandleWeaponChanged()
        {
            // A running swing keeps its own data; the next attack uses the new weapon with a fresh combo.
            combat = null;
            combatWeapon = null;
            hasBufferedInput = false;
            EnsureCombatState();
        }

        /// <summary>Builds the combo tracker for the current weapon when missing. False without a weapon.</summary>
        private bool EnsureCombatState()
        {
            WeaponData weapon = CurrentWeapon;
            if (weapon == null)
            {
                if (combat == null)
                    Debug.LogWarning("[WeaponManager] No weapon: assign PlayerArmament.defaultWeapon.", this);
                return false;
            }

            if (combat != null && combatWeapon == weapon)
                return true;

            combatWeapon = weapon;
            combat = new CombatState(weapon, ScaledCooldown, ScaledComboWindow);
            return true;
        }

        private float ScaledCooldown => attackCooldown / attackSpeedMultiplier;
        private float ScaledComboWindow => comboWindow / attackSpeedMultiplier;

        private void SyncCombatStateTimings()
        {
            combat?.SetTiming(ScaledCooldown, ScaledComboWindow);
        }

        #endregion

        #region Helpers

        private void RequestAttackAnimation(int executionId, string animationName)
        {
            if (!IsCurrentAttackExecution(executionId))
                return;

            attackAnimationRequested = true;
            if (playerState != null && !string.IsNullOrEmpty(animationName))
                playerState.RequestAttackAnimation(animationName);
            else
                CompleteAttackWithoutAnimation(executionId);
        }

        private void CompleteAttackWithoutAnimation(int executionId)
        {
            if (IsCurrentAttackExecution(executionId))
            {
                attackAnimationRequested = true;
                OnAttackAnimationComplete();
            }
        }

        private float WarpHitDelay(string animationName, float hitDelay) =>
            spineController != null ? spineController.WarpAttackDelay(animationName, hitDelay) : hitDelay;

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

        /// <summary>
        /// Combat focus for a side swing: with a focused enemy the lunge closes exactly to hitting
        /// distance (further than authored if needed) and stops on arrival; without one it moves at most
        /// the weapon's noTargetLungeDistance. Other directions keep their authored motion (-1).
        /// </summary>
        private void ResolveFocusLunge(AttackDirection dir, int comboIndex, bool isGrounded, WeaponData data,
            out float lungeDistance, out Collider focusCollider, out Func<bool> stopLunge)
        {
            lungeDistance = -1f;
            focusCollider = null;
            stopLunge = null;
            if ((dir != AttackDirection.Side && dir != AttackDirection.Up) || combatFocus == null || !combatFocus.isActiveAndEnabled ||
                data is not MeleeWeaponData melee ||
                !melee.TryGetMeleeStep(dir, comboIndex, isGrounded, out MeleeWeaponData.MeleeComboStep step))
                return;

            float side = Facing;
            EnemyCharacter target = combatFocus.Acquire(side, out float gap);
            if (target == null)
            {
                // The launcher always steps its authored dash; only side swings hold still with no target.
                if (dir == AttackDirection.Up)
                    return;

                float authored = LungeWarp.AuthoredDistance(step.forwardImpulse, step.forwardImpulseDuration, step.lungeCurve);
                lungeDistance = Mathf.Min(melee.noTargetLungeDistance, authored) * step.LungeScale;
                return;
            }

            // Stop where the focused enemy's near side sits focusSpacing deep inside the hit sphere.
            Transform anchor = GetAttackTransform(dir);
            Vector3 anchorPosition = anchor != null ? anchor.position : playerTransform.position;
            float sphereCentre = Mathf.Abs(Vector3.Dot(anchorPosition - playerTransform.position, FacingAxis)) +
                                 melee.attackRange;
            float sphereRadius = step.hitRadius + GetFallbackRadius(dir);
            float stopGap = sphereCentre + sphereRadius * (1f - melee.focusSpacing);

            // A step that slides further closes in past the usual stop (never through the body).
            float scaled = Mathf.Max(0f, gap - stopGap) * step.LungeScale;
            stopGap = Mathf.Max(Mathf.Min(stopGap, gap - scaled), MinLungeStopGap);
            lungeDistance = Mathf.Max(0f, gap - stopGap);
            focusCollider = combatFocus.CurrentCollider;
            PlayerCombatFocus focus = combatFocus;
            stopLunge = () => !focus.TryGetCurrentGap(side, out float liveGap) || liveGap <= stopGap;
        }

        // Closest a focus lunge may stop to the enemy's near side (m).
        private const float MinLungeStopGap = 0.3f;

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
            // The swing connected: its recovery can be cancelled into a jump or dash now.
            if (isAttacking)
                attackCancelableTime = Mathf.Min(attackCancelableTime, Time.time);

            // Hitting something in the air earns air time: fall slowly with the target.
            if (controller != null && playerState != null && !playerState.IsGrounded &&
                currentAttackDir != AttackDirection.Down && airHitHangDuration > 0f)
                controller.ApplyAirHang(airHitHangGravityScale, airHitHangDuration);

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
