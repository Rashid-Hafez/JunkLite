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
        [SerializeField] private ParticleSystem particleDashBurst;   // one-shot on dash start
        [SerializeField] private TrailRenderer dashTrail;            // enabled while dashing
        [SerializeField] private Transform feet;                     // optional VFX anchor

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

            // Listen to STATE changes (for VFX/SFX hooks if you want)
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
            // Stop inputs & event wiring
            UnsubscribeFromInput();

            // Stop movement
            if (Controller != null)
            {
                Controller.CanMove = false;
                if (_rb != null) _rb.linearVelocity = Vector3.zero; // no drift
            }

            // Disable colliders if requested
            if (disableCollidersOnDeactivate && _cachedColliders != null)
            {
                foreach (var c in _cachedColliders)
                    if (c) c.enabled = false;
            }

            // Lock combat & damage
            if (State != null)
            {
                State.SetAttacking(false);
                State.SetDashing(false);
                State.SetRolling(false);
                State.SetVulnerable(false); // invulnerable while deactivated
            }

            // Stop ongoing attack timer if any
            if (_attackCo != null) { StopCoroutine(_attackCo); _attackCo = null; }
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
                horizontalAxis = inputManager.MoveDirection.x; // normalized; controller multiplies by speed
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
                inputManager.OnRoll += OnRollPressed;
            }

            if (Controller != null)
            {
                // --- Controller → State wiring (single source of truth) ---
                Controller.OnGroundedStateChanged += HandleGroundedFromController;
                Controller.OnMovementChanged += HandleMovementFromController;
                Controller.OnDashStarted += HandleDashStarted;
                Controller.OnDashEnded += HandleDashEnded;
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
                Controller.OnGroundedStateChanged -= HandleGroundedFromController;
                Controller.OnMovementChanged -= HandleMovementFromController;
                Controller.OnDashStarted -= HandleDashStarted;
                Controller.OnDashEnded -= HandleDashEnded;
                Controller.OnRollStarted -= HandleRollStarted;
                Controller.OnRollEnded -= HandleRollEnded;
            }
        }

        // ===== Controller → State adapters =====
        void HandleGroundedFromController(bool grounded)
        {
            State?.SetGrounded(grounded);
        }

        void HandleMovementFromController(Vector3 move)
        {
            // simple threshold to avoid flicker
            bool moving = Mathf.Abs(move.x) > 0.05f || Mathf.Abs(move.z) > 0.05f;
            State?.SetMoving(moving);
        }

        // ===== Input Handlers =====
        void OnJumpPressed()
        {
            if (State != null && State.CanJump && Controller != null)
            {
                Controller.Jump();
                if (particleJumpUp != null)
                {
                    if (feet) particleJumpUp.transform.position = feet.position;
                    particleJumpUp.Play();
                }
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
                Controller.TryStartRoll();
        }

        void HandleRollStarted()
        {
            if (State != null) State.SetRolling(true);
        }

        void HandleRollEnded()
        {
            if (State != null) State.SetRolling(false);
        }

        // Dash VFX + state sync (events come from Controller)
        void HandleDashStarted()
        {
            if (State != null) State.SetDashing(true);

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
            if (State != null) State.SetDashing(false);
            if (dashTrail != null) dashTrail.emitting = false;
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

            if (_attackCo != null) StopCoroutine(_attackCo);
            _attackCo = StartCoroutine(EndAttackAfter(0.3f));
        }

        IEnumerator EndAttackAfter(float t)
        {
            yield return new WaitForSeconds(t);
            if (State != null) State.SetAttacking(false);
            _attackCo = null;
        }

        #region State Event Handlers (optional hooks)
        void OnGroundedStateChanged(bool grounded)
        {
            if (grounded) OnLanding();
            else OnFall();
        }

        void OnMovingStateChanged(bool moving) { /* VFX/SFX if needed */ }
        void OnDashingStateChanged(bool dashing) { /* UI or camera effects */ }
        void OnAttackingStateChanged(bool attacking) { /* combo windows, etc. */ }
        void OnStunnedStateChanged(bool stunned) { /* UI feedback */ }
        #endregion

        public void OnFall()
        {
            // Only VFX here (Animator is driven by AnimationController)
        }

        public void OnLanding()
        {
            if (particleJumpDown != null)
            {
                if (feet) particleJumpDown.transform.position = feet.position;
                particleJumpDown.Play();
            }
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
 