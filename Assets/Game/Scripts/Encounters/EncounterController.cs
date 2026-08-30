using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace junklite
{
    /// <summary>
    /// Scene-local owner of encounter participants and sequential wave completion.
    /// Level sequences decide when to start it and how to react to completion.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class EncounterController : MonoBehaviour
    {
        [SerializeField] private bool startOnStart;
        [SerializeField] private List<EncounterWave> waves = new();

        private readonly HashSet<EnemyCharacter> livingEnemies = new();
        private readonly HashSet<EnemyCharacter> usedExistingEnemies = new();
        private readonly List<EnemyCharacter> pruneBuffer = new();

        private Coroutine progressionRoutine;
        private bool completionPublished;

        public EncounterState State { get; private set; } = EncounterState.Idle;
        public int CurrentWaveIndex { get; private set; } = -1;
        public int AliveEnemyCount => livingEnemies.Count;
        public int ConfiguredWaveCount => waves?.Count ?? 0;
        public bool StartOnStart => startOnStart;

        public event Action<EncounterController> EncounterStarted;
        public event Action<EnemyCharacter> EnemyRegistered;
        public event Action<EnemyCharacter> EnemyDied;
        public event Action<EncounterController> EncounterCompleted;

        private void Start()
        {
            if (startOnStart)
                StartEncounter();
        }

        public void StartEncounter()
        {
            if (State != EncounterState.Idle)
            {
                Debug.LogWarning(
                    $"[Encounter] '{name}' cannot start from state {State}.",
                    this);
                return;
            }

            if (!isActiveAndEnabled)
            {
                Debug.LogWarning(
                    $"[Encounter] '{name}' must be active and enabled before it can start.",
                    this);
                return;
            }

            livingEnemies.Clear();
            usedExistingEnemies.Clear();
            CurrentWaveIndex = -1;
            completionPublished = false;
            State = EncounterState.Running;

            EncounterStarted?.Invoke(this);
            if (State != EncounterState.Running)
                return;

            if (waves == null || waves.Count == 0)
            {
                Debug.LogWarning(
                    $"[Encounter] '{name}' has no configured waves and will complete immediately.",
                    this);
            }

            progressionRoutine = StartCoroutine(RunEncounter());
        }

        public void CancelEncounter()
        {
            if (State == EncounterState.Completed || State == EncounterState.Cancelled)
                return;

            if (progressionRoutine != null)
            {
                StopCoroutine(progressionRoutine);
                progressionRoutine = null;
            }

            UnsubscribeAllParticipants();
            usedExistingEnemies.Clear();
            State = EncounterState.Cancelled;
        }

        public bool UnregisterEnemy(EnemyCharacter enemy)
        {
            if (enemy == null || !livingEnemies.Remove(enemy))
                return false;

            enemy.Died -= HandleEnemyDied;
            return true;
        }

        public IEnumerator WaitUntilFinished()
        {
            while (State == EncounterState.Running)
                yield return null;
        }

        /// <summary>
        /// Supplies unsaved runtime data for narrow legacy migration bridges.
        /// Native scene encounters should use the serialized wave list instead.
        /// </summary>
        public bool ConfigureRuntimeWaves(IEnumerable<EncounterWave> runtimeWaves)
        {
            if (State != EncounterState.Idle)
            {
                Debug.LogWarning(
                    $"[Encounter] '{name}' cannot be configured from state {State}.",
                    this);
                return false;
            }

            waves = runtimeWaves != null
                ? new List<EncounterWave>(runtimeWaves)
                : new List<EncounterWave>();
            return true;
        }

        public int ValidateConfiguration(bool logWarnings = true)
        {
            int issueCount = 0;

            void Report(string message)
            {
                issueCount++;
                if (logWarnings)
                    Debug.LogWarning($"[Encounter] '{name}' {message}", this);
            }

            if (waves == null || waves.Count == 0)
            {
                Report("has no waves.");
                return issueCount;
            }

            HashSet<EnemyCharacter> existingEnemies = new();

            for (int waveIndex = 0; waveIndex < waves.Count; waveIndex++)
            {
                EncounterWave wave = waves[waveIndex];
                if (wave == null)
                {
                    Report($"has a null wave at index {waveIndex}.");
                    continue;
                }

                if (wave.DelayBeforeWave < 0f)
                    Report($"wave {waveIndex} has a negative delay.");

                IReadOnlyList<EncounterEnemyEntry> entries = wave.Enemies;
                for (int entryIndex = 0; entryIndex < entries.Count; entryIndex++)
                {
                    EncounterEnemyEntry entry = entries[entryIndex];
                    string location = $"wave {waveIndex}, entry {entryIndex}";
                    if (entry == null)
                    {
                        Report($"has a null enemy entry in {location}.");
                        continue;
                    }

                    bool hasPrefab = entry.EnemyPrefab != null;
                    bool hasExisting = entry.ExistingEnemy != null;
                    if (hasPrefab && hasExisting)
                        Report($"assigns both a prefab and existing enemy in {location}.");

                    if (entry.SourceMode == EncounterEnemySourceMode.SpawnPrefab)
                    {
                        if (!hasPrefab)
                            Report($"is missing its enemy prefab in {location}.");
                        else if (entry.EnemyPrefab.gameObject.scene.IsValid())
                            Report($"uses a scene enemy where a prefab asset is required in {location}.");
                        if (entry.SpawnTransform == null)
                            Report($"is missing its spawn transform in {location}.");
                        if (hasExisting)
                            Report($"assigns an existing enemy to a prefab entry in {location}.");
                    }
                    else
                    {
                        if (!hasExisting)
                            Report($"is missing its existing enemy in {location}.");
                        else if (!entry.ExistingEnemy.gameObject.scene.IsValid())
                            Report($"uses a prefab asset where a scene enemy is required in {location}.");
                        if (hasPrefab || entry.SpawnTransform != null)
                            Report($"assigns prefab-spawn data to an existing-enemy entry in {location}.");
                        if (hasExisting && !existingEnemies.Add(entry.ExistingEnemy))
                            Report($"uses the same existing enemy more than once ({location}).");
                    }
                }
            }

            return issueCount;
        }

        private IEnumerator RunEncounter()
        {
            int waveCount = waves?.Count ?? 0;
            for (int waveIndex = 0; waveIndex < waveCount; waveIndex++)
            {
                if (State != EncounterState.Running)
                    yield break;

                CurrentWaveIndex = waveIndex;
                EncounterWave wave = waves[waveIndex];
                if (wave == null)
                {
                    Debug.LogWarning(
                        $"[Encounter] '{name}' skipped null wave {waveIndex}.",
                        this);
                    continue;
                }

                if (wave.DelayBeforeWave < 0f)
                {
                    Debug.LogWarning(
                        $"[Encounter] '{name}' wave {waveIndex} has a negative delay; using zero.",
                        this);
                }

                float remainingDelay = Mathf.Max(0f, wave.DelayBeforeWave);
                while (remainingDelay > 0f && State == EncounterState.Running)
                {
                    remainingDelay -= Time.deltaTime;
                    yield return null;
                }

                if (State != EncounterState.Running)
                    yield break;

                IReadOnlyList<EncounterEnemyEntry> entries = wave.Enemies;
                for (int entryIndex = 0;
                     entryIndex < entries.Count && State == EncounterState.Running;
                     entryIndex++)
                {
                    StartEntry(entries[entryIndex], waveIndex, entryIndex);
                }

                while (State == EncounterState.Running && livingEnemies.Count > 0)
                {
                    PruneDestroyedParticipants();
                    if (livingEnemies.Count > 0)
                        yield return null;
                }
            }

            if (State == EncounterState.Running)
                CompleteEncounter();
        }

        private void StartEntry(
            EncounterEnemyEntry entry,
            int waveIndex,
            int entryIndex)
        {
            string location = $"wave {waveIndex}, entry {entryIndex}";
            if (entry == null)
            {
                WarnSkippedEntry(location, "the entry is null");
                return;
            }

            switch (entry.SourceMode)
            {
                case EncounterEnemySourceMode.SpawnPrefab:
                    StartPrefabEntry(entry, location);
                    break;

                case EncounterEnemySourceMode.ExistingEnemy:
                    StartExistingEntry(entry, location);
                    break;

                default:
                    WarnSkippedEntry(location, "the source mode is invalid");
                    break;
            }
        }

        private void StartPrefabEntry(EncounterEnemyEntry entry, string location)
        {
            if (entry.EnemyPrefab == null || entry.SpawnTransform == null)
            {
                WarnSkippedEntry(location, "the prefab or spawn transform is missing");
                return;
            }

            if (entry.EnemyPrefab.gameObject.scene.IsValid())
            {
                WarnSkippedEntry(location, "the assigned enemy is a scene object rather than a prefab asset");
                return;
            }

            if (entry.ExistingEnemy != null)
            {
                WarnSkippedEntry(location, "both prefab and existing-enemy data are assigned");
                return;
            }

            EnemyCharacter enemy = Instantiate(
                entry.EnemyPrefab,
                entry.SpawnTransform.position,
                entry.SpawnTransform.rotation);

            TryRegisterEnemy(enemy, location);
        }

        private void StartExistingEntry(EncounterEnemyEntry entry, string location)
        {
            if (entry.ExistingEnemy == null)
            {
                WarnSkippedEntry(location, "the existing enemy is missing");
                return;
            }

            if (!entry.ExistingEnemy.gameObject.scene.IsValid())
            {
                WarnSkippedEntry(location, "the assigned existing enemy is a prefab asset");
                return;
            }

            if (entry.EnemyPrefab != null || entry.SpawnTransform != null)
            {
                WarnSkippedEntry(location, "prefab-spawn data is assigned to an existing enemy");
                return;
            }

            EnemyCharacter enemy = entry.ExistingEnemy;
            if (!usedExistingEnemies.Add(enemy))
            {
                WarnSkippedEntry(location, $"existing enemy '{enemy.name}' is already used by this encounter");
                return;
            }

            if (!TryRegisterEnemy(enemy, location))
                return;

            // Subscribe and publish registration before activation so OnEnable-driven
            // death cannot escape encounter tracking.
            if (entry.ActivateExistingEnemy &&
                livingEnemies.Contains(enemy) &&
                enemy.IsAlive &&
                !enemy.gameObject.activeSelf)
            {
                enemy.gameObject.SetActive(true);
            }
        }

        private bool TryRegisterEnemy(EnemyCharacter enemy, string location)
        {
            if (enemy == null)
            {
                WarnSkippedEntry(location, "no EnemyCharacter was resolved");
                return false;
            }

            if (!enemy.IsAlive)
            {
                WarnSkippedEntry(location, $"enemy '{enemy.name}' is already dead");
                return false;
            }

            if (!livingEnemies.Add(enemy))
            {
                WarnSkippedEntry(location, $"enemy '{enemy.name}' is already registered");
                return false;
            }

            enemy.Died += HandleEnemyDied;
            EnemyRegistered?.Invoke(enemy);

            if (livingEnemies.Contains(enemy) && !enemy.IsAlive)
                HandleEnemyDied(enemy);

            return true;
        }

        private void HandleEnemyDied(EnemyCharacter enemy)
        {
            if (enemy == null || !livingEnemies.Remove(enemy))
                return;

            enemy.Died -= HandleEnemyDied;
            EnemyDied?.Invoke(enemy);
        }

        private void PruneDestroyedParticipants()
        {
            pruneBuffer.Clear();
            foreach (EnemyCharacter enemy in livingEnemies)
            {
                if (enemy == null)
                    pruneBuffer.Add(enemy);
            }

            for (int i = 0; i < pruneBuffer.Count; i++)
            {
                EnemyCharacter destroyedEnemy = pruneBuffer[i];
                livingEnemies.Remove(destroyedEnemy);
                Debug.LogWarning(
                    $"[Encounter] '{name}' pruned a destroyed participant without reporting a kill.",
                    this);
            }

            pruneBuffer.Clear();
        }

        private void CompleteEncounter()
        {
            if (completionPublished)
                return;

            progressionRoutine = null;
            completionPublished = true;
            State = EncounterState.Completed;
            EncounterCompleted?.Invoke(this);
        }

        private void UnsubscribeAllParticipants()
        {
            foreach (EnemyCharacter enemy in livingEnemies)
            {
                if (enemy != null)
                    enemy.Died -= HandleEnemyDied;
            }

            livingEnemies.Clear();
            pruneBuffer.Clear();
        }

        private void WarnSkippedEntry(string location, string reason)
        {
            Debug.LogWarning(
                $"[Encounter] '{name}' skipped {location}: {reason}.",
                this);
        }

        private void OnDisable()
        {
            if (State == EncounterState.Running)
                CancelEncounter();
        }

#if UNITY_EDITOR
        private void OnValidate()
        {
            ValidateConfiguration();
        }
#endif
    }
}
