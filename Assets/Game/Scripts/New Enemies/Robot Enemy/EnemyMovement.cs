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
        private bool landingPending;
        private bool launchEndsOnLanding;
        private float launchGravityScale = 1f;
        // Apex hang: while launched and moving slower than this vertically, use the
        // lighter hang gravity so the body floats at the top for air attacks.
        private float launchHangSpeed;
        private float launchHangGravityScale = 1f;
        private float lastAirVerticalSpeed;
        private float landingImpactSpeed;

        // Ground state
        private bool isGrounded;
        private int settledGroundCheckCounter;

        // Events for FSM integration
        /// <summary>
        /// Fired when knockback starts. FSM can use this to transition to a stunned state.
        /// </summary>
        public event Action OnKnockbackStart;

        /// <summary>
        /// Fired when knockback ends (grounded and velocity decayed). FSM can use this to resume normal behavior.
        /// </summary>
        public event Action OnKnockbackEnd;

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
                launchArmed = true;
                lastAirVerticalSpeed = rb.linearVelocity.y;
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

        private float CurrentLaunchGravityScale()
        {
            if (launchHangSpeed > 0f && rb != null && Mathf.Abs(rb.linearVelocity.y) < launchHangSpeed)
                return launchHangGravityScale;
            return launchGravityScale;
        }

        public void EndLaunch()
        {
            if (!isLaunched)
                return;

            launchHangSpeed = 0f;

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
