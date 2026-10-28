using UnityEditor;
using UnityEngine;

namespace junklite.Editor
{
    /// <summary>Explicit asset/prefab authoring only. Never opens or edits a scene.</summary>
    public static class PlayerProgressionSetup
    {
        public const string ConfigPath = "Assets/Game/Resources/PlayerProgressionConfig.asset";

        [MenuItem("Tools/JunkLite/Progression/Install Defaults")]
        public static void Install()
        {
            if (!AssetDatabase.IsValidFolder("Assets/Game/Resources"))
                AssetDatabase.CreateFolder("Assets/Game", "Resources");
            var profile = AssetDatabase.LoadAssetAtPath<PlayerProgressionConfig>(ConfigPath);
            bool createdProfile = profile == null;
            if (profile == null)
            {
                profile = ScriptableObject.CreateInstance<PlayerProgressionConfig>();
                profile.Generate();
                AssetDatabase.CreateAsset(profile, ConfigPath);
            }
            ConfigureRoot("Assets/Game/Prefabs/Manager/Game Root.prefab", profile);
            ConfigureRoot("Assets/Game/Prefabs/Manager/Game Manager.prefab", profile);
            ConfigurePlayer();
            if (createdProfile) ConfigureTrainingTargets();
            AssetDatabase.SaveAssets();
            Selection.activeObject = profile;
            Debug.Log("[Progression] Installed profile, root services (progression + upgrades) and four player slots. Scenes were not edited.");
        }

        private static void ConfigureRoot(string path, PlayerProgressionConfig profile)
        {
            if (AssetDatabase.LoadAssetAtPath<GameObject>(path) == null) return;
            var root = PrefabUtility.LoadPrefabContents(path);
            try
            {
                var progression = root.GetComponent<PlayerProgression>() ?? root.AddComponent<PlayerProgression>();
                var serialized = new SerializedObject(progression);
                if (serialized.FindProperty("config").objectReferenceValue == null)
                {
                    serialized.FindProperty("config").objectReferenceValue = profile;
                    serialized.ApplyModifiedPropertiesWithoutUndo();
                }
                if (root.GetComponent<PlayerUpgrades>() == null) root.AddComponent<PlayerUpgrades>();
                PrefabUtility.SaveAsPrefabAsset(root, path);
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
        }

        private static void ConfigurePlayer()
        {
            const string path = "Assets/Game/Prefabs/PLAYER/Player_2.2.prefab";
            var root = PrefabUtility.LoadPrefabContents(path);
            try
            {
                var manager = new SerializedObject(root.GetComponent<ModManager>());
                manager.FindProperty("maxActiveSlots").intValue = 4;
                manager.FindProperty("unlockedActiveSlots").intValue = 1;
                manager.ApplyModifiedPropertiesWithoutUndo();
                PrefabUtility.SaveAsPrefabAsset(root, path);
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
        }

        private static void ConfigureTrainingTargets()
        {
            foreach (string guid in AssetDatabase.FindAssets("t:Prefab", new[] { "Assets/Game/Prefabs" }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (prefab == null || prefab.GetComponent<DummyEnemy>() == null) continue;
                // Only initialize the newly added reward on installation. Authors can edit it later.
                var root = PrefabUtility.LoadPrefabContents(path);
                try
                {
                    var enemy = new SerializedObject(root.GetComponent<DummyEnemy>());
                    enemy.FindProperty("experienceReward").intValue = 0;
                    enemy.ApplyModifiedPropertiesWithoutUndo();
                    PrefabUtility.SaveAsPrefabAsset(root, path);
                }
                finally { PrefabUtility.UnloadPrefabContents(root); }
            }
        }

        /// <summary>Wipes XP, upgrade points, purchases and acquired weapons. In Play Mode resets the live profile; otherwise deletes the save file.</summary>
        [MenuItem("Tools/JunkLite/Progression/Reset Saved Progress")]
        public static void ResetSavedProgress()
        {
            if (!EditorUtility.DisplayDialog("Reset Saved Progress",
                    "Wipe XP, level, upgrade points, bought upgrades and acquired weapons from the player profile?", "Reset", "Cancel"))
                return;
            if (Application.isPlaying && PlayerProgression.Instance != null)
            {
                PlayerProgression.Instance.ResetProfile();
                PlayerLifecycle.Instance?.Player?.GetComponent<PlayerArmament>()?.RevertToDefault();
                Debug.Log("[Progression] Live profile reset and saved.");
                return;
            }
            string path = PlayerProgression.SavePath;
            bool deleted = false;
            foreach (string file in new[] { path, path + ".bak", path + ".tmp" })
            {
                if (!System.IO.File.Exists(file)) continue;
                System.IO.File.Delete(file);
                deleted = true;
            }
            Debug.Log(deleted ? $"[Progression] Deleted saved progress at {path}." : "[Progression] No saved progress to delete.");
        }

        [MenuItem("Tools/JunkLite/Progression/Select Profile")]
        public static void SelectProfile() => Selection.activeObject = AssetDatabase.LoadAssetAtPath<PlayerProgressionConfig>(ConfigPath);
    }
}
