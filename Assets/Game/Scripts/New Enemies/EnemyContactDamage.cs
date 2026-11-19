using UnityEngine;

namespace junklite
{
    /// <summary>
    /// Body contact: a shove. A player running at full speed, or for long enough, into its body
    /// gets a quick shove: the enemy plays its attack clip fast (no attack warning, can't be
    /// parried), then she takes a little damage and is pushed back to the side she came from
    /// (lane speed replaced, not added). This is not the enemy's attack: that is MeleeAttackState.
    /// Anything slower walks through it, and so does a dash, and any body that is stunned or
    /// parried. One push per cooldown. Player and enemy bodies do not collide (layer matrix),
    /// so without this she passes freely. Only a player facing the body is pushed: one that
    /// backs into it (e.g. shoved there by another enemy) passes through, so two flanking
    /// enemies can't ping-pong her.
    ///
    /// Optional per enemy: add it to the enemy root and tune per prefab (weak enemies shove
    /// softly, heavies hard).
    ///
    /// Cost per physics step: a few flag checks and one bounds test; the exact overlap
    /// (<see cref="Physics.ComputePenetration"/>) runs only when the bounds meet.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(EnemyCharacter))]
    public sealed class EnemyContactDamage : MonoBehaviour
    {
        [Header("Shove")]
        [Tooltip("x = lane speed back to the side the player came from (must beat her run speed). y = minimum upward speed.")]
        [SerializeField] private Vector2 knockback = new(9f, 2f);
        [Tooltip("Seconds her input can't cancel the push. No stun and no Hurt clip.")]
        [SerializeField, Min(0f)] private float hitstun = 0.2f;
        [Tooltip("After a shove, seconds before this body can shove again. Meanwhile she passes through it freely.")]
        [SerializeField, Min(0f)] private float shoveCooldown = 1f;
        [Tooltip("Damage of the shove: a little. 0 = push only. Unparryable, no attack warning.")]
        [SerializeField, Min(0f)] private float shoveDamage = 3f;
        [Tooltip("Seconds the attack clip plays for the shove (fast).")]
        [SerializeField, Min(0.05f)] private float shoveClipDuration = 0.25f;
        [Tooltip("Seconds into the shove clip when she is hit and pushed.")]
        [SerializeField, Min(0f)] private float shoveImpactDelay = 0.08f;

        [Header("When It Pushes")]
        [Tooltip("Pushes when her speed into the body is at least this fraction of her run speed. Above 1 = only the run time below can push.")]
        [SerializeField, Min(0f)] private float pushSpeedFraction = 0.95f;
        [Tooltip("Also pushes when she has held the run this long, at any speed.")]
        [SerializeField, Min(0f)] private float pushRunTime = 1.5f;

        [Header("Contact")]
        [Tooltip("How deep (metres) the bodies must overlap before it counts. Avoids triggering on a brush of the edges.")]
        [SerializeField, Min(0f)] private float minOverlap = 0.02f;
        [Tooltip("Solid body collider. Empty = the first non-trigger collider on this object.")]
        [SerializeField] private Collider bodyCollider;

        [Header("Exceptions")]
        [Tooltip("No contact while this enemy is in the air (vault, leap, launch).")]
        [SerializeField] private bool groundedOnly = true;

        private EnemyCharacter enemy;
        private EnemyMovement movement;

        private PlayerCharacter player;
        private Collider playerBody;

        private float lastShoveTime = float.NegativeInfinity;
        // Lane side (+1/-1, relative to this enemy) the player was on when the overlap began; 0 = not overlapping.
        private float entrySide;
        // She entered with her back to this body (e.g. shoved into it by another enemy): no shove,
        // no damage, until she turns to face it or the overlap ends. Stops flanking enemies ping-ponging her.
        private bool passThrough;
        // She came in on a dash, or this body was stunned: free pass until the overlap ends.
        private bool freePass;
        // A shove whose clip is playing: the hit lands at pendingImpactTime, pushing along pendingDirection.
        private bool impactPending;
        private float pendingImpactTime;
        private Vector3 pendingDirection;

        private void Awake()
        {
            enemy = GetComponent<EnemyCharacter>();
            movement = GetComponent<EnemyMovement>();
            if (bodyCollider == null)
                bodyCollider = FindBodyCollider();
        }

        private void OnDisable()
        {
            impactPending = false;
            EndContact();
        }

        private void FixedUpdate()
        {
            if (impactPending && Time.time >= pendingImpactTime)
                LandShove();

            if (bodyCollider == null || !enemy.IsAlive || enemy.IsTutorialFrozen)
            {
                EndContact();
                return;
            }

            if (!TryRefreshPlayer() || !IsOverlapping())
            {
                EndContact();
                return;
            }

            Vector3 axis = movement != null ? movement.MovementAxis : transform.right;
            if (entrySide == 0f)
            {
                entrySide = ResolveEntrySide(axis);
                passThrough = !FacesBody(entrySide, axis);
            }
            else if (passThrough)
            {
                // Turning to face the body while inside it blocks again, pushing her out the
                // side she is on now (never through the body). Not at dead centre: no clear side.
                float side = OffsetSide(axis);
                if (side != 0f && FacesBody(side, axis))
                {
                    entrySide = side;
                    passThrough = false;
                }
            }

            // A dash or a stun that ends inside the body must not push her out of it.
            if (IsPassable())
                freePass = true;

            // Push on cooldown: the body is passable until it is ready again.
            if (freePass || passThrough || !CanShove() || !IsRunningIntoBody(axis) ||
                Time.time < lastShoveTime + shoveCooldown)
                return;

            lastShoveTime = Time.time;
            pendingDirection = axis * entrySide;
            pendingImpactTime = Time.time + shoveImpactDelay;
            impactPending = true;
            movement?.FaceTarget(player.transform.position);
            enemy.AnimationPresenter?.PlayShove(shoveClipDuration);
            if (shoveImpactDelay <= 0f)
                LandShove();
        }

        private void LandShove()
        {
            impactPending = false;
            if (player == null || !player.IsAlive || !enemy.IsAlive || IsPassable())
                return;

            if (shoveDamage > 0f)
                DamageReceiverUtility.Receive(player, new DamageRequest(shoveDamage, gameObject).AsUnparryable());
            player.ApplyBodyPush(pendingDirection, knockback, hitstun);
        }

        private void EndContact()
        {
            entrySide = 0f;
            passThrough = false;
            freePass = false;
        }

        private bool CanShove()
        {
            if (groundedOnly && movement != null && !movement.IsGrounded)
                return false;

            // A launched (juggled) or dead body never shoves; air combos stay inside it.
            IState state = enemy.StateMachine != null ? enemy.StateMachine.CurrentState : null;
            if (state is LaunchedState || state is DeadState)
                return false;

            if (player.IsGrabbed)
                return false;

            PlayerState playerState = player.State;
            // An attack lunge moves her fast too, and must not be read as running into the body.
            if (playerState != null && playerState.IsAttacking)
                return false;

            return true;
        }

        private bool IsPassable()
        {
            IState state = enemy.StateMachine != null ? enemy.StateMachine.CurrentState : null;
            if (state is StunnedState || state is ParriedState)
                return true;

            PlayerState playerState = player.State;
            return playerState != null && (playerState.IsDashing || playerState.IsRolling);
        }

        // She holds a direction toward the body and is moving that way, fast enough or for long enough.
        private bool IsRunningIntoBody(Vector3 axis)
        {
            Character2D5Controller controller = player.Controller;
            if (controller == null)
                return false;

            float input = controller.HorizontalInput * Vector3.Dot(controller.MovementAxis, axis);
            float speedIn = -Vector3.Dot(controller.Velocity, axis) * entrySide;
            return input * entrySide < -0.1f &&
                   ShouldPush(speedIn, controller.EffectiveMoveSpeed, controller.GroundRunTime, pushSpeedFraction, pushRunTime);
        }

        /// <summary>A push needs her moving in, at the speed fraction or after the run time.</summary>
        public static bool ShouldPush(float speedIn, float runSpeed, float runTime, float speedFraction, float minRunTime) =>
            speedIn > 0.1f && (speedIn >= runSpeed * speedFraction || runTime >= minRunTime);

        private bool TryRefreshPlayer()
        {
            PlayerLifecycle lifecycle = PlayerLifecycle.Instance;
            PlayerCharacter current = lifecycle != null ? lifecycle.Player : null;
            if (current != player)
            {
                player = current;
                playerBody = player != null ? player.GetComponent<Collider>() : null;
                EndContact();
            }

            return player != null && playerBody != null && playerBody.enabled && player.IsAlive;
        }

        private bool IsOverlapping() => BodyContact.IsOverlapping(bodyCollider, playerBody, minOverlap);

        /// <summary>
        /// The side the player is on, or, when she is dead centre, the side behind her
        /// facing (so a centred contact always blocks), else in front of this enemy.
        /// </summary>
        private float ResolveEntrySide(Vector3 axis)
        {
            float side = OffsetSide(axis);
            if (side != 0f)
                return side;

            float facing = PlayerFacingAlong(axis);
            if (facing != 0f)
                return -facing;

            return movement != null ? movement.FacingDirection : 1f;
        }

        /// <summary>Lane side (+1/-1) the player is on relative to this enemy; 0 at dead centre.</summary>
        private float OffsetSide(Vector3 axis)
        {
            float offset = Vector3.Dot(player.transform.position - transform.position, axis);
            return Mathf.Abs(offset) > 0.05f ? Mathf.Sign(offset) : 0f;
        }

        /// <summary>The player's facing along this enemy's axis (+1/-1); 0 when unknown.</summary>
        private float PlayerFacingAlong(Vector3 axis)
        {
            Character2D5Controller controller = player.Controller;
            if (controller == null)
                return 0f;

            float along = Vector3.Dot(controller.MovementAxis, axis) * controller.FacingSign;
            return Mathf.Abs(along) < 0.1f ? 0f : Mathf.Sign(along);
        }

        /// <summary>
        /// True when the player, on <paramref name="side"/>, faces this body (she is trying to
        /// get past it). Unknown facing counts as facing, so the body never leaks.
        /// </summary>
        private bool FacesBody(float side, Vector3 axis)
        {
            float facing = PlayerFacingAlong(axis);
            return facing == 0f || facing == -side;
        }

        private Collider FindBodyCollider()
        {
            Collider found = BodyContact.FindSolidCollider(this);
            if (found == null)
                Debug.LogWarning($"[{name}] EnemyContactDamage found no solid body collider on the root. Assign Body Collider.", this);
            return found;
        }
    }
}
