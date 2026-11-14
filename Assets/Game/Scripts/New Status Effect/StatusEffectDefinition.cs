using UnityEngine;

namespace junklite
{
    /// <summary>
    /// Shared, data-driven status configuration. Runtime timers and sources never
    /// live on this asset; they belong to StatusEffectInstance.
    /// </summary>
    [CreateAssetMenu(fileName = "Status Effect", menuName = "Junklite/Combat/Status Effect")]
    public sealed class StatusEffectDefinition : ScriptableObject
    {
        [SerializeField] private StatusEffectType type;
        [SerializeField] private StatusEffectTags tags = StatusEffectTags.Debuff;
        [SerializeField] private StatusStackingPolicy stackingPolicy = StatusStackingPolicy.RefreshDuration;
        [SerializeField] private StatusActionBlock blockedActions;

        [Header("Lifetime and stacking")]
        [SerializeField, Min(0f)] private float duration = 1f;
        [SerializeField, Min(0f)] private float strength = 1f;
        [SerializeField, Min(1)] private int maxStacks = 1;

        [Header("Movement")]
        [SerializeField, Min(0f)] private float moveSpeedMultiplier = 1f;

        [Header("Periodic damage")]
        [SerializeField, Min(0f)] private float damagePerTick;
        [SerializeField, Min(0f)] private float tickInterval;
        [SerializeField] private DamageType damageType = DamageType.Physical;

        public StatusEffectType Type => type;

        public StatusEffectSpec BuildSpec()
        {
            return new StatusEffectSpec(
                type,
                duration,
                tags,
                stackingPolicy,
                blockedActions,
                strength,
                maxStacks,
                moveSpeedMultiplier,
                damagePerTick,
                tickInterval,
                damageType);
        }

        public StatusEffectApplication CreateApplication(
            GameObject source = null,
            float durationOverride = -1f,
            float strengthOverride = -1f)
        {
            return new StatusEffectApplication(this, source, durationOverride, strengthOverride);
        }
    }
}
