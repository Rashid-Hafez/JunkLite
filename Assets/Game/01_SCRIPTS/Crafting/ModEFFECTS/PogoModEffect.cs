using UnityEngine;
using junklite;
using System;

public class PogoModEffect : ModEffectBase
{
    protected override void BindEvents()
    {
        weapon.OnHit += OnHit;
        weapon.OnWeaponBroken += OnRemove;
        weapon.OnWeaponUnequipped += OnRemove;
        weapon.OnWeaponDropped += OnRemove;
        weapon.OnParry += OnHit;
    }

    protected override void OnHit()
    {
        base.OnHit(); // Consume durability via parent class
        weapon.GetComponent<Rigidbody>().AddForce(Vector3.up * modData.EffectSpecificStrength, ForceMode.Impulse);
        
        // #rashwashere: Consider adding sound or visual effects to enhance feedback
    }

    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
        
    }

    // Update is called once per frame
    void Update()
    {
        
    }
}
