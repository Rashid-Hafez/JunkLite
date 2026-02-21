using UnityEngine;

namespace junklite
{
    /// <summary>
    /// Runtime wrapper for a mod. Tracks durability and charge state.
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

        public ModInstance(ModData data)
        {
            Data = data;
            CurrentDurability = data.maxDurability;
            CurrentCharges = 0;
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
    }
}