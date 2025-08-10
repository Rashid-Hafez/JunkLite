// CharacterController.cs - 2.5D controller for 2D movement in 3D world
using UnityEngine;
using UnityEngine.Events;
using System.Collections;

namespace junklite
{
    public class CharacterController : MonoBehaviour
    {
        [Header("Movement Settings")]
        [SerializeField] private float m_JumpForce = 400f;
        [Range(0, .3f)][SerializeField] private float m_MovementSmoothing = .05f;
        [SerializeField] private bool m_AirControl = false;
        [SerializeField] private float limitFallSpeed = 25f;
        [SerializeField] private float moveSpeed = 10f;

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

        // State tracking
        private bool m_Grounded;
        private bool m_IsWall = false;
        private bool isWallSliding = false;
        private bool oldWallSlidding = false;
        private bool canCheck = false;

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
        /// <param name="jump">Jump input</param>
        /// <param name="dash">Dash input</param>
        public void Move(float move, bool jump, bool dash)
        {
            if (!canMove) return;

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
            // Normal movement (grounded or air control)
            else if (m_Grounded || m_AirControl)
            {
                // Limit fall speed
                if (m_Rigidbody.linearVelocity.y < -limitFallSpeed)
                {
                    Vector3 vel = m_Rigidbody.linearVelocity;
                    vel.y = -limitFallSpeed;
                    m_Rigidbody.linearVelocity = vel;
                }

                // Calculate target velocity (only X and Y movement for 2.5D)
                Vector3 targetVelocity = new Vector3(move * moveSpeed, m_Rigidbody.linearVelocity.y,
                                                     constrainToZAxis ? 0 : m_Rigidbody.linearVelocity.z);

                // Apply smoothed movement
                m_Rigidbody.linearVelocity = Vector3.SmoothDamp(m_Rigidbody.linearVelocity, targetVelocity,
                                                                ref velocity, m_MovementSmoothing);

                // Handle sprite flipping
                if (move > 0 && !m_FacingRight && !isWallSliding)
                    Flip();
                else if (move < 0 && m_FacingRight && !isWallSliding)
                    Flip();
            }

            // Handle jumping
            HandleJumping(jump);

            // Handle wall mechanics
            HandleWallMechanics(move, jump, dash);
        }

        private void HandleJumping(bool jump)
        {
            // Ground jump
            if (m_Grounded && jump)
            {
                PerformJump(m_JumpForce);
                canDoubleJump = true;
            }
            // Double jump
            else if (!m_Grounded && jump && canDoubleJump && !isWallSliding)
            {
                canDoubleJump = false;
                Vector3 vel = m_Rigidbody.linearVelocity;
                vel.y = 0;
                m_Rigidbody.linearVelocity = vel;
                PerformJump(m_JumpForce / 1.2f);
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

        private void PerformJump(float jumpForce)
        {
            m_Grounded = false;
            m_Rigidbody.AddForce(new Vector3(0f, jumpForce, 0f));
        }

        private void PerformWallJump()
        {
            m_Rigidbody.linearVelocity = Vector3.zero;
            m_Rigidbody.AddForce(new Vector3(transform.localScale.x * m_JumpForce * 1.2f, m_JumpForce, 0f));

            jumpWallStartX = transform.position.x;
            limitVelOnWallJump = true;
            canDoubleJump = true;
            canMove = false;

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