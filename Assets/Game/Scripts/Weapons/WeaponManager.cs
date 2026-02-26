using UnityEngine;
using System;
using System.Collections;
using System.Collections.Generic;
using static UnityEngine.EventSystems.EventTrigger;

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

        // Weapon slots
        private WeaponInstance weaponSlot1;
        private WeaponInstance weaponSlot2;
        private WorldWeaponPickup storedPickup1;
        private WorldWeaponPickup storedPickup2;

        // Combat mode
        private bool isModCombat;

        // Attack state
        private bool isAttacking;
        private int activeWeaponSlot;            // 0=fists, 1=slot1, 2=slot2
        private WeaponInstance activeWeapon;      // null for fists
        private CombatState activeCombatState;    // always set during attack
        private WeaponData activeWeaponData;      // always set during attack
        private int lastAttackedSlot = -1;
        private AttackDirection currentAttackDir;
        private WeaponData.ComboStep currentStep;
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

            // Route mod pickups to inventory for now - ModManager will handle this later
            var modPickup = other.GetComponent<WorldModPickup>();
            if (modPickup != null && modPickup.gameObject.activeSelf)
            {
                // Disable immediately to prevent double-pickup from overlapping colliders
                modPickup.gameObject.SetActive(false);

                var modInstance = new ModInstance(modPickup.modData);

                // Try auto-equip to ModManager first
                var modManager = GetComponent<ModManager>();
                if (modManager != null && modManager.TryEquipMod(modInstance))
                {
                    Destroy(modPickup.gameObject);
                    return;
                }

                // No free mod slot - store in inventory
                var inventory = GetComponent<InventoryComponent>();
                if (inventory != null)
                {
                    inventory.AddMod(modInstance);
                    Destroy(modPickup.gameObject);
                    return;
                }

                // Neither worked - re-enable the pickup
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

            // Weapons can break, fists can't
            if (weaponSlot != 0)
            {
                var weapon = GetWeaponForSlot(weaponSlot);
                if (weapon == null || weapon.IsBroken) return;
            }

            // Validate combat mode
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

        /// <summary>
        /// Returns the CombatState for a slot. Fists (0) use fistCombat, weapons use their internal state.
        /// </summary>
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

        /// <summary>
        /// Returns the WeaponData for a slot. Fists (0) use fistWeaponData, weapons use their own data.
        /// </summary>
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

            // Reset combo on both since slot context changed
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

            // Air attack gating
            if (playerState != null && !playerState.IsGrounded && !playerState.CanAirAttack)
            {
                Log("Air attack blocked");
                return;
            }

            // Reset combo on previous weapon if switching
            if (lastAttackedSlot >= 0 && lastAttackedSlot != slot)
            {
                GetCombatStateForSlot(lastAttackedSlot)?.ResetCombo();
                Log($"Weapon switch: reset combo on slot {lastAttackedSlot}");
            }

            bool isGrounded = playerState == null || playerState.IsGrounded;
            if (!combat.TryGetComboStep(dir, isGrounded, out var step, out int comboIndex, out string animName))
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

            // Set attack state
            isAttacking = true;
            activeWeaponSlot = slot;
            activeWeapon = GetWeaponForSlot(slot);  // null for fists
            activeCombatState = combat;
            activeWeaponData = data;
            lastAttackedSlot = slot;
            currentAttackDir = dir;
            currentStep = step;
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

            ExecuteAttack(dir, step);
        }

        private void ExecuteAttack(AttackDirection dir, WeaponData.ComboStep step)
        {
            Transform anchor = GetAttackTransform(dir);
            if (anchor == null) return;

            StartCoroutine(CoAttackDelay(dir, step, anchor));
        }

        private IEnumerator CoAttackDelay(AttackDirection dir, WeaponData.ComboStep step, Transform anchor)
        {
            yield return new WaitForSeconds(delayBeforeAttack);

            // Kick off the push as a separate coroutine so it doesn't block hit detection.
            // The push moves the player, but the hitbox origin is snapshotted HERE — before
            // any movement has happened — so sliding into an enemy after the swing never
            // awards a hit.
            StartCoroutine(CoApplyAttackPush(dir, step));
            ApplyAttackGravityOverride(step);

            Vector3 hitOrigin = ResolveHitOrigin(dir);

            // Resolve piercing: ComboStep default OR runtime override on the weapon instance.
            // Mods/abilities can set WeaponInstance.PiercingOverride = true at any time.
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
                        DealDamageToAll(hitResult.allTargets, step);
                    else if (hitResult.target != null)
                        DealDamage(hitResult.target, step);

                    ApplyRecoil(dir, step);
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
                    ApplyRecoil(dir, step);
                }

                yield return null;
            }
        }

        public void OnAttackAnimationComplete()
        {
            Log($"Attack complete - slot {activeWeaponSlot}, {currentAttackDir}, combo {currentComboIndex}");

            activeCombatState?.OnAttackComplete(currentAttackDir, currentAttackGrounded);

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
        /// Applies a clean, snappy velocity lunge in the attack direction, then stops it.
        /// Unlike AddForce/Impulse, this directly sets velocity for a fixed duration so the
        /// feel is consistent regardless of prior player speed, and it stops sharply rather
        /// than decaying over many frames. Duration is set per ComboStep so each hit in a
        /// combo can have a distinct lunge character.
        /// </summary>
        private IEnumerator CoApplyAttackPush(AttackDirection dir, WeaponData.ComboStep step)
        {
            if (playerRb == null) yield break;

            // Build the push velocity for this direction
            Vector3 pushVelocity = Vector3.zero;

            if (dir == AttackDirection.Side && Mathf.Abs(step.forwardImpulse) > 0f)
            {
                pushVelocity.x = step.forwardImpulse * Facing;
            }
            else if (dir != AttackDirection.Side && Mathf.Abs(step.verticalImpulse) > 0f)
            {
                pushVelocity.y = dir == AttackDirection.Down
                    ? -step.verticalImpulse
                    : step.verticalImpulse;
            }

            if (pushVelocity.sqrMagnitude <= 0f) yield break;

            // Per-step duration, fall back to the manager-level default
            float duration = step.forwardImpulseDuration > 0f
                ? step.forwardImpulseDuration
                : defaultPushDuration;

            // Zero the pushed axis first so the lunge is always the same speed
            // regardless of what the player was doing — this is what makes it feel
            // deliberate and game-like rather than floaty
            var vel = playerRb.linearVelocity;
            if (pushVelocity.x != 0f) vel.x = pushVelocity.x;
            if (pushVelocity.y != 0f) vel.y = pushVelocity.y;
            playerRb.linearVelocity = vel;

            yield return new WaitForSeconds(duration);

            // Hard-stop only the axis we pushed — vertical physics stays untouched
            var endVel = playerRb.linearVelocity;
            if (pushVelocity.x != 0f) endVel.x = 0f;
            if (pushVelocity.y != 0f) endVel.y = 0f;
            playerRb.linearVelocity = endVel;
        }

        private void ApplyAttackGravityOverride(WeaponData.ComboStep step)
        {
            if (controller == null) return;
            if (playerState != null && !playerState.IsGrounded && step.airGravityMultiplier > 0f)
                controller.SetGravityMultiplierOverride(step.airGravityMultiplier);
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
            public Collider target;           // used when isPiercing = false
            public Collider[] allTargets;     // used when isPiercing = true
            public Vector3 point;
        }

        /// <summary>
        /// Performs an OverlapSphere at the given origin.
        /// Single-target mode returns the closest enemy. Piercing mode returns all enemies.
        /// Note: origin is always the snapshotted position from CoAttackDelay, never a live
        /// anchor — this prevents the player lunging into an out-of-range enemy mid-swing.
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

        private void DealDamage(Collider target, WeaponData.ComboStep step)
        {
            var damageable = target.GetComponent<IDamageable>()
                          ?? target.GetComponentInParent<IDamageable>();

            if (damageable == null || !damageable.IsAlive) return;

            float damage = activeWeaponData != null ? activeWeaponData.baseDamage : 10f;
            if (step.damageMultiplier > 0f)
                damage *= step.damageMultiplier;

            var knockback = activeWeaponData != null
                ? activeWeaponData.knockbackForce
                : new Vector2(8f, 4f);

            var damageInfo = new DamageInfo(damage, playerTransform.gameObject, DamageType.Physical, knockback);
            bool damageDealt = damageable.TakeDamage(damageInfo);

            if (damageDealt)
            {
                var enemy = target.GetComponent<EnemyCharacter>() ?? target.GetComponentInParent<EnemyCharacter>();
                OnEnemyHit?.Invoke(enemy);

                // Consume weapon durability (fists don't break)
                if (activeWeaponSlot != 0 && activeWeapon != null)
                {
                    if (activeWeapon.ConsumeDurability())
                        HandleWeaponBroken(activeWeaponSlot);
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

        /// <summary>
        /// Damages all enemies in the hit result (piercing path).
        /// Durability is consumed once for the whole swing, not per enemy.
        /// </summary>
        private void DealDamageToAll(Collider[] targets, WeaponData.ComboStep step)
        {
            bool anyHit = false;

            foreach (var target in targets)
            {
                var damageable = target.GetComponent<IDamageable>()
                              ?? target.GetComponentInParent<IDamageable>();

                if (damageable == null || !damageable.IsAlive) continue;

                float damage = activeWeaponData != null ? activeWeaponData.baseDamage : 10f;
                if (step.damageMultiplier > 0f)
                    damage *= step.damageMultiplier;

                var knockback = activeWeaponData != null
                    ? activeWeaponData.knockbackForce
                    : new Vector2(8f, 4f);

                var damageInfo = new DamageInfo(damage, playerTransform.gameObject, DamageType.Physical, knockback);
                bool damageDealt = damageable.TakeDamage(damageInfo);

                if (damageDealt)
                {
                    anyHit = true;
                    var enemy = target.GetComponent<EnemyCharacter>() ?? target.GetComponentInParent<EnemyCharacter>();
                    OnEnemyHit?.Invoke(enemy);

                    // VFX per enemy hit
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

            // Single durability tick for the entire swing regardless of how many enemies were hit
            if (anyHit && activeWeaponSlot != 0 && activeWeapon != null)
            {
                if (activeWeapon.ConsumeDurability())
                    HandleWeaponBroken(activeWeaponSlot);
            }
        }

        private void PlayHitFeedback()
        {
            if (FeedbackManager.Instance != null)
                FeedbackManager.Instance.DoHitFeedback(impulseSource, enemyHitHitstopDuration, enemyHitShakeForce);
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

            // Hide weapon unless in mod combat
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

            // Force back to regular if both slots empty
            if (isModCombat && weaponSlot1 == null && weaponSlot2 == null)
                ExitModCombat();
        }

        #endregion

        #region Helpers

        private Vector3 ResolveHitOrigin(AttackDirection dir)
        {
            switch (dir)
            {
                case AttackDirection.Side:
                    return new Vector3(
                        playerTransform.position.x + Facing * activeWeaponData.attackRange,
                        sideAttack.position.y,
                        playerTransform.position.z
                    );

                case AttackDirection.Up:
                    return new Vector3(
                        upAttack.position.x,
                        playerTransform.position.y + activeWeaponData.attackRange,
                        playerTransform.position.z
                    );

                case AttackDirection.Down:
                    return new Vector3(
                        downAttack.position.x,
                        playerTransform.position.y - activeWeaponData.attackRange,
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

        private void ApplyRecoil(AttackDirection dir, WeaponData.ComboStep step)
        {
            if (playerRb == null || dir != AttackDirection.Side) return;
            if (Mathf.Abs(step.hitRecoil) <= 0f) return;
            playerRb.AddForce(Vector3.right * -Facing * step.hitRecoil, ForceMode.Impulse);
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