using System;
using UnityEngine;

namespace junklite
{
    /// <summary>
    /// Brute grab tuning and hitbox handling. The grab is close-range, telegraphed
    /// by the attack warning, and cannot be parried (only dodged).
    /// Used by: BruteGrabState
    /// </summary>
    [Serializable]
    public sealed class BruteGrabBehavior
    {
        [Tooltip("Only grab when the player is within this axis distance.")]
        [SerializeField] private float grabRange = 2.2f;
        [SerializeField] private float cooldown = 6f;

        [Header("Timing")]
        [Tooltip("Warning sign time before the grab reaches out (dodge window).")]
        [SerializeField] private float warningTime = 0.6f;
        [Tooltip("How long the grab hitbox is live once the reach starts.")]
        [SerializeField] private float reachActiveTime = 0.2f;
        [Tooltip("How long the player is held before the throw.")]
        [SerializeField] private float holdTime = 0.8f;
        [Tooltip("Gameplay length of the throw animation.")]
        [SerializeField] private float throwDuration = 0.7f;
        [Tooltip("Point in the slam (0-1) where the player is released. 1 = when the slam animation ends.")]
        [SerializeField][Range(0f, 1f)] private float throwReleaseNormalized = 1f;
        [Tooltip("Recovery after a missed grab.")]
        [SerializeField] private float missRecoveryTime = 0.6f;

        [Header("Hit")]
        [SerializeField] private Hitbox grabHitbox;
        [Tooltip("Follow point while held (a BoneFollower on the hand bone).")]
        [SerializeField] private Transform grabAnchor;
        [SerializeField] private Vector3 grabOffset = Vector3.zero;
        [SerializeField, Min(1f)] private float grabDamage = 5f;
        [Tooltip("Push applied on release. Zero = slam: the player drops where the hand is.")]
        [SerializeField] private Vector2 throwForce = Vector2.zero;
        [SerializeField] private float throwDamage = 20f;

        [Header("Grab On Touch")]
        [Tooltip("Touching his body while he is idle or walking grabs her at once (no warning). A dash or roll still passes through.")]
        [SerializeField] private bool grabOnTouch = true;
        [Tooltip("Seconds after any grab ends before a touch can grab again. The normal grab keeps its own cooldown.")]
        [SerializeField, Min(0f)] private float touchCooldown = 1.5f;
        [Tooltip("How deep (metres) the bodies must overlap before a touch counts.")]
        [SerializeField, Min(0f)] private float touchMinOverlap = 0.05f;

        // A touch that caught nothing (dash, shield) waits this long before testing again.
        private const float TouchRetryDelay = 0.3f;

        private GameObject owner;
        private Hitbox subscribedHitbox;
        private float readyTime;
        private float touchReadyTime;
        private IGrabbable touchCatch;

        /// <summary>Raised when the grab hitbox catches a grabbable target.</summary>
        public event Action<IGrabbable> Caught;
        /// <summary>Raised when the grab ends. True if the player was caught and slammed.</summary>
        public event Action<bool> Completed;

        public float GrabRange => grabRange;
        public float WarningTime => warningTime;
        public float ReachActiveTime => reachActiveTime;
        public float HoldTime => holdTime;
        public float ThrowDuration => throwDuration;
        public float ThrowReleaseNormalized => throwReleaseNormalized;
        public float MissRecoveryTime => missRecoveryTime;
        public Hitbox GrabHitbox => grabHitbox;
        public Transform GrabAnchor => grabAnchor;
        public Vector3 GrabOffset => grabOffset;
        public Vector2 ThrowForce => throwForce;
        public float ThrowDamage => throwDamage;
        public bool IsReady => Time.time >= readyTime;
        public bool CanGrabOnTouch => grabOnTouch && Time.time >= touchReadyTime;
        public float TouchMinOverlap => touchMinOverlap;

        /// <summary>Safety timeout for the player side if the boss never releases.</summary>
        public float HoldTimeout => warningTime + reachActiveTime + holdTime + throwDuration + 2f;

        public void Initialize(GameObject damageOwner)
        {
            Dispose();
            owner = damageOwner;
            subscribedHitbox = grabHitbox;
            if (subscribedHitbox != null)
            {
                subscribedHitbox.OnHit += HandleHit;
                subscribedHitbox.Deactivate();
            }
        }

        public void Dispose()
        {
            if (subscribedHitbox != null)
                subscribedHitbox.OnHit -= HandleHit;

            subscribedHitbox = null;
            owner = null;
        }

        public void StartCooldown(float multiplier) => readyTime = Time.time + cooldown * multiplier;
        public void OnGrabComplete(bool caught) => Completed?.Invoke(caught);

        /// <summary>Called when the grab state ends (finished or interrupted).</summary>
        public void StartTouchCooldown() => touchReadyTime = Time.time + touchCooldown;

        /// <summary>
        /// Grab on touch: applies the grab damage now and keeps the catch for
        /// BruteGrabState, which then skips the warning and the reach.
        /// </summary>
        public bool TryCatchOnTouch(Collider target)
        {
            if (!TryCatch(target, out IGrabbable grabbable))
            {
                touchReadyTime = Time.time + TouchRetryDelay;
                return false;
            }

            touchCatch = grabbable;
            return true;
        }

        /// <summary>The target caught by touch, once; null for a normal grab.</summary>
        public IGrabbable TakeTouchCatch()
        {
            IGrabbable caught = touchCatch;
            touchCatch = null;
            return caught;
        }

        private void HandleHit(Collider other, Hitbox sourceHitbox)
        {
            if (!TryCatch(other, out IGrabbable grabbable))
                return;

            sourceHitbox?.Deactivate();
            Caught?.Invoke(grabbable);
        }

        private bool TryCatch(Collider other, out IGrabbable grabbable)
        {
            grabbable = null;
            if (!EnemyAttackTargetFilter.CanDamage(owner, other))
                return false;

            grabbable = other.GetComponentInParent<IGrabbable>();
            if (grabbable == null || !grabbable.CanBeGrabbed)
                return false;

            // Dodge only: an active dash or roll avoids the grab.
            PlayerCharacter player = other.GetComponentInParent<PlayerCharacter>();
            if (player != null && player.State != null && (player.State.IsDashing || player.State.IsRolling))
                return false;

            // Unparryable contact damage. Post-hit i-frames (Invulnerable) still
            // get grabbed so a grab can follow a hit; shields and dead targets don't.
            DamageResult result = DamageReceiverUtility.Receive(other,
                new DamageRequest(grabDamage, owner, DamageType.Physical)
                    .WithHitReaction(HitReactionRequest.None)
                    .AsUnparryable());

            return result.Outcome == DamageOutcome.Applied || result.Outcome == DamageOutcome.Invulnerable;
        }
    }

    /// <summary>
    /// The wait after each move: he stands idle facing the player before picking the
    /// next one. Bigger moves leave longer openings; every hit he takes cuts the wait
    /// short, so a greedy player gets caught by the next move.
    /// </summary>
    [Serializable]
    public struct BruteBreatherSettings
    {
        [Min(0f)] public float afterPunch;
        [Min(0f)] public float afterGrab;
        [Tooltip("After the grab's miss pose (Grab > Miss Recovery Time).")]
        [Min(0f)] public float afterMissedGrab;
        [Min(0f)] public float afterBlast;
        [Min(0f)] public float afterDash;
        [Min(0f)] public float afterDashIntoWall;
        [Tooltip("After standing up from a parry stun or a knockdown.")]
        [Min(0f)] public float afterGetUp;
        [Tooltip("Seconds each hit takes off the remaining wait.")]
        [Min(0f)] public float cutPerHit;
        [Tooltip("Hits never cut the wait below this many seconds.")]
        [Min(0f)] public float minRemaining;
        [Tooltip("Seconds after a wait ends before touching him grabs again, so he never grabs without a warning.")]
        [Min(0f)] public float touchGrabDelay;

        public static BruteBreatherSettings Default => new BruteBreatherSettings
        {
            afterPunch = 1f,
            afterGrab = 1.2f,
            afterMissedGrab = 1f,
            afterBlast = 1.5f,
            afterDash = 2f,
            afterDashIntoWall = 2.5f,
            afterGetUp = 1f,
            cutPerHit = 0.3f,
            minRemaining = 0.3f,
            touchGrabDelay = 0.6f,
        };

        /// <summary>Remaining wait after one hit: cut, but never below the floor (or raised to it).</summary>
        public static float Shorten(float remaining, float cut, float floor) =>
            Mathf.Max(remaining - cut, Mathf.Min(remaining, floor));
    }

    /// <summary>
    /// Brute phase-two tuning. Values are multipliers applied once health drops
    /// to the threshold.
    /// </summary>
    [Serializable]
    public struct BrutePhaseSettings
    {
        [Range(0f, 1f)] public float healthThreshold;
        public float chargeTimeMultiplier;
        public float tauntMultiplier;
        public float cooldownMultiplier;
        public float dashSpeedMultiplier;
        [Tooltip("The faster taunt never goes below this (seconds), so the punch stays readable.")]
        [Min(0f)] public float minTauntDuration;
        [Tooltip("Multiplier on every wait between moves.")]
        public float breatherMultiplier;
        [Tooltip("Seconds each hit takes off the remaining wait (replaces Cut Per Hit).")]
        [Min(0f)] public float breatherCutPerHit;

        public static BrutePhaseSettings Default => new BrutePhaseSettings
        {
            healthThreshold = 0.5f,
            chargeTimeMultiplier = 0.6f,
            tauntMultiplier = 0.5f,
            cooldownMultiplier = 0.7f,
            dashSpeedMultiplier = 1.25f,
            minTauntDuration = 0.45f,
            breatherMultiplier = 0.75f,
            breatherCutPerHit = 0.4f,
        };
    }

    /// <summary>
    /// One weighted voluntary move. Cooldowns are runtime state and stay here,
    /// on the brain's serialized instance, never on a ScriptableObject.
    /// </summary>
    [Serializable]
    public sealed class BruteMoveWeight
    {
        [Min(0f)] public float weight = 1f;
        [Min(0f)] public float cooldown = 3f;

        [NonSerialized] private float readyTime;

        public bool IsReady => weight > 0f && Time.time >= readyTime;
        public void StartCooldown(float multiplier) => readyTime = Time.time + cooldown * multiplier;
    }
}
