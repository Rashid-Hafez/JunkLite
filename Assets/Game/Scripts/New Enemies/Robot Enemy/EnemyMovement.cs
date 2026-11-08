using System;
using System.Collections;
using UnityEngine;
using Unity.Cinemachine;

namespace junklite
{
    /// <summary>
    /// Axis-agnostic enemy movement for 2.5D platformer.
    /// 
    /// Movement plane is determined once at startup from the enemy's transform orientation:
    ///   - horizontalAxis = transform.right  (the "left/right" axis the enemy moves along)
    ///   - upAxis         = Vector3.up        (gravity is always world Y)
    ///   - depthAxis      = cross(up, horizontal) — the locked axis
    ///
    /// XY-plane enemy (rotation Y=0):   horizontal=(1,0,0), depth=(0,0,1) → freezes Z
    /// ZY-plane enemy (rotation Y=90):  horizontal=(0,0,-1), depth=(1,0,0) → freezes X
    ///
    /// Place the enemy in the scene, rotate its Y to face the correct wall,
    /// and all movement, knockback, and facing works automatically.
    /// </summary>
    /// <summary>What <see cref="EnemyMovement.ProbeGap"/> found past a ledge.</summary>
    public readonly struct GapProbe
    {
        /// <summary>A floor within the jump range, with a clear flight path and nobody standing on it.</summary>
        public readonly bool CanCross;
        /// <summary>A floor is there, but an enemy body is standing on the landing point.</summary>
        public readonly bool LandingOccupied;
        /// <summary>Landing point on the far floor (feet height).</summary>
        public readonly Vector3 Landing;
        /// <summary>Axis distance from the feet to the landing point.</summary>
        public readonly float Width;
        /// <summary>Unit direction of the step that was stopped.</summary>
        public readonly Vector3 Direction;

        public GapProbe(bool canCross, bool landingOccupied, Vector3 landing, float width, Vector3 direction)
        {
            CanCross = canCross;
            LandingOccupied = landingOccupied;
            Landing = landing;
            Width = width;
            Direction = direction;
        }
    }

    [RequireComponent(typeof(Rigidbody))]
    public class EnemyMovement : MonoBehaviour
    {
        [Header("Movement Settings")]
        [SerializeField] private float moveSpeed = 3f;
        [SerializeField] private float stoppingDistance = 0.1f;
        [SerializeField] private float gravityScale = 1f;

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
        [SerializeField, Min(1), Tooltip("Ground checks are throttled by this many physics ticks while settled.")]
        private int settledGroundCheckInterval = 3;

        [Header("Ledges")]
        [Tooltip("Stop at a drop instead of walking off it, and report it (LedgeReached) so the brain can jump the gap or give up. Knockback and launches still carry the body over the edge.")]
        [SerializeField] private bool ledgeGuard = true;
        [Tooltip("How far ahead of the feet the floor is probed, along the commanded direction (never less than the body's half width).")]
        [SerializeField, Min(0.1f)] private float ledgeProbeDistance = 0.7f;
        [Tooltip("A floor this far below the feet still counts as walkable (steps, ramps). Deeper is a ledge.")]
        [SerializeField, Min(0.1f)] private float ledgeDropTolerance = 0.8f;
        [Tooltip("Widest gap (near lip to far floor, clear air) this enemy will jump. 0 = never jumps. The player clears about 10 u with a held jump.")]
        [SerializeField, Min(0f)] private float maxGapWidth = 9f;
        [Tooltip("The far floor may be this much higher than the feet.")]
        [SerializeField, Min(0f)] private float gapLandingMaxRise = 1f;
        [Tooltip("The far floor may be this much lower than the feet.")]
        [SerializeField, Min(0f)] private float gapLandingMaxDrop = 2f;
        [Tooltip("Horizontal speed of the jump arc (u/s).")]
        [SerializeField, Min(0.5f)] private float gapJumpSpeed = 9f;
        [Tooltip("Arc height for a short hop. Longer jumps rise more (GapArcHeight), up to about 2.5x this.")]
        [SerializeField, Min(0f)] private float gapJumpHeight = 1.2f;
        [Tooltip("Layers that block the flight path over a gap. 0 = Default, Ground, Wall.")]
        [SerializeField] private LayerMask gapObstacleLayers;

        [Header("Fall Out Of World")]
        [Tooltip("This far below the last floor it stood on, the enemy is removed (FellOutOfWorld: it dies at once, no drops). 0 = off.")]
        [SerializeField, Min(0f)] private float fallKillDepth = 12f;
        [Tooltip("Absolute floor: below this world Y the enemy is removed regardless of where it stood.")]
        [SerializeField] private float fallKillWorldY = -40f;

        // Components
        private Rigidbody rb;
        private Collider bodyCollider;

        // State
        private Vector3 targetPosition;
        private Vector3 moveDirection;
        private bool isMoving;
        private bool isDirectionalMovement;
        private bool isDashing;
        private float currentSpeed;
        private float dashSpeed;
        private float statusMoveSpeedMultiplier = 1f;
        private int facingDirection = 1;

        // Axis-agnostic movement plane (cached once at startup)
        private Vector3 horizontalAxis = Vector3.right; // the "left/right" movement axis
        private Vector3 depthAxis = Vector3.forward;     // the locked axis
        // upAxis is always Vector3.up

        public Vector3 MovementAxis => horizontalAxis;

        // Knockback state
        private Vector3 knockbackVelocity;
        private bool isInKnockback;
        private float knockbackTimer;

        // Push-over-time state (parry pushback etc.)
        private bool isPushActive;

        // Launch state: airborne after a launching hit. Voluntary movement and the
        // idle horizontal zeroing are suspended until the owner ends the launch.
        private bool isLaunched;
        private bool launchArmed;
        // A launched fall faster than this that keeps less than this fraction of its speed
        // in one physics step was stopped by a collider: treat it as the landing.
        private const float ContactStopMinFallSpeed = 2f;
        private const float ContactStopKeptFraction = 0.25f;
        private bool landingPending;
        private bool launchEndsOnLanding;
        private float launchGravityScale = 1f;
        // Apex hang: while launched and moving slower than this vertically, use the
        // lighter hang gravity so the body floats at the top for air attacks.
        private float launchHangSpeed;
        private float launchHangGravityScale = 1f;
        // Timed slow fall after a mid-air hit (scaled time, so hit-stop extends it).
        private float launchFallHangScale = 1f;
        private float launchFallHangUntil;
        private float lastAirVerticalSpeed;
        private float landingImpactSpeed;

        // Ground state
        private bool isGrounded;
        private int settledGroundCheckCounter;

        // Ledge guard: a commanded step with no floor under it is braked and reported once per edge.
        private bool ledgeAhead;
        private int ledgeSign;
        private int landingMask;
        private int gapObstacleMask;
        private readonly Collider[] landingOverlapBuffer = new Collider[4];

        // Fall-out-of-world: height of the last floor stood on.
        private float lastGroundedY;
        private bool hasStoodOnGround;
        private bool fellOutReported;

        // Events for FSM integration
        /// <summary>
        /// Fired when knockback starts. FSM can use this to transition to a stunned state.
        /// </summary>
        public event Action OnKnockbackStart;

        /// <summary>
        /// Fired when knockback ends (grounded and velocity decayed). FSM can use this to resume normal behavior.
        /// </summary>
        public event Action OnKnockbackEnd;

        /// <summary>
        /// A commanded step was stopped at a drop (once per edge and direction). The argument says
        /// whether the gap can be jumped and where to land. Raised from FixedUpdate.
        /// </summary>
        public event Action<GapProbe> LedgeReached;

        /// <summary>The body fell below the kill depth / world floor. Raised once; the owner removes it.</summary>
        public event Action FellOutOfWorld;

        /// <summary>True while a commanded step is held back by the ledge guard.</summary>
        public bool LedgeAhead => ledgeAhead;
        /// <summary>+1 / -1 along the lane while braked at a ledge, 0 otherwise.</summary>
        public int LedgeDirection => ledgeAhead ? ledgeSign : 0;
        public float GapJumpSpeed => gapJumpSpeed;
        public float GapJumpHeight => gapJumpHeight;

        /// <summary>Arc height for a jump of this horizontal length: the base height for short hops, rising with distance.</summary>
        public float GapArcHeight(float distance)
        {
            return Mathf.Clamp(distance * 0.28f, gapJumpHeight, gapJumpHeight * 2.5f);
        }

        // Public accessors
        public float MoveSpeed { get => moveSpeed; set => moveSpeed = value; }
        public float CurrentSpeed => currentSpeed * statusMoveSpeedMultiplier;
        public float EffectiveMoveSpeed => moveSpeed * statusMoveSpeedMultiplier;
        public bool IsMoving => isMoving;
        public bool HasReachedDestination => !isMoving || (!isDirectionalMovement && DistanceToTarget <= stoppingDistance);
        public float DistanceToTarget => GetPlanarDistance(rb.position, targetPosition);
        public Vector3 MoveDirection => moveDirection;
        /// <summary>Actual physics velocity (zero while kinematic, e.g. mid-leap).</summary>
        public Vector3 Velocity => rb != null && !rb.isKinematic ? rb.linearVelocity : Vector3.zero;
        public int FacingDirection => facingDirection;
        public bool IsGrounded => isGrounded;
        public bool IsInKnockback => isInKnockback;
        /// <summary>True from a launching hit until the owner state (or the landing, for corpses) ends it.</summary>
        public bool IsLaunched => isLaunched;
        /// <summary>Only a dynamic body under gravity can be thrown (not flying, frozen or kinematic enemies).</summary>
        public bool CanBeLaunched => rb != null && !rb.isKinematic && rb.useGravity;
        public bool IgnoreKnockback { get => ignoreKnockback; set => ignoreKnockback = value; }
        private void Awake()
        {
            rb = GetComponent<Rigidbody>();
            ResolveLedgeMasks();
            bodyCollider = GetComponent<Collider>();

            // Configure Rigidbody for velocity-based movement with gravity
            rb.isKinematic = false;
            rb.interpolation = RigidbodyInterpolation.Interpolate;
            rb.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;

            // Cache the movement plane axes from the enemy's initial orientation
            CacheMovementAxes();

            // Freeze rotation + freeze the depth axis position
            rb.constraints = RigidbodyConstraints.FreezeRotationX |
                            RigidbodyConstraints.FreezeRotationY |
                            RigidbodyConstraints.FreezeRotationZ |
                            GetDepthConstraint();

            facingDirection = defaultFacing;
            ApplyFacing();
        }

        /// <summary>
        /// Determine movement plane from the enemy's transform orientation.
        /// Called once at Awake — enemies don't rotate at runtime.
        /// </summary>
        private void CacheMovementAxes()
        {
            // The enemy's local right IS the horizontal movement axis
            horizontalAxis = transform.right;

            // Snap to nearest world axis to avoid floating-point drift
            // (enemies are placed at 0, 90, 180, or 270 degree Y rotation)
            horizontalAxis = SnapToNearestAxis(horizontalAxis);

            // Depth axis = the axis we want to lock
            // It's perpendicular to both horizontal and up
            depthAxis = Vector3.Cross(Vector3.up, horizontalAxis).normalized;

            // If cross product is zero (shouldn't happen), fall back to Z
            if (depthAxis.sqrMagnitude < 0.01f)
                depthAxis = Vector3.forward;
        }

        /// <summary>
        /// Snap a direction to the nearest world axis (X or Z).
        /// Keeps things clean for the 4 cardinal placements.
        /// </summary>
        private Vector3 SnapToNearestAxis(Vector3 dir)
        {
            float absX = Mathf.Abs(dir.x);
            float absZ = Mathf.Abs(dir.z);

            if (absX >= absZ)
                return new Vector3(Mathf.Sign(dir.x), 0f, 0f);
            else
                return new Vector3(0f, 0f, Mathf.Sign(dir.z));
        }

        /// <summary>
        /// Returns the appropriate RigidbodyConstraints to freeze the depth axis.
        /// </summary>
        private RigidbodyConstraints GetDepthConstraint()
        {
            if (Mathf.Abs(depthAxis.z) > 0.5f)
                return RigidbodyConstraints.FreezePositionZ; // Standard XY movement
            else if (Mathf.Abs(depthAxis.x) > 0.5f)
                return RigidbodyConstraints.FreezePositionX; // ZY movement
            else
                return RigidbodyConstraints.FreezePositionZ; // Fallback
        }

        private void FixedUpdate()
        {
            if (rb.isKinematic) return;

            bool needsFrequentGroundCheck = !isGrounded || isMoving || isInKnockback || isPushActive || isLaunched;
            if (needsFrequentGroundCheck || ++settledGroundCheckCounter >= settledGroundCheckInterval)
            {
                settledGroundCheckCounter = 0;
                CheckGrounded();
            }

            if (isGrounded)
            {
                lastGroundedY = rb.position.y;
                hasStoodOnGround = true;
            }
            UpdateFallOutOfWorld();

            float effectiveGravityScale = isLaunched ? CurrentLaunchGravityScale() : gravityScale;
            if (rb.useGravity && (effectiveGravityScale > 1f || (isLaunched && effectiveGravityScale < 1f)))
            {
                rb.AddForce(Vector3.down * Physics.gravity.magnitude * (effectiveGravityScale - 1f), ForceMode.Acceleration);
            }

            if (isLaunched)
            {
                HandleLaunch();
                return;
            }

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
            // Ground check is always downward — axis-independent
            // The ray starts at the feet: enemy roots sit at the body centre (the
            // grunt's is ~1.3 u above the floor), so a ray from the transform would
            // never reach the ground.
            isGrounded = Physics.Raycast(
                GetFeetPosition() + Vector3.up * 0.05f,
                Vector3.down,
                groundCheckDistance + 0.05f,
                groundLayers,
                QueryTriggerInteraction.Ignore
            );
        }

        /// <summary>Bottom of the body collider under the transform (the transform itself without one).</summary>
        public Vector3 GetFeetPosition()
        {
            Vector3 position = rb != null ? rb.position : transform.position;
            if (bodyCollider != null && bodyCollider.enabled)
                position.y = bodyCollider.bounds.min.y;
            return position;
        }

        // ============================================================
        // VELOCITY HELPERS — all axis-agnostic
        // ============================================================

        /// <summary>
        /// Extract the horizontal speed component from a velocity vector.
        /// </summary>
        private float GetHorizontalSpeed(Vector3 velocity)
        {
            return Vector3.Dot(velocity, horizontalAxis);
        }

        /// <summary>
        /// Build a final velocity vector from horizontal and vertical components.
        /// Depth axis is always zero.
        /// </summary>
        private Vector3 BuildVelocity(float horizontal, float vertical)
        {
            return horizontalAxis * horizontal + Vector3.up * vertical;
        }

        /// <summary>
        /// Project a world-space velocity onto the movement plane (horizontal + up).
        /// Strips out any depth component.
        /// </summary>
        private Vector3 ProjectOntoMovementPlane(Vector3 velocity)
        {
            float h = Vector3.Dot(velocity, horizontalAxis);
            float v = velocity.y;
            return BuildVelocity(h, v);
        }

        // ============================================================
        // KNOCKBACK
        // ============================================================

        private void HandleKnockback()
        {
            knockbackTimer += Time.fixedDeltaTime;

            float t = knockbackDrag * Time.fixedDeltaTime;

            // Decay the horizontal component of knockback
            float knockH = GetHorizontalSpeed(knockbackVelocity);
            knockH = Mathf.Lerp(knockH, 0f, t);

            if (rb.useGravity)
            {
                // Ground enemy — let physics handle Y, decay horizontal
                rb.linearVelocity = BuildVelocity(knockH, rb.linearVelocity.y);
            }
            else
            {
                // Flying enemy — decay both horizontal and vertical
                float knockV = knockbackVelocity.y;
                knockV = Mathf.Lerp(knockV, 0f, t);
                rb.linearVelocity = BuildVelocity(knockH, knockV);
                knockbackVelocity = BuildVelocity(knockH, knockV);
            }

            // Update stored knockback so next frame's decay continues smoothly
            knockbackVelocity = BuildVelocity(knockH, knockbackVelocity.y);

            if (knockbackTimer >= knockbackDuration)
                EndKnockback();
        }

        private void EndKnockback()
        {
            isInKnockback = false;
            knockbackVelocity = Vector3.zero;
            RefreshCollisionDetectionMode();
            OnKnockbackEnd?.Invoke();
        }

        // ============================================================
        // LAUNCH
        // ============================================================

        /// <summary>
        /// Records the vertical speed every airborne step and flags one landing
        /// per flight, using the speed from the step before contact (by the time
        /// the ground check sees the floor the contact has already killed it).
        /// </summary>
        private void HandleLaunch()
        {
            if (!isGrounded)
            {
                // Contact stop: gravity only ever speeds a fall up, so a fall that lost most of
                // its speed in one step hit something solid. Counts as a landing even when that
                // floor is on a layer outside Ground Layers (otherwise the body lay on it in the
                // flight pose until Max Air Time ran out).
                float verticalSpeed = rb.linearVelocity.y;
                if (launchArmed && lastAirVerticalSpeed < -ContactStopMinFallSpeed &&
                    verticalSpeed > lastAirVerticalSpeed * ContactStopKeptFraction)
                {
                    launchArmed = false;
                    landingImpactSpeed = -lastAirVerticalSpeed;
                    landingPending = true;
                    if (launchEndsOnLanding)
                        EndLaunch();
                    return;
                }

                launchArmed = true;
                lastAirVerticalSpeed = verticalSpeed;
                TryLandThisStep();
                return;
            }

            if (!launchArmed)
                return;

            launchArmed = false;
            landingImpactSpeed = Mathf.Max(0f, -lastAirVerticalSpeed);
            landingPending = true;

            if (launchEndsOnLanding)
                EndLaunch();
        }

        /// <summary>
        /// A fast fall moves up to half a unit per physics step, so the body would end
        /// the step inside the floor and the solver would push it back out at up to
        /// the depenetration limit (10 u/s): a big rebound before any authored bounce.
        /// When the floor is within this step's travel, slow the fall so the feet
        /// arrive exactly on it, and report the landing with the real impact speed.
        /// </summary>
        private void TryLandThisStep()
        {
            Vector3 velocity = rb.linearVelocity;
            if (velocity.y >= 0f)
                return;

            float dt = Time.fixedDeltaTime;
            float gravity = rb.useGravity ? Physics.gravity.magnitude * CurrentLaunchGravityScale() : 0f;
            // Velocity is integrated before position, so this step's gravity adds travel too.
            float travel = (-velocity.y + gravity * dt) * dt;
            const float Skin = 0.02f;

            Vector3 origin = GetFeetPosition() + Vector3.up * 0.05f;
            if (!Physics.Raycast(origin, Vector3.down, out RaycastHit hit, travel + 0.05f + Skin,
                    groundLayers, QueryTriggerInteraction.Ignore))
            {
                return;
            }

            // Put the feet on the floor now and leave no downward speed: arriving at
            // full speed still made the solver stop (and push back) a fast body.
            // +gravity*dt cancels this step's gravity so the body stays put.
            float gap = Mathf.Max(0f, hit.distance - 0.05f - Skin);
            rb.position += Vector3.down * gap;
            velocity.y = gravity * dt;
            rb.linearVelocity = velocity;

            launchArmed = false;
            landingImpactSpeed = Mathf.Max(0f, -lastAirVerticalSpeed);
            landingPending = true;

            if (launchEndsOnLanding)
                EndLaunch();
        }

        /// <summary>
        /// Throws the body with an explicit velocity (projected onto the movement
        /// plane) under a launch-only gravity scale. Knockback and voluntary movement
        /// are suspended until <see cref="EndLaunch"/>. With <paramref name="endOnLanding"/>
        /// the launch ends itself on the first landing (corpses, no owner state).
        /// </summary>
        public void Launch(Vector3 velocity, float gravityScale, bool endOnLanding = false)
        {
            if (!CanBeLaunched)
                return;

            Stop();
            isInKnockback = false;
            knockbackVelocity = Vector3.zero;

            isLaunched = true;
            launchArmed = false;
            landingPending = false;
            launchEndsOnLanding = endOnLanding;
            launchGravityScale = Mathf.Max(1f, gravityScale);

            rb.linearVelocity = ProjectOntoMovementPlane(velocity);
            lastAirVerticalSpeed = rb.linearVelocity.y;
            RefreshCollisionDetectionMode();
        }

        /// <summary>Replaces the velocity mid-launch (juggle hit, bounce). No-op when not launched.</summary>
        public void SetLaunchVelocity(Vector3 velocity)
        {
            if (!isLaunched || rb == null)
                return;

            rb.linearVelocity = ProjectOntoMovementPlane(velocity);
            lastAirVerticalSpeed = rb.linearVelocity.y;
            launchArmed = false;
            landingPending = false;
        }

        /// <summary>Changes the launch-only gravity mid-flight (a softer bounce, a juggle). No-op when not launched.</summary>
        public void SetLaunchGravityScale(float gravityScale)
        {
            if (isLaunched)
                launchGravityScale = Mathf.Max(1f, gravityScale);
        }

        /// <summary>True once per landing while launched, with the downward speed just before contact.</summary>
        public bool TryConsumeLanding(out float impactSpeed)
        {
            impactSpeed = landingImpactSpeed;
            if (!landingPending)
                return false;

            landingPending = false;
            return true;
        }

        /// <summary>Lets the launch end itself on the next landing (the owner state is gone).</summary>
        public void ReleaseLaunchToPhysics()
        {
            launchEndsOnLanding = true;
        }

        /// <summary>
        /// Floats the launched body near its apex: while its vertical speed is below
        /// <paramref name="speed"/>, gravity uses <paramref name="gravityScale"/>.
        /// 0 speed turns the hang off (bounce, spike).
        /// </summary>
        public void SetLaunchHang(float speed, float gravityScale)
        {
            launchHangSpeed = Mathf.Max(0f, speed);
            launchHangGravityScale = Mathf.Max(0.05f, gravityScale);
        }

        /// <summary>
        /// While launched and falling, use <paramref name="gravityScale"/> for <paramref name="seconds"/>
        /// (the air time earned by a mid-air hit). Rising is unaffected. 0 seconds clears it.
        /// </summary>
        public void SetLaunchFallHang(float gravityScale, float seconds)
        {
            launchFallHangScale = Mathf.Max(0.05f, gravityScale);
            launchFallHangUntil = seconds > 0f ? Time.time + seconds : 0f;
        }

        /// <summary>True while a mid-air hit's slow fall is still running.</summary>
        public bool IsLaunchFallHangActive => isLaunched && Time.time < launchFallHangUntil;

        private float CurrentLaunchGravityScale()
        {
            if (Time.time < launchFallHangUntil && rb != null && rb.linearVelocity.y <= 0f)
                return launchFallHangScale;
            if (launchHangSpeed > 0f && rb != null && Mathf.Abs(rb.linearVelocity.y) < launchHangSpeed)
                return launchHangGravityScale;
            return launchGravityScale;
        }

        public void EndLaunch()
        {
            if (!isLaunched)
                return;

            launchHangSpeed = 0f;
            launchFallHangUntil = 0f;

            isLaunched = false;
            launchArmed = false;
            landingPending = false;
            launchEndsOnLanding = false;
            launchGravityScale = 1f;
            RefreshCollisionDetectionMode();
        }

        // ============================================================
        // MOVEMENT
        // ============================================================

        private void HandleMovement()
        {
            if (!isMoving)
            {
                ClearLedge();
                // Let push-over-time coroutine control velocity while active
                if (isPushActive) return;

                // When not moving, zero out horizontal velocity (preserve Y for gravity)
                float verticalVel = rb.useGravity ? rb.linearVelocity.y : 0f;
                rb.linearVelocity = BuildVelocity(0f, verticalVel);
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

            // Build final velocity — horizontal from desired, vertical from physics or desired
            float desiredH = GetHorizontalSpeed(desiredVelocity);
            float desiredV = desiredVelocity.y;

            // No commanded step leaves the floor. The LedgeReached handler may switch state
            // (GapJumpState makes the body kinematic), so stop here when it did.
            desiredH = GuardLedge(desiredH);
            if (rb.isKinematic)
                return;

            Vector3 newVelocity;
            if (rb.useGravity)
            {
                // Ground enemy — apply horizontal, let physics handle Y
                newVelocity = BuildVelocity(desiredH, rb.linearVelocity.y);
            }
            else
            {
                // Flying enemy — apply both
                newVelocity = BuildVelocity(desiredH, desiredV);
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
            return planarDir * speed * statusMoveSpeedMultiplier;
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

            return moveDirection * currentSpeed * statusMoveSpeedMultiplier;
        }

        // ============================================================
        // PUBLIC API — unchanged signatures
        // ============================================================

        /// <summary>
        /// Start moving towards a position.
        /// </summary>
        public void MoveTo(Vector3 position)
        {
            if (isInKnockback) return;

            targetPosition = position;
            isMoving = true;
            isDirectionalMovement = false;
            isDashing = false;
            currentSpeed = moveSpeed;
            RefreshCollisionDetectionMode();
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
            RefreshCollisionDetectionMode();
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
            RefreshCollisionDetectionMode();
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
            RefreshCollisionDetectionMode();
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
            RefreshCollisionDetectionMode();
            UpdateFacingFromDirection(direction);
        }

        /// <summary>
        /// Applies the aggregate status multiplier without mutating the configured
        /// base speed. This prevents a slow expiring from overwriting upgrades.
        /// </summary>
        public void SetStatusMoveSpeedMultiplier(float multiplier)
        {
            statusMoveSpeedMultiplier = Mathf.Max(0f, multiplier);
        }

        /// <summary>
        /// Stop all movement immediately.
        /// </summary>
        public void Stop()
        {
            ClearLedge();
            isMoving = false;
            isDirectionalMovement = false;
            isDashing = false;
            currentSpeed = 0f;
            moveDirection = Vector3.zero;
            RefreshCollisionDetectionMode();
        }

        /// <summary>
        /// Apply knockback force to the enemy.
        /// Returns true if knockback was applied, false if ignored.
        /// The force vector is in world space — it will be projected onto this enemy's movement plane.
        /// </summary>
        public bool ApplyKnockback(Vector3 force)
        {
            if (ignoreKnockback || isLaunched)
                return false;

            Stop();

            isInKnockback = true;
            knockbackTimer = 0f;
            RefreshCollisionDetectionMode();

            // Project the knockback force onto our movement plane
            knockbackVelocity = ProjectOntoMovementPlane(force);

            // Immediately apply
            rb.linearVelocity = knockbackVelocity;

            OnKnockbackStart?.Invoke();
            return true;
        }

        /// <summary>
        /// Apply a gradual push force over several physics steps (e.g. from a parry).
        /// The worldDirection is projected onto this enemy's movement plane automatically.
        /// </summary>
        public void ApplyPushOverTime(Vector3 worldDirection, float horizontalImpulse, float upwardImpulse, float duration)
        {
            if (ignoreKnockback) return;
            StartCoroutine(CoPushOverTime(worldDirection, horizontalImpulse, upwardImpulse, duration));
        }

        private IEnumerator CoPushOverTime(Vector3 worldDir, float totalHorizontalImpulse, float totalUpwardImpulse, float duration)
        {
            if (rb == null || duration <= 0f) yield break;

            isPushActive = true;
            RefreshCollisionDetectionMode();

            float hDot = Vector3.Dot(worldDir, horizontalAxis);
            float hSign = hDot >= 0f ? 1f : -1f;
            Vector3 horizAccel = horizontalAxis * hSign * (totalHorizontalImpulse / Mathf.Max(0.0001f, duration));
            Vector3 upAccel = Vector3.up * (totalUpwardImpulse / Mathf.Max(0.0001f, duration));

            float elapsed = 0f;
            while (elapsed < duration)
            {
                rb.AddForce(horizAccel, ForceMode.Acceleration);
                rb.AddForce(upAccel, ForceMode.Acceleration);
                yield return new WaitForFixedUpdate();
                elapsed += Time.fixedDeltaTime;
            }

            isPushActive = false;
            RefreshCollisionDetectionMode();

            // Stop sliding after push ends
            if (rb != null)
            {
                float verticalVel = rb.useGravity ? rb.linearVelocity.y : 0f;
                rb.linearVelocity = BuildVelocity(0f, verticalVel);
            }
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

        private void RefreshCollisionDetectionMode()
        {
            if (rb == null || rb.isKinematic)
                return;

            CollisionDetectionMode desired = isDashing || isInKnockback || isPushActive || isLaunched
                ? CollisionDetectionMode.ContinuousDynamic
                : CollisionDetectionMode.ContinuousSpeculative;
            if (rb.collisionDetectionMode != desired)
                rb.collisionDetectionMode = desired;
        }

        // ============================================================
        // FACING
        // ============================================================

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
            // Project direction onto horizontal axis to determine facing
            float dot = Vector3.Dot(horizontalAxis, direction);

            if (dot > 0.01f)
                facingDirection = 1;
            else if (dot < -0.01f)
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

        // ============================================================
        // LEDGES AND FALLING OUT OF THE WORLD
        // ============================================================

        private void ResolveLedgeMasks()
        {
            // Bodies are never floor: an enemy or the player standing past the edge must not read as ground.
            int bodies = 0;
            foreach (string name in new[] { "Enemies", "Player", "EnemySensor", "Damageable" })
            {
                int layer = LayerMask.NameToLayer(name);
                if (layer >= 0)
                    bodies |= 1 << layer;
            }

            landingMask = groundLayers.value & ~bodies;
            gapObstacleMask = gapObstacleLayers.value != 0
                ? gapObstacleLayers.value
                : LayerMask.GetMask("Default", "Ground", "Wall");
        }

        /// <summary>
        /// Brakes a commanded step that has no floor under it, and reports the edge once.
        /// Knockback and launches never come through here, so they still carry the body off.
        /// </summary>
        private float GuardLedge(float desiredH)
        {
            if (!ledgeGuard || !rb.useGravity || !isGrounded || Mathf.Abs(desiredH) < 0.01f)
            {
                ClearLedge();
                return desiredH;
            }

            int sign = desiredH > 0f ? 1 : -1;
            if (HasFloorAhead(sign))
            {
                ClearLedge();
                return desiredH;
            }

            if (!ledgeAhead || ledgeSign != sign)
            {
                ledgeAhead = true;
                ledgeSign = sign;
                LedgeReached?.Invoke(ProbeGap(sign));
            }

            return 0f;
        }

        private void ClearLedge()
        {
            ledgeAhead = false;
            ledgeSign = 0;
        }

        private float ProbeDistance()
        {
            float halfWidth = bodyCollider != null && bodyCollider.enabled
                ? Mathf.Abs(Vector3.Dot(bodyCollider.bounds.extents, horizontalAxis))
                : 0f;
            return Mathf.Max(ledgeProbeDistance, halfWidth + 0.2f);
        }

        /// <summary>
        /// Floor a step ahead within the drop tolerance. A wall ahead also counts as "not a
        /// ledge": the body is blocked, not about to fall, and the brains already handle walls.
        /// </summary>
        private bool HasFloorAhead(int sign)
        {
            Vector3 feet = GetFeetPosition();
            Vector3 dir = horizontalAxis * sign;
            float ahead = ProbeDistance();

            if (Physics.Raycast(feet + Vector3.up * 0.5f, dir, ahead + 0.1f, gapObstacleMask, QueryTriggerInteraction.Ignore))
                return true;

            // From above the feet so a rising ramp is hit from outside, not from inside its collider.
            float rise = gapLandingMaxRise + 0.3f;
            Vector3 origin = feet + dir * ahead + Vector3.up * rise;
            return Physics.Raycast(origin, Vector3.down, rise + ledgeDropTolerance, landingMask, QueryTriggerInteraction.Ignore);
        }

        /// <summary>
        /// Looks for a floor past the edge in the given direction (+1 / -1 along the lane) within
        /// Max Gap Width, at most Max Rise above or Max Drop below the feet, with a clear flight
        /// path. Public so a brain can re-check before committing.
        /// </summary>
        public GapProbe ProbeGap(int sign)
        {
            Vector3 feet = GetFeetPosition();
            Vector3 dir = horizontalAxis * (sign >= 0 ? 1f : -1f);
            if (maxGapWidth <= 0f)
                return new GapProbe(false, false, default, 0f, dir);

            const float Step = 0.4f;
            const float LipClearance = 0.35f;
            float rise = gapLandingMaxRise + 0.1f;
            float rayLength = rise + gapLandingMaxDrop;

            // The gap starts where the floor stopped: the probe point in front of the feet. Width
            // is the clear air from there to the far floor, so Max Gap Width reads like the level.
            float lipDistance = ProbeDistance();
            // The probe covers lipDistance plus the lip clearance past the far edge.
            float maxProbe = lipDistance + maxGapWidth + LipClearance;

            for (float d = lipDistance + Step; d <= maxProbe; d += Step)
            {
                Vector3 top = feet + dir * d + Vector3.up * rise;
                if (!Physics.Raycast(top, Vector3.down, out RaycastHit hit, rayLength, landingMask, QueryTriggerInteraction.Ignore))
                    continue;
                if (hit.normal.y < 0.7f)
                    continue;

                // Land a little past the lip, and only where that spot is floor too.
                Vector3 landing = hit.point + dir * LipClearance;
                if (!Physics.Raycast(landing + Vector3.up * rise, Vector3.down, out RaycastHit lipHit, rayLength, landingMask, QueryTriggerInteraction.Ignore)
                    || Mathf.Abs(lipHit.point.y - hit.point.y) > 0.3f)
                    landing = hit.point;
                else
                    landing.y = lipHit.point.y;

                // Clear air: from the near lip to where the far floor began (the previous probe step was empty).
                float width = Mathf.Max(0f, Mathf.Abs(Vector3.Dot(hit.point - feet, horizontalAxis)) - lipDistance);
                if (width > maxGapWidth)
                    return new GapProbe(false, false, landing, width, dir);

                if (!IsFlightPathClear(feet, landing))
                    return new GapProbe(false, false, landing, width, dir);

                bool occupied = IsLandingOccupied(landing);
                return new GapProbe(!occupied, occupied, landing, width, dir);
            }

            return new GapProbe(false, false, default, 0f, dir);
        }

        private bool IsFlightPathClear(Vector3 from, Vector3 to)
        {
            Vector3 delta = to - from;
            delta.y = 0f;
            float distance = delta.magnitude;
            if (distance <= 0.001f)
                return true;
            Vector3 dir = delta / distance;

            // Chest height along the whole flight, and the apex height over the middle.
            Vector3 chest = Vector3.up * 1f;
            if (Physics.Raycast(from + chest, dir, distance, gapObstacleMask, QueryTriggerInteraction.Ignore))
                return false;

            Vector3 apex = Vector3.up * (1f + GapArcHeight(distance));
            Vector3 mid = (from + to) * 0.5f;
            return !Physics.Raycast(from + apex, (mid + apex - (from + apex)).normalized, distance * 0.5f, gapObstacleMask, QueryTriggerInteraction.Ignore)
                && !Physics.Raycast(mid + apex, (to + apex - (mid + apex)).normalized, distance * 0.5f, gapObstacleMask, QueryTriggerInteraction.Ignore);
        }

        private bool IsLandingOccupied(Vector3 landing)
        {
            int enemies = LayerMask.NameToLayer("Enemies");
            if (enemies < 0)
                return false;

            int count = Physics.OverlapSphereNonAlloc(landing + Vector3.up * 1f, 0.45f, landingOverlapBuffer, 1 << enemies, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < count; i++)
            {
                Collider other = landingOverlapBuffer[i];
                if (other != null && other.transform.root != transform.root)
                    return true;
            }

            return false;
        }

        private void UpdateFallOutOfWorld()
        {
            if (fellOutReported)
                return;

            float y = rb.position.y;
            bool below = y < fallKillWorldY
                || (fallKillDepth > 0f && hasStoodOnGround && y < lastGroundedY - fallKillDepth);
            if (!below)
                return;

            fellOutReported = true;
            FellOutOfWorld?.Invoke();
        }

        // ============================================================
        // PLANAR MATH — axis-agnostic
        // ============================================================

        /// <summary>
        /// Project a direction onto the movement plane (horizontal + up), stripping depth.
        /// </summary>
        private Vector3 GetPlanarDirection(Vector3 direction)
        {
            float h = Vector3.Dot(direction, horizontalAxis);
            float v = direction.y;
            return (horizontalAxis * h + Vector3.up * v).normalized;
        }

        /// <summary>
        /// Distance between two points on the movement plane (ignoring depth axis).
        /// </summary>
        private float GetPlanarDistance(Vector3 a, Vector3 b)
        {
            Vector3 diff = a - b;
            float h = Vector3.Dot(diff, horizontalAxis);
            float v = diff.y;
            return Mathf.Sqrt(h * h + v * v);
        }

        /// <summary>Distance between two points along the horizontal movement axis.</summary>
        public float GetAbsAxisDistance(Vector3 a, Vector3 b)
        {
            return Mathf.Abs(Vector3.Dot(a - b, horizontalAxis));
        }

        /// <summary>Signed distance from a to b along the movement axis (positive = "right").</summary>
        public float GetSignedAxisDistance(Vector3 from, Vector3 to)
        {
            return Vector3.Dot(to - from, horizontalAxis);
        }

        // ============================================================
        // DEBUG
        // ============================================================

        private void OnDrawGizmosSelected()
        {
            Vector3 pos = Application.isPlaying && rb != null ? rb.position : transform.position;

            // Draw ground check
            Gizmos.color = isGrounded ? Color.green : Color.red;
            Vector3 feet = Application.isPlaying ? GetFeetPosition() : pos;
            Gizmos.DrawLine(feet + Vector3.up * 0.05f, feet + Vector3.down * groundCheckDistance);

            // Draw horizontal movement axis (blue line shows which direction "right" is)
            Gizmos.color = Color.blue;
            Vector3 hAxis = Application.isPlaying ? horizontalAxis : transform.right;
            Gizmos.DrawRay(pos, hAxis * 1.5f);

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
