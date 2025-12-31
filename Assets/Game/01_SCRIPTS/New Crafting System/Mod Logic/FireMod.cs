using junklite;
using UnityEngine;

[CreateAssetMenu(menuName = "Junklite/Mods/Fire")]
public class FireModLogic : ModLogic
{
    [Header("Burn Settings")]
    public float burnDamagePerTick = 5f;
    public float tickInterval = 0.5f;
    public float burnDuration = 3f;
    
    public Sprite fireModSprite;
    // Called whenever the weapon hits an enemy
    
    public override void OnHit(WeaponInstance weapon, EnemyCharacter enemy, ref DamageInfo dmg)
    {
        if (enemy != null)
        {
            // Apply burn effect to enemy
            enemy.ApplyStatusEffect(new BurnStatusEffect(burnDamagePerTick, tickInterval, burnDuration), null);
            Debug.LogWarning("enemy detected");
        }
    }

    // Activate weapon VFX when mod is equipped
    public override void OnEquip(WeaponInstance weapon)
    {
       // weapon.EnableModVFX("Fire");
    }

    // Turn off VFX when mod is removed
    public override void OnUnequip(WeaponInstance weapon)
    {
       // weapon.DisableModVFX("Fire");
    }
}
