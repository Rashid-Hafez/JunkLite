using UnityEngine;
using TMPro;
using UnityEngine.UI;
using System.Collections.Generic;
using Unity.VisualScripting;

namespace junklite
{
    public class WeaponUI : MonoBehaviour
    {
        [Header("References")]
        [SerializeField] private Image weaponIcon;
        [SerializeField] private GameObject weaponPanel;
        [SerializeField] private TMP_Text weaponNameText;
        [SerializeField] private Transform modSlotsParent;
        [SerializeField] private ModSlotUI modSlotPrefab;

        private WeaponHolder holder;
        private WeaponInstance currentWeapon;
        private List<ModSlotUI> slotUIs = new();

        public void Bind(WeaponHolder weaponHolder)
        {
            holder = weaponHolder;

            holder.OnWeaponChanged += RefreshWeapon;

            // Subscribe for weapon equip changes if needed later
            RefreshWeapon();
        }

        public void Unbind()
        {
            if (holder != null)
                holder.OnWeaponChanged -= RefreshWeapon;

            ClearSlots();
            holder = null;
            currentWeapon = null;
            weaponPanel.gameObject.SetActive(false);
        }

        // Call whenever weapon changes
        public void RefreshWeapon()
        {
            if (holder == null || holder.CurrentWeapon == null)
            {
                weaponPanel.gameObject.SetActive(false);
                return;
            }

            weaponPanel.gameObject.SetActive(true);

            BindToWeapon(holder.CurrentWeapon);
        }

        private void BindToWeapon(WeaponInstance weapon)
        {
            // Remove old listeners
            if (currentWeapon != null)
                currentWeapon.OnModsChanged -= RefreshSlots;

            currentWeapon = weapon;

            // Weapon icon
            if (weaponIcon != null)
                weaponIcon.sprite = weapon.weaponData.icon;

            // Name
            if (weaponNameText != null)
                weaponNameText.text = weapon.weaponData.displayName;

            // Build slots
            ClearSlots();
            CreateSlots();

            // Listen for mod updates
            currentWeapon.OnModsChanged += RefreshSlots;
        }

        private void CreateSlots()
        {
            int slotCount = currentWeapon.weaponData.maxActiveModSlots;
            var mods = currentWeapon.GetActiveMods();

            for (int i = 0; i < slotCount; i++)
            {
                var ui = Instantiate(modSlotPrefab, modSlotsParent);
                ui.Bind(i < mods.Count ? mods[i] : null, currentWeapon);
                slotUIs.Add(ui);
            }
        }

        private void ClearSlots()
        {
            foreach (var ui in slotUIs)
                Destroy(ui.gameObject);

            slotUIs.Clear();
        }

        private void RefreshSlots()
        {
            var mods = currentWeapon.GetActiveMods();

            for (int i = 0; i < slotUIs.Count; i++)
            {
                ModRuntimeInstance runtime = i < mods.Count ? mods[i] : null;
                slotUIs[i].Bind(runtime, currentWeapon);
            }
        }
    }
}
