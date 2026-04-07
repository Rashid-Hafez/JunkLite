using UnityEngine;

namespace junklite
{
    /// <summary>
    /// Runtime wrapper for a mod. Tracks durability, charge state, and cooldown.
    /// One ModInstance per equipped mod slot.
    /// </summary>
    public class ModInstance
    {
        public ModData Data { get; private set; }
        public float CurrentDurability { get; private set; }
        public int CurrentCharges { get; private set; }

        public bool IsBroken => CurrentDurability <= 0f;
        public bool IsActive => Data is ActiveModData;
        public bool IsPassive => Data is PassiveModData;

        // Cooldown
        private float cooldownStartTime;
        private float cooldownEndTime;

        public bool IsOnCooldown => Time.time < cooldownEndTime;

        /// <summary>
        /// Normalized cooldown value: 1 when cooldown just started, 0 when finished.
        /// </summary>
        public float CooldownNormalized
        {
            get
            {
                if (!IsOnCooldown) return 0f;
                float total = cooldownEndTime - cooldownStartTime;
                if (total <= 0f) return 0f;
                return Mathf.Clamp01((cooldownEndTime - Time.time) / total);
            }
        }

        public ModInstance(ModData data)
        {
            Data = data;
            CurrentDurability = data.maxDurability;
            CurrentCharges = 0;
            cooldownStartTime = 0f;
            cooldownEndTime = 0f;
        }

        public void ConsumeDurability()
        {
            if (IsBroken || Data == null) return;
            CurrentDurability = Mathf.Max(0f, CurrentDurability - Data.durabilityPerUse);
        }

        public void AddCharge(int amount)
        {
            CurrentCharges += amount;
        }

        public void ResetCharges()
        {
            CurrentCharges = 0;
        }

        /// <summary>
        /// Start cooldown. Call this when the mod's effect finishes (not when it starts).
        /// </summary>
        public void StartCooldown(float duration)
        {
            if (duration <= 0f) return;
            cooldownStartTime = Time.time;
            cooldownEndTime = Time.time + duration;
        }

        public void ResetCooldown()
        {
            cooldownStartTime = 0f;
            cooldownEndTime = 0f;
        }
    }
}