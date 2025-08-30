using System.Collections.Generic;
using UnityEngine;

public class DropTableGameObj : MonoBehaviour
{
    public static DropTableGameObj Instance; //make class singleton and only one instance

    [Header("Drop Table")]
    public List<Mod_Data> allMods;

    [Header ("Rarity Drop Chances")]
    [SerializeField]
    [Range(0f, 1f)] public float commonChance = 0.7f;
    [SerializeField]
    [Range(0f, 1f)] public float uncommonChance = 0.2f;
    [SerializeField]
    [Range(0f, 1f)] public float rareChance = 0.09f;
    [SerializeField]
    [Range(0f, 1f)] public float mythicChance = 0.01f;

    void Awake()
    {
        if (Instance == null)
        {
            Instance = this;
            DontDestroyOnLoad(gameObject);
        }
        else if (Instance != this)
        {
            Debug.LogWarning("Duplicate DropTableManager found, destroying this one!");
            Destroy(gameObject); //destroy other instances
        }
    }

/// <summary>
    /// Gets a random mod from the drop table based on the defined rarity chances.
    /// rolls a random number and when the number is hit we see which chance range its falls into
    /// Calls another method @GetRandomModOfRarity
    /// </summary>
    /// <returns></returns>
    public Mod_Data GetRandomDrop()
    {
        Mod_Data dropData;
        float roll = Random.Range(0f, 1f);

        if (roll < commonChance)
        {
            dropData = GetRandomModOfRarity(Mod_Data.Rarity.Common);
        }
        else if (roll < commonChance + uncommonChance)
        {
            dropData = GetRandomModOfRarity(Mod_Data.Rarity.Uncommon);
        }
        else if (roll < commonChance + uncommonChance + rareChance)
        {
            dropData = GetRandomModOfRarity(Mod_Data.Rarity.Rare);
        }
        else
        {
            dropData = GetRandomModOfRarity(Mod_Data.Rarity.Legendary);
        }

        return dropData;
    }

/// <summary>
/// Gets a random mod of the specified rarity.
/// Searches the whole mod list for mods of the specified rarity.
/// Puts them in a "FilteredMods" list and returns a random one from the new list.
/// </summary>
/// <param name="rarity"></param>
/// <returns>Mod_Data</returns>
    private Mod_Data GetRandomModOfRarity(Mod_Data.Rarity rarity)
    {
        List<Mod_Data> filteredMods = new List<Mod_Data>();
        foreach (var mod in allMods)
        {
            if (mod.rarity == rarity)
            {
                filteredMods.Add(mod);
            }
        }
        int rando = Random.Range(0, filteredMods.Count);
        Mod_Data randomMod = filteredMods[Mathf.Abs(rando)];
        return randomMod;
    }
}
