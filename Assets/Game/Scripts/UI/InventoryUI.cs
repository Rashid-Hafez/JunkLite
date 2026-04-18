using UnityEngine;
using UnityEngine.EventSystems;
using System;
using System.Collections.Generic;

namespace junklite
{
    public class InventoryUI : MonoBehaviour
    {
        #region Fields
        [Header("Tabs")]
        [SerializeField] private MenuButton inventoryTabButton;
        [SerializeField] private MenuButton infoTabButton;
        [SerializeField] private MenuButton missionsTabButton;

        [Header("Tab Screens")]
        [SerializeField] private GameObject inventoryScreen;
        [SerializeField] private GameObject infoScreen;
        [SerializeField] private GameObject missionsScreen;

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
        [SerializeField] private InventoryWeaponSlotUI weaponSlot1;
        [SerializeField] private InventoryWeaponSlotUI weaponSlot2;

        [Header("Description Box")]
        [SerializeField] private ItemDescriptionUI descriptionUI;

        private InventoryComponent inventory;
        private WeaponManager weaponManager;
        private ModManager modManager;

        private readonly List<ModSlotUI> inventorySlots = new();
        private readonly List<ModSlotUI> activeModSlots = new();
        private readonly List<ModSlotUI> passiveModSlots = new();

        private enum Tab { Inventory, Info, Missions }
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

            ModSlotUI.OnModSelected += HandleModSelected;
            InventoryWeaponSlotUI.OnWeaponSelected += HandleWeaponSelected;

            if (inventoryTabButton != null)
                inventoryTabButton.OnClick += ShowInventoryTab;

            if (infoTabButton != null)
                infoTabButton.OnClick += ShowInfoTab;

            if (missionsTabButton != null)
                missionsTabButton.OnClick += ShowMissionsTab;

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
                inventoryTabButton.OnClick -= ShowInventoryTab;

            if (infoTabButton != null)
                infoTabButton.OnClick -= ShowInfoTab;

            if (missionsTabButton != null)
                missionsTabButton.OnClick -= ShowMissionsTab;

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

            inventoryTabButton?.SetSelected(true);
            infoTabButton?.SetSelected(false);
            missionsTabButton?.SetSelected(false);

            if (inventoryScreen != null) inventoryScreen.SetActive(true);
            if (infoScreen != null) infoScreen.SetActive(false);
            if (missionsScreen != null) missionsScreen.SetActive(false);

            descriptionUI?.Clear();
        }

        private void ShowInfoTab()
        {
            activeTab = Tab.Info;

            inventoryTabButton?.SetSelected(false);
            infoTabButton?.SetSelected(true);
            missionsTabButton?.SetSelected(false);

            if (inventoryScreen != null) inventoryScreen.SetActive(false);
            if (infoScreen != null) infoScreen.SetActive(true);
            if (missionsScreen != null) missionsScreen.SetActive(false);

            descriptionUI?.Clear();
        }

        private void ShowMissionsTab()
        {
            activeTab = Tab.Missions;

            inventoryTabButton?.SetSelected(false);
            infoTabButton?.SetSelected(false);
            missionsTabButton?.SetSelected(true);

            if (inventoryScreen != null) inventoryScreen.SetActive(false);
            if (infoScreen != null) infoScreen.SetActive(false);
            if (missionsScreen != null) missionsScreen.SetActive(true);

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


        #region Weapon Slots

        private void RefreshWeapons()
        {
            if (weaponManager == null) return;

            weaponSlot1?.Bind(weaponManager, 1);
            weaponSlot2?.Bind(weaponManager, 2);
        }

        private void ClearWeaponSlots()
        {
            weaponSlot1?.Unbind();
            weaponSlot2?.Unbind();
        }

        #endregion


        #region Mod Slots

        private void RefreshModSlots()
        {
            ClearSlots(activeModSlots);
            ClearSlots(passiveModSlots);

            if (modManager == null) return;

            if (activeModSlotParent != null && activeModSlotPrefab != null)
            {
                for (int i = 0; i < modManager.MaxActiveSlots; i++)
                {
                    bool locked = i >= modManager.UnlockedActiveSlots;
                    var go = Instantiate(activeModSlotPrefab, activeModSlotParent);
                    var slot = go.GetComponent<ModSlotUI>();
                    if (slot != null)
                    {
                        slot.Bind(modManager.GetActiveMod(i), modManager, inventory, i, true, locked);
                        activeModSlots.Add(slot);
                    }
                }
            }

            if (passiveModSlotParent != null && passiveModSlotPrefab != null)
            {
                for (int i = 0; i < modManager.MaxPassiveSlots; i++)
                {
                    bool locked = i >= modManager.UnlockedPassiveSlots;
                    var go = Instantiate(passiveModSlotPrefab, passiveModSlotParent);
                    var slot = go.GetComponent<ModSlotUI>();
                    if (slot != null)
                    {
                        slot.Bind(modManager.GetPassiveMod(i), modManager, inventory, i, false, locked);
                        passiveModSlots.Add(slot);
                    }
                }
            }
        }

        #endregion


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