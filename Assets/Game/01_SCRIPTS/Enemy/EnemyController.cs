using UnityEngine;

namespace junklite
{
    /// <summary>
    /// Simple movement controller for enemies.
    /// Handles basic movement, facing direction, and ground detection.
    /// Brain tells it WHERE to go, this handles HOW.
    /// </summary>
    [RequireComponent(typeof(Rigidbody))]
    public class EnemyController : MonoBehaviour
    {
        [Header("Movement")]
        [SerializeField] private float moveSpeed = 3f;
        [SerializeField] private float acceleration = 10f;

        [Header("Visuals")]
        [SerializeField] private bool flipByScale = true;  // true = scale.x, false = rotate Y
        [SerializeField] private Transform visualTransform; // optional - if null, uses this.transform

        [Header("Ground Detection")]
        [SerializeField] private Transform groundCheck;
        [SerializeField] private float groundCheckRadius = 0.2f;
        [SerializeField] private LayerMask groundLayer;

        [Header("Wall Detection")]
        [SerializeField] private Transform wallCheck;
        [SerializeField] private float wallCheckDistance = 0.5f;

        [Header("Ledge Detection")]
        [SerializeField] private Transform ledgeCheck;
        [SerializeField] private float ledgeCheckDistance = 1f;

        // Components
        private Rigidbody rb;

        // State
        private float moveInput;
        private int facingDirection = 1; // 1 = right, -1 = left
        private bool isGrounded;
        private bool canMove = true;

        // Detection results
        private bool wallAhead;
        private bool ledgeAhead;

        // Public accessors
        public float MoveSpeed => moveSpeed;
        public int FacingDirection => facingDirection;
        public bool IsGrounded => isGrounded;
        public bool CanMove { get => canMove; set => canMove = value; }
        public Vector3 Velocity => rb != null ? rb.linearVelocity : Vector3.zero;
        public bool WallAhead => wallAhead;
        public bool LedgeAhead => ledgeAhead;
        public bool ShouldTurnAround => wallAhead || ledgeAhead;

        private void Awake()
        {
            rb = GetComponent<Rigidbody>();
            rb.freezeRotation = true;
        }

        private void FixedUpdate()
        {
            CheckGround();
            CheckWall();
            CheckLedge();
            ApplyMovement();
        }

        // ================= PUBLIC API =================

        /// <summary>
        /// Set movement input. -1 = left, 0 = stop, 1 = right
        /// </summary>
        public void SetMoveInput(float input)
        {
            moveInput = Mathf.Clamp(input, -1f, 1f);

            // Update facing direction
            int newDirection = facingDirection;
            if (input > 0.1f) newDirection = 1;
            else if (input < -0.1f) newDirection = -1;

            if (newDirection != facingDirection)
            {
                facingDirection = newDirection;
                FlipVisual();
            }
        }

        /// <summary>
        /// Stop all movement immediately.
        /// </summary>
        public void Stop()
        {
            moveInput = 0f;
            if (rb != null)
                rb.linearVelocity = new Vector3(0f, rb.linearVelocity.y, 0f);
        }

        /// <summary>
        /// Face a specific direction. 1 = right, -1 = left
        /// </summary>
        public void SetFacing(int direction)
        {
            if (direction == 0) return;
            int newDirection = direction > 0 ? 1 : -1;

            if (newDirection != facingDirection)
            {
                facingDirection = newDirection;
                FlipVisual();
            }
        }

        /// <summary>
        /// Flip facing direction.
        /// </summary>
        public void TurnAround()
        {
            facingDirection *= -1;
            FlipVisual();
        }

        /// <summary>
        /// Face toward a target position.
        /// </summary>
        public void FaceTarget(Vector3 targetPosition)
        {
            int newDirection = facingDirection;
            if (targetPosition.x > transform.position.x)
                newDirection = 1;
            else if (targetPosition.x < transform.position.x)
                newDirection = -1;

            if (newDirection != facingDirection)
            {
                facingDirection = newDirection;
                FlipVisual();
            }
        }

        /// <summary>
        /// Flip the visual to match facing direction.
        /// </summary>
        private void FlipVisual()
        {
            Transform t = visualTransform != null ? visualTransform : transform;

            if (flipByScale)
            {
                // Flip using scale
                Vector3 scale = t.localScale;
                scale.x = Mathf.Abs(scale.x) * facingDirection;
                t.localScale = scale;
            }
            else
            {
                // Flip using Y rotation
                t.rotation = Quaternion.Euler(0f, facingDirection > 0 ? 0f : 180f, 0f);
            }
        }

        /// <summary>
        /// Move toward a target position.
        /// </summary>
        public void MoveToward(Vector3 targetPosition)
        {
            FaceTarget(targetPosition);
            SetMoveInput(facingDirection);
        }

        /// <summary>
        /// Move away from a target position.
        /// </summary>
        public void MoveAway(Vector3 targetPosition)
        {
            FaceTarget(targetPosition);
            SetMoveInput(-facingDirection);
        }

        // ================= DETECTION =================

        private void CheckGround()
        {
            if (groundCheck == null)
            {
                isGrounded = true;
                return;
            }

            isGrounded = Physics.CheckSphere(groundCheck.position, groundCheckRadius, groundLayer);
        }

        private void CheckWall()
        {
            if (wallCheck == null)
            {
                wallAhead = false;
                return;
            }

            Vector3 direction = Vector3.right * facingDirection;
            wallAhead = Physics.Raycast(wallCheck.position, direction, wallCheckDistance, groundLayer);
        }

        private void CheckLedge()
        {
            if (ledgeCheck == null)
            {
                ledgeAhead = false;
                return;
            }

            // Cast down from in front of enemy - if no ground, there's a ledge
            Vector3 rayOrigin = ledgeCheck.position + Vector3.right * facingDirection * 0.1f;
            ledgeAhead = !Physics.Raycast(rayOrigin, Vector3.down, ledgeCheckDistance, groundLayer);
        }

        // ================= MOVEMENT =================

        private void ApplyMovement()
        {
            if (!canMove || rb == null)
            {
                return;
            }

            // Calculate target velocity
            float targetVelX = moveInput * moveSpeed;

            // Smoothly accelerate toward target
            float currentVelX = rb.linearVelocity.x;
            float newVelX = Mathf.MoveTowards(currentVelX, targetVelX, acceleration * Time.fixedDeltaTime);

            // Apply velocity (keep Y for gravity)
            rb.linearVelocity = new Vector3(newVelX, rb.linearVelocity.y, 0f);
        }

        // ================= DEBUG =================

        private void OnDrawGizmosSelected()
        {
            // Ground check
            if (groundCheck != null)
            {
                Gizmos.color = isGrounded ? Color.green : Color.red;
                Gizmos.DrawWireSphere(groundCheck.position, groundCheckRadius);
            }

            // Wall check
            if (wallCheck != null)
            {
                Gizmos.color = wallAhead ? Color.red : Color.green;
                Vector3 dir = Vector3.right * (Application.isPlaying ? facingDirection : 1);
                Gizmos.DrawRay(wallCheck.position, dir * wallCheckDistance);
            }

            // Ledge check
            if (ledgeCheck != null)
            {
                Gizmos.color = ledgeAhead ? Color.red : Color.green;
                Vector3 rayOrigin = ledgeCheck.position + Vector3.right * (Application.isPlaying ? facingDirection : 1) * 0.1f;
                Gizmos.DrawRay(rayOrigin, Vector3.down * ledgeCheckDistance);
            }

            // Facing direction
            Gizmos.color = Color.blue;
            Gizmos.DrawRay(transform.position, Vector3.right * facingDirection * 1f);
        }
    }
}