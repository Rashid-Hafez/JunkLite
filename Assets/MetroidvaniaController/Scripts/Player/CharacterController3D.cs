// CharacterController3D.cs - 2.5D controller for 2D movement in 3D world
using UnityEngine;
using UnityEngine.Events;
using System.Collections;

namespace junklite
{
    [DefaultExecutionOrder(10)]
    public class CharacterController3D : MonoBehaviour
    {
        [Header("Movement Settings")]
        [SerializeField] private float m_JumpHeight = 3f; // Jump height in units
        [SerializeField] private float m_TimeToJumpApex = 0.4f; // Time to reach jump peak
        [Range(0, .3f)][SerializeField] private float m_MovementSmoothing = .05f;
        [SerializeField] private bool m_AirControl = false;
        [SerializeField] private float m_AirControlAmount = 0.5f; // Multiplier for air movement (0-1)
        [SerializeField] private float limitFallSpeed = 25f;
        [SerializeField] private float moveSpeed = 10f;

        [Header("Jump Feel")]
        [SerializeField] private float m_FallMultiplier = 2.5f; // Makes falling faster than rising
        [SerializeField] private float m_LowJumpMultiplier = 2f; // For variable jump height
        [SerializeField] private float m_CoyoteTime = 0.15f; // Grace period for jumping after leaving ground
        [SerializeField] private float m_JumpBufferTime = 0.15f; // Input buffer for jump

        [Header("Movement Constraint")]
        [SerializeField] private bool constrainToZAxis = true;
        [SerializeField] private bool lockToCurrentZPosition = true; // Auto-lock to current Z position
        [SerializeField] private float zPosition = 0f; // The Z position to lock the character to (if not using current)

        [Header("Dash Settings")]
        [SerializeField] private float m_DashForce = 25f;
        [SerializeField] private float dashCooldown = 0.5f;
        [SerializeField] private float dashDuration = 0.1f;

        [Header("Ground & Wall Detection")]
        [SerializeField] private LayerMask m_WhatIsGround;
        [SerializeField] private Transform m_GroundCheck;
        [SerializeField] private Transform m_WallCheck;
        [SerializeField] private float groundCheckDistance = 0.3f;
        [SerializeField] private float wallCheckDistance = 0.3f;

        [Header("Jump Settings")]
        public bool canDoubleJump = true;

        [Header("Events")]
        public UnityEvent OnFallEvent;
        public UnityEvent OnLandEvent;

        // Constants
        const float k_GroundedRadius = .2f;

        // Private variables
        private Rigidbody m_Rigidbody;
        private Vector3 velocity = Vector3.zero;
        private bool m_FacingRight = true;

        // Jump physics calculations
        private float m_Gravity;
        private float m_JumpVelocity;

        // State tracking
        private bool m_Grounded;
        private bool m_IsWall = false;
        private bool isWallSliding = false;
        private bool oldWallSlidding = false;
        private bool canCheck = false;

        // Jump timing
        private float m_CoyoteTimeCounter;
        private float m_JumpBufferCounter;
        private bool m_IsJumping = false;

        // Dash system
        private bool canDash = true;
        private bool isDashing = false;

        // Wall jump system
        private float jumpWallStartX = 0;
        private float jumpWallDistX = 0;
        private bool limitVelOnWallJump = false;
        private bool canMove = true;

        // Properties
        public bool isGrounded => m_Grounded;
        public bool facingRight => m_FacingRight;
        public Vector3 velocity3D => m_Rigidbody.linearVelocity;
        public bool CanMove { get => canMove; set => canMove = value; }
        public bool IsWallSliding => isWallSliding;
        public bool IsDashing => isDashing;

        private void Awake()
        {
            m_Rigidbody = GetComponent<Rigidbody>();

            // Configure Rigidbody for 2.5D movement
            if (m_Rigidbody != null)
            {
                // Freeze rotation on X and Z axes to keep character upright
                m_Rigidbody.freezeRotation = true;

                // Disable Unity's gravity - we'll handle it ourselves for better control
                m_Rigidbody.useGravity = false;

                // Set drag to 0 for crisp movement
                m_Rigidbody.linearDamping = 0f;

                // If constraining to Z axis, freeze Z position
                if (constrainToZAxis)
                {
                    m_Rigidbody.constraints = RigidbodyConstraints.FreezePositionZ |
                                             RigidbodyConstraints.FreezeRotationX |
                                             RigidbodyConstraints.FreezeRotationZ;
                }
                else
                {
                    m_Rigidbody.constraints = RigidbodyConstraints.FreezeRotationX |
                                             RigidbodyConstraints.FreezeRotationZ;
                }
            }

            // Calculate gravity and jump velocity based on desired jump height and time to apex
            m_Gravity = -(2 * m_JumpHeight) / Mathf.Pow(m_TimeToJumpApex, 2);
            m_JumpVelocity = Mathf.Abs(m_Gravity) * m_TimeToJumpApex;

            if (OnFallEvent == null)
                OnFallEvent = new UnityEvent();
            if (OnLandEvent == null)
                OnLandEvent = new UnityEvent();
        }

        private void Start()
        {
            // Set initial Z position if constrained
            if (constrainToZAxis)
            {
                // If lockToCurrentZPosition is true, use the character's current Z position
                if (lockToCurrentZPosition)
                {
                    zPosition = transform.position.z;
                }
                else
                {
                    // Otherwise, set to the specified Z position
                    Vector3 pos = transform.position;
                    pos.z = zPosition;
                    transform.position = pos;
                }
            }
        }

        private void FixedUpdate()
        {
            bool wasGrounded = m_Grounded;
            CheckGrounded(wasGrounded);
            CheckWall();
            HandleWallJumpLimiting();

            // Apply custom gravity
            ApplyGravity();

            // Update coyote time
            if (m_Grounded)
                m_CoyoteTimeCounter = m_CoyoteTime;
            else
                m_CoyoteTimeCounter -= Time.fixedDeltaTime;

            // Ensure Z position stays locked if constrained
            if (constrainToZAxis)
            {
                Vector3 pos = transform.position;
                if (Mathf.Abs(pos.z - zPosition) > 0.01f)
                {
                    pos.z = zPosition;
                    transform.position = pos;
                }
            }
        }

        private void ApplyGravity()
        {
            Vector3 vel = m_Rigidbody.linearVelocity;

            // Apply different gravity when falling vs rising for better game feel
            if (vel.y < 0)
            {
                // Falling - apply extra gravity for snappier descent
                vel.y += m_Gravity * m_FallMultiplier * Time.fixedDeltaTime;
                m_IsJumping = false;
            }
            else if (vel.y > 0 && !m_IsJumping)
            {
                // Rising but jump button released - fall faster for variable jump height
                vel.y += m_Gravity * m_LowJumpMultiplier * Time.fixedDeltaTime;
            }
            else
            {
                // Normal gravity during jump
                vel.y += m_Gravity * Time.fixedDeltaTime;
            }

            // Clamp fall speed
            if (vel.y < -limitFallSpeed)
                vel.y = -limitFallSpeed;

            m_Rigidbody.linearVelocity = vel;
        }

        private void CheckGrounded(bool wasGrounded)
        {
            m_Grounded = false;

            // Use SphereCast for 3D ground detection
            RaycastHit hit;
            Vector3 spherePosition = m_GroundCheck.position + Vector3.up * k_GroundedRadius;

            if (Physics.SphereCast(spherePosition, k_GroundedRadius, Vector3.down, out hit,
                groundCheckDistance, m_WhatIsGround))
            {
                m_Grounded = true;
                if (!wasGrounded)
                {
                    OnLandEvent.Invoke();
                    canDoubleJump = true;
                    if (m_Rigidbody.linearVelocity.y < 0f)
                        limitVelOnWallJump = false;
                }
            }
        }

        private void CheckWall()
        {
            m_IsWall = false;

            if (!m_Grounded)
            {
                OnFallEvent.Invoke();

                // Use SphereCast for 3D wall detection
                Vector3 wallCheckDirection = m_FacingRight ? Vector3.right : Vector3.left;
                RaycastHit hit;

                if (Physics.SphereCast(m_WallCheck.position, k_GroundedRadius, wallCheckDirection,
                    out hit, wallCheckDistance, m_WhatIsGround))
                {
                    isDashing = false;
                    m_IsWall = true;
                }
            }
        }

        private void HandleWallJumpLimiting()
        {
            if (limitVelOnWallJump)
            {
                if (m_Rigidbody.linearVelocity.y < -0.5f)
                    limitVelOnWallJump = false;

                jumpWallDistX = (jumpWallStartX - transform.position.x) * transform.localScale.x;

                if (jumpWallDistX < -0.5f && jumpWallDistX > -1f)
                {
                    canMove = true;
                }
                else if (jumpWallDistX < -1f && jumpWallDistX >= -2f)
                {
                    canMove = true;
                    m_Rigidbody.linearVelocity = new Vector3(10f * transform.localScale.x, m_Rigidbody.linearVelocity.y, 0);
                }
                else if (jumpWallDistX < -2f || jumpWallDistX > 0)
                {
                    limitVelOnWallJump = false;
                    m_Rigidbody.linearVelocity = new Vector3(0, m_Rigidbody.linearVelocity.y, 0);
                }
            }
        }

        /// <summary>
        /// Main movement method - can be called by any entity
        /// </summary>
        /// <param name="move">Horizontal movement input (-1 to 1)</param>
        /// <param name="jumpPressed">Jump button is currently pressed</param>
        /// <param name="jumpReleased">Jump button was released this frame</param>
        /// <param name="dash">Dash input</param>
        public void Move(float move, bool jumpPressed, bool jumpReleased, bool dash)
        {
            if (!canMove) return;

            // Update jump buffer
            if (jumpPressed)
                m_JumpBufferCounter = m_JumpBufferTime;
            else
                m_JumpBufferCounter -= Time.fixedDeltaTime;

            // Handle jump release for variable jump height
            if (jumpReleased && m_IsJumping && m_Rigidbody.linearVelocity.y > 0)
            {
                m_IsJumping = false;
            }

            // Handle dashing
            if (dash && canDash && !isWallSliding)
            {
                StartCoroutine(DashCooldown());
            }

            if (isDashing)
            {
                m_Rigidbody.linearVelocity = new Vector3(transform.localScale.x * m_DashForce,
                                                         m_Rigidbody.linearVelocity.y,
                                                         constrainToZAxis ? 0 : m_Rigidbody.linearVelocity.z);
            }
            // Normal movement
            else
            {
                // Determine movement speed based on grounded state
                float currentMoveSpeed = moveSpeed;
                float currentSmoothing = m_MovementSmoothing;

                if (!m_Grounded)
                {
                    // In air - apply air control if enabled
                    if (!m_AirControl)
                    {
                        // No air control - maintain current X velocity unless wall jumping
                        if (!limitVelOnWallJump)
                            return;
                    }
                    else
                    {
                        // Air control enabled - reduce movement speed
                        currentMoveSpeed *= m_AirControlAmount;
                        currentSmoothing = m_MovementSmoothing * 0.5f; // Less responsive in air
                    }
                }

                // Calculate target velocity
                Vector3 targetVelocity = new Vector3(move * currentMoveSpeed, m_Rigidbody.linearVelocity.y,
                                                     constrainToZAxis ? 0 : m_Rigidbody.linearVelocity.z);

                // Apply smoothed movement
                Vector3 currentVel = m_Rigidbody.linearVelocity;
                currentVel.x = Mathf.Lerp(currentVel.x, targetVelocity.x, 1f - Mathf.Pow(currentSmoothing, Time.fixedDeltaTime));
                m_Rigidbody.linearVelocity = currentVel;

                // Handle sprite flipping - only flip if there's significant input and not wall sliding
                if (!isWallSliding && Mathf.Abs(move) > 0.1f) // Dead zone to prevent flip loops
                {
                    if (move > 0 && !m_FacingRight)
                        Flip();
                    else if (move < 0 && m_FacingRight)
                        Flip();
                }
            }

            // Handle jumping with buffer and coyote time
            HandleJumping(jumpPressed);

            // Handle wall mechanics
            HandleWallMechanics(move, jumpPressed, dash);
        }

        // Overload for backward compatibility
        public void Move(float move, bool jump, bool dash)
        {
            Move(move, jump, false, dash);
        }

        private void HandleJumping(bool jumpPressed)
        {
            // Check for jump with coyote time and jump buffer
            bool canJump = (m_CoyoteTimeCounter > 0 || m_Grounded) && m_JumpBufferCounter > 0;

            // Ground jump with buffer
            if (canJump)
            {
                PerformJump(m_JumpVelocity);
                m_JumpBufferCounter = 0;
                m_CoyoteTimeCounter = 0;
                m_IsJumping = true;
                canDoubleJump = true;
            }
            // Double jump
            else if (!m_Grounded && jumpPressed && canDoubleJump && !isWallSliding && m_JumpBufferCounter > 0)
            {
                canDoubleJump = false;
                // Cut current velocity for consistent double jump
                Vector3 vel = m_Rigidbody.linearVelocity;
                vel.y = 0;
                m_Rigidbody.linearVelocity = vel;
                PerformJump(m_JumpVelocity * 0.9f); // Slightly lower double jump
                m_IsJumping = true;
                m_JumpBufferCounter = 0;
            }
        }

        private void HandleWallMechanics(float move, bool jump, bool dash)
        {
            if (m_IsWall && !m_Grounded)
            {
                if (!oldWallSlidding && m_Rigidbody.linearVelocity.y < 0 || isDashing)
                {
                    StartWallSlide();
                }

                isDashing = false;

                if (isWallSliding)
                {
                    if (move * transform.localScale.x > 0.1f)
                    {
                        StartCoroutine(WaitToEndSliding());
                    }
                    else
                    {
                        oldWallSlidding = true;
                        m_Rigidbody.linearVelocity = new Vector3(-transform.localScale.x * 2, -5,
                                                                 constrainToZAxis ? 0 : m_Rigidbody.linearVelocity.z);
                    }
                }

                // Wall jump
                if (jump && isWallSliding)
                {
                    PerformWallJump();
                }
                // Dash from wall
                else if (dash && canDash)
                {
                    EndWallSlide();
                    StartCoroutine(DashCooldown());
                }
            }
            else if (isWallSliding && !m_IsWall && canCheck)
            {
                EndWallSlide();
            }
        }

        private void PerformJump(float jumpVelocity)
        {
            m_Grounded = false;
            // Set Y velocity directly for instant, snappy jump
            Vector3 vel = m_Rigidbody.linearVelocity;
            vel.y = jumpVelocity;
            m_Rigidbody.linearVelocity = vel;
        }

        private void PerformWallJump()
        {
            // Reset velocity for consistent wall jump
            Vector3 vel = m_Rigidbody.linearVelocity;
            vel.x = transform.localScale.x * m_JumpVelocity * 0.7f; // Horizontal push
            vel.y = m_JumpVelocity * 0.9f; // Slightly lower than normal jump
            m_Rigidbody.linearVelocity = vel;

            jumpWallStartX = transform.position.x;
            limitVelOnWallJump = true;
            canDoubleJump = true;
            canMove = false;
            m_IsJumping = true;

            EndWallSlide();
        }

        private void StartWallSlide()
        {
            isWallSliding = true;
            m_WallCheck.localPosition = new Vector3(-m_WallCheck.localPosition.x,
                                                    m_WallCheck.localPosition.y,
                                                    m_WallCheck.localPosition.z);
            Flip();
            StartCoroutine(WaitToCheck(0.1f));
            canDoubleJump = true;
        }

        private void EndWallSlide()
        {
            isWallSliding = false;
            oldWallSlidding = false;
            m_WallCheck.localPosition = new Vector3(Mathf.Abs(m_WallCheck.localPosition.x),
                                                    m_WallCheck.localPosition.y,
                                                    m_WallCheck.localPosition.z);
            canDoubleJump = true;
        }

        private void Flip()
        {
            m_FacingRight = !m_FacingRight;

            // For 3D, you might want to rotate instead of scale
            // Option 1: Scale flip (works with sprites)
            Vector3 theScale = transform.localScale;
            theScale.x *= -1;
            transform.localScale = theScale;

            // Option 2: Rotation flip (uncomment if using 3D models)
            // transform.Rotate(0, 180, 0);
        }

        public void SetMovementEnabled(bool enabled)
        {
            canMove = enabled;
        }

        public void ApplyKnockback(Vector3 force)
        {
            m_Rigidbody.linearVelocity = Vector3.zero;
            m_Rigidbody.AddForce(force);
        }

        // Overload for 2D compatibility
        public void ApplyKnockback(Vector2 force)
        {
            ApplyKnockback(new Vector3(force.x, force.y, 0));
        }

        // Coroutines
        IEnumerator DashCooldown()
        {
            isDashing = true;
            canDash = false;
            yield return new WaitForSeconds(dashDuration);
            isDashing = false;
            yield return new WaitForSeconds(dashCooldown);
            canDash = true;
        }

        IEnumerator WaitToCheck(float time)
        {
            canCheck = false;
            yield return new WaitForSeconds(time);
            canCheck = true;
        }

        IEnumerator WaitToEndSliding()
        {
            yield return new WaitForSeconds(0.1f);
            EndWallSlide();
        }

        // Helper method to visualize detection areas in Scene view
        private void OnDrawGizmosSelected()
        {
            // Ground check
            if (m_GroundCheck != null)
            {
                Gizmos.color = m_Grounded ? Color.green : Color.red;
                Gizmos.DrawWireSphere(m_GroundCheck.position, k_GroundedRadius);
                Gizmos.DrawLine(m_GroundCheck.position, m_GroundCheck.position + Vector3.down * groundCheckDistance);
            }

            // Wall check
            if (m_WallCheck != null)
            {
                Gizmos.color = m_IsWall ? Color.green : Color.yellow;
                Vector3 wallCheckDirection = m_FacingRight ? Vector3.right : Vector3.left;
                Gizmos.DrawWireSphere(m_WallCheck.position, k_GroundedRadius);
                Gizmos.DrawLine(m_WallCheck.position, m_WallCheck.position + wallCheckDirection * wallCheckDistance);
            }
        }
    }
}