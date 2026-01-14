using UnityEngine;

namespace junklite
{
    /// <summary>
    /// Dodge state - enemy jumps backward to evade attacks.
    /// 
    /// REQUIRES: Enemy must implement IDodger
    /// 
    /// Pure ACTION state: applies backward force, optionally grants i-frames.
    /// Calls IDodger.OnDodgeComplete() when done - enemy decides what to do next.
    /// 
    /// I-FRAMES: Handled via CanTakeDamage property - enemy's TakeDamage checks this.
    /// Movement uses a simple parabolic arc calculated each frame for smooth motion.
    /// </summary>
    public class DodgeState : EnemyStateBase
    {
        private IDodger dodger;
        private EnemyMovement movement;

        private Vector3 startPosition;
        private Vector3 targetPosition;
        private float timer;
        private bool hasStarted;
        private bool dodgeComplete;

        // Cached VFX instance
        private GameObject activeVFX;

        public DodgeState(EnemyCharacter enemy) : base(enemy) { }

        public override void Enter()
        {
            // Get capability interface
            dodger = enemy as IDodger;
            if (dodger == null)
            {
                Debug.LogError($"{enemy.gameObject.name}: DodgeState requires IDodger interface!");
                return;
            }

            movement = enemy.Movement;
            hasStarted = false;
            dodgeComplete = false;
            timer = 0f;

            StartDodge();
        }

        private void StartDodge()
        {
            hasStarted = true;

            // Stop any current movement
            movement?.Stop();

            // Calculate dodge direction (away from target, or just backward)
            Vector3 dodgeDirection;
            if (HasTarget)
            {
                // Dodge away from player
                dodgeDirection = (Transform.position - Target.position).normalized;
                dodgeDirection.y = 0f; // Keep horizontal
                dodgeDirection.z = 0f; // 2.5D - no Z movement

                if (dodgeDirection.sqrMagnitude < 0.01f)
                {
                    // Fallback if directly on top of player
                    dodgeDirection = movement != null && movement.FacingDirection > 0
                        ? Vector3.left
                        : Vector3.right;
                }
            }
            else
            {
                // No target - dodge backward based on facing direction
                dodgeDirection = movement != null && movement.FacingDirection > 0
                    ? Vector3.left
                    : Vector3.right;
            }

            dodgeDirection = dodgeDirection.normalized;

            // Calculate start and end positions
            startPosition = Transform.position;
            targetPosition = startPosition + dodgeDirection * dodger.DodgeDistance;

            // Face the target during dodge (keeps eye on player)
            if (HasTarget && movement != null)
                movement.FaceTarget(Target.position);

            // Spawn VFX
            activeVFX = VFXPool.Get(dodger.DodgeVFXPrefab, enemy.transform);

            Debug.Log($"{enemy.gameObject.name}: DODGE! (distance: {dodger.DodgeDistance}, duration: {dodger.DodgeDuration}s, i-frames: {dodger.DodgeHasIFrames})");
        }

        public override void Update()
        {
            if (dodger == null || !hasStarted || dodgeComplete) return;

            timer += Time.deltaTime;
            float progress = Mathf.Clamp01(timer / dodger.DodgeDuration);

            // Calculate position along parabolic arc
            Vector3 currentPos = Vector3.Lerp(startPosition, targetPosition, progress);

            // Add parabolic height (peaks at progress = 0.5)
            float heightOffset = 4f * dodger.DodgeHeight * progress * (1f - progress);
            currentPos.y = startPosition.y + heightOffset;

            // Apply position
            Transform.position = currentPos;

            // Check if dodge is complete
            if (progress >= 1f)
            {
                CompleteDodge();
            }
        }

        private void CompleteDodge()
        {
            dodgeComplete = true;

            // Snap to ground position (remove any floating point errors)
            Vector3 finalPos = Transform.position;
            finalPos.y = startPosition.y;
            Transform.position = finalPos;

            dodger.OnDodgeComplete();
        }

        public override void Exit()
        {
            VFXPool.Release(ref activeVFX);
            timer = 0f;
            dodgeComplete = true;
        }

        /// <summary>
        /// Dodge state has i-frames - cannot take damage while actively dodging.
        /// </summary>
        public override bool CanTakeDamage => dodger == null || !dodger.DodgeHasIFrames || dodgeComplete;
    }
}