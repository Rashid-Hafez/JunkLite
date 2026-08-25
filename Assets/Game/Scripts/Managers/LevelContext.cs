using System.Collections.Generic;
using UnityEngine;

namespace junklite
{
    /// <summary>
    /// Scene-local configuration consumed by the persistent GameRoot.
    /// Keep this object in the level; it must never be marked DontDestroyOnLoad.
    /// </summary>
    [DefaultExecutionOrder(-9000)]
    [DisallowMultipleComponent]
    public sealed class LevelContext : MonoBehaviour
    {
        [Header("Identity")]
        [SerializeField] private string levelId = "new_level";
        [SerializeField] private string displayName = "Gameplay Level";
        [SerializeField] private bool trainingLevel;

        [Header("Player")]
        [SerializeField] private bool spawnPlayer = true;
        [SerializeField] private SpawnPoint primaryPlayerSpawn;
        [SerializeField] private List<SpawnPoint> additionalPlayerSpawns = new();

        public string LevelId => levelId;
        public string DisplayName => displayName;
        public bool IsTrainingLevel => trainingLevel;
        public bool SpawnPlayer => spawnPlayer;

        public IReadOnlyList<Transform> GetPlayerSpawns()
        {
            var result = new List<Transform>();

            SpawnPoint primary = ResolvePrimarySpawn();
            if (primary != null)
                result.Add(primary.transform);

            foreach (SpawnPoint spawn in additionalPlayerSpawns)
            {
                if (spawn != null && !result.Contains(spawn.transform))
                    result.Add(spawn.transform);
            }

            return result;
        }

        private SpawnPoint ResolvePrimarySpawn()
        {
            if (primaryPlayerSpawn != null)
                return primaryPlayerSpawn;

            primaryPlayerSpawn = GetComponentInChildren<SpawnPoint>(true);
            return primaryPlayerSpawn;
        }

        private void OnValidate()
        {
            ResolvePrimarySpawn();

            if (string.IsNullOrWhiteSpace(levelId))
                levelId = gameObject.scene.IsValid() ? gameObject.scene.name : "new_level";

            if (string.IsNullOrWhiteSpace(displayName))
                displayName = levelId;
        }
    }
}
