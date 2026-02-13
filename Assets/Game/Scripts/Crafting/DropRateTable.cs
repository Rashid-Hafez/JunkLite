using System.Collections.Generic;
using junklite;
using UnityEngine;

[CreateAssetMenu(fileName = "DropRateTable", menuName = "Junklite/DropRateTable")]
public class DropRateTable : ScriptableObject
{
    public List<ModData> GlobalMods;

    [Range(0f, 1f)] public float commonRate = 0.7f;
    [Range(0f, 1f)] public float uncommonRate = 0.2f;
    [Range(0f, 1f)] public float rareRate = 0.09f;
    [Range(0f, 0.1f)] public float legendaryRate = 0.01f;

}
