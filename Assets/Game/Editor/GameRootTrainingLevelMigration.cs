using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using Unity.Cinemachine;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;

namespace junklite.Editor
{
    /// <summary>
    /// Explicit authoring tools for the reusable GameRoot and V2.5 training scene.
    /// Nothing runs automatically when the project or scene opens.
    /// </summary>
    public static class GameRootTrainingLevelMigration
    {
        private const string SourcePrefabPath = "Assets/Game/Prefabs/Manager/Game Manager.prefab";
        private const string GameRootPrefabPath = "Assets/Game/Prefabs/Manager/Game Root.prefab";
        private const string TrainingScenePath = "Assets/Game/Scenes/V2.5.unity";

        [MenuItem("Tools/JunkLite/V2.5/Strip Legacy Systems and Rebuild")]
        public static void Run()
        {
            bool confirmed = EditorUtility.DisplayDialog(
                "Rebuild V2.5 Infrastructure",
                "This removes obsolete player, input, UI, GameRoot, LevelContext, and prototype-test " +
                "objects from V2.5, then installs one Game Root and one standalone Level Context. " +
                "Level geometry, lighting, cameras, enemies, pickups, and required presentation services are preserved.",
                "Rebuild V2.5",
                "Cancel");

            if (!confirmed) return;
            RebuildTrainingSetup();
        }

        /// <summary>Command-line entry point. Intentionally explicit; never called on editor load.</summary>
        public static void RunBatch()
        {
            RebuildTrainingSetup();
        }

        [MenuItem("Tools/JunkLite/V2.5/Rebuild Game Root Prefab Only")]
        public static void RebuildGameRootPrefabOnly()
        {
            BuildGameRootPrefab();
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log("[GameRootMigration] Rebuilt the Game Root prefab without scene-local data.");
        }

        /// <summary>Command-line entry point for the small prefab-only rebuild.</summary>
        public static void RebuildGameRootPrefabBatch()
        {
            RebuildGameRootPrefabOnly();
        }

        [MenuItem("Tools/JunkLite/V2.5/Validate Redesigned Setup")]
        public static void Validate()
        {
            Scene scene = OpenTrainingScene();
            ValidateTrainingScene(scene);
            Debug.Log("[GameRootMigration] V2.5 redesigned infrastructure validation passed.");
        }

        private static void RebuildTrainingSetup()
        {
            GameObject gameRootPrefab = BuildGameRootPrefab();
            MigrateTrainingScene(gameRootPrefab);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            Debug.Log(
                "[GameRootMigration] V2.5 now has one persistent Game Root and one standalone " +
                "scene-local Level Context. Legacy player/input/UI/bootstrap objects were removed.");
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
                root.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
                root.transform.localScale = Vector3.one;

                ActivateTestManager obsoleteTrigger = root.GetComponent<ActivateTestManager>();
                if (obsoleteTrigger != null)
                    Object.DestroyImmediate(obsoleteTrigger);

                if (root.GetComponent<GameRoot>() == null)
                    root.AddComponent<GameRoot>();

                PlayerLifecycle lifecycle = root.GetComponent<PlayerLifecycle>();
                if (lifecycle == null)
                    lifecycle = root.AddComponent<PlayerLifecycle>();

                GameUIManager uiManager = root.GetComponent<GameUIManager>();
                if (uiManager == null)
                    uiManager = root.AddComponent<GameUIManager>();

                GameManager manager = root.GetComponent<GameManager>();
                if (manager != null)
                {
                    var serializedManager = new SerializedObject(manager);
                    lifecycle.ApplyDefaultsIfMissing(
                        serializedManager.FindProperty("playerPrefab").objectReferenceValue as GameObject,
                        serializedManager.FindProperty("respawnDelay").floatValue,
                        serializedManager.FindProperty("deathScreenFallbackDelay").floatValue);

                    uiManager.ApplyDefaultsIfMissing(
                        serializedManager.FindProperty("playerUIPrefab").objectReferenceValue as GameObject,
                        serializedManager.FindProperty("pauseMenuUIPrefab").objectReferenceValue as GameObject,
                        serializedManager.FindProperty("gameOverUIPrefab").objectReferenceValue as GameObject,
                        serializedManager.FindProperty("loadingScreenUIPrefab").objectReferenceValue as GameObject);
                }

                if (root.GetComponent<GameInputManager>() == null)
                    root.AddComponent<GameInputManager>();

                // LevelContext is scene data. It must never be saved beneath a
                // DontDestroyOnLoad root or depend on Awake-time detachment.
                foreach (LevelContext context in root.GetComponentsInChildren<LevelContext>(true))
                    Object.DestroyImmediate(context.gameObject);

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
            Scene scene = OpenTrainingScene();
            ResolveSpawnPose(scene, out Vector3 spawnPosition, out Quaternion spawnRotation);

            RemoveLegacyInfrastructure(scene);

            GameObject gameRootInstance = PrefabUtility.InstantiatePrefab(gameRootPrefab, scene) as GameObject;
            if (gameRootInstance == null)
                throw new System.InvalidOperationException("Failed to instantiate Game Root in V2.5.");

            gameRootInstance.name = "Game Root";
            gameRootInstance.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            gameRootInstance.transform.localScale = Vector3.one;

            var contextObject = new GameObject("Level Context");
            SceneManager.MoveGameObjectToScene(contextObject, scene);
            contextObject.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);

            LevelContext context = contextObject.AddComponent<LevelContext>();

            var spawnObject = new GameObject("Player Spawn Point");
            spawnObject.transform.SetParent(contextObject.transform, false);
            spawnObject.transform.SetPositionAndRotation(spawnPosition, spawnRotation);
            SpawnPoint spawn = spawnObject.AddComponent<SpawnPoint>();

            SetLevelContextValues(
                context,
                "training_v2_5",
                "Training Level",
                true,
                spawn);

            EditorUtility.SetDirty(gameRootInstance);
            EditorUtility.SetDirty(contextObject);
            EditorUtility.SetDirty(context);
            EditorUtility.SetDirty(spawnObject);

            EditorSceneManager.MarkSceneDirty(scene);
            if (!EditorSceneManager.SaveScene(scene))
                throw new System.InvalidOperationException("Failed to save the V2.5 training scene.");

            ValidateTrainingScene(scene);
        }

        private static Scene OpenTrainingScene()
        {
            Scene activeScene = SceneManager.GetActiveScene();
            return activeScene.path == TrainingScenePath
                ? activeScene
                : EditorSceneManager.OpenScene(TrainingScenePath, OpenSceneMode.Single);
        }

        private static void ResolveSpawnPose(
            Scene scene,
            out Vector3 spawnPosition,
            out Quaternion spawnRotation)
        {
            spawnPosition = new Vector3(-58.400005f, 0.2f, -0.279f);
            spawnRotation = Quaternion.identity;

            LevelContext existingContext = FindSceneComponents<LevelContext>(scene).FirstOrDefault();
            Transform configuredSpawn = existingContext?.GetPlayerSpawns().FirstOrDefault();

            SpawnPoint existingSpawn = configuredSpawn != null
                ? configuredSpawn.GetComponent<SpawnPoint>()
                : FindSceneComponents<SpawnPoint>(scene)
                    .FirstOrDefault(point => point.name == "Basic Spawn Point");

            if (existingSpawn == null)
            {
                existingSpawn = FindSceneComponents<SpawnPoint>(scene)
                    .OrderBy(point => point.Priority)
                    .FirstOrDefault();
            }

            if (existingSpawn == null) return;

            spawnPosition = existingSpawn.transform.position;
            spawnRotation = existingSpawn.transform.rotation;
        }

        private static void RemoveLegacyInfrastructure(Scene scene)
        {
            var rootsToRemove = new HashSet<GameObject>();

            CollectOutermostRoots<GameRoot>(scene, rootsToRemove);
            CollectOutermostRoots<GameManager>(scene, rootsToRemove);
            CollectOutermostRoots<PlayerLifecycle>(scene, rootsToRemove);
            CollectOutermostRoots<GameUIManager>(scene, rootsToRemove);
            CollectOutermostRoots<GameInputManager>(scene, rootsToRemove);
            CollectOutermostRoots<PlayerCombatTracker>(scene, rootsToRemove);
            CollectOutermostRoots<PlayerCharacter>(scene, rootsToRemove);
            CollectOutermostRoots<LevelContext>(scene, rootsToRemove);
            CollectOutermostRoots<EventSystem>(scene, rootsToRemove);
            CollectOutermostRoots<UIManager>(scene, rootsToRemove);
            CollectOutermostRoots<PlayerUI>(scene, rootsToRemove);
            CollectOutermostRoots<PauseMenuUI>(scene, rootsToRemove);
            CollectOutermostRoots<LoadingScreenUI>(scene, rootsToRemove);

            foreach (GameObject root in rootsToRemove)
            {
                if (root != null)
                    Object.DestroyImmediate(root);
            }

            foreach (SceneSettings settings in FindSceneComponents<SceneSettings>(scene).ToArray())
            {
                if (settings != null)
                    Object.DestroyImmediate(settings);
            }

            RemoveRootByExactName(scene, "Spawn Points");
            RemoveRootByExactName(scene, "UI Manager");
            RemoveRootByExactName(scene, "EventSystem");
            RemoveRootByExactName(scene, "Prototype Test Manager");
            RemoveRootByExactName(scene, "Legacy Gameplay Canvas (Disabled)");
            RemoveLegacyGameplayCanvas(scene);
        }

        private static void CollectOutermostRoots<T>(
            Scene scene,
            HashSet<GameObject> roots) where T : Component
        {
            foreach (T component in FindSceneComponents<T>(scene))
            {
                if (component == null) continue;

                GameObject outermost = PrefabUtility.GetOutermostPrefabInstanceRoot(component.gameObject);
                roots.Add(outermost != null ? outermost : component.transform.root.gameObject);
            }
        }

        private static void RemoveLegacyGameplayCanvas(Scene scene)
        {
            foreach (GameObject root in scene.GetRootGameObjects().ToArray())
            {
                Canvas canvas = root.GetComponent<Canvas>();
                if (canvas == null) continue;

                Transform gameplayPanel = FindDescendant(root.transform, "Gameplay  UI");
                if (gameplayPanel == null)
                    gameplayPanel = FindDescendant(root.transform, "Gameplay UI");

                if (gameplayPanel != null)
                    Object.DestroyImmediate(root);
            }
        }

        private static void RemoveRootByExactName(Scene scene, string objectName)
        {
            foreach (GameObject root in scene.GetRootGameObjects()
                         .Where(candidate => candidate.name == objectName)
                         .ToArray())
            {
                Object.DestroyImmediate(root);
            }
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
            serializedContext.FindProperty("additionalPlayerSpawns").arraySize = 0;
            serializedContext.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void ValidateTrainingScene(Scene scene)
        {
            GameRoot[] gameRoots = FindSceneComponents<GameRoot>(scene).ToArray();
            GameManager[] gameManagers = FindSceneComponents<GameManager>(scene).ToArray();
            PlayerLifecycle[] playerLifecycles = FindSceneComponents<PlayerLifecycle>(scene).ToArray();
            GameUIManager[] uiManagers = FindSceneComponents<GameUIManager>(scene).ToArray();
            GameInputManager[] inputManagers = FindSceneComponents<GameInputManager>(scene).ToArray();
            PlayerCombatTracker[] combatTrackers = FindSceneComponents<PlayerCombatTracker>(scene).ToArray();
            LevelContext[] contexts = FindSceneComponents<LevelContext>(scene).ToArray();
            PlayerCharacter[] players = FindSceneComponents<PlayerCharacter>(scene).ToArray();
            CameraManager[] cameraManagers = FindSceneComponents<CameraManager>(scene).ToArray();
            CinemachineBrain[] cameraBrains = FindSceneComponents<CinemachineBrain>(scene).ToArray();

            if (gameRoots.Length != 1)
                throw new System.InvalidOperationException($"Expected one GameRoot, found {gameRoots.Length}.");
            if (gameManagers.Length != 1)
                throw new System.InvalidOperationException($"Expected one GameManager, found {gameManagers.Length}.");
            if (playerLifecycles.Length != 1)
                throw new System.InvalidOperationException(
                    $"Expected one PlayerLifecycle, found {playerLifecycles.Length}.");
            if (uiManagers.Length != 1)
                throw new System.InvalidOperationException(
                    $"Expected one GameUIManager, found {uiManagers.Length}.");
            if (inputManagers.Length != 1)
                throw new System.InvalidOperationException($"Expected one GameInputManager, found {inputManagers.Length}.");
            if (combatTrackers.Length != 1)
                throw new System.InvalidOperationException($"Expected one PlayerCombatTracker, found {combatTrackers.Length}.");
            if (contexts.Length != 1)
                throw new System.InvalidOperationException($"Expected one LevelContext, found {contexts.Length}.");
            if (players.Length != 0)
                throw new System.InvalidOperationException($"Expected no scene-placed player, found {players.Length}.");
            if (cameraManagers.Length != 1)
                throw new System.InvalidOperationException($"Expected one scene-local CameraManager, found {cameraManagers.Length}.");
            if (cameraBrains.Length != 1)
                throw new System.InvalidOperationException($"Expected one CinemachineBrain, found {cameraBrains.Length}.");

            CameraManager cameraManager = cameraManagers[0];
            if (cameraManager.transform.IsChildOf(gameRoots[0].transform))
                throw new System.InvalidOperationException("CameraManager must remain scene-local, not beneath GameRoot.");
            if (cameraManager.MainCamera == null)
                throw new MissingReferenceException("CameraManager has no main Cinemachine camera assigned.");
            if (cameraManager.MainCamera.gameObject.scene != scene)
                throw new System.InvalidOperationException("CameraManager main camera must belong to the V2.5 scene camera rig.");

            LevelContext context = contexts[0];
            if (context.transform.parent != null)
                throw new System.InvalidOperationException("LevelContext must be a standalone scene root.");
            if (!context.SpawnPlayer || context.GetPlayerSpawns().Count == 0)
                throw new System.InvalidOperationException("LevelContext is not configured to spawn the player.");

            if (playerLifecycles[0].gameObject != gameRoots[0].gameObject)
                throw new System.InvalidOperationException("PlayerLifecycle must be hosted by Game Root.");
            if (uiManagers[0].gameObject != gameRoots[0].gameObject)
                throw new System.InvalidOperationException("GameUIManager must be hosted by Game Root.");

            GameObject playerPrefab = playerLifecycles[0].PlayerPrefab;
            if (playerPrefab == null)
                throw new MissingReferenceException("PlayerLifecycle has no player prefab assigned.");
            if (playerPrefab.GetComponent<PlayerCharacter>() == null ||
                playerPrefab.GetComponent<Damageable>() == null ||
                playerPrefab.GetComponent<ModManager>() == null)
            {
                throw new MissingReferenceException(
                    "The configured player prefab is missing PlayerCharacter, Damageable, or ModManager.");
            }

            ValidateGameUIPrefabs(uiManagers[0]);

            if (FindSceneComponents<EventSystem>(scene).Any())
                throw new System.InvalidOperationException("A legacy scene EventSystem remains; GameRoot creates it at runtime.");

            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(GameRootPrefabPath);
            if (prefab == null)
                throw new MissingReferenceException("Game Root prefab is missing.");
            if (prefab.GetComponent<PlayerLifecycle>() == null)
                throw new MissingReferenceException("Game Root prefab has no PlayerLifecycle component.");
            if (prefab.GetComponent<GameUIManager>() == null)
                throw new MissingReferenceException("Game Root prefab has no GameUIManager component.");
            if (prefab.GetComponentInChildren<LevelContext>(true) != null)
                throw new System.InvalidOperationException("Game Root prefab still contains scene-local LevelContext data.");
        }

        private static void ValidateGameUIPrefabs(GameUIManager uiManager)
        {
            if (uiManager.PlayerUIPrefab == null ||
                uiManager.PlayerUIPrefab.GetComponent<PlayerUI>() == null)
            {
                throw new MissingReferenceException(
                    "GameUIManager needs a player UI prefab with PlayerUI.");
            }

            if (uiManager.PauseMenuUIPrefab == null ||
                uiManager.PauseMenuUIPrefab.GetComponent<PauseMenuUI>() == null)
            {
                throw new MissingReferenceException(
                    "GameUIManager needs a pause menu prefab with PauseMenuUI.");
            }

            if (uiManager.GameOverUIPrefab == null)
                throw new MissingReferenceException("GameUIManager has no game-over prefab assigned.");

            if (uiManager.LoadingScreenUIPrefab == null ||
                uiManager.LoadingScreenUIPrefab.GetComponent<LoadingScreenUI>() == null)
            {
                throw new MissingReferenceException(
                    "GameUIManager needs a loading-screen prefab with LoadingScreenUI.");
            }
        }
    }
}
