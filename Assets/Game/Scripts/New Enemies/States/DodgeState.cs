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
    /// 
    /// Axis-agnostic: uses movement.MovementAxis for fallback directions.
    /// 
    /// Sets Rigidbody to kinematic during dodge to prevent physics/gravity from
    /// fighting the direct position manipulation, then restores it on exit.
    /// </summary>
    public class DodgeState : EnemyStateBase
    {
        private IDodger dodger;
        private EnemyMovement movement;
        private Rigidbody rb;

        private Vector3 startPosition;
        private Vector3 targetPosition;
        private float timer;
        private bool hasStarted;
        private bool dodgeComplete;
        private bool wasKinematic;
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
            rb = enemy.GetComponent<Rigidbody>();
            hasStarted = false;
            dodgeComplete = false;
            timer = 0f;

            StartDodge();
        }

        private void StartDodge()
        {
            hasStarted = true;
            movement?.Stop();

            // Go kinematic so physics/gravity don't fight the position lerp
            if (rb != null)
            {
                wasKinematic = rb.isKinematic;
                rb.linearVelocity = Vector3.zero;
                rb.isKinematic = true;
            }

            // Calculate dodge direction (away from target, or backward)
            Vector3 dodgeDirection;
            if (HasTarget)
            {
                dodgeDirection = Transform.right * -1f * movement.FacingDirection;

                if (dodgeDirection.sqrMagnitude < 0.01f)
                {
                    dodgeDirection = movement != null && movement.FacingDirection > 0
                        ? -movement.MovementAxis
                        : movement.MovementAxis;
                }

                // Roll for forward dodge (past/behind the player)
                if (Random.value < dodger.DodgeForwardChance)
                    dodgeDirection = -dodgeDirection;
            }
            else
            {
                dodgeDirection = movement != null && movement.FacingDirection > 0
                    ? -movement.MovementAxis
                    : movement.MovementAxis;
            }

            dodgeDirection = dodgeDirection.normalized;

            startPosition = Transform.position;
            float dodgeDistance = dodger.DodgeDistance;

            // Wall check: raycast in dodge direction and clamp if a wall is in the way
            LayerMask wallMask = dodger.DodgeWallLayer;
            if (wallMask.value != 0)
            {
                Vector3 rayOrigin = startPosition + Vector3.up * 0.5f;
                if (Physics.Raycast(rayOrigin, dodgeDirection, out RaycastHit hit, dodgeDistance, wallMask))
                {
                    float maxDistance = hit.distance - dodger.DodgeWallCheckBuffer;
                    if (maxDistance < 0f) maxDistance = 0f;
                    dodgeDistance = Mathf.Min(dodgeDistance, maxDistance);
                }
            }

            targetPosition = startPosition + dodgeDirection * dodgeDistance;

            if (HasTarget && movement != null)
                movement.FaceTarget(Target.position);

            activeVFX = VFXPool.Get(dodger.DodgeVFXPrefab, enemy.transform);
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

            // Land exactly at the target position (no Y snap needed, targetPosition is at ground level)
            Transform.position = targetPosition;

            // Restore physics before callback so the enemy is ready for whatever comes next
            RestoreRigidbody();

            dodger.OnDodgeComplete();
        }

        public override void Exit()
        {
            VFXPool.Release(ref activeVFX);
            RestoreRigidbody();
            timer = 0f;
            dodgeComplete = true;
        }

        private void RestoreRigidbody()
        {
            if (rb != null && rb.isKinematic != wasKinematic)
            {
                rb.isKinematic = wasKinematic;
                rb.linearVelocity = Vector3.zero;
            }
        }

        /// <summary>
        /// I-frames active during dodge if enabled.
        /// </summary>
        public override bool CanTakeDamage => dodger == null || !dodger.DodgeHasIFrames || dodgeComplete;
    }
}