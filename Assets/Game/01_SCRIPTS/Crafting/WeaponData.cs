using UnityEngine;

[CreateAssetMenu(fileName = "WeaponData", menuName = "Junklite/WeaponData")]
public class WeaponData : ScriptableObject
{
    public enum Rarity
    {
        Common,
        Uncommon,
        Rare,
        Epic,
        Legendary
    }
    [Header("Core")]
    public Rarity rarity;
    public string weaponId;
    public string displayName;
    public WeaponType type;

    [Header("Base Stats")]
    public float baseAttackSpeed = 1f;
    public int maxWeaponDurability = 100;
    public float baseDamage = 10f; // base damage without any mods applied

    [Header("Progression")]
    public int maxActiveModSlots = 2;   // grows via workbench upgrades (permanent)
    public int maxReserveSlots = 4;     // extra pockets; not directly usable in combat
    
}