using UnityEngine;
using UnityEngine.UI;

namespace junklite
{
    public class InventoryModIconUI : MonoBehaviour
    {
        [SerializeField] private Image icon;

        private ActiveMod mod;
        private InventoryComponent inventory;

        public void Bind(ActiveMod activeMod, InventoryComponent inv)
        {
            mod = activeMod;
            inventory = inv;

            if (activeMod != null && activeMod.data != null)
                icon.sprite = activeMod.data.icon;
        }

        public void OnClick()
        {
            if (mod != null && inventory != null)
                inventory.EquipMod(mod);
        }
    }
}