using UnityEngine;
using UnityEngine.UI;

namespace junklite
{
    public class ModSlotUI : MonoBehaviour
    {
        [Header("UI References")]
        [SerializeField] private Image iconImage;        // The child "Icon"
        [SerializeField] private Image durabilityFill;   // Optional child under icon

        private ModRuntimeInstance runtime;
        private WeaponInstance weapon;

        public void Bind(ModRuntimeInstance runtimeInstance, WeaponInstance weaponInstance)
        {
            runtime = runtimeInstance;
            weapon = weaponInstance;

            if (runtime == null)
            {
                // Slot is empty → show empty background only
                iconImage.enabled = false;
                if (durabilityFill != null)
                    durabilityFill.gameObject.SetActive(false);
                return;
            }

            // Slot has mod → show mod icon
            iconImage.enabled = true;
            iconImage.sprite = runtime.data.icon;

            // Durability
            if (durabilityFill != null)
            {
                durabilityFill.gameObject.SetActive(true);
                float pct = runtime.durability / runtime.data.maxModDurability;
                durabilityFill.fillAmount = pct;
            }
        }

        // Optional: removing mods later
        public void OnClickRemove()
        {
            if (runtime == null) return;

            var inv = weapon.GetComponentInParent<InventoryComponent>();
            if (inv != null)
                inv.UnequipMod(runtime);
        }
    }
}
