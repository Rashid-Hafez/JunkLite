using System;
using UnityEngine;

namespace junklite
{
    [Flags]
    public enum StatusEffectTags
    {
        None = 0,
        Debuff = 1 << 0,
        Buff = 1 << 1,
        DamageOverTime = 1 << 2,
        MovementModifier = 1 << 3,
        CrowdControl = 1 << 4,
        HitReaction = 1 << 5
    }

    [Flags]
    public enum StatusActionBlock
    {
        None = 0,
        Move = 1 << 0,
        Jump = 1 << 1,
        Attack = 1 << 2,
        Dash = 1 << 3,
        Roll = 1 << 4,
        Parry = 1 << 5,
        All = Move | Jump | Attack | Dash | Roll | Parry
    }

    public enum StatusStackingPolicy
    {
        RefreshDuration,
        ExtendDuration,
        ReplaceIfStronger,
        StackIntensity,
        IndependentPerSource
    }

    // Existing numeric values are explicit so current prefab data remains valid.
    public enum StatusEffectType
    {
        None = 0,
        Burn = 1,
        Poison = 2,
        Bleed = 3,
        Electric = 4,
        Hitstun = 5,
        Stun = 6,
        Slow = 7,
        Freeze = 8
    }

    /// <summary>
    /// Immutable configuration copied into a runtime status instance. It can come
    /// from a StatusEffectDefinition asset or from a small code-created effect.
    /// </summary>
    [Serializable]
    public struct StatusEffectSpec
    {
        public StatusEffectType Type { get; }
        public StatusEffectTags Tags { get; }
        public StatusStackingPolicy StackingPolicy { get; }
        public StatusActionBlock BlockedActions { get; }
        public float Duration { get; }
        public float Strength { get; }
        public int MaxStacks { get; }
        public float MoveSpeedMultiplier { get; }
        public float DamagePerTick { get; }
        public float TickInterval { get; }
        public DamageType DamageType { get; }

        public StatusEffectSpec(
            StatusEffectType type,
            float duration,
            StatusEffectTags tags = StatusEffectTags.Debuff,
            StatusStackingPolicy stackingPolicy = StatusStackingPolicy.RefreshDuration,
            StatusActionBlock blockedActions = StatusActionBlock.None,
            float strength = 1f,
            int maxStacks = 1,
            float moveSpeedMultiplier = 1f,
            float damagePerTick = 0f,
            float tickInterval = 0f,
            DamageType damageType = DamageType.Physical)
        {
            Type = type;
            Tags = tags;
            StackingPolicy = stackingPolicy;
            BlockedActions = blockedActions;
            Duration = Mathf.Max(0f, duration);
            Strength = Mathf.Max(0f, strength);
            MaxStacks = Mathf.Max(1, maxStacks);
            MoveSpeedMultiplier = Mathf.Max(0f, moveSpeedMultiplier);
            DamagePerTick = Mathf.Max(0f, damagePerTick);
            TickInterval = Mathf.Max(0f, tickInterval);
            DamageType = damageType;
        }

        public StatusEffectSpec WithDamagePerTick(float amount)
        {
            return new StatusEffectSpec(
                Type,
                Duration,
                Tags,
                StackingPolicy,
                BlockedActions,
                Strength,
                MaxStacks,
                MoveSpeedMultiplier,
                amount,
                TickInterval,
                DamageType);
        }
    }

    /// <summary>
    /// Describes one request to apply a status. This is intentionally independent
    /// of damage so hazards and scripted events can apply statuses directly.
    /// </summary>
    public struct StatusEffectApplication
    {
        public StatusEffectDefinition Definition { get; }
        public StatusEffectSpec Spec { get; }
        public GameObject Source { get; }
        public float Duration { get; }
        public float Strength { get; }

        public bool IsValid => Spec.Type != StatusEffectType.None && Duration > 0f;

        public StatusEffectApplication(
            StatusEffectDefinition definition,
            GameObject source = null,
            float durationOverride = -1f,
            float strengthOverride = -1f)
            : this(
                definition != null ? definition.BuildSpec() : default,
                source,
                durationOverride,
                strengthOverride,
                definition)
        {
        }

        public StatusEffectApplication(
            StatusEffectSpec spec,
            GameObject source = null,
            float durationOverride = -1f,
            float strengthOverride = -1f,
            StatusEffectDefinition definition = null)
        {
            Definition = definition;
            Spec = spec;
            Source = source;
            Duration = durationOverride >= 0f ? durationOverride : spec.Duration;
            Strength = strengthOverride >= 0f ? strengthOverride : spec.Strength;
        }

        public static StatusEffectApplication Hitstun(float duration, GameObject source = null)
        {
            return new StatusEffectApplication(
                new StatusEffectSpec(
                    StatusEffectType.Hitstun,
                    duration,
                    StatusEffectTags.Debuff | StatusEffectTags.CrowdControl | StatusEffectTags.HitReaction,
                    StatusStackingPolicy.RefreshDuration,
                    StatusActionBlock.All),
                source);
        }

        public static StatusEffectApplication Stun(float duration, GameObject source = null)
        {
            return new StatusEffectApplication(
                new StatusEffectSpec(
                    StatusEffectType.Stun,
                    duration,
                    StatusEffectTags.Debuff | StatusEffectTags.CrowdControl,
                    StatusStackingPolicy.RefreshDuration,
                    StatusActionBlock.All),
                source);
        }

        public static StatusEffectApplication Slow(
            float duration,
            float moveSpeedMultiplier,
            GameObject source = null)
        {
            float clampedMultiplier = Mathf.Clamp01(moveSpeedMultiplier);
            return new StatusEffectApplication(
                new StatusEffectSpec(
                    StatusEffectType.Slow,
                    duration,
                    StatusEffectTags.Debuff | StatusEffectTags.MovementModifier,
                    StatusStackingPolicy.IndependentPerSource,
                    strength: 1f - clampedMultiplier,
                    moveSpeedMultiplier: clampedMultiplier),
                source);
        }

        public static StatusEffectApplication Freeze(float duration, GameObject source = null)
        {
            return new StatusEffectApplication(
                new StatusEffectSpec(
                    StatusEffectType.Freeze,
                    duration,
                    StatusEffectTags.Debuff | StatusEffectTags.CrowdControl | StatusEffectTags.MovementModifier,
                    StatusStackingPolicy.RefreshDuration,
                    StatusActionBlock.All,
                    moveSpeedMultiplier: 0f),
                source);
        }

        public static StatusEffectApplication DamageOverTime(
            StatusEffectType type,
            float damagePerTick,
            float tickInterval,
            float duration,
            DamageType damageType,
            GameObject source = null,
            StatusStackingPolicy stackingPolicy = StatusStackingPolicy.RefreshDuration,
            int maxStacks = 1)
        {
            return new StatusEffectApplication(
                new StatusEffectSpec(
                    type,
                    duration,
                    StatusEffectTags.Debuff | StatusEffectTags.DamageOverTime,
                    stackingPolicy,
                    strength: damagePerTick,
                    maxStacks: maxStacks,
                    damagePerTick: damagePerTick,
                    tickInterval: tickInterval,
                    damageType: damageType),
                source);
        }
    }

    public struct StatusEffectSnapshot
    {
        public StatusEffectTags Tags { get; }
        public StatusActionBlock BlockedActions { get; }
        public float MoveSpeedMultiplier { get; }

        public bool IsCrowdControlled => (Tags & StatusEffectTags.CrowdControl) != 0;

        public StatusEffectSnapshot(
            StatusEffectTags tags,
            StatusActionBlock blockedActions,
            float moveSpeedMultiplier)
        {
            Tags = tags;
            BlockedActions = blockedActions;
            MoveSpeedMultiplier = Mathf.Max(0f, moveSpeedMultiplier);
        }

        public static StatusEffectSnapshot Clear =>
            new StatusEffectSnapshot(StatusEffectTags.None, StatusActionBlock.None, 1f);
    }

    /// <summary>
    /// Implemented by actors that translate aggregate status data into their own
    /// movement/state architecture. The status controller never depends on a
    /// concrete player or enemy class.
    /// </summary>
    public interface IStatusEffectTarget : IDamageReceiver
    {
        void ApplyStatusEffectSnapshot(StatusEffectSnapshot snapshot);
    }

    /// <summary>
    /// Runtime-only state for one active status effect.
    /// </summary>
    public sealed class StatusEffectInstance
    {
        private StatusEffectSpec spec;

        public StatusEffectDefinition Definition { get; private set; }
        public StatusEffectType Type => spec.Type;
        public StatusEffectTags Tags => spec.Tags;
        public StatusStackingPolicy StackingPolicy => spec.StackingPolicy;
        public StatusActionBlock BlockedActions => spec.BlockedActions;
        public float Duration => spec.Duration;
        public float Strength { get; private set; }
        public int StackCount { get; private set; }
        public int MaxStacks => spec.MaxStacks;
        public float MoveSpeedMultiplier => spec.MoveSpeedMultiplier;
        public float DamagePerTick => spec.DamagePerTick * StackCount;
        public float TickInterval => spec.TickInterval;
        public DamageType DamageType => spec.DamageType;
        public GameObject Source { get; private set; }
        public float ExpiresAt { get; private set; }
        public float NextTickAt { get; private set; }
        public float RemainingDuration => Mathf.Max(0f, ExpiresAt - Time.time);
        public bool IsExpired => Time.time >= ExpiresAt;
        public bool CanTick => DamagePerTick > 0f && TickInterval > 0f;

        public StatusEffectInstance(StatusEffectApplication application, float now)
        {
            Definition = application.Definition;
            spec = application.Spec;
            Source = application.Source;
            Strength = application.Strength;
            StackCount = 1;
            ExpiresAt = now + application.Duration;
            NextTickAt = spec.TickInterval > 0f ? now + spec.TickInterval : float.PositiveInfinity;
        }

        // Compatibility constructor for older callers while they migrate to applications.
        public StatusEffectInstance(
            StatusEffectType type,
            float damagePerTick,
            float tickInterval,
            float duration,
            DamageType damageType = DamageType.Fire,
            GameObject source = null,
            float speedModifier = 1f)
            : this(
                new StatusEffectApplication(
                    new StatusEffectSpec(
                        type,
                        duration,
                        StatusEffectTags.Debuff |
                        (damagePerTick > 0f ? StatusEffectTags.DamageOverTime : StatusEffectTags.None) |
                        (speedModifier != 1f ? StatusEffectTags.MovementModifier : StatusEffectTags.None),
                        StatusStackingPolicy.RefreshDuration,
                        strength: Mathf.Max(damagePerTick, 1f - speedModifier),
                        moveSpeedMultiplier: speedModifier,
                        damagePerTick: damagePerTick,
                        tickInterval: tickInterval,
                        damageType: damageType),
                    source),
                Time.time)
        {
        }

        internal bool Matches(StatusEffectApplication application)
        {
            bool sameIdentity = Definition != null || application.Definition != null
                ? Definition == application.Definition
                : Type == application.Spec.Type;

            if (!sameIdentity)
                return false;

            StatusStackingPolicy policy = application.Spec.StackingPolicy;
            return policy != StatusStackingPolicy.IndependentPerSource || Source == application.Source;
        }

        internal bool Merge(StatusEffectApplication application, float now)
        {
            bool aggregatesChanged = false;
            float requestedExpiry = now + application.Duration;

            switch (application.Spec.StackingPolicy)
            {
                case StatusStackingPolicy.ExtendDuration:
                    ExpiresAt = Mathf.Max(ExpiresAt, now) + application.Duration;
                    Source = application.Source;
                    break;

                case StatusStackingPolicy.ReplaceIfStronger:
                    if (application.Strength > Strength)
                    {
                        ReplaceConfiguration(application, now);
                        aggregatesChanged = true;
                    }
                    ExpiresAt = Mathf.Max(ExpiresAt, requestedExpiry);
                    break;

                case StatusStackingPolicy.StackIntensity:
                    int previousStacks = StackCount;
                    StackCount = Mathf.Min(StackCount + 1, application.Spec.MaxStacks);
                    ExpiresAt = Mathf.Max(ExpiresAt, requestedExpiry);
                    Source = application.Source;
                    aggregatesChanged = previousStacks != StackCount;
                    break;

                case StatusStackingPolicy.IndependentPerSource:
                case StatusStackingPolicy.RefreshDuration:
                default:
                    ExpiresAt = Mathf.Max(ExpiresAt, requestedExpiry);
                    if (application.Strength > Strength)
                    {
                        ReplaceConfiguration(application, now);
                        aggregatesChanged = true;
                    }
                    else
                    {
                        Source = application.Source;
                    }
                    break;
            }

            return aggregatesChanged;
        }

        internal void AdvanceTick()
        {
            NextTickAt += TickInterval;
        }

        internal StatusEffectApplication ToApplication()
        {
            return new StatusEffectApplication(spec, Source, RemainingDuration, Strength, Definition);
        }

        private void ReplaceConfiguration(StatusEffectApplication application, float now)
        {
            Definition = application.Definition;
            spec = application.Spec;
            Source = application.Source;
            Strength = application.Strength;
            StackCount = 1;
            NextTickAt = spec.TickInterval > 0f ? now + spec.TickInterval : float.PositiveInfinity;
        }
    }
}
