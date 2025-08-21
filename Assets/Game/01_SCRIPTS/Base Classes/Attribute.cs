using System;
using System.Collections.Generic;
using UnityEngine;

namespace junklite
{
    public enum AttributeType
    {
        Health,
        Resource, // Mana, Stamina, Energy, etc.
        Stat      // Strength, Defense, etc.
    }

    /// <summary>
    /// Simple attribute class
    /// </summary>
    [System.Serializable]
    public class Attribute
    {
        [Header("Basic Settings")]
        public string name = "Attribute";
        public float maxValue = 100f;
        public float startingValue = 100f;

        [Header("Behavior")]
        public AttributeType type = AttributeType.Health;
        public bool hasRegeneration = false;
        public float regenRate = 1f; // per second
        public float regenDelay = 2f; // delay after damage

        // Runtime values
        [SerializeField] private float currentValue;
        private float lastChangeTime;

        // Events
        public event Action<float, float> OnValueChanged; // (current, max)
        public event Action OnEmpty;
        public event Action OnFull;
        public event Action OnDeath; // Only for health type

        // Properties
        public float Current => currentValue;
        public float Max => maxValue;
        public float Percentage => maxValue > 0 ? currentValue / maxValue : 0f;
        public bool IsEmpty => currentValue <= 0f;
        public bool IsFull => currentValue >= maxValue;
        public bool IsAlive => type != AttributeType.Health || currentValue > 0f;

        public void Initialize()
        {
            currentValue = Mathf.Clamp(startingValue, 0f, maxValue);
            lastChangeTime = Time.time;
            OnValueChanged?.Invoke(currentValue, maxValue);
        }

        public bool TryChange(float amount)
        {
            float newValue = Mathf.Clamp(currentValue + amount, 0f, maxValue);

            if (Mathf.Abs(newValue - currentValue) > 0.001f)
            {
                bool wasEmpty = IsEmpty;
                bool wasFull = IsFull;
                bool wasAlive = IsAlive;

                currentValue = newValue;
                lastChangeTime = Time.time;

                OnValueChanged?.Invoke(currentValue, maxValue);

                // Fire special events
                if (!wasEmpty && IsEmpty) OnEmpty?.Invoke();
                if (!wasFull && IsFull) OnFull?.Invoke();

                // Death event for health
                if (type == AttributeType.Health && wasAlive && !IsAlive)
                    OnDeath?.Invoke();

                return true;
            }
            return false;
        }

        public void UpdateRegen(float deltaTime)
        {
            if (hasRegeneration && currentValue < maxValue)
            {
                if (Time.time >= lastChangeTime + regenDelay)
                {
                    TryChange(regenRate * deltaTime);
                }
            }
        }

        // Simple methods
        public void Add(float amount) => TryChange(amount);
        public void Remove(float amount) => TryChange(-amount);
        public void SetToMax() => TryChange(maxValue - currentValue);
        public void SetToZero() => TryChange(-currentValue);

        // Resource methods
        public bool CanAfford(float cost) => currentValue >= cost;
        public bool TryConsume(float cost)
        {
            if (CanAfford(cost))
            {
                Remove(cost);
                return true;
            }
            return false;
        }
    }
}