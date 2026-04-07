using UnityEngine;
using System;

namespace junklite
{
    /// <summary>
    /// Generic damage shield component. Absorbs incoming damage before it reaches health.
    /// Reusable by any mod or system that needs to provide a temporary shield.
    /// Place on the player - checked by PlayerCharacter.TakeDamage.
    /// </summary>
    public class DamageShield : MonoBehaviour
    {
        #region Fields

        private float currentHP;
        private float maxHP;
        private float expireTime;
        private bool isActive;

        #endregion

        #region Properties

        public bool IsActive => isActive && currentHP > 0f && Time.time < expireTime;
        public float CurrentHP => currentHP;
        public float MaxHP => maxHP;

        /// <summary>Normalized 0-1 shield health for UI.</summary>
        public float NormalizedHP => maxHP > 0f ? Mathf.Clamp01(currentHP / maxHP) : 0f;

        public float TimeRemaining => isActive ? Mathf.Max(0f, expireTime - Time.time) : 0f;

        #endregion

        #region Events

        /// <summary>Fired when shield absorbs damage. Args: currentHP, maxHP.</summary>
        public event Action<float, float> OnShieldDamaged;

        /// <summary>Fired when shield breaks (HP depleted) or expires (time ran out).</summary>
        public event Action OnShieldBroken;

        /// <summary>Fired when shield is activated.</summary>
        public event Action<float, float> OnShieldActivated;

        #endregion

        #region Public API

        /// <summary>
        /// Activate the shield with the given HP and duration.
        /// If already active, replaces the current shield.
        /// </summary>
        public void Activate(float shieldHP, float duration)
        {
            maxHP = shieldHP;
            currentHP = shieldHP;
            expireTime = Time.time + duration;
            isActive = true;

            OnShieldActivated?.Invoke(currentHP, maxHP);
        }

        /// <summary>
        /// Deactivate the shield immediately.
        /// </summary>
        public void Deactivate()
        {
            if (!isActive) return;

            isActive = false;
            currentHP = 0f;
            OnShieldBroken?.Invoke();
        }

        /// <summary>
        /// Absorb incoming damage. Returns the remaining damage that passes through.
        /// Returns 0 if fully absorbed.
        /// </summary>
        public float Absorb(float damage)
        {
            if (!IsActive) return damage;

            if (damage <= currentHP)
            {
                // Fully absorbed
                currentHP -= damage;
                OnShieldDamaged?.Invoke(currentHP, maxHP);

                if (currentHP <= 0f)
                    Deactivate();

                return 0f;
            }

            // Partially absorbed - shield breaks, remainder passes through
            float remainder = damage - currentHP;
            currentHP = 0f;
            Deactivate();

            return remainder;
        }

        #endregion

        #region Update

        private void Update()
        {
            // Auto-expire
            if (isActive && Time.time >= expireTime)
                Deactivate();
        }

        #endregion
    }
}