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

        [Header("References")]
        [SerializeField] private Animator animator;
        [SerializeField] private ParticleSystem particleJumpUp;
        [SerializeField] private ParticleSystem particleJumpDown;

        // Input tracking (one-frame)
        float horizontalMove = 0f;
        bool jump, dash;

        // Systems
        GameInputManager inputManager;

        // Animator hashes
        static readonly int HashSpeed = Animator.StringToHash("Speed");
        static readonly int HashIsGrounded = Animator.StringToHash("IsGrounded");
        static readonly int HashIsWall = Animator.StringToHash("IsWallSliding");
        static readonly int HashIsDashing = Animator.StringToHash("IsDashing");
        static readonly int HashIsJumping = Animator.StringToHash("IsJumping");
        static readonly int HashIsAttacking = Animator.StringToHash("IsAttacking");
        static readonly int HashIsStunned = Animator.StringToHash("IsStunned");

        // Non-alloc overlap buffer
        static readonly Collider[] overlapBuffer = new Collider[12];

        protected override void Awake()
        {
            base.Awake();
            inputManager = GameInputManager.Instance;
        }

        void Start()
        {
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

        void OnEnable() => SubscribeToInput();
        void OnDisable() => UnsubscribeFromInput();

        void Update()
        {
            HandleInput();
            UpdateAnimations();

            if (jump && State != null && State.CanJump && Controller != null)
            {
                Controller.Jump();
                if (particleJumpUp != null) particleJumpUp.Play();
            }

            if (dash && State != null && State.CanDash && Controller != null)
                Controller.Dash();

            // reset one-frame inputs
            jump = false;
            dash = false;

            // debug keys (optional)
            if (Keyboard.current?.hKey.wasPressedThisFrame == true) Heal(20f);
            if (Keyboard.current?.tKey.wasPressedThisFrame == true) TakeDamage(new DamageInfo(15f, null));
            if (Keyboard.current?.yKey.wasPressedThisFrame == true) InstantDeath();
        }

        void FixedUpdate()
        {
            if (State != null && State.CanMove && Controller != null)
            {
                float normalized = Controller.MoveSpeed > 0f ? (horizontalMove / Controller.MoveSpeed) : 0f;
                Controller.SetMovementInput(normalized);
            }
        }

        void HandleInput()
        {
            if (inputManager != null && State != null && State.CanMove && Controller != null)
                horizontalMove = inputManager.MoveDirection.x * Controller.MoveSpeed;
            else
                horizontalMove = 0f;
        }

        void UpdateAnimations()
        {
            if (animator == null || State == null) return;

            animator.SetFloat(HashSpeed, Mathf.Abs(horizontalMove));
            animator.SetBool(HashIsGrounded, State.IsGrounded);
            animator.SetBool(HashIsWall, false);
            animator.SetBool(HashIsDashing, State.IsDashing);
            animator.SetBool(HashIsJumping, !State.IsGrounded);
            animator.SetBool(HashIsAttacking, State.IsAttacking);
            animator.SetBool(HashIsStunned, State.IsStunned);
        }

        void SubscribeToInput()
        {
            if (inputManager == null) return;
            inputManager.OnJump += HandleJumpInput;
            inputManager.OnAttack += HandleAttackInput;
            inputManager.OnDash += HandleDashInput;
        }

        void UnsubscribeFromInput()
        {
            if (inputManager == null) return;
            inputManager.OnJump -= HandleJumpInput;
            inputManager.OnAttack -= HandleAttackInput;
            inputManager.OnDash -= HandleDashInput;
        }

        void HandleJumpInput() { if (State != null && State.CanJump) jump = true; }
        void HandleAttackInput() { if (State != null && State.CanAttack) PerformAttack(); }
        void HandleDashInput() { if (State != null && State.CanDash) dash = true; }

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
        void OnGroundedStateChanged(bool grounded) { if (grounded) OnLanding(); else OnFall(); }
        void OnMovingStateChanged(bool moving) { }
        void OnDashingStateChanged(bool dashing) { }
        void OnAttackingStateChanged(bool attacking) { }
        void OnStunnedStateChanged(bool stunned) { }
        #endregion

        public void OnFall()
        {
            if (animator != null)
                animator.SetBool(HashIsJumping, true);
        }

        public void OnLanding()
        {
            if (animator != null)
                animator.SetBool(HashIsJumping, false);

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

        void OnDrawGizmosSelected()
        {
            Gizmos.color = Color.red;
            Gizmos.DrawWireSphere(transform.position, attackRange);
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
        }
    }
}
