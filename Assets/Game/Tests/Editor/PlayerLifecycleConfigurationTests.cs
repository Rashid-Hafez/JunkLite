using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace junklite.Tests
{
    public sealed class PlayerLifecycleConfigurationTests
    {
        private const string GameRootPrefabPath =
            "Assets/Game/Prefabs/Manager/Game Root.prefab";

        private GameObject levelRoot;

        [SetUp]
        public void SetUp()
        {
            levelRoot = new GameObject("Lifecycle Test Level");
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(levelRoot);
        }

        [Test]
        public void LevelContextReturnsPrimaryThenAdditionalTypedSpawns()
        {
            LevelContext context = levelRoot.AddComponent<LevelContext>();
            SpawnPoint primary = CreateSpawn("Primary", 10);
            SpawnPoint secondary = CreateSpawn("Secondary", 20);

            var serializedContext = new SerializedObject(context);
            serializedContext.FindProperty("primaryPlayerSpawn").objectReferenceValue = primary;

            SerializedProperty additional =
                serializedContext.FindProperty("additionalPlayerSpawns");
            additional.arraySize = 2;
            additional.GetArrayElementAtIndex(0).objectReferenceValue = primary;
            additional.GetArrayElementAtIndex(1).objectReferenceValue = secondary;
            serializedContext.ApplyModifiedPropertiesWithoutUndo();

            var spawns = context.GetPlayerSpawns();

            Assert.That(spawns.Count, Is.EqualTo(2));
            Assert.That(spawns[0], Is.SameAs(primary.transform));
            Assert.That(spawns[1], Is.SameAs(secondary.transform));
        }

        [Test]
        public void SpawnPointExposesStableNameAndPriority()
        {
            SpawnPoint spawn = CreateSpawn("Fallback Name", 7);

            Assert.That(spawn.SpawnPointName, Is.EqualTo("Fallback Name"));
            Assert.That(spawn.Priority, Is.EqualTo(7));

            var serializedSpawn = new SerializedObject(spawn);
            serializedSpawn.FindProperty("spawnPointName").stringValue = "Checkpoint A";
            serializedSpawn.ApplyModifiedPropertiesWithoutUndo();

            Assert.That(spawn.SpawnPointName, Is.EqualTo("Checkpoint A"));
        }

        [Test]
        public void ReusableGameRootOwnsConfiguredPlayerLifecycle()
        {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(GameRootPrefabPath);

            Assert.That(prefab, Is.Not.Null);
            Assert.That(prefab.GetComponent<GameManager>(), Is.Not.Null);

            PlayerLifecycle lifecycle = prefab.GetComponent<PlayerLifecycle>();
            Assert.That(lifecycle, Is.Not.Null);
            Assert.That(lifecycle.PlayerPrefab, Is.Not.Null);
            Assert.That(lifecycle.PlayerPrefab.GetComponent<PlayerCharacter>(), Is.Not.Null);
        }

        [Test]
        public void ReusableGameRootOwnsConfiguredGameUIManager()
        {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(GameRootPrefabPath);

            Assert.That(prefab, Is.Not.Null);

            GameUIManager uiManager = prefab.GetComponent<GameUIManager>();
            Assert.That(uiManager, Is.Not.Null);
            Assert.That(uiManager.PlayerUIPrefab, Is.Not.Null);
            Assert.That(uiManager.PlayerUIPrefab.GetComponent<PlayerUI>(), Is.Not.Null);
            Assert.That(uiManager.PauseMenuUIPrefab, Is.Not.Null);
            Assert.That(uiManager.PauseMenuUIPrefab.GetComponent<PauseMenuUI>(), Is.Not.Null);
            Assert.That(uiManager.GameOverUIPrefab, Is.Not.Null);
            Assert.That(uiManager.LoadingScreenUIPrefab, Is.Not.Null);
            Assert.That(uiManager.LoadingScreenUIPrefab.GetComponent<LoadingScreenUI>(), Is.Not.Null);
        }

        private SpawnPoint CreateSpawn(string objectName, int priority)
        {
            var spawnObject = new GameObject(objectName);
            spawnObject.transform.SetParent(levelRoot.transform, false);
            SpawnPoint spawn = spawnObject.AddComponent<SpawnPoint>();

            var serializedSpawn = new SerializedObject(spawn);
            serializedSpawn.FindProperty("priority").intValue = priority;
            serializedSpawn.ApplyModifiedPropertiesWithoutUndo();
            return spawn;
        }
    }
}
