using System;
using UnityEngine;

namespace junklite
{
    /// <summary>
    /// Handles enemy movement using Rigidbody velocity for physics-accurate collider positioning.
    /// States tell it WHERE to go, this handles HOW.
    /// Works for 2.5D (movement on XZ plane, or XY - configurable).
    /// 
    /// IMPORTANT: Requires a Rigidbody component. This script configures it automatically:
    /// - isKinematic = false (velocity-based movement)
    /// - useGravity = true (real gravity)
    /// - Interpolate = Interpolate (for smooth visuals)
    /// - Constraints = FreezeRotation (prevents tumbling)
    /// </summary>
    [RequireComponent(typeof(Rigidbody))]
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

        [Header("Knockback Settings")]
        [Tooltip("If true, this enemy cannot be knocked back")]
        [SerializeField] private bool ignoreKnockback = false;
        [Tooltip("How quickly knockback velocity decays (visual smoothing)")]
        [SerializeField] private float knockbackDrag = 10f;
        [Tooltip("How long knockback lasts before enemy regains control")]
        [SerializeField] private float knockbackDuration = 0.2f;

        [Header("Ground Detection")]
        [SerializeField] private float groundCheckDistance = 0.1f;
        [SerializeField] private LayerMask groundLayers = ~0; // Default to all layers

        // Components
        private Rigidbody rb;

        // State
        private Vector3 targetPosition;
        private Vector3 moveDirection;
        private bool isMoving;
        private bool isDirectionalMovement;
        private bool isDashing;
        private float currentSpeed;
        private float dashSpeed;
        private int facingDirection = 1;

        // Knockback state
        private Vector3 knockbackVelocity;
        private bool isInKnockback;
        private float knockbackTimer;

        // Ground state
        private bool isGrounded;

        // Events for FSM integration
        /// <summary>
        /// Fired when knockback starts. FSM can use this to transition to a stunned state.
        /// </summary>
        public event Action OnKnockbackStart;

        /// <summary>
        /// Fired when knockback ends (grounded and velocity decayed). FSM can use this to resume normal behavior.
        /// </summary>
        public event Action OnKnockbackEnd;

        // Public accessors
        public float MoveSpeed { get => moveSpeed; set => moveSpeed = value; }
        public float CurrentSpeed => currentSpeed;
        public bool IsMoving => isMoving;
        public bool HasReachedDestination => !isMoving || (!isDirectionalMovement && DistanceToTarget <= stoppingDistance);
        public float DistanceToTarget => GetPlanarDistance(rb.position, targetPosition);
        public Vector3 MoveDirection => moveDirection;
        public int FacingDirection => facingDirection;
        public bool IsGrounded => isGrounded;
        public bool IsInKnockback => isInKnockback;
        public bool IgnoreKnockback { get => ignoreKnockback; set => ignoreKnockback = value; }

        public enum MovementPlane
        {
            XZ,  // Top-down or 3D (Y is up)
            XY   // Side-scroller (Z is depth)
        }

        private void Awake()
        {
            rb = GetComponent<Rigidbody>();

            // Configure Rigidbody for velocity-based movement with gravity
            rb.isKinematic = false;
            rb.interpolation = RigidbodyInterpolation.Interpolate;
            rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic; // Better collision detection at high speeds

            // Freeze rotation to prevent tumbling
            rb.constraints = RigidbodyConstraints.FreezeRotationX |
                            RigidbodyConstraints.FreezeRotationY |
                            RigidbodyConstraints.FreezeRotationZ;

            // Lock Z position for XY plane (side-scroller) or lock Y rotation for XZ plane
            if (movementPlane == MovementPlane.XY)
            {
                rb.constraints |= RigidbodyConstraints.FreezePositionZ;
            }

            facingDirection = defaultFacing;
            ApplyFacing();
        }

        private void FixedUpdate()
        {
            CheckGrounded();

            if (isInKnockback)
            {
                HandleKnockback();
            }
            else
            {
                HandleMovement();
            }
        }

        private void CheckGrounded()
        {
            // Simple ground check using raycast
            isGrounded = Physics.Raycast(
                rb.position + Vector3.up * 0.05f,
                Vector3.down,
                groundCheckDistance + 0.05f,
                groundLayers
            );
        }

        private void HandleKnockback()
        {
            knockbackTimer += Time.fixedDeltaTime;

            // Decay velocity for smooth visual deceleration
            float t = knockbackDrag * Time.fixedDeltaTime;
            knockbackVelocity.x = Mathf.Lerp(knockbackVelocity.x, 0f, t);
            knockbackVelocity.z = Mathf.Lerp(knockbackVelocity.z, 0f, t);

            if (rb.useGravity)
                rb.linearVelocity = new Vector3(knockbackVelocity.x, rb.linearVelocity.y, knockbackVelocity.z);
            else
            {
                knockbackVelocity.y = Mathf.Lerp(knockbackVelocity.y, 0f, t);
                rb.linearVelocity = knockbackVelocity;
            }

            // Timer is the only end condition — clean and predictable
            if (knockbackTimer >= knockbackDuration)
                EndKnockback();
        }

        private void EndKnockback()
        {
            isInKnockback = false;
            knockbackVelocity = Vector3.zero;
            OnKnockbackEnd?.Invoke();
        }

        private void HandleMovement()
        {
            if (!isMoving)
            {
                // When not moving, zero out velocity (preserve Y for gravity-based enemies)
                Vector3 vel = rb.linearVelocity;
                vel.x = 0f;
                vel.z = 0f;
                if (!rb.useGravity) vel.y = 0f; // Also zero Y for flyers
                rb.linearVelocity = vel;
                return;
            }

            Vector3 desiredVelocity = Vector3.zero;

            if (isDashing)
            {
                desiredVelocity = CalculateDashVelocity();
            }
            else if (isDirectionalMovement)
            {
                desiredVelocity = CalculateDirectionalVelocity();
            }
            else
            {
                desiredVelocity = CalculateTargetVelocity();
            }

            // Apply velocity - preserve Y for gravity-based enemies, use full velocity for flyers
            Vector3 newVelocity;
            if (rb.useGravity)
            {
                // Ground enemy - apply horizontal velocity, let physics handle Y
                newVelocity = new Vector3(desiredVelocity.x, rb.linearVelocity.y, desiredVelocity.z);
            }
            else
            {
                // Flying enemy - apply full velocity including Y
                newVelocity = desiredVelocity;
            }
            rb.linearVelocity = newVelocity;
        }

        private Vector3 CalculateTargetVelocity()
        {
            float distance = DistanceToTarget;

            if (distance <= stoppingDistance)
            {
                Stop();
                return Vector3.zero;
            }

            Vector3 direction = (targetPosition - rb.position).normalized;
            Vector3 planarDir = GetPlanarDirection(direction);

            float speed = currentSpeed > 0f ? currentSpeed : moveSpeed;
            return planarDir * speed;
        }

        private Vector3 CalculateDashVelocity()
        {
            float distance = DistanceToTarget;

            if (distance <= stoppingDistance)
            {
                Stop();
                return Vector3.zero;
            }

            Vector3 direction = (targetPosition - rb.position).normalized;
            Vector3 planarDir = GetPlanarDirection(direction);

            return planarDir * dashSpeed;
        }

        private Vector3 CalculateDirectionalVelocity()
        {
            if (moveDirection == Vector3.zero)
            {
                Stop();
                return Vector3.zero;
            }

            return moveDirection * currentSpeed;
        }

        /// <summary>
        /// Start moving towards a position.
        /// </summary>
        public void MoveTo(Vector3 position)
        {
            if (isInKnockback) return; // Don't allow movement during knockback

            targetPosition = position;
            isMoving = true;
            isDirectionalMovement = false;
            isDashing = false;
            currentSpeed = moveSpeed;
            UpdateFacingFromDirection(position - rb.position);
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
            if (isInKnockback) return;

            targetPosition = position;
            currentSpeed = speed;
            isMoving = true;
            isDirectionalMovement = false;
            isDashing = false;
            UpdateFacingFromDirection(position - rb.position);
        }

        /// <summary>
        /// Dash to a position in a straight line at high speed.
        /// Use for quick attack dashes - moves directly, ignores pathfinding.
        /// </summary>
        public void DashTo(Vector3 position, float speed)
        {
            if (isInKnockback) return;

            targetPosition = position;
            dashSpeed = speed;
            currentSpeed = speed;
            isMoving = true;
            isDirectionalMovement = false;
            isDashing = true;
            UpdateFacingFromDirection(position - rb.position);
        }

        /// <summary>
        /// Move continuously in a direction (doesn't stop until told to).
        /// </summary>
        public void MoveInDirection(Vector3 direction)
        {
            if (isInKnockback) return;

            moveDirection = GetPlanarDirection(direction);
            currentSpeed = moveSpeed;
            isMoving = true;
            isDirectionalMovement = true;
            isDashing = false;
            UpdateFacingFromDirection(direction);
        }

        /// <summary>
        /// Move continuously in a direction at specific speed.
        /// </summary>
        public void MoveInDirection(Vector3 direction, float speed)
        {
            if (isInKnockback) return;

            moveDirection = GetPlanarDirection(direction);
            currentSpeed = speed;
            isMoving = true;
            isDirectionalMovement = true;
            isDashing = false;
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
        /// Apply knockback force to the enemy.
        /// Returns true if knockback was applied, false if ignored.
        /// </summary>
        public bool ApplyKnockback(Vector3 force)
        {
            // Check if this enemy ignores knockback
            if (ignoreKnockback)
            {
                return false;
            }

            // Stop any current movement
            Stop();

            // Enter knockback state
            isInKnockback = true;
            knockbackVelocity = force;
            knockbackTimer = 0f;

            // Immediately apply the knockback velocity
            rb.linearVelocity = new Vector3(force.x, force.y, force.z);

            // Notify listeners (FSM)
            OnKnockbackStart?.Invoke();

            return true;
        }

        /// <summary>
        /// Force end knockback immediately (useful for certain states).
        /// </summary>
        public void CancelKnockback()
        {
            if (isInKnockback)
            {
                EndKnockback();
            }
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
            UpdateFacingFromDirection(targetPos - rb.position);
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
            Vector3 pos = Application.isPlaying && rb != null ? rb.position : transform.position;

            // Draw ground check
            Gizmos.color = isGrounded ? Color.green : Color.red;
            Gizmos.DrawLine(pos + Vector3.up * 0.05f, pos + Vector3.down * groundCheckDistance);

            if (isMoving)
            {
                Gizmos.color = isDashing ? Color.red : Color.green;
                if (isDirectionalMovement)
                    Gizmos.DrawRay(pos, moveDirection * 2f);
                else
                {
                    Gizmos.DrawLine(pos, targetPosition);
                    Gizmos.DrawWireSphere(targetPosition, 0.2f);
                }
            }

            // Draw knockback direction
            if (isInKnockback)
            {
                Gizmos.color = Color.magenta;
                Gizmos.DrawRay(pos, knockbackVelocity.normalized * 2f);
            }
        }
    }
}