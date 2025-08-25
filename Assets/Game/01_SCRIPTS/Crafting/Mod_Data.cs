using UnityEngine;

/// <summary>
/// This class holds all the data for a mod. We attach this scriptable object to a mod asset.
/// </summary>
[CreateAssetMenu(menuName = "Junklite/Mod")]
public class Mod_Data : ScriptableObject
{
    public string modId;
    public string displayName;
    [TextArea] public string description;

    public enum ModElement
    {
        Dull, Fire, Ice, Electric, Plasma
    }

    [Header("Element")]
    public ModElement element;

    public enum Rarity
    {
        Common, Uncommon, Rare, Legendary
    }

    [Header("Rarity")]
    public Rarity rarity;

    [Header("Stats")]
    public float damageBonus;
    public float attackSpeedMult = 1f;
    public float durabilityCostPerHit = 1f;   // how fast THIS mod burns
    public float maxModDurability = 20f;      // per-instance cap
    public float DPS = 0f;

    [Header("FX")]
    public Sprite icon;
    public GameObject vfxPrefab;
  
}
