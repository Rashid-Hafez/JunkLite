using UnityEngine;
using UnityEngine.EventSystems;
using System;
using System.Collections.Generic;
using UnityEngine.UI;

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
        private PlayerWeaponLoadout weaponLoadout;
        private ModManager modManager;

        private readonly List<ModSlotUI> inventorySlots = new();
        private readonly List<ModSlotUI> activeModSlots = new();
        private readonly List<ModSlotUI> passiveModSlots = new();

        private enum Tab { Inventory, Info, Missions }
        private Tab activeTab = Tab.Inventory;
        private const float NavigateDeadZone = 0.45f;
        private const float NavigateRepeatDelay = 0.16f;
        private float nextNavigateTime;

        #endregion


        #region Bind / Unbind

        public void Bind(InventoryComponent inv, WeaponManager wm)
        {
            Unbind();

            inventory = inv;
            weaponManager = wm;
            weaponLoadout = wm != null ? wm.Loadout : null;
            modManager = wm != null ? wm.GetComponent<ModManager>() : null;

            if (inventory != null)
                inventory.OnInventoryChanged += RefreshInventory;

            if (weaponLoadout != null)
                weaponLoadout.WeaponChanged += RefreshWeapons;

            if (modManager != null)
                modManager.OnModSlotsChanged += RefreshModSlots;

            ModSlotUI.OnModHovered += HandleModHovered;
            ModSlotUI.OnModHoverExit += HandleHoverExit;
            ModSlotUI.OnModSelected += HandleModSelected;
            InventoryWeaponSlotUI.OnWeaponHovered += HandleWeaponHovered;
            InventoryWeaponSlotUI.OnWeaponHoverExit += HandleHoverExit;

            if (GameInputManager.Instance != null)
                GameInputManager.Instance.OnUINavigate += HandleUINavigate;

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

            if (weaponLoadout != null)
                weaponLoadout.WeaponChanged -= RefreshWeapons;

            if (modManager != null)
                modManager.OnModSlotsChanged -= RefreshModSlots;

            ModSlotUI.OnModHovered -= HandleModHovered;
            ModSlotUI.OnModHoverExit -= HandleHoverExit;
            ModSlotUI.OnModSelected -= HandleModSelected;
            InventoryWeaponSlotUI.OnWeaponHovered -= HandleWeaponHovered;
            InventoryWeaponSlotUI.OnWeaponHoverExit -= HandleHoverExit;

            if (GameInputManager.Instance != null)
                GameInputManager.Instance.OnUINavigate -= HandleUINavigate;

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
            weaponLoadout = null;
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
            TrySelectDefaultSlotIfGamepad();
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

        private void HandleModHovered(ModInstance mod)
        {
            if (mod == null) descriptionUI?.Clear();
            else descriptionUI?.ShowMod(mod);
        }

        private void HandleWeaponHovered(WeaponInstance weapon)
        {
            if (weapon == null) descriptionUI?.Clear();
            else descriptionUI?.ShowWeapon(weapon);
        }

        private void HandleHoverExit()
        {
            descriptionUI?.Clear();
        }

        private void HandleModSelected(ModInstance selectedMod)
        {
            if (selectedMod == null) return;
            if (EventSystem.current == null) return;

            var preferredTarget = GetPreferredTargetSlotFor(selectedMod);
            if (preferredTarget == null) return;

            EventSystem.current.SetSelectedGameObject(preferredTarget.gameObject);
        }

        private void HandleUINavigate(Vector2 move)
        {
            if (activeTab != Tab.Inventory) return;
            if (EventSystem.current == null) return;
            if (move.sqrMagnitude < NavigateDeadZone * NavigateDeadZone) return;
            if (Time.unscaledTime < nextNavigateTime) return;

            nextNavigateTime = Time.unscaledTime + NavigateRepeatDelay;

            if (TryMoveSelection(move.normalized))
                return;

            TrySelectDefaultSlotIfGamepad();
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
                    ConfigureSlotNavigation(slot);
                    inventorySlots.Add(slot);
                }
            }

            TrySelectDefaultSlotIfGamepad();
        }

        #endregion


        #region Weapon Slots

        private void RefreshWeapons()
        {
            if (weaponManager == null || weaponLoadout == null) return;

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
                        ConfigureSlotNavigation(slot);
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
                        ConfigureSlotNavigation(slot);
                        passiveModSlots.Add(slot);
                    }
                }
            }

            TrySelectDefaultSlotIfGamepad();
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

        private void TrySelectDefaultSlotIfGamepad()
        {
            if (activeTab != Tab.Inventory) return;
            if (GameInputManager.Instance == null || !GameInputManager.Instance.IsUsingGamepad) return;
            if (EventSystem.current == null) return;

            var currentSelected = EventSystem.current.currentSelectedGameObject;
            if (currentSelected != null &&
                currentSelected.activeInHierarchy &&
                currentSelected.GetComponent<ModSlotUI>() != null)
            {
                return;
            }

            ModSlotUI defaultSlot = GetFirstSelectableModSlot();
            if (defaultSlot == null) return;

            EventSystem.current.SetSelectedGameObject(defaultSlot.gameObject);
        }

        private ModSlotUI GetFirstSelectableModSlot()
        {
            foreach (var slot in activeModSlots)
            {
                if (IsSlotSelectable(slot)) return slot;
            }

            foreach (var slot in passiveModSlots)
            {
                if (IsSlotSelectable(slot)) return slot;
            }

            foreach (var slot in inventorySlots)
            {
                if (IsSlotSelectable(slot)) return slot;
            }

            return null;
        }

        private ModSlotUI GetPreferredTargetSlotFor(ModInstance selectedMod)
        {
            if (selectedMod == null) return null;

            foreach (var slot in activeModSlots)
            {
                if (IsCompatibleEquipTarget(slot, selectedMod)) return slot;
            }

            foreach (var slot in passiveModSlots)
            {
                if (IsCompatibleEquipTarget(slot, selectedMod)) return slot;
            }

            return null;
        }

        private static bool IsCompatibleEquipTarget(ModSlotUI slot, ModInstance mod)
        {
            if (!IsSlotSelectable(slot) || slot == null || !slot.IsModSlot || slot.IsLocked || mod == null)
                return false;

            if (slot.Type == ModSlotUI.SlotType.ActiveMod)
                return mod.IsActive;

            if (slot.Type == ModSlotUI.SlotType.PassiveMod)
                return mod.IsPassive;

            return false;
        }

        private bool TryMoveSelection(Vector2 direction)
        {
            var currentObj = EventSystem.current.currentSelectedGameObject;
            var currentSlot = currentObj != null ? currentObj.GetComponent<ModSlotUI>() : null;
            var allSlots = GetAllNavigableSlots();
            if (allSlots.Count == 0) return false;

            if (currentSlot == null || !IsSlotSelectable(currentSlot))
            {
                EventSystem.current.SetSelectedGameObject(allSlots[0].gameObject);
                return true;
            }

            var currentPos = (Vector2)currentSlot.transform.position;
            ModSlotUI bestCandidate = null;
            float bestScore = float.NegativeInfinity;

            foreach (var candidate in allSlots)
            {
                if (candidate == currentSlot) continue;

                Vector2 toCandidate = (Vector2)candidate.transform.position - currentPos;
                float distance = toCandidate.magnitude;
                if (distance <= 0.001f) continue;

                Vector2 dir = toCandidate / distance;
                float alignment = Vector2.Dot(direction, dir);
                if (alignment <= 0.2f) continue;

                float score = (alignment * 1000f) - distance;
                if (score > bestScore)
                {
                    bestScore = score;
                    bestCandidate = candidate;
                }
            }

            if (bestCandidate == null) return false;

            EventSystem.current.SetSelectedGameObject(bestCandidate.gameObject);
            return true;
        }

        private List<ModSlotUI> GetAllNavigableSlots()
        {
            var result = new List<ModSlotUI>(activeModSlots.Count + passiveModSlots.Count + inventorySlots.Count);
            AddNavigableSlots(activeModSlots, result);
            AddNavigableSlots(passiveModSlots, result);
            AddNavigableSlots(inventorySlots, result);
            return result;
        }

        private static void AddNavigableSlots(List<ModSlotUI> source, List<ModSlotUI> target)
        {
            foreach (var slot in source)
            {
                if (IsSlotSelectable(slot))
                    target.Add(slot);
            }
        }

        private static bool IsSlotSelectable(ModSlotUI slot)
        {
            if (slot == null || !slot.gameObject.activeInHierarchy) return false;
            var selectable = slot.GetComponent<Selectable>();
            return selectable == null || selectable.IsInteractable();
        }

        private static void ConfigureSlotNavigation(ModSlotUI slot)
        {
            if (slot == null) return;
            var selectable = slot.GetComponent<Selectable>();
            if (selectable == null) return;

            var navigation = selectable.navigation;
            navigation.mode = Navigation.Mode.Automatic;
            selectable.navigation = navigation;
        }

        #endregion
    }
}
