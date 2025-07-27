using UnityEngine;

namespace junklite
{
    [RequireComponent(typeof(CharacterController2D))]
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

        protected override void Awake()
        {
            base.Awake();
            inputManager = GameInputManager.Instance;
        }

        private void Start()
        {
            // Subscribe to controller events for effects/animations
            if (controller != null)
            {
                controller.OnFallEvent.AddListener(OnFall);
                controller.OnLandEvent.AddListener(OnLanding);
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
        }

        private void FixedUpdate()
        {
            if (IsAlive)
            {
                // Move the character using the controller
                controller.Move(horizontalMove * Time.fixedDeltaTime, jump, dash);
            }

            // Reset one-frame inputs
            jump = false;
            dash = false;
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
            if (animator != null)
            {
                animator.SetFloat("Speed", Mathf.Abs(horizontalMove));
                animator.SetBool("IsGrounded", controller.isGrounded);
                animator.SetBool("IsWallSliding", controller.IsWallSliding);
                animator.SetBool("IsDashing", controller.IsDashing);
                animator.SetBool("IsJumping", !controller.isGrounded);
            }
        }

        private void SubscribeToInput()
        {
            if (inputManager != null)
            {
                inputManager.OnJump += HandleJumpInput;
                inputManager.OnAttack += HandleAttackInput;
                // Add dash input when available in your input system
                // inputManager.OnDash += HandleDashInput;
            }
        }

        private void UnsubscribeFromInput()
        {
            if (inputManager != null)
            {
                inputManager.OnJump -= HandleJumpInput;
                inputManager.OnAttack -= HandleAttackInput;
                // inputManager.OnDash -= HandleDashInput;
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

            // Detect enemies in attack range
            Collider2D[] enemies = Physics2D.OverlapCircleAll(
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
            if (particleJumpDown != null && !controller.IsWallSliding && !controller.IsDashing)
                particleJumpDown.Play();
        }

        public override void TakeDamage(DamageInfo info)
        {
            if (!IsAlive) return;

            base.TakeDamage(info);

            // Apply knockback
            if (info.Source != null && controller != null)
            {
                Vector2 knockbackDirection = (transform.position - info.Source.transform.position).normalized;
                controller.ApplyKnockback(knockbackDirection * 15f);
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
                controller.SetMovementEnabled(false);
            }

            // Unsubscribe from input
            UnsubscribeFromInput();

            Debug.Log($"{Stats.characterName} has died.");
        }

        private System.Collections.IEnumerator ApplyHitStun(float duration)
        {
            if (controller != null)
                controller.SetMovementEnabled(false);

            yield return new WaitForSeconds(duration);

            if (IsAlive && controller != null)
                controller.SetMovementEnabled(true);
        }

        private void OnDrawGizmosSelected()
        {
            // Draw attack range
            Gizmos.color = Color.red;
            Gizmos.DrawWireSphere(transform.position, attackRange);
        }
    }
}