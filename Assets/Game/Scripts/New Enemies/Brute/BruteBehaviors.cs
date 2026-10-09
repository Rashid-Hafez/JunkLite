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
        [Tooltip("Point in the throw (0-1) where the player is released.")]
        [SerializeField][Range(0f, 1f)] private float throwReleaseNormalized = 0.5f;
        [Tooltip("Recovery after a missed grab.")]
        [SerializeField] private float missRecoveryTime = 0.6f;

        [Header("Hit")]
        [SerializeField] private Hitbox grabHitbox;
        [Tooltip("Follow point while held (a BoneFollower on the hand bone).")]
        [SerializeField] private Transform grabAnchor;
        [SerializeField] private Vector3 grabOffset = Vector3.zero;
        [SerializeField, Min(1f)] private float grabDamage = 5f;
        [SerializeField] private Vector2 throwForce = new Vector2(25f, 12f);
        [SerializeField] private float throwDamage = 20f;

        private GameObject owner;
        private Hitbox subscribedHitbox;
        private float readyTime;

        /// <summary>Raised when the grab hitbox catches a grabbable target.</summary>
        public event Action<IGrabbable> Caught;
        public event Action Completed;

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
        public void OnGrabComplete() => Completed?.Invoke();

        private void HandleHit(Collider other, Hitbox sourceHitbox)
        {
            if (!EnemyAttackTargetFilter.CanDamage(owner, other))
                return;

            IGrabbable grabbable = other.GetComponentInParent<IGrabbable>();
            if (grabbable == null || !grabbable.CanBeGrabbed)
                return;

            // Dodge only: an active dash or roll avoids the grab.
            PlayerCharacter player = other.GetComponentInParent<PlayerCharacter>();
            if (player != null && player.State != null && (player.State.IsDashing || player.State.IsRolling))
                return;

            // Unparryable contact damage. Post-hit i-frames (Invulnerable) still
            // get grabbed so a grab can follow a hit; shields and dead targets don't.
            DamageResult result = DamageReceiverUtility.Receive(other,
                new DamageRequest(grabDamage, owner, DamageType.Physical)
                    .WithHitReaction(HitReactionRequest.None)
                    .AsUnparryable());

            if (result.Outcome != DamageOutcome.Applied && result.Outcome != DamageOutcome.Invulnerable)
                return;

            sourceHitbox?.Deactivate();
            Caught?.Invoke(grabbable);
        }
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

        public static BrutePhaseSettings Default => new BrutePhaseSettings
        {
            healthThreshold = 0.5f,
            chargeTimeMultiplier = 0.6f,
            tauntMultiplier = 0.5f,
            cooldownMultiplier = 0.7f,
            dashSpeedMultiplier = 1.25f,
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
