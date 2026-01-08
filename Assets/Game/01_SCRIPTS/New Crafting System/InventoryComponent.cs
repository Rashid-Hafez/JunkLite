using System.Collections.Generic;
using UnityEngine;

namespace junklite
{
    public class InventoryComponent : MonoBehaviour
    {
        [Header("References")]
        [SerializeField] private WeaponManager weaponManager;

        // Store ActiveMod to preserve durability
        private List<ActiveMod> storedMods = new();

        public event System.Action OnInventoryChanged;

        public IReadOnlyList<ActiveMod> StoredMods => storedMods;
        public int StoredModCount => storedMods.Count;

        private void Awake()
        {
            if (weaponManager == null)
                weaponManager = GetComponent<WeaponManager>();
        }

        /// <summary>
        /// Called when player picks up a mod (creates new ActiveMod with full durability).
        /// </summary>
        public void PickupMod(ModData mod)
        {
            if (mod == null) return;

            var weapon = weaponManager?.CurrentWeapon;

            if (weapon != null && weapon.HasFreeSlot)
            {
                if (weapon.TryAddMod(mod))
                    return;
            }

            // Create new ActiveMod with full durability
            storedMods.Add(new ActiveMod(mod));
            OnInventoryChanged?.Invoke();
        }

        /// <summary>
        /// Store an ActiveMod directly (preserves durability).
        /// </summary>
        public void StoreMod(ActiveMod mod)
        {
            if (mod == null) return;

            storedMods.Add(mod);
            OnInventoryChanged?.Invoke();
        }

        /// <summary>
        /// Equip a mod from inventory to weapon.
        /// </summary>
        public bool EquipMod(ActiveMod mod)
        {
            if (mod == null || !storedMods.Contains(mod))
                return false;

            var weapon = weaponManager?.CurrentWeapon;
            if (weapon == null || !weapon.HasFreeSlot)
                return false;

            if (weapon.TryAddActiveMod(mod))
            {
                storedMods.Remove(mod);
                OnInventoryChanged?.Invoke();
                return true;
            }

            return false;
        }

        /// <summary>
        /// Unequip a mod from weapon back to inventory (preserves durability).
        /// </summary>
        public void UnequipMod(ActiveMod activeMod)
        {
            if (activeMod == null) return;

            var weapon = weaponManager?.CurrentWeapon;
            if (weapon == null) return;

            weapon.RemoveMod(activeMod);
            storedMods.Add(activeMod);

            OnInventoryChanged?.Invoke();
        }

        /// <summary>
        /// Swap two mods in inventory by index.
        /// </summary>
        public void SwapMods(int indexA, int indexB)
        {
            if (indexA < 0 || indexB < 0)
                return;

            int maxIndex = Mathf.Max(indexA, indexB);
            while (storedMods.Count <= maxIndex)
                storedMods.Add(null);

            var temp = storedMods[indexA];
            storedMods[indexA] = storedMods[indexB];
            storedMods[indexB] = temp;

            CleanupNulls();
            OnInventoryChanged?.Invoke();
        }

        /// <summary>
        /// Insert a mod at a specific index.
        /// </summary>
        public void InsertModAt(int index, ActiveMod mod)
        {
            if (index < 0)
            {
                storedMods.Add(mod);
            }
            else
            {
                while (storedMods.Count <= index)
                    storedMods.Add(null);

                storedMods[index] = mod;
            }

            OnInventoryChanged?.Invoke();
        }

        /// <summary>
        /// Remove mod at a specific index and return it.
        /// </summary>
        public ActiveMod RemoveModAt(int index)
        {
            if (index < 0 || index >= storedMods.Count)
                return null;

            var mod = storedMods[index];
            storedMods[index] = null;
            CleanupNulls();
            OnInventoryChanged?.Invoke();
            return mod;
        }

        /// <summary>
        /// Get mod at index.
        /// </summary>
        public ActiveMod GetModAt(int index)
        {
            if (index < 0 || index >= storedMods.Count)
                return null;
            return storedMods[index];
        }

        private void CleanupNulls()
        {
            while (storedMods.Count > 0 && storedMods[storedMods.Count - 1] == null)
                storedMods.RemoveAt(storedMods.Count - 1);
        }

        public void EquipAllPossible()
        {
            var weapon = weaponManager?.CurrentWeapon;
            if (weapon == null) return;

            for (int i = storedMods.Count - 1; i >= 0; i--)
            {
                if (!weapon.HasFreeSlot) break;

                var mod = storedMods[i];
                if (mod != null && weapon.TryAddActiveMod(mod))
                    storedMods.RemoveAt(i);
            }

            OnInventoryChanged?.Invoke();
        }

        public bool HasMod(ActiveMod mod)
        {
            return storedMods.Contains(mod);
        }

        public bool RemoveMod(ActiveMod mod)
        {
            if (storedMods.Remove(mod))
            {
                OnInventoryChanged?.Invoke();
                return true;
            }
            return false;
        }
    }
}