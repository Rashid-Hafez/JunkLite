using UnityEngine;
using System.Collections.Generic;
using junklite;

/// <summary>
/// Manages the player's inventory: weapons and mods.
/// </summary>
public class InventoryComponent : MonoBehaviour
{
    // ═══════════════════════════════════════════════════════════════════════════
    // MOD MANAGEMENT
    // ═══════════════════════════════════════════════════════════════════════════

    [SerializeField] private List<Mod_Data> modsInReserve = new List<Mod_Data>();

    // ═══════════════════════════════════════════════════════════════════════════
    // WEAPON MANAGEMENT
    // ═══════════════════════════════════════════════════════════════════════════

    [SerializeField] private WeaponInstance equippedWeapon;
    [SerializeField] private List<WeaponData> weaponsInReserve = new List<WeaponData>();

    // UI references
    private WeaponModDisplayUI weaponModDisplay;
    private InventoryModListUI inventoryModListUI;

    public event System.Action OnInventoryChanged;  // Fired when mods are added/removed/equipped
    public event System.Action OnWeaponChanged;     // Fired when weapon is swapped

    void Start()
    {
        if (equippedWeapon == null)
            equippedWeapon = FindFirstObjectByType<WeaponInstance>();

        weaponModDisplay = FindFirstObjectByType<WeaponModDisplayUI>();
        inventoryModListUI = FindFirstObjectByType<InventoryModListUI>();

        if (weaponModDisplay != null)
            weaponModDisplay.Initialize(equippedWeapon, this);

        if (inventoryModListUI != null)
            inventoryModListUI.Initialize(this);
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // MOD MANAGEMENT METHODS
    // ═══════════════════════════════════════════════════════════════════════════

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
    /// Equip a mod from reserve to the current weapon.
    /// </summary>
    public void EquipModToWeapon(Mod_Data modData)
    {
        if (equippedWeapon == null)
        {
            Debug.LogWarning("No weapon equipped!");
            return;
        }

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
    /// Unequip a mod from the current weapon back to reserve.
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

    // ═══════════════════════════════════════════════════════════════════════════
    // WEAPON MANAGEMENT METHODS
    // ═══════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Equip a new weapon (swap from brass knuckles to pipe, etc).
    /// Stores old weapon in reserve.
    /// </summary>
    public void EquipWeapon(WeaponData newWeaponData)
    {
        if (newWeaponData == null)
        {
            Debug.LogWarning("Cannot equip null weapon!");
            return;
        }

        // Store old weapon data if one is equipped
        if (equippedWeapon != null && equippedWeapon.WeaponData != null)
        {
            weaponsInReserve.Add(equippedWeapon.WeaponData);
            Debug.Log($"✓ Stored {equippedWeapon.WeaponData.name} in reserve");
        }

        // Remove new weapon from reserve if it was there
        if (weaponsInReserve.Contains(newWeaponData))
            weaponsInReserve.Remove(newWeaponData);

        // Unequip old weapon (destroy or hide it)
        if (equippedWeapon != null)
        {
            equippedWeapon.UnequipWeapon();
            Destroy(equippedWeapon.gameObject);
        }

        // Instantiate and equip new weapon
        // TODO: Create weapon instance from prefab
        // For now, assume it's already in the scene:
        equippedWeapon.WeaponData = newWeaponData;
        equippedWeapon.EquipWeapon();

        Debug.Log($"✓ Equipped {newWeaponData.displayName}");
        OnWeaponChanged?.Invoke();
        OnInventoryChanged?.Invoke(); // Refresh UI
    }

    /// <summary>
    /// Unequip current weapon and store it in reserve.
    /// </summary>
    public void UnequipWeapon()
    {
        if (equippedWeapon == null)
        {
            Debug.LogWarning("No weapon equipped!");
            return;
        }

        weaponsInReserve.Add(equippedWeapon.WeaponData);
        equippedWeapon.UnequipWeapon();
        Destroy(equippedWeapon.gameObject);
        equippedWeapon = null;

        Debug.Log("✓ Weapon unequipped and stored in reserve");
        OnWeaponChanged?.Invoke();
        OnInventoryChanged?.Invoke();
    }

    /// <summary>
    /// Get the currently equipped weapon.
    /// </summary>
    public WeaponInstance GetEquippedWeapon() => equippedWeapon;

    /// <summary>
    /// Get all weapons in reserve.
    /// </summary>
    public List<WeaponData> GetWeaponsInReserve() => weaponsInReserve;

    /// <summary>
    /// Pick up a weapon from the world.
    /// </summary>
    public void PickupWeapon(WeaponData weaponData)
    {
        weaponsInReserve.Add(weaponData);
        Debug.Log($"✓ Picked up weapon: {weaponData.displayName}");
        OnWeaponChanged?.Invoke();
    }
}
