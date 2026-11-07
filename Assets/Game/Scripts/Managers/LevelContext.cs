using System.Collections.Generic;
using UnityEngine;

namespace junklite
{
    /// <summary>Which player prefab a level spawns. Game Default follows the Game Manager prefab's Use New Player.</summary>
    public enum LevelPlayerChoice
    {
        GameDefault,
        OldPlayer,
        NewPlayer,
    }

    /// <summary>Optional per-level music. Empty Music keeps the AudioLibrary's level track.</summary>
    [System.Serializable]
    public sealed class LevelMusicSettings
    {
        [Tooltip("Looping track. Empty = the AudioLibrary level music.")]
        public SoundEntry music = new();
        [Tooltip("Optional: plays once, then Music loops gaplessly from its first sample.")]
        public AudioClip intro;
        [Tooltip("Keep this track during fights instead of switching to the combat music.")]
        public bool playThroughCombat = true;

        public bool IsSet => music != null && music.IsValid;
    }

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
        [Tooltip("Override the player this level spawns. Game Default uses the Game Manager prefab's Use New Player.")]
        [SerializeField] private LevelPlayerChoice playerChoice = LevelPlayerChoice.GameDefault;
        [SerializeField] private SpawnPoint primaryPlayerSpawn;
        [SerializeField] private List<SpawnPoint> additionalPlayerSpawns = new();

        [Header("Music")]
        [SerializeField] private LevelMusicSettings levelMusic = new();

        public string LevelId => levelId;
        public string DisplayName => displayName;
        public bool IsTrainingLevel => trainingLevel;
        public bool SpawnPlayer => spawnPlayer;
        public LevelPlayerChoice PlayerChoice => playerChoice;
        public LevelMusicSettings LevelMusic => levelMusic;

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
