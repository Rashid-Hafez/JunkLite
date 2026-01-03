using System.Collections.Generic;
using UnityEngine;

namespace junklite
{
    /// <summary>
    /// Stores mods that couldn't fit on the weapon.
    /// Mods go to weapon first → if no free slots, stored here.
    /// </summary>
    public class InventoryComponent : MonoBehaviour
    {
        [Header("Stored Mods")]
        [SerializeField] private List<ModData> storedMods = new();

        [Header("References")]
        [SerializeField] private WeaponManager weaponManager;

        public event System.Action OnInventoryChanged;

        // Public accessors
        public IReadOnlyList<ModData> StoredMods => storedMods;
        public int StoredModCount => storedMods.Count;

        private void Awake()
        {
            if (weaponManager == null)
                weaponManager = GetComponent<WeaponManager>();
        }

        /// <summary>
        /// Called when player picks up a mod.
        /// Tries to equip on weapon first, stores in inventory if no slot.
        /// </summary>
        public void PickupMod(ModData mod)
        {
            if (mod == null) return;

            var weapon = weaponManager?.CurrentWeapon;

            // Try to equip directly on weapon
            if (weapon != null && weapon.HasFreeSlot)
            {
                if (weapon.TryAddMod(mod))
                {
                    Debug.Log($"[Inventory] Mod equipped directly: {mod.modName}");
                    return;
                }
            }

            // No free slot - store in inventory
            storedMods.Add(mod);
            OnInventoryChanged?.Invoke();
            Debug.Log($"[Inventory] Mod stored (no free slot): {mod.modName}");
        }

        /// <summary>
        /// Manually equip a mod from inventory to weapon.
        /// </summary>
        public bool EquipMod(ModData mod)
        {
            if (mod == null || !storedMods.Contains(mod))
                return false;

            var weapon = weaponManager?.CurrentWeapon;
            if (weapon == null || !weapon.HasFreeSlot)
                return false;

            if (weapon.TryAddMod(mod))
            {
                storedMods.Remove(mod);
                OnInventoryChanged?.Invoke();
                Debug.Log($"[Inventory] Equipped from inventory: {mod.modName}");
                return true;
            }

            return false;
        }

        /// <summary>
        /// Unequip a mod from weapon back to inventory.
        /// </summary>
        public void UnequipMod(ActiveMod activeMod)
        {
            if (activeMod == null) return;

            var weapon = weaponManager?.CurrentWeapon;
            if (weapon == null) return;

            // Store the mod data before removing
            storedMods.Add(activeMod.data);
            weapon.RemoveMod(activeMod);

            OnInventoryChanged?.Invoke();
            Debug.Log($"[Inventory] Unequipped to inventory: {activeMod.data.modName}");
        }

        /// <summary>
        /// Try to equip a random stored mod (useful for auto-equip on weapon pickup).
        /// </summary>
        public bool EquipRandomMod()
        {
            if (storedMods.Count == 0) return false;

            var weapon = weaponManager?.CurrentWeapon;
            if (weapon == null || !weapon.HasFreeSlot) return false;

            // Try each stored mod
            for (int i = storedMods.Count - 1; i >= 0; i--)
            {
                var mod = storedMods[i];
                if (weapon.TryAddMod(mod))
                {
                    storedMods.RemoveAt(i);
                    OnInventoryChanged?.Invoke();
                    Debug.Log($"[Inventory] Auto-equipped: {mod.modName}");
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Equip all stored mods that can fit on the weapon.
        /// </summary>
        public void EquipAllPossible()
        {
            var weapon = weaponManager?.CurrentWeapon;
            if (weapon == null) return;

            for (int i = storedMods.Count - 1; i >= 0; i--)
            {
                if (!weapon.HasFreeSlot) break;

                var mod = storedMods[i];
                if (weapon.TryAddMod(mod))
                {
                    storedMods.RemoveAt(i);
                }
            }

            OnInventoryChanged?.Invoke();
        }

        /// <summary>
        /// Check if a specific mod is in inventory.
        /// </summary>
        public bool HasMod(ModData mod)
        {
            return storedMods.Contains(mod);
        }

        /// <summary>
        /// Remove a mod from inventory (e.g., if sold or discarded).
        /// </summary>
        public bool RemoveMod(ModData mod)
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