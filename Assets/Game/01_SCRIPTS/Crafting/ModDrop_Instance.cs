using UnityEngine;

public class ModDrop_Instance : MonoBehaviour
{
    public Mod_Data modData;
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

    // Update is called once per frame
    void Update()
    {
        
    }
}
