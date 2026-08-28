using System;
using System.Collections.Generic;
using UnityEngine;

namespace junklite
{
    public enum EncounterState
    {
        Idle,
        Running,
        Completed,
        Cancelled
    }

    public enum EncounterEnemySourceMode
    {
        SpawnPrefab,
        ExistingEnemy
    }

    [Serializable]
    public sealed class EncounterEnemyEntry
    {
        [SerializeField] private EncounterEnemySourceMode sourceMode;
        [SerializeField] private EnemyCharacter enemyPrefab;
        [SerializeField] private Transform spawnTransform;
        [SerializeField] private EnemyCharacter existingEnemy;

        public EncounterEnemySourceMode SourceMode => sourceMode;
        public EnemyCharacter EnemyPrefab => enemyPrefab;
        public Transform SpawnTransform => spawnTransform;
        public EnemyCharacter ExistingEnemy => existingEnemy;

        public static EncounterEnemyEntry SpawnPrefab(
            EnemyCharacter prefab,
            Transform spawnPoint)
        {
            return new EncounterEnemyEntry
            {
                sourceMode = EncounterEnemySourceMode.SpawnPrefab,
                enemyPrefab = prefab,
                spawnTransform = spawnPoint
            };
        }

        public static EncounterEnemyEntry UseExisting(EnemyCharacter enemy)
        {
            return new EncounterEnemyEntry
            {
                sourceMode = EncounterEnemySourceMode.ExistingEnemy,
                existingEnemy = enemy
            };
        }
    }

    [Serializable]
    public sealed class EncounterWave
    {
        [SerializeField, Min(0f)] private float delayBeforeWave;
        [SerializeField] private List<EncounterEnemyEntry> enemies = new();

        public float DelayBeforeWave => delayBeforeWave;
        public IReadOnlyList<EncounterEnemyEntry> Enemies => enemies != null
            ? enemies
            : (IReadOnlyList<EncounterEnemyEntry>)Array.Empty<EncounterEnemyEntry>();

        public EncounterWave() { }

        public EncounterWave(
            IEnumerable<EncounterEnemyEntry> entries,
            float delay = 0f)
        {
            delayBeforeWave = delay;
            enemies = entries != null
                ? new List<EncounterEnemyEntry>(entries)
                : new List<EncounterEnemyEntry>();
        }
    }
}
