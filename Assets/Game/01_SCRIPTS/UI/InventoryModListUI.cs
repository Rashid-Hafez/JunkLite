using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Displays the inventory of mods in reserve (not equipped).
/// Players can click on mods to equip them to the weapon.
/// </summary>
public class InventoryModListUI : MonoBehaviour
{
    [SerializeField] private Transform modListContainer;    // Parent for mod item prefabs
    [SerializeField] private GameObject modItemPrefab;      // InventoryModItemUI prefab
    [SerializeField] private LayoutGroup layoutGroup;       // VerticalLayoutGroup for scrolling

    private InventoryComponent inventory;
    private List<InventoryModItemUI> modItems = new List<InventoryModItemUI>();

    public void Initialize(InventoryComponent inv)
    {
        inventory = inv;
        inventory.OnInventoryChanged += RefreshDisplay;
        RefreshDisplay();
    }

    private void RefreshDisplay()
    {
        // Clear old items
        foreach (InventoryModItemUI item in modItems)
            Destroy(item.gameObject);
        modItems.Clear();

        // Get mods in reserve
        List<Mod_Data> modsInReserve = inventory.GetModsInReserve();

        // Create UI item for each mod
        foreach (Mod_Data mod in modsInReserve)
        {
            GameObject itemObj = Instantiate(modItemPrefab, modListContainer);
            InventoryModItemUI item = itemObj.GetComponent<InventoryModItemUI>();
            item.SetMod(mod, inventory);
            modItems.Add(item);
        }

        // Refresh layout
        if (layoutGroup != null)
            LayoutRebuilder.ForceRebuildLayoutImmediate((RectTransform)layoutGroup.transform);

        // Show message if empty
        if (modsInReserve.Count == 0 && modListContainer.childCount > 0)
        {
            Debug.Log("No mods in inventory");
        }
    }

    void OnDestroy()
    {
        if (inventory != null)
            inventory.OnInventoryChanged -= RefreshDisplay;
    }
}
