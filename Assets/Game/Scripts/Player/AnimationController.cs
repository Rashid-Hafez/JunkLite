using System;
using UnityEngine;

namespace junklite
{
    public class AnimationController : MonoBehaviour
    {
        [Header("Animation Settings")]
        [SerializeField] private Animator animator;
        [SerializeField] private bool autoFindAnimator = true;

        // Cached refs
        private PlayerState characterSystem;
        private Character2D5Controller controller;

        // Internal
        private bool wasGroundedLastFrame = true;

        private void Awake()
        {
            if (autoFindAnimator && animator == null)
                animator = GetComponentInChildren<Animator>();

            characterSystem = GetComponentInParent<PlayerState>();
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
                characterSystem.OnWallSlideChanged += OnWallSlideChanged;
                characterSystem.OnLedgeDetectedChanged += OnLedgeDetectedChanged;
            characterSystem.OnParryChanged += OnParryChanged;
                characterSystem.OnJumpStateChanged += OnJumpStateChanged;
                characterSystem.OnDoubleJumpChanged += OnDoubleJumpChanged;
                characterSystem.OnComboAttackTriggered += OnComboAttackTriggered;
                characterSystem.OnStunnedChanged += OnStunnedChanged;
            }
        }


        private void Update()
        {
            if (!animator || !controller || !characterSystem) return;

            // --- Base locomotion parameters ---
            float speed = Mathf.Abs(controller.Velocity.x);

            // Don't show run animation if stunned or no movement input
            bool hasMovementInput = controller.GetMovementInputMagnitude() > 0.1f;
            if (characterSystem.IsStunned || !hasMovementInput)
                speed = 0f;

            animator.SetFloat("Speed", speed);
            animator.SetBool("IsGrounded", characterSystem.IsGrounded);

            // --- Jump, Fall & Wall Slide states ---
            // These are now properly managed by CharacterState with mutual exclusivity
            animator.SetBool("IsJumping", characterSystem.IsJumping);
            animator.SetBool("IsFalling", characterSystem.IsFalling);
            animator.SetBool("IsWallSliding", characterSystem.IsWallSliding);
            animator.SetBool("IsLedgeDetected", characterSystem.IsLedgeDetected);

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

        private void OnStunnedChanged(bool obj)
        {
            animator.SetTrigger("Stunned");
        }

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

        private void OnWallSlideChanged(bool wallSliding)
        {
            // Handled in Update() for consistency, but also respond to events for immediate feedback
            animator.SetBool("IsWallSliding", wallSliding);
        }

        private void OnLedgeDetectedChanged(bool detected)
        {
            animator.SetBool("IsLedgeDetected", detected);
        }

        private void OnJumpStateChanged(bool jumping)
        {
            // Handled in Update() for consistency, but also respond to events for immediate feedback
            animator.SetBool("IsJumping", jumping);
        }

        private void OnDoubleJumpChanged(bool doubleJumping)
        {
            if (animator == null) return;

            animator.SetBool("IsDoubleJumping", doubleJumping);
            if (doubleJumping)
                animator.SetTrigger("DoubleJump");
        }

        private void OnComboAttackTriggered(int comboIndex)
        {
            if (animator == null) return;

            // Only trigger combo animations when grounded
            if (characterSystem != null && !characterSystem.IsGrounded)
                return;

            animator.SetInteger("ComboStep", comboIndex);
            animator.SetTrigger("AttackTrigger");
        }

        private void OnDeath()
        {
            animator.SetTrigger("Die");
        }

        private void OnParryChanged(bool parrying)
        {
            if (animator == null) return;
            animator.SetBool("IsParrying", parrying);
            if (parrying)
                animator.SetTrigger("Parry");
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
            characterSystem.OnWallSlideChanged -= OnWallSlideChanged;
            characterSystem.OnLedgeDetectedChanged -= OnLedgeDetectedChanged;
            characterSystem.OnParryChanged -= OnParryChanged;
            characterSystem.OnJumpStateChanged -= OnJumpStateChanged;
            characterSystem.OnDoubleJumpChanged -= OnDoubleJumpChanged;
            characterSystem.OnComboAttackTriggered -= OnComboAttackTriggered;
        }
    }
}