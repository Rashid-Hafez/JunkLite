using UnityEngine;

public class ModDrop_Instance : MonoBehaviour
{
    public Mod_Data modData;
    public float CurrentDurability;
    public bool IsBroken => CurrentDurability <= 0;


    void Start()
    {
        if (modData != null)
        {
            // Apply the mod data to the instance
            // For example:
            // this.damage += modData.damageBonus;
            // this.attackSpeed *= modData.attackSpeedMult;
        }
    }

    public ModDrop_Instance(Mod_Data data)
    {
        modData = data;
        CurrentDurability = data.maxModDurability;
    }

    public void ConsumeDurability(float amount)
    {
        CurrentDurability = Mathf.Max(0, CurrentDurability - amount);
    }

}
