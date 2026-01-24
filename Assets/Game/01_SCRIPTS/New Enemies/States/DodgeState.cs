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
        private GameObject activeVFX;

        public DodgeState(EnemyCharacter enemy) : base(enemy) { }

        public override void Enter()
        {
            dodger = GetCapability<IDodger>();
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
            movement?.Stop();

            // Calculate dodge direction (away from target, or backward)
            Vector3 dodgeDirection;
            if (HasTarget)
            {
                dodgeDirection = (Transform.position - Target.position).normalized;
                dodgeDirection.y = 0f;
                dodgeDirection.z = 0f;

                if (dodgeDirection.sqrMagnitude < 0.01f)
                {
                    dodgeDirection = movement != null && movement.FacingDirection > 0
                        ? Vector3.left
                        : Vector3.right;
                }
            }
            else
            {
                dodgeDirection = movement != null && movement.FacingDirection > 0
                    ? Vector3.left
                    : Vector3.right;
            }

            dodgeDirection = dodgeDirection.normalized;

            startPosition = Transform.position;
            targetPosition = startPosition + dodgeDirection * dodger.DodgeDistance;

            if (HasTarget && movement != null)
                movement.FaceTarget(Target.position);

            activeVFX = VFXPool.Get(dodger.DodgeVFXPrefab, enemy.transform);

            Debug.Log($"{enemy.gameObject.name}: DODGE! (distance: {dodger.DodgeDistance}, duration: {dodger.DodgeDuration}s, i-frames: {dodger.DodgeHasIFrames})");
        }

        public override void Update()
        {
            if (dodger == null || !hasStarted || dodgeComplete) return;

            timer += Time.deltaTime;
            float progress = Mathf.Clamp01(timer / dodger.DodgeDuration);

            // Parabolic arc movement
            Vector3 currentPos = Vector3.Lerp(startPosition, targetPosition, progress);
            float heightOffset = 4f * dodger.DodgeHeight * progress * (1f - progress);
            currentPos.y = startPosition.y + heightOffset;

            Transform.position = currentPos;

            if (progress >= 1f)
                CompleteDodge();
        }

        private void CompleteDodge()
        {
            dodgeComplete = true;

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
        /// I-frames active during dodge if enabled.
        /// </summary>
        public override bool CanTakeDamage => dodger == null || !dodger.DodgeHasIFrames || dodgeComplete;
    }
}