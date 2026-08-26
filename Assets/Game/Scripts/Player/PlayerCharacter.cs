using System;
using SkeletonGhost = Spine.Unity.Examples.SkeletonGhost;
using System.Collections;
using Unity.Cinemachine;
using UnityEngine;
using UnityEngine.InputSystem;

namespace junklite
{
    [RequireComponent(typeof(Character2D5Controller))]
    [RequireComponent(typeof(PlayerState))]
    [RequireComponent(typeof(AttributeManager))]
    [RequireComponent(typeof(Damageable))]
    [DefaultExecutionOrder(5)]
    [RequireComponent(typeof(CinemachineImpulseSource))]
    public class PlayerCharacter : MonoBehaviour, IDamageReceiver, IGrabbable
    {
        [Header("Config")]
        [SerializeField] protected CharacterStats baseStats;

        [Header("Audio")]
        [SerializeField] private PlayerSoundProfile soundProfile;

        [Header("VFX")]
        [SerializeField] private ParticleSystem particleJumpUp;
        [SerializeField] private ParticleSystem particleJumpDown;
        [SerializeField] private SkeletonGhost skeletonGhost;

        [Header("Dash VFX")]
        [SerializeField] private ParticleSystem particleDashBurst;
        [SerializeField] private TrailRenderer dashTrail;
        [SerializeField] private Transform feet;

        [Header("Respawn Settings")]
        [SerializeField] private float reviveInvulnerability = 1.25f;
        [SerializeField] private bool disableCollidersOnDeactivate = true;
        [SerializeField] private bool reloadSceneOnDeathTemp = false;

        [Header("Damage Feedback")]
        [SerializeField] private float damageInvulnerability = 0.5f;
        [SerializeField] private GameObject damageHitVFXPrefab;
        [SerializeField] private float damageHitVFXLifetime = 0.5f;

        [SerializeField] private float cameraShakeOnHit = 5f;
        [SerializeField] private CinemachineImpulseSource damageImpulseSource;

        public AttackDirection LastAttackDirection => _weaponManager?.CurrentAttackDirection ?? AttackDirection.Side;
        public bool JumpHeld => inputManager != null && inputManager.IsJumpHeld;
        public bool ReloadSceneOnDeathTemp => reloadSceneOnDeathTemp;
        public event Action<bool> OnCameraFollowRequested;

        private Vector3 damageVFXOffset;

        // Movement input
        float horizontalAxis = 0f;

        // Cached
        Collider[] _cachedColliders;
        private SpriteRenderer[] _spriteRenderers;
        Rigidbody _rb;
        GameInputManager inputManager;
        [HideInInspector] public AttributeManager attributes;
        protected Damageable damageable;
        private DamageShield damageShield;

        // Controller
        protected Character2D5Controller controller;
        public Character2D5Controller Controller => controller;

        // Player State
        protected PlayerState playerState;
        public PlayerState PlayerState => playerState;
        public PlayerState State => playerState;
        public CharacterStats Stats => baseStats;
        public Attribute Health => attributes ? attributes.Health : null;
        public bool IsAlive => attributes ? attributes.IsAlive : true;

        // Grab state
        private bool isGrabbed = false;
        public bool IsGrabbed => isGrabbed;
        public bool CanBeGrabbed => IsAlive && !isGrabbed;
        public PlayerSoundProfile SoundProfile => soundProfile;

        // Attached Comps
        private WeaponManager _weaponManager;
        private ModManager _modManager;
        private ParryHandler parryHandler;
        private bool inputSubscribed;

        private FeedbackManager feedbackManager;
        private PlayerAudioHandler audioHandler;


        public Vector3 VFXCenter => transform.position + damageVFXOffset;
        protected virtual void Awake()
        {
            controller = GetComponent<Character2D5Controller>();
            playerState = GetComponent<PlayerState>();
            parryHandler = GetComponent<ParryHandler>();
            attributes = GetComponent<AttributeManager>();
            damageable = GetComponent<Damageable>();
            TryGetComponent(out damageShield);

            if (attributes != null && baseStats != null)
                attributes.Initialize(baseStats);

            if (damageable != null)
                damageable.Bind(baseStats, attributes, playerState);

            if (attributes != null)
                attributes.OnDeath += HandleDeath;

            skeletonGhost = GetComponentInChildren<SkeletonGhost>();
            if (skeletonGhost == null)
            {
                Debug.LogError("SkeletonGhost component not found on player character!", this);
            }

            inputManager = GameInputManager.Instance;
            _cachedColliders = GetComponentsInChildren<Collider>(includeInactive: true);
            _rb = GetComponent<Rigidbody>();
            _spriteRenderers = GetComponentsInChildren<SpriteRenderer>(includeInactive: true);
            audioHandler = GetComponent<PlayerAudioHandler>();

            Collider col = GetComponent<Collider>();
            damageVFXOffset = col != null ? col.bounds.center - transform.position : Vector3.up;

            Deactivate();
        }


        protected virtual void Start()
        {
            // Pipe controller events into CharacterState flags
            ConnectController();

            // Apply movement stats to controller
            UpdateControllerStats();

            ////// SCREENSHAKE AND FEEDBACK, VIBRATION CONTROLLER, FLASH VFX ETC
            feedbackManager = FeedbackManager.Instance;

            if (damageImpulseSource == null)
                damageImpulseSource = GetComponent<CinemachineImpulseSource>();

            _weaponManager = GetComponent<WeaponManager>();
            _modManager = GetComponent<ModManager>();

            if (playerState != null)
            {
                playerState.OnGroundedChanged += OnGroundedStateChanged;
                playerState.OnMovingChanged += OnMovingStateChanged;
                playerState.OnDashingChanged += OnDashingStateChanged;
                playerState.OnAttackingChanged += OnAttackingStateChanged;
                playerState.OnStunnedChanged += OnStunnedStateChanged;
            }
        }

        protected virtual void OnDestroy()
        {
            if (attributes != null)
                attributes.OnDeath -= HandleDeath;

            if (controller != null && playerState != null)
            {
                controller.OnGroundedStateChanged -= playerState.SetGrounded;
                controller.OnDashStarted -= OnDashStarted;
                controller.OnDashEnded -= OnDashEnded;
                controller.OnMovementChanged -= OnMovementChanged;
            }

            if (playerState != null)
            {
                playerState.OnGroundedChanged -= OnGroundedStateChanged;
                playerState.OnMovingChanged -= OnMovingStateChanged;
                playerState.OnDashingChanged -= OnDashingStateChanged;
                playerState.OnAttackingChanged -= OnAttackingStateChanged;
                playerState.OnStunnedChanged -= OnStunnedStateChanged;
            }

            UnsubscribeFromInput();
        }

        // --- Controller -> State wiring
        private void ConnectController()
        {
            if (controller == null || playerState == null) return;

            controller.OnGroundedStateChanged += playerState.SetGrounded;
            controller.OnDashStarted += OnDashStarted;
            controller.OnDashEnded += OnDashEnded;
            controller.OnMovementChanged += OnMovementChanged;
        }


        private void OnDashStarted() => playerState.SetDashing(true);
        private void OnDashEnded() => playerState.SetDashing(false);
        private void OnMovementChanged(Vector3 move)
        {
            // Use X/Z magnitude for 2.5D movement
            // 0.1f threshold => compare squared to avoid sqrt
            bool isMoving = (move.x * move.x + move.z * move.z) > 0.01f;
            playerState.SetMoving(isMoving);
        }

        // Apply baseStats movement into controller
        protected virtual void UpdateControllerStats()
        {
            if (controller == null || baseStats == null) return;

            controller.MoveSpeed = baseStats.moveSpeed;

            // set optional fields if they exist
            SetControllerProperty("JumpForce", baseStats.jumpForce);
            SetControllerProperty("DashForce", baseStats.dashForce);
            SetControllerProperty("DashDuration", baseStats.dashDuration);
        }

        public Attribute GetAttribute(AttributeType type) => attributes ? attributes.Get(type) : null;

        public void Heal(float amount)
        {
            attributes?.Heal(amount);
        }

        public DamageResult Kill()
        {
            if (!IsAlive || attributes?.Health == null)
                return DamageResult.Rejected(DamageOutcome.Dead, 0f);

            return ReceiveDamage(DamageRequest.Forced(attributes.Health.Current));
        }

        protected void InstantDeath()
        {
            var result = Kill();
            if (result.WasApplied)
                Debug.Log($"{gameObject.name} died instantly!");
        }

        private void SetControllerProperty(string prop, object value)
        {
            var p = controller.GetType().GetProperty(prop);
            if (p != null && p.CanWrite) p.SetValue(controller, value);
        }

        // ====================================================================
        // DEACTIVATE & ACTIVATE
        // ====================================================================

        public virtual void Deactivate()
        {
            UnsubscribeFromInput();

            if (Controller != null)
            {
                Controller.CanMove = false;
                if (_rb != null) _rb.linearVelocity = Vector3.zero;
            }

            if (disableCollidersOnDeactivate && _cachedColliders != null)
            {
                foreach (var c in _cachedColliders)
                    if (c) c.enabled = false;

                if (playerState != null)
                {
                    playerState.SetAttacking(false);
                    playerState.SetDashing(false);
                    playerState.SetRolling(false);
                    playerState.SetVulnerable(false);
                }
            }
        }

        public virtual void Activate()
        {
            if (_cachedColliders != null)
                foreach (var c in _cachedColliders)
                    if (c) c.enabled = true;

            if (Controller != null)
                Controller.CanMove = true;

            if (playerState != null)
            {
                playerState.SetAttacking(false);
                playerState.SetDashing(false);
                playerState.SetRolling(false);
                playerState.ApplyInvulnerability(reviveInvulnerability);
            }

            SubscribeToInput();
        }

        public void ReviveAt(Vector3 position)
        {
            if (attributes != null)
                attributes.RestoreHealthToMax();

            if (Controller != null)
            {
                Controller.TeleportTo(position);
                Controller.SetMovementInput(0f);
                Controller.CanMove = false;
            }
            else
                transform.position = position;

            if (playerState != null)
            {
                playerState.ResetForRespawn();
                playerState.SetGrounded(true);
                playerState.SetVulnerable(false);
            }
        }

        //  void OnEnable() => SubscribeToInput();
        void OnDisable()
        {
            UnsubscribeFromInput();
        }

        // ====================================================================
        // INPUT
        // ====================================================================

        void Update() //PLEASE REMOVE THIS
        {
            HandleInput();

            //Temp Debug keys 
            if (Keyboard.current?.hKey.wasPressedThisFrame == true) Heal(20f);
            if (Keyboard.current?.tKey.wasPressedThisFrame == true) ReceiveDamage(new DamageRequest(15f));
            if (Keyboard.current?.yKey.wasPressedThisFrame == true) InstantDeath();
        }

        void FixedUpdate()
        {
            if (playerState != null && playerState.CanMove && Controller != null)
            {
                Controller.SetMovementInput(horizontalAxis);
                Controller.SetJumpHeld(JumpHeld);
            }

            // if we're airborne but our ledge probe is active, try snapping down
            if (controller != null && playerState != null && !playerState.IsGrounded)
            {
                if (controller.LedgeDetected)
                {
                    if (controller.TrySnapToGround())
                    {
                        // landing logic already handled in TrySnapToGround
                        playerState.SetGrounded(true);
                    }
                }
            }

            // Handle velocity-based state transitions
            UpdateAirborneStates();

        }

        /// <summary>
        /// Updates jumping/falling states based on velocity when airborne.
        /// </summary>
        void UpdateAirborneStates()
        {
            if (playerState == null || Controller == null) return;

            // Only process when airborne and not wall sliding
            if (playerState.IsGrounded || playerState.IsWallSliding) return;

            float yVel = Controller.Velocity.y;

            // Transition from jumping to falling when velocity goes negative
            if (playerState.IsJumping && yVel < -0.1f)
            {
                playerState.SetJumping(false);
                playerState.SetFalling(true);
            }
            // If not jumping and going down, ensure falling is set
            else if (!playerState.IsJumping && yVel < -0.1f && !playerState.IsFalling)
            {
                playerState.SetFalling(true);
            }
            // If going up but not marked as jumping (e.g. launched by something), set jumping
            else if (yVel > 0.1f && !playerState.IsJumping && !playerState.IsWallJumping && !playerState.IsDoubleJumping)
            {
                // Only auto-set jumping if we're clearly going upward
                // This handles edge cases like external forces
            }
        }

        void HandleInput()
        {
            if (inputManager != null && playerState != null && playerState.CanMove && Controller != null)
                horizontalAxis = inputManager.MoveDirection.x;
            else
                horizontalAxis = 0f;

        }


        // SUBSCRIBE / UNSUBSCRIBE
        void SubscribeToInput()
        {
            if (inputSubscribed)
                return;

            if (inputManager == null) inputManager = GameInputManager.Instance;

            if (inputManager != null)
            {
                inputManager.OnJump += OnJumpPressed;
                inputManager.OnJumpReleased += OnJumpReleased;
                inputManager.OnAttack += HandleAttackInput;
                inputManager.OnDash += OnDashPressed;

                
                inputManager.OnCombatModeToggle += HandleCombatModeToggle;
                inputManager.OnWeapon1Attack += HandleWeapon1Attack;
                inputManager.OnWeapon2Attack += HandleWeapon2Attack;
                inputManager.OnModActivate1 += HandleModActivate1;
                inputManager.OnModActivate2 += HandleModActivate2;
                inputManager.OnModActivate3 += HandleModActivate3;
                inputManager.OnModActivate4 += HandleModActivate4;
            }

            if (Controller != null)
            {
                // Base movement updates
                Controller.OnGroundedStateChanged += HandleGroundedFromController;
                Controller.OnMovementChanged += HandleMovementFromController;
                Controller.OnDashStarted += HandleDashStarted;
                Controller.OnDashEnded += HandleDashEnded;

                // === NEW MOVEMENT STATES ===
                Controller.OnWallSlideChanged += HandleWallSlideChanged;
                Controller.OnLedgeDetectedChanged += HandleLedgeDetectedChanged;
                Controller.OnWallJumped += HandleWallJumped;
                Controller.OnDoubleJumpPerformed += HandleDoubleJump;
                Controller.OnJumpStarted += HandleJumpStarted;
                Controller.OnFallStarted += HandleFallStarted;
                Controller.OnFallEnded += HandleFallEnded;
            }

            inputSubscribed = true;
        }

        void UnsubscribeFromInput()
        {
            if (!inputSubscribed)
                return;

            if (inputManager != null)
            {
                inputManager.OnJump -= OnJumpPressed;
                inputManager.OnJumpReleased -= OnJumpReleased;
                inputManager.OnAttack -= HandleAttackInput;
                inputManager.OnDash -= OnDashPressed;

                // TODO: Unwire when added to GameInputManager
                inputManager.OnCombatModeToggle -= HandleCombatModeToggle;
                inputManager.OnWeapon1Attack -= HandleWeapon1Attack;
                inputManager.OnWeapon2Attack -= HandleWeapon2Attack;
                inputManager.OnModActivate1 -= HandleModActivate1;
                inputManager.OnModActivate2 -= HandleModActivate2;
                inputManager.OnModActivate3 -= HandleModActivate3;
                inputManager.OnModActivate4 -= HandleModActivate4;
            }

            if (Controller != null)
            {
                Controller.OnGroundedStateChanged -= HandleGroundedFromController;
                Controller.OnMovementChanged -= HandleMovementFromController;
                Controller.OnDashStarted -= HandleDashStarted;
                Controller.OnDashEnded -= HandleDashEnded;

                // New movement unsubscriptions
                Controller.OnWallSlideChanged -= HandleWallSlideChanged;
                Controller.OnLedgeDetectedChanged -= HandleLedgeDetectedChanged;
                Controller.OnWallJumped -= HandleWallJumped;
                Controller.OnDoubleJumpPerformed -= HandleDoubleJump;
                Controller.OnJumpStarted -= HandleJumpStarted;
                Controller.OnFallStarted -= HandleFallStarted;
                Controller.OnFallEnded -= HandleFallEnded;
            }

            inputSubscribed = false;
        }

        // ====================================================================
        // CONTROLLER → STATE ADAPTERS
        // ====================================================================

        void HandleGroundedFromController(bool grounded)
        {
            playerState?.SetGrounded(grounded);
        }

        void HandleMovementFromController(Vector3 move)
        {
            bool moving = Mathf.Abs(move.x) > 0.05f || Mathf.Abs(move.z) > 0.05f;
            playerState?.SetMoving(moving);
        }

        // =======================
        // NEW MOVEMENT STATE HOOKS
        // =======================

        void HandleWallSlideChanged(bool sliding)
        {
            playerState?.SetWallSliding(sliding);
        }

        void HandleLedgeDetectedChanged(bool detected)
        {
            playerState?.SetLedgeDetected(detected);
        }

        void HandleWallJumped()
        {
            // Wall jump: first clear wall sliding, then set wall jumping, then set jumping
            // Order matters for proper state transitions!
            playerState?.SetWallSliding(false);  // Clear wall slide first
            playerState?.SetWallJumping(true);   // Mark as wall jumping
            playerState?.SetJumping(true);       // Now we're also jumping
            playerState?.SetFalling(false);      // Not falling while going up
            StartCoroutine(ResetWallJumpFlag());
        }

        IEnumerator ResetWallJumpFlag()
        {
            yield return new WaitForSeconds(0.20f);
            playerState?.SetWallJumping(false);
        }

        void HandleDoubleJump()
        {
            // Refund one air attack if scheduled (e.g. pogo hit) so player can pogo again after double jump
            playerState?.TryRefundAirAttackAfterDoubleJump();

            // Double jump: clear falling, set jumping and double jumping
            playerState?.SetFalling(false);       // Clear falling first
            playerState?.SetDoubleJumping(true);  // Mark as double jumping
            playerState?.SetJumping(true);        // Also set regular jumping
            StartCoroutine(ResetDoubleJumpFlag());
        }

        IEnumerator ResetDoubleJumpFlag()
        {
            yield return new WaitForSeconds(0.15f);
            playerState?.SetDoubleJumping(false);
        }

        void HandleJumpStarted()
        {
            // Ground jump: set jumping and clear falling
            playerState?.SetFalling(false);  // Clear falling first
            playerState?.SetJumping(true);   // Now set jumping

            if (particleJumpUp != null)
            {
                if (feet) particleJumpUp.transform.position = feet.position;
                particleJumpUp.Play();
            }
        }

        void HandleFallStarted()
        {
            // Note: This fires when leaving the ground, NOT when starting to fall downward
            // Do NOT clear IsJumping here - jumping transitions to falling based on velocity
            // IsJumping will be cleared when we land or when velocity goes negative
            playerState?.SetFalling(true);
        }

        void HandleFallEnded()
        {
            playerState?.SetFalling(false);
            playerState?.SetJumping(false);
            playerState?.SetWallJumping(false);
            playerState?.SetDoubleJumping(false);

            if (particleJumpDown != null)
            {
                if (feet) particleJumpDown.transform.position = feet.position;
                particleJumpDown.Play();
            }
        }

        #region Input Actions
        void OnJumpPressed()
        {
            if (playerState != null && playerState.CanJump && Controller != null)
            {
                Controller.SetJumpHeld(true);
                Controller.Jump();
            }
        }

        void OnDashPressed()
        {
            if (playerState != null && playerState.CanDash && Controller != null)
                Controller.Dash();
        }


        void OnJumpReleased()
        {
            if (Controller != null)
                Controller.SetJumpHeld(false);
        }
        #endregion

        // DASH STATE
        void HandleDashStarted()
        {
            playerState?.SetDashing(true);

            if (particleDashBurst != null)
            {
                if (feet) particleDashBurst.transform.position = feet.position;
                else particleDashBurst.transform.position = transform.position;
                particleDashBurst.Play();
            }

            if (dashTrail != null) dashTrail.emitting = true;

            SkeletonGhostActivation(true);
        }

        void HandleDashEnded()
        {
            playerState?.SetDashing(false);
            if (dashTrail != null) dashTrail.emitting = false;
            SkeletonGhostActivation(false);
        }

        void HandleAttackInput()
        {
            if (playerState == null || !playerState.CanAttack || _weaponManager == null)
                return;

            // Regular mode: fists (slot 0). Mod combat uses weapon-specific inputs.
            if (!_weaponManager.IsModCombat)
                _weaponManager.Attack(0, inputManager.MoveDirection, playerState.IsGrounded);
        }

        void HandleWeapon1Attack()
        {
            if (playerState == null || !playerState.CanAttack || _weaponManager == null)
                return;

            if (_weaponManager.IsModCombat)
                _weaponManager.Attack(1, inputManager.MoveDirection, playerState.IsGrounded);
        }

        void HandleWeapon2Attack()
        {
            if (playerState == null || !playerState.CanAttack || _weaponManager == null)
                return;

            if (_weaponManager.IsModCombat)
                _weaponManager.Attack(2, inputManager.MoveDirection, playerState.IsGrounded);
        }

        void HandleCombatModeToggle()
        {
            _weaponManager?.TryToggleCombatMode();
        }

        void HandleModActivate1() => _modManager?.TryActivateMod(0);
        void HandleModActivate2() => _modManager?.TryActivateMod(1);
        void HandleModActivate3() => _modManager?.TryActivateMod(2);
        void HandleModActivate4() => _modManager?.TryActivateMod(3);


        public void RequestCameraFollow(bool follow)
        {
            OnCameraFollowRequested?.Invoke(follow);
        }

        public void SetVisible(bool visible)
        {
            // Hide/show player sprites
            if (_spriteRenderers != null)
            {
                foreach (var sr in _spriteRenderers)
                {
                    if (sr != null)
                        sr.enabled = visible;
                }
            }

            // Hide/show weapon
            if (_weaponManager != null)
                _weaponManager.SetWeaponVisible(visible);
        }
        public Vector3 GetGroundPosition()
        {
            return feet != null ? feet.position : transform.position;
        }

        void OnGroundedStateChanged(bool grounded)
        {
            if (grounded) OnLanding();
            else OnFall();
        }

        void OnMovingStateChanged(bool moving) { }
        void OnDashingStateChanged(bool dashing) { }
        void OnAttackingStateChanged(bool attacking) { }
        void OnStunnedStateChanged(bool stunned) { }

        public void OnFall() { }
        public void OnLanding() { }

        // ====================================================================
        // DAMAGE
        // ====================================================================

        #region Damage
        public DamageResult ReceiveDamage(DamageRequest request)
        {
            float originallyRequested = request.Amount;

            if (damageable == null)
                return DamageResult.Rejected(DamageOutcome.Invalid, originallyRequested);

            // Validate the target/source/team before player-specific defenses.
            if (!damageable.TryValidateRequest(request, out var rejection, checkDefensiveState: false))
                return rejection;

            if (!request.BypassesDefenses)
            {
                if (parryHandler != null && parryHandler.HandleIncomingHit(request.Source))
                    return DamageResult.Rejected(DamageOutcome.Parried, originallyRequested);

                if (playerState != null && (!playerState.CanTakeDamage || playerState.IsInvincible))
                    return DamageResult.Rejected(DamageOutcome.Invulnerable, originallyRequested);

                if (damageShield == null)
                    TryGetComponent(out damageShield);

                var shield = damageShield;
                if (shield != null && shield.IsActive)
                {
                    float remainder = shield.Absorb(request.Amount);
                    if (remainder <= 0f)
                        return DamageResult.Rejected(DamageOutcome.Blocked, originallyRequested);

                    request = request.WithAmount(remainder);
                }
            }

            DamageResult result = damageable.ReceiveDamage(request)
                .WithRequestedDamage(originallyRequested);
            if (!result.WasApplied)
                return result;

            if (!IsAlive)
                return result;

            if (playerState != null && damageInvulnerability > 0f)
                playerState.ApplyInvulnerability(damageInvulnerability);

            audioHandler?.PlayHurt(); // TODO - Maybe move this to audio handler? IDK

            if (damageHitVFXPrefab != null)
            {
                Vector3 spawnPos = transform.position + damageVFXOffset;
                GameObject vfx = Instantiate(damageHitVFXPrefab, spawnPos, Quaternion.identity);
            }

            if (feedbackManager != null)
                feedbackManager.DoCameraShake(damageImpulseSource, cameraShakeOnHit);

            if (request.Source != null && Controller != null && request.KnockbackForce.sqrMagnitude > 0f)
            {
                Vector3 dir = (transform.position - request.Source.transform.position).normalized;
                Vector3 knockback = new Vector3(
                    dir.x * request.KnockbackForce.x,
                    request.KnockbackForce.y,
                    dir.z * request.KnockbackForce.x
                );
                Controller.AddForce(knockback, ForceMode.VelocityChange);
            }

            if (playerState != null && !request.IsTickDamage)
                playerState.ApplyStun(0.25f);

            return result;
        }
        #endregion

        // ====================================================================
        // GRAB (IGrabbable)
        // ====================================================================

        #region Grab

        public void GetGrabbed(GrabInfo info)
        {
            if (!CanBeGrabbed) return;
            StartCoroutine(HandleGrab(info));
        }

        private IEnumerator HandleGrab(GrabInfo info)
        {
            isGrabbed = true;

            // Stun player for entire grab + throw recovery
            if (playerState != null)
                playerState.ApplyStun(info.Duration + 0.5f);

            // Disable controller movement and stop velocity
            if (Controller != null)
            {
                Controller.CanMove = false;
                Controller.StopAllVelocity();
            }

            // Disable rigidbody physics during grab
            bool wasKinematic = false;
            if (_rb != null)
            {
                wasKinematic = _rb.isKinematic;
                _rb.isKinematic = true;
                _rb.linearVelocity = Vector3.zero;
            }

            Transform enemyTransform = info.Source?.transform;

            // Hold player attached to enemy for grab duration
            float timer = 0f;
            while (timer < info.Duration)
            {
                timer += Time.deltaTime;

                // Keep player attached to enemy at grabOffset
                if (enemyTransform != null)
                {
                    transform.position = enemyTransform.position + info.GrabOffset;
                }

                // Keep velocity zero during grab
                if (Controller != null)
                    Controller.StopAllVelocity();

                yield return null;
            }

            // Re-enable rigidbody physics before throw
            if (_rb != null)
            {
                _rb.isKinematic = wasKinematic;
            }

            // Apply throw damage
            if (info.ThrowDamage > 0f)
                ReceiveDamage(DamageRequest.Forced(info.ThrowDamage, info.Source));

            // Apply throw force
            if (Controller != null && info.ThrowForce.sqrMagnitude > 0f)
            {
                Vector3 throwKnockback = new Vector3(
                    info.ThrowDirection * info.ThrowForce.x,
                    info.ThrowForce.y,
                    0f
                );

                Controller.AddForce(throwKnockback, ForceMode.VelocityChange);
                Debug.Log($"Player thrown! Direction: {info.ThrowDirection}, Force: {throwKnockback}");
            }

            // Re-enable controller after throw
            if (Controller != null)
            {
                Controller.CanMove = true;
            }

            isGrabbed = false;
        }

        #endregion

        #region Death Handling

        protected virtual void HandleDeath()
        {
            // Stop all movement and velocity on death
            if (controller != null)
            {
                controller.CanMove = false;
                controller.StopAllVelocity();
            }

            isGrabbed = false;

            Debug.Log($"{gameObject.name} has died!");
            UnsubscribeFromInput();
        }

        void OnDrawGizmosSelected()
        {
            // Gizmos.color = Color.red;
            // Gizmos.DrawWireSphere(transform.position, attackRange);
        }

        #endregion

        #region Animation

        public void SkeletonGhostActivation(bool activate)
        {
            if (skeletonGhost == null)
            {
                Debug.LogWarning("[PlayerCharacter] SkeletonGhost is null!", this);
                return;
            }

            // Ensure the GameObject is active
            if (!skeletonGhost.gameObject.activeInHierarchy)
            {
                Debug.LogWarning("[PlayerCharacter] SkeletonGhost GameObject is inactive, activating it...", this);
                skeletonGhost.gameObject.SetActive(true);
            }

            // Ensure the component is enabled
            skeletonGhost.enabled = true;

            // Ensure it's initialized (in case Start() hasn't run yet)
            skeletonGhost.Initialize(false);

            // Toggle ghosting
            skeletonGhost.ghostingEnabled = activate;

            //Debug.Log($"[PlayerCharacter] SkeletonGhost ghostingEnabled set to: {skeletonGhost.ghostingEnabled}, Component enabled: {skeletonGhost.enabled}, GameObject active: {skeletonGhost.gameObject.activeInHierarchy}", this);
        }

        #endregion
    }

}
