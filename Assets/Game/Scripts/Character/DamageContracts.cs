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
    /// Describes one attempt to damage a receiver. Runtime receivers may reduce
    /// Amount while resolving shields, but the returned result retains the
    /// originally requested amount.
    /// </summary>
    public struct DamageRequest
    {
        public float Amount;
        public GameObject Source;
        public DamageType Type;
        public Vector2 KnockbackForce;
        public bool IsTickDamage;
        public bool BypassesDefenses;
        public bool BypassesMitigation;

        public DamageRequest(
            float amount,
            GameObject source = null,
            DamageType type = DamageType.Physical,
            Vector2 knockback = default,
            bool isTickDamage = false,
            bool bypassesDefenses = false,
            bool bypassesMitigation = false)
        {
            Amount = amount;
            Source = source;
            Type = type;
            KnockbackForce = knockback;
            IsTickDamage = isTickDamage;
            BypassesDefenses = bypassesDefenses;
            BypassesMitigation = bypassesMitigation;
        }

        public static DamageRequest FromLegacy(DamageInfo info)
        {
            return new DamageRequest(
                info.Amount,
                info.Source,
                info.Type,
                info.KnockbackForce,
                info.IsTickDamage);
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

        public DamageInfo ToLegacy()
        {
            return new DamageInfo(Amount, Source, Type, KnockbackForce, IsTickDamage);
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
    /// Temporary bridge used while enemies and damage producers still implement
    /// the legacy IDamageable/DamageInfo API.
    /// </summary>
    public static class DamageReceiverUtility
    {
        public static bool IsAlive(Component target)
        {
            if (target == null) return false;

            var receiver = target.GetComponent<IDamageReceiver>()
                         ?? target.GetComponentInParent<IDamageReceiver>();
            if (receiver != null) return receiver.IsAlive;

            var legacy = target.GetComponent<IDamageable>()
                      ?? target.GetComponentInParent<IDamageable>();
            return legacy != null && legacy.IsAlive;
        }

        public static DamageResult Receive(Component target, DamageRequest request)
        {
            if (target == null)
                return DamageResult.Rejected(DamageOutcome.Invalid, request.Amount);

            var receiver = target.GetComponent<IDamageReceiver>()
                         ?? target.GetComponentInParent<IDamageReceiver>();
            if (receiver != null)
                return receiver.ReceiveDamage(request);

            var legacy = target.GetComponent<IDamageable>()
                      ?? target.GetComponentInParent<IDamageable>();
            if (legacy == null)
                return DamageResult.Rejected(DamageOutcome.Invalid, request.Amount);
            if (!legacy.IsAlive)
                return DamageResult.Rejected(DamageOutcome.Dead, request.Amount);

            bool applied = legacy.TakeDamage(request.ToLegacy());
            return applied
                ? DamageResult.Applied(request.Amount, request.Amount)
                : DamageResult.Rejected(DamageOutcome.Blocked, request.Amount);
        }
    }
}
