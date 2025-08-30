using System;
using System.Collections.Generic;
using UnityEngine;

public class EnemyDropManager : MonoBehaviour
{
    public enum DropMode
    {
        GlobalTable, CustomList
    }

    [Header("Drop Settings")][SerializeField] [Tooltip("GlobalTable means it calls the global drop table. CustomList means it only drops from the list below.")]
    public DropMode dropMode = DropMode.GlobalTable; //by def
    [SerializeField] [Tooltip("Set inside array which drops are allowed to drop")]
    public List<Mod_Data> customDrops = new List<Mod_Data>();
    [SerializeField] [Tooltip("Setting this means it only drops of specific rarity type.")]
    public Mod_Data.Rarity RarityManager = Mod_Data.Rarity.Common; //default

    [SerializeField] [Tooltip("Setting this means it only drops of specific element type.")]
    public Mod_Data.ModElement ElementManager = Mod_Data.ModElement.Dull; //default

    /// <summary>
    /// This method checks whether we call the table or the self drop method.
    /// Then it instantiates an instance of a mod that has "Mod_Data"
    /// </summary>
    /// <returns></returns>
    public Mod_Data DropMod()
    {
        if (dropMode == DropMode.GlobalTable)
        {
            Debug.Log("Dropping from Global Table");
            Mod_Data vv = DropTableGameObj.Instance.GetRandomDrop();
            Debug.Log("Dropped Mod: " + vv.displayName + " of Rarity: " + vv.rarity + " and Element: " + vv.element);
            return vv;
        }

        else return DropCustomListMod();
    }

    private Mod_Data DropCustomListMod()
    {
        return new Mod_Data();
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
