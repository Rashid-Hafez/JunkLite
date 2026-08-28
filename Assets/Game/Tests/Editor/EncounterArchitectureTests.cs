using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;

namespace junklite.Tests
{
    public sealed class EncounterArchitectureTests
    {
        private const string EnemyPrefabPath = "Assets/Game/Prefabs/Enemies/Grunt Enemy.prefab";
        private readonly HashSet<GameObject> cleanupObjects = new();

        [TearDown]
        public void TearDown()
        {
            foreach (GameObject cleanupObject in cleanupObjects)
            {
                if (cleanupObject != null)
                    Object.DestroyImmediate(cleanupObject);
            }

            cleanupObjects.Clear();
        }

        [UnityTest]
        public IEnumerator SequentialWavesTrackAndCompleteExactlyOnce()
        {
            EnemyCharacter first = CreateEnemyInstance("First Enemy");
            EnemyCharacter second = CreateEnemyInstance("Second Enemy");
            yield return null;
            first.gameObject.SetActive(false);
            second.gameObject.SetActive(false);

            EncounterController encounter = CreateEncounter(
                new EncounterWave(new[] { EncounterEnemyEntry.UseExisting(first) }),
                new EncounterWave(new[] { EncounterEnemyEntry.UseExisting(second) }));

            int completionCount = 0;
            encounter.EncounterCompleted += _ => completionCount++;
            encounter.StartEncounter();

            Assert.That(encounter.State, Is.EqualTo(EncounterState.Running));
            Assert.That(encounter.CurrentWaveIndex, Is.EqualTo(0));
            Assert.That(encounter.AliveEnemyCount, Is.EqualTo(1));
            Assert.That(first.gameObject.activeSelf, Is.True);
            Assert.That(second.gameObject.activeSelf, Is.False);

            Kill(first);
            yield return null;

            Assert.That(encounter.CurrentWaveIndex, Is.EqualTo(1));
            Assert.That(encounter.AliveEnemyCount, Is.EqualTo(1));
            Assert.That(second.gameObject.activeSelf, Is.True);

            Kill(second);
            yield return null;

            Assert.That(encounter.State, Is.EqualTo(EncounterState.Completed));
            Assert.That(completionCount, Is.EqualTo(1));
        }

        [UnityTest]
        public IEnumerator EmptyAndInvalidWavesCannotDeadlockEncounter()
        {
            EncounterController encounter = CreateEncounter(
                new EncounterWave(),
                null,
                new EncounterWave(new EncounterEnemyEntry[] { null }));

            int startedCount = 0;
            int completionCount = 0;
            encounter.EncounterStarted += _ => startedCount++;
            encounter.EncounterCompleted += _ => completionCount++;

            encounter.StartEncounter();

            Assert.That(encounter.State, Is.EqualTo(EncounterState.Completed));
            Assert.That(startedCount, Is.EqualTo(1));
            Assert.That(completionCount, Is.EqualTo(1));
            yield return null;
        }

        [Test]
        public void ConfigurationValidationRejectsNegativeDelayAndMissingSpawnData()
        {
            EncounterController encounter = CreateEncounter(
                new EncounterWave(new[] { new EncounterEnemyEntry() }, -1f));

            Assert.That(encounter.ValidateConfiguration(false), Is.GreaterThanOrEqualTo(3));
        }

        [UnityTest]
        public IEnumerator SpawnedAndExistingEntriesUseTheSameRegistrationPath()
        {
            EnemyCharacter existing = CreateEnemyInstance("Existing Enemy");
            GameObject spawnPointObject = Track(new GameObject("Spawn Point"));
            yield return null;
            existing.gameObject.SetActive(false);

            EnemyCharacter prefab = LoadEnemyPrefab().GetComponent<EnemyCharacter>();
            EncounterController encounter = CreateEncounter(new EncounterWave(new[]
            {
                EncounterEnemyEntry.SpawnPrefab(prefab, spawnPointObject.transform),
                EncounterEnemyEntry.UseExisting(existing)
            }));

            List<EnemyCharacter> registered = new();
            encounter.EnemyRegistered += enemy =>
            {
                registered.Add(enemy);
                Track(enemy.gameObject);
            };

            encounter.StartEncounter();
            yield return null;

            Assert.That(registered.Count, Is.EqualTo(2));
            Assert.That(encounter.AliveEnemyCount, Is.EqualTo(2));

            foreach (EnemyCharacter enemy in registered)
                Kill(enemy);

            yield return null;
            Assert.That(encounter.State, Is.EqualTo(EncounterState.Completed));
        }

        [UnityTest]
        public IEnumerator DuplicateExistingRegistrationIsIgnored()
        {
            EnemyCharacter enemy = CreateEnemyInstance("Duplicate Enemy");
            yield return null;
            enemy.gameObject.SetActive(false);

            EncounterController encounter = CreateEncounter(new EncounterWave(new[]
            {
                EncounterEnemyEntry.UseExisting(enemy),
                EncounterEnemyEntry.UseExisting(enemy)
            }));

            int registeredCount = 0;
            encounter.EnemyRegistered += _ => registeredCount++;
            encounter.StartEncounter();

            Assert.That(registeredCount, Is.EqualTo(1));
            Assert.That(encounter.AliveEnemyCount, Is.EqualTo(1));

            Kill(enemy);
            yield return null;
            Assert.That(encounter.State, Is.EqualTo(EncounterState.Completed));
        }

        [UnityTest]
        public IEnumerator DeathDuringRegistrationIsHandledOnce()
        {
            EnemyCharacter enemy = CreateEnemyInstance("Immediate Death Enemy");
            yield return null;

            EncounterController encounter = CreateEncounter(
                new EncounterWave(new[] { EncounterEnemyEntry.UseExisting(enemy) }));

            int deathCount = 0;
            encounter.EnemyRegistered += Kill;
            encounter.EnemyDied += _ => deathCount++;
            encounter.StartEncounter();

            Assert.That(encounter.State, Is.EqualTo(EncounterState.Completed));
            Assert.That(encounter.AliveEnemyCount, Is.Zero);
            Assert.That(deathCount, Is.EqualTo(1));
        }

        [UnityTest]
        public IEnumerator DestroyedParticipantIsPrunedWithoutPublishingDeath()
        {
            EnemyCharacter enemy = CreateEnemyInstance("Destroyed Enemy");
            yield return null;

            EncounterController encounter = CreateEncounter(
                new EncounterWave(new[] { EncounterEnemyEntry.UseExisting(enemy) }));

            int deathCount = 0;
            encounter.EnemyDied += _ => deathCount++;
            encounter.StartEncounter();

            LogAssert.Expect(
                LogType.Warning,
                new System.Text.RegularExpressions.Regex("pruned a destroyed participant"));
            Object.Destroy(enemy.gameObject);
            yield return null;
            yield return null;

            Assert.That(encounter.State, Is.EqualTo(EncounterState.Completed));
            Assert.That(deathCount, Is.Zero);
        }

        [UnityTest]
        public IEnumerator DisabledParticipantRemainsUntilExplicitlyUnregistered()
        {
            EnemyCharacter enemy = CreateEnemyInstance("Disabled Enemy");
            yield return null;

            EncounterController encounter = CreateEncounter(
                new EncounterWave(new[] { EncounterEnemyEntry.UseExisting(enemy) }));

            int deathCount = 0;
            encounter.EnemyDied += _ => deathCount++;
            encounter.StartEncounter();
            enemy.gameObject.SetActive(false);
            yield return null;

            Assert.That(encounter.State, Is.EqualTo(EncounterState.Running));
            Assert.That(encounter.AliveEnemyCount, Is.EqualTo(1));
            Assert.That(encounter.UnregisterEnemy(enemy), Is.True);
            yield return null;

            Assert.That(encounter.State, Is.EqualTo(EncounterState.Completed));
            Assert.That(deathCount, Is.Zero);
        }

        [UnityTest]
        public IEnumerator CancellationCleansTrackingWithoutKillingOrCompleting()
        {
            EnemyCharacter enemy = CreateEnemyInstance("Cancelled Enemy");
            yield return null;

            EncounterController encounter = CreateEncounter(
                new EncounterWave(new[] { EncounterEnemyEntry.UseExisting(enemy) }));

            int registeredCount = 0;
            int completionCount = 0;
            encounter.EnemyRegistered += _ => registeredCount++;
            encounter.EncounterCompleted += _ => completionCount++;

            encounter.StartEncounter();
            encounter.StartEncounter();
            encounter.CancelEncounter();
            yield return null;

            Assert.That(registeredCount, Is.EqualTo(1));
            Assert.That(encounter.State, Is.EqualTo(EncounterState.Cancelled));
            Assert.That(encounter.AliveEnemyCount, Is.Zero);
            Assert.That(enemy.IsAlive, Is.True);
            Assert.That(enemy.gameObject.activeSelf, Is.True);
            Assert.That(completionCount, Is.Zero);
        }

        [UnityTest]
        public IEnumerator CancellationDuringRegistrationStopsRemainingEntries()
        {
            EnemyCharacter first = CreateEnemyInstance("First Cancel Enemy");
            EnemyCharacter second = CreateEnemyInstance("Second Cancel Enemy");
            yield return null;

            EncounterController encounter = CreateEncounter(new EncounterWave(new[]
            {
                EncounterEnemyEntry.UseExisting(first),
                EncounterEnemyEntry.UseExisting(second)
            }));

            int registeredCount = 0;
            encounter.EnemyRegistered += _ =>
            {
                registeredCount++;
                encounter.CancelEncounter();
            };

            encounter.StartEncounter();

            Assert.That(encounter.State, Is.EqualTo(EncounterState.Cancelled));
            Assert.That(registeredCount, Is.EqualTo(1));
            Assert.That(encounter.AliveEnemyCount, Is.Zero);
            Assert.That(first.IsAlive, Is.True);
            Assert.That(second.IsAlive, Is.True);
        }

        [Test]
        public void LevelSequenceDelegatesEncounterOwnership()
        {
            string source = File.ReadAllText(
                "Assets/Game/Scripts/Managers/Level 0 Sequence Manager.cs");

            StringAssert.Contains("encounter.StartEncounter()", source);
            StringAssert.Contains("encounter.EnemyRegistered += OnEncounterEnemyRegistered", source);
            StringAssert.Contains("encounter.EnemyDied += OnEncounterEnemyDied", source);
            StringAssert.DoesNotContain("spawnedEnemies", source);
            StringAssert.DoesNotContain("enemiesAlive", source);
            StringAssert.DoesNotContain("SpawnEnemyWave", source);
            StringAssert.DoesNotContain("WaitForAllEnemiesDead", source);
        }

        [Test]
        public void TutorialReactionsRemainOutsideEncounterController()
        {
            string encounterSource = File.ReadAllText(
                "Assets/Game/Scripts/Encounters/EncounterController.cs");
            string levelSequenceSource = File.ReadAllText(
                "Assets/Game/Scripts/Managers/Level 0 Sequence Manager.cs");

            StringAssert.DoesNotContain("OnAttackNotifyShown", encounterSource);
            StringAssert.DoesNotContain("EnemyType.Hyena", encounterSource);
            StringAssert.Contains("OnAttackNotifyShown", levelSequenceSource);
            StringAssert.Contains("EnemyType.Hyena", levelSequenceSource);
        }

        private EncounterController CreateEncounter(params EncounterWave[] waves)
        {
            GameObject encounterObject = Track(new GameObject("Test Encounter"));
            EncounterController encounter = encounterObject.AddComponent<EncounterController>();
            Assert.That(encounter.ConfigureRuntimeWaves(waves), Is.True);
            return encounter;
        }

        private EnemyCharacter CreateEnemyInstance(string instanceName)
        {
            GameObject instance = Track(Object.Instantiate(LoadEnemyPrefab()));
            instance.name = instanceName;
            return instance.GetComponent<EnemyCharacter>();
        }

        private static GameObject LoadEnemyPrefab()
        {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(EnemyPrefabPath);
            Assert.That(prefab, Is.Not.Null, EnemyPrefabPath);
            return prefab;
        }

        private GameObject Track(GameObject gameObject)
        {
            cleanupObjects.Add(gameObject);
            return gameObject;
        }

        private static void Kill(EnemyCharacter enemy)
        {
            MethodInfo handleDeath = typeof(EnemyCharacter).GetMethod(
                "HandleDeath",
                BindingFlags.Instance | BindingFlags.NonPublic);

            Assert.That(handleDeath, Is.Not.Null);
            handleDeath.Invoke(enemy, null);
        }
    }
}
