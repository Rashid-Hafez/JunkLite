using UnityEngine;

namespace junklite
{
    public class InventoryModsUI : MonoBehaviour
    {
        [Header("UI Parent for Mod Icons")]
        [SerializeField] private Transform contentParent;

        [Header("Prefab for One Inventory Icon")]
        [SerializeField] private InventoryModIconUI iconPrefab;

        private InventoryComponent inventory;

        public void Bind(InventoryComponent inv)
        {
            // Unbind previous just in case
            Unbind();

            inventory = inv;
            inventory.OnInventoryChanged += Refresh;
            Refresh();
        }

        public void Unbind()
        {
            if (inventory != null)
                inventory.OnInventoryChanged -= Refresh;

            Clear();
            inventory = null;
        }

        private void Refresh()
        {
            Clear();

            foreach (var mod in inventory.StoredMods)
            {
                var icon = Instantiate(iconPrefab, contentParent);
                icon.Bind(mod, inventory);
            }
        }

        private void Clear()
        {
            foreach (Transform t in contentParent)
                Destroy(t.gameObject);
        }
    }
}