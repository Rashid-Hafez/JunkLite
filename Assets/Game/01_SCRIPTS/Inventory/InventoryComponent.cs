using UnityEngine;
using System.Collections.Generic;
using junklite;

/// <summary>
/// Manages the player's mod inventory: reserve mods and equipped mods on the weapon.
/// </summary>
public class InventoryComponent : MonoBehaviour
{
    [SerializeField] private WeaponInstance equippedWeapon;

    // Mods in reserve (not equipped)
    private List<Mod_Data> modsInReserve = new List<Mod_Data>();

    // UI references
    private WeaponModDisplayUI weaponModDisplay;
    private InventoryModListUI inventoryModListUI;

    public event System.Action OnInventoryChanged;  // Fired when mods are added/removed/equipped

    void Start()
    {
        if (equippedWeapon == null)
            equippedWeapon = FindObjectOfType<WeaponInstance>();

        weaponModDisplay = FindObjectOfType<WeaponModDisplayUI>();
        inventoryModListUI = FindObjectOfType<InventoryModListUI>();

        if (weaponModDisplay != null)
            weaponModDisplay.Initialize(equippedWeapon, this);

        if (inventoryModListUI != null)
            inventoryModListUI.Initialize(this);
    }

    /// <summary>
    /// Player picks up a mod from the world.
    /// </summary>
    public void PickupMod(Mod_Data modData)
    {
        modsInReserve.Add(modData);
        Debug.Log($"✓ Picked up mod: {modData.displayName}");
        OnInventoryChanged?.Invoke();
    }

    /// <summary>
    /// Equip a mod from reserve to the weapon.
    /// </summary>
    public void EquipModToWeapon(Mod_Data modData)
    {
        if (!modsInReserve.Contains(modData))
        {
            Debug.LogWarning($"Mod {modData.displayName} not in reserve!");
            return;
        }

        // Check if weapon has slot space
        if (equippedWeapon.GetEquippedMods().Count >= equippedWeapon.WeaponData.modSlots)
        {
            Debug.LogWarning("Weapon mod slots are full!");
            return;
        }

        modsInReserve.Remove(modData);
        equippedWeapon.AddMod(modData);

        Debug.Log($"✓ Equipped {modData.displayName} to weapon");
        OnInventoryChanged?.Invoke();
    }

    /// <summary>
    /// Unequip a mod from the weapon back to reserve.
    /// </summary>
    public void UnequipModFromWeapon(ModEffectBase effect)
    {
        modsInReserve.Add(effect.modData);
        equippedWeapon.RemoveMod(effect);

        Debug.Log($"✓ Unequipped {effect.modData.displayName} from weapon");
        OnInventoryChanged?.Invoke();
    }

    /// <summary>
    /// Get all mods in reserve (for inventory display).
    /// </summary>
    public List<Mod_Data> GetModsInReserve() => modsInReserve;

    /// <summary>
    /// Get the equipped weapon.
    /// </summary>
    public WeaponInstance GetEquippedWeapon() => equippedWeapon;
}
