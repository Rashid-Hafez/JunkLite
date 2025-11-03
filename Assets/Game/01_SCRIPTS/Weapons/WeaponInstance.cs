using System.Collections.Generic;
using UnityEngine;

[System.Serializable]
public class WeaponInstance : MonoBehaviour
{
    [SerializeField]
    public WeaponType weaponType;
    [SerializeField]
    private List<ModDrop_Instance> mods;

    [SerializeField]
    private float weaponDurability;

    [SerializeField]
    public int ModCount = 3;

    /// <summary>
    /// Attach a mod to this weapon instance To be called when crafting or looting a mod or attaching through UI
    /// </summary>
    /// <param name="modData"></param>
    void AttachMod(Mod_Data modData)
    {
            var runtime = new ModDrop_Instance(modData);
            mods.Add(runtime);

            // if mod has a runtime effect component, attach it
            if (modData.modEffectPrefab != null)
            {
                var effect = gameObject.AddComponent(modData.modEffectPrefab.GetType()) as ModEffectBase;
                effect.Initialize(this, runtime);
            }
    }
}
