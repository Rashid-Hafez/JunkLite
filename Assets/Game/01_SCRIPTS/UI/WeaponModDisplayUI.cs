using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Displays equipped mods on the weapon as a toolbar at the bottom of the screen.
/// Dynamically spawns mod slots based on equippedMods count.
/// </summary>
public class WeaponModDisplayUI : MonoBehaviour
{
    [SerializeField] private Transform modSlotsContainer;  // Parent for mod slot UI prefabs
    [SerializeField] private GameObject modSlotPrefab;      // ModSlotUI prefab
    [SerializeField] private LayoutGroup layoutGroup;       // HorizontalLayoutGroup or VerticalLayoutGroup

    private WeaponInstance equippedWeapon;
    private InventoryComponent inventory;
    private List<ModSlotUI> modSlots = new List<ModSlotUI>();

    public void Initialize(WeaponInstance weapon, InventoryComponent inv)
    {
        equippedWeapon = weapon;
        inventory = inv;

        // Subscribe to changes
        inventory.OnInventoryChanged += RefreshDisplay;
        equippedWeapon.OnHit += RefreshDurabilityBars;

        RefreshDisplay();
    }

    private void RefreshDisplay()
    {
        // Clear old slots
        foreach (ModSlotUI slot in modSlots)
            Destroy(slot.gameObject);
        modSlots.Clear();

        // Get equipped mods
        List<ModEffectBase> equippedEffects = equippedWeapon.GetActiveEffects();
        int totalSlots = equippedWeapon.WeaponData.modSlots;

        // Create slots for each equipped mod + empty slots
        for (int i = 0; i < totalSlots; i++)
        {
            GameObject slotObj = Instantiate(modSlotPrefab, modSlotsContainer);
            ModSlotUI slot = slotObj.GetComponent<ModSlotUI>();

            if (i < equippedEffects.Count)
            {
                // Show equipped mod
                slot.SetMod(equippedEffects[i], inventory);
            }
            else
            {
                // Show empty slot
                slot.SetEmpty();
            }

            modSlots.Add(slot);
        }

        // Refresh layout
        if (layoutGroup != null)
            LayoutRebuilder.ForceRebuildLayoutImmediate((RectTransform)layoutGroup.transform);
    }

    private void RefreshDurabilityBars()
    {
        foreach (ModSlotUI slot in modSlots)
            slot.UpdateDurabilityBar();
    }

    void OnDestroy()
    {
        if (inventory != null)
            inventory.OnInventoryChanged -= RefreshDisplay;
        if (equippedWeapon != null)
            equippedWeapon.OnHit -= RefreshDurabilityBars;
    }
}
