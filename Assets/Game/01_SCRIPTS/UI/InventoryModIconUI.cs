using UnityEngine;
using UnityEngine.UI;

namespace junklite
{
    public class InventoryModIconUI : MonoBehaviour
    {
        [SerializeField] private Image icon;

        private Mod_Data mod;
        private InventoryComponent inventory;

        public void Bind(Mod_Data data, InventoryComponent inv)
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
