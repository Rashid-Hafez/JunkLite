using System.Collections;
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
        [SerializeField] private string weaponSocketSortingLayerName;

        [Header("Jump Tuning")]
        [SerializeField] private float minJumpHoldTime = 0.08f;       // guaranteed jump time even if released instantly
        [SerializeField] private float lowJumpMultiplier = 2.5f;      // stronger gravity when jump released while rising
        [SerializeField] private float fallGravityMultiplier = 2.5f;  // stronger gravity when falling (snappy descent)
        [SerializeField] private float jumpCutMultiplier = 0.4f;      // one-time velocity cut when jump released early
        [SerializeField] private float apexThreshold = 2f;            // velocity below this = "at apex", applies fall gravity early
        [SerializeField] private float maxJumpHoldTime = 0.18f;      // hard cap on how long holding jump reduces gravity
        [SerializeField] private float heldJumpGravityRamp = 1.8f;   // gravity ramps from 1x to this while jump is held

        private float minJumpHoldEndTime = 0f;
        private float jumpStartTime = 0f;
        public bool JumpHeldExternally = false;
        private float becameAirborneTime = 0f;
        private bool hasJumpBeenCut = false;  // ensures velocity cut happens only once per jump
        private bool isExternalBounce = false; // true when bounce comes from pogo/trampoline/etc (ignores jump hold)
        private bool isPogoBounce = false;    // true during pogo arc: ignores jump hold but uses gentle gravity

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
        [SerializeField] private float wallJumpUpwardBonus = 2f;
        [SerializeField] private float doubleJumpLockoutAfterWallJump = 0.2f;

        private bool isWallJumping = false;
        private float wallJumpEndTime = 0f;
        private float lastWallJumpTime = float.NegativeInfinity;

        [Header("Ledge Detection Settings")]
        [SerializeField] private Transform ledgeCheckTransform;
        [SerializeField] private Vector2 ledgeCheckSize = new Vector2(0.5f, 1f);
        // how far below the probe we raycast when attempting to snap
        [SerializeField] private float groundSnapMaxDistance = 5f;
        // small push forward when we land so we don't snag on the corner
        [SerializeField] private float groundSnapForwardOffset = 0.1f;
        // additional vertical offset applied when snapping (can be negative to sink slightly)
        [SerializeField] private float groundSnapVerticalOffset = 0f;

        // ledge detection state (written by external helper or logic)
        private bool ledgeDetected;
        /// <summary>True when controller has detected a ledge (via an external component such as LedgeDetection).</summary>
        public bool LedgeDetected
        {
            get => ledgeDetected;
            set
            {
                if (ledgeDetected == value) return;
                ledgeDetected = value;
                OnLedgeDetectedChanged?.Invoke(value);
            }
        }

        // event fired when ledge detection state changes (true=>started, false=>ended)
        public System.Action<bool> OnLedgeDetectedChanged;

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

        [Header("Smooth ATTACK INPUT LOCK Stop")]
        [SerializeField] private float smoothStopTime = 0.23f;
        [SerializeField] private float smoothStopMaxSpeed = 10f;
        [SerializeField] private float smoothStopThreshold = 0.01f;
        [SerializeField] private float smoothStopInputMaxSpeed = 1f;
        [SerializeField] private float smoothStopInputThreshold = 0.001f;

        [Header("Character Settings")]
        [SerializeField] private bool faceMovementDirection = true;
        [SerializeField] private FacingMode facingMode = FacingMode.ScaleFlip;
        [SerializeField] private float rotationSpeed = 10f;

        private bool physicsOverride = false;
        public enum FacingMode { ScaleFlip, YAxisRotation }

        // Components
        private Rigidbody rb;
        private Collider col;
        private PlayerState playerState;

        // Movement state
        private Vector3 moveInput;
        private bool canMove = true;
        private bool allowMovementInput = true;
        private Coroutine smoothStopRoutine;
        private Vector3 smoothStopVelocity;
        private Vector3 smoothStopAngularVelocity;
        private Vector3 smoothStopInputVelocity;

        private float gravityMultiplierOverride = -1f;

        // Grounding & jump feel
        private bool isGrounded;
        private float coyoteTimer = 0f;
        private float jumpBufferTimer = 0f;

        // Dash state
        [SerializeField] private bool isDashing = false;
        private float dashEndTime = 0f;
        private float dashCooldownTimer = 0f;
        private Vector3 dashDirection;
        private bool wasDashOnCooldown = false;

        // Facing lock (prevents flipping during attacks)
        private bool facingLocked = false;
        private float facingLockEndTime = 0f;


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

        //Coroutines
        private Coroutine dashRoutine;



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

        // expose ground mask for external raycasts
        public LayerMask GroundLayerMask => groundLayerMask;

        /// <summary>
        /// When skimming near a ledge we may want to snap down to the real ground.
        /// Returns true if a floor surface was found and the character was repositioned.
        /// </summary>
        public bool TrySnapToGround()
        {
            if (!ledgeDetected)
                return false;

            if (rb == null)
                return false;

            // if we're moving upward don't snap
            if (rb.linearVelocity.y > 0f)
                return false;

            Vector3 origin = (ledgeCheckTransform != null)
                ? ledgeCheckTransform.position
                : transform.position;

            if (!Physics.Raycast(origin + Vector3.up * 0.1f, Vector3.down,
                                out RaycastHit hit, groundSnapMaxDistance, groundLayerMask))
            {
                return false;
            }

            // only consider relatively flat surfaces as ground
            if (hit.normal.y < 0.65f)
                return false;

            float halfHeight = (col != null) ? col.bounds.extents.y : 0.5f;
            Vector3 target = hit.point + Vector3.up * (halfHeight + groundSnapVerticalOffset);

            Vector3 forward = IsFacingRight ? transform.right : -transform.right;
            if (groundSnapForwardOffset != 0f)
                target += forward * groundSnapForwardOffset;

            // move to the full target position (x,z included)
            transform.position = new Vector3(target.x, target.y, target.z);

            // mark grounded so other logic can respond
            isGrounded = true;
            OnGroundedStateChanged?.Invoke(true);

            return true;
        }

        public bool IsFacingRight => facingMode == FacingMode.ScaleFlip
            ? transform.localScale.x > 0f
            : Mathf.Abs(transform.eulerAngles.y) < 90f;

        public bool IsDashing => isDashing;
        public bool CanDash => dashCooldownTimer <= 0f && (isGrounded || canDashInAir) && canMove;
        public bool IsFacingLocked => facingLocked;

        /// <summary>
        /// Resets air jump count so the player can double jump again. Call when stunned/hurt so the double jump isn't "wasted".
        /// </summary>
        public void ResetAirJumpCount()
        {
            airJumpCount = 0;
        }

        /// <summary>
        /// Locks facing direction for the specified duration. 
        /// Used during attacks to prevent mid-swing flipping.
        /// </summary>
        public void LockFacing(float duration)
        {
            facingLocked = true;
            facingLockEndTime = Time.time + duration;
        }

        public void FreezePerpendicularAxis()
        {
            Vector3 r = transform.right.normalized;

            // Compare only X and Z axes
            float dotX = Mathf.Abs(Vector3.Dot(r, Vector3.right));
            float dotZ = Mathf.Abs(Vector3.Dot(r, Vector3.forward));

            Vector3 mostPerpendicularAxis =
                (dotX < dotZ) ? Vector3.right : Vector3.forward;

            RigidbodyConstraints constraints = RigidbodyConstraints.FreezeRotationX | RigidbodyConstraints.FreezeRotationZ | RigidbodyConstraints.FreezeRotationY;

            if (mostPerpendicularAxis == Vector3.right)
                constraints |= RigidbodyConstraints.FreezePositionX;
            else
                constraints |= RigidbodyConstraints.FreezePositionZ;

            rb.constraints = constraints;
        }

        /// <summary>
        /// Immediately unlocks facing direction.
        /// </summary>
        public void UnlockFacing()
        {
            facingLocked = false;
            facingLockEndTime = 0f;
        }

        private void Awake()
        {
            rb = GetComponent<Rigidbody>();
            col = GetComponent<Collider>();
            playerState = GetComponent<PlayerState>();

            rb.freezeRotation = true;
            rb.constraints |= RigidbodyConstraints.FreezePositionZ; // default to locked Z for 2.5D, can be unlocked for roaming sections

            /*// Lock Z with constraints to avoid snap pops
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
            }*/

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
                isExternalBounce = false; // reset external bounce flag
                isPogoBounce = false;     // reset pogo flag
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

            // --- Facing lock expiry ---
            if (facingLocked && Time.time >= facingLockEndTime)
                UnlockFacing();

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
            if (physicsOverride)
                return;

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
                if (dashRoutine == null)
                    dashRoutine = StartCoroutine(DashCoroutine());
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

        public void SetPhysicsOverride(bool enabled)
        {
            physicsOverride = enabled;
        }


        public void SetJumpHeld(bool held)
        {
            JumpHeldExternally = held;
        }

        /// <summary>
        /// Apply an external bounce (pogo, trampoline, enemy stomp, etc).
        /// This bounce has fixed height and ignores jump-hold input.
        /// </summary>
        public void ApplyExternalBounce(float bounceForce)
        {
            // Cancel any downward velocity first
            rb.linearVelocity = new Vector3(rb.linearVelocity.x, 0f, rb.linearVelocity.z);

            // Apply the bounce force
            rb.linearVelocity = new Vector3(rb.linearVelocity.x, bounceForce, rb.linearVelocity.z);

            // Mark as external bounce - gravity system will ignore jump hold
            isExternalBounce = true;
            hasJumpBeenCut = true; // Prevent the one-time velocity cut from triggering

            // Reset air jump count so player can still double jump after pogo
            airJumpCount = 0;
        }

        private void StartMinJumpHoldWindow()
        {
            jumpStartTime = Time.time;
            minJumpHoldEndTime = Time.time + minJumpHoldTime;
        }

        /// <summary>
        /// Pogo / stomp bounce. Uses gentle gravity arc but ignores jump hold — height is fixed.
        /// </summary>
        public void ApplyPogoLaunch(float force)
        {
            rb.linearVelocity = new Vector3(rb.linearVelocity.x, 0f, rb.linearVelocity.z);
            rb.linearVelocity = new Vector3(rb.linearVelocity.x, force, rb.linearVelocity.z);

            isExternalBounce = false;
            isPogoBounce = true;   // gentle gravity, but jump hold is ignored
            hasJumpBeenCut = true; // height is fixed, no velocity cut
            StartMinJumpHoldWindow();
            airJumpCount = 0;
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

            if (!allowMovementInput)
                return;

            if (playerState != null && (playerState.IsInputLocked || playerState.IsAttacking))
                return;

            if (smoothStopRoutine != null)
            {
                StopCoroutine(smoothStopRoutine);
                smoothStopRoutine = null;
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

            // Ground jump - queue into fixed-step buffer
            if (coyoteTimer > 0f)
            {
                jumpBufferTimer = jumpBufferTime;
                return;
            }

            // Air jump - direct, no buffer
            bool canAirJump = airJumpCount < maxAirJumps
                && Time.time >= becameAirborneTime + minAirtimeForDoubleJump
                && Time.time >= lastWallJumpTime + doubleJumpLockoutAfterWallJump;

            if (canAirJump)
            {
                OnDoubleJumpPerformed?.Invoke();
                isDoubleJumpStalling = true;
                doubleJumpStallEndTime = Time.time + doubleJumpStallTime;
                rb.linearVelocity = new Vector3(rb.linearVelocity.x, 0f, rb.linearVelocity.z);
                airJumpCount++;
                JumpHeldExternally = true;
                hasJumpBeenCut = false;  // fresh jump, allow one cut
                return;
            }

            // No coyote jump and no air jump available: keep a short landing buffer.
            jumpBufferTimer = jumpBufferTime;
        }

        public void Dash()
        {
            // Debug.Log("Dash");
            if (!CanDash) return;

            Vector3 dir = transform.right * (IsFacingRight ? 1f : -1f);
            if (Mathf.Abs(moveInput.x) > 0.1f)
                dir = transform.right * Mathf.Sign(moveInput.x);

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
            rb.linearVelocity = Vector3.zero;
            rb.angularVelocity = Vector3.zero;
            moveInput = Vector3.zero;
        }

        public void StopAllVelocitySmooth()
        {
            if (smoothStopRoutine != null)
                StopCoroutine(smoothStopRoutine);

            smoothStopRoutine = StartCoroutine(CoStopAllVelocitySmooth());
        }

        public void SetGravityMultiplierOverride(float multiplier)
        {
            gravityMultiplierOverride = Mathf.Max(0f, multiplier);
        }

        public void ClearGravityMultiplierOverride()
        {
            gravityMultiplierOverride = -1f;
        }

        private IEnumerator CoStopAllVelocitySmooth()
        {
            if (rb == null)
            {
                smoothStopRoutine = null;
                yield break;
            }

            smoothStopVelocity = Vector3.zero;
            smoothStopAngularVelocity = Vector3.zero;
            smoothStopInputVelocity = Vector3.zero;
            allowMovementInput = false;

            while (rb.linearVelocity.sqrMagnitude > smoothStopThreshold ||
                   rb.angularVelocity.sqrMagnitude > smoothStopThreshold ||
                   moveInput.sqrMagnitude > smoothStopInputThreshold)
            {
                rb.linearVelocity = Vector3.SmoothDamp(rb.linearVelocity, Vector3.zero, ref smoothStopVelocity, smoothStopTime, maxSpeed: smoothStopMaxSpeed);
                rb.angularVelocity = Vector3.SmoothDamp(rb.angularVelocity, Vector3.zero, ref smoothStopAngularVelocity, smoothStopTime, maxSpeed: smoothStopMaxSpeed);
                moveInput = Vector3.SmoothDamp(moveInput, Vector3.zero, ref smoothStopInputVelocity, smoothStopTime, maxSpeed: smoothStopInputMaxSpeed);
                OnMovementChanged?.Invoke(moveInput);
                yield return new WaitForFixedUpdate();
            }

            rb.linearVelocity = Vector3.zero;
            rb.angularVelocity = Vector3.zero;
            moveInput = Vector3.zero;
            OnMovementChanged?.Invoke(moveInput);
            allowMovementInput = true;
            smoothStopRoutine = null;
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
            lastWallJumpTime = Time.time;

            // Jump away from the wall
            int jumpDir = -wallDirection;

            // Wall jump refreshes the air jump and gets a little extra vertical pop.
            airJumpCount = 0;
            rb.linearVelocity = transform.right * jumpDir * wallJumpHorizontalForce
                + transform.up * (wallJumpForce + wallJumpUpwardBonus);

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
                float currentGravityMultiplier = gravityMultiplierOverride >= 0f
                    ? gravityMultiplierOverride
                    : gravityMultiplier;
                rb.AddForce(Physics.gravity * currentGravityMultiplier, ForceMode.Acceleration);
            }

            // After duration, hand control back to normal movement
            if (Time.time >= wallJumpEndTime)
                isWallJumping = false;
        }

        #endregion

        // ===== Fixed-step writers =====
        private void ApplyMovementFixed()
        {
            // Don't apply player input changes during dash or wall jump - movement is locked to a specific velocity

            // --- Wall Jump Movement Lock ---
            if (isWallJumping)
                return;

            // --- Stunned - don't override velocity (let knockback play out) ---
            if (playerState != null && playerState.IsStunned)
                return;

            // --- Input locked (attacks, parry, etc.) - allow smooth stop to control velocity ---
            if (playerState != null && (playerState.IsInputLocked || playerState.IsAttacking || playerState.IsParrying))
                return;

            // --- Ground Jump via Buffer + Coyote ---
            if (jumpBufferTimer > 0f && canMove && !isDashing && !isWallSliding)
            {
                if (coyoteTimer > 0f)
                {
                    rb.linearVelocity = new Vector3(rb.linearVelocity.x, jumpForce, rb.linearVelocity.z);
                    StartMinJumpHoldWindow();
                    hasJumpBeenCut = false;
                    jumpBufferTimer = 0f;
                    coyoteTimer = 0f;
                    OnJumpStarted?.Invoke();
                }
            }

            if (!canMove) return;

            // Decompose current velocity into local axes
            Vector3 right = transform.right;
            Vector3 up = transform.up;

            float currentRightVel = Vector3.Dot(rb.linearVelocity, right);
            float currentUpVel = Vector3.Dot(rb.linearVelocity, up);

            float targetRightVel;

            // --- Air movement ---
            if (!isGrounded)
            {
                if (Mathf.Abs(moveInput.x) < 0.1f)
                {
                    // Strong air drag toward zero LOCAL horizontal velocity
                    targetRightVel = Mathf.Lerp(currentRightVel, 0f, 0.35f);
                }
                else
                {
                    targetRightVel = moveInput.x * moveSpeed;
                }
            }
            else
            {
                targetRightVel = moveInput.x * moveSpeed;
            }

            // Rebuild velocity in local space (preserve vertical)
            Vector3 v =
                right * targetRightVel +
                up * currentUpVel +
                Vector3.Project(rb.linearVelocity, transform.forward);

            rb.linearVelocity = v;

            // Facing direction
            if (faceMovementDirection && Mathf.Abs(moveInput.x) > 0.1f)
                HandleFacingDirectionFixed(moveInput.x);
        }



        private IEnumerator DashCoroutine()
        {
            //Debug.Log("Dash started");
            isDashing = true;
            dashEndTime = Time.time + dashDuration;
            dashCooldownTimer = dashCooldown;

            while (Time.time < dashEndTime)
            {
                float t = 1f - Mathf.Clamp01((dashEndTime - Time.time) / dashDuration);
                float curve = dashCurve.Evaluate(t);
                float dashSpeed = dashForce * curve;

                Vector3 right = transform.right;
                Vector3 up = transform.up;

                float dir = IsFacingRight ? 1f : -1f;

                Vector3 dashVel = right * dashSpeed * dir;

                if (dashResetsGravity)
                    rb.linearVelocity = dashVel;
                else
                    rb.linearVelocity = dashVel + up * Vector3.Dot(rb.linearVelocity, up);

                yield return null;
            }

            isDashing = false;
            dashRoutine = null;

            OnDashEnded?.Invoke();
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
            float currentGravityMultiplier = gravityMultiplierOverride >= 0f
                ? gravityMultiplierOverride
                : gravityMultiplier;
            bool minHoldActive = Time.time < minJumpHoldEndTime;

            // Hold window expired → treat as released even if button is still down
            bool holdExpired = Time.time >= jumpStartTime + minJumpHoldTime + maxJumpHoldTime;

            // Pogo ignores jump hold — height is fixed, can't be extended by holding space
            bool holdEffective = !isExternalBounce && !isPogoBounce && JumpHeldExternally;
            bool canCutJump = isExternalBounce || isPogoBounce || holdExpired || (!holdEffective && !minHoldActive);

            // --- ONE-TIME HARD JUMP CUT (Hollow Knight / Celeste style) ---
            // Skip for external bounce and pogo — their height is always fixed
            if (yVel > 0.1f && canCutJump && !hasJumpBeenCut && !isExternalBounce && !isPogoBounce)
            {
                rb.linearVelocity = new Vector3(rb.linearVelocity.x, yVel * jumpCutMultiplier, rb.linearVelocity.z);
                hasJumpBeenCut = true;
                yVel = rb.linearVelocity.y;
            }

            // --- WALL COLLISION: Extra gravity when hitting wall while jumping ---
            bool touchingWall = CheckWall();
            if (yVel > 0f && touchingWall && JumpHeldExternally && !isExternalBounce)
            {
                rb.AddForce(Physics.gravity * currentGravityMultiplier * lowJumpMultiplier * 1.5f, ForceMode.Acceleration);
                return;
            }

            // --- GRAVITY ---
            if (yVel < apexThreshold)
            {
                // Falling or near apex — always heavy gravity
                rb.AddForce(Physics.gravity * currentGravityMultiplier * fallGravityMultiplier, ForceMode.Acceleration);
            }
            else if (!canCutJump || isPogoBounce)
            {
                // Rising with jump held (or pogo arc) — gentle ramp from 1x to heldJumpGravityRamp
                float holdElapsed = Time.time - jumpStartTime - minJumpHoldTime;
                float holdT = Mathf.Clamp01(holdElapsed / maxJumpHoldTime);
                float rampedGravity = Mathf.Lerp(1f, heldJumpGravityRamp, holdT);
                rb.AddForce(Physics.gravity * currentGravityMultiplier * rampedGravity, ForceMode.Acceleration);
            }
            else
            {
                // Jump released / cut — snappier gravity on the way up
                rb.AddForce(Physics.gravity * currentGravityMultiplier * lowJumpMultiplier, ForceMode.Acceleration);
            }
        }


        /// <summary>
        /// Safely rotates the character 90 degrees around the Y axis.
        /// This mimics changing Rotation.Y in the Inspector.
        /// Uses Rigidbody.MoveRotation to stay physics-safe.
        /// </summary>
        public void RotatePLayer(float yRotation)
        {
            // Clear horizontal velocity so we don't "carry" motion across axes
            Vector3 up = transform.up;
            float verticalVel = Vector3.Dot(rb.linearVelocity, up);


            // Apply rotation safely via Rigidbody
            Quaternion delta = Quaternion.Euler(0f, yRotation, 0f);
            rb.MoveRotation(delta);

            // Optional: realign facing if using Y-axis rotation mode
            if (facingMode == FacingMode.YAxisRotation)
            {
                // Ensure facing logic remains consistent
                Vector3 e = transform.eulerAngles;
                e.y = Mathf.Round(e.y / 90f) * 90f;
                transform.eulerAngles = e;
            }


            float deltaVel = yRotation - transform.eulerAngles.y;

            transform.eulerAngles = new Vector3(0f, yRotation, 0f);

            // Rotate current velocity to match new orientation
            rb.linearVelocity = Quaternion.Euler(0f, deltaVel, 0f) * rb.linearVelocity;
        }


        private void HandleFacingDirectionFixed(float horizontalInput)
        {
            // Don't flip if facing is locked (during attacks)
            if (facingLocked) return;

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
            if (isWallSliding || physicsOverride) return;

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

            // draw the origin of the ledge snap ray as a small green sphere
            if (ledgeCheckTransform != null)
            {
                Gizmos.color = Color.green;
                Gizmos.DrawWireSphere(ledgeCheckTransform.position, 0.05f);

                // draw the ray downward to max distance
                Vector3 rayOrigin = ledgeCheckTransform.position + Vector3.up * 0.1f;
                Vector3 rayEnd = rayOrigin + Vector3.down * groundSnapMaxDistance;
                Gizmos.color = Color.yellow;
                Gizmos.DrawLine(rayOrigin, rayEnd);
            }

            // Retain existing commented debug for reference /* ... */
        }
    }
}