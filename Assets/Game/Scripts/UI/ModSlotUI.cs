using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;

namespace junklite
{
    public class ModSlotUI : MonoBehaviour,
        IBeginDragHandler, IDragHandler, IEndDragHandler, IDropHandler,
        IPointerClickHandler
    {
        #region Fields

        [Header("UI References")]
        [SerializeField] private Image iconImage;
        [SerializeField] private Image durabilityFill;
        [SerializeField] private Image backgroundImage;
        [SerializeField] private Image crossIcon;       // Incompatible indicator
        [SerializeField] private Image highlightImage;   // Valid target glow

        // Data
        private ModInstance modInstance;
        private InventoryComponent inventory;
        private ModManager modManager;
        private int slotIndex;
        private SlotType slotType;

        // Drag state
        private static ModSlotUI draggedSlot;
        private static GameObject dragIcon;
        private static Canvas rootCanvas;

        // Click-to-place state
        private static ModSlotUI selectedSlot;

        #endregion

        #region Types

        public enum SlotType
        {
            Inventory,
            ActiveMod,
            PassiveMod
        }

        #endregion

        #region Properties

        public bool IsEmpty => modInstance == null;
        public ModInstance ModInstance => modInstance;
        public SlotType Type => slotType;
        public int SlotIndex => slotIndex;
        public bool IsModSlot => slotType == SlotType.ActiveMod || slotType == SlotType.PassiveMod;

        #endregion

        #region Binding

        public void Bind(ModInstance mod, InventoryComponent inv, int index)
        {
            modInstance = mod;
            inventory = inv;
            modManager = null;
            slotIndex = index;
            slotType = SlotType.Inventory;
            UpdateDisplay();
        }

        public void Bind(ModInstance mod, ModManager manager, InventoryComponent inv, int index, bool isActiveMod)
        {
            modInstance = mod;
            modManager = manager;
            inventory = inv;
            slotIndex = index;
            slotType = isActiveMod ? SlotType.ActiveMod : SlotType.PassiveMod;
            UpdateDisplay();
        }

        private void UpdateDisplay()
        {
            if (backgroundImage != null)
            {
                backgroundImage.enabled = true;
                backgroundImage.raycastTarget = true;
            }

            if (crossIcon != null)
            {
                crossIcon.enabled = false;
                crossIcon.raycastTarget = false;
            }

            if (highlightImage != null)
            {
                highlightImage.enabled = false;
                highlightImage.raycastTarget = false;
            }

            if (iconImage != null)
            {
                if (modInstance != null && modInstance.Data != null && modInstance.Data.icon != null)
                {
                    iconImage.sprite = modInstance.Data.icon;
                    iconImage.enabled = true;
                    iconImage.color = Color.white;
                }
                else
                {
                    iconImage.sprite = null;
                    iconImage.enabled = false;
                }
            }

            UpdateDurabilityBar();
        }

        private void Update()
        {
            if (modInstance != null && durabilityFill != null && modInstance.Data != null)
            {
                float max = modInstance.Data.maxDurability;
                durabilityFill.fillAmount = max > 0f ? modInstance.CurrentDurability / max : 0f;
            }

            UpdateOverlays();
        }

        /// <summary>
        /// Update cross icon and highlight based on drag or click-select state.
        /// </summary>
        private void UpdateOverlays()
        {
            // Determine which mod is being moved (drag or click-select)
            ModSlotUI source = draggedSlot != null ? draggedSlot : selectedSlot;

            // --- Cross icon (incompatible mod slot) ---
            if (crossIcon != null)
            {
                bool showCross = false;
                if (source != null && source != this && IsModSlot)
                {
                    ModInstance srcMod = source.modInstance;
                    if (srcMod != null)
                    {
                        bool compatible = slotType == SlotType.ActiveMod
                            ? srcMod.IsActive
                            : srcMod.IsPassive;
                        showCross = !compatible;
                    }
                }
                crossIcon.enabled = showCross;
            }

            // --- Highlight (valid target for click-select) ---
            if (highlightImage != null)
            {
                bool showHighlight = false;
                if (selectedSlot != null && selectedSlot != this)
                {
                    showHighlight = IsValidTargetFor(selectedSlot);
                }
                highlightImage.enabled = showHighlight;
            }
        }

        /// <summary>
        /// Can the selected mod be placed into this slot?
        /// </summary>
        private bool IsValidTargetFor(ModSlotUI source)
        {
            if (source == null || source.modInstance == null) return false;

            ModInstance srcMod = source.modInstance;

            // Inventory slots accept any mod
            if (!IsModSlot) return true;

            // Mod slots require type match
            if (slotType == SlotType.ActiveMod && !srcMod.IsActive) return false;
            if (slotType == SlotType.PassiveMod && !srcMod.IsPassive) return false;

            // If this slot has a mod, check it can go back to the source
            if (modInstance != null && source.IsModSlot)
            {
                bool sourceIsActive = source.slotType == SlotType.ActiveMod;
                if (sourceIsActive && !modInstance.IsActive) return false;
                if (!sourceIsActive && !modInstance.IsPassive) return false;
            }

            return true;
        }

        private void UpdateDurabilityBar()
        {
            if (durabilityFill == null) return;

            if (modInstance != null)
            {
                durabilityFill.gameObject.SetActive(true);
                float max = modInstance.Data.maxDurability;
                durabilityFill.fillAmount = max > 0f ? modInstance.CurrentDurability / max : 0f;
            }
            else
            {
                durabilityFill.gameObject.SetActive(false);
            }
        }

        #endregion

        #region Click-to-Place

        public void OnPointerClick(PointerEventData eventData)
        {
            // Don't process clicks during drag
            if (draggedSlot != null) return;

            // No selection yet — select this slot if it has a mod
            if (selectedSlot == null)
            {
                if (!IsEmpty)
                {
                    selectedSlot = this;
                    if (iconImage != null)
                        iconImage.color = new Color(1, 1, 1, 0.5f);
                }
                return;
            }

            // Clicking the already-selected slot — deselect
            if (selectedSlot == this)
            {
                ClearSelection();
                return;
            }

            // Clicking a valid target — perform the move
            if (IsValidTargetFor(selectedSlot))
            {
                ModSlotUI source = selectedSlot;
                ClearSelection();

                ModInstance srcMod = source.modInstance;
                ModInstance dstMod = this.modInstance;

                RemoveFromSlot(source);
                RemoveFromSlot(this);

                PlaceInSlot(this, srcMod);
                PlaceInSlot(source, dstMod);
            }
            else
            {
                // Clicked an invalid target — cancel selection
                ClearSelection();
            }
        }

        private static void ClearSelection()
        {
            if (selectedSlot != null)
            {
                if (selectedSlot.iconImage != null)
                    selectedSlot.iconImage.color = Color.white;
                selectedSlot = null;
            }
        }

        #endregion

        #region Drag

        public void OnBeginDrag(PointerEventData eventData)
        {
            // Cancel any click-selection when starting a drag
            ClearSelection();

            if (IsEmpty)
            {
                eventData.pointerDrag = null;
                return;
            }

            draggedSlot = this;

            if (rootCanvas == null)
                rootCanvas = GetComponentInParent<Canvas>()?.rootCanvas;

            if (rootCanvas == null) return;

            dragIcon = new GameObject("DragIcon");
            dragIcon.transform.SetParent(rootCanvas.transform, false);

            var img = dragIcon.AddComponent<Image>();
            img.sprite = modInstance.Data.icon;
            img.raycastTarget = false;

            var rt = dragIcon.GetComponent<RectTransform>();
            rt.sizeDelta = new Vector2(64, 64);

            if (iconImage != null)
                iconImage.color = new Color(1, 1, 1, 0.3f);

            UpdateDragPosition(eventData);
        }

        public void OnDrag(PointerEventData eventData)
        {
            UpdateDragPosition(eventData);
        }

        public void OnEndDrag(PointerEventData eventData)
        {
            CleanupDrag();
        }

        private void UpdateDragPosition(PointerEventData eventData)
        {
            if (dragIcon == null || rootCanvas == null) return;

            RectTransformUtility.ScreenPointToLocalPointInRectangle(
                rootCanvas.GetComponent<RectTransform>(),
                eventData.position,
                rootCanvas.worldCamera,
                out Vector2 pos);

            dragIcon.GetComponent<RectTransform>().anchoredPosition = pos;
        }

        private void CleanupDrag()
        {
            if (iconImage != null)
                iconImage.color = Color.white;

            if (dragIcon != null)
            {
                Destroy(dragIcon);
                dragIcon = null;
            }

            draggedSlot = null;
        }

        #endregion

        #region Drop

        public void OnDrop(PointerEventData eventData)
        {
            if (draggedSlot == null || draggedSlot == this || draggedSlot.IsEmpty)
                return;

            ModSlotUI source = draggedSlot;

            // Validate mod type if dropping INTO a mod slot
            if (this.IsModSlot)
            {
                bool targetIsActive = this.slotType == SlotType.ActiveMod;
                if (targetIsActive && !source.modInstance.IsActive) return;
                if (!targetIsActive && !source.modInstance.IsPassive) return;
            }

            // Validate mod type if swapping back (occupied target going into source mod slot)
            if (source.IsModSlot && this.modInstance != null)
            {
                bool sourceIsActive = source.slotType == SlotType.ActiveMod;
                if (sourceIsActive && !this.modInstance.IsActive) return;
                if (!sourceIsActive && !this.modInstance.IsPassive) return;
            }

            source.CleanupDrag();

            ModInstance srcMod = source.modInstance;
            ModInstance dstMod = this.modInstance;

            RemoveFromSlot(source);
            RemoveFromSlot(this);

            PlaceInSlot(this, srcMod);
            PlaceInSlot(source, dstMod);
        }

        /// <summary>Remove whatever mod is in this slot from its backing store.</summary>
        private void RemoveFromSlot(ModSlotUI slot)
        {
            if (slot.modInstance == null) return;

            if (slot.slotType == SlotType.Inventory)
            {
                slot.inventory?.RemoveMod(slot.modInstance);
            }
            else
            {
                bool isActive = slot.slotType == SlotType.ActiveMod;
                slot.modManager?.UnequipMod(isActive, slot.slotIndex);
            }
        }

        /// <summary>Place a mod into this slot's backing store.</summary>
        private void PlaceInSlot(ModSlotUI slot, ModInstance mod)
        {
            if (mod == null) return;

            if (slot.slotType == SlotType.Inventory)
            {
                slot.inventory?.InsertMod(mod, slot.slotIndex);
            }
            else
            {
                bool isActive = slot.slotType == SlotType.ActiveMod;
                slot.modManager?.EquipModAt(mod, isActive, slot.slotIndex);
            }
        }

        #endregion

        #region Cleanup

        private void OnDisable()
        {
            if (draggedSlot == this) CleanupDrag();
            if (selectedSlot == this) ClearSelection();
        }

        private void OnDestroy()
        {
            if (draggedSlot == this) CleanupDrag();
            if (selectedSlot == this) ClearSelection();
        }

        #endregion
    }
}