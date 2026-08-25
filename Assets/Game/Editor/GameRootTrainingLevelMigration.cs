using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace junklite.Editor
{
    /// <summary>
    /// One-time authoring migration for the first reusable GameRoot and the V2.5 training level.
    /// Kept as a menu command so the setup can be rebuilt deterministically if needed.
    /// </summary>
    [InitializeOnLoad]
    public static class GameRootTrainingLevelMigration
    {
        private const string SourcePrefabPath = "Assets/Game/Prefabs/Manager/Game Manager.prefab";
        private const string GameRootPrefabPath = "Assets/Game/Prefabs/Manager/Game Root.prefab";
        private const string TrainingScenePath = "Assets/Game/Scenes/V2.5.unity";
        private const string AutoRunSessionKey = "JunkLite.GameRootTrainingMigration.2026-08-25";

        static GameRootTrainingLevelMigration()
        {
            EditorApplication.delayCall += TryRunInOpenTrainingScene;
        }

        [MenuItem("Tools/JunkLite/Rebuild Game Root and Training Level")]
        public static void Run()
        {
            GameObject gameRootPrefab = BuildGameRootPrefab();
            MigrateTrainingScene(gameRootPrefab);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            Debug.Log("[GameRootMigration] Game Root prefab and V2.5 training level are ready.");
        }

        // Entry point used by command-line validation.
        public static void RunBatch()
        {
            Run();
        }

        private static void TryRunInOpenTrainingScene()
        {
            if (SessionState.GetBool(AutoRunSessionKey, false)) return;
            if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling)
            {
                EditorApplication.delayCall += TryRunInOpenTrainingScene;
                return;
            }

            Scene activeScene = SceneManager.GetActiveScene();
            if (activeScene.path != TrainingScenePath)
            {
                Debug.Log(
                    "[GameRootMigration] V2.5 is not the active scene. " +
                    "Use Tools/JunkLite/Rebuild Game Root and Training Level when ready.");
                return;
            }

            SessionState.SetBool(AutoRunSessionKey, true);
            if (IsTrainingSceneFullyMigrated(activeScene))
                return;

            try
            {
                Run();
            }
            catch (System.Exception exception)
            {
                Debug.LogException(exception);
            }
        }

        private static bool IsTrainingSceneFullyMigrated(Scene scene)
        {
            if (AssetDatabase.LoadAssetAtPath<GameObject>(GameRootPrefabPath) == null)
                return false;

            if (FindSceneComponents<GameRoot>(scene).Count() != 1)
                return false;
            if (FindSceneComponents<PlayerCharacter>(scene).Any())
                return false;

            string[] obsoleteRoots =
            {
                "Spawn Points",
                "patrol dummy",
                "UI Manager",
                "EventSystem",
                "--Managers--",
                "Legacy Gameplay Canvas (Disabled)"
            };

            foreach (GameObject root in scene.GetRootGameObjects())
            {
                if (obsoleteRoots.Contains(root.name))
                    return false;

                if (root.GetComponent<Canvas>() != null &&
                    (FindDescendant(root.transform, "Gameplay  UI") != null ||
                     FindDescendant(root.transform, "Gameplay UI") != null))
                {
                    return false;
                }
            }

            return true;
        }

        private static GameObject BuildGameRootPrefab()
        {
            GameObject sourcePrefab = AssetDatabase.LoadAssetAtPath<GameObject>(SourcePrefabPath);
            if (sourcePrefab == null)
                throw new MissingReferenceException($"Missing source prefab at '{SourcePrefabPath}'.");

            GameObject root = PrefabUtility.LoadPrefabContents(SourcePrefabPath);
            try
            {
                root.name = "Game Root";

                ActivateTestManager obsoleteTrigger = root.GetComponent<ActivateTestManager>();
                if (obsoleteTrigger != null)
                    Object.DestroyImmediate(obsoleteTrigger);

                GameRoot gameRoot = root.GetComponent<GameRoot>();
                if (gameRoot == null)
                    gameRoot = root.AddComponent<GameRoot>();

                if (root.GetComponent<GameInputManager>() == null)
                    root.AddComponent<GameInputManager>();

                Transform existingContext = root.transform.Find("Level Context (Scene Local)");
                if (existingContext != null)
                    Object.DestroyImmediate(existingContext.gameObject);

                var contextObject = new GameObject("Level Context (Scene Local)");
                contextObject.transform.SetParent(root.transform, false);
                LevelContext context = contextObject.AddComponent<LevelContext>();

                var spawnObject = new GameObject("Player Spawn Point");
                spawnObject.transform.SetParent(contextObject.transform, false);
                SpawnPoint spawnPoint = spawnObject.AddComponent<SpawnPoint>();

                SetSerializedReference(gameRoot, "bundledLevelContext", context);
                SetLevelContextValues(
                    context,
                    "new_level",
                    "Gameplay Level",
                    false,
                    spawnPoint);

                GameObject savedPrefab = PrefabUtility.SaveAsPrefabAsset(root, GameRootPrefabPath);
                if (savedPrefab == null)
                    throw new System.InvalidOperationException("Failed to save the Game Root prefab.");

                return savedPrefab;
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }

        private static void MigrateTrainingScene(GameObject gameRootPrefab)
        {
            Scene activeScene = SceneManager.GetActiveScene();
            Scene scene = activeScene.path == TrainingScenePath
                ? activeScene
                : EditorSceneManager.OpenScene(TrainingScenePath, OpenSceneMode.Single);

            Vector3 spawnPosition = new(-58.400005f, 0.2f, -0.279f);
            Quaternion spawnRotation = Quaternion.identity;

            SpawnPoint existingSpawn = FindSceneComponents<SpawnPoint>(scene)
                .FirstOrDefault(point => point.name == "Basic Spawn Point");
            if (existingSpawn == null)
            {
                existingSpawn = FindSceneComponents<SpawnPoint>(scene)
                    .OrderBy(point => point.Priority)
                    .FirstOrDefault();
            }
            if (existingSpawn != null)
            {
                spawnPosition = existingSpawn.transform.position;
                spawnRotation = existingSpawn.transform.rotation;
            }

            RemoveExistingGameRoots(scene);
            RemoveScenePlayers(scene);

            RemoveRootByExactName(scene, "Spawn Points");
            RemoveRootByExactName(scene, "patrol dummy");
            RemoveRootByExactName(scene, "UI Manager");
            RemoveRootByExactName(scene, "EventSystem");
            RemoveRootByExactName(scene, "--Managers--");
            RemoveLegacyGameplayCanvas(scene);

            GameObject instance = PrefabUtility.InstantiatePrefab(gameRootPrefab, scene) as GameObject;
            if (instance == null)
                throw new System.InvalidOperationException("Failed to instantiate Game Root in V2.5.");

            instance.name = "Game Root";
            instance.transform.SetPositionAndRotation(spawnPosition, spawnRotation);

            LevelContext context = instance.GetComponentInChildren<LevelContext>(true);
            SpawnPoint spawn = instance.GetComponentInChildren<SpawnPoint>(true);
            if (context == null || spawn == null)
                throw new MissingReferenceException("Game Root prefab is missing its bundled level setup.");

            SetLevelContextValues(
                context,
                "training_v2_5",
                "Training Level",
                true,
                spawn);

            EditorUtility.SetDirty(instance);
            EditorUtility.SetDirty(context);
            PrefabUtility.RecordPrefabInstancePropertyModifications(instance.transform);
            PrefabUtility.RecordPrefabInstancePropertyModifications(context);

            EditorSceneManager.MarkSceneDirty(scene);
            if (!EditorSceneManager.SaveScene(scene))
                throw new System.InvalidOperationException("Failed to save the V2.5 training scene.");

            ValidateTrainingScene(scene);
        }

        private static void RemoveExistingGameRoots(Scene scene)
        {
            foreach (GameManager manager in FindSceneComponents<GameManager>(scene).ToArray())
                DestroyOutermostObject(manager.gameObject);
        }

        private static void RemoveScenePlayers(Scene scene)
        {
            foreach (PlayerCharacter player in FindSceneComponents<PlayerCharacter>(scene).ToArray())
                DestroyOutermostObject(player.gameObject);
        }

        private static void RemoveLegacyGameplayCanvas(Scene scene)
        {
            foreach (GameObject root in scene.GetRootGameObjects())
            {
                Canvas canvas = root.GetComponent<Canvas>();
                if (canvas == null) continue;

                Transform gameplayPanel = FindDescendant(root.transform, "Gameplay  UI");
                if (gameplayPanel == null)
                    gameplayPanel = FindDescendant(root.transform, "Gameplay UI");

                if (gameplayPanel == null) continue;
                Object.DestroyImmediate(root);
            }
        }

        private static void RemoveRootByExactName(Scene scene, string objectName)
        {
            GameObject root = scene.GetRootGameObjects()
                .FirstOrDefault(candidate => candidate.name == objectName);
            if (root != null)
                Object.DestroyImmediate(root);
        }

        private static void DestroyOutermostObject(GameObject target)
        {
            GameObject outermost = PrefabUtility.GetOutermostPrefabInstanceRoot(target);
            Object.DestroyImmediate(outermost != null ? outermost : target.transform.root.gameObject);
        }

        private static IEnumerable<T> FindSceneComponents<T>(Scene scene) where T : Component
        {
            foreach (GameObject root in scene.GetRootGameObjects())
            {
                foreach (T component in root.GetComponentsInChildren<T>(true))
                    yield return component;
            }
        }

        private static Transform FindDescendant(Transform parent, string objectName)
        {
            foreach (Transform child in parent)
            {
                if (child.name == objectName)
                    return child;

                Transform nested = FindDescendant(child, objectName);
                if (nested != null)
                    return nested;
            }

            return null;
        }

        private static void SetLevelContextValues(
            LevelContext context,
            string levelId,
            string displayName,
            bool trainingLevel,
            SpawnPoint primarySpawn)
        {
            var serializedContext = new SerializedObject(context);
            serializedContext.FindProperty("levelId").stringValue = levelId;
            serializedContext.FindProperty("displayName").stringValue = displayName;
            serializedContext.FindProperty("trainingLevel").boolValue = trainingLevel;
            serializedContext.FindProperty("spawnPlayer").boolValue = true;
            serializedContext.FindProperty("primaryPlayerSpawn").objectReferenceValue = primarySpawn;
            serializedContext.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void SetSerializedReference(
            Object target,
            string propertyName,
            Object value)
        {
            var serializedObject = new SerializedObject(target);
            serializedObject.FindProperty(propertyName).objectReferenceValue = value;
            serializedObject.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void ValidateTrainingScene(Scene scene)
        {
            int gameRootCount = FindSceneComponents<GameRoot>(scene).Count();
            int playerCount = FindSceneComponents<PlayerCharacter>(scene).Count();
            int dummyCount = FindSceneComponents<DummyEnemy>(scene).Count();

            if (gameRootCount != 1)
                throw new System.InvalidOperationException($"Expected one GameRoot, found {gameRootCount}.");
            if (playerCount != 0)
                throw new System.InvalidOperationException($"Expected no scene-placed player, found {playerCount}.");
            if (dummyCount < 1)
                throw new System.InvalidOperationException("Training level has no DummyEnemy target.");
        }
    }
}
