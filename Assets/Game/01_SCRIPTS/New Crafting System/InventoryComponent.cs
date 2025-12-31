using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Serialization;

namespace junklite
{
    public class InventoryComponent : MonoBehaviour
    {
        [Header("Stored Mods")]
        [SerializeField] private List<Mod_Data> ownedMods = new();

        [FormerlySerializedAs("weaponHolder")]
        [Header("References")]
        [SerializeField] private WeaponManager weaponManager;

        public event System.Action OnInventoryChanged;

        void Awake()
        {
            if (!weaponManager)
                weaponManager = GetComponent<WeaponManager>();
        }

        // ===== PICKUP MOD =====
        public void PickupMod(Mod_Data mod)
        {
            ownedMods.Add(mod);

            // Optional auto-equip if weapon has a free slot
            var weapon = weaponManager.CurrentWeapon;
            if (weapon != null && weapon.HasFreeModSlot)
            {
                if (weapon.TryAddMod(mod))
                {
                    ownedMods.Remove(mod); // moved from inventory → weapon
                }
            }

            OnInventoryChanged?.Invoke();
        }

        // ===== MANUAL EQUIP FROM INVENTORY =====
        public void EquipMod(Mod_Data mod)
        {
            var weapon = weaponManager.CurrentWeapon;
            if (weapon == null) return;
            if (!ownedMods.Contains(mod)) return;

            bool equipped = weapon.TryAddMod(mod);
            if (equipped)
            {
                ownedMods.Remove(mod);
                OnInventoryChanged?.Invoke();
            }
            // if !equipped (no slot) → stays in inventory
        }

        // ===== UNEQUIP BACK TO INVENTORY =====
        public void UnequipMod(ModRuntimeInstance runtime)
        {
            var weapon = weaponManager.CurrentWeapon;
            if (weapon == null) return;

            ownedMods.Add(runtime.data);
            weapon.RemoveMod(runtime);

            OnInventoryChanged?.Invoke();
        }

        public IReadOnlyList<Mod_Data> GetOwnedMods() => ownedMods;

        public void EquipRandomMod()
        {
            if (ownedMods.Count == 0) return;

            var weapon = weaponManager.CurrentWeapon;
            if (weapon == null) return;

            // Try to equip each mod until one works
            foreach (var mod in ownedMods)
            {
                if (weapon.TryAddMod(mod))
                {
                    ownedMods.Remove(mod);
                    OnInventoryChanged?.Invoke();
                    break;
                }
            }
        }
    }

}
