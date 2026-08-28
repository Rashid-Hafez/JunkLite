using UnityEngine;

namespace junklite
{
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

        public bool HasKnockback => KnockbackForce.sqrMagnitude > 0f;
        public bool HasHitstun => UseReceiverDefaultHitstun || HitstunDuration > 0f;
        public bool HasAnyReaction => HasKnockback || HasHitstun || InterruptsActions;

        public HitReactionRequest(
            Vector2 knockbackForce,
            float hitstunDuration = 0f,
            bool useReceiverDefaultHitstun = false,
            bool interruptsActions = true)
        {
            KnockbackForce = knockbackForce;
            HitstunDuration = Mathf.Max(0f, hitstunDuration);
            UseReceiverDefaultHitstun = useReceiverDefaultHitstun;
            InterruptsActions = interruptsActions;
        }

        public float ResolveHitstunDuration(float receiverDefault)
        {
            return UseReceiverDefaultHitstun
                ? Mathf.Max(0f, receiverDefault)
                : HitstunDuration;
        }

        public static HitReactionRequest DefaultHit(Vector2 knockbackForce)
        {
            return new HitReactionRequest(
                knockbackForce,
                useReceiverDefaultHitstun: true,
                interruptsActions: true);
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
            HitReactionRequest? hitReaction = null)
        {
            Amount = amount;
            Source = source;
            Type = type;
            IsTickDamage = isTickDamage;
            BypassesDefenses = bypassesDefenses;
            BypassesMitigation = bypassesMitigation;
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
                bypassesMitigation: true);
        }

        public DamageRequest WithAmount(float amount)
        {
            var copy = this;
            copy.Amount = amount;
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
