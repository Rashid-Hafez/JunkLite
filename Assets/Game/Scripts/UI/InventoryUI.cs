using UnityEngine;
using UnityEngine.UI;
using System.Collections.Generic;

namespace junklite
{

    public class InventoryUI : MonoBehaviour
    {
        #region Fields
        [Header("Tabs")]
        [SerializeField] private Button inventoryTabButton;
        [SerializeField] private Button infoTabButton;

        [Header("Tab Screens")]
        [SerializeField] private GameObject inventoryScreen;
        [SerializeField] private GameObject infoScreen;


        [Header("Inventory Slots")]
        [SerializeField] private GameObject inventorySlotPrefab;
        [SerializeField] private Transform inventorySlotParent;

        [Header("Active Mod Slots")]
        [SerializeField] private GameObject activeModSlotPrefab;
        [SerializeField] private Transform activeModSlotParent;

        [Header("Passive Mod Slots")]
        [SerializeField] private GameObject passiveModSlotPrefab;
        [SerializeField] private Transform passiveModSlotParent;

        [Header("Weapon Slots")]
        [SerializeField] private GameObject weaponSlotPrefab;
        [SerializeField] private Transform weaponSlotParent;

        [Header("Description Box")]
        [SerializeField] private ItemDescriptionUI descriptionUI;

        private InventoryComponent inventory;
        private WeaponManager weaponManager;
        private ModManager modManager;

        private readonly List<ModSlotUI> inventorySlots = new();
        private readonly List<ModSlotUI> activeModSlots = new();
        private readonly List<ModSlotUI> passiveModSlots = new();
        private readonly List<InventoryWeaponSlotUI> weaponSlots = new();

        private enum Tab { Inventory, Info }
        private Tab activeTab = Tab.Inventory;

        #endregion

        
        #region Bind / Unbind

        public void Bind(InventoryComponent inv, WeaponManager wm)
        {
            Unbind();

            inventory = inv;
            weaponManager = wm;
            modManager = wm != null ? wm.GetComponent<ModManager>() : null;

            if (inventory != null)
                inventory.OnInventoryChanged += RefreshInventory;

            if (weaponManager != null)
                weaponManager.OnWeaponChanged += RefreshWeapons;

            if (modManager != null)
                modManager.OnModSlotsChanged += RefreshModSlots;

            // Slot selection events → description box
            ModSlotUI.OnModSelected += HandleModSelected;
            InventoryWeaponSlotUI.OnWeaponSelected += HandleWeaponSelected;

            // Tab buttons
            if (inventoryTabButton != null)
                inventoryTabButton.onClick.AddListener(ShowInventoryTab);

            if (infoTabButton != null)
                infoTabButton.onClick.AddListener(ShowInfoTab);

            ShowInventoryTab();
            RefreshAll();
        }

        public void Unbind()
        {
            if (inventory != null)
                inventory.OnInventoryChanged -= RefreshInventory;

            if (weaponManager != null)
                weaponManager.OnWeaponChanged -= RefreshWeapons;

            if (modManager != null)
                modManager.OnModSlotsChanged -= RefreshModSlots;

            ModSlotUI.OnModSelected -= HandleModSelected;
            InventoryWeaponSlotUI.OnWeaponSelected -= HandleWeaponSelected;

            if (inventoryTabButton != null)
                inventoryTabButton.onClick.RemoveListener(ShowInventoryTab);

            if (infoTabButton != null)
                infoTabButton.onClick.RemoveListener(ShowInfoTab);

            ClearSlots(inventorySlots);
            ClearSlots(activeModSlots);
            ClearSlots(passiveModSlots);
            ClearWeaponSlots();

            descriptionUI?.Clear();

            inventory = null;
            weaponManager = null;
            modManager = null;
        }

        public void RefreshAll()
        {
            RefreshInventory();
            RefreshWeapons();
            RefreshModSlots();
        }

        #endregion

        #region Tabs

        private void ShowInventoryTab()
        {
            activeTab = Tab.Inventory;

            if (inventoryScreen != null) inventoryScreen.SetActive(true);
            if (infoScreen != null) infoScreen.SetActive(false);

            descriptionUI?.Clear();
        }

        private void ShowInfoTab()
        {
            activeTab = Tab.Info;

            if (inventoryScreen != null) inventoryScreen.SetActive(false);
            if (infoScreen != null) infoScreen.SetActive(true);

            descriptionUI?.Clear();
        }

        #endregion

    
        #region Description Box Handlers

        private void HandleModSelected(ModInstance mod)
        {
            if (mod == null) descriptionUI?.Clear();
            else descriptionUI?.ShowMod(mod);
        }

        private void HandleWeaponSelected(WeaponInstance weapon)
        {
            if (weapon == null) descriptionUI?.Clear();
            else descriptionUI?.ShowWeapon(weapon);
        }

        #endregion

       
        #region Inventory Slots

        private void RefreshInventory()
        {
            ClearSlots(inventorySlots);

            if (inventory == null || inventorySlotPrefab == null || inventorySlotParent == null) return;

            for (int i = 0; i < inventory.SlotCount; i++)
            {
                ModInstance mod = inventory.GetModAt(i);

                var go = Instantiate(inventorySlotPrefab, inventorySlotParent);
                var slot = go.GetComponent<ModSlotUI>();

                if (slot != null)
                {
                    slot.Bind(mod, inventory, i);
                    inventorySlots.Add(slot);
                }
            }
        }

        #endregion

        // -----------------------------------------------------------------------
        #region Weapon Slots

        private void RefreshWeapons()
        {
            ClearWeaponSlots();

            if (weaponManager == null || weaponSlotPrefab == null || weaponSlotParent == null) return;

            for (int i = 1; i <= 2; i++)
            {
                var go = Instantiate(weaponSlotPrefab, weaponSlotParent);
                var ui = go.GetComponent<InventoryWeaponSlotUI>();
                if (ui != null)
                {
                    ui.Bind(weaponManager, i);
                    weaponSlots.Add(ui);
                }
            }
        }

        private void ClearWeaponSlots()
        {
            foreach (var slot in weaponSlots)
                if (slot != null) Destroy(slot.gameObject);
            weaponSlots.Clear();
        }

        #endregion

        // -----------------------------------------------------------------------
        #region Mod Slots

        private void RefreshModSlots()
        {
            ClearSlots(activeModSlots);
            ClearSlots(passiveModSlots);

            if (modManager == null) return;

            if (activeModSlotParent != null && activeModSlotPrefab != null)
            {
                for (int i = 0; i < modManager.UnlockedActiveSlots; i++)
                {
                    var go = Instantiate(activeModSlotPrefab, activeModSlotParent);
                    var slot = go.GetComponent<ModSlotUI>();
                    if (slot != null)
                    {
                        slot.Bind(modManager.GetActiveMod(i), modManager, inventory, i, true);
                        activeModSlots.Add(slot);
                    }
                }
            }

            if (passiveModSlotParent != null && passiveModSlotPrefab != null)
            {
                for (int i = 0; i < modManager.UnlockedPassiveSlots; i++)
                {
                    var go = Instantiate(passiveModSlotPrefab, passiveModSlotParent);
                    var slot = go.GetComponent<ModSlotUI>();
                    if (slot != null)
                    {
                        slot.Bind(modManager.GetPassiveMod(i), modManager, inventory, i, false);
                        passiveModSlots.Add(slot);
                    }
                }
            }
        }

        #endregion

        // -----------------------------------------------------------------------
        #region Helpers

        private void ClearSlots(List<ModSlotUI> slots)
        {
            foreach (var slot in slots)
                if (slot != null) Destroy(slot.gameObject);
            slots.Clear();
        }

        private void OnDisable()
        {
            ClearSlots(activeModSlots);
            ClearSlots(passiveModSlots);
        }

        private void OnDestroy() => Unbind();

        #endregion
    }
}