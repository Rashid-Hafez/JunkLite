using System;
using System.IO;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace junklite
{
    public readonly struct ProgressionChange
    {
        public readonly int PreviousLevel;
        public readonly int Level;
        public readonly long ExperienceGained;
        public readonly bool Earned;
        public bool LeveledUp => Earned && Level > PreviousLevel;
        public ProgressionChange(int previous, int level, long gained, bool earned)
        { PreviousLevel = previous; Level = level; ExperienceGained = gained; Earned = earned; }
    }

    [DisallowMultipleComponent]
    public sealed class PlayerProgression : MonoBehaviour
    {
        public static PlayerProgression Instance { get; private set; }
        [SerializeField] private PlayerProgressionConfig config;
        public PlayerProgressionConfig Config => config;
        public long TotalExperience { get; private set; }
        public int Level => config.LevelForExperience(TotalExperience);
        public bool AtMaximum => Level >= config.MaxLevel;
        public long ExperienceInLevel => TotalExperience - config.ExperienceToReach(Level);
        public long ExperienceForNextLevel => AtMaximum ? 0 :
            config.ExperienceToReach(Level + 1) - config.ExperienceToReach(Level);
        public float Progress => AtMaximum ? 1f : (float)((double)ExperienceInLevel / ExperienceForNextLevel);
        public bool IsPreviewing { get; private set; }
        public event Action<ProgressionChange> Changed;
        private ProgressionSaveStore store;
        private bool dirty;
        private bool saveAllowed = true;
        private float saveAt;
        private long savedPreviewExperience;
        private bool ownsConfig;

        public static PlayerProgression Ensure(Transform parent = null)
        {
            if (Instance != null) return Instance;
            if (parent != null) return parent.GetComponent<PlayerProgression>() ?? parent.gameObject.AddComponent<PlayerProgression>();
            var go = new GameObject("Player Progression");
            DontDestroyOnLoad(go);
            return go.AddComponent<PlayerProgression>();
        }

        private void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(this); return; }
            Instance = this;
            if (config == null) config = Resources.Load<PlayerProgressionConfig>(PlayerProgressionConfig.ResourceName);
            if (config == null || !config.Validate(out _))
            {
                Debug.LogWarning("[Progression] Missing/invalid profile; using defaults. Run Tools > JunkLite > Progression > Install Defaults.", this);
                config = ScriptableObject.CreateInstance<PlayerProgressionConfig>();
                config.Generate();
                ownsConfig = true;
                saveAllowed = false; // A broken configuration must not overwrite an existing profile.
            }
            store = new ProgressionSaveStore(Path.Combine(Application.persistentDataPath, "player-progression.json"));
            if (store.TryLoad(out var data, out string warning))
            {
                TotalExperience = RestoreExperience(config, data);
                dirty = data.configRevision != config.revision;
                saveAt = Time.unscaledTime + 1f;
            }
            else if (warning != null) saveAllowed = false;
            if (warning != null) Debug.LogWarning("[Progression] " + warning, this);
            SceneManager.activeSceneChanged += OnSceneChanged;
        }

        public static long RestoreExperience(PlayerProgressionConfig profile, ProgressionSaveData data)
        {
            long xp = Math.Max(0, data.totalExperience);
            if (data.configRevision != profile.revision &&
                profile.migrationPolicy == ProgressionMigrationPolicy.PreserveLevelAndProgress)
            {
                int level = Mathf.Clamp(data.level, 1, profile.MaxLevel);
                xp = profile.ExperienceToReach(level);
                if (level < profile.MaxLevel)
                {
                    long cost = profile.ExperienceToReach(level + 1) - xp;
                    xp += Math.Min(cost - 1, (long)(cost * Math.Max(0, Math.Min(1, data.levelProgress))));
                }
            }
            return Math.Min(xp, profile.ExperienceToReach(profile.MaxLevel));
        }

        public void AddExperience(int amount)
        {
            if (amount <= 0 || AtMaximum) return;
            int oldLevel = Level;
            long oldXP = TotalExperience;
            TotalExperience = Math.Min(TotalExperience + amount, config.ExperienceToReach(config.MaxLevel));
            if (!IsPreviewing)
            {
                if (!dirty) saveAt = Time.unscaledTime + 1f;
                dirty = true;
            }
            Changed?.Invoke(new ProgressionChange(oldLevel, Level, TotalExperience - oldXP, true));
            if (Level > oldLevel) Flush();
        }

        /// <summary>All XP earned while previewing is temporary, including combat rewards.</summary>
        public void PreviewLevel(int level)
        {
            if (!IsPreviewing) { Flush(); savedPreviewExperience = TotalExperience; IsPreviewing = true; }
            int oldLevel = Level;
            TotalExperience = config.ExperienceToReach(Mathf.Clamp(level, 1, config.MaxLevel));
            Changed?.Invoke(new ProgressionChange(oldLevel, Level, 0, false));
        }

        public void EndPreview()
        {
            if (!IsPreviewing) return;
            int oldLevel = Level;
            TotalExperience = savedPreviewExperience;
            IsPreviewing = false;
            Changed?.Invoke(new ProgressionChange(oldLevel, Level, 0, false));
        }

        private void Update() { if (dirty && Time.unscaledTime >= saveAt) Flush(); }
        public void Flush()
        {
            if (!dirty || !saveAllowed || IsPreviewing || store == null) return;
            var data = new ProgressionSaveData
            { configRevision = config.revision, totalExperience = TotalExperience, level = Level, levelProgress = Progress };
            if (store.TrySave(data, out string error)) dirty = false;
            else { Debug.LogWarning("[Progression] Save failed: " + error, this); saveAt = Time.unscaledTime + 10f; }
        }
        private void OnSceneChanged(Scene previous, Scene next) => Flush();
        private void OnApplicationPause(bool paused) { if (paused) Flush(); }
        private void OnApplicationQuit() => Flush();
        private void OnDestroy()
        {
            if (Instance != this) return;
            EndPreview();
            Flush();
            SceneManager.activeSceneChanged -= OnSceneChanged;
            Instance = null;
            if (ownsConfig) Destroy(config);
        }
    }
}
