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
        [SerializeField] private float groundCheckRadius = 0.08f;   // spherecast radius

        [Header("Premium Jump Feel")]
        [SerializeField] private float coyoteTime = 0.10f;          // after leaving ground
        [SerializeField] private float jumpBufferTime = 0.10f;      // before landing

        [Header("Dash Settings")]
        [SerializeField] private float dashForce = 20f;
        [SerializeField] private float dashDuration = 0.2f;
        [SerializeField] private float dashCooldown = 1f;
        [SerializeField] private bool canDashInAir = true;
        [SerializeField] private bool dashResetsGravity = true;
        [SerializeField] private AnimationCurve dashCurve = AnimationCurve.EaseInOut(0f, 1f, 1f, 0f);

        [Header("Roll Settings")]
        [SerializeField] private float rollCooldown = 0.6f;
        [SerializeField] private bool rollCancelsDash = true;

        // --- Ground Roll (forward on ground) ---
        [SerializeField] private float groundRollSpeed = 9f;
        [SerializeField] private float groundRollDuration = 0.35f;
        [SerializeField] private AnimationCurve groundRollCurve = AnimationCurve.EaseInOut(0f, 1f, 1f, 0f);
        [SerializeField] private bool groundRollIgnoreFriction = true;

        // --- Air Roll / Roll-Down ---
        [Header("Air Roll (Roll-Down)")]
        [SerializeField] private float airRollForce = 18f;
        [SerializeField] private float airRollDuration = 0.35f;
        [SerializeField] private float airRollAngleDegrees = 55f;
        [SerializeField] private bool airRollResetsGravity = true;
        [SerializeField] private float airRollMaxDownSpeed = -40f;
        [SerializeField] private AnimationCurve airRollCurve = AnimationCurve.EaseInOut(0f, 1f, 1f, 0f);

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

        public enum FacingMode { ScaleFlip, YAxisRotation }

        // Components
        private Rigidbody rb;
        private Collider col;

        // Movement state
        private Vector3 moveInput;
        private bool canMove = true;

        // Grounding & jump feel
        private bool isGrounded;
        private float coyoteTimer = 0f;
        private float jumpBufferTimer = 0f;

        // Dash state
        private bool isDashing = false;
        private float dashEndTime = 0f;
        private float dashCooldownTimer = 0f;
        private Vector3 dashDirection;

        // Roll state
        private bool isRolling = false;
        private bool rollIsAir = false;
        private float rollEndTime = 0f;
        private float rollCooldownTimer = 0f;
        private Vector3 rollDirection;

        // Events
        public System.Action<bool> OnGroundedStateChanged;
        public System.Action<Vector3> OnMovementChanged;
        public System.Action OnDashStarted;
        public System.Action OnDashEnded;
        public System.Action OnSlamStarted;
        public System.Action OnSlamEnded;
        public System.Action OnRollStarted;
        public System.Action OnRollEnded;

        // Properties
        public bool IsGrounded => isGrounded;
        public bool CanMove { get => canMove; set => canMove = value; }
        public Vector3 Velocity => rb.linearVelocity;
        public float MoveSpeed { get => moveSpeed; set => moveSpeed = value; }
        public float JumpForce { get => jumpForce; set => jumpForce = value; }
        public float DashForce { get => dashForce; set => dashForce = value; }
        public float DashDuration { get => dashDuration; set => dashDuration = value; }
        public bool SnapToZPosition { get => snapToZPosition; set => snapToZPosition = value; }
        public float FixedZPosition { get => fixedZPosition; set => fixedZPosition = value; }
        public bool IsFacingRight => facingMode == FacingMode.ScaleFlip
            ? transform.localScale.x > 0f
            : Mathf.Abs(transform.eulerAngles.y) < 90f;

        public bool IsDashing => isDashing;
        public bool IsRolling => isRolling;
        public bool IsAirRolling => isRolling && rollIsAir;
        public bool CanDash => dashCooldownTimer <= 0f && (isGrounded || canDashInAir) && canMove;
        public bool CanRoll => rollCooldownTimer <= 0f && canMove && !isRolling;

        private void Awake()
        {
            rb = GetComponent<Rigidbody>();
            col = GetComponent<Collider>();

            rb.freezeRotation = true;

            // Lock Z with constraints to avoid snap pops
            if (snapToZPosition)
            {
                fixedZPosition = transform.position.z;
                rb.constraints |= RigidbodyConstraints.FreezePositionZ;
                // Ensure exact Z on spawn
                var p = transform.position;
                transform.position = new Vector3(p.x, p.y, fixedZPosition);
            }
            else
            {
                // If lane roaming, ensure Z is free
                rb.constraints &= ~RigidbodyConstraints.FreezePositionZ;
            }

            // Recommended for smooth visuals with physics motion
            rb.interpolation = RigidbodyInterpolation.Interpolate;
            rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
        }

        private void Update()
        {
            // --- Grounding ---
            bool wasGrounded = isGrounded;
            isGrounded = CheckGroundedSpherecast();

            // coyote timer
            if (isGrounded) coyoteTimer = coyoteTime;
            else coyoteTimer = Mathf.Max(0f, coyoteTimer - Time.deltaTime);

            // jump buffer timer naturally counts down
            if (jumpBufferTimer > 0f)
                jumpBufferTimer = Mathf.Max(0f, jumpBufferTimer - Time.deltaTime);

            if (wasGrounded != isGrounded)
                OnGroundedStateChanged?.Invoke(isGrounded);

            // --- Cooldowns / absolute end times ---
            if (dashCooldownTimer > 0f) dashCooldownTimer = Mathf.Max(0f, dashCooldownTimer - Time.deltaTime);
            if (rollCooldownTimer > 0f) rollCooldownTimer = Mathf.Max(0f, rollCooldownTimer - Time.deltaTime);

            if (isDashing && Time.time >= dashEndTime)
                EndDash();

            if (isRolling && Time.time >= rollEndTime)
                EndRoll();

            // If roaming on Z, clamp position in Update (visual) – physics stays in FixedUpdate
            if (!snapToZPosition && allowZMovement)
            {
                Vector3 pos = transform.position;
                pos.z = Mathf.Clamp(pos.z, minZPosition, maxZPosition);
                transform.position = pos;
            }
        }

        private void FixedUpdate()
        {
            if (isRolling)
            {
                ApplyRollVelocityFixed();
            }
            else if (isDashing)
            {
                ApplyDashVelocityFixed();
            }
            else
            {
                ApplyMovementFixed();
                ApplyGravityFixed();
            }

            ClampFallSpeedFixed();
        }

        // ===== Public API =====

        public void SetMovementInput(float horizontal, float vertical = 0f)
        {
            if (!canMove)
            {
                moveInput = Vector3.zero;
                OnMovementChanged?.Invoke(moveInput);
                return;
            }

            moveInput.x = horizontal;
            moveInput.z = (allowZMovement && !snapToZPosition) ? vertical : 0f;
            OnMovementChanged?.Invoke(moveInput);
        }

        /// <summary>
        /// Queue a jump using buffer/coyote rules. Keeps external API the same.
        /// </summary>
        public void Jump()
        {
            // Don’t jump immediately; buffer it and consume in FixedUpdate phase.
            jumpBufferTimer = jumpBufferTime;
        }

        public void Dash()
        {
            if (!CanDash) return;

            Vector3 dir = Vector3.right * (IsFacingRight ? 1f : -1f);
            if (Mathf.Abs(moveInput.x) > 0.1f)
                dir = Vector3.right * Mathf.Sign(moveInput.x);

            StartDash(dir);
        }

        public void StartDash(Vector3 direction)
        {
            if (!CanDash) return;

            isDashing = true;
            dashDirection = direction.normalized;
            dashEndTime = Time.time + dashDuration;
            dashCooldownTimer = dashCooldown;

            if (dashResetsGravity)
                rb.linearVelocity = new Vector3(rb.linearVelocity.x, 0f, rb.linearVelocity.z);

            OnDashStarted?.Invoke();
        }

        public void TryStartRoll()
        {
            if (!CanRoll) return;

            if (rollCancelsDash && isDashing)
                EndDash();

            rollIsAir = !isGrounded && Mathf.Abs(rb.linearVelocity.y) > 0.05f;
            isRolling = true;
            rollEndTime = Time.time + (rollIsAir ? airRollDuration : groundRollDuration);
            rollCooldownTimer = rollCooldown;

            if (rollIsAir)
            {
                float xSign = IsFacingRight ? 1f : -1f;
                float rad = airRollAngleDegrees * Mathf.Deg2Rad;
                rollDirection = new Vector3(Mathf.Cos(rad) * xSign, -Mathf.Sin(rad), 0f).normalized;

                if (airRollResetsGravity)
                    rb.linearVelocity = new Vector3(rb.linearVelocity.x, 0f, rb.linearVelocity.z);

                OnSlamStarted?.Invoke();
            }
            else
            {
                rollDirection = (IsFacingRight ? Vector3.right : Vector3.left);

                if (groundRollIgnoreFriction)
                {
                    // keep current Y/Z; we’ll drive X directly
                    rb.linearVelocity = new Vector3(rb.linearVelocity.x, rb.linearVelocity.y, rb.linearVelocity.z);
                }
            }

            OnRollStarted?.Invoke();
        }

        public void AddForce(Vector3 force, ForceMode mode = ForceMode.Impulse)
        {
            // Use for external knockback only. We otherwise write linearVelocity directly.
            rb.AddForce(force, mode);
        }

        public void TeleportTo(Vector3 position)
        {
            if (snapToZPosition) position.z = fixedZPosition;
            transform.position = position;
            rb.linearVelocity = Vector3.zero;
        }

        public void SetFacingDirection(bool facingRight)
        {
            switch (facingMode)
            {
                case FacingMode.ScaleFlip:
                    var s = transform.localScale;
                    s.x = Mathf.Abs(s.x) * (facingRight ? 1f : -1f);
                    transform.localScale = s;
                    break;
                case FacingMode.YAxisRotation:
                    var e = transform.eulerAngles;
                    e.y = facingRight ? 0f : 180f;
                    transform.eulerAngles = e;
                    break;
            }
        }

        // ===== Fixed-step writers =====

        private void ApplyMovementFixed()
        {
            // Consume buffered jump with coyote
            if (jumpBufferTimer > 0f && coyoteTimer > 0f && canMove && !isDashing && !isRolling)
            {
                rb.linearVelocity = new Vector3(rb.linearVelocity.x, jumpForce, rb.linearVelocity.z);
                jumpBufferTimer = 0f;
                coyoteTimer = 0f;
            }

            if (!canMove) return;

            Vector3 v = rb.linearVelocity;
            v.x = moveInput.x * moveSpeed;

            if (allowZMovement && !snapToZPosition)
                v.z = moveInput.z * zMoveSpeed;

            rb.linearVelocity = v;

            if (faceMovementDirection && Mathf.Abs(moveInput.x) > 0.1f)
                HandleFacingDirectionFixed(moveInput.x);
        }

        private void ApplyDashVelocityFixed()
        {
            // t in [0..1]
            float t = 1f - Mathf.Clamp01((dashEndTime - Time.time) / dashDuration);
            float curve = dashCurve.Evaluate(t);
            Vector3 dashV = dashDirection * dashForce * curve;

            if (dashResetsGravity)
                rb.linearVelocity = new Vector3(dashV.x, 0f, dashV.z);
            else
                rb.linearVelocity = new Vector3(dashV.x, rb.linearVelocity.y, dashV.z);

            if (faceMovementDirection && Mathf.Abs(dashDirection.x) > 0.1f)
                HandleFacingDirectionFixed(dashDirection.x);
        }

        private void ApplyRollVelocityFixed()
        {
            float total = rollIsAir ? airRollDuration : groundRollDuration;
            float t = 1f - Mathf.Clamp01((rollEndTime - Time.time) / total);

            if (rollIsAir)
            {
                float curve = airRollCurve.Evaluate(t);
                Vector3 v = rollDirection * airRollForce * curve;
                rb.linearVelocity = new Vector3(v.x, v.y, rb.linearVelocity.z);

                if (rb.linearVelocity.y < airRollMaxDownSpeed)
                    rb.linearVelocity = new Vector3(rb.linearVelocity.x, airRollMaxDownSpeed, rb.linearVelocity.z);

                if (faceMovementDirection && Mathf.Abs(rollDirection.x) > 0.1f)
                    SetFacingDirection(rollDirection.x > 0f);

                // hard-stop on land
                if (isGrounded)
                    EndRoll();
            }
            else
            {
                float curve = groundRollCurve.Evaluate(t);
                float xVel = rollDirection.x * groundRollSpeed * curve;
                rb.linearVelocity = new Vector3(xVel, rb.linearVelocity.y, rb.linearVelocity.z);

                if (faceMovementDirection && Mathf.Abs(rollDirection.x) > 0.1f)
                    SetFacingDirection(rollDirection.x > 0f);
            }
        }

        private void EndDash()
        {
            if (!isDashing) return;
            isDashing = false;
            OnDashEnded?.Invoke();
        }

        private void EndRoll()
        {
            if (!isRolling) return;
            isRolling = false;

            if (rollIsAir) OnSlamEnded?.Invoke();
            else
            {
                // tiny carry to keep flow
                rb.linearVelocity = new Vector3(rb.linearVelocity.x * 0.8f, rb.linearVelocity.y, rb.linearVelocity.z);
            }
            OnRollEnded?.Invoke();
        }

        private void ApplyGravityFixed()
        {
            if (isGrounded) return;
            // Skip gravity while rolling in air if you want; feels good to keep it on here
            rb.AddForce(Physics.gravity * gravityMultiplier, ForceMode.Acceleration);
        }

        private void HandleFacingDirectionFixed(float horizontalInput)
        {
            bool facingRight = horizontalInput > 0f;

            switch (facingMode)
            {
                case FacingMode.ScaleFlip:
                    var s = transform.localScale;
                    s.x = Mathf.Abs(s.x) * (facingRight ? 1f : -1f);
                    transform.localScale = s;
                    break;

                case FacingMode.YAxisRotation:
                    float targetY = facingRight ? 0f : 180f;
                    Vector3 e = transform.eulerAngles;
                    e.y = Mathf.LerpAngle(e.y, targetY, rotationSpeed * Time.fixedDeltaTime);
                    transform.eulerAngles = e;
                    break;
            }
        }

        private void ClampFallSpeedFixed()
        {
            if (rb.linearVelocity.y < maxFallSpeed)
                rb.linearVelocity = new Vector3(rb.linearVelocity.x, maxFallSpeed, rb.linearVelocity.z);
        }

        // ===== Grounding & Z helpers =====

        private bool CheckGroundedSpherecast()
        {
            if (col == null) col = GetComponent<Collider>();

            Vector3 origin = col.bounds.center;
            float dist = col.bounds.extents.y + groundCheckDistance;

            return Physics.SphereCast(origin, groundCheckRadius, Vector3.down, out _, dist, groundLayerMask, QueryTriggerInteraction.Ignore);
        }

        // ===== Debug viz =====

        private void OnDrawGizmosSelected()
        {
            if (col == null) col = GetComponent<Collider>();

            // Ground spherecast
            Gizmos.color = isGrounded ? Color.green : Color.red;
            Vector3 origin = col.bounds.center;
            Vector3 end = origin + Vector3.down * (col.bounds.extents.y + groundCheckDistance);
            // draw center line
            Gizmos.DrawLine(origin, end);
            // draw start sphere
            Gizmos.DrawWireSphere(origin, groundCheckRadius);
            // draw end sphere
            Gizmos.DrawWireSphere(end, groundCheckRadius);

            // Z lock marker
            if (snapToZPosition)
            {
                Gizmos.color = Color.blue;
                Vector3 p = transform.position;
                Gizmos.DrawLine(new Vector3(p.x - 1f, p.y, fixedZPosition),
                                new Vector3(p.x + 1f, p.y, fixedZPosition));
            }

            // Z bounds if roaming
            if (allowZMovement && !snapToZPosition)
            {
                Gizmos.color = Color.yellow;
                Vector3 p = transform.position;
                Gizmos.DrawLine(new Vector3(p.x, p.y, minZPosition),
                                new Vector3(p.x, p.y, maxZPosition));
            }

            // Dash preview (debug)
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
