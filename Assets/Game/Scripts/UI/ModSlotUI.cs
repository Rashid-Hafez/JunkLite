using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using System;
using TMPro;

namespace junklite
{
    public class ModSlotUI : MonoBehaviour,
        IBeginDragHandler, IDragHandler, IEndDragHandler, IDropHandler,
        IPointerClickHandler, IPointerEnterHandler, IPointerExitHandler
    {
        #region Fields

        [Header("UI References")]
        [SerializeField] private Image iconImage;
        [SerializeField] private Image durabilityFill;
        [SerializeField] private Image backgroundImage;
        [SerializeField] private Image crossIcon;
        [SerializeField] private Image highlightImage;
        [SerializeField] private GameObject hoverImage;
        [SerializeField] private TMP_Text inputHintText;

        // Data
        private ModInstance modInstance;
        private InventoryComponent inventory;
        private ModManager modManager;
        private int slotIndex;
        private SlotType slotType;
        private bool isLocked;

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

        #region Events

        public static event Action<ModInstance> OnModSelected;
        public static event Action<ModInstance> OnModHovered;
        public static event Action OnModHoverExit;

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

        public void Bind(ModInstance mod, ModManager manager, InventoryComponent inv, int index, bool isActiveMod, bool locked = false)
        {
            modInstance = mod;
            modManager = manager;
            inventory = inv;
            slotIndex = index;
            slotType = isActiveMod ? SlotType.ActiveMod : SlotType.PassiveMod;
            isLocked = locked;

            if (inputHintText != null)
            {
                if (isActiveMod && GameInputManager.Instance != null)
                    inputHintText.text = GameInputManager.Instance.GetModActivateHint(index);
                else
                    inputHintText.text = "";
            }

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
                crossIcon.enabled = isLocked;
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

        private void UpdateOverlays()
        {
            ModSlotUI source = draggedSlot != null ? draggedSlot : selectedSlot;

            if (crossIcon != null)
            {
                bool showCross = isLocked;
                if (!isLocked && source != null && source != this && IsModSlot)
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

            if (highlightImage != null)
            {
                bool showHighlight = false;
                if (!isLocked && selectedSlot != null && selectedSlot != this)
                    showHighlight = IsValidTargetFor(selectedSlot);
                highlightImage.enabled = showHighlight;
            }
        }

        private bool IsValidTargetFor(ModSlotUI source)
        {
            if (source == null || source.modInstance == null) return false;
            if (isLocked) return false;

            ModInstance srcMod = source.modInstance;

            if (!IsModSlot) return true;

            if (slotType == SlotType.ActiveMod && !srcMod.IsActive) return false;
            if (slotType == SlotType.PassiveMod && !srcMod.IsPassive) return false;

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

        #region Hover

        public void OnPointerEnter(PointerEventData eventData)
        {
            if (hoverImage != null) hoverImage.SetActive(true);
            if (modInstance == null) return;
            OnModHovered?.Invoke(modInstance);
        }

        public void OnPointerExit(PointerEventData eventData)
        {
            if (hoverImage != null) hoverImage.SetActive(false);
            OnModHoverExit?.Invoke();
        }

        #endregion

        #region Click-to-Place

        public void OnPointerClick(PointerEventData eventData)
        {
            if (draggedSlot != null) return;

            if (selectedSlot == null)
            {
                if (!IsEmpty)
                {
                    selectedSlot = this;
                    if (iconImage != null)
                        iconImage.color = new Color(1, 1, 1, 0.5f);

                    OnModSelected?.Invoke(modInstance);
                }
                return;
            }

            if (selectedSlot == this)
            {
                ClearSelection();
                OnModSelected?.Invoke(null);
                return;
            }

            if (IsValidTargetFor(selectedSlot))
            {
                ModSlotUI source = selectedSlot;
                ClearSelection();
                OnModSelected?.Invoke(null);

                ModInstance srcMod = source.modInstance;
                ModInstance dstMod = this.modInstance;

                RemoveFromSlot(source);
                RemoveFromSlot(this);

                PlaceInSlot(this, srcMod);
                PlaceInSlot(source, dstMod);
            }
            else
            {
                ClearSelection();
                OnModSelected?.Invoke(null);
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
            ClearSelection();
            OnModSelected?.Invoke(null);

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

            if (isLocked) return;

            ModSlotUI source = draggedSlot;

            if (this.IsModSlot)
            {
                bool targetIsActive = this.slotType == SlotType.ActiveMod;
                if (targetIsActive && !source.modInstance.IsActive) return;
                if (!targetIsActive && !source.modInstance.IsPassive) return;
            }

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

        private void RemoveFromSlot(ModSlotUI slot)
        {
            if (slot.modInstance == null) return;

            if (slot.slotType == SlotType.Inventory)
                slot.inventory?.RemoveMod(slot.modInstance);
            else
            {
                bool isActive = slot.slotType == SlotType.ActiveMod;
                slot.modManager?.UnequipMod(isActive, slot.slotIndex);
            }
        }

        private void PlaceInSlot(ModSlotUI slot, ModInstance mod)
        {
            if (mod == null) return;

            if (slot.slotType == SlotType.Inventory)
                slot.inventory?.InsertMod(mod, slot.slotIndex);
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