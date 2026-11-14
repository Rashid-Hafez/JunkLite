using UnityEditor;
using UnityEngine;

namespace junklite.EditorTools
{
    /// <summary>
    /// One-click swap of a plain MeleeChaserBrain for a GruntBrain. The script
    /// reference is changed in place, so the component keeps its file id and
    /// every serialized value (chase, melee, stun, hitbox, pursuit radius) and
    /// scene-instance overrides on it survive. One Undo step for selections.
    /// </summary>
    public static class GruntBrainMigration
    {
        private const string GruntPrefabPath = "Assets/Game/ENEMIES/Grunt/Grunt Enemy.prefab";
        private const string GruntBrainScriptPath = "Assets/Game/Scripts/New Enemies/Grunt Enemy/GruntBrain.cs";

        [MenuItem("Tools/JunkLite/Enemies/Migrate Grunt Prefab To GruntBrain")]
        public static void MigrateGruntPrefab()
        {
            GameObject root = PrefabUtility.LoadPrefabContents(GruntPrefabPath);
            if (root == null)
            {
                Debug.LogError($"[GruntBrainMigration] Could not load {GruntPrefabPath}.");
                return;
            }

            try
            {
                if (Migrate(root))
                {
                    PrefabUtility.SaveAsPrefabAsset(root, GruntPrefabPath);
                    Debug.Log($"[GruntBrainMigration] {GruntPrefabPath} now uses GruntBrain.");
                }
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }

        [MenuItem("Tools/JunkLite/Enemies/Migrate Selected To GruntBrain")]
        public static void MigrateSelected()
        {
            int migrated = 0;
            foreach (GameObject selected in Selection.gameObjects)
            {
                if (Migrate(selected, true))
                    migrated++;
            }

            Debug.Log($"[GruntBrainMigration] Migrated {migrated} object(s).");
        }

        private static bool Migrate(GameObject root, bool withUndo = false)
        {
            MeleeChaserBrain existing = root.GetComponent<MeleeChaserBrain>();
            if (existing == null)
            {
                Debug.LogWarning($"[GruntBrainMigration] {root.name} has no MeleeChaserBrain.", root);
                return false;
            }

            if (existing is GruntBrain)
            {
                Debug.Log($"[GruntBrainMigration] {root.name} already uses GruntBrain.", root);
                return false;
            }

            if (existing.GetType() != typeof(MeleeChaserBrain))
            {
                Debug.LogWarning(
                    $"[GruntBrainMigration] {root.name} uses {existing.GetType().Name}; only plain MeleeChaserBrain is migrated.",
                    root);
                return false;
            }

            MonoScript gruntScript = AssetDatabase.LoadAssetAtPath<MonoScript>(GruntBrainScriptPath);
            if (gruntScript == null || gruntScript.GetClass() != typeof(GruntBrain))
            {
                Debug.LogError($"[GruntBrainMigration] GruntBrain script not found at {GruntBrainScriptPath}.");
                return false;
            }

            var serialized = new SerializedObject(existing);
            SerializedProperty script = serialized.FindProperty("m_Script");
            script.objectReferenceValue = gruntScript;

            if (withUndo)
                serialized.ApplyModifiedProperties();
            else
                serialized.ApplyModifiedPropertiesWithoutUndo();

            EditorUtility.SetDirty(root);
            return true;
        }
    }
}
