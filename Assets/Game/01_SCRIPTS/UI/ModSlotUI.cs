using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// A single mod slot in the weapon toolbar.
/// Shows mod icon, durability bar, and unequip button.
/// </summary>
public class ModSlotUI : MonoBehaviour
{
    [SerializeField] private Image modIcon;
    [SerializeField] private Slider durabilityBar;
    [SerializeField] private Text modNameText;        // Optional: show mod name
    [SerializeField] private Button unequipButton;

    private ModEffectBase currentModEffect;
    private InventoryComponent inventory;

    void Start()
    {
        if (unequipButton != null)
            unequipButton.onClick.AddListener(OnUnequipClicked);
    }

    /// <summary>
    /// Display an equipped mod.
    /// </summary>
    public void SetMod(ModEffectBase effect, InventoryComponent inv)
    {
        currentModEffect = effect;
        inventory = inv;

        // Icon
        if (modIcon != null && effect.modData.icon != null)
        {
            modIcon.sprite = effect.modData.icon;
            modIcon.enabled = true;
        }

        // Durability bar
        if (durabilityBar != null)
        {
            durabilityBar.maxValue = effect.modData.maxModDurability;
            durabilityBar.value = effect.CurrentDurability;
            durabilityBar.gameObject.SetActive(true);
        }

        // Name
        if (modNameText != null)
        {
            modNameText.text = effect.modData.displayName;
            modNameText.gameObject.SetActive(true);
        }

        // Button
        if (unequipButton != null)
            unequipButton.gameObject.SetActive(true);
    }

    /// <summary>
    /// Display an empty slot.
    /// </summary>
    public void SetEmpty()
    {
        currentModEffect = null;
        inventory = null;

        if (modIcon != null)
            modIcon.enabled = false;
        if (durabilityBar != null)
            durabilityBar.gameObject.SetActive(false);
        if (modNameText != null)
            modNameText.gameObject.SetActive(false);
        if (unequipButton != null)
            unequipButton.gameObject.SetActive(false);
    }

    /// <summary>
    /// Update durability bar (called after weapon hits).
    /// </summary>
    public void UpdateDurabilityBar()
    {
        if (currentModEffect != null && durabilityBar != null)
            durabilityBar.value = currentModEffect.CurrentDurability;
    }

    private void OnUnequipClicked()
    {
        if (currentModEffect != null && inventory != null)
        {
            inventory.UnequipModFromWeapon(currentModEffect);
        }
    }
}
