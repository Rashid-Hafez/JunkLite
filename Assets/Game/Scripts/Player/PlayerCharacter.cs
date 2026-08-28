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
    [RequireComponent(typeof(StatusEffectHandler))]
    [DefaultExecutionOrder(5)]
    [RequireComponent(typeof(CinemachineImpulseSource))]
    public class PlayerCharacter : MonoBehaviour, IDamageReceiver, IGrabbable, IStatusEffectTarget
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
        [SerializeField, Min(0f)] private float defaultHitstunDuration = 0.25f;
        [SerializeField] private GameObject damageHitVFXPrefab;
        [SerializeField] private float damageHitVFXLifetime = 0.5f;

        [SerializeField] private float cameraShakeOnHit = 5f;
        [SerializeField] private CinemachineImpulseSource damageImpulseSource;

        public AttackDirection LastAttackDirection => _weaponManager?.CurrentAttackDirection ?? AttackDirection.Side;
        public bool JumpHeld => inputManager != null && inputManager.IsJumpHeld;
        public bool ReloadSceneOnDeathTemp => reloadSceneOnDeathTemp;
        public event Action<bool> OnCameraFollowRequested;
        public event Action OnActivated;
        public event Action OnDeactivated;
        public event Action OnRevived;

        private Vector3 damageVFXOffset;

        // Movement input
        float horizontalAxis = 0f;

        // Cached
        Collider[] _cachedColliders;
        private SpriteRenderer[] _spriteRenderers;
        GameInputManager inputManager;
        [HideInInspector] public AttributeManager attributes;
        protected Damageable damageable;
        private DamageShield damageShield;
        private StatusEffectHandler statusEffects;
        private PlayerHitReactionResolver hitReactionResolver;

        // Controller
        protected Character2D5Controller controller;
        public Character2D5Controller Controller => controller;

        // Player State
        protected PlayerState playerState;
        public PlayerState PlayerState => playerState;
        public PlayerState State => playerState;
        public CharacterStats Stats => baseStats;
        public StatusEffectHandler StatusEffects => statusEffects;
        public Attribute Health => attributes ? attributes.Health : null;
        public bool IsAlive => attributes ? attributes.IsAlive : true;
        public bool IsActive { get; private set; }

        // Grab state
        private PlayerGrabController grabController;
        private Coroutine grabRoutine;
        public bool IsGrabbed => grabController?.IsGrabbed ?? false;
        public bool CanBeGrabbed => grabController?.CanBeGrabbed ?? false;
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

            statusEffects = GetComponent<StatusEffectHandler>();
            if (statusEffects == null)
                statusEffects = gameObject.AddComponent<StatusEffectHandler>();
            statusEffects.BindTarget(this);
            playerState?.BindStatusEffects(statusEffects);
            hitReactionResolver = new PlayerHitReactionResolver(
                transform,
                controller,
                statusEffects,
                defaultHitstunDuration);
            grabController = new PlayerGrabController(this, playerState, controller);

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
            IsActive = false;
            CancelGrab(stopCoroutine: true);
            UnsubscribeFromInput();

            if (Controller != null)
            {
                Controller.SetLocomotionEnabled(false);
                Controller.StopAllVelocity();
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

            OnDeactivated?.Invoke();
        }

        public virtual void Activate()
        {
            IsActive = true;
            if (_cachedColliders != null)
                foreach (var c in _cachedColliders)
                    if (c) c.enabled = true;

            if (Controller != null)
                Controller.SetLocomotionEnabled(true);

            if (playerState != null)
            {
                playerState.SetAttacking(false);
                playerState.SetDashing(false);
                playerState.SetRolling(false);
                playerState.ApplyInvulnerability(reviveInvulnerability);
            }

            SubscribeToInput();
            OnActivated?.Invoke();
        }

        public void ReviveAt(Vector3 position)
        {
            if (attributes != null)
                attributes.RestoreHealthToMax();

            if (Controller != null)
            {
                Controller.TeleportTo(position);
                Controller.SetMovementInput(0f);
                Controller.SetLocomotionEnabled(false);
            }
            else
                transform.position = position;

            if (playerState != null)
            {
                playerState.ResetForRespawn();
                playerState.SetGrounded(true);
                playerState.SetVulnerable(false);
            }

            OnRevived?.Invoke();
        }

        //  void OnEnable() => SubscribeToInput();
        void OnDisable()
        {
            UnsubscribeFromInput();
            CancelGrab(stopCoroutine: true);
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

        public void ApplyStatusEffectSnapshot(StatusEffectSnapshot snapshot)
        {
            bool wasCrowdControlled = playerState != null && playerState.IsStunned;

            playerState?.ApplyStatusEffectSnapshot(snapshot);
            controller?.SetStatusMoveSpeedMultiplier(snapshot.MoveSpeedMultiplier);

            if (snapshot.IsCrowdControlled && !wasCrowdControlled)
                controller?.InterruptSpecialMovement();
        }

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

            // Periodic damage changes health without repeatedly granting i-frames
            // or replaying the full impact presentation.
            if (!request.IsTickDamage)
            {
                if (playerState != null && damageInvulnerability > 0f)
                    playerState.ApplyInvulnerability(damageInvulnerability);

                audioHandler?.PlayHurt();

                if (damageHitVFXPrefab != null)
                {
                    Vector3 spawnPos = transform.position + damageVFXOffset;
                    Instantiate(damageHitVFXPrefab, spawnPos, Quaternion.identity);
                }

                if (feedbackManager != null)
                    feedbackManager.DoCameraShake(damageImpulseSource, cameraShakeOnHit);
            }

            hitReactionResolver?.Resolve(request);

            return result;
        }
        #endregion

        // ====================================================================
        // GRAB (IGrabbable)
        // ====================================================================

        #region Grab

        public void GetGrabbed(GrabInfo info)
        {
            if (grabController == null || !grabController.TryBegin())
                return;

            grabRoutine = StartCoroutine(RunGrab(info));
        }

        private IEnumerator RunGrab(GrabInfo info)
        {
            yield return grabController.Execute(info);
            grabRoutine = null;
        }

        private void CancelGrab(bool stopCoroutine)
        {
            if (stopCoroutine && grabRoutine != null)
                StopCoroutine(grabRoutine);

            grabRoutine = null;
            grabController?.Cancel();
        }

        #endregion

        #region Death Handling

        protected virtual void HandleDeath()
        {
            IsActive = false;
            // Stop all movement and velocity on death
            if (controller != null)
            {
                controller.SetLocomotionEnabled(false);
                controller.StopAllVelocity();
            }

            statusEffects?.ClearAllEffects();
            // Do not stop the coroutine from inside a damage callback. Cancelling the
            // helper releases ownership immediately; the iterator exits on its next check.
            CancelGrab(stopCoroutine: false);

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

    /// <summary>
    /// Resolves the player's response after damage was accepted. It does not own
    /// health, Rigidbody state or timers; it delegates those responsibilities.
    /// </summary>
    internal sealed class PlayerHitReactionResolver
    {
        private readonly Transform targetTransform;
        private readonly Character2D5Controller controller;
        private readonly StatusEffectHandler statusEffects;
        private readonly float defaultHitstunDuration;

        public PlayerHitReactionResolver(
            Transform targetTransform,
            Character2D5Controller controller,
            StatusEffectHandler statusEffects,
            float defaultHitstunDuration)
        {
            this.targetTransform = targetTransform;
            this.controller = controller;
            this.statusEffects = statusEffects;
            this.defaultHitstunDuration = Mathf.Max(0f, defaultHitstunDuration);
        }

        public void Resolve(DamageRequest request)
        {
            HitReactionRequest reaction = request.HitReaction;
            if (!reaction.HasAnyReaction)
                return;

            float hitstunDuration = reaction.ResolveHitstunDuration(defaultHitstunDuration);
            if (hitstunDuration > 0f)
                statusEffects?.ApplyHitstun(hitstunDuration, request.Source);

            if (reaction.HasKnockback && controller != null)
            {
                Vector3 sourcePosition = request.Source != null
                    ? request.Source.transform.position
                    : targetTransform.position;
                controller.ApplyExternalKnockback(
                    sourcePosition,
                    reaction.KnockbackForce,
                    reaction.InterruptsActions);
            }
            else if (reaction.InterruptsActions && hitstunDuration <= 0f)
            {
                controller?.InterruptSpecialMovement();
            }
        }
    }

}
