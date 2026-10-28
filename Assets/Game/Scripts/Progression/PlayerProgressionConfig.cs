using System;
using System.Collections.Generic;
using UnityEngine;

namespace junklite
{
    public enum LevelUpHealthPolicy { KeepCurrent, KeepPercentage, AddCapacity, FullHeal }
    public enum ProgressionMigrationPolicy { PreserveLevelAndProgress, PreserveTotalExperience }

    [Serializable]
    public struct ProgressionStatBonus
    {
        public PlayerStat stat;
        public float flat;
        [Tooltip("Additive percentage points: 3 means +3%, not x3.")]
        public float percent;
    }

    [Serializable]
    public sealed class ProgressionLevel
    {
        [Min(1), Tooltip("XP needed from the preceding level. Entry 0 grants level 2.")]
        public int experienceRequired = 100;
        [Tooltip("Optional automatic bonuses earned at this level. The shipped profile leaves these empty: growth comes from the upgrade tree.")]
        public List<ProgressionStatBonus> bonuses = new();
    }

    /// <summary>
    /// XP table and level-up presentation. Each earned level grants upgrade points
    /// (<see cref="UpgradeTreeConfig.pointsPerLevel"/>); per-level stat bonuses remain supported but ship empty.
    /// </summary>
    [CreateAssetMenu(menuName = "Junklite/Progression/Player Progression", fileName = "PlayerProgressionConfig")]
    public sealed class PlayerProgressionConfig : ScriptableObject
    {
        public const string ResourceName = "PlayerProgressionConfig";
        [Tooltip("Increment when rebalancing an existing save's level requirements.")]
        [Min(1)] public int revision = 1;
        public ProgressionMigrationPolicy migrationPolicy = ProgressionMigrationPolicy.PreserveLevelAndProgress;
        public LevelUpHealthPolicy healthPolicy = LevelUpHealthPolicy.AddCapacity;
        [Tooltip("Entry 0 = level 2. The last entry determines the maximum level.")]
        public List<ProgressionLevel> levels = new();

        [Header("HUD and feedback")]
        public Color xpColor = new(0.24f, 0.91f, 0.98f, 1f);
        public Color levelUpColor = new(0.86f, 0.25f, 0.89f, 1f);
        [Min(0.05f)] public float xpAnimationDuration = 0.3f;
        [UnityEngine.Serialization.FormerlySerializedAs("pulseDuration")]
        [Min(0.05f)] public float portraitHighlightDuration = 0.7f;
        [Min(0.1f)] public float notificationDuration = 2.5f;
        public bool showLevelUpRipple = true;
        [Tooltip("Optional override. Unassigned reuses Cassidy's parry screen-distortion effect, including its authored graph/settings.")]
        public GameObject levelUpRipplePrefab;
        [Min(0.1f)] public float rippleLifetime = 2f;
        [Min(0.01f)] public float rippleScale = 1f;
        public AudioClip levelUpSound;
        [Range(0f, 1f)] public float soundVolume = 0.7f;

        [Header("XP popups")]
        public bool showXpPopups = true;
        [Tooltip("Offset from the player's center along the camera's right/up axes, in world units.")]
        public Vector2 xpPopupOffset = new(0f, 1.25f);
        [Min(0f)] public float xpPopupHorizontalSpread = 0.35f;
        [Min(0.1f)] public float xpPopupLifetime = 1.2f;
        [Min(0f)] public float xpPopupRiseSpeed = 1f;
        [Min(0.01f), Tooltip("Size relative to the existing damage-number prefab. Uses XP Color above.")]
        public float xpPopupScale = 0.8f;

        public int MaxLevel => (levels?.Count ?? 0) + 1;

        public long ExperienceToReach(int level)
        {
            long total = 0;
            for (int i = 0; i < Mathf.Clamp(level - 1, 0, MaxLevel - 1); i++)
                total += Mathf.Max(1, levels[i]?.experienceRequired ?? 1);
            return total;
        }

        public int LevelForExperience(long experience)
        {
            long remaining = Math.Max(0, experience);
            for (int i = 0; i < MaxLevel - 1; i++)
            {
                int cost = Mathf.Max(1, levels[i]?.experienceRequired ?? 1);
                if (remaining < cost) return i + 1;
                remaining -= cost;
            }
            return MaxLevel;
        }

        public ProgressionStatBonus BonusAt(PlayerStat stat, int level)
        {
            var total = new ProgressionStatBonus { stat = stat };
            for (int i = 0; i < Mathf.Clamp(level - 1, 0, MaxLevel - 1); i++)
            {
                if (levels[i]?.bonuses == null) continue;
                foreach (var bonus in levels[i].bonuses)
                {
                    if (bonus.stat != stat) continue;
                    total.flat += bonus.flat;
                    total.percent += bonus.percent;
                }
            }
            return total;
        }

        public float Evaluate(PlayerStat stat, float baseValue, int level)
        {
            ProgressionStatBonus bonus = BonusAt(stat, level);
            return Mathf.Max(0f, (baseValue + bonus.flat) * (1f + bonus.percent * 0.01f));
        }

        /// <summary>
        /// Builds the XP table. Per-level stat growth defaults to zero because growth is bought in the
        /// upgrade tree; pass values to generate the old automatic rewards.
        /// </summary>
        public void Generate(int maximumLevel = 20, int initialCost = 100, int costGrowth = 50,
            float healthPerLevel = 0f, float damagePercentPerLevel = 0f)
        {
            levels = new List<ProgressionLevel>();
            for (int level = 2; level <= Mathf.Clamp(maximumLevel, 2, 1000); level++)
            {
                var entry = new ProgressionLevel
                {
                    experienceRequired = (int)Math.Min(int.MaxValue,
                        Math.Max(1L, (long)initialCost + (long)(level - 2) * costGrowth)),
                    bonuses = new List<ProgressionStatBonus>()
                };
                if (healthPerLevel > 0f)
                    entry.bonuses.Add(new ProgressionStatBonus { stat = PlayerStat.MaxHealth, flat = healthPerLevel });
                if (damagePercentPerLevel > 0f)
                    entry.bonuses.Add(new ProgressionStatBonus { stat = PlayerStat.OutgoingDamage, percent = damagePercentPerLevel });
                levels.Add(entry);
            }
        }

        public bool Validate(out string error)
        {
            if (revision < 1 || levels == null || levels.Count == 0)
            { error = "A positive revision and at least one level are required."; return false; }
            foreach (var level in levels)
            {
                if (level == null || level.experienceRequired < 1 || level.bonuses == null)
                { error = "Every level needs a positive XP cost and a bonus list."; return false; }
                foreach (var bonus in level.bonuses)
                    if (!Enum.IsDefined(typeof(PlayerStat), bonus.stat) ||
                        !float.IsFinite(bonus.flat) || !float.IsFinite(bonus.percent) ||
                        bonus.flat < 0f || bonus.percent < 0f)
                    { error = "Stat rewards must use supported stats and finite, non-negative values."; return false; }
            }
            if (!Enum.IsDefined(typeof(LevelUpHealthPolicy), healthPolicy) ||
                !Enum.IsDefined(typeof(ProgressionMigrationPolicy), migrationPolicy) ||
                !float.IsFinite(xpAnimationDuration) || xpAnimationDuration <= 0 ||
                !float.IsFinite(portraitHighlightDuration) || portraitHighlightDuration <= 0 ||
                !float.IsFinite(notificationDuration) || notificationDuration <= 0 ||
                !float.IsFinite(rippleLifetime) || rippleLifetime <= 0 ||
                !float.IsFinite(rippleScale) || rippleScale <= 0 ||
                !float.IsFinite(soundVolume) || soundVolume < 0 || soundVolume > 1)
            { error = "Select valid policies and finite, positive feedback durations/scales."; return false; }
            if (!float.IsFinite(xpPopupOffset.x) || !float.IsFinite(xpPopupOffset.y) ||
                !float.IsFinite(xpPopupHorizontalSpread) || xpPopupHorizontalSpread < 0 ||
                !float.IsFinite(xpPopupLifetime) || xpPopupLifetime <= 0 ||
                !float.IsFinite(xpPopupRiseSpeed) || xpPopupRiseSpeed < 0 ||
                !float.IsFinite(xpPopupScale) || xpPopupScale <= 0)
            { error = "XP popups need finite offsets, non-negative spread/speed, and positive lifetime/scale."; return false; }
            error = null;
            return true;
        }
    }
}
