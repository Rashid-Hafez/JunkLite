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

    protected virtual void BindEvents() { }
}