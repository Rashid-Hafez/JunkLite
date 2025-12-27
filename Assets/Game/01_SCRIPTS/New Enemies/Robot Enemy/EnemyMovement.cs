using UnityEngine;

namespace junklite
{
    /// <summary>
    /// Handles enemy movement. States tell it WHERE to go, this handles HOW.
    /// Works for 2.5D (movement on XZ plane, or XY - configurable).
    /// </summary>
    public class EnemyMovement : MonoBehaviour
    {
        [Header("Movement Settings")]
        [SerializeField] private float moveSpeed = 3f;
        [SerializeField] private float stoppingDistance = 0.1f;

        [Header("2.5D Settings")]
        [Tooltip("Lock movement to a specific axis")]
        [SerializeField] private MovementPlane movementPlane = MovementPlane.XZ;

        [Header("Sprite Facing")]
        [Tooltip("Use scale flip for 2D sprites (recommended for 2.5D)")]
        [SerializeField] private bool useScaleFlip = true;
        [Tooltip("Default facing direction (1 = right, -1 = left)")]
        [SerializeField] private int defaultFacing = 1;

        // State
        private Vector3 targetPosition;
        private Vector3 moveDirection;
        private bool isMoving;
        private bool isDirectionalMovement;
        private bool isDashing;
        private float currentSpeed;
        private float dashSpeed;
        private int facingDirection = 1;

        // Public accessors
        public float MoveSpeed { get => moveSpeed; set => moveSpeed = value; }
        public float CurrentSpeed => currentSpeed;
        public bool IsMoving => isMoving;
        public bool HasReachedDestination => !isMoving || (!isDirectionalMovement && DistanceToTarget <= stoppingDistance);
        public float DistanceToTarget => GetPlanarDistance(transform.position, targetPosition);
        public Vector3 MoveDirection => moveDirection;
        public int FacingDirection => facingDirection;

        public enum MovementPlane
        {
            XZ,  // Top-down or 3D (Y is up)
            XY   // Side-scroller (Z is depth)
        }

        private void Awake()
        {
            facingDirection = defaultFacing;
            ApplyFacing();
        }

        private void Update()
        {
            if (isMoving)
            {
                if (isDashing)
                    DashTowardsTarget();
                else if (isDirectionalMovement)
                    MoveInCurrentDirection();
                else
                    MoveTowardsTarget();
            }
        }

        /// <summary>
        /// Start moving towards a position.
        /// </summary>
        public void MoveTo(Vector3 position)
        {
            targetPosition = position;
            isMoving = true;
            isDirectionalMovement = false;
            UpdateFacingFromDirection(position - transform.position);
        }

        /// <summary>
        /// Start moving towards a transform.
        /// </summary>
        public void MoveTo(Transform target)
        {
            if (target != null)
                MoveTo(target.position);
        }

        /// <summary>
        /// Move towards target at a specific speed.
        /// </summary>
        public void MoveTo(Vector3 position, float speed)
        {
            targetPosition = position;
            currentSpeed = speed;
            isMoving = true;
            isDirectionalMovement = false;
            isDashing = false;
            UpdateFacingFromDirection(position - transform.position);
        }

        /// <summary>
        /// Dash to a position in a straight line at high speed.
        /// Use for quick attack dashes - moves directly, ignores pathfinding.
        /// </summary>
        public void DashTo(Vector3 position, float speed)
        {
            targetPosition = position;
            dashSpeed = speed;
            isMoving = true;
            isDirectionalMovement = false;
            isDashing = true;
            UpdateFacingFromDirection(position - transform.position);
        }

        /// <summary>
        /// Move continuously in a direction (doesn't stop until told to).
        /// </summary>
        public void MoveInDirection(Vector3 direction)
        {
            moveDirection = GetPlanarDirection(direction);
            isMoving = true;
            isDirectionalMovement = true;
            UpdateFacingFromDirection(direction);
        }

        /// <summary>
        /// Move continuously in a direction at specific speed.
        /// </summary>
        public void MoveInDirection(Vector3 direction, float speed)
        {
            moveDirection = GetPlanarDirection(direction);
            currentSpeed = speed;
            isMoving = true;
            isDirectionalMovement = true;
            UpdateFacingFromDirection(direction);
        }

        /// <summary>
        /// Stop all movement immediately.
        /// </summary>
        public void Stop()
        {
            isMoving = false;
            isDirectionalMovement = false;
            isDashing = false;
            currentSpeed = 0f;
            moveDirection = Vector3.zero;
        }

        /// <summary>
        /// Set facing direction (1 = right, -1 = left).
        /// </summary>
        public void SetFacing(int direction)
        {
            facingDirection = direction >= 0 ? 1 : -1;
            ApplyFacing();
        }

        /// <summary>
        /// Flip the current facing direction.
        /// </summary>
        public void FlipFacing()
        {
            facingDirection *= -1;
            ApplyFacing();
        }

        /// <summary>
        /// Face towards a target position.
        /// </summary>
        public void FaceTarget(Vector3 targetPos)
        {
            UpdateFacingFromDirection(targetPos - transform.position);
        }

        private void UpdateFacingFromDirection(Vector3 direction)
        {
            if (direction.x > 0.01f)
                facingDirection = 1;
            else if (direction.x < -0.01f)
                facingDirection = -1;

            ApplyFacing();
        }

        private void ApplyFacing()
        {
            if (useScaleFlip)
            {
                Vector3 scale = transform.localScale;
                scale.x = Mathf.Abs(scale.x) * facingDirection;
                transform.localScale = scale;
            }
        }

        private void MoveTowardsTarget()
        {
            float distance = DistanceToTarget;

            if (distance <= stoppingDistance)
            {
                Stop();
                return;
            }

            Vector3 direction = (targetPosition - transform.position).normalized;
            Vector3 planarDir = GetPlanarDirection(direction);

            // Use currentSpeed if it was set (e.g., by MoveTo with speed parameter), otherwise use default moveSpeed
            float speed = currentSpeed > 0f ? currentSpeed : moveSpeed;
            Vector3 movement = planarDir * speed * Time.deltaTime;
            transform.position += movement;
        }

        private void DashTowardsTarget()
        {
            float distance = DistanceToTarget;

            if (distance <= stoppingDistance)
            {
                Stop();
                return;
            }

            Vector3 direction = (targetPosition - transform.position).normalized;
            Vector3 planarDir = GetPlanarDirection(direction);

            currentSpeed = dashSpeed;
            Vector3 movement = planarDir * dashSpeed * Time.deltaTime;
            transform.position += movement;
        }

        private void MoveInCurrentDirection()
        {
            if (moveDirection == Vector3.zero)
            {
                Stop();
                return;
            }

            currentSpeed = moveSpeed;
            Vector3 movement = moveDirection * currentSpeed * Time.deltaTime;
            transform.position += movement;
        }

        private Vector3 GetPlanarDirection(Vector3 direction)
        {
            switch (movementPlane)
            {
                case MovementPlane.XZ:
                    return new Vector3(direction.x, 0f, direction.z).normalized;
                case MovementPlane.XY:
                    return new Vector3(direction.x, direction.y, 0f).normalized;
                default:
                    return direction.normalized;
            }
        }

        private float GetPlanarDistance(Vector3 a, Vector3 b)
        {
            switch (movementPlane)
            {
                case MovementPlane.XZ:
                    return Vector2.Distance(new Vector2(a.x, a.z), new Vector2(b.x, b.z));
                case MovementPlane.XY:
                    return Vector2.Distance(new Vector2(a.x, a.y), new Vector2(b.x, b.y));
                default:
                    return Vector3.Distance(a, b);
            }
        }

        private void OnDrawGizmosSelected()
        {
            if (isMoving)
            {
                Gizmos.color = Color.green;
                if (isDirectionalMovement)
                    Gizmos.DrawRay(transform.position, moveDirection * 2f);
                else
                {
                    Gizmos.DrawLine(transform.position, targetPosition);
                    Gizmos.DrawWireSphere(targetPosition, 0.2f);
                }
            }
        }
    }
}