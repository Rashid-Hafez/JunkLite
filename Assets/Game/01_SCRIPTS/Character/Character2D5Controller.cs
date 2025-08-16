using UnityEngine;

namespace junklite
{
    [RequireComponent(typeof(Rigidbody))]
    public class Character2D5Controller : MonoBehaviour
    {
        [Header("Movement Settings")]
        [SerializeField] private float moveSpeed = 5f;
        [SerializeField] private float jumpForce = 10f;
        [SerializeField] private float groundCheckDistance = 0.1f;

        [Header("Dash Settings")]
        [SerializeField] private float dashForce = 20f;
        [SerializeField] private float dashDuration = 0.2f;
        [SerializeField] private float dashCooldown = 1f;
        [SerializeField] private bool canDashInAir = true;
        [SerializeField] private bool dashResetsGravity = true;
        [SerializeField] private AnimationCurve dashCurve = AnimationCurve.EaseInOut(0f, 1f, 1f, 0f);

        [Header("2.5D Settings")]
        [SerializeField] private bool snapToZPosition = true;
        [SerializeField] private float fixedZPosition = 0f;
        [SerializeField] private bool allowZMovement = false;
        [SerializeField] private float zMoveSpeed = 3f;
        [SerializeField] private float minZPosition = -5f;
        [SerializeField] private float maxZPosition = 5f;

        [Header("Physics Settings")]
        [SerializeField] private LayerMask groundLayerMask = 1;
        [SerializeField] private float gravityMultiplier = 1f;
        [SerializeField] private float maxFallSpeed = -20f;

        [Header("Character Settings")]
        [SerializeField] private bool faceMovementDirection = true;
        [SerializeField] private FacingMode facingMode = FacingMode.ScaleFlip;
        [SerializeField] private float rotationSpeed = 10f;

        public enum FacingMode
        {
            ScaleFlip,      // Flip sprite by scaling X axis (good for 2D sprites)
            YAxisRotation   // Rotate 180 degrees on Y axis (good for 3D models)
        }

        // Components
        private Rigidbody rb;
        private Collider col;

        // Movement state
        private Vector3 moveInput;
        private bool isGrounded;
        private bool canMove = true;

        // Dash state
        private bool isDashing = false;
        private float dashTimer = 0f;
        private float dashCooldownTimer = 0f;
        private Vector3 dashDirection;
        private float originalMoveSpeed;

        // Events
        public System.Action<bool> OnGroundedStateChanged;
        public System.Action<Vector3> OnMovementChanged;
        public System.Action OnDashStarted;
        public System.Action OnDashEnded;

        // Properties
        public bool IsGrounded => isGrounded;
        public bool CanMove { get => canMove; set => canMove = value; }
        public Vector3 Velocity => rb.linearVelocity;
        public float MoveSpeed { get => moveSpeed; set => moveSpeed = value; }
        public bool SnapToZPosition { get => snapToZPosition; set => snapToZPosition = value; }
        public float FixedZPosition { get => fixedZPosition; set => fixedZPosition = value; }
        public bool IsFacingRight => facingMode == FacingMode.ScaleFlip ?
            transform.localScale.x > 0 :
            Mathf.Abs(transform.eulerAngles.y) < 90f;
        public bool IsDashing => isDashing;
        public bool CanDash => dashCooldownTimer <= 0f && (isGrounded || canDashInAir) && canMove;

        private void Awake()
        {
            rb = GetComponent<Rigidbody>();
            col = GetComponent<Collider>();

            // Configure rigidbody for 2.5D
            rb.freezeRotation = true;

            // Set fixed Z position to current position for platformers
            if (snapToZPosition)
            {
                fixedZPosition = transform.position.z;
                transform.position = new Vector3(transform.position.x, transform.position.y, fixedZPosition);
            }

            // Store original move speed
            originalMoveSpeed = moveSpeed;
        }

        private void Update()
        {
            CheckGrounded();
            HandleZPositionConstraint();
            UpdateDash();
        }

        private void FixedUpdate()
        {
            if (isDashing)
            {
                ApplyDashMovement();
            }
            else
            {
                ApplyMovement();
                ApplyGravity();
            }
            ClampFallSpeed();
        }

        /// <summary>
        /// Set movement input for the character
        /// </summary>
        /// <param name="horizontal">Horizontal input (-1 to 1)</param>
        /// <param name="vertical">Vertical input (for Z-axis movement, -1 to 1)</param>
        public void SetMovementInput(float horizontal, float vertical = 0f)
        {
            if (!canMove)
            {
                moveInput = Vector3.zero;
                return;
            }

            moveInput.x = horizontal;
            moveInput.z = allowZMovement && !snapToZPosition ? vertical : 0f;

            OnMovementChanged?.Invoke(moveInput);
        }

        /// <summary>
        /// Make the character jump
        /// </summary>
        public void Jump()
        {
            if (!canMove || !isGrounded || isDashing) return;

            rb.linearVelocity = new Vector3(rb.linearVelocity.x, jumpForce, rb.linearVelocity.z);
        }

        /// <summary>
        /// Make the character dash
        /// </summary>
        public void Dash()
        {
            if (!CanDash) return;

            // Determine dash direction
            Vector3 direction = Vector3.right * (IsFacingRight ? 1 : -1);

            // Allow dash in input direction if there's movement input
            if (Mathf.Abs(moveInput.x) > 0.1f)
            {
                direction = Vector3.right * Mathf.Sign(moveInput.x);
            }

            StartDash(direction);
        }

        /// <summary>
        /// Start dash in specific direction
        /// </summary>
        public void StartDash(Vector3 direction)
        {
            if (!CanDash) return;

            isDashing = true;
            dashTimer = 0f;
            dashCooldownTimer = dashCooldown;
            dashDirection = direction.normalized;

            // Reset gravity if enabled
            if (dashResetsGravity)
            {
                rb.linearVelocity = new Vector3(rb.linearVelocity.x, 0f, rb.linearVelocity.z);
            }

            OnDashStarted?.Invoke();
        }

        /// <summary>
        /// Add external force to the character
        /// </summary>
        public void AddForce(Vector3 force, ForceMode forceMode = ForceMode.Impulse)
        {
            rb.AddForce(force, forceMode);
        }

        /// <summary>
        /// Teleport character to position
        /// </summary>
        public void TeleportTo(Vector3 position)
        {
            if (snapToZPosition)
            {
                position.z = fixedZPosition;
            }

            transform.position = position;
            rb.linearVelocity = Vector3.zero;
        }

        /// <summary>
        /// Set the facing direction of the character
        /// </summary>
        /// <param name="facingRight">True to face right, false to face left</param>
        public void SetFacingDirection(bool facingRight)
        {
            switch (facingMode)
            {
                case FacingMode.ScaleFlip:
                    Vector3 scale = transform.localScale;
                    scale.x = Mathf.Abs(scale.x) * (facingRight ? 1 : -1);
                    transform.localScale = scale;
                    break;

                case FacingMode.YAxisRotation:
                    Vector3 rotation = transform.eulerAngles;
                    rotation.y = facingRight ? 0f : 180f;
                    transform.eulerAngles = rotation;
                    break;
            }
        }

        private void UpdateDash()
        {
            // Update dash cooldown
            if (dashCooldownTimer > 0f)
            {
                dashCooldownTimer -= Time.deltaTime;
            }

            // Update dash timer
            if (isDashing)
            {
                dashTimer += Time.deltaTime;

                if (dashTimer >= dashDuration)
                {
                    EndDash();
                }
            }
        }

        private void ApplyDashMovement()
        {
            // Calculate dash force based on curve
            float normalizedTime = dashTimer / dashDuration;
            float curveValue = dashCurve.Evaluate(normalizedTime);
            Vector3 dashVelocity = dashDirection * dashForce * curveValue;

            // Apply dash velocity (preserve Y if not resetting gravity)
            if (dashResetsGravity)
            {
                rb.linearVelocity = new Vector3(dashVelocity.x, 0f, dashVelocity.z);
            }
            else
            {
                rb.linearVelocity = new Vector3(dashVelocity.x, rb.linearVelocity.y, dashVelocity.z);
            }

            // Update facing direction during dash
            if (faceMovementDirection && Mathf.Abs(dashDirection.x) > 0.1f)
            {
                HandleFacingDirection(dashDirection.x);
            }
        }

        private void EndDash()
        {
            isDashing = false;
            dashTimer = 0f;
            OnDashEnded?.Invoke();
        }

        private void ApplyMovement()
        {
            if (!canMove) return;

            Vector3 movement = Vector3.zero;

            // Horizontal movement
            movement.x = moveInput.x * moveSpeed;

            // Z movement (depth)
            if (allowZMovement && !snapToZPosition)
            {
                movement.z = moveInput.z * zMoveSpeed;
            }

            // Apply movement while preserving Y velocity
            rb.linearVelocity = new Vector3(movement.x, rb.linearVelocity.y, movement.z);

            // Handle character facing direction
            if (faceMovementDirection && Mathf.Abs(moveInput.x) > 0.1f)
            {
                HandleFacingDirection(moveInput.x);
            }
        }

        private void CheckGrounded()
        {
            bool wasGrounded = isGrounded;

            Vector3 rayOrigin = col.bounds.center;
            float rayDistance = col.bounds.extents.y + groundCheckDistance;

            isGrounded = Physics.Raycast(rayOrigin, Vector3.down, rayDistance, groundLayerMask);

            if (wasGrounded != isGrounded)
            {
                OnGroundedStateChanged?.Invoke(isGrounded);
            }
        }

        private void HandleZPositionConstraint()
        {
            if (snapToZPosition)
            {
                Vector3 pos = transform.position;
                if (Mathf.Abs(pos.z - fixedZPosition) > 0.001f)
                {
                    transform.position = new Vector3(pos.x, pos.y, fixedZPosition);
                    rb.linearVelocity = new Vector3(rb.linearVelocity.x, rb.linearVelocity.y, 0f);
                }
            }
            else if (allowZMovement)
            {
                // Clamp Z position within bounds
                Vector3 pos = transform.position;
                pos.z = Mathf.Clamp(pos.z, minZPosition, maxZPosition);
                transform.position = pos;
            }
        }

        private void ApplyGravity()
        {
            if (!isGrounded)
            {
                rb.AddForce(Physics.gravity * gravityMultiplier, ForceMode.Acceleration);
            }
        }

        private void HandleFacingDirection(float horizontalInput)
        {
            bool facingRight = horizontalInput > 0;

            switch (facingMode)
            {
                case FacingMode.ScaleFlip:
                    // Flip sprite by scaling X axis (instant flip for platformers)
                    Vector3 scale = transform.localScale;
                    scale.x = Mathf.Abs(scale.x) * (facingRight ? 1 : -1);
                    transform.localScale = scale;
                    break;

                case FacingMode.YAxisRotation:
                    // Rotate on Y axis (smooth rotation for 3D models)
                    float targetYRotation = facingRight ? 0f : 180f;
                    Vector3 currentRotation = transform.eulerAngles;
                    currentRotation.y = Mathf.LerpAngle(currentRotation.y, targetYRotation, rotationSpeed * Time.fixedDeltaTime);
                    transform.eulerAngles = currentRotation;
                    break;
            }
        }

        private void ClampFallSpeed()
        {
            if (rb.linearVelocity.y < maxFallSpeed)
            {
                rb.linearVelocity = new Vector3(rb.linearVelocity.x, maxFallSpeed, rb.linearVelocity.z);
            }
        }

        // Debug visualization
        private void OnDrawGizmosSelected()
        {
            if (col == null) col = GetComponent<Collider>();

            // Draw ground check ray
            Gizmos.color = isGrounded ? Color.green : Color.red;
            Vector3 rayOrigin = col.bounds.center;
            Vector3 rayEnd = rayOrigin + Vector3.down * (col.bounds.extents.y + groundCheckDistance);
            Gizmos.DrawLine(rayOrigin, rayEnd);

            // Draw Z position constraint
            if (snapToZPosition)
            {
                Gizmos.color = Color.blue;
                Vector3 pos = transform.position;
                Gizmos.DrawLine(new Vector3(pos.x - 1f, pos.y, fixedZPosition),
                               new Vector3(pos.x + 1f, pos.y, fixedZPosition));
            }

            // Draw Z movement bounds
            if (allowZMovement && !snapToZPosition)
            {
                Gizmos.color = Color.yellow;
                Vector3 pos = transform.position;
                Gizmos.DrawLine(new Vector3(pos.x, pos.y, minZPosition),
                               new Vector3(pos.x, pos.y, maxZPosition));
            }

            // Draw dash range visualization
            if (isDashing)
            {
                Gizmos.color = Color.cyan;
                Vector3 dashEnd = transform.position + dashDirection * dashForce * 0.1f;
                Gizmos.DrawLine(transform.position, dashEnd);
                Gizmos.DrawWireSphere(dashEnd, 0.2f);
            }
        }
    }
}