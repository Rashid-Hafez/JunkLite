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
    [DefaultExecutionOrder(5)]
    [RequireComponent(typeof(CinemachineImpulseSource))]
    public class PlayerCharacter : CharacterBase, IGrabbable
    {
        [Header("Player Settings")]
        [SerializeField] private float attackRange = 1.5f;
        [SerializeField] private LayerMask enemyLayerMask = 1;
        [Header("Audio")]
        [SerializeField] private PlayerSoundProfile soundProfile;

        [Header("Attack Settings")]
        [SerializeField] private float attackFacingLockDuration = 0.25f;

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

        [Header("Damage Feedback")]
        [SerializeField] private float damageInvulnerability = 0.5f;
        [SerializeField] private GameObject damageHitVFXPrefab;
        [SerializeField] private float damageHitVFXLifetime = 0.5f;
        [SerializeField] private float cameraShakeOnHit = 5f;
        [SerializeField] private CinemachineImpulseSource damageImpulseSource;

        public AttackDirection LastAttackDirection => _weaponManager?.CurrentAttackDirection ?? AttackDirection.Side;
        public bool JumpHeld => inputManager != null && inputManager.IsJumpHeld;
        public event Action<bool> OnCameraFollowRequested;

        // Movement input
        float horizontalAxis = 0f;

        // Cached
        Collider[] _cachedColliders;
        private SpriteRenderer[] _spriteRenderers;
        Rigidbody _rb;
        GameInputManager inputManager;

        // Controller
        protected Character2D5Controller controller;
        public Character2D5Controller Controller => controller;

        // Player State
        protected PlayerState playerState;
        public PlayerState PlayerState => playerState;
       
        // attack coroutine
        Coroutine _attackCo;

        // Grab state
        private bool isGrabbed = false;
        public bool IsGrabbed => isGrabbed;
        public bool CanBeGrabbed => IsAlive && !isGrabbed;
        public PlayerSoundProfile SoundProfile => soundProfile;

        // Attached Comps
        private WeaponManager _weaponManager;

        private FeedbackManager feedbackManager;

        protected override void Awake()
        {
            base.Awake();

            controller = GetComponent<Character2D5Controller>();
            playerState = GetComponent<PlayerState>();

            skeletonGhost = GetComponentInChildren<SkeletonGhost>();
            if (skeletonGhost == null)
            {
                Debug.LogError("SkeletonGhost component not found on player character!", this);
            }

            inputManager = GameInputManager.Instance;
            _cachedColliders = GetComponentsInChildren<Collider>(includeInactive: true);
            _rb = GetComponent<Rigidbody>();
            _spriteRenderers = GetComponentsInChildren<SpriteRenderer>(includeInactive: true);

            Deactivate();
        }


        protected override void Start()
        {
            base.Start();

            // Pipe controller events into CharacterState flags
            ConnectController();

            // Apply movement stats to controller
            UpdateControllerStats();

            ////// SCREENSHAKE AND FEEDBACK, VIBRATION CONTROLLER, FLASH VFX ETC
            feedbackManager = FeedbackManager.Instance;

            if (damageImpulseSource == null)
                damageImpulseSource = GetComponent<CinemachineImpulseSource>();

            _weaponManager = GetComponent<WeaponManager>();

            if (playerState != null)
            {
                playerState.OnGroundedChanged += OnGroundedStateChanged;
                playerState.OnMovingChanged += OnMovingStateChanged;
                playerState.OnDashingChanged += OnDashingStateChanged;
                playerState.OnAttackingChanged += OnAttackingStateChanged;
                playerState.OnStunnedChanged += OnStunnedStateChanged;
            }
        }

        protected override void OnDestroy()
        {
            base.OnDestroy();

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

        private void SetControllerProperty(string prop, object value)
        {
            var p = controller.GetType().GetProperty(prop);
            if (p != null && p.CanWrite) p.SetValue(controller, value);
        }

        // ====================================================================
        // DEACTIVATE & ACTIVATE
        // ====================================================================

        public override void Deactivate()
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

                if (_attackCo != null) { StopCoroutine(_attackCo); _attackCo = null; }
            }
        }

        public override void Activate()
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
            if (Keyboard.current?.tKey.wasPressedThisFrame == true) TakeDamage(new DamageInfo(15f, null));
            if (Keyboard.current?.yKey.wasPressedThisFrame == true) InstantDeath();
        }

        void FixedUpdate()
        {
            if (playerState != null && playerState.CanMove && Controller != null)
            {
                Controller.SetMovementInput(horizontalAxis);
                Controller.SetJumpHeld(JumpHeld);
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
            if (inputManager == null) inputManager = GameInputManager.Instance;

            if (inputManager != null)
            {
                inputManager.OnJump += OnJumpPressed;
                inputManager.OnJumpReleased += OnJumpReleased;
                inputManager.OnAttack += HandleAttackInput;
                inputManager.OnDash += OnDashPressed;
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
                Controller.OnWallJumped += HandleWallJumped;
                Controller.OnDoubleJumpPerformed += HandleDoubleJump;
                Controller.OnJumpStarted += HandleJumpStarted;
                Controller.OnFallStarted += HandleFallStarted;
                Controller.OnFallEnded += HandleFallEnded;
            }
        }

        void UnsubscribeFromInput()
        {
            if (inputManager != null)
            {
                inputManager.OnJump -= OnJumpPressed;
                inputManager.OnJumpReleased -= OnJumpReleased;
                inputManager.OnAttack -= HandleAttackInput;
                inputManager.OnDash -= OnDashPressed;
            }

            if (Controller != null)
            {
                Controller.OnGroundedStateChanged -= HandleGroundedFromController;
                Controller.OnMovementChanged -= HandleMovementFromController;
                Controller.OnDashStarted -= HandleDashStarted;
                Controller.OnDashEnded -= HandleDashEnded;

                // New movement unsubscriptions
                Controller.OnWallSlideChanged -= HandleWallSlideChanged;
                Controller.OnWallJumped -= HandleWallJumped;
                Controller.OnDoubleJumpPerformed -= HandleDoubleJump;
                Controller.OnJumpStarted -= HandleJumpStarted;
                Controller.OnFallStarted -= HandleFallStarted;
                Controller.OnFallEnded -= HandleFallEnded;
            }
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

            // Pass raw input - WeaponManager handles direction resolution
            _weaponManager.Attack(inputManager.MoveDirection, playerState.IsGrounded);
        }


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

        public override bool TakeDamage(DamageInfo info)
        {
            if (playerState != null && !playerState.CanTakeDamage)
                return false;

            bool damageDealt = base.TakeDamage(info);

            if (!damageDealt)
                return false;

            // If this hit killed us, don't apply post-hit effects (stun, knockback, etc.)
            // Death animation is already triggered via AttributeManager.OnDeath -> CharacterState.HandleDeathForward
            if (!IsAlive)
                return true;

            // i-frames on hit
            if (playerState != null && damageInvulnerability > 0f)
                playerState.ApplyInvulnerability(damageInvulnerability);

            // Optional hit VFX
            if (damageHitVFXPrefab != null)
            {
                Vector3 spawnPos = feet != null ? feet.position : transform.position;
                GameObject vfx = Instantiate(damageHitVFXPrefab, spawnPos, Quaternion.identity);
                if (damageHitVFXLifetime > 0f)
                    Destroy(vfx, damageHitVFXLifetime);
            }

            if (feedbackManager != null)
                feedbackManager.DoCameraShake(damageImpulseSource, cameraShakeOnHit);

            // Apply knockback
            if (info.Source != null && Controller != null && info.KnockbackForce.sqrMagnitude > 0f)
            {
                Vector3 dir = (transform.position - info.Source.transform.position).normalized;
                Vector3 knockback = new Vector3(
                    dir.x * info.KnockbackForce.x,
                    info.KnockbackForce.y,
                    dir.z * info.KnockbackForce.x
                );
                Controller.AddForce(knockback, ForceMode.VelocityChange);
            }

            // Stun duration covers knockback time
            if (playerState != null)
                playerState.ApplyStun(0.25f);

            return true;
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
            if (info.ThrowDamage > 0f && attributes != null)
                attributes.Health.Remove(info.ThrowDamage);

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

        protected override void HandleDeath()
        {
            // Stop all movement and velocity on death
            if (controller != null)
            {
                controller.CanMove = false;
                controller.StopAllVelocity();
            }

            isGrabbed = false;

            base.HandleDeath();
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
        
        Debug.Log($"[PlayerCharacter] SkeletonGhost ghostingEnabled set to: {skeletonGhost.ghostingEnabled}, Component enabled: {skeletonGhost.enabled}, GameObject active: {skeletonGhost.gameObject.activeInHierarchy}", this);
    }

    #endregion
    }
    
}