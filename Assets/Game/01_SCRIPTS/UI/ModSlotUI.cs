using UnityEngine;
using UnityEngine.UI;

namespace junklite
{
    public class ModSlotUI : MonoBehaviour
    {
        [Header("UI References")]
        [SerializeField] private Image iconImage;
        [SerializeField] private Image durabilityFill;

        private ActiveMod activeMod;
        private WeaponInstance weapon;

        public void Bind(ActiveMod mod, WeaponInstance weaponInstance)
        {
            activeMod = mod;
            weapon = weaponInstance;

            if (activeMod == null)
            {
                // Slot is empty → show empty background only
                iconImage.enabled = false;
                if (durabilityFill != null)
                    durabilityFill.gameObject.SetActive(false);
                return;
            }

            // Slot has mod → show mod icon
            iconImage.enabled = true;
            iconImage.sprite = activeMod.data.icon;

            // Durability
            UpdateDurabilityBar();
        }

        private void Update()
        {
            // Update durability bar in real-time
            if (activeMod != null && durabilityFill != null)
            {
                UpdateDurabilityBar();
            }
        }

        private void UpdateDurabilityBar()
        {
            if (durabilityFill == null || activeMod == null)
                return;

            durabilityFill.gameObject.SetActive(true);
            durabilityFill.fillAmount = activeMod.DurabilityPercent;
        }

        // Optional: removing mods later
        public void OnClickRemove()
        {
            if (activeMod == null || weapon == null)
                return;

            var inventory = weapon.GetComponentInParent<InventoryComponent>();
            if (inventory != null)
            {
                inventory.UnequipMod(activeMod);
            }
        }
    }
}