using UnityEngine;
using junklite;

public abstract class ModEffectBase : MonoBehaviour
{
    protected WeaponInstance weapon;
    protected ModDrop_Instance runtime;

    public virtual void Initialize(WeaponInstance weapon, ModDrop_Instance runtime)
    {
        this.weapon = weapon;
        this.runtime = runtime;

        BindEvents();
    }

    protected abstract void BindEvents();

    protected void Consume(float amount)
    {
        runtime.ConsumeDurability(amount);

        if (runtime.IsBroken)
            weapon.RemoveMod(this);
    }
}