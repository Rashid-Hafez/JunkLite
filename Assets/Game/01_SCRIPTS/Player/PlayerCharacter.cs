using System.Collections;
using UnityEngine;
using UnityEngine.InputSystem;

namespace junklite
{
    [RequireComponent(typeof(Character2D5Controller))]
    [RequireComponent(typeof(CharacterState))]
    [DefaultExecutionOrder(5)]
    public class PlayerCharacter : CharacterBase
    {
        [Header("Player Settings")]
        [SerializeField] private float attackRange = 1.5f;
        [SerializeField] private LayerMask enemyLayerMask = 1;

        [Header("VFX")]
        [SerializeField] private ParticleSystem particleJumpUp;
        [SerializeField] private ParticleSystem particleJumpDown;

        [Header("Dash VFX")]
        [SerializeField] private ParticleSystem particleDashBurst;
        [SerializeField] private TrailRenderer dashTrail;
        [SerializeField] private Transform feet;

        [Header("Respawn Settings")]
        [SerializeField] private float reviveInvulnerability = 1.25f;
        [SerializeField] private bool disableCollidersOnDeactivate = true;

        public bool JumpHeld => inputManager != null && inputManager.IsJumpHeld;

        // Movement input
        float horizontalAxis = 0f;

        // Cached
        Collider[] _cachedColliders;
        Rigidbody _rb;
        GameInputManager inputManager;

        // Non-alloc
        static readonly Collider[] overlapBuffer = new Collider[12];

        // attack coroutine
        Coroutine _attackCo;

        protected override void Awake()
        {
            base.Awake();

            inputManager = GameInputManager.Instance;
            _cachedColliders = GetComponentsInChildren<Collider>(includeInactive: true);
            _rb = GetComponent<Rigidbody>();

            Deactivate();
        }

        protected override void Start()
        {
            base.Start();

            if (CameraManager.Instance != null)
                CameraManager.Instance.SetPlayerTarget(transform);

            if (State != null)
            {
                State.OnGroundedChanged += OnGroundedStateChanged;
                State.OnMovingChanged += OnMovingStateChanged;
                State.OnDashingChanged += OnDashingStateChanged;
                State.OnAttackingChanged += OnAttackingStateChanged;
                State.OnStunnedChanged += OnStunnedStateChanged;
            }
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

                if (State != null)
                {
                    State.SetAttacking(false);
                    State.SetDashing(false);
                    State.SetRolling(false);
                    State.SetVulnerable(false);
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

            if (State != null)
            {
                State.SetAttacking(false);
                State.SetDashing(false);
                State.SetRolling(false);
                State.ApplyInvulnerability(reviveInvulnerability);
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

            if (State != null)
            {
                State.ResetForRespawn();
                State.SetGrounded(true);
                State.SetVulnerable(false);
            }
        }

        void OnEnable() => SubscribeToInput();
        void OnDisable() => UnsubscribeFromInput();

        // ====================================================================
        // INPUT
        // ====================================================================

        void Update()
        {
            HandleInput();

            //Temp Debug keys 
            if (Keyboard.current?.hKey.wasPressedThisFrame == true) Heal(20f);
            if (Keyboard.current?.tKey.wasPressedThisFrame == true) TakeDamage(new DamageInfo(15f, null));
            if (Keyboard.current?.yKey.wasPressedThisFrame == true) InstantDeath();
        }

        void FixedUpdate()
        {
            if (State != null && State.CanMove && Controller != null)
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
            if (State == null || Controller == null) return;

            // Only process when airborne and not wall sliding
            if (State.IsGrounded || State.IsWallSliding) return;

            float yVel = Controller.Velocity.y;

            // Transition from jumping to falling when velocity goes negative
            if (State.IsJumping && yVel < -0.1f)
            {
                State.SetJumping(false);
                State.SetFalling(true);
            }
            // If not jumping and going down, ensure falling is set
            else if (!State.IsJumping && yVel < -0.1f && !State.IsFalling)
            {
                State.SetFalling(true);
            }
            // If going up but not marked as jumping (e.g. launched by something), set jumping
            else if (yVel > 0.1f && !State.IsJumping && !State.IsWallJumping && !State.IsDoubleJumping)
            {
                // Only auto-set jumping if we're clearly going upward
                // This handles edge cases like external forces
            }
        }

        void HandleInput()
        {
            if (inputManager != null && State != null && State.CanMove && Controller != null)
                horizontalAxis = inputManager.MoveDirection.x;
            else
                horizontalAxis = 0f;
        }

        // ====================================================================
        // SUBSCRIBE / UNSUBSCRIBE
        // ====================================================================

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
            State?.SetGrounded(grounded);
        }

        void HandleMovementFromController(Vector3 move)
        {
            bool moving = Mathf.Abs(move.x) > 0.05f || Mathf.Abs(move.z) > 0.05f;
            State?.SetMoving(moving);
        }

        // =======================
        // NEW MOVEMENT STATE HOOKS
        // =======================

        void HandleWallSlideChanged(bool sliding)
        {
            State?.SetWallSliding(sliding);
        }

        void HandleWallJumped()
        {
            // Wall jump: first clear wall sliding, then set wall jumping, then set jumping
            // Order matters for proper state transitions!
            State?.SetWallSliding(false);  // Clear wall slide first
            State?.SetWallJumping(true);   // Mark as wall jumping
            State?.SetJumping(true);       // Now we're also jumping
            State?.SetFalling(false);      // Not falling while going up
            StartCoroutine(ResetWallJumpFlag());
        }

        IEnumerator ResetWallJumpFlag()
        {
            yield return new WaitForSeconds(0.20f);
            State?.SetWallJumping(false);
        }

        void HandleDoubleJump()
        {
            // Double jump: clear falling, set jumping and double jumping
            State?.SetFalling(false);       // Clear falling first
            State?.SetDoubleJumping(true);  // Mark as double jumping
            State?.SetJumping(true);        // Also set regular jumping
            StartCoroutine(ResetDoubleJumpFlag());
        }

        IEnumerator ResetDoubleJumpFlag()
        {
            yield return new WaitForSeconds(0.15f);
            State?.SetDoubleJumping(false);
        }

        void HandleJumpStarted()
        {
            // Ground jump: set jumping and clear falling
            State?.SetFalling(false);  // Clear falling first
            State?.SetJumping(true);   // Now set jumping

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
            State?.SetFalling(true);
        }

        void HandleFallEnded()
        {
            State?.SetFalling(false);
            State?.SetJumping(false);
            State?.SetWallJumping(false);
            State?.SetDoubleJumping(false);

            if (particleJumpDown != null)
            {
              if (feet) particleJumpDown.transform.position = feet.position;
                particleJumpDown.Play();
            }
        }

        #region Input Actions
        void OnJumpPressed()
        {
            if (State != null && State.CanJump && Controller != null)
                Controller.Jump();
        }

        void OnDashPressed()
        {
            if (State != null && State.CanDash && Controller != null)
                Controller.Dash();
        }


        void OnJumpReleased()
        {
            if (Controller != null)
                Controller.SetJumpHeld(false);  
        }
        #endregion

        // ====================================================================
        // DASH STATE
        // ====================================================================

        void HandleDashStarted()
        {
            State?.SetDashing(true);

            if (particleDashBurst != null)
            {
                if (feet) particleDashBurst.transform.position = feet.position;
                else particleDashBurst.transform.position = transform.position;
                particleDashBurst.Play();
            }

            if (dashTrail != null) dashTrail.emitting = true;
        }

        void HandleDashEnded()
        {
            State?.SetDashing(false);
            if (dashTrail != null) dashTrail.emitting = false;
        }

        // ====================================================================
        // ROLL STATE
        // ====================================================================

        void HandleRollStarted()
        {
            State?.SetRolling(true);
        }

        void HandleRollEnded()
        {
            State?.SetRolling(false);
        }

        // ====================================================================
        // COMBAT
        // ====================================================================

        void HandleAttackInput()
        {
            if (State != null && State.CanAttack)
                PerformAttack();
        }

        void PerformAttack()
        {
            if (State == null) return;

            State.SetAttacking(true);

            int count = Physics.OverlapSphereNonAlloc(
                transform.position,
                attackRange,
                overlapBuffer,
                enemyLayerMask
            );

            for (int i = 0; i < count; i++)
            {
                var enemyCol = overlapBuffer[i];
                if (!enemyCol) continue;

                var enemy = enemyCol.GetComponent<CharacterBase>();
                if (enemy != null)
                {
                    var dmg = new DamageInfo(Stats.damage, gameObject);
                    enemy.TakeDamage(dmg);
                }

                overlapBuffer[i] = null;
            }

            if (_attackCo != null) StopCoroutine(_attackCo);
            _attackCo = StartCoroutine(EndAttackAfter(0.3f));
        }

        IEnumerator EndAttackAfter(float t)
        {
            yield return new WaitForSeconds(t);
            State?.SetAttacking(false);
            _attackCo = null;
        }

        // ====================================================================
        // STATE EVENT HANDLERS
        // ====================================================================

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

        public override void TakeDamage(DamageInfo info)
        {
            if (State != null && !State.CanTakeDamage) return;

            base.TakeDamage(info);

            if (info.Source != null && Controller != null)
            {
                Vector3 dir = (transform.position - info.Source.transform.position).normalized;
                Controller.AddForce(dir * 15f, ForceMode.Impulse);
            }

            if (State != null)
                State.ApplyStun(0.1f);
        }

        protected override void HandleDeath()
        {
            base.HandleDeath();
            UnsubscribeFromInput();
        }

        protected override void OnDestroy()
        {
            base.OnDestroy();

            if (State != null)
            {
                State.OnGroundedChanged -= OnGroundedStateChanged;
                State.OnMovingChanged -= OnMovingStateChanged;
                State.OnDashingChanged -= OnDashingStateChanged;
                State.OnAttackingChanged -= OnAttackingStateChanged;
                State.OnStunnedChanged -= OnStunnedStateChanged;
            }

            UnsubscribeFromInput();
        }

        void OnDrawGizmosSelected()
        {
            Gizmos.color = Color.red;
            Gizmos.DrawWireSphere(transform.position, attackRange);
        }
    }
}
