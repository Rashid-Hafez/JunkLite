using UnityEngine;
using UnityEngine.EventSystems;

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

        [Header("Weapon Socket Settings")]
        [SerializeField] private Transform TargetParent;
        [SerializeField] private Transform weaponSocket;
        [SerializeField] private float weaponSocketOffset = 0.5f;
        [SerializeField] private float weaponSocketRotation = 0f;
        [SerializeField] private float weaponSocketScale = 1f;
        [SerializeField] private int weaponSocketSortingOrder = 10;
        [SerializeField] private int weaponSocketSortingLayer = 0;
        [SerializeField] private int weaponSocketSortingLayerID = 0;
        [SerializeField] private string weaponSocketSortingLayerName;

        [Header("Jump Tuning")]
        [SerializeField] private float minJumpHoldTime = 0.08f;       // guaranteed jump time even if released instantly
        [SerializeField] private float lowJumpMultiplier = 2.5f;      // stronger gravity when jump released while rising
        [SerializeField] private float fallGravityMultiplier = 2.5f;  // stronger gravity when falling (snappy descent)
        [SerializeField] private float jumpCutMultiplier = 0.4f;      // one-time velocity cut when jump released early
        [SerializeField] private float apexThreshold = 2f;            // velocity below this = "at apex", applies fall gravity early

        private float minJumpHoldEndTime = 0f;
        public bool JumpHeldExternally = false;
        private float becameAirborneTime = 0f;
        private bool hasJumpBeenCut = false;  // ensures velocity cut happens only once per jump

        [Header("Double Jump Settings")]
        [SerializeField] private int maxAirJumps = 1;
        [SerializeField] private float doubleJumpForce = 9f; // smaller than normal jump
        [SerializeField] private float doubleJumpStallTime = 0.05f;  // small delay before the upward launch
        [SerializeField] private float minAirtimeForDoubleJump = 0.2f;  // tune this (0.15-0.3 feels good)

        private int airJumpCount = 0;
        private bool isDoubleJumpStalling = false;
        private float doubleJumpStallEndTime = 0f;

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
        [SerializeField] private GameObject dashReadyVFXPrefab;
        [SerializeField] private Transform dashReadyVFXSpawnPoint;



        // --- Wall Slide ---
        [Header("Wall Slide Settings")]
        [SerializeField] private float wallSlideSpeed = -2f;           // negative = downward
        [SerializeField] private float wallCheckRadius = 0.3f;
        [SerializeField] private Transform wallCheckTransform;
        [SerializeField] private LayerMask wallLayer;

        private bool isWallSliding;
        private int wallDirection; // +1 = right wall, -1 = left wall

        // --- Wall Jump ---
        [Header("Wall Jump Settings")]
        [SerializeField] private float wallJumpForce = 15f;
        [SerializeField] private float wallJumpHorizontalForce = 7f;
        [SerializeField] private float wallJumpDuration = 0.18f;

        private bool isWallJumping = false;
        private float wallJumpEndTime = 0f;

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
        private PlayerState playerState;

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
        private bool wasDashOnCooldown = false;


        // Events
        public System.Action<bool> OnGroundedStateChanged;
        public System.Action<Vector3> OnMovementChanged;
        public System.Action OnDashStarted;
        public System.Action OnDashEnded;
        public System.Action OnSlamStarted;
        public System.Action OnSlamEnded;

        public System.Action<bool> OnWallSlideChanged;   // true = started, false = ended
        public System.Action OnWallJumped;               // fired when wall jump begins
        public System.Action OnDoubleJumpPerformed;      // fired when double jump is triggered
        public System.Action OnJumpStarted;              // fired on ground jump
        public System.Action OnFallStarted;              // airborne began
        public System.Action OnFallEnded;                // landed after falling



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
        public bool CanDash => dashCooldownTimer <= 0f && (isGrounded || canDashInAir) && canMove;

        private void Awake()
        {
            rb = GetComponent<Rigidbody>();
            col = GetComponent<Collider>();
            playerState = GetComponent<PlayerState>();

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

            if (isGrounded)
            {
                airJumpCount = 0;
                hasJumpBeenCut = false;  // reset for next jump
            }
            else
            {
                if (wasGrounded)  // just became airborne
                    becameAirborneTime = Time.time;
            }

            // jump buffer timer naturally counts down
            if (jumpBufferTimer > 0f)
                jumpBufferTimer = Mathf.Max(0f, jumpBufferTimer - Time.deltaTime);

            if (wasGrounded != isGrounded)
            {
                OnGroundedStateChanged?.Invoke(isGrounded);

                // fall start (left the ground)
                if (!isGrounded && wasGrounded)
                    OnFallStarted?.Invoke();

                // fall end (landed)
                if (isGrounded && !wasGrounded)
                    OnFallEnded?.Invoke();
            }

            // --- Cooldowns / absolute end times ---
            if (dashCooldownTimer > 0f)
            {
                wasDashOnCooldown = true;
                dashCooldownTimer = Mathf.Max(0f, dashCooldownTimer - Time.deltaTime);

                if (dashCooldownTimer <= 0.3f)
                {
                    SpawnDashReadyVFX();
                }

                wasDashOnCooldown = false;
            }

            if (isDashing && Time.time >= dashEndTime)
                EndDash();

            // If roaming on Z, clamp position in Update (visual) – physics stays in FixedUpdate
            if (!snapToZPosition && allowZMovement)
            {
                Vector3 pos = transform.position;
                pos.z = Mathf.Clamp(pos.z, minZPosition, maxZPosition);
                transform.position = pos;
            }

            // Weapon Socket Update
            // idk what this does
            // The following code section was generated by Cursor IDE to explain this spot.
            // This block is where you may want to update the weapon socket every frame
            // so it can visually follow or align with the character, weapon target, etc.
            // The update below is intended to sync the weapon socket's transform
            // according to desired offsets, rotation, and layering settings for rendering.

            if (weaponSocket != null && TargetParent != null)
            {
                weaponSocket.rotation = TargetParent.rotation;

            }
        }

        private void FixedUpdate()
        {

            if (!IsGrounded)
                coyoteTimer -= Time.fixedDeltaTime;

            if (isDoubleJumpStalling)
            {
                // forbid gravity & movement change during stall
                rb.linearVelocity = new Vector3(rb.linearVelocity.x, 0f, rb.linearVelocity.z);

                if (Time.time >= doubleJumpStallEndTime)
                {
                    // Stall over → apply upward launch
                    isDoubleJumpStalling = false;
                    rb.linearVelocity = new Vector3(rb.linearVelocity.x, doubleJumpForce, rb.linearVelocity.z);
                    StartMinJumpHoldWindow();
                }

                // Skip rest of movement while stalling
                return;
            }

            if (isDashing)
            {
                ApplyDashVelocityFixed();
            }
            else if (isWallJumping)
            {
                ApplyWallJumpFixed();
            }
            else
            {
                ApplyMovementFixed();
                HandleWallSlide();
                ApplyGravityFixed();
            }

            ClampFallSpeedFixed();
        }

        #region Public Methods

        public void SetJumpHeld(bool held)
        {
            JumpHeldExternally = held;
        }

        private void StartMinJumpHoldWindow()
        {
            minJumpHoldEndTime = Time.time + minJumpHoldTime;
        }


        public void SetMovementInput(float horizontal, float vertical = 0f)
        {
            if (!canMove)
            {
                moveInput = Vector3.zero;
                OnMovementChanged?.Invoke(moveInput);
                return;
            }

            if (isWallJumping)
            {
                moveInput = Vector3.zero;
                return;
            }

            moveInput.x = horizontal;
            moveInput.z = (allowZMovement && !snapToZPosition) ? vertical : 0f;
            OnMovementChanged?.Invoke(moveInput);
        }

        /// <summary>
        /// Jump entry point. Decides between wall jump and normal jump.
        /// </summary>
        public void Jump()
        {
            if (isWallSliding)
            {
                StartWallJump();
                return;
            }

            // Ground jump - use buffer system
            if (coyoteTimer > 0f)
            {
                jumpBufferTimer = jumpBufferTime;
                return;
            }

            // Air jump - direct, no buffer
            bool canAirJump = airJumpCount < maxAirJumps
                && Time.time >= becameAirborneTime + minAirtimeForDoubleJump;

            if (canAirJump)
            {
                OnDoubleJumpPerformed?.Invoke();
                isDoubleJumpStalling = true;
                doubleJumpStallEndTime = Time.time + doubleJumpStallTime;
                rb.linearVelocity = new Vector3(rb.linearVelocity.x, 0f, rb.linearVelocity.z);
                airJumpCount++;
                JumpHeldExternally = true;
                hasJumpBeenCut = false;  // fresh jump, allow one cut
            }
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

            //////// vfx /sfx trigger could go here ////////


            dashDirection = direction.normalized;
            dashEndTime = Time.time + dashDuration;
            dashCooldownTimer = dashCooldown;

            if (dashResetsGravity)
                rb.linearVelocity = new Vector3(rb.linearVelocity.x, 0f, rb.linearVelocity.z);

            OnDashStarted?.Invoke();
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

        /// <summary>
        /// Returns current movement input magnitude (for animation checks).
        /// </summary>
        public float GetMovementInputMagnitude()
        {
            return Mathf.Abs(moveInput.x);
        }

        /// <summary>
        /// Stops all velocity immediately. Use on death or hard stops.
        /// </summary>
        public void StopAllVelocity()
        {
            //rb.linearVelocity = Vector3.zero;
            //rb.angularVelocity = Vector3.zero;
            moveInput = Vector3.zero;
        }

        #endregion

        #region Wall Slide & Wall Jump

        private bool CheckWall()
        {
            if (wallCheckTransform == null) return false;
            return Physics.CheckSphere(wallCheckTransform.position, wallCheckRadius, wallLayer);
        }

        private void HandleWallSlide()
        {
            bool previous = isWallSliding;

            if (isGrounded || isWallJumping)
            {
                isWallSliding = false;
                if (previous != isWallSliding)
                    OnWallSlideChanged?.Invoke(false);
                return;
            }

            bool touchingWall = CheckWall();
            if (!touchingWall)
            {
                isWallSliding = false;
                if (previous != isWallSliding)
                    OnWallSlideChanged?.Invoke(false);
                return;
            }

            // Determine wall direction
            wallDirection = IsFacingRight ? +1 : -1;

            bool holdingTowardWall =
                Mathf.Abs(moveInput.x) > 0.1f &&
                Mathf.Sign(moveInput.x) == wallDirection;

            if (!holdingTowardWall)
            {
                isWallSliding = false;
                if (previous != isWallSliding)
                    OnWallSlideChanged?.Invoke(false);
                return;
            }

            // success → wall sliding
            isWallSliding = true;

            // slow downward speed (already your logic)
            Vector3 v = rb.linearVelocity;
            if (v.y < wallSlideSpeed)
                v.y = -wallSlideSpeed;
            rb.linearVelocity = v;

            if (previous != isWallSliding)
                OnWallSlideChanged?.Invoke(true);

            // Turn off jumping while wall sliding
            coyoteTimer = 0f;
            jumpBufferTimer = 0f;
        }


        private void StartWallJump()
        {
            // Ensure wall sliding is cleared before starting wall jump
            if (isWallSliding)
            {
                isWallSliding = false;
                OnWallSlideChanged?.Invoke(false);
            }

            isWallJumping = true;
            wallJumpEndTime = Time.time + wallJumpDuration;

            // Jump away from the wall
            int jumpDir = -wallDirection;

            rb.linearVelocity = new Vector3(
                wallJumpHorizontalForce * jumpDir,
                wallJumpForce,
                rb.linearVelocity.z
            );

            StartMinJumpHoldWindow();

            // Clear timers so a ground jump isn't consumed
            coyoteTimer = 0f;
            jumpBufferTimer = 0f;

            // Face the jump direction
            SetFacingDirection(jumpDir > 0);

            OnWallJumped?.Invoke();
        }

        private void ApplyWallJumpFixed()
        {
            // Let gravity act during wall jump
            if (!isGrounded)
            {
                rb.AddForce(Physics.gravity * gravityMultiplier, ForceMode.Acceleration);
            }

            // After duration, hand control back to normal movement
            if (Time.time >= wallJumpEndTime)
                isWallJumping = false;
        }

        #endregion

        // ===== Fixed-step writers =====

        private void ApplyMovementFixed()
        {
            // --- Wall Jump Movement Lock ---
            if (isWallJumping)
                return;

            // --- Stunned - don't override velocity (let knockback play out) ---
            if (playerState != null && playerState.IsStunned)
                return;

            // --- Ground Jump via Buffer + Coyote ---
            if (jumpBufferTimer > 0f && canMove && !isDashing && !isWallSliding)
            {
                if (coyoteTimer > 0f)
                {
                    rb.linearVelocity = new Vector3(rb.linearVelocity.x, jumpForce, rb.linearVelocity.z);
                    StartMinJumpHoldWindow();
                    hasJumpBeenCut = false;  // fresh jump, allow one cut
                    jumpBufferTimer = 0f;
                    coyoteTimer = 0f;
                    OnJumpStarted?.Invoke();
                }
                else
                {
                    // No coyote available, clear buffer (air jump handled in Jump() directly)
                    jumpBufferTimer = 0f;
                }
            }

            if (!canMove) return;

            Vector3 v = rb.linearVelocity;

            // --- Air movement ---
            if (!isGrounded)
            {
                if (Mathf.Abs(moveInput.x) < 0.1f)
                {
                    // Strong air drag to force straight-down fall
                    v.x = Mathf.Lerp(v.x, 0f, 0.35f);
                }
                else
                {
                    // Normal air control
                    v.x = moveInput.x * moveSpeed;
                }
            }
            else
            {
                v.x = moveInput.x * moveSpeed;
            }

            // Z-axis movement if enabled
            if (allowZMovement && !snapToZPosition)
                v.z = moveInput.z * zMoveSpeed;

            // Apply final velocity
            rb.linearVelocity = v;

            // Facing direction
            if (faceMovementDirection && Mathf.Abs(moveInput.x) > 0.1f)
                HandleFacingDirectionFixed(moveInput.x);
        }


        private void ApplyDashVelocityFixed()
        {
            if (isWallJumping)
                return;

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



        private void EndDash()
        {
            if (!isDashing) return;
            isDashing = false;
            OnDashEnded?.Invoke();
        }

        private void SpawnDashReadyVFX()
        {
            if (dashReadyVFXPrefab == null) return;

            Vector3 spawnPos = dashReadyVFXSpawnPoint != null
                ? dashReadyVFXSpawnPoint.position
                : transform.position;

            GameObject dashReadyVFX = Instantiate(dashReadyVFXPrefab, spawnPos, Quaternion.identity);
            dashReadyVFX.transform.localScale = Vector3.one * 2f;
            dashReadyVFX.transform.parent = transform;
            Destroy(dashReadyVFX, 0.3f);
        }

        private void ApplyGravityFixed()
        {
            if (isGrounded) return;

            float yVel = rb.linearVelocity.y;
            bool minHoldActive = Time.time < minJumpHoldEndTime;
            bool canCutJump = !JumpHeldExternally && !minHoldActive;

            // --- ONE-TIME HARD JUMP CUT (Hollow Knight / Celeste style) ---
            if (yVel > 0.1f && canCutJump && !hasJumpBeenCut)
            {
                rb.linearVelocity = new Vector3(rb.linearVelocity.x, yVel * jumpCutMultiplier, rb.linearVelocity.z);
                hasJumpBeenCut = true;
                yVel = rb.linearVelocity.y;
            }

            // --- WALL COLLISION: Extra gravity when hitting wall while jumping ---
            bool touchingWall = CheckWall();
            if (yVel > 0f && touchingWall && JumpHeldExternally)
            {
                rb.AddForce(Physics.gravity * gravityMultiplier * lowJumpMultiplier * 1.5f, ForceMode.Acceleration);
                return;
            }

            // --- GRAVITY (always applied, like Rigidbody.useGravity) ---
            // Key fix: Apply fall gravity when AT or NEAR apex, not just when yVel < 0
            // This prevents the "float" at the peak of the jump

            if (yVel < apexThreshold)
            {
                // At apex or falling - snap down immediately with fall gravity
                rb.AddForce(Physics.gravity * gravityMultiplier * fallGravityMultiplier, ForceMode.Acceleration);
            }
            else if (canCutJump)
            {
                // Rising fast but jump released - strong gravity to shorten arc
                rb.AddForce(Physics.gravity * gravityMultiplier * lowJumpMultiplier, ForceMode.Acceleration);
            }
            else
            {
                // Rising fast with jump held - normal gravity
                rb.AddForce(Physics.gravity * gravityMultiplier, ForceMode.Acceleration);
            }
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
            if (isWallSliding) return;

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

            // Wall check gizmo
            if (wallCheckTransform != null)
            {
                Gizmos.color = Color.magenta;
                Gizmos.DrawWireSphere(wallCheckTransform.position, wallCheckRadius);
            }
        }
    }
}