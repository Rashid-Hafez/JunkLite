using UnityEngine;

namespace junklite
{
    public class AnimationController : MonoBehaviour
    {
        [Header("Animation Settings")]
        [SerializeField] private Animator animator;
        [SerializeField] private bool autoFindAnimator = true;

        // Cached refs
        private CharacterState characterSystem;
        private Character2D5Controller controller;

        // Internal
        private bool wasGroundedLastFrame = true;

        private void Awake()
        {
            if (autoFindAnimator && animator == null)
                animator = GetComponentInChildren<Animator>();

            characterSystem = GetComponentInParent<CharacterState>();
            controller = GetComponentInParent<Character2D5Controller>();

            if (animator == null)
                Debug.LogError($"AnimationController on {gameObject.name} couldn't find Animator!");
        }

        private void Start()
        {
            if (characterSystem != null)
            {
                characterSystem.OnGroundedChanged += OnGroundedChanged;
                characterSystem.OnDashingChanged += OnDashingChanged;
                characterSystem.OnAttackingChanged += OnAttackingChanged;
                characterSystem.OnRollingChanged += OnRollingChanged;
                characterSystem.OnDeath += OnDeath;
            }
        }

        private void Update()
        {
            if (!animator || !controller || !characterSystem) return;

            // --- Base locomotion parameters ---
            float speed = Mathf.Abs(controller.Velocity.x);
            animator.SetFloat("Speed", speed);
            animator.SetBool("IsGrounded", characterSystem.IsGrounded);

            // --- Jump & fall detection ---
            if (!characterSystem.IsGrounded)
            {
                if (controller.Velocity.y > 0.1f)
                {
                    animator.SetBool("IsJumping", true);
                    animator.SetBool("IsFalling", false);
                }
                else if (controller.Velocity.y < -0.1f)
                {
                    animator.SetBool("IsFalling", true);
                    animator.SetBool("IsJumping", false);
                }
            }
            else
            {
                animator.SetBool("IsJumping", false);
                animator.SetBool("IsFalling", false);
            }

            // --- Optional landing trigger ---
            if (!wasGroundedLastFrame && characterSystem.IsGrounded)
                animator.SetTrigger("Land");

            wasGroundedLastFrame = characterSystem.IsGrounded;

            // --- Dynamic run animation speed scaling (optional) ---
            if (characterSystem.IsGrounded && !characterSystem.IsRolling && !characterSystem.IsDashing)
                animator.speed = Mathf.Clamp(speed / controller.MoveSpeed, 0.8f, 1.3f);
            else
                animator.speed = 1f;
        }

        #region Event Handlers

        private void OnGroundedChanged(bool grounded)
        {
            animator.SetBool("IsGrounded", grounded);
        }

        private void OnDashingChanged(bool dashing)
        {
            animator.SetBool("IsDashing", dashing);
        }

        private void OnRollingChanged(bool rolling)
        {
            animator.SetBool("IsRolling", rolling);
        }

        private void OnAttackingChanged(bool attacking)
        {
            // You can add an Attack trigger if you want Animator-driven combos later
            if (attacking)
                animator.SetTrigger("Attack");
        }

        private void OnDeath()
        {
            animator.SetTrigger("Die");
        }

        #endregion

        private void OnDestroy()
        {
            if (characterSystem == null) return;

            characterSystem.OnGroundedChanged -= OnGroundedChanged;
            characterSystem.OnDashingChanged -= OnDashingChanged;
            characterSystem.OnAttackingChanged -= OnAttackingChanged;
            characterSystem.OnRollingChanged -= OnRollingChanged;
            characterSystem.OnDeath -= OnDeath;
        }
    }
}
