using System;
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
        [SerializeField] private string rollAnimName = "roll";   // used while IsRolling == true

        // Current animation state tracking
        private string currentAnimation;
        private bool isPlayingOneShot = false;

        // References
        private CharacterState characterSystem;
        private Character2D5Controller controller;

        private void Awake()
        {
            if (autoFindAnimator && animator == null)
                animator = GetComponentInChildren<Animator>();

            characterSystem = GetComponentInParent<CharacterState>();
            controller = GetComponentInParent<Character2D5Controller>();

            if (animator == null)
                Debug.LogError($"AnimationController on {gameObject.name} couldn't find Animator component!");
        }

        private void Start()
        {
            if (characterSystem != null)
            {
                characterSystem.OnGroundedChanged += OnGroundedChanged;
                characterSystem.OnDashingChanged += OnDashingChanged;
                characterSystem.OnAttackingChanged += OnAttackingChanged;
                characterSystem.OnRollingChanged += OnRollingChanged;   // <-- subscribe
                characterSystem.OnStunnedChanged += OnStunnedChanged;
                characterSystem.OnDeath += OnDeath;
            }

            PlayAnimation(idleAnimName);
        }

        private void Update()
        {
            UpdateMovementAnimations();
        }

     
        // Priority: Rolling > Attacking > Dashing > Jump/Fall > Run > Idle
        private void UpdateMovementAnimations()
        {
            if (animator == null || characterSystem == null || controller == null)
                return;

            // ---- ROLL HAS TOP PRIORITY ----
            if (characterSystem.IsRolling)
            {
                // Make sure roll stays active the whole time
                isPlayingOneShot = false;
                PlayAnimation(rollAnimName);
                return;
            }

            // One-shots (like attack) can block only if not rolling
            if (isPlayingOneShot)
                return;

            if (!characterSystem.IsAlive)
                return;

            if (characterSystem.IsAttacking)
            {
                return;
            }
            else if (characterSystem.IsDashing)
            {
                return;
            }
            else if (!characterSystem.IsGrounded)
            {
                PlayAnimation(controller.Velocity.y > 0.1f ? jumpUpAnimName : fallAnimName);
            }
            else if (Mathf.Abs(controller.Velocity.x) > 0.1f && characterSystem.CanMove)
            {
                PlayAnimation(runAnimName);
            }
            else
            {
                PlayAnimation(idleAnimName);
            }
        }


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

        private void PlayOneShotAnimation(string animationName, float duration = 0f)
        {
            if (animator == null || string.IsNullOrEmpty(animationName))
                return;

            PlayAnimation(animationName);
            isPlayingOneShot = true;

            if (duration > 0f)
            {
                CancelInvoke(nameof(ClearOneShotFlag));
                Invoke(nameof(ClearOneShotFlag), duration);
            }
        }

        private void ClearOneShotFlag() => isPlayingOneShot = false;

        public void ResetGraph(bool playIdle = true)
        {
            if (animator == null) return;

            animator.Rebind();
            animator.Update(0f);

            if (playIdle && !string.IsNullOrEmpty(idleAnimName))
                animator.Play(idleAnimName, 0, 0f);

            isPlayingOneShot = false;
            currentAnimation = idleAnimName;
        }

        public void EndOneShotAnimation() => ClearOneShotFlag();

        #region Event Handlers

        private void OnGroundedChanged(bool grounded)
        {
            if (grounded && !isPlayingOneShot && !characterSystem.IsRolling)
            {
                // Optional land blip
                // PlayOneShotAnimation(landAnimName, 0.2f);
            }
        }

        private void OnDashingChanged(bool dashing)
        {
            if (dashing && !characterSystem.IsRolling)
            {
                // Optional dash blip
                // PlayOneShotAnimation(dashAnimName, 0.2f);
            }
        }

        // ROLL: don’t use a short one-shot; keep the roll looping until state clears
        private void OnRollingChanged(bool rolling)
        {
            if (rolling)
            {
                isPlayingOneShot = false;       // make sure roll takes over immediately
                PlayAnimation(rollAnimName);
            }
            else
            {
                // when roll ends, Update() will resume normal state selection
            }
        }

        private void OnAttackingChanged(bool attacking)
        {
            if (attacking && !characterSystem.IsRolling)
            {
                PlayOneShotAnimation(attackAnimName, 0.3f);
            }
        }

        private void OnStunnedChanged(bool stunned)
        {
            if (stunned && !characterSystem.IsRolling)
            {
                // PlayOneShotAnimation(hurtAnimName, 0.1f);
            }
        }

        private void OnDeath()
        {
            // PlayAnimation(deathAnimName);
            isPlayingOneShot = true; // Lock on death animation
        }

        #endregion

        #region Cleanup

        private void OnDestroy()
        {
            if (characterSystem != null)
            {
                characterSystem.OnGroundedChanged -= OnGroundedChanged;
                characterSystem.OnDashingChanged -= OnDashingChanged;
                characterSystem.OnAttackingChanged -= OnAttackingChanged;
                characterSystem.OnRollingChanged -= OnRollingChanged;   // <-- make sure to unsubscribe
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

            GUILayout.BeginArea(new Rect(Screen.width - 250, 10, 240, 170));
            GUILayout.Label("=== Animation Debug ===");
            GUILayout.Label($"Current: {currentAnimation}");
            GUILayout.Label($"One-Shot: {isPlayingOneShot}");

            if (characterSystem != null)
            {
                GUILayout.Label($"Grounded:  {characterSystem.IsGrounded}");
                GUILayout.Label($"Rolling:   {characterSystem.IsRolling}");
                GUILayout.Label($"Attacking: {characterSystem.IsAttacking}");
                GUILayout.Label($"Dashing:   {characterSystem.IsDashing}");
            }

            if (controller != null)
            {
                GUILayout.Label($"VelX: {controller.Velocity.x:F1}  VelY: {controller.Velocity.y:F1}");
            }

            GUILayout.EndArea();
        }

        #endregion
    }
}
