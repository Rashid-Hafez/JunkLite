using UnityEngine;

namespace junklite
{
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
        private GameObject vfx;

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
            vfx = dodger.DodgeVFXPrefab;

            StartDodge();
        }

        private void StartDodge()
        {
            hasStarted = true;
            movement?.Stop();

            if (rb != null)
            {
                wasKinematic = rb.isKinematic;
                rb.linearVelocity = Vector3.zero;
                rb.isKinematic = true;
            }

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

            if (vfx != null) vfx.SetActive(true);
        }

        public override void Update()
        {
            if (dodger == null || !hasStarted || dodgeComplete) return;

            timer += Time.deltaTime;
            float progress = Mathf.Clamp01(timer / dodger.DodgeDuration);

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

            Transform.position = targetPosition;
            RestoreRigidbody();

            if (vfx != null) vfx.SetActive(false);

            dodger.OnDodgeComplete();
        }

        public override void Exit()
        {
            if (vfx != null) vfx.SetActive(false);
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

        public override bool CanTakeDamage => dodger == null || !dodger.DodgeHasIFrames || dodgeComplete;
    }
}