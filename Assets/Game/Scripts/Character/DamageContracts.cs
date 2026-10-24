using UnityEngine;

namespace junklite
{
    // Fixed is for forced damage or a proc derived from an already-scaled hit.
    public enum DamageScaling { SourceStats, Fixed }
    public enum DamageOutcome
    {
        Applied,
        Blocked,
        Parried,
        Invulnerable,
        FriendlyFire,
        Dead,
        Invalid
    }

    /// <summary>What an accepted hit asks the receiver's body to do.</summary>
    public enum HitReactionKind
    {
        /// <summary>Knockback plus hitstun. The default for every hit.</summary>
        Flinch,
        /// <summary>
        /// Lift the receiver off the ground. <see cref="HitReactionRequest.KnockbackForce"/>
        /// is then the launch velocity: x along the lane away from the source, y up.
        /// Receivers that cannot be launched treat it as a flinch.
        /// </summary>
        Launch,
        /// <summary>
        /// Drive an airborne (launched) receiver down: <see cref="HitReactionRequest.KnockbackForce"/>
        /// x is the sideways speed away from the source, y the downward speed. On a grounded
        /// receiver it is a flinch with the sideways push only (it never lifts).
        /// </summary>
        Spike
    }

    /// <summary>
    /// Optional physical/behavioral response to an accepted hit. Damage, status
    /// effects and hit reactions remain independently callable systems.
    /// </summary>
    public struct HitReactionRequest
    {
        public Vector2 KnockbackForce;
        public float HitstunDuration;
        public bool UseReceiverDefaultHitstun;
        public bool InterruptsActions;
        public HitReactionKind Kind;
        /// <summary>
        /// World direction to push the receiver along (the attacker's swing). Zero =
        /// away from the source's position, which flips when the bodies overlap and the
        /// attacker has passed the receiver's centre.
        /// </summary>
        public Vector3 PushDirection;

        public bool HasKnockback => KnockbackForce.sqrMagnitude > 0f;
        public bool IsLaunch => Kind == HitReactionKind.Launch;
        public bool IsSpike => Kind == HitReactionKind.Spike;
        public bool HasHitstun => UseReceiverDefaultHitstun || HitstunDuration > 0f;
        public bool HasAnyReaction => HasKnockback || HasHitstun || InterruptsActions;

        public HitReactionRequest(
            Vector2 knockbackForce,
            float hitstunDuration = 0f,
            bool useReceiverDefaultHitstun = false,
            bool interruptsActions = true,
            HitReactionKind kind = HitReactionKind.Flinch)
        {
            KnockbackForce = knockbackForce;
            HitstunDuration = Mathf.Max(0f, hitstunDuration);
            UseReceiverDefaultHitstun = useReceiverDefaultHitstun;
            InterruptsActions = interruptsActions;
            Kind = kind;
            PushDirection = Vector3.zero;
        }

        public float ResolveHitstunDuration(float receiverDefault)
        {
            return UseReceiverDefaultHitstun
                ? Mathf.Max(0f, receiverDefault)
                : HitstunDuration;
        }

        public static HitReactionRequest DefaultHit(
            Vector2 knockbackForce,
            HitReactionKind kind = HitReactionKind.Flinch)
        {
            return new HitReactionRequest(
                knockbackForce,
                useReceiverDefaultHitstun: true,
                interruptsActions: true,
                kind: kind);
        }

        public static HitReactionRequest None =>
            new HitReactionRequest(Vector2.zero, interruptsActions: false);
    }

    /// <summary>
    /// Describes one attempt to damage a receiver. Runtime receivers may reduce
    /// Amount while resolving shields, but the returned result retains the
    /// originally requested amount.
    /// </summary>
    public struct DamageRequest
    {
        public float Amount;
        public GameObject Source;
        public DamageType Type;
        public HitReactionRequest HitReaction;
        public bool IsTickDamage;
        public bool BypassesDefenses;
        public bool BypassesMitigation;
        public DamageScaling Scaling;
        /// <summary>Parry cannot catch this hit; i-frames and shields still apply.</summary>
        public bool Unparryable;

        public Vector2 KnockbackForce
        {
            get => HitReaction.KnockbackForce;
            set
            {
                HitReactionRequest reaction = HitReaction;
                reaction.KnockbackForce = value;
                HitReaction = reaction;
            }
        }

        public DamageRequest(
            float amount,
            GameObject source = null,
            DamageType type = DamageType.Physical,
            Vector2 knockback = default,
            bool isTickDamage = false,
            bool bypassesDefenses = false,
            bool bypassesMitigation = false,
            HitReactionRequest? hitReaction = null,
            DamageScaling scaling = DamageScaling.SourceStats)
        {
            Amount = amount;
            Source = source;
            Type = type;
            IsTickDamage = isTickDamage;
            BypassesDefenses = bypassesDefenses;
            BypassesMitigation = bypassesMitigation;
            Scaling = scaling;
            Unparryable = false;
            HitReaction = hitReaction ?? (isTickDamage
                ? HitReactionRequest.None
                : HitReactionRequest.DefaultHit(knockback));
        }

        public static DamageRequest Forced(
            float amount,
            GameObject source = null,
            DamageType type = DamageType.Physical)
        {
            return new DamageRequest(
                amount,
                source,
                type,
                bypassesDefenses: true,
                bypassesMitigation: true,
                scaling: DamageScaling.Fixed);
        }

        public DamageRequest WithAmount(float amount)
        {
            var copy = this;
            copy.Amount = amount;
            return copy;
        }

        public DamageRequest AsUnparryable()
        {
            var copy = this;
            copy.Unparryable = true;
            return copy;
        }

        public DamageRequest WithHitReaction(HitReactionRequest reaction)
        {
            var copy = this;
            copy.HitReaction = reaction;
            return copy;
        }

    }

    public struct DamageResult
    {
        public DamageOutcome Outcome { get; }
        public float RequestedDamage { get; }
        public float AppliedDamage { get; }
        public bool WasApplied => Outcome == DamageOutcome.Applied && AppliedDamage > 0f;

        public DamageResult(DamageOutcome outcome, float requestedDamage, float appliedDamage)
        {
            Outcome = outcome;
            RequestedDamage = requestedDamage;
            AppliedDamage = appliedDamage;
        }

        public DamageResult WithRequestedDamage(float requestedDamage)
        {
            return new DamageResult(Outcome, requestedDamage, AppliedDamage);
        }

        public static DamageResult Rejected(DamageOutcome outcome, float requestedDamage)
        {
            return new DamageResult(outcome, requestedDamage, 0f);
        }

        public static DamageResult Applied(float requestedDamage, float appliedDamage)
        {
            return new DamageResult(DamageOutcome.Applied, requestedDamage, appliedDamage);
        }
    }

    public interface IDamageReceiver
    {
        bool IsAlive { get; }
        DamageResult ReceiveDamage(DamageRequest request);
    }

    /// <summary>
    /// Resolves the authoritative damage receiver on a collider/component hierarchy.
    /// </summary>
    public static class DamageReceiverUtility
    {
        public static bool TryGetReceiver(Component target, out IDamageReceiver receiver)
        {
            receiver = null;
            if (target == null) return false;

            receiver = target.GetComponent<IDamageReceiver>()
                    ?? target.GetComponentInParent<IDamageReceiver>();
            return receiver != null;
        }

        public static bool IsAlive(Component target)
        {
            return TryGetReceiver(target, out var receiver) && receiver.IsAlive;
        }

        public static DamageResult Receive(Component target, DamageRequest request)
        {
            if (target == null)
                return DamageResult.Rejected(DamageOutcome.Invalid, request.Amount);

            if (TryGetReceiver(target, out var receiver))
                return receiver.ReceiveDamage(request);

            return DamageResult.Rejected(DamageOutcome.Invalid, request.Amount);
        }
    }
}
