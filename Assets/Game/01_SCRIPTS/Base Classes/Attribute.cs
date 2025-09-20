using System;
using UnityEngine;

namespace junklite
{
    public enum AttributeType
    {
        Health,
        Armor,
        Resource, // Mana, Stamina, Energy, etc.
        Stat      // Strength, Defense, etc.
        // TIP: You can add specific ones (e.g., Armor) to your enum or keep using generic types.
    }

    /// <summary>
    /// Runtime attribute with optional regeneration and event-driven updates.
    /// </summary>
    [Serializable]
    public class Attribute
    {
        [Header("Basic Settings")]
        public string name = "Attribute";
        public float maxValue = 100f;
        public float startingValue = 100f;

        [Header("Behavior")]
        public AttributeType type = AttributeType.Health;
        public bool hasRegeneration = false;
        public float regenRate = 1f;    // per second
        public float regenDelay = 2f;   // seconds after last change

        // Runtime
        [SerializeField] private float currentValue;
        private float lastChangeTime;

        // Events
        /// <summary>Fires when Current changes. Arg: new current value.</summary>
        public event Action<float> OnValueChanged;

        /// <summary>Fires when Max changes. Arg: new max value.</summary>
        public event Action<float> OnMaxChanged;

        /// <summary>Convenience combined event. Args: current, max.</summary>
        public event Action<float, float> OnValueAndMaxChanged;

        public event Action OnEmpty;
        public event Action OnFull;
        /// <summary>Only meaningful when type == Health.</summary>
        public event Action OnDeath;

        // Properties
        public float Current => currentValue;
        public float Max => maxValue;
        public float Percentage => maxValue > 0f ? currentValue / maxValue : 0f;
        public bool IsEmpty => currentValue <= 0f;
        public bool IsFull => currentValue >= maxValue;
        public bool IsAlive => type != AttributeType.Health || currentValue > 0f;

        // --- Lifecycle -------------------------------------------------------

        public void Initialize()
        {
            currentValue = Mathf.Clamp(startingValue, 0f, maxValue);
            lastChangeTime = Time.time;

            // Initial broadcast so UI binds render correct values immediately.
            RaiseValueChanged();
            RaiseMaxChanged();
            RaiseValueAndMaxChanged();
        }

        // --- Value & Max mutation -------------------------------------------

        /// <summary>
        /// Attempts to change Current by 'amount' (can be negative). Clamped to [0, Max].
        /// Triggers events (OnValueChanged, OnEmpty/OnFull, OnDeath for health).
        /// </summary>
        public bool TryChange(float amount)
        {
            float newValue = Mathf.Clamp(currentValue + amount, 0f, maxValue);
            if (Mathf.Abs(newValue - currentValue) <= 0.001f) return false;

            bool wasEmpty = IsEmpty;
            bool wasFull = IsFull;
            bool wasAlive = IsAlive;

            currentValue = newValue;
            lastChangeTime = Time.time;

            RaiseValueChanged();
            RaiseValueAndMaxChanged();

            if (!wasEmpty && IsEmpty) OnEmpty?.Invoke();
            if (!wasFull && IsFull) OnFull?.Invoke();

            if (type == AttributeType.Health && wasAlive && !IsAlive)
                OnDeath?.Invoke();

            return true;
        }

        /// <summary>
        /// Sets a new Max. Optionally keeps the same % of the bar.
        /// Also clamps Current and fires OnMaxChanged and combined event.
        /// </summary>
        public void SetMax(float newMax, bool keepPercent = true)
        {
            newMax = Mathf.Max(0f, newMax);
            if (Mathf.Approximately(newMax, maxValue)) return;

            float pct = Percentage;
            maxValue = newMax;

            if (keepPercent)
                currentValue = maxValue * pct;

            // Always clamp after recompute.
            currentValue = Mathf.Clamp(currentValue, 0f, maxValue);

            RaiseMaxChanged();
            RaiseValueChanged();
            RaiseValueAndMaxChanged();
        }

        // Convenience helpers
        public void Add(float amount) => TryChange(amount);
        public void Remove(float amount) => TryChange(-amount);
        public void SetToMax() => TryChange(maxValue - currentValue);
        public void SetToZero() => TryChange(-currentValue);

        // --- Resource helpers -----------------------------------------------

        public bool CanAfford(float cost) => currentValue >= cost;

        public bool TryConsume(float cost)
        {
            if (!CanAfford(cost)) return false;
            Remove(cost);
            return true;
        }

        // --- Regeneration ----------------------------------------------------

        public void UpdateRegen(float deltaTime)
        {
            if (!hasRegeneration) return;
            if (currentValue >= maxValue) return;

            if (Time.time >= lastChangeTime + regenDelay)
                TryChange(regenRate * deltaTime);
        }

        // --- Event raisers ---------------------------------------------------

        private void RaiseValueChanged() => OnValueChanged?.Invoke(currentValue);
        private void RaiseMaxChanged() => OnMaxChanged?.Invoke(maxValue);
        private void RaiseValueAndMaxChanged() => OnValueAndMaxChanged?.Invoke(currentValue, maxValue);
    }
}
