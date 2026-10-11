using System;
using System.IO;
using UnityEngine;

namespace junklite
{
    [Serializable]
    public sealed class ProgressionSaveData
    {
        public int version = 1;
        public int configRevision;
        public long totalExperience;
        public int level = 1;
        public double levelProgress;
    }

    /// <summary>One small character profile. No scene, inventory or transient combat state.</summary>
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
                return data != null && data.version == 1 && data.totalExperience >= 0 &&
                    data.level >= 1 && !double.IsNaN(data.levelProgress) &&
                    !double.IsInfinity(data.levelProgress) && data.levelProgress >= 0 && data.levelProgress <= 1;
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
