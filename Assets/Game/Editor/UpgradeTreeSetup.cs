using UnityEditor;
using UnityEngine;

namespace junklite.Editor
{
    /// <summary>Creates the shipped upgrade tree asset and wires the upgrades service onto the root prefabs. Never edits scenes.</summary>
    public static class UpgradeTreeSetup
    {
        public const string ConfigPath = "Assets/Game/Resources/UpgradeTreeConfig.asset";
        private const string BladesPath = "Assets/Game/Scripts/Weapons/Sword.asset";

        [MenuItem("Tools/JunkLite/Progression/Install Upgrade Tree")]
        public static void Install()
        {
            if (!AssetDatabase.IsValidFolder("Assets/Game/Resources"))
                AssetDatabase.CreateFolder("Assets/Game", "Resources");
            var config = AssetDatabase.LoadAssetAtPath<UpgradeTreeConfig>(ConfigPath);
            bool created = config == null;
            if (created)
            {
                config = ScriptableObject.CreateInstance<UpgradeTreeConfig>();
                UpgradeTreeConfig.PopulateDefaults(config, AssetDatabase.LoadAssetAtPath<WeaponData>(BladesPath));
                AssetDatabase.CreateAsset(config, ConfigPath);
            }
            foreach (string path in new[] { "Assets/Game/Prefabs/Manager/Game Root.prefab", "Assets/Game/Prefabs/Manager/Game Manager.prefab" })
                ConfigureRoot(path, config);
            AssignIcons(config);
            AssetDatabase.SaveAssets();
            Selection.activeObject = config;
            if (!config.Validate(out string error)) Debug.LogWarning("[Upgrades] Tree asset is invalid: " + error, config);
            Debug.Log(created
                ? "[Upgrades] Created Resources/UpgradeTreeConfig with the default tree and wired PlayerUpgrades onto the root prefabs."
                : "[Upgrades] Existing tree kept; PlayerUpgrades wired onto the root prefabs.");
        }

        [MenuItem("Tools/JunkLite/Progression/Reset Upgrade Tree To Defaults")]
        public static void ResetToDefaults()
        {
            var config = AssetDatabase.LoadAssetAtPath<UpgradeTreeConfig>(ConfigPath);
            if (config == null) { Install(); return; }
            if (!EditorUtility.DisplayDialog("Reset Upgrade Tree", "Replace every node on the tree asset with the built-in defaults?", "Replace", "Cancel"))
                return;
            Undo.RecordObject(config, "Reset Upgrade Tree");
            UpgradeTreeConfig.PopulateDefaults(config, AssetDatabase.LoadAssetAtPath<WeaponData>(BladesPath));
            AssignIcons(config);
            EditorUtility.SetDirty(config);
            AssetDatabase.SaveAssets();
        }

        /// <summary>Gives every node the sprite named after its id in the icon folder (Assets/Game/New UI/Icons/Upgrades/&lt;id&gt;.png).</summary>
        [MenuItem("Tools/JunkLite/Progression/Assign Upgrade Icons")]
        public static void AssignIcons()
        {
            var config = AssetDatabase.LoadAssetAtPath<UpgradeTreeConfig>(ConfigPath);
            if (config == null) { Install(); return; }
            AssignIcons(config);
            AssetDatabase.SaveAssets();
        }

        public static int AssignIcons(UpgradeTreeConfig config)
        {
            if (config == null || config.nodes == null) return 0;
            int assigned = 0, missing = 0;
            foreach (UpgradeNode node in config.nodes)
            {
                if (node == null || string.IsNullOrEmpty(node.id)) continue;
                var sprite = AssetDatabase.LoadAssetAtPath<Sprite>(UpgradeIconImporter.IconFolder + node.id + ".png");
                if (sprite == null) { missing++; continue; }
                if (node.icon == sprite) continue;
                node.icon = sprite;
                assigned++;
            }
            if (assigned > 0) EditorUtility.SetDirty(config);
            Debug.Log($"[Upgrades] Icons: {assigned} assigned, {missing} node(s) without an icon file in {UpgradeIconImporter.IconFolder}.");
            return assigned;
        }

        [MenuItem("Tools/JunkLite/Progression/Select Upgrade Tree")]
        public static void Select() => Selection.activeObject = AssetDatabase.LoadAssetAtPath<UpgradeTreeConfig>(ConfigPath);

        private static void ConfigureRoot(string path, UpgradeTreeConfig config)
        {
            if (AssetDatabase.LoadAssetAtPath<GameObject>(path) == null) return;
            var root = PrefabUtility.LoadPrefabContents(path);
            try
            {
                var upgrades = root.GetComponent<PlayerUpgrades>() ?? root.AddComponent<PlayerUpgrades>();
                var serialized = new SerializedObject(upgrades);
                if (serialized.FindProperty("config").objectReferenceValue == null)
                {
                    serialized.FindProperty("config").objectReferenceValue = config;
                    serialized.ApplyModifiedPropertiesWithoutUndo();
                }
                PrefabUtility.SaveAsPrefabAsset(root, path);
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
        }
    }
}
