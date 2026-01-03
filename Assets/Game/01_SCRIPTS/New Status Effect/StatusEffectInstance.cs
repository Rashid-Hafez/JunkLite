using UnityEngine;

namespace junklite
{
    /// <summary>
    /// Runtime instance of a status effect.
    /// Created by mods, managed by StatusEffectHandler.
    /// </summary>
    public class StatusEffectInstance
    {
        // Config (set once on creation)
        public StatusEffectType Type { get; private set; }
        public float DamagePerTick { get; private set; }
        public float TickInterval { get; private set; }
        public float Duration { get; private set; }
        public DamageType DamageType { get; private set; }
        public GameObject Source { get; private set; }

        // Optional modifiers
        public float SpeedModifier { get; private set; }

        // Runtime state (managed by handler)
        public float RemainingDuration { get; set; }
        public float TickTimer { get; set; }

        public StatusEffectInstance(
            StatusEffectType type,
            float damagePerTick,
            float tickInterval,
            float duration,
            DamageType damageType = DamageType.Fire,
            GameObject source = null,
            float speedModifier = 1f)
        {
            Type = type;
            DamagePerTick = damagePerTick;
            TickInterval = tickInterval;
            Duration = duration;
            DamageType = damageType;
            Source = source;
            SpeedModifier = speedModifier;

            // Initialize runtime state
            RemainingDuration = duration;
            TickTimer = 0f;
        }

        /// <summary>
        /// Refresh duration (for re-application of same effect type).
        /// </summary>
        public void Refresh()
        {
            RemainingDuration = Duration;
        }

        /// <summary>
        /// Refresh with new values (if new application is stronger).
        /// </summary>
        public void RefreshWith(StatusEffectInstance other)
        {
            // Take the stronger values
            if (other.DamagePerTick > DamagePerTick)
                DamagePerTick = other.DamagePerTick;

            if (other.Duration > RemainingDuration)
                RemainingDuration = other.Duration;

            // Update source to latest applicator
            Source = other.Source;
        }

        public bool IsExpired => RemainingDuration <= 0f;
    }
}