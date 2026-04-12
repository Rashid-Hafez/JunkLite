using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using System;

namespace junklite
{
    public class InventoryWeaponSlotUI : MonoBehaviour,
        IBeginDragHandler, IDragHandler, IEndDragHandler, IDropHandler,
        IPointerClickHandler
    {
        #region Fields

        [Header("UI References")]
        [SerializeField] private Image iconImage;
        [SerializeField] private Image durabilityFill;
        [SerializeField] private Image highlightImage;

        private WeaponManager weaponManager;
        private int slotIndex; // 1 or 2

        // Drag state
        private static InventoryWeaponSlotUI draggedSlot;
        private static GameObject dragIcon;
        private static Canvas rootCanvas;

        // Click-to-swap state
        private static InventoryWeaponSlotUI selectedSlot;

        #endregion

        #region Events

        /// <summary>
        /// Fired when a weapon slot is clicked. Passes null when deselected.
        /// InventoryUI subscribes to this to update the description box.
        /// </summary>
        public static event Action<WeaponInstance> OnWeaponSelected;

        #endregion

        #region Properties

        public bool IsEmpty => GetWeapon() == null;
        public int SlotIndex => slotIndex;

        #endregion

        #region Bind / Unbind

        public void Bind(WeaponManager manager, int slot)
        {
            weaponManager = manager;
            slotIndex = slot;
            Refresh();
        }

        public void Unbind()
        {
            if (draggedSlot == this) CleanupDrag();
            if (selectedSlot == this) ClearSelection();

            weaponManager = null;
            slotIndex = 0;

            if (iconImage != null) { iconImage.enabled = false; iconImage.sprite = null; iconImage.color = Color.white; }
            if (durabilityFill != null) durabilityFill.enabled = false;
            if (highlightImage != null) highlightImage.enabled = false;
        }

        public void Refresh()
        {
            var weapon = GetWeapon();
            bool hasWeapon = weapon != null && weapon.weaponData != null;

            if (iconImage != null)
            {
                iconImage.enabled = hasWeapon && weapon.weaponData.icon != null;
                if (iconImage.enabled)
                    iconImage.sprite = weapon.weaponData.icon;
            }

            if (durabilityFill != null)
            {
                durabilityFill.enabled = hasWeapon;
                if (hasWeapon && weapon.MaxDurability > 0f)
                    durabilityFill.fillAmount = weapon.CurrentDurability / weapon.MaxDurability;
            }

            if (highlightImage != null)
            {
                highlightImage.enabled = false;
                highlightImage.raycastTarget = false;
            }
        }

        private WeaponInstance GetWeapon()
        {
            if (weaponManager == null) return null;
            return slotIndex == 1 ? weaponManager.WeaponSlot1 : weaponManager.WeaponSlot2;
        }

        #endregion

        #region Update

        private void Update()
        {
            if (highlightImage != null)
            {
                bool showHighlight = selectedSlot != null
                    && selectedSlot != this
                    && selectedSlot.weaponManager == weaponManager;
                highlightImage.enabled = showHighlight;
            }
        }

        #endregion

        #region Click-to-Swap

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

                    OnWeaponSelected?.Invoke(GetWeapon());
                }
                return;
            }

            if (selectedSlot == this)
            {
                ClearSelection();
                OnWeaponSelected?.Invoke(null);
                return;
            }

            if (selectedSlot.weaponManager == weaponManager)
            {
                ClearSelection();
                OnWeaponSelected?.Invoke(null);
                weaponManager.SwapWeaponSlots();
            }
            else
            {
                ClearSelection();
                OnWeaponSelected?.Invoke(null);
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
            OnWeaponSelected?.Invoke(null);

            if (IsEmpty)
            {
                eventData.pointerDrag = null;
                return;
            }

            draggedSlot = this;

            if (rootCanvas == null)
                rootCanvas = GetComponentInParent<Canvas>()?.rootCanvas;

            if (rootCanvas == null) return;

            var weapon = GetWeapon();

            dragIcon = new GameObject("DragWeaponIcon");
            dragIcon.transform.SetParent(rootCanvas.transform, false);

            var img = dragIcon.AddComponent<Image>();
            img.sprite = weapon.weaponData.icon;
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
            if (draggedSlot == null || draggedSlot == this) return;
            if (weaponManager == null || draggedSlot.weaponManager != weaponManager) return;

            draggedSlot.CleanupDrag();
            weaponManager.SwapWeaponSlots();
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