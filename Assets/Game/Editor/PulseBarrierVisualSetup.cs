using UnityEditor;
using UnityEngine;

namespace junklite.Editor
{
    public static class PulseBarrierVisualSetup
    {
        public const string Folder = "Assets/Game/VFX/Pulse Barrier";
        public const string ProfilePath = Folder + "/Pulse Barrier Visual Profile.asset";
        public const string ModPath = "Assets/Game/New Prefabs/Mods/PulseBarrierMod.asset";

        [MenuItem("Tools/JunkLite/VFX/Install Pulse Barrier Arcs")]
        public static void Install()
        {
            if (!AssetDatabase.IsValidFolder("Assets/Game/VFX")) AssetDatabase.CreateFolder("Assets/Game", "VFX");
            if (!AssetDatabase.IsValidFolder(Folder)) AssetDatabase.CreateFolder("Assets/Game/VFX", "Pulse Barrier");
            string materialPath = Folder + "/Pulse Barrier Arcs.mat";
            Material material = AssetDatabase.LoadAssetAtPath<Material>(materialPath);
            if (material == null)
            {
                Shader shader = Shader.Find("JunkLite/VFX/Pulse Barrier Arcs");
                if (shader == null) throw new System.InvalidOperationException("Pulse Barrier Arcs shader has not imported.");
                material = new Material(shader) { name = "Pulse Barrier Arcs" };
                AssetDatabase.CreateAsset(material, materialPath);
            }
            PulseBarrierVisualProfile profile = AssetDatabase.LoadAssetAtPath<PulseBarrierVisualProfile>(ProfilePath);
            if (profile == null)
            {
                profile = ScriptableObject.CreateInstance<PulseBarrierVisualProfile>();
                profile.arcMaterial = material;
                AssetDatabase.CreateAsset(profile, ProfilePath);
            }
            else if (profile.arcMaterial == null)
            {
                Undo.RecordObject(profile, "Assign pulse arc material");
                profile.arcMaterial = material;
                EditorUtility.SetDirty(profile);
            }
            PulseBarrierMod mod = AssetDatabase.LoadAssetAtPath<PulseBarrierMod>(ModPath);
            if (mod == null) throw new System.InvalidOperationException("Pulse Barrier mod asset is missing.");
            Undo.RecordObject(mod, "Replace Pulse Barrier sphere with arcs");
            mod.pulseArcVisuals = profile;
            mod.shieldActivateVFX = null;
            mod.shieldLoopVFXPrefab = null;
            mod.shieldAbsorbVFX = null;
            EditorUtility.SetDirty(mod);
            AssetDatabase.SaveAssetIfDirty(material);
            AssetDatabase.SaveAssetIfDirty(profile);
            AssetDatabase.SaveAssetIfDirty(mod);
            Debug.Log("Pulse Barrier arcs installed and wired to the current mod. No scene or player prefab changes required.");
        }
    }
}
