using UnityEngine;

namespace junklite
{
    /// <summary>
    /// Runtime wrapper for a mod. Tracks durability, charges, active duration, and cooldown.
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
        public bool IsExecuting { get; private set; }
        public bool HasActiveEffect { get; private set; }

        private float activeStartTime;
        private float activeEndTime;
        private float pendingCooldown;

        public float ActiveDurationRemaining => HasActiveEffect ? Mathf.Max(0f, activeEndTime - Time.time) : 0f;
        public float ActiveDurationNormalized => HasActiveEffect && activeEndTime > activeStartTime
            ? Mathf.Clamp01(ActiveDurationRemaining / (activeEndTime - activeStartTime))
            : 0f;

#if UNITY_EDITOR
        /// <summary>Editor play-mode override used by the runtime developer console.</summary>
        public bool EditorInfiniteDurability { get; set; }

        public void EditorRestoreDurability()
        {
            if (Data != null)
                CurrentDurability = Data.maxDurability;
        }
#endif

        // Cooldown
        private float cooldownStartTime;
        private float cooldownEndTime;

        public bool IsOnCooldown => Time.time < cooldownEndTime;
        public float CooldownRemaining => Mathf.Max(0f, cooldownEndTime - Time.time);

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
            if (data == null)
                throw new System.ArgumentNullException(nameof(data));

            Data = data;
            CurrentDurability = data.maxDurability;
            CurrentCharges = 0;
            cooldownStartTime = 0f;
            cooldownEndTime = 0f;
        }

        internal bool TryBeginExecution()
        {
            if (IsExecuting) return false;
            IsExecuting = true;
            return true;
        }

        internal void EndExecution()
        {
            IsExecuting = false;
            TryStartPendingCooldown();
        }

        // The effect owner must end this on completion, early termination, or cancellation.
        // A timer reaching zero alone does not mean the effect's cleanup has finished.
        internal void BeginActiveDuration(float duration)
        {
            HasActiveEffect = true;
            activeStartTime = Time.time;
            activeEndTime = Time.time + Mathf.Max(0f, duration);
        }

        internal void SetActiveDurationRemaining(float remaining)
        {
            if (HasActiveEffect)
                activeEndTime = Time.time + Mathf.Max(0f, remaining);
        }

        internal void EndActiveDuration()
        {
            HasActiveEffect = false;
            activeStartTime = activeEndTime = 0f;
            TryStartPendingCooldown();
        }

        private void TryStartPendingCooldown()
        {
            if (IsExecuting || HasActiveEffect || pendingCooldown <= 0f) return;
            float duration = pendingCooldown;
            pendingCooldown = 0f;
            StartCooldown(duration);
        }

        public void ConsumeDurability()
        {
#if UNITY_EDITOR
            if (EditorInfiniteDurability)
            {
                EditorRestoreDurability();
                return;
            }
#endif
            if (IsBroken || Data == null) return;
            CurrentDurability = Mathf.Max(0f, CurrentDurability - Data.durabilityPerUse);
        }

        public void AddCharge(int amount)
        {
            if (amount <= 0) return;

            int required = Data is ActiveModData active ? active.chargesRequired : 0;
            CurrentCharges = required > 0
                ? Mathf.Min(CurrentCharges + amount, required)
                : CurrentCharges + amount;
        }

        public void ResetCharges()
        {
            CurrentCharges = 0;
        }

        /// <summary>
        /// Queues the full cooldown while the ability is active; otherwise starts it now.
        /// ActiveModData calls this only after a successful activation.
        /// </summary>
        public void StartCooldown(float duration)
        {
            if (duration <= 0f) return;
            if (IsExecuting || HasActiveEffect)
            {
                pendingCooldown = duration;
                cooldownStartTime = cooldownEndTime = 0f;
                return;
            }
            cooldownStartTime = Time.time;
            cooldownEndTime = Time.time + duration;
        }

        public void ResetCooldown()
        {
            cooldownStartTime = 0f;
            cooldownEndTime = 0f;
            pendingCooldown = 0f;
        }
    }
}
