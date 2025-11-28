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

    private List<ModDrop_Instance> activeMods = new List<ModDrop_Instance>();
    private List<ModEffectBase> activeEffects = new List<ModEffectBase>();

    public void RemoveMod(ModEffectBase effect)
    {
        // Remove runtime copy
        ModDrop_Instance runtime = effect.GetType() != null
            ? activeMods.Find(m => m.modData.modEffectPrefab.GetType() == effect.GetType())
            : null;

        if (runtime != null)
            activeMods.Remove(runtime);

        // Cleanup
        activeEffects.Remove(effect);
        Destroy(effect.gameObject);
    }
}
