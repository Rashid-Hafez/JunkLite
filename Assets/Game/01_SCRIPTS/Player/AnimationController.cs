using UnityEngine;

namespace junklite
{
    public class AnimationController : MonoBehaviour
    {
        [Header("Animation Settings")]
        [SerializeField] private Animator animator;
        [SerializeField] private bool autoFindAnimator = true;

        [Header("Animation Names")]
        [SerializeField] private string idleAnimName = "Idle";
        [SerializeField] private string runAnimName = "run";
        [SerializeField] private string jumpUpAnimName = "jump_up";
        [SerializeField] private string fallAnimName = "fall";
        [SerializeField] private string landAnimName = "land";
        [SerializeField] private string attackAnimName = "Attack";

        // Current animation state tracking
        private string currentAnimation;
        private bool isPlayingOneShot = false;

        // References
        private CharacterSystem characterSystem;
        private Character2D5Controller controller;

        private void Awake()
        {
            // Auto-find animator if not assigned
            if (autoFindAnimator && animator == null)
            {
                animator = GetComponentInChildren<Animator>();
            }

            // Get required components from parent
            characterSystem = GetComponentInParent<CharacterSystem>();
            controller = GetComponentInParent<Character2D5Controller>();

            if (animator == null)
            {
                Debug.LogError($"AnimationController on {gameObject.name} couldn't find Animator component!");
            }
        }

        private void Start()
        {
            // Subscribe to character system events for immediate animation responses
            if (characterSystem != null)
            {
                characterSystem.OnGroundedChanged += OnGroundedChanged;
                characterSystem.OnDashingChanged += OnDashingChanged;
                characterSystem.OnAttackingChanged += OnAttackingChanged;
                characterSystem.OnStunnedChanged += OnStunnedChanged;
                characterSystem.OnDeath += OnDeath;
            }

            // Play initial idle animation
            PlayAnimation(idleAnimName);
        }

        private void Update()
        {
            UpdateMovementAnimations();
        }

        /// <summary>
        /// Main animation update - handles movement-based animations
        /// </summary>
        private void UpdateMovementAnimations()
        {
            if (animator == null || characterSystem == null || controller == null)
                return;

            // Skip if playing one-shot animations
            if (isPlayingOneShot)
                return;

            // Skip if dead
            if (!characterSystem.IsAlive)
                return;

            // Priority: Attacking > Dashing > Jumping > Running > Idle
            if (characterSystem.IsAttacking)
            {
                // Attack animation is handled by event
                return;
            }
            else if (characterSystem.IsDashing)
            {
                // Dash animation is handled by event
                return;
            }
            else if (!characterSystem.IsGrounded)
            {
                // Check if just jumped (rising) vs falling
                if (controller.Velocity.y > 0.1f)
                    PlayAnimation(jumpUpAnimName);
                else
                    PlayAnimation(fallAnimName);
            }
            else if (Mathf.Abs(controller.Velocity.x) > 0.1f && characterSystem.CanMove)
            {
                // Moving on ground - play run animation
                PlayAnimation(runAnimName);
            }
            else
            {
                // Standing still - play idle animation
                PlayAnimation(idleAnimName);
            }
        }

        /// <summary>
        /// Play an animation if it's not already playing
        /// </summary>
        private void PlayAnimation(string animationName)
        {
            if (animator == null || string.IsNullOrEmpty(animationName))
                return;

            if (currentAnimation != animationName)
            {
                animator.Play(animationName);
                currentAnimation = animationName;
            }
        }

        /// <summary>
        /// Play a one-shot animation (like attack, hurt) that should override movement animations
        /// </summary>
        private void PlayOneShotAnimation(string animationName, float duration = 0f)
        {
            if (animator == null || string.IsNullOrEmpty(animationName))
                return;

            PlayAnimation(animationName);
            isPlayingOneShot = true;

            // Auto-clear one-shot flag after duration
            if (duration > 0f)
            {
                CancelInvoke(nameof(ClearOneShotFlag));
                Invoke(nameof(ClearOneShotFlag), duration);
            }
        }

        /// <summary>
        /// Clear the one-shot animation flag
        /// </summary>
        private void ClearOneShotFlag()
        {
            isPlayingOneShot = false;
        }

        /// <summary>
        /// Manually clear one-shot animation (useful for animation events)
        /// </summary>
        public void EndOneShotAnimation()
        {
            ClearOneShotFlag();
        }

        #region Event Handlers

        private void OnGroundedChanged(bool grounded)
        {
            if (grounded && !isPlayingOneShot)
            {
                //PlayOneShotAnimation(landAnimName, 0.2f); 
            }
        }

        private void OnDashingChanged(bool dashing)
        {
            if (dashing)
            {
               // PlayOneShotAnimation(dashAnimName, 0.2f); 
            }
        }

        private void OnAttackingChanged(bool attacking)
        {
            if (attacking)
            {
                PlayOneShotAnimation(attackAnimName, 0.3f); // Match attack duration
            }
        }

        private void OnStunnedChanged(bool stunned)
        {
            if (stunned)
            {
               // PlayOneShotAnimation(hurtAnimName, 0.1f); // Match stun duration
            }
        }

        private void OnDeath()
        {
           // PlayAnimation(deathAnimName);
            isPlayingOneShot = true; // Lock on death animation
        }

        #endregion

        #region Public Animation Controls

        /// <summary>
        /// Force play a specific animation (useful for cutscenes, special states)
        /// </summary>
        public void ForcePlayAnimation(string animationName)
        {
            currentAnimation = ""; // Reset current to force play
            PlayAnimation(animationName);
        }

        /// <summary>
        /// Play a custom one-shot animation with duration
        /// </summary>
        public void PlayCustomOneShot(string animationName, float duration)
        {
            PlayOneShotAnimation(animationName, duration);
        }

        /// <summary>
        /// Check if currently playing a specific animation
        /// </summary>
        public bool IsPlayingAnimation(string animationName)
        {
            return currentAnimation == animationName;
        }

        /// <summary>
        /// Check if currently playing any one-shot animation
        /// </summary>
        public bool IsPlayingOneShot => isPlayingOneShot;

        #endregion

        #region Cleanup

        private void OnDestroy()
        {
            if (characterSystem != null)
            {
                characterSystem.OnGroundedChanged -= OnGroundedChanged;
                characterSystem.OnDashingChanged -= OnDashingChanged;
                characterSystem.OnAttackingChanged -= OnAttackingChanged;
                characterSystem.OnStunnedChanged -= OnStunnedChanged;
                characterSystem.OnDeath -= OnDeath;
            }
        }

        #endregion

        #region Debug

        [Header("Debug")]
        [SerializeField] private bool showDebugInfo = false;

        private void OnGUI()
        {
            if (!showDebugInfo) return;

            GUILayout.BeginArea(new Rect(Screen.width - 250, 10, 240, 150));
            GUILayout.Label("=== Animation Debug ===");
            GUILayout.Label($"Current: {currentAnimation}");
            GUILayout.Label($"One-Shot: {isPlayingOneShot}");

            if (characterSystem != null)
            {
                GUILayout.Label($"Grounded: {characterSystem.IsGrounded}");
                GUILayout.Label($"Moving: {characterSystem.IsMoving}");
                GUILayout.Label($"Attacking: {characterSystem.IsAttacking}");
                GUILayout.Label($"Dashing: {characterSystem.IsDashing}");
            }

            if (controller != null)
            {
                GUILayout.Label($"Velocity: {controller.Velocity.x:F1}");
            }

            GUILayout.EndArea();
        }

        #endregion
    }
}