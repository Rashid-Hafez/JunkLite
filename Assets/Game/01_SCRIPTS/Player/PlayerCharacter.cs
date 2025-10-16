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

        [Header("Respawn Settings")]
        [SerializeField] private float reviveInvulnerability = 1.25f;
        [SerializeField] private bool disableCollidersOnDeactivate = true;

        // Movement input (axis)
        float horizontalAxis = 0f;

        // Cached components
        Collider[] _cachedColliders;
        Rigidbody _rb; // optional, used if present

        // Systems
        GameInputManager inputManager;

        // Non-alloc overlap buffer
        static readonly Collider[] overlapBuffer = new Collider[12];

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

        #region Overrides

        public override void Deactivate()
        {
            // Stop inputs
            UnsubscribeFromInput();

            // Stop movement
            if (Controller != null)
            {
                Controller.CanMove = false;
                //clear velocity so we don't drift while deactivated
                if (_rb != null) _rb.linearVelocity = Vector3.zero;
            }

            // Disable colliders if requested
            if (disableCollidersOnDeactivate && _cachedColliders != null)
                foreach (var c in _cachedColliders)
                    if (c) c.enabled = false;

            // Lock combat & damage
            if (State != null)
            {
                State.SetAttacking(false);
                State.SetDashing(false);
                State.SetRolling(false);
                State.SetVulnerable(false); // invulnerable while deactivated
            }
        }

        public override void Activate()
        {
            // Re-enable colliders
            if (_cachedColliders != null)
                foreach (var c in _cachedColliders)
                    if (c) c.enabled = true;

            // Allow movement again
            if (Controller != null)
                Controller.CanMove = true;

            // Re-enable combat, give short i-frames
            if (State != null)
            {
                State.SetAttacking(false);
                State.SetDashing(false);
                State.SetRolling(false);
                State.ApplyInvulnerability(reviveInvulnerability);
            }

            if (animationController != null)
                animationController.ResetGraph();

            // Subscribe to input last
            SubscribeToInput();
        }

        public void ReviveAt(Vector3 position)
        {
            // --- Reset health / core stats ---
            if (attributes != null)
            {
                attributes.RestoreHealthToMax();
                // restore armor etc. if needed
            }

            if (Controller != null)
            {
                Controller.TeleportTo(position);
                Controller.SetMovementInput(0f);
                Controller.CanMove = false; // stays off until Activate()
            }
            else
            {
                transform.position = position;
            }

            if (State != null)
            {
                State.ResetForRespawn();
                State.SetGrounded(true);
                State.SetVulnerable(false);
            }
        }

        #endregion

        void OnEnable() => SubscribeToInput();
        void OnDisable() => UnsubscribeFromInput();

        void Update()
        {
            HandleInput();

            // debug keys (optional)
            if (Keyboard.current?.hKey.wasPressedThisFrame == true) Heal(20f);
            if (Keyboard.current?.tKey.wasPressedThisFrame == true) TakeDamage(new DamageInfo(15f, null));
            if (Keyboard.current?.yKey.wasPressedThisFrame == true) InstantDeath();
        }

        void FixedUpdate()
        {
            if (State != null && State.CanMove && Controller != null)
            {
                // Character2D5Controller expects normalized axis (-1..1)
                Controller.SetMovementInput(horizontalAxis);
            }
        }

        void HandleInput()
        {
            if (inputManager != null && State != null && State.CanMove && Controller != null)
                horizontalAxis = inputManager.MoveDirection.x; // keep normalized; controller multiplies by speed
            else
                horizontalAxis = 0f;
        }

        void SubscribeToInput()
        {
            if (inputManager == null) inputManager = GameInputManager.Instance;
            if (inputManager != null)
            {
                inputManager.OnJump += OnJumpPressed;
                inputManager.OnAttack += HandleAttackInput;
                inputManager.OnDash += OnDashPressed;
                inputManager.OnRoll += OnRollPressed;  // requires GameInputManager event
                inputManager.OnDroneAttack += OnDroneAttackPressed; // subscribe to drone attack input
            }

            if (Controller != null)
            {
                Controller.OnRollStarted += HandleRollStarted;
                Controller.OnRollEnded += HandleRollEnded;
            }
        }
        
        void UnsubscribeFromInput()
        {
            if (inputManager != null)
            {
                inputManager.OnJump -= OnJumpPressed;
                inputManager.OnAttack -= HandleAttackInput;
                inputManager.OnDash -= OnDashPressed;
                inputManager.OnRoll -= OnRollPressed;
            }

            if (Controller != null)
            {
                Controller.OnRollStarted += HandleRollStarted; 
                Controller.OnRollEnded += HandleRollEnded;
            }
        }

        // ===== Input Handlers (event-driven) =====
        void OnJumpPressed()
        {
            if (State != null && State.CanJump && Controller != null)
            {
                Controller.Jump();
                if (particleJumpUp != null) particleJumpUp.Play();
            }
        }

        void OnDashPressed()
        {
            if (State != null && State.CanDash && Controller != null)
                Controller.Dash();
        }


        void OnRollPressed()
        {
            if (Controller != null && State != null && State.CanRoll && Controller.CanMove)
                Controller.TryStartRoll(); // Controller decides ground vs air
        }

        void HandleRollStarted() { if (State != null) State.SetRolling(true); }
        void HandleRollEnded() { if (State != null) State.SetRolling(false); }

        void OnDroneAttackPressed()
        {
            // Invoke the drone attack event in CharacterState
            if (State != null)
            {
                State.DroneAttacking = true;
            }
        }


        // ===== Combat =====
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

                overlapBuffer[i] = null; // clear reference
            }

            Invoke(nameof(EndAttack), 0.3f);
        }

        void EndAttack()
        {
            if (State != null)
                State.SetAttacking(false);
        }

        #region State Event Handlers
        void OnGroundedStateChanged(bool grounded)
        {
            if (grounded) OnLanding();
            else OnFall();
        }

        void OnMovingStateChanged(bool moving) { /* hook VFX/SFX if needed */ }
        void OnDashingStateChanged(bool dashing) { /* dash enter/exit hooks */ }
        void OnAttackingStateChanged(bool attacking) { /* combo windows, etc. */ }
        void OnStunnedStateChanged(bool stunned) { /* UI feedback, etc. */ }
        #endregion

        public void OnFall()
        {
            // Only VFX here (Animator is driven by AnimationController)
        }

        public void OnLanding()
        {
            if (particleJumpDown != null)
                particleJumpDown.Play();
        }

        public override void TakeDamage(DamageInfo info)
        {
            if (State != null && !State.CanTakeDamage) return;

            base.TakeDamage(info);

            // knockback
            if (info.Source != null && Controller != null)
            {
                Vector3 dir = (transform.position - info.Source.transform.position).normalized;
                Controller.AddForce(dir * 15f, ForceMode.Impulse);
            }

            // short hit-stun
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

            // extra safety
            UnsubscribeFromInput();
        }

        void OnDrawGizmosSelected()
        {
            Gizmos.color = Color.red;
            Gizmos.DrawWireSphere(transform.position, attackRange);
        }
    }
}
