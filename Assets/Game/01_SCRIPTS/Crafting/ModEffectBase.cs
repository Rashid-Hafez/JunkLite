using UnityEngine;
using junklite;

public abstract class ModEffectBase : MonoBehaviour
{
    protected WeaponInstance weapon;
    public Mod_Data modData { get; set; }
    
    private float _currentDurability;
    public float CurrentDurability
    {
        get { return _currentDurability; }
        set { _currentDurability = Mathf.Max(0, value); }
    }

    public bool IsBroken => CurrentDurability <= 0;

    public virtual void Initialize(WeaponInstance weapon, Mod_Data modData)
    {
        this.weapon = weapon;
        this.modData = modData;
        this.CurrentDurability = modData.maxModDurability;

        BindEvents();
    }

    protected abstract void BindEvents();

    // All mods consume durability on hit
    protected virtual void OnHit()
    {
        Consume(modData.durabilityCostPerHit);
    }

    protected void Consume(float amount)
    {
        CurrentDurability -= amount;

        if (IsBroken)
            weapon.RemoveMod(this);
    }
    
    public virtual void OnRemove()
    {
        // Optional cleanup logic when the mod is removed
    }
}