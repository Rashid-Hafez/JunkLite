using junklite;
using UnityEngine;

[CreateAssetMenu(menuName = "Junklite/Mod")]
public class Mod_Data : ScriptableObject
{
    public string modId;
    public string displayName;
    public Sprite icon;

    public ModLogic logic;

    [Header("Durability")]
    public float durabilityCostPerHit = 1f;
    public float maxModDurability = 20f;

    [Header("Stats")]
    public float damageBonus;
    public float attackSpeedMult;
}
