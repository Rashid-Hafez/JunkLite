using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;

namespace junklite
{
    public class ModSlotUI : MonoBehaviour, IBeginDragHandler, IDragHandler, IEndDragHandler, IDropHandler
    {
        [Header("UI References")]
        [SerializeField] private Image iconImage;
        [SerializeField] private Image durabilityFill;

        // Data
        private ActiveMod activeMod;
        private WeaponInstance weapon;
        private InventoryComponent inventory;
        private int slotIndex;
        private bool isWeaponSlot;

        // Drag state
        private static ModSlotUI draggedSlot;
        private static GameObject dragIcon;
        private static Canvas rootCanvas;

        public bool IsEmpty => activeMod == null;
        public ActiveMod ActiveMod => activeMod;
        public bool IsWeaponSlot => isWeaponSlot;
        public int SlotIndex => slotIndex;

        // -----------------------------------------------------------------------
        // BINDING
        // -----------------------------------------------------------------------

        /// <summary>
        /// Bind as weapon mod slot.
        /// </summary>
        public void Bind(ActiveMod mod, WeaponInstance weaponInstance, InventoryComponent inv, int index)
        {
            activeMod = mod;
            weapon = weaponInstance;
            inventory = inv;
            slotIndex = index;
            isWeaponSlot = true;

            UpdateDisplay();
        }

        /// <summary>
        /// Bind as inventory slot.
        /// </summary>
        public void Bind(ActiveMod mod, InventoryComponent inv, int index)
        {
            activeMod = mod;
            weapon = null;
            inventory = inv;
            slotIndex = index;
            isWeaponSlot = false;

            UpdateDisplay();
        }

        private void UpdateDisplay()
        {
            if (iconImage != null)
            {
                if (activeMod != null && activeMod.data != null && activeMod.data.icon != null)
                {
                    iconImage.sprite = activeMod.data.icon;
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
            if (activeMod != null && durabilityFill != null)
                durabilityFill.fillAmount = activeMod.DurabilityPercent;
        }

        private void UpdateDurabilityBar()
        {
            if (durabilityFill == null)
                return;

            if (activeMod != null)
            {
                durabilityFill.gameObject.SetActive(true);
                durabilityFill.fillAmount = activeMod.DurabilityPercent;
            }
            else
            {
                durabilityFill.gameObject.SetActive(false);
            }
        }

        // -----------------------------------------------------------------------
        // DRAG
        // -----------------------------------------------------------------------

        public void OnBeginDrag(PointerEventData eventData)
        {
            if (IsEmpty)
            {
                eventData.pointerDrag = null;
                return;
            }

            draggedSlot = this;

            if (rootCanvas == null)
                rootCanvas = GetComponentInParent<Canvas>()?.rootCanvas;

            if (rootCanvas == null)
                return;

            // Create drag icon
            dragIcon = new GameObject("DragIcon");
            dragIcon.transform.SetParent(rootCanvas.transform, false);

            var img = dragIcon.AddComponent<Image>();
            img.sprite = activeMod.data.icon;
            img.raycastTarget = false;

            var rt = dragIcon.GetComponent<RectTransform>();
            rt.sizeDelta = new Vector2(64, 64);

            // Dim original
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
            if (dragIcon == null || rootCanvas == null)
                return;

            RectTransformUtility.ScreenPointToLocalPointInRectangle(
                rootCanvas.GetComponent<RectTransform>(),
                eventData.position,
                rootCanvas.worldCamera,
                out Vector2 pos);

            dragIcon.GetComponent<RectTransform>().anchoredPosition = pos;
        }

        private void CleanupDrag()
        {
            // Restore icon color
            if (iconImage != null)
                iconImage.color = Color.white;

            // Destroy drag icon
            if (dragIcon != null)
            {
                Destroy(dragIcon);
                dragIcon = null;
            }

            draggedSlot = null;
        }

        // -----------------------------------------------------------------------
        // DROP
        // -----------------------------------------------------------------------

        public void OnDrop(PointerEventData eventData)
        {
            if (draggedSlot == null || draggedSlot == this || draggedSlot.IsEmpty)
                return;

            if (inventory == null)
                return;

            // Cache the source before any operations
            ModSlotUI source = draggedSlot;

            // Perform the drop
            PerformDrop(source);

            // Cleanup drag - check if source still exists
            if (source != null)
                source.CleanupDrag();
        }

        private void PerformDrop(ModSlotUI source)
        {
            // Inventory -> Weapon
            if (!source.IsWeaponSlot && this.IsWeaponSlot)
            {
                InventoryToWeapon(source);
            }
            // Weapon -> Inventory
            else if (source.IsWeaponSlot && !this.IsWeaponSlot)
            {
                WeaponToInventory(source);
            }
            // Inventory -> Inventory
            else if (!source.IsWeaponSlot && !this.IsWeaponSlot)
            {
                SwapInventorySlots(source);
            }
        }

        private void InventoryToWeapon(ModSlotUI source)
        {
            if (source.ActiveMod == null || weapon == null)
                return;

            ActiveMod sourceMod = source.ActiveMod;
            ActiveMod existingWeaponMod = this.activeMod;

            // 1. Remove source mod from inventory
            inventory.RemoveModAt(source.SlotIndex);

            // 2. If weapon slot has a mod, move it to inventory at source position
            if (existingWeaponMod != null)
            {
                weapon.RemoveMod(existingWeaponMod);
                inventory.InsertModAt(source.SlotIndex, existingWeaponMod);
            }

            // 3. Add source mod to weapon
            weapon.TryAddActiveMod(sourceMod);
        }

        private void WeaponToInventory(ModSlotUI source)
        {
            if (source.ActiveMod == null || source.weapon == null)
                return;

            ActiveMod sourceMod = source.ActiveMod;
            ActiveMod existingInventoryMod = this.activeMod;

            // 1. Remove source mod from weapon
            source.weapon.RemoveMod(sourceMod);

            // 2. If inventory slot has a mod, move it to weapon
            if (existingInventoryMod != null)
            {
                inventory.RemoveModAt(this.SlotIndex);
                source.weapon.TryAddActiveMod(existingInventoryMod);
            }

            // 3. Add source mod to inventory at target position
            inventory.InsertModAt(this.SlotIndex, sourceMod);
        }

        private void SwapInventorySlots(ModSlotUI source)
        {
            if (source.SlotIndex < 0 || this.SlotIndex < 0)
                return;

            inventory.SwapMods(source.SlotIndex, this.SlotIndex);
        }

        // -----------------------------------------------------------------------
        // CLEANUP
        // -----------------------------------------------------------------------

        private void OnDisable()
        {
            if (draggedSlot == this)
                CleanupDrag();
        }

        private void OnDestroy()
        {
            if (draggedSlot == this)
                CleanupDrag();
        }
    }
}