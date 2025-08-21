using UnityEngine;
using UnityEngine.InputSystem;

namespace junklite
{
    [RequireComponent(typeof(Character2D5Controller))]
    [DefaultExecutionOrder(5)]
    public class PlayerCharacter : CharacterBase
    {
        [Header("Player Settings")]
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
            if (CameraManager.Instance != null)
            {
                CameraManager.Instance.SetPlayerTarget(transform);
            }

            // Subscribe to CHARACTER SYSTEM events
            if (characterSystem != null)
            {
                characterSystem.OnGroundedChanged += OnGroundedStateChanged;
                characterSystem.OnMovingChanged += OnMovingStateChanged;
                characterSystem.OnDashingChanged += OnDashingStateChanged;
                characterSystem.OnAttackingChanged += OnAttackingStateChanged;
                characterSystem.OnStunnedChanged += OnStunnedStateChanged;
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

            // Handle jump input - check system capabilities
            if (jump && characterSystem.CanJump)
            {
                controller.Jump();
                if (particleJumpUp != null)
                    particleJumpUp.Play();
            }

            // Handle dash input - check system capabilities
            if (dash && characterSystem.CanDash)
            {
                controller.Dash();
            }

            // Reset one-frame inputs
            jump = false;
            dash = false;


            // Test healing with H key
            if (Keyboard.current.hKey.wasPressedThisFrame)
            {
                float healAmount = 20f;
                Heal(healAmount); // Use CharacterBase method
                Debug.Log($"Healed {Stats.characterName} for {healAmount} HP!");
            }

            // Test damage with T key
            if (Keyboard.current.tKey.wasPressedThisFrame)
            {
                float testDamage = 15f;
                DamageInfo damageInfo = new DamageInfo(testDamage, gameObject);
                TakeDamage(damageInfo); // Use CharacterBase method (single entry point)
                Debug.Log($"Applied {testDamage} test damage to {Stats.characterName}!");
            }

            
            if (Keyboard.current.yKey.wasPressedThisFrame)
            {
                InstantDeath();
            }
        }

        private void FixedUpdate()
        {
            if (characterSystem.CanMove && controller != null)
            {
                // Convert movement to normalized input
                float normalizedInput = horizontalMove / controller.MoveSpeed;
                controller.SetMovementInput(normalizedInput);
            }
        }

        private void HandleInput()
        {
            if (inputManager != null && characterSystem.CanMove)
            {
                horizontalMove = inputManager.MoveDirection.x * controller.MoveSpeed;
            }
            else
            {
                horizontalMove = 0f;
            }
        }

        private void UpdateAnimations()
        {
            if (animator != null && characterSystem != null)
            {
                // Use character system states
                animator.SetFloat("Speed", Mathf.Abs(horizontalMove));
                animator.SetBool("IsGrounded", characterSystem.IsGrounded);
                animator.SetBool("IsWallSliding", false);
                animator.SetBool("IsDashing", characterSystem.IsDashing);
                animator.SetBool("IsJumping", !characterSystem.IsGrounded);
                animator.SetBool("IsAttacking", characterSystem.IsAttacking);
                animator.SetBool("IsStunned", characterSystem.IsStunned);
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
            if (characterSystem.CanJump)
                jump = true;
        }

        private void HandleAttackInput()
        {
            if (characterSystem.CanAttack)
                PerformAttack();
        }

        private void HandleDashInput()
        {
            if (characterSystem.CanDash)
                dash = true;
        }

        private void PerformAttack()
        {
            // Set attacking state
            characterSystem.SetAttacking(true);
            
            Debug.Log($"{Stats.characterName} performed an attack with {Stats.damage} damage!");

            // Detect enemies in attack range
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

            // End attack after a short duration
            Invoke(nameof(EndAttack), 0.3f);
        }

        private void EndAttack()
        {
            characterSystem.SetAttacking(false);
        }

        #region State Event Handlers
        private void OnGroundedStateChanged(bool grounded)
        {
            if (grounded)
            {
                OnLanding();
            }
            else
            {
                OnFall();
            }
        }

        private void OnMovingStateChanged(bool moving)
        {
            // Add movement-based effects here if needed
        }

        private void OnDashingStateChanged(bool dashing)
        {
            if (dashing)
            {
               
            }
            else
            {
                
            }
        }

        private void OnAttackingStateChanged(bool attacking)
        {
            if (attacking)
            {
               
            }
        }

        private void OnStunnedStateChanged(bool stunned)
        {
            if (stunned)
            {
               
            }
            else
            {
               
            }
        }
        #endregion

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
            if (!characterSystem.CanTakeDamage) return;

            base.TakeDamage(info);

            // Apply knockback
            if (info.Source != null && controller != null)
            {
                Vector3 knockbackDirection = (transform.position - info.Source.transform.position).normalized;
                controller.AddForce(knockbackDirection * 15f, ForceMode.Impulse);
            }

            // Apply hit stun using the character system
            characterSystem.ApplyStun(0.1f);
        }

        protected override void HandleDeath()
        {
            base.HandleDeath();

            // Unsubscribe from input when dead
            UnsubscribeFromInput();

            Debug.Log($"{Stats.characterName} has died.");
        }

        private void OnDrawGizmosSelected()
        {
            // Draw attack range
            Gizmos.color = Color.red;
            Gizmos.DrawWireSphere(transform.position, attackRange);
        }

        protected override void OnDestroy()
        {
            base.OnDestroy(); // This handles character system cleanup
            
            // Clean up player-specific event subscriptions
            if (characterSystem != null)
            {
                characterSystem.OnGroundedChanged -= OnGroundedStateChanged;
                characterSystem.OnMovingChanged -= OnMovingStateChanged;
                characterSystem.OnDashingChanged -= OnDashingStateChanged;
                characterSystem.OnAttackingChanged -= OnAttackingStateChanged;
                characterSystem.OnStunnedChanged -= OnStunnedStateChanged;
            }
        }
    }
}