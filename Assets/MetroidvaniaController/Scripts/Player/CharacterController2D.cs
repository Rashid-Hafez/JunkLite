// CharacterController2D.cs - Generic controller for all entities
using UnityEngine;
using UnityEngine.Events;
using System.Collections;

namespace junklite
{
    public class CharacterController2D : MonoBehaviour
    {
        [Header("Movement Settings")]
        [SerializeField] private float m_JumpForce = 400f;
        [Range(0, .3f)][SerializeField] private float m_MovementSmoothing = .05f;
        [SerializeField] private bool m_AirControl = false;
        [SerializeField] private float limitFallSpeed = 25f;

        [Header("Dash Settings")]
        [SerializeField] private float m_DashForce = 25f;
        [SerializeField] private float dashCooldown = 0.5f;
        [SerializeField] private float dashDuration = 0.1f;

        [Header("Ground & Wall Detection")]
        [SerializeField] private LayerMask m_WhatIsGround;
        [SerializeField] private Transform m_GroundCheck;
        [SerializeField] private Transform m_WallCheck;

        [Header("Jump Settings")]
        public bool canDoubleJump = true;

        [Header("Events")]
        public UnityEvent OnFallEvent;
        public UnityEvent OnLandEvent;

        // Constants
        const float k_GroundedRadius = .2f;

        // Private variables
        private Rigidbody2D m_Rigidbody2D;
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
        public Vector2 velocity2D => m_Rigidbody2D.linearVelocity;
        public bool CanMove { get => canMove; set => canMove = value; }
        public bool IsWallSliding => isWallSliding;
        public bool IsDashing => isDashing;

        private void Awake()
        {
            m_Rigidbody2D = GetComponent<Rigidbody2D>();

            if (OnFallEvent == null)
                OnFallEvent = new UnityEvent();
            if (OnLandEvent == null)
                OnLandEvent = new UnityEvent();
        }

        private void FixedUpdate()
        {
            bool wasGrounded = m_Grounded;
            CheckGrounded(wasGrounded);
            CheckWall();
            HandleWallJumpLimiting();
        }

        private void CheckGrounded(bool wasGrounded)
        {
            m_Grounded = false;

            Collider2D[] colliders = Physics2D.OverlapCircleAll(m_GroundCheck.position, k_GroundedRadius, m_WhatIsGround);
            for (int i = 0; i < colliders.Length; i++)
            {
                if (colliders[i].gameObject != gameObject)
                {
                    m_Grounded = true;
                    if (!wasGrounded)
                    {
                        OnLandEvent.Invoke();
                        canDoubleJump = true;
                        if (m_Rigidbody2D.linearVelocity.y < 0f)
                            limitVelOnWallJump = false;
                    }
                }
            }
        }

        private void CheckWall()
        {
            m_IsWall = false;

            if (!m_Grounded)
            {
                OnFallEvent.Invoke();
                Collider2D[] collidersWall = Physics2D.OverlapCircleAll(m_WallCheck.position, k_GroundedRadius, m_WhatIsGround);
                for (int i = 0; i < collidersWall.Length; i++)
                {
                    if (collidersWall[i].gameObject != null)
                    {
                        isDashing = false;
                        m_IsWall = true;
                    }
                }
            }
        }

        private void HandleWallJumpLimiting()
        {
            if (limitVelOnWallJump)
            {
                if (m_Rigidbody2D.linearVelocity.y < -0.5f)
                    limitVelOnWallJump = false;

                jumpWallDistX = (jumpWallStartX - transform.position.x) * transform.localScale.x;

                if (jumpWallDistX < -0.5f && jumpWallDistX > -1f)
                {
                    canMove = true;
                }
                else if (jumpWallDistX < -1f && jumpWallDistX >= -2f)
                {
                    canMove = true;
                    m_Rigidbody2D.linearVelocity = new Vector2(10f * transform.localScale.x, m_Rigidbody2D.linearVelocity.y);
                }
                else if (jumpWallDistX < -2f || jumpWallDistX > 0)
                {
                    limitVelOnWallJump = false;
                    m_Rigidbody2D.linearVelocity = new Vector2(0, m_Rigidbody2D.linearVelocity.y);
                }
            }
        }

        /// <summary>
        /// Main movement method - can be called by any entity
        /// </summary>
        /// <param name="move">Horizontal movement input</param>
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
                m_Rigidbody2D.linearVelocity = new Vector2(transform.localScale.x * m_DashForce, 0);
            }
            // Normal movement (grounded or air control)
            else if (m_Grounded || m_AirControl)
            {
                // Limit fall speed
                if (m_Rigidbody2D.linearVelocity.y < -limitFallSpeed)
                    m_Rigidbody2D.linearVelocity = new Vector2(m_Rigidbody2D.linearVelocity.x, -limitFallSpeed);

                // Calculate target velocity
                Vector3 targetVelocity = new Vector2(move * 10f, m_Rigidbody2D.linearVelocity.y);

                // Apply smoothed movement
                m_Rigidbody2D.linearVelocity = Vector3.SmoothDamp(m_Rigidbody2D.linearVelocity, targetVelocity, ref velocity, m_MovementSmoothing);

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
                m_Rigidbody2D.linearVelocity = new Vector2(m_Rigidbody2D.linearVelocity.x, 0);
                PerformJump(m_JumpForce / 1.2f);
            }
        }

        private void HandleWallMechanics(float move, bool jump, bool dash)
        {
            if (m_IsWall && !m_Grounded)
            {
                if (!oldWallSlidding && m_Rigidbody2D.linearVelocity.y < 0 || isDashing)
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
                        m_Rigidbody2D.linearVelocity = new Vector2(-transform.localScale.x * 2, -5);
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
            m_Rigidbody2D.AddForce(new Vector2(0f, jumpForce));
        }

        private void PerformWallJump()
        {
            m_Rigidbody2D.linearVelocity = new Vector2(0f, 0f);
            m_Rigidbody2D.AddForce(new Vector2(transform.localScale.x * m_JumpForce * 1.2f, m_JumpForce));

            jumpWallStartX = transform.position.x;
            limitVelOnWallJump = true;
            canDoubleJump = true;
            canMove = false;

            EndWallSlide();
        }

        private void StartWallSlide()
        {
            isWallSliding = true;
            m_WallCheck.localPosition = new Vector3(-m_WallCheck.localPosition.x, m_WallCheck.localPosition.y, 0);
            Flip();
            StartCoroutine(WaitToCheck(0.1f));
            canDoubleJump = true;
        }

        private void EndWallSlide()
        {
            isWallSliding = false;
            oldWallSlidding = false;
            m_WallCheck.localPosition = new Vector3(Mathf.Abs(m_WallCheck.localPosition.x), m_WallCheck.localPosition.y, 0);
            canDoubleJump = true;
        }

        private void Flip()
        {
            m_FacingRight = !m_FacingRight;
            Vector3 theScale = transform.localScale;
            theScale.x *= -1;
            transform.localScale = theScale;
        }

        public void SetMovementEnabled(bool enabled)
        {
            canMove = enabled;
        }

        public void ApplyKnockback(Vector2 force)
        {
            m_Rigidbody2D.linearVelocity = Vector2.zero;
            m_Rigidbody2D.AddForce(force);
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
    }
}
