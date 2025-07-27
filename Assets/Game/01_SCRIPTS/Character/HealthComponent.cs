using System;
using Gentleland.StemapunkUI.DemoAndExample;
using UnityEngine;
using UnityEngine.Events;

namespace junklite
{

    public class HealthComponent : MonoBehaviour
    {
        float currentHealth;
        float maxHealth;
        float armor;

        /// <summary>Fires after any health change: (current, max)</summary>
        public event Action<float, float> OnHealthChanged;

        /// <summary>Fires once when health reaches zero.</summary>
        public event Action OnDeath;

        public float CurrentHealth => currentHealth;
        public float MaxHealth => maxHealth;
        public bool IsAlive => currentHealth > 0f;
        public float HealthFraction => maxHealth > 0f ? currentHealth / maxHealth : 0f;

        /// <summary>Initialize with stats (clone your SO first!).</summary>
        public void Initialize(CharacterStats stats)
        {
            maxHealth = stats.maxHealth;
            armor = stats.armor;
            currentHealth = maxHealth;
            OnHealthChanged?.Invoke(currentHealth, maxHealth);
        }

        public void TakeDamage(DamageInfo info)
        {
            if (!IsAlive) return;

            float dmgAfterArmor = CalculateDamageAfterArmor(info.Amount);
            currentHealth = Mathf.Max(0f, currentHealth - dmgAfterArmor);

            OnHealthChanged?.Invoke(currentHealth, maxHealth);

            if (currentHealth <= 0f)
                OnDeath?.Invoke();
        }

        public void Heal(float amount)
        {
            if (!IsAlive) return;

            currentHealth = Mathf.Min(maxHealth, currentHealth + amount);
            OnHealthChanged?.Invoke(currentHealth, maxHealth);
        }

        float CalculateDamageAfterArmor(float raw)
            => raw * (1f - armor / (armor + 100f));
    }
}
