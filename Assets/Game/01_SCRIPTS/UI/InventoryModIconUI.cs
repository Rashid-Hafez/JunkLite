using UnityEngine;
using UnityEngine.UI;

namespace junklite
{
    public class InventoryModIconUI : MonoBehaviour
    {
        [SerializeField] private Image icon;

        private ModData mod;
        private InventoryComponent inventory;

        public void Bind(ModData data, InventoryComponent inv)
        {
            mod = data;
            inventory = inv;

            icon.sprite = data.icon;
        }

        public void OnClick()
        {
            inventory.EquipMod(mod);
        }
    }
}
