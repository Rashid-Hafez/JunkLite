using System.Collections.Generic;
using UnityEngine;

[System.Serializable]
public class WeaponInstance : MonoBehaviour
{
    [SerializeField]
    public WeaponType weaponType;
    [SerializeField]
    private List<Mod_Data> mods;

    [SerializeField]
    private float weaponDurability;
}
