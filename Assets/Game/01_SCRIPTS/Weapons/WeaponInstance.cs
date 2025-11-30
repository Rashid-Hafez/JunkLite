using System.Collections.Generic;
using System.ComponentModel;
using junklite;
using NUnit.Framework;
using UnityEngine;

[System.Serializable]
public class WeaponInstance : MonoBehaviour
{

    private WeaponType weaponType;

    [SerializeField]
    private WeaponData weaponData; // NEW: holds base damage

    private WeaponData.Rarity rarity;

    private float baseDamage;
    private float attackSpeed;
    private int maxDurability;

    // Public accessor for weaponData
    public WeaponData WeaponData { get => weaponData; set => weaponData = value; }

    [SerializeField]
    private int _currentDurability;
    public int currentDurability
    {
        get { return _currentDurability; }
        set
        {
            int previousDurability = _currentDurability;
            _currentDurability = Mathf.Clamp(value, 0, maxDurability);
            
            OnDurabilityChanged?.Invoke();
            
            // Check if durability increased or decreased
            if (_currentDurability > previousDurability)
            {
                OnDurabilityIncreased?.Invoke();
            }
            else if (_currentDurability < previousDurability)
            {
                OnDurabilityDecreased?.Invoke();
            }
            
            // Auto-trigger break if durability hits zero or below
            if (_currentDurability <= 0)
            {
                BreakWeapon();
            }
        }
    }

    [SerializeField]
    private List<Mod_Data> mods; // List of equipped mods

    [SerializeField] [Header("Collider")]
    public Collider weaponCollider;

    public event System.Action OnHit;
    public event System.Action OnBlock;
    public event System.Action OnDurabilityChanged;
    public event System.Action OnWeaponBroken;
    public event System.Action OnWeaponRepaired;
    public event System.Action OnWeaponSwing;
    public event System.Action OnWeaponReloaded;
    public event System.Action OnWeaponEquipped;
    public event System.Action OnWeaponUnequipped;
    public event System.Action OnWeaponDropped;
    public event System.Action OnWeaponUpgraded;
    public event System.Action OnParry; // Parry event // come back later, should maybe be called from enemy system
    public event System.Action OnDurabilityIncreased;
    public event System.Action OnDurabilityDecreased;

    [SerializeField]
    private float weaponDurability;

    private List<ModEffectBase> activeEffects = new List<ModEffectBase>();
    
    private Collider lastHitEnemy; // NEW: track which enemy we hit this swing

    void Start()
    {
        weaponDurability = weaponData.maxWeaponDurability;
        mods = new List<Mod_Data>(weaponData.modSlots);
        weaponType = weaponData.type;

        if (GetComponent<Collider>() == null)
        {
            WarningException warning = new WarningException("No Collider found on WeaponInstance: " + gameObject.name);
            weaponCollider = gameObject.AddComponent<BoxCollider>();
            weaponCollider.enabled = false; // Start disabled
        }
         else if (weaponCollider == null)
        {
            weaponCollider = GetComponent<Collider>();
            weaponCollider.enabled = false; // Start disabled
        }

        if (weaponData == null)
        {
            Assert.IsNotNull(weaponData, "WeaponData is not assigned on WeaponInstance: " + gameObject.name);
            Debug.LogError("WeaponData is not assigned on WeaponInstance: " + gameObject.name);
            throw new WarningException("WeaponData is not assigned on WeaponInstance: " + gameObject.name);
        }

        attackSpeed = weaponData.baseAttackSpeed;
        baseDamage = weaponData.baseDamage;
        maxDurability = weaponData.maxWeaponDurability;
        rarity = weaponData.rarity;
    }

    public void RemoveMod(ModEffectBase effect)
    {
        activeEffects.Remove(effect);
        effect.OnRemove();
        Destroy(effect.gameObject);
    }

    public void AddMod(Mod_Data modData)
    {
        // Instantiate the mod effect prefab (the logic lives here)
        ModEffectBase effect = Instantiate(modData.modEffectPrefab, transform); // THIS IS WHERE WE CREATE THE MOD EFFECT PREFAB AND LINK HAVE OUR LOGIC RUN. THIS IS SO IMPORTANT ITS THE MOST IMPORTANT LINE IN THE ENTIRE CODEBASE!!!!
        
        // Initialize it with weapon reference and the mod data blueprint
        effect.Initialize(this, modData);
        
        // Add to active list
        activeEffects.Add(effect);
    }

    // NEW: Calculate total damage from weapon + all active mods
    public float CalculateTotalDamage()
    {
        float totalDamage = weaponData.baseDamage;
        
        foreach (ModEffectBase effect in activeEffects)
        {
            totalDamage += effect.modData.damageBonus;
        }
        
        return totalDamage;
    }

    // NEW: Get the dominant element type (for VFX)
    public Mod_Data.ModElement GetDominantElement()
    {
        if (activeEffects.Count == 0)
            return Mod_Data.ModElement.Dull;
        
        // Return first mod's element (or implement priority logic)
        return activeEffects[0].modData.element;
    }
    
    // Expose active effects for UI and debugging
    public List<ModEffectBase> GetActiveEffects() => activeEffects;

    // NEW: Get equipped mods as Mod_Data (for inventory access)
    public List<Mod_Data> GetEquippedMods()
    {
        List<Mod_Data> equippedModData = new List<Mod_Data>();
        foreach (ModEffectBase effect in activeEffects)
        {
            equippedModData.Add(effect.modData);
        }
        return equippedModData;
    }

    // NEW: Enable collider during swing animation called from anim notify
    public void EnableWeaponCollider()
    {
        weaponCollider.enabled = true;
        lastHitEnemy = null; // Reset for this swing
    }

    // NEW: Disable collider after swing animation
    public void DisableWeaponCollider()
    {
        weaponCollider.enabled = false;
    }

    // NEW: Called by OnTriggerEnter on this weapon's collider
    private void OnTriggerEnter(Collider collision)
    {
        // Prevent hitting same enemy twice in one swing
        if (lastHitEnemy == collision)
            return;

        // Check if it's an enemy
        IDamageable enemy = collision.GetComponent<IDamageable>();
        if (enemy != null)
        {
            lastHitEnemy = collision; // Mark as hit this swing
            
            // Apply damage
            float damage = CalculateTotalDamage();

            Mod_Data.ModElement damageType = GetDominantElement();
            //enemy.TakeDamage(damage, damageType);
            enemy.TakeDamage(new DamageInfo
            {
                Amount = damage,
                Type = (DamageType)damageType // Assuming DamageType enum matches ModElement
            });
            // Fire OnHit event so mods can react
            Hit();
        }
    }

    ///////////////////////////////////////////////////////////////////////////////
    //  Event Invokers
    ///////////////////////////////////////////////////////////////////////////////
    ///
    /// <summary>
    /// Call this method when the weapon hits something.
    /// </summary>
    public void Hit()
    {
        /// deduct durability from weapon and mods here if needed
        OnHit?.Invoke();
    }

    public void Parry()
    {
        OnParry?.Invoke();
    }

    public void BreakWeapon()
    {
        OnWeaponBroken?.Invoke();
    }

    public void RepairWeapon()
    {
        OnWeaponRepaired?.Invoke();
    }

    public void EquipWeapon()
    {
        OnWeaponEquipped?.Invoke();
    }

    public void UnequipWeapon()
    {
        OnWeaponUnequipped?.Invoke();
    }

    public void DropWeapon()
    {
        OnWeaponDropped?.Invoke();
    }

    public void UpgradeWeapon()
    {
        OnWeaponUpgraded?.Invoke();
    }

///////////////////////////////////////////////////////////////////////////////
//  Event Invokers END
///////////////////////////////////////////////////////////////////////////////
}