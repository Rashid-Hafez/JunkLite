using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// A single mod item in the inventory list.
/// Shows mod icon, name, and equip button.
/// </summary>
public class InventoryModItemUI : MonoBehaviour
{
    [SerializeField] private Image modIcon;
    [SerializeField] private Text modNameText;
    [SerializeField] private Text modBonusText;          // e.g., "+5 Damage"
    [SerializeField] private Button equipButton;

    private Mod_Data currentMod;
    private InventoryComponent inventory;

    void Start()
    {
        if (equipButton != null)
            equipButton.onClick.AddListener(OnEquipClicked);
    }

    public void SetMod(Mod_Data mod, InventoryComponent inv)
    {
        currentMod = mod;
        inventory = inv;

        // Icon
        if (modIcon != null && mod.icon != null)
        {
            modIcon.sprite = mod.icon;
        }

        // Name
        if (modNameText != null)
        {
            modNameText.text = mod.displayName;
        }

        // Bonus text
        if (modBonusText != null)
        {
            modBonusText.text = $"+{mod.damageBonus} DMG";
        }
    }

    private void OnEquipClicked()
    {
        if (currentMod != null && inventory != null)
        {
            inventory.EquipModToWeapon(currentMod);
        }
    }
}
