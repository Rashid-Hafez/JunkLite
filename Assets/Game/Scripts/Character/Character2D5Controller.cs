using System;
using System.Collections;
using System.Collections.Generic;
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
        [Tooltip("Seconds lane speed eases to zero after letting go while running on the ground. 0 = stop dead.")]
        [SerializeField, Min(0f)] private float groundStopSlideDuration = 0.15f;
        [Tooltip("Reversing a real run carries her on in the old direction for this long, still facing it. 0 = instant turn.")]
        [SerializeField, Min(0f)] private float turnSlideDuration = 0.195f;
        [Tooltip("Seconds of no input that still count as the same run, so holding both keys for a moment still slides.")]
        [SerializeField, Min(0f)] private float turnSlideInputGrace = 0.12f;
        [Tooltip("Seconds of running before a reversal slides.")]
        [SerializeField, Min(0f)] private float turnSlideMinRunTime = 0.5f;
        [Tooltip("Lane speed that halves the run needed before a reversal slides. Above the base run speed (10), so it means boosted speed.")]
        [SerializeField, Min(0f)] private float turnSlideMinSpeed = 12f;

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
        [SerializeField] private float maxJumpHoldTime = 0.18f;       // hard cap on how long holding jump reduces gravity
        [SerializeField] private float heldJumpGravityRamp = 1.8f;    // gravity ramps from 1x to this while jump is held

        private float minJumpHoldEndTime = 0f;
        private float jumpStartTime = 0f;
        public bool JumpHeldExternally = false;
        private float becameAirborneTime = 0f;
        private bool hasJumpBeenCut = false;  // ensures velocity cut happens only once per jump
        private bool isExternalBounce = false; // true when bounce comes from pogo/trampoline/etc (ignores jump hold)
        private bool isPogoBounce = false;    // true during pogo arc: ignores jump hold but uses gentle gravity
        private bool isAirJumpArc = false;    // true after the double-jump launch: fixed arc with its own rise gravity

        [Header("Double Jump Settings")]
        [SerializeField] private int maxAirJumps = 1;
        [SerializeField] private float doubleJumpForce = 9f; // smaller than normal jump
        [SerializeField] private float doubleJumpStallTime = 0.05f;  // small delay before the upward launch
        [Tooltip("Gravity multiplier while the double jump rises. Higher = snappier, less hang time (raise Double Jump Force to keep the height).")]
        [SerializeField, Min(0.1f)] private float doubleJumpRiseGravity = 1f;
        [Tooltip("Extra fall gravity after a double jump (multiplies Fall Gravity Multiplier). Higher = lands sooner.")]
        [SerializeField, Min(0.1f)] private float doubleJumpFallGravity = 1f;
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

        [Header("Wall Slide & Wall Jump")]
        [Tooltip("Walls on these layers can be slid down and jumped off. Walls on other layers only block.")]
        [SerializeField] private LayerMask wallLayer = 1 << 12; // Wall
        [Tooltip("On: only walls with a WallJumpSurface component can be slid down and jumped off. Other walls only block.")]
        [SerializeField] private bool requireWallJumpSurface = true;
        [Tooltip("Gap between the capsule side and a wall that still counts as touching it.")]
        [SerializeField, Min(0.01f)] private float wallCheckDistance = 0.1f;
        [Tooltip("Fastest fall (m/s) while holding into a wall.")]
        [SerializeField, Min(0f)] private float wallSlideSpeed = 2f;
        [Tooltip("Upward speed of a wall jump. Releasing jump early cuts it like a ground jump.")]
        [SerializeField, Min(0f)] private float wallJumpForce = 12f;
        [Tooltip("Speed away from the wall during the push-off.")]
        [SerializeField, Min(0f)] private float wallJumpHorizontalForce = 8f;
        [Tooltip("Seconds the push-off keeps its outward speed before air steering resumes.")]
        [SerializeField, Min(0f)] private float wallJumpDuration = 0.2f;
        [Tooltip("Seconds after leaving a wall that jump still wall-jumps (lets you press away, then jump).")]
        [SerializeField, Min(0f)] private float wallJumpCoyoteTime = 0.1f;
        [Tooltip("Seconds after a wall jump before the double jump can be used.")]
        [SerializeField, Min(0f)] private float doubleJumpLockoutAfterWallJump = 0.2f;

        private bool isWallSliding;
        private float lastWallContactTime = float.NegativeInfinity;
        private float lastWallNormalSign;      // +1: the wall's face points along +MovementAxis
        private float wallJumpTimeRemaining;
        private float wallJumpDirection;       // +1/-1 along MovementAxis
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
        [Tooltip("Runtime-only: free-move rooms switch this on (LaneEntryTrigger) and lanes switch it off.")]
        [SerializeField] private bool allowZMovement = false;
        [Tooltip("Speed (m/s) walking into/out of the screen in free-move rooms like Room 0. Run speed is Move Speed.")]
        [SerializeField] private float zMoveSpeed = 5f;

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
        [Tooltip("ScaleFlip: legacy, mirrors the root (old player prefab only). " +
                 "SkeletonSwap: root never flips; facing is a value that FacingMirror and the Spine L/R swap read.")]
        [SerializeField] private FacingMode facingMode = FacingMode.ScaleFlip;
        [SerializeField] private float rotationSpeed = 10f;

        private readonly HashSet<int> movementLocks = new();
        private readonly HashSet<int> physicsOverrideLocks = new();
        private readonly HashSet<int> kinematicLocks = new();
        private int nextControlLockId;
        private bool rigidbodyWasKinematicBeforeLocks;
        public enum FacingMode { ScaleFlip, YAxisRotation, SkeletonSwap }

        // Components
        private Rigidbody rb;
        private Collider col;
        private PlayerState playerState;

        // Movement state
        private Vector3 moveInput;
        private bool canMove = true;
        private bool allowMovementInput = true;
        private float statusMoveSpeedMultiplier = 1f;
        private Coroutine smoothStopRoutine;
        private Coroutine laneStepRoutine;
        private IDisposable laneStepOverride;
        private Vector3 smoothStopVelocity;
        private Vector3 smoothStopAngularVelocity;
        private Vector3 smoothStopInputVelocity;

        // Stop slide: lane speed easing out after the player lets go on the ground.
        private bool ranOnGroundLastStep;
        private float groundStopSlideRemaining;
        private float groundStopSlideStartSpeed;
        private float groundRunTime;
        private float shoveHoldEndTime = float.NegativeInfinity;
        private float noInputTime;
        private float turnSlideRemaining;
        private float turnSlideStartSpeed;

        private float gravityMultiplierOverride = -1f;
        // Air-hit hang: a timed slow fall after landing a hit in the air. Separate from the
        // attack motion's single gravity override so the two never clear each other.
        private float airHangScale = 1f;
        private float airHangUntil;

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

        // SkeletonSwap facing (the root transform never changes for facing)
        private bool isFacingRight = true;

        /// <summary>Raised with the new IsFacingRight value whenever facing changes.</summary>
        public event Action<bool> FacingChanged;


        // Events
        public System.Action<bool> OnGroundedStateChanged;
        public System.Action<Vector3> OnMovementChanged;
        public System.Action OnDashStarted;
        public System.Action OnDashEnded;
        public System.Action OnSlamStarted;
        public System.Action OnSlamEnded;

        public System.Action OnWallJumped;               // fired when a wall jump launches
        public System.Action OnDoubleJumpPerformed;      // fired when double jump is triggered
        public System.Action OnJumpStarted;              // fired on ground jump
        public System.Action OnFallStarted;              // airborne began
        public System.Action OnFallEnded;                // landed after falling

        // Properties
        public bool IsGrounded => isGrounded;
        /// <summary>
        /// Final locomotion permission. The serialized switch is reserved for lifecycle
        /// enable/disable, while temporary systems must use AcquireMovementLock().
        /// </summary>
        public bool CanMove => canMove && movementLocks.Count == 0 &&
                               (playerState == null || playerState.CanMove);

        public bool IsPhysicsOverridden => physicsOverrideLocks.Count > 0;
        public Vector3 Velocity => rb.linearVelocity;
        public float MoveSpeed { get => moveSpeed; set => moveSpeed = value; }
        public float EffectiveMoveSpeed => moveSpeed * statusMoveSpeedMultiplier;
        public Vector3 MovementAxis => transform.right.normalized;
        /// <summary>Raw horizontal intent, kept even while attacks or locks stop moveInput from updating.</summary>
        public float HorizontalInput { get; private set; }
        /// <summary>Accepted move input this frame (x = lane, z = depth in free roam).</summary>
        public Vector3 MoveInput => moveInput;
        /// <summary>True while lane speed eases out after letting go on the ground. Not running, even though speed is above zero.</summary>
        public bool IsGroundStopSliding => groundStopSlideRemaining > 0f;
        /// <summary>True while a reversed run carries on in the old direction; facing flips when it ends.</summary>
        public bool IsTurnSliding => turnSlideRemaining > 0f;
        /// <summary>Seconds of the current unbroken run on the ground.</summary>
        public float GroundRunTime => groundRunTime;
        /// <summary>True while move input points against her lane speed: a reversal about to be handled.</summary>
        public bool IsInputAgainstLaneSpeed
        {
            get
            {
                float laneSpeed = Vector3.Dot(rb.linearVelocity, transform.right);
                return Mathf.Abs(HorizontalInput) >= 0.1f && Mathf.Abs(laneSpeed) > 0.1f &&
                       Mathf.Sign(HorizontalInput) != Mathf.Sign(laneSpeed);
            }
        }
        /// <summary>True while airborne, holding into a wall-jump wall and moving down it.</summary>
        public bool IsWallSliding => isWallSliding;
        /// <summary>True during a wall jump's push-off, while it keeps its outward speed.</summary>
        public bool IsWallJumping => wallJumpTimeRemaining > 0f;
        public float JumpForce { get => jumpForce; set => jumpForce = value; }
        public float DashForce { get => dashForce; set => dashForce = value; }
        public float DashDuration { get => dashDuration; set => dashDuration = value; }
        public float DashCooldown { get => dashCooldown; set => dashCooldown = Mathf.Max(0f, value); }
        public float DoubleJumpForce { get => doubleJumpForce; set => doubleJumpForce = value; }
        /// <summary>Air jumps allowed per airtime. 0 disables the double jump (upgrade-gated on the player).</summary>
        public int MaxAirJumps { get => maxAirJumps; set => maxAirJumps = Mathf.Max(0, value); }
        /// <summary>Player input dash. False until the Dash upgrade is bought; abilities that call StartDash are not gated.</summary>
        public bool DashEnabled { get; set; } = true;
        public bool SnapToZPosition { get => snapToZPosition; set => snapToZPosition = value; }
        public float FixedZPosition { get => fixedZPosition; set => fixedZPosition = value; }
        public bool IsFreeRoaming => allowZMovement && !snapToZPosition;

        // expose ground mask for external raycasts
        public LayerMask GroundLayerMask => groundLayerMask;

        /// <summary>
        /// When skimming near a ledge we may want to snap down to the real ground.
        /// Returns true if a floor surface was found and the character was repositioned.
        /// </summary>
        public bool TrySnapToGround()
        {
            if (IsPhysicsOverridden || !CanMove || !ledgeDetected || wallJumpTimeRemaining > 0f || isWallSliding)
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

        public bool IsFacingRight => facingMode switch
        {
            FacingMode.ScaleFlip => transform.localScale.x > 0f,
            FacingMode.YAxisRotation => Mathf.Abs(transform.eulerAngles.y) < 90f,
            _ => isFacingRight
        };

        /// <summary>+1 facing right, -1 facing left. Use this instead of reading localScale.</summary>
        public float FacingSign => IsFacingRight ? 1f : -1f;

        public bool IsDashing => isDashing;
        public bool CanDash => isActiveAndEnabled && !isDashing && rb != null &&
                               !rb.isKinematic && !IsPhysicsOverridden && dashDuration > 0f &&
                               dashCooldownTimer <= 0f && (isGrounded || canDashInAir) && CanMove && !IsTurnSliding;
        public bool IsFacingLocked => facingLocked;

        /// <summary>
        /// Resets air jump count so the player can double jump again.
        /// Call when stunned/hurt so the double jump isn't "wasted".
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
            if (IsFreeRoaming)
            {
                rb.constraints = RigidbodyConstraints.FreezeRotation;
                return;
            }

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
        /// Resets the player's Y rotation and Rigidbody axis constraints back to the
        /// orientation they should have at the spawn point. Call this on every respawn
        /// so that a death mid-ZY-section doesn't carry its axis config into the new run.
        /// </summary>
        public void ResetToSpawnOrientation(float spawnYRotation)
        {
            CancelLaneStep();
            ClearWallState();
            // Clear any residual horizontal momentum from the previous run
            StopAllVelocity();

            // Rotate to the spawn-point facing — this is what FreezePerpendicularAxis
            // reads to decide which world axis to lock, so order matters here.
            RotatePLayer(spawnYRotation);

            // Re-derive and apply the correct constraint for the new rotation
            FreezePerpendicularAxis();
        }

        /// <summary>
        /// Places the character on a lane: exact position, lane Y rotation, zero velocity,
        /// and the perpendicular axis locked. Used by LaneEntryTrigger.
        /// </summary>
        public void SnapToLane(Vector3 position, float laneYRotation)
        {
            CancelLaneStep();
            ClearWallState();
            StopAllVelocity();

            Quaternion rotation = Quaternion.Euler(0f, laneYRotation, 0f);
            rb.position = position;
            rb.rotation = rotation;
            transform.SetPositionAndRotation(position, rotation);
            fixedZPosition = position.z;
            allowZMovement = false;
            snapToZPosition = true;

            FreezePerpendicularAxis();
        }

        /// <summary>
        /// Walks the character onto the lane at <paramref name="speed"/> (m/s, eased), then ends with
        /// SnapToLane. The controller's physics is overridden during the step, so input can't fight it.
        /// A new snap, respawn orientation or disable cancels it. Speed 0 snaps at once.
        /// </summary>
        public void StepToLane(Vector3 position, float laneYRotation, float speed)
        {
            CancelLaneStep();
            Vector3 offset = position - rb.position;
            offset.y = 0f;
            float distance = offset.magnitude;
            if (speed <= 0f || distance < 0.05f || !isActiveAndEnabled)
            {
                SnapToLane(position, laneYRotation);
                return;
            }

            StopAllVelocity();
            laneStepOverride = AcquirePhysicsOverride();
            laneStepRoutine = StartCoroutine(CoStepToLane(position, laneYRotation, distance / speed));
        }

        public bool IsSteppingToLane => laneStepRoutine != null;

        private IEnumerator CoStepToLane(Vector3 target, float laneYRotation, float duration)
        {
            var wait = new WaitForFixedUpdate();
            Vector3 start = rb.position;
            target.y = start.y;

            for (float t = 0f; t < duration;)
            {
                yield return wait;
                t += Time.fixedDeltaTime;
                Vector3 next = Vector3.Lerp(start, target, Mathf.SmoothStep(0f, 1f, t / duration));
                rb.linearVelocity = (next - rb.position) / Time.fixedDeltaTime;
            }

            laneStepRoutine = null;
            SnapToLane(target, laneYRotation);
        }

        private void CancelLaneStep()
        {
            if (laneStepRoutine != null) StopCoroutine(laneStepRoutine);
            laneStepRoutine = null;
            laneStepOverride?.Dispose();
            laneStepOverride = null;
        }

        /// <summary>
        /// Free-move rooms: vertical input walks along transform.forward (into/out of the screen)
        /// and no ground axis is locked; room colliders bound the player. Off re-locks the lane axis.
        /// </summary>
        public void SetFreeRoam(bool enabled)
        {
            allowZMovement = enabled;
            snapToZPosition = !enabled;
            fixedZPosition = transform.position.z;
            moveInput.z = 0f;
            FreezePerpendicularAxis();
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
                airHangUntil = 0f;
                airJumpCount = 0;
                hasJumpBeenCut = false;  // reset for next jump
                isExternalBounce = false; // reset external bounce flag
                isPogoBounce = false;     // reset pogo flag
                isAirJumpArc = false;
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

                // Dash-ready VFX invocation removed; use SkeletonGhost for dash visuals.

                wasDashOnCooldown = false;
            }

            if (isDashing && Time.time >= dashEndTime)
                EndDash();

            // --- Facing lock expiry ---
            if (facingLocked && Time.time >= facingLockEndTime)
                UnlockFacing();

            // Weapon Socket Update
            if (weaponSocket != null && TargetParent != null)
            {
                weaponSocket.rotation = TargetParent.rotation;
            }
        }

        private void OnDisable()
        {
            CancelLaneStep();
            InterruptSpecialMovement();
        }

        private void FixedUpdate()
        {
            if (Time.timeScale <= 0f) return;
            if (IsPhysicsOverridden || rb.isKinematic)
            {
                InterruptSpecialMovement();
                return;
            }

            if (!IsGrounded)
                coyoteTimer -= Time.fixedDeltaTime;

            // Hard control has authority over dash, jump stalls and wall jumps.
            // Gravity still runs and an external impulse remains untouched.
            if (playerState != null && playerState.IsStunned)
            {
                InterruptSpecialMovement();
                ApplyGravityFixed();
                ClampFallSpeedFixed();
                StopPushingIntoWallsFixed();
                return;
            }

            // Wall jump push-off: keep the outward speed for a moment so holding toward
            // the wall can't cancel it. The axis is re-read so a lane rotation can't skew it.
            if (wallJumpTimeRemaining > 0f)
            {
                wallJumpTimeRemaining = Mathf.Max(0f, wallJumpTimeRemaining - Time.fixedDeltaTime);
                SetVelocity(MovementAxis * (wallJumpDirection * wallJumpHorizontalForce) +
                            Vector3.up * rb.linearVelocity.y);
                ApplyGravityFixed();
                ClampFallSpeedFixed();
                StopPushingIntoWallsFixed();
                return;
            }

            UpdateWallSlideFixed();

            if (isDoubleJumpStalling)
            {
                // forbid gravity & movement change during stall
                rb.linearVelocity = new Vector3(rb.linearVelocity.x, 0f, rb.linearVelocity.z);

                if (Time.time >= doubleJumpStallEndTime)
                {
                    // Stall over → apply upward launch. The double jump is a fixed impulse:
                    // it rides the pogo arc (gentle gravity, hold ignored, no cut), so holding never adds height.
                    isDoubleJumpStalling = false;
                    rb.linearVelocity = new Vector3(rb.linearVelocity.x, doubleJumpForce, rb.linearVelocity.z);
                    isExternalBounce = false;
                    isPogoBounce = true;
                    isAirJumpArc = true;
                    hasJumpBeenCut = true;
                    StartMinJumpHoldWindow();
                }

                // Skip rest of movement while stalling
                return;
            }

            if (isDashing)
            {
                ApplyDashFixed();
            }
            else
            {
                ApplyMovementFixed();
                ApplyGravityFixed();
            }

            ClampFallSpeedFixed();
            StopPushingIntoWallsFixed();
        }

        #region Public Methods

        /// <summary>
        /// Enables or disables locomotion for player lifecycle transitions such as
        /// activation, deactivation, death and respawn. Temporary systems use locks.
        /// </summary>
        public void SetLocomotionEnabled(bool enabled)
        {
            canMove = enabled;
            if (!enabled)
                InterruptSpecialMovement();
        }

        /// <summary>
        /// Temporarily prevents locomotion without changing the player's lifecycle
        /// enabled state. Multiple callers can safely overlap their locks.
        /// </summary>
        public IDisposable AcquireMovementLock(bool stopVelocity = true)
        {
            int id = ++nextControlLockId;
            movementLocks.Add(id);
            InterruptSpecialMovement();

            if (stopVelocity)
                StopAllVelocity();

            return new ControlLock(() => movementLocks.Remove(id));
        }

        /// <summary>
        /// Temporarily suspends controller FixedUpdate physics. Use this when another
        /// gameplay sequence owns the player's transform or Rigidbody motion.
        /// </summary>
        public IDisposable AcquirePhysicsOverride()
        {
            int id = ++nextControlLockId;
            physicsOverrideLocks.Add(id);
            InterruptSpecialMovement();
            return new ControlLock(() => physicsOverrideLocks.Remove(id));
        }

        /// <summary>
        /// Temporarily makes the controller Rigidbody kinematic. The original state is
        /// restored only after the final overlapping lock is released.
        /// </summary>
        public IDisposable AcquireKinematicLock(bool stopVelocity = true)
        {
            if (rb == null)
                return ControlLock.Empty;

            int id = ++nextControlLockId;
            InterruptSpecialMovement();
            if (kinematicLocks.Count == 0)
            {
                rigidbodyWasKinematicBeforeLocks = rb.isKinematic;
                if (stopVelocity && !rb.isKinematic)
                    StopAllVelocity();
                rb.isKinematic = true;
            }

            kinematicLocks.Add(id);
            return new ControlLock(() => ReleaseKinematicLock(id));
        }

        private void ReleaseKinematicLock(int id)
        {
            if (!kinematicLocks.Remove(id) || kinematicLocks.Count > 0 || rb == null)
                return;

            rb.isKinematic = rigidbodyWasKinematicBeforeLocks;
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
            isAirJumpArc = false;
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
            isAirJumpArc = false;
            isPogoBounce = true;   // gentle gravity, but jump hold is ignored
            hasJumpBeenCut = true; // height is fixed, no velocity cut
            StartMinJumpHoldWindow();
            airJumpCount = 0;
        }


        public void SetMovementInput(float horizontal, float vertical = 0f)
        {
            HorizontalInput = horizontal;
            if (!CanMove)
            {
                moveInput = Vector3.zero;
                OnMovementChanged?.Invoke(moveInput);
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
        /// Queues a ground jump or uses the remaining air jump.
        /// </summary>
        public void Jump()
        {
            if (!CanMove || IsPhysicsOverridden || rb == null || rb.isKinematic) return;

            // Wall jump while sliding wins over coyote time: the slide clears the ground-jump
            // buffer, so a coyote press right after catching a wall was swallowed entirely.
            if (isWallSliding && CanWallJump)
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

            // Wall jump - touching a wall now, or left one a moment ago
            if (CanWallJump)
            {
                StartWallJump();
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
            if (!DashEnabled || !CanDash) return;

            // Sliding faces the wall; a dash from it goes away from the wall, not into it.
            if (isWallSliding)
            {
                bool awayRight = lastWallNormalSign > 0f;
                SetWallSliding(false);
                FaceForTraversal(awayRight);
                StartDash(transform.right * (awayRight ? 1f : -1f));
                return;
            }

            Vector3 dir = transform.right * (IsFacingRight ? 1f : -1f);
            if (Mathf.Abs(moveInput.x) > 0.1f)
                dir = transform.right * Mathf.Sign(moveInput.x);

            StartDash(dir);
        }

        public void StartDash(Vector3 direction)
        {
            if (!CanDash) return;

            isDashing = true;
            ClearGroundStopSlide();

            //////// vfx /sfx trigger could go here ////////

            dashDirection = direction.normalized;
            dashEndTime = Time.time + dashDuration;
            dashCooldownTimer = dashCooldown;

            if (dashResetsGravity)
                rb.linearVelocity = new Vector3(rb.linearVelocity.x, 0f, rb.linearVelocity.z);

            OnDashStarted?.Invoke();
        }

        /// <summary>
        /// Apply externally imposed movement. The controller owns plane projection
        /// and interruption so dash/jump writers cannot erase the impulse.
        /// </summary>
        public void ApplyExternalImpulse(
            Vector3 worldImpulse,
            ForceMode mode = ForceMode.VelocityChange,
            bool interruptSpecialMovement = true)
        {
            if (rb == null || rb.isKinematic || worldImpulse.sqrMagnitude <= 0f)
                return;

            if (interruptSpecialMovement)
                InterruptSpecialMovement();

            Vector3 planarImpulse = Vector3.Project(worldImpulse, MovementAxis)
                                  + Vector3.Project(worldImpulse, Vector3.up);
            rb.AddForce(planarImpulse, mode);
        }

        public void ApplyExternalKnockback(
            Vector3 sourcePosition,
            Vector2 force,
            bool interruptSpecialMovement = true)
        {
            Vector3 axis = MovementAxis;
            float side = Vector3.Dot(transform.position - sourcePosition, axis);
            float direction = Mathf.Approximately(side, 0f)
                ? (IsFacingRight ? -1f : 1f)
                : Mathf.Sign(side);

            Vector3 impulse = axis * direction * Mathf.Abs(force.x)
                            + Vector3.up * force.y;
            ApplyExternalImpulse(impulse, ForceMode.VelocityChange, interruptSpecialMovement);
        }

        /// <summary>
        /// Sets the lane speed to force.x along direction (replaced, not added, so moving
        /// into the source can't cancel it) and the upward speed to at least force.y.
        /// </summary>
        public void ApplyExternalShove(Vector3 direction, Vector2 force, float holdSeconds = 0f)
        {
            if (rb == null || rb.isKinematic)
                return;

            InterruptSpecialMovement();

            Vector3 axis = MovementAxis;
            float along = Vector3.Dot(direction, axis);
            float sign = Mathf.Approximately(along, 0f)
                ? (IsFacingRight ? -1f : 1f)
                : Mathf.Sign(along);

            float up = Mathf.Max(rb.linearVelocity.y, force.y);
            SetVelocity(axis * (sign * Mathf.Abs(force.x)) + Vector3.up * up);
            shoveHoldEndTime = Time.time + holdSeconds;
        }

        // Compatibility entry point. New combat code should use ApplyExternalImpulse.
        public void AddForce(Vector3 force, ForceMode mode = ForceMode.Impulse)
        {
            ApplyExternalImpulse(force, mode);
        }

        public void SetStatusMoveSpeedMultiplier(float multiplier)
        {
            statusMoveSpeedMultiplier = Mathf.Max(0f, multiplier);
        }

        public void InterruptSpecialMovement()
        {
            ClearWallState();
            EndDash();
            CancelSmoothStop();
            ClearGroundStopSlide();
            isDoubleJumpStalling = false;

            jumpBufferTimer = 0f;
        }

        /// <summary>Moves exactly to position (respawn). The lane depth follows the new position.</summary>
        public void TeleportTo(Vector3 position)
        {
            ClearWallState();
            fixedZPosition = position.z;
            // Write the body too: with interpolation a transform-only move can be reverted next physics step.
            rb.position = position;
            transform.position = position;
            StopAllVelocity();
        }

        public void SetFacingDirection(bool facingRight)
        {
            bool wasFacingRight = IsFacingRight;
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
                case FacingMode.SkeletonSwap:
                    isFacingRight = facingRight;
                    break;
            }

            if (IsFacingRight != wasFacingRight)
                FacingChanged?.Invoke(IsFacingRight);
        }

        /// <summary>
        /// Sets facing without moving the collider: ScaleFlip mirrors an off-centre capsule,
        /// which would otherwise push it into the wall the player is turning away from.
        /// </summary>
        public void FaceForTraversal(bool facingRight)
        {
            if (col is not CapsuleCollider capsule) { SetFacingDirection(facingRight); return; }
            Vector3 center = transform.TransformPoint(capsule.center);
            SetFacingDirection(facingRight);
            // Mirroring the offset player capsule must not move it into its wall.
            rb.position += center - transform.TransformPoint(capsule.center);
        }

        /// <summary>
        /// Returns current movement input magnitude (for animation checks).
        /// </summary>
        public float GetMovementInputMagnitude()
        {
            return Mathf.Max(Mathf.Abs(moveInput.x), Mathf.Abs(moveInput.z));
        }

        /// <summary>
        /// Stops all velocity immediately. Use on death or hard stops.
        /// </summary>
        public void StopAllVelocity()
        {
            CancelSmoothStop();
            ClearGroundStopSlide();
            if (rb != null && !rb.isKinematic)
            {
                rb.linearVelocity = Vector3.zero;
                rb.angularVelocity = Vector3.zero;
            }
            moveInput = Vector3.zero;
        }

        /// <summary>
        /// Changes player velocity through the Rigidbody-owning component. Combat and
        /// ability systems may calculate motion, but they should not write the body.
        /// </summary>
        public void SetVelocity(Vector3 velocity)
        {
            if (rb == null || rb.isKinematic)
                return;

            rb.linearVelocity = velocity;
        }

        private void CancelSmoothStop()
        {
            if (smoothStopRoutine != null)
            {
                StopCoroutine(smoothStopRoutine);
                smoothStopRoutine = null;
            }

            allowMovementInput = true;
            smoothStopVelocity = Vector3.zero;
            smoothStopAngularVelocity = Vector3.zero;
            smoothStopInputVelocity = Vector3.zero;
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

        /// <summary>
        /// Falls slowly for <paramref name="duration"/> seconds: the falling gravity is multiplied by
        /// <paramref name="gravityScale"/>. Rising is unaffected; landing ends it. Re-applying refreshes it.
        /// </summary>
        public void ApplyAirHang(float gravityScale, float duration)
        {
            if (duration <= 0f || isGrounded) return;
            airHangScale = Mathf.Max(0f, gravityScale);
            airHangUntil = Time.time + duration;
        }

        /// <summary>True while an air-hit hang is slowing the fall.</summary>
        public bool IsAirHangActive => !isGrounded && Time.time < airHangUntil;

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


        // ===== Fixed-step writers =====
        private void ApplyMovementFixed()
        {
            // --- Stunned - don't override velocity (let knockback play out) ---
            if (playerState != null && playerState.IsStunned)
                return;

            // --- Input locked (attacks, parry, etc.) - allow smooth stop to control velocity ---
            if (playerState != null && (playerState.IsInputLocked || playerState.IsAttacking || playerState.IsParrying))
            {
                ClearGroundStopSlide();
                return;
            }

            // --- Ground Jump via Buffer + Coyote ---
            if (jumpBufferTimer > 0f && CanMove && !isDashing)
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

            if (!CanMove)
            {
                ClearGroundStopSlide();
                return;
            }

            // Decompose current velocity into local axes
            Vector3 right = transform.right;
            Vector3 up = transform.up;

            float currentRightVel = Vector3.Dot(rb.linearVelocity, right);
            float currentUpVel = Vector3.Dot(rb.linearVelocity, up);

            float targetRightVel;

            // A body push owns the lane speed for its hold: input can't cancel it.
            if (Time.time < shoveHoldEndTime)
            {
                targetRightVel = currentRightVel;
            }
            // --- Air movement ---
            else if (!isGrounded)
            {
                // Keeps the run time: a brief step off the ground does not end the run.
                ClearGroundStopSlide(keepRunTime: true);
                if (Mathf.Abs(moveInput.x) < 0.1f)
                {
                    // Strong air drag toward zero LOCAL horizontal velocity
                    targetRightVel = Mathf.Lerp(currentRightVel, 0f, 0.35f);
                }
                else
                {
                    targetRightVel = moveInput.x * EffectiveMoveSpeed;
                }
            }
            else
            {
                targetRightVel = GroundLaneTarget(currentRightVel);
            }

            // Rebuild velocity in local space (preserve vertical)
            Vector3 vel =
                right * targetRightVel +
                up * currentUpVel +
                (IsFreeRoaming
                    ? transform.forward * (moveInput.z * zMoveSpeed)
                    : Vector3.Project(rb.linearVelocity, transform.forward));

            rb.linearVelocity = vel;

            // Facing direction
            if (faceMovementDirection && Mathf.Abs(moveInput.x) > 0.1f && !IsTurnSliding)
                HandleFacingDirectionFixed(moveInput.x);
        }

        /// <summary>
        /// Lane speed on the ground. Letting go while running eases out over groundStopSlideDuration;
        /// any input (including the other direction) ends the slide at once. It reads the live speed, so
        /// a wall or door blocker that stopped her (StopPushingIntoWallsFixed runs after this) stays stopped.
        /// </summary>
        private float GroundLaneTarget(float currentRightVel)
        {
            bool hasInput = Mathf.Abs(moveInput.x) >= 0.1f;
            if (TryTurnSlide(hasInput, currentRightVel, out float turnSpeed))
                return turnSpeed;

            if (hasInput || groundStopSlideDuration <= 0f)
            {
                groundStopSlideRemaining = 0f;
                ranOnGroundLastStep = hasInput;
                return moveInput.x * EffectiveMoveSpeed;
            }

            if (ranOnGroundLastStep && Mathf.Abs(currentRightVel) > 0.1f)
            {
                groundStopSlideStartSpeed = Mathf.Abs(currentRightVel);
                groundStopSlideRemaining = groundStopSlideDuration;
            }
            ranOnGroundLastStep = false;

            if (groundStopSlideRemaining <= 0f)
                return moveInput.x * EffectiveMoveSpeed;

            groundStopSlideRemaining = Mathf.Max(0f, groundStopSlideRemaining - Time.fixedDeltaTime);
            return EaseStopSlide(currentRightVel, groundStopSlideStartSpeed, groundStopSlideRemaining / groundStopSlideDuration);
        }

        // Reversal: keep the old direction and facing, eased to a stop, then the normal target takes over.
        // Pressing the old direction again cancels it. Letting go lets it finish.
        private bool TryTurnSlide(bool hasInput, float currentRightVel, out float speed)
        {
            speed = 0f;
            bool reversing = hasInput && Mathf.Abs(currentRightVel) > 0.1f &&
                             Mathf.Sign(moveInput.x) != Mathf.Sign(currentRightVel);

            if (turnSlideRemaining > 0f)
            {
                if (hasInput && !reversing)
                {
                    turnSlideRemaining = 0f;
                    return false;
                }

                turnSlideRemaining = Mathf.Max(0f, turnSlideRemaining - Time.fixedDeltaTime);
                if (turnSlideRemaining <= 0f)
                    return false;

                speed = EaseStopSlide(currentRightVel, turnSlideStartSpeed, turnSlideRemaining / turnSlideDuration);
                return true;
            }

            if (reversing && turnSlideDuration > 0f &&
                ShouldTurnSlide(groundRunTime, Mathf.Abs(currentRightVel), turnSlideMinRunTime, turnSlideMinSpeed))
            {
                // Both keys held: the stop slide has begun easing, so start from the speed before it.
                turnSlideStartSpeed = groundStopSlideRemaining > 0f
                    ? Mathf.Max(groundStopSlideStartSpeed, Mathf.Abs(currentRightVel))
                    : Mathf.Abs(currentRightVel);
                groundStopSlideRemaining = 0f;
                groundRunTime = 0f;
                noInputTime = 0f;
                turnSlideRemaining = turnSlideDuration;
                speed = Mathf.Sign(currentRightVel) * turnSlideStartSpeed;
                return true;
            }

            if (hasInput)
            {
                // Only one unbroken hold counts: taps never add up to a run, and neither does a reversal.
                groundRunTime = reversing || noInputTime > 0f ? 0f : groundRunTime + Time.fixedDeltaTime;
                noInputTime = 0f;
            }
            else
            {
                noInputTime += Time.fixedDeltaTime;
                if (noInputTime > turnSlideInputGrace) groundRunTime = 0f;
            }
            return false;
        }

        /// <summary>A reversal slides after a long enough run, or after half of one at high speed, so spam never slides.</summary>
        public static bool ShouldTurnSlide(float runTime, float speed, float minRunTime, float minSpeed) =>
            runTime >= minRunTime || (speed >= minSpeed && runTime >= minRunTime * 0.5f);

        /// <summary>Eased lane speed during a stop slide: remaining 1 = full start speed, 0 = stopped. Never faster than the live speed.</summary>
        public static float EaseStopSlide(float currentSpeed, float startSpeed, float remaining)
        {
            float eased = startSpeed * Mathf.SmoothStep(0f, 1f, remaining);
            return Mathf.Sign(currentSpeed) * Mathf.Min(Mathf.Abs(currentSpeed), eased);
        }

        private void ClearGroundStopSlide(bool keepRunTime = false)
        {
            ranOnGroundLastStep = false;
            groundStopSlideRemaining = 0f;
            if (!keepRunTime) groundRunTime = 0f;
            turnSlideRemaining = 0f;
        }


        private void ApplyDashFixed()
        {
            if (Time.time >= dashEndTime || dashDuration <= 0f)
            {
                EndDash();
                return;
            }

            // Dash shares the controller's fixed-step ownership checks. A cinematic,
            // grab or ability cannot leave a render-frame coroutine writing velocity.
            float t = 1f - Mathf.Clamp01((dashEndTime - Time.time) / dashDuration);
            float dashSpeed = dashForce * dashCurve.Evaluate(t);
            Vector3 up = transform.up;
            float dir = IsFacingRight ? 1f : -1f;
            Vector3 dashVel = transform.right * dashSpeed * dir;

            rb.linearVelocity = dashResetsGravity
                ? dashVel
                : dashVel + up * Vector3.Dot(rb.linearVelocity, up);
        }


        private void EndDash()
        {
            if (!isDashing) return;

            isDashing = false;
            OnDashEnded?.Invoke();
        }

        // SpawnDashReadyVFX removed. SkeletonGhost handles dash visuals.

        private void ApplyGravityFixed()
        {
            if (isGrounded) return;

            float yVel = rb.linearVelocity.y;
            // At slide speed the wall carries her: no more gravity, so the fall stays capped.
            if (isWallSliding && yVel <= -wallSlideSpeed) return;
            float currentGravityMultiplier = gravityMultiplierOverride >= 0f
                ? gravityMultiplierOverride
                : gravityMultiplier;
            if (Time.time < airHangUntil && yVel < apexThreshold)
                currentGravityMultiplier *= airHangScale;
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

            // --- GRAVITY ---
            if (yVel < apexThreshold)
            {
                // Falling or near apex — always heavy gravity (heavier still after a double jump)
                float fall = fallGravityMultiplier * (isAirJumpArc ? doubleJumpFallGravity : 1f);
                rb.AddForce(Physics.gravity * currentGravityMultiplier * fall, ForceMode.Acceleration);
            }
            else if (isAirJumpArc)
            {
                // Double jump rising: fixed, snappy arc so it never floats
                rb.AddForce(Physics.gravity * currentGravityMultiplier * doubleJumpRiseGravity, ForceMode.Acceleration);
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
            ClearWallState();
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
            SetVelocity(Quaternion.Euler(0f, deltaVel, 0f) * rb.linearVelocity);
        }


        private void HandleFacingDirectionFixed(float horizontalInput)
        {
            // Don't flip if facing is locked (during attacks)
            if (facingLocked) return;

            bool facingRight = horizontalInput > 0f;

            if (facingMode == FacingMode.YAxisRotation)
            {
                float targetY = facingRight ? 0f : 180f;
                Vector3 e = transform.eulerAngles;
                e.y = Mathf.LerpAngle(e.y, targetY, rotationSpeed * Time.fixedDeltaTime);
                transform.eulerAngles = e;
                return;
            }

            SetFacingDirection(facingRight);
        }

        private void ClampFallSpeedFixed()
        {
            if (IsPhysicsOverridden) return;

            float limit = isWallSliding ? Mathf.Max(maxFallSpeed, -wallSlideSpeed) : maxFallSpeed;
            if (rb.linearVelocity.y < limit)
                rb.linearVelocity = new Vector3(rb.linearVelocity.x, limit, rb.linearVelocity.z);
        }

        // ===== Wall slide & wall jump =====
        // Airborne and holding toward a wall-jump wall (Wall layer + WallJumpSurface): falling is capped
        // at wallSlideSpeed and jump pushes off it. The probe follows input along the lane axis, not a child
        // transform, so it works on both sides whatever the facing mode (SkeletonSwap never flips the root).

        private bool CanWallJump => !isGrounded && !isDashing && wallJumpTimeRemaining <= 0f &&
                                    (playerState == null || playerState.CanJump) &&
                                    (isWallSliding || Time.time - lastWallContactTime <= wallJumpCoyoteTime);

        private void UpdateWallSlideFixed()
        {
            bool sliding = false;
            float input = HorizontalInput;
            if (!isGrounded && CanMove && !isDashing && !isDoubleJumpStalling && Mathf.Abs(input) > 0.1f &&
                (playerState == null || !playerState.IsRolling) && ProbeWall(Mathf.Sign(input), out float normalSign))
            {
                lastWallContactTime = Time.time;
                lastWallNormalSign = normalSign;
                // Rising along the wall keeps its jump; only the way down is a slide.
                sliding = rb.linearVelocity.y <= 0f;
                if (sliding)
                {
                    // A buffered ground jump must not fire off the wall later.
                    jumpBufferTimer = 0f;
                    // The slide owns the fall: drop any jump/pogo arc so it can't carry over.
                    isAirJumpArc = isPogoBounce = isExternalBounce = false;
                }
            }

            SetWallSliding(sliding);
        }

        /// <summary>Wall-jump wall within wallCheckDistance on <paramref name="side"/> (+1/-1 along the lane)?</summary>
        private bool ProbeWall(float side, out float normalSign)
        {
            normalSign = 0f;
            if (col == null || wallLayer.value == 0) return false;

            Vector3 axis = MovementAxis;
            Vector3 direction = axis * side;
            Bounds bounds = col.bounds;
            Vector3 extents = bounds.extents;
            float halfAlongAxis = Mathf.Abs(Vector3.Dot(extents, new Vector3(Mathf.Abs(axis.x), 0f, Mathf.Abs(axis.z))));
            // A thin slab through the body's middle: shorter than the capsule so floor and
            // ceiling edges don't count, and starting inside the body so it never begins in the wall.
            const float slab = 0.05f;
            Vector3 halfExtents = new Vector3(
                Mathf.Abs(axis.x) > 0.5f ? slab : extents.x * 0.9f,
                extents.y * 0.6f,
                Mathf.Abs(axis.z) > 0.5f ? slab : extents.z * 0.9f);
            float distance = Mathf.Max(0f, halfAlongAxis - slab) + wallCheckDistance;

            if (!Physics.BoxCast(bounds.center, halfExtents, direction, out RaycastHit hit, Quaternion.identity,
                    distance, wallLayer, QueryTriggerInteraction.Ignore))
                return false;
            if (hit.collider.attachedRigidbody == rb || Mathf.Abs(hit.normal.y) >= WallNormalMaxY ||
                Vector3.Dot(hit.normal, -direction) < 0.7f)
                return false;
            if (requireWallJumpSurface && !hit.collider.TryGetComponent<WallJumpSurface>(out _))
                return false;

            normalSign = -side;
            return true;
        }

        private void StartWallJump()
        {
            float away = lastWallNormalSign != 0f ? lastWallNormalSign : -FacingSign;
            SetWallSliding(false);
            lastWallContactTime = float.NegativeInfinity;
            EndDash();
            CancelSmoothStop();
            isDoubleJumpStalling = false;

            FaceForTraversal(away > 0f);
            wallJumpDirection = away;
            wallJumpTimeRemaining = wallJumpDuration;
            lastWallJumpTime = Time.time;
            SetVelocity(MovementAxis * (away * wallJumpHorizontalForce) + Vector3.up * wallJumpForce);

            // A wall jump is a fresh, variable-height jump and refreshes the double jump.
            coyoteTimer = 0f;
            jumpBufferTimer = 0f;
            airJumpCount = 0;
            isExternalBounce = isPogoBounce = isAirJumpArc = hasJumpBeenCut = false;
            StartMinJumpHoldWindow();

            OnWallJumped?.Invoke();
        }

        private void SetWallSliding(bool sliding)
        {
            if (isWallSliding == sliding) return;
            isWallSliding = sliding;
            playerState?.SetWallSliding(sliding);
        }

        private void ClearWallState()
        {
            wallJumpTimeRemaining = 0f;
            lastWallContactTime = float.NegativeInfinity;
            SetWallSliding(false);
        }

        // ===== Wall contact =====
        // Walls never hold the player by friction: velocity into a wall is always removed, so
        // there is no contact force to grip and she falls along it whatever the wall's material.
        // Holding into a Wall-layer wall slides instead (UpdateWallSlideFixed caps the fall).
        private const float WallNormalMaxY = 0.7f;
        private const float WallProbeSkin = 0.05f;
        private const float WallContactGap = 0.01f;
        private readonly ContactPoint[] wallContactBuffer = new ContactPoint[8];
        private Vector3 wallContactNormal;
        private bool hasWallContact;

        private void OnCollisionEnter(Collision collision) => RecordWallContact(collision);
        private void OnCollisionStay(Collision collision) => RecordWallContact(collision);

        private void RecordWallContact(Collision collision)
        {
            int count = collision.GetContacts(wallContactBuffer);
            for (int i = 0; i < count; i++)
            {
                Vector3 normal = wallContactBuffer[i].normal;
                if (Mathf.Abs(normal.y) >= WallNormalMaxY) continue;
                wallContactNormal = new Vector3(normal.x, 0f, normal.z).normalized;
                hasWallContact = true;
                return;
            }
        }

        /// <summary>
        /// Sweeps only the main collider. Rigidbody.SweepTest sweeps every collider on the body,
        /// triggers included, so the LedgeCheck trigger (≈0.3 m ahead of the capsule) "hit" walls
        /// early and the player was stopped short of them in the air.
        /// </summary>
        private bool SweepBody(Vector3 direction, float distance, out RaycastHit hit)
        {
            hit = default;
            if (col == null) return false;
            int mask = BodyCollisionMask;
            Bounds bounds = col.bounds;
            const float skin = 0.02f; // shrink so the cast never starts touching the floor or the wall
            bool found;
            if (col is CapsuleCollider capsule)
            {
                Vector3 scale = transform.lossyScale;
                float radius = capsule.radius * Mathf.Max(Mathf.Abs(scale.x), Mathf.Abs(scale.z));
                float half = Mathf.Max(0f, bounds.extents.y - radius);
                Vector3 up = Vector3.up * half;
                found = Physics.CapsuleCast(bounds.center + up, bounds.center - up, Mathf.Max(0.01f, radius - skin),
                    direction, out hit, distance + skin, mask, QueryTriggerInteraction.Ignore);
            }
            else
            {
                found = Physics.BoxCast(bounds.center, bounds.extents - Vector3.one * skin, direction, out hit,
                    Quaternion.identity, distance + skin, mask, QueryTriggerInteraction.Ignore);
            }
            if (!found || hit.collider.attachedRigidbody == rb) return false;
            hit.distance = Mathf.Max(0f, hit.distance - skin);
            return true;
        }

        // Layers the player's body actually collides with (Physics layer matrix), cached per layer.
        private int bodyMaskLayer = -1;
        private int bodyMask;
        private int BodyCollisionMask
        {
            get
            {
                int layer = gameObject.layer;
                if (layer == bodyMaskLayer) return bodyMask;
                bodyMaskLayer = layer;
                bodyMask = 0;
                for (int i = 0; i < 32; i++)
                    if (!Physics.GetIgnoreLayerCollision(layer, i)) bodyMask |= 1 << i;
                bodyMask &= ~col.excludeLayers.value;
                return bodyMask;
            }
        }

        private void StopPushingIntoWallsFixed()
        {
            bool touched = hasWallContact;
            hasWallContact = false;
            // Runs on the ground too: the ground check accepts ground ~0.18 m below and gravity
            // is off while grounded, so pushing into a wall there let friction hold her in the
            // air beside it (stuck in the fall pose, and ground jumps were damped to nothing).
            if (IsPhysicsOverridden || rb.isKinematic) return;

            Vector3 velocity = rb.linearVelocity;
            Vector3 horizontal = new Vector3(velocity.x, 0f, velocity.z);
            float speed = horizontal.magnitude;
            if (speed < 0.01f) return;

            // Already touching (last step's contact): remove all speed into the wall.
            // A wall this step's motion is about to reach: keep just enough speed to close the
            // gap. Zeroing it there left her hovering up to one step's travel off the wall.
            Vector3 normal = touched ? wallContactNormal : Vector3.zero;
            float allowedInto = 0f;
            if (SweepBody(horizontal / speed, speed * Time.fixedDeltaTime + WallProbeSkin, out RaycastHit hit) &&
                Mathf.Abs(hit.normal.y) < WallNormalMaxY)
            {
                normal = new Vector3(hit.normal.x, 0f, hit.normal.z).normalized;
                if (!touched)
                    allowedInto = Mathf.Max(0f, hit.distance - WallContactGap) / Time.fixedDeltaTime;
            }

            float into = -Vector3.Dot(horizontal, normal);
            if (into > allowedInto)
                rb.linearVelocity = velocity + normal * (into - allowedInto);
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
        }

        private sealed class ControlLock : IDisposable
        {
            public static readonly ControlLock Empty = new(null);

            private Action release;

            public ControlLock(Action releaseAction)
            {
                release = releaseAction;
            }

            public void Dispose()
            {
                Action action = release;
                release = null;
                action?.Invoke();
            }
        }
    }
}
