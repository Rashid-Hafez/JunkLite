using System.Collections.Generic;
using UnityEngine;
using static junklite.ModData;

namespace junklite
{
    [CreateAssetMenu(fileName = "DropTable", menuName = "Junklite/Drop Table")]
    public class DropTable : ScriptableObject
    {
        [Header("All Available Mods")]
        public List<ModData> allMods = new();

        [Header("Rarity Weights")]
        [SerializeField] private float commonWeight = 70f;
        [SerializeField] private float uncommonWeight = 20f;
        [SerializeField] private float rareWeight = 9f;
        [SerializeField] private float legendaryWeight = 1f;

        /// <summary>
        /// Gets a random mod based on rarity weights.
        /// </summary>
        public ModData GetRandomMod()
        {
            if (allMods == null || allMods.Count == 0)
                return null;

            // Single mod? Just return it
            if (allMods.Count == 1)
                return allMods[0];

            // Roll for rarity
            float totalWeight = commonWeight + uncommonWeight + rareWeight + legendaryWeight;
            float roll = Random.Range(0f, totalWeight);

            ModRarity targetRarity;

            if (roll < commonWeight)
                targetRarity = ModRarity.Common;
            else if (roll < commonWeight + uncommonWeight)
                targetRarity = ModRarity.Uncommon;
            else if (roll < commonWeight + uncommonWeight + rareWeight)
                targetRarity = ModRarity.Rare;
            else
                targetRarity = ModRarity.Legendary;

            return GetRandomModOfRarity(targetRarity);
        }

        /// <summary>
        /// Gets a random mod of specific rarity. Falls back to any mod if none found.
        /// </summary>
        public ModData GetRandomModOfRarity(ModRarity rarity)
        {
            if (allMods == null || allMods.Count == 0)
                return null;

            // Filter mods by rarity
            List<ModData> filtered = new();
            foreach (var mod in allMods)
            {
                if (mod != null && mod.rarity == rarity)
                    filtered.Add(mod);
            }

            // If no mods of that rarity, fall back to any mod
            if (filtered.Count == 0)
                return allMods[Random.Range(0, allMods.Count)];

            return filtered[Random.Range(0, filtered.Count)];
        }
    }
}