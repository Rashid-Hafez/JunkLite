using UnityEngine;
using UnityEngine.Serialization;

namespace junklite
{
    /// <summary>
    /// Body contact: the player can't walk through this enemy. Every physics step her body
    /// overlaps its body she is shoved back to the side she came from, instantly (lane speed
    /// replaced, not added). The shove does not depend on damage, so i-frames and the damage
    /// cooldown never open a gap to slip through. Damage lands at most once per cooldown,
    /// through the one damage path (i-frames, shields and death rules apply as usual).
    /// Player and enemy bodies do not collide (layer matrix), so without this she passes freely.
    /// Only a player facing the body is blocked: one that backs into it (e.g. shoved there by
    /// another enemy) passes through, so two flanking enemies can't ping-pong her.
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
        [Tooltip("Seconds the player loses control on each shove so her input can't cancel it.")]
        [SerializeField, Min(0f)] private float hitstun = 0.2f;

        [Header("Damage")]
        [Tooltip("Damage per bump. 0 = shove only, no damage.")]
        [SerializeField, Min(0f)] private float damage = 5f;
        [Tooltip("Shortest time between two separate bumps' damage, so one bump never counts twice.")]
        [SerializeField, Min(0.05f)] private float minBumpInterval = 0.25f;
        [Tooltip("Seconds between hits while she stays pressed against the body without separating (e.g. cornered against a wall).")]
        [FormerlySerializedAs("cooldown")]
        [SerializeField, Min(0.05f)] private float repeatInterval = 0.6f;
        [Tooltip("Off = a parry does not catch contact hits (they are not attacks).")]
        [SerializeField] private bool parryable = false;

        [Header("Contact")]
        [Tooltip("How deep (metres) the bodies must overlap before it counts. Avoids triggering on a brush of the edges.")]
        [SerializeField, Min(0f)] private float minOverlap = 0.02f;
        [Tooltip("Solid body collider. Empty = the first non-trigger collider on this object.")]
        [SerializeField] private Collider bodyCollider;

        [Header("Exceptions")]
        [Tooltip("A dashing or rolling player passes through (no shove, no damage).")]
        [SerializeField] private bool dashPassesThrough = false;
        [Tooltip("No contact while this enemy is in the air (vault, leap, launch).")]
        [SerializeField] private bool groundedOnly = true;

        private EnemyCharacter enemy;
        private EnemyMovement movement;

        private PlayerCharacter player;
        private Collider playerBody;

        private float lastDamageTime = float.NegativeInfinity;
        // Lane side (+1/-1, relative to this enemy) the player was on when the overlap began; 0 = not overlapping.
        private float entrySide;
        // She entered with her back to this body (e.g. shoved into it by another enemy): no shove,
        // no damage, until she turns to face it or the overlap ends. Stops flanking enemies ping-ponging her.
        private bool passThrough;
        // This overlap already dealt damage; further hits wait for repeatInterval instead of minBumpInterval.
        private bool damagedThisContact;

        private void Awake()
        {
            enemy = GetComponent<EnemyCharacter>();
            movement = GetComponent<EnemyMovement>();
            if (bodyCollider == null)
                bodyCollider = FindBodyCollider();
        }

        private void OnDisable()
        {
            EndContact();
        }

        private void FixedUpdate()
        {
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

            if (passThrough || !CanShove())
                return;

            if (TryDamage() == DamageOutcome.Parried)
                return;

            player.ApplyBodyShove(axis * entrySide, knockback, hitstun, gameObject);
        }

        private void EndContact()
        {
            entrySide = 0f;
            passThrough = false;
            damagedThisContact = false;
        }

        /// <summary>
        /// Every new bump deals damage at once; staying pressed in repeats it every repeatInterval.
        /// The shove follows whatever this returns, except a parry.
        /// </summary>
        private DamageOutcome TryDamage()
        {
            float wait = damagedThisContact ? repeatInterval : minBumpInterval;
            if (damage <= 0f || Time.time < lastDamageTime + wait || IsReeling())
                return DamageOutcome.Invalid;

            // No hit reaction here: the shove is the reaction, applied every contact. No post-hit
            // i-frames either, so a quick second bump isn't silently ignored.
            var request = new DamageRequest(damage, gameObject, DamageType.Physical, hitReaction: HitReactionRequest.None)
                .WithoutHitInvulnerability();
            if (!parryable)
                request = request.AsUnparryable();

            DamageResult result = DamageReceiverUtility.Receive(playerBody, request);

            // Other i-frames (a punch just landed, parry, dodge mods) reject it and it retries
            // next step; a hit, a shield block or a parry counts as this contact's damage, so a
            // shield isn't drained every physics step.
            if (result.Outcome == DamageOutcome.Applied || result.Outcome == DamageOutcome.Blocked
                || result.Outcome == DamageOutcome.Parried)
            {
                lastDamageTime = Time.time;
                damagedThisContact = true;
            }

            return result.Outcome;
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
            if (dashPassesThrough && playerState != null && (playerState.IsDashing || playerState.IsRolling))
                return false;

            return true;
        }

        /// <summary>Stunned or parried enemies still block, but don't hurt the player for standing close.</summary>
        private bool IsReeling()
        {
            IState state = enemy.StateMachine != null ? enemy.StateMachine.CurrentState : null;
            return state is StunnedState || state is ParriedState;
        }

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
