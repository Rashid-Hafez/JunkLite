using UnityEngine;
using UnityEngine.UI;
using System.Collections.Generic;

namespace junklite
{
    public class InventoryUI : MonoBehaviour
    {
        [Header("Mod Slot Prefab")]
        [SerializeField] private GameObject modSlotPrefab;

        [Header("Inventory Slots")]
        [SerializeField] private Transform inventorySlotParent;

        [Header("Weapon Display")]
        [SerializeField] private Image weaponIconImage;

        [Header("Weapon Mod Slots")]
        [SerializeField] private Transform weaponSlotParent;

        private InventoryComponent inventory;
        private WeaponManager weaponManager;
        private WeaponInstance subscribedWeapon;

        private readonly List<ModSlotUI> inventorySlots = new();
        private readonly List<ModSlotUI> weaponSlots = new();
        private Transform[] slotTransforms;

       

        public void Bind(InventoryComponent inv, WeaponManager wm)
        {
            Unbind();

            inventory = inv;
            weaponManager = wm;

            CacheSlotTransforms();

            if (inventory != null)
                inventory.OnInventoryChanged += RefreshInventory;

            if (weaponManager != null)
            {
                weaponManager.OnWeaponChanged += RefreshWeapon;
                SubscribeToWeaponMods();
            }

            RefreshAll();
        }

        public void Unbind()
        {
            if (inventory != null)
                inventory.OnInventoryChanged -= RefreshInventory;

            if (weaponManager != null)
                weaponManager.OnWeaponChanged -= RefreshWeapon;

            UnsubscribeFromWeaponMods();

            ClearSlots(inventorySlots);
            ClearSlots(weaponSlots);

            if (weaponIconImage != null)
                weaponIconImage.enabled = false;

            inventory = null;
            weaponManager = null;
        }

        public void RefreshAll()
        {
            RefreshInventory();
            RefreshWeapon();
        }

        // -----------------------------------------------------------------------

        private void CacheSlotTransforms()
        {
            if (inventorySlotParent == null)
            {
                slotTransforms = new Transform[0];
                return;
            }

            int count = inventorySlotParent.childCount;
            slotTransforms = new Transform[count];

            for (int i = 0; i < count; i++)
                slotTransforms[i] = inventorySlotParent.GetChild(i);
        }

        private void RefreshInventory()
        {
            ClearSlots(inventorySlots);

            if (inventory == null || slotTransforms == null)
                return;

            var mods = inventory.StoredMods;

            for (int i = 0; i < slotTransforms.Length; i++)
            {
                ActiveMod mod = (i < mods.Count) ? mods[i] : null;
                ModSlotUI slot = SpawnSlot(slotTransforms[i]);

                if (slot != null)
                {
                    slot.Bind(mod, inventory, i);
                    inventorySlots.Add(slot);
                }
            }
        }

        private void RefreshWeapon()
        {
            // Weapon icon
            if (weaponIconImage != null)
            {
                var weapon = weaponManager?.CurrentWeapon;
                if (weapon != null)
                {
                    var sr = weapon.GetComponent<SpriteRenderer>();
                    weaponIconImage.sprite = sr != null ? sr.sprite : null;
                    weaponIconImage.enabled = weaponIconImage.sprite != null;
                }
                else
                {
                    weaponIconImage.enabled = false;
                }
            }

            RefreshWeaponMods();
            SubscribeToWeaponMods();
        }

        private void RefreshWeaponMods()
        {
            ClearSlots(weaponSlots);

            var weapon = weaponManager?.CurrentWeapon;
            if (weapon == null || weaponSlotParent == null)
                return;

            int maxSlots = weapon.MaxModSlots;
            var activeMods = weapon.GetMods();

            for (int i = 0; i < maxSlots; i++)
            {
                ModSlotUI slot = SpawnSlot(weaponSlotParent);
                if (slot == null)
                    continue;

                ActiveMod mod = (i < activeMods.Count) ? activeMods[i] : null;
                slot.Bind(mod, weapon, inventory, i);

                weaponSlots.Add(slot);
            }
        }

        // -----------------------------------------------------------------------

        private ModSlotUI SpawnSlot(Transform parent)
        {
            if (modSlotPrefab == null)
                return null;

            GameObject obj = Instantiate(modSlotPrefab, parent);

            // Center in parent
            RectTransform rt = obj.GetComponent<RectTransform>();
            if (rt != null)
            {
                rt.anchorMin = new Vector2(0.5f, 0.5f);
                rt.anchorMax = new Vector2(0.5f, 0.5f);
                rt.pivot = new Vector2(0.5f, 0.5f);
                rt.anchoredPosition = Vector2.zero;
            }

            return obj.GetComponent<ModSlotUI>();
        }

        private void ClearSlots(List<ModSlotUI> slots)
        {
            foreach (var slot in slots)
            {
                if (slot != null)
                    Destroy(slot.gameObject);
            }
            slots.Clear();
        }

        // -----------------------------------------------------------------------

        private void SubscribeToWeaponMods()
        {
            UnsubscribeFromWeaponMods();

            if (weaponManager?.CurrentWeapon != null)
            {
                subscribedWeapon = weaponManager.CurrentWeapon;
                subscribedWeapon.OnModsChanged += RefreshWeaponMods;
            }
        }

        private void UnsubscribeFromWeaponMods()
        {
            if (subscribedWeapon != null)
            {
                subscribedWeapon.OnModsChanged -= RefreshWeaponMods;
                subscribedWeapon = null;
            }
        }

        private void OnDisable() => UnsubscribeFromWeaponMods();
        private void OnDestroy() => Unbind();
    }
}