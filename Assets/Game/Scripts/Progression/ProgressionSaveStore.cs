using System;
using System.IO;
using UnityEngine;

namespace junklite
{
    /// <summary>
    /// One small character profile: experience plus permanent upgrades. Version 1 saves (XP only)
    /// load with empty upgrade data. No scene, inventory or transient combat state.
    /// </summary>
    [Serializable]
    public sealed class ProgressionSaveData
    {
        public const int CurrentVersion = 2;

        public int version = CurrentVersion;
        public int configRevision;
        public long totalExperience;
        public int level = 1;
        public double levelProgress;

        // Version 2: permanent upgrades.
        public int bonusUpgradePoints;
        public string[] purchasedUpgrades = Array.Empty<string>();
        public string[] ownedWeapons = Array.Empty<string>();
        public string[] collectedPointSources = Array.Empty<string>();
    }

    public sealed class ProgressionSaveStore
    {
        private readonly string path;
        private bool recoveredBackup;
        public ProgressionSaveStore(string path) => this.path = path;

        public bool TryLoad(out ProgressionSaveData data, out string error)
        {
            data = null;
            error = null;
            if (TryRead(path, out data)) return true;
            if (TryRead(path + ".bak", out data))
            {
                recoveredBackup = true;
                error = "Recovered character progression from the backup save.";
                return true;
            }
            if (File.Exists(path) || File.Exists(path + ".bak"))
                error = "Progression save could not be read. Saving is disabled to preserve it.";
            return false;
        }

        private static bool TryRead(string filename, out ProgressionSaveData data)
        {
            data = null;
            try
            {
                if (!File.Exists(filename)) return false;
                string json = File.ReadAllText(filename);
                // JsonUtility otherwise accepts empty objects as a new save.
                if (!json.Contains("\"totalExperience\"") || !json.Contains("\"version\"")) return false;
                data = JsonUtility.FromJson<ProgressionSaveData>(json);
                if (data == null || data.version < 1 || data.version > ProgressionSaveData.CurrentVersion ||
                    data.totalExperience < 0 || data.level < 1 || double.IsNaN(data.levelProgress) ||
                    double.IsInfinity(data.levelProgress) || data.levelProgress < 0 || data.levelProgress > 1 ||
                    data.bonusUpgradePoints < 0)
                    return false;
                data.purchasedUpgrades ??= Array.Empty<string>();
                data.ownedWeapons ??= Array.Empty<string>();
                data.collectedPointSources ??= Array.Empty<string>();
                data.version = ProgressionSaveData.CurrentVersion;
                return true;
            }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException || e is ArgumentException)
            { return false; }
        }

        public bool TrySave(ProgressionSaveData data, out string error)
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                string temporary = path + ".tmp";
                File.WriteAllText(temporary, JsonUtility.ToJson(data, true));
                if (File.Exists(path))
                    File.Replace(temporary, path, recoveredBackup ? null : path + ".bak");
                else
                    File.Move(temporary, path);
                recoveredBackup = false;
                error = null;
                return true;
            }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException || e is ArgumentException)
            { error = e.Message; return false; }
        }
    }
}
