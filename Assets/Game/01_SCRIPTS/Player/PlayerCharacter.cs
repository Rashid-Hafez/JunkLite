using UnityEngine;

namespace junklite
{
    [RequireComponent(typeof(Character2D5Controller))]
    [DefaultExecutionOrder(5)]
    public class PlayerCharacter : CharacterBase
    {
        [Header("Player Settings")]
        [SerializeField] private float runSpeed = 40f;
        [SerializeField] private float attackRange = 1.5f;
        [SerializeField] private LayerMask enemyLayerMask = 1;

        [Header("References")]
        public Animator animator;
        public ParticleSystem particleJumpUp;
        public ParticleSystem particleJumpDown;

        // Input tracking
        private float horizontalMove = 0f;
        private bool jump = false;
        private bool dash = false;

        private GameInputManager inputManager;
        private bool wasGrounded = true;

        protected override void Awake()
        {
            base.Awake();
            inputManager = GameInputManager.Instance;
        }

        private void Start()
        {
            if (CameraManager.Instance != null)
            {
                CameraManager.Instance.SetPlayerTarget(transform);
            }

            // Subscribe to controller events for effects/animations
            if (controller != null)
            {
                controller.OnGroundedStateChanged += OnGroundedStateChanged;
                controller.OnMovementChanged += OnMovementChanged;
                controller.OnDashStarted += OnDashStarted;
                controller.OnDashEnded += OnDashEnded;
            }

            // Set initial movement speed
            if (controller != null)
            {
                controller.MoveSpeed = runSpeed;
            }
        }

        private void OnEnable()
        {
            SubscribeToInput();
        }

        private void OnDisable()
        {
            UnsubscribeFromInput();
        }

        private void Update()
        {
            HandleInput();
            UpdateAnimations();

            // Handle jump input
            if (jump && IsAlive)
            {
                controller.Jump();
                if (particleJumpUp != null)
                    particleJumpUp.Play();
            }

            // Handle dash input
            if (dash && IsAlive)
            {
                controller.Dash();
            }

            // Reset one-frame inputs
            jump = false;
            dash = false;
        }

        private void FixedUpdate()
        {
            if (IsAlive && controller != null)
            {
                // Convert run speed to normalized input for the controller
                float normalizedInput = horizontalMove / runSpeed;
                controller.SetMovementInput(normalizedInput);
            }
        }

        private void HandleInput()
        {
            if (inputManager != null && IsAlive)
            {
                horizontalMove = inputManager.MoveDirection.x * runSpeed;
            }
            else
            {
                horizontalMove = 0f;
            }
        }

        private void UpdateAnimations()
        {
            if (animator != null && controller != null)
            {
                animator.SetFloat("Speed", Mathf.Abs(horizontalMove));
                animator.SetBool("IsGrounded", controller.IsGrounded);
                animator.SetBool("IsWallSliding", false);
                animator.SetBool("IsDashing", controller.IsDashing);
                animator.SetBool("IsJumping", !controller.IsGrounded);
            }
        }

        private void SubscribeToInput()
        {
            if (inputManager != null)
            {
                inputManager.OnJump += HandleJumpInput;
                inputManager.OnAttack += HandleAttackInput;
                inputManager.OnDash += HandleDashInput;

                Debug.Log("Subscribed to input");
            }
        }

        private void UnsubscribeFromInput()
        {
            if (inputManager != null)
            {
                inputManager.OnJump -= HandleJumpInput;
                inputManager.OnAttack -= HandleAttackInput;
                inputManager.OnDash -= HandleDashInput;
            }
        }

        private void HandleJumpInput()
        {
            if (IsAlive)
                jump = true;
        }

        private void HandleAttackInput()
        {
            if (IsAlive)
                PerformAttack();
        }

        private void HandleDashInput()
        {
            if (IsAlive)
                dash = true;
        }

        private void PerformAttack()
        {
            Debug.Log($"{Stats.characterName} performed an attack with {Stats.damage} damage!");

            // Detect enemies in attack range using 3D physics
            Collider[] enemies = Physics.OverlapSphere(
                transform.position,
                attackRange,
                enemyLayerMask
            );

            foreach (var enemy in enemies)
            {
                var enemyCharacter = enemy.GetComponent<CharacterBase>();
                if (enemyCharacter != null)
                {
                    DamageInfo damageInfo = new DamageInfo(Stats.damage, gameObject);
                    enemyCharacter.TakeDamage(damageInfo);
                }
            }
        }

        private void OnGroundedStateChanged(bool grounded)
        {
            if (grounded && !wasGrounded)
            {
                OnLanding();
            }
            else if (!grounded && wasGrounded)
            {
                OnFall();
            }

            wasGrounded = grounded;
        }

        private void OnMovementChanged(Vector3 movement)
        {
            // You can add movement-based effects here if needed
        }

        private void OnDashStarted()
        {
            Debug.Log($"{Stats.characterName} started dashing!");
            // Add dash start effects here (particles, sound, etc.)
        }

        private void OnDashEnded()
        {
            Debug.Log($"{Stats.characterName} finished dashing!");
            // Add dash end effects here
        }

        public void OnFall()
        {
            if (animator != null)
                animator.SetBool("IsJumping", true);
        }

        public void OnLanding()
        {
            if (animator != null)
                animator.SetBool("IsJumping", false);

            // Play landing particle effect
            if (particleJumpDown != null)
                particleJumpDown.Play();
        }

        public override void TakeDamage(DamageInfo info)
        {
            if (!IsAlive) return;

            base.TakeDamage(info);

            // Apply knockback using the new controller
            if (info.Source != null && controller != null)
            {
                Vector3 knockbackDirection = (transform.position - info.Source.transform.position).normalized;
                controller.AddForce(knockbackDirection * 15f, ForceMode.Impulse);
            }

            // Apply hit stun
            StartCoroutine(ApplyHitStun(0.1f));
        }

        protected override void HandleDeath()
        {
            base.HandleDeath();

            // Disable movement
            if (controller != null)
            {
                controller.CanMove = false;
            }

            // Unsubscribe from input
            UnsubscribeFromInput();

            Debug.Log($"{Stats.characterName} has died.");
        }

        private System.Collections.IEnumerator ApplyHitStun(float duration)
        {
            if (controller != null)
                controller.CanMove = false;

            yield return new WaitForSeconds(duration);

            if (IsAlive && controller != null)
                controller.CanMove = true;
        }

        private void OnDrawGizmosSelected()
        {
            // Draw attack range
            Gizmos.color = Color.red;
            Gizmos.DrawWireSphere(transform.position, attackRange);
        }

        private void OnDestroy()
        {
            // Clean up event subscriptions
            if (controller != null)
            {
                controller.OnGroundedStateChanged -= OnGroundedStateChanged;
                controller.OnMovementChanged -= OnMovementChanged;
                controller.OnDashStarted -= OnDashStarted;
                controller.OnDashEnded -= OnDashEnded;
            }
        }
    }
}