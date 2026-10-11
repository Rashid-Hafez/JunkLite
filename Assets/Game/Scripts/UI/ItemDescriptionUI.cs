using UnityEngine;
using UnityEngine.UI;
using TMPro;

namespace junklite
{
    public class ItemDescriptionUI : MonoBehaviour
    {
        #region Fields

        [Header("Icon")]
        [SerializeField] private Image iconImage;

        [Header("Text References")]
        [SerializeField] private TMP_Text itemNameText;
        [SerializeField] private TMP_Text descriptionText;
        [SerializeField] private TMP_Text statsText;
        [SerializeField] private Color statLabelColor = new(0.24f, 0.91f, 0.98f, 1f);
        [SerializeField] private Color statValueColor = new(0.9f, 0.94f, 1f, 1f);

        [Header("Empty State")]
        [SerializeField] private GameObject emptyLabel; // Optional "Select an item" label
        private GameObject progressionPanel;
        private TMP_Text levelText;
        private TMP_Text experienceText;
        private TMP_Text nextLevelText;
        private Image experienceFill;
        private TMP_Text cardTitle;
        private RectTransform detailsRule;
        private Vector2 itemStatsPosition;
        private Vector2 itemRulePosition;
        private float itemStatsFontSize;
        private float itemStatsLineSpacing;
        private bool playerLayoutConfigured;

        #endregion

        // -----------------------------------------------------------------------

        public void ConfigurePlayerProgression(GameObject panel, TMP_Text level, TMP_Text experience,
            TMP_Text nextLevel, Image fill, TMP_Text title, RectTransform rule)
        {
            progressionPanel = panel;
            levelText = level;
            experienceText = experience;
            nextLevelText = nextLevel;
            experienceFill = fill;
            cardTitle = title;
            detailsRule = rule;
            if (detailsRule != null) itemRulePosition = detailsRule.anchoredPosition;
            if (statsText != null)
            {
                itemStatsPosition = statsText.rectTransform.anchoredPosition;
                itemStatsFontSize = statsText.fontSize;
                itemStatsLineSpacing = statsText.lineSpacing;
            }
            playerLayoutConfigured = true;
            SetPlayerMode(false);
        }

        public void ShowPlayer(PlayerCharacter player, Sprite portrait)
        {
            if (player == null) { Clear(); return; }
            SetEmptyState(false);
            SetPlayerMode(true);
            var runtime = player.RuntimeStats;
            var progression = runtime?.Progression;
            var profile = progression != null ? progression.Config : null;
            int level = progression != null ? progression.Level : 1;
            if (iconImage != null)
            {
                iconImage.sprite = portrait;
                iconImage.enabled = portrait != null;
            }
            if (itemNameText != null)
                itemNameText.text = player.Stats != null ? player.Stats.characterName : "Cassidy";
            if (levelText != null) levelText.text = $"LEVEL {level}";
            if (experienceText != null)
                experienceText.text = progression == null ? "XP --" : progression.AtMaximum
                    ? $"{progression.TotalExperience:N0} TOTAL XP"
                    : $"{progression.ExperienceInLevel:N0} / {progression.ExperienceForNextLevel:N0} XP";
            if (nextLevelText != null)
                nextLevelText.text = progression != null && progression.AtMaximum ? "MAX LEVEL" : "";
            if (experienceFill != null)
            {
                // Geometry-based fill also works without an assigned sprite.
                experienceFill.rectTransform.anchorMax = new Vector2(progression != null ? progression.Progress : 0f, 1f);
                if (profile != null) experienceFill.color = profile.xpColor;
            }
            if (descriptionText != null) descriptionText.text = "";
            if (statsText != null)
            {
                Attribute health = player.Health;
                string healthValue = health != null ? $"{health.Current:0.#} / {health.Max:0.#}" : "--";
                float armor = runtime != null ? runtime.Armor : player.Stats != null ? player.Stats.armor : 0f;
                statsText.text = StatLine("HEALTH", healthValue) + "\n" +
                    StatLine("ARMOR", armor.ToString("0.##")) + "\n" +
                    StatLine("DAMAGE", BonusText(profile, ProgressionStat.OutgoingDamage, level));
            }
        }

        private string StatLine(string label, string value) =>
            $"<color=#{ColorUtility.ToHtmlStringRGB(statLabelColor)}>{label}</color>  " +
            $"<color=#{ColorUtility.ToHtmlStringRGB(statValueColor)}>{value}</color>";

        public void ShowLockedSlot(int requiredLevel)
        {
            Clear();
            SetEmptyState(false);
            if (itemNameText != null) itemNameText.text = "LOCKED MOD SLOT";
            if (descriptionText != null)
                descriptionText.text = requiredLevel > 0 ? $"Reach {requiredLevel} level to unlock" : "Slot locked";
        }

        private static string BonusText(PlayerProgressionConfig profile, ProgressionStat stat, int level)
        {
            var bonus = profile != null ? profile.BonusAt(stat, level) : default;
            if (bonus.flat > 0f && bonus.percent > 0f) return $"+{bonus.flat:0.##}, +{bonus.percent:0.##}%";
            return bonus.percent > 0f ? $"+{bonus.percent:0.##}%" : $"+{bonus.flat:0.##}";
        }

        private void SetPlayerMode(bool playerMode)
        {
            if (progressionPanel != null) progressionPanel.SetActive(playerMode);
            if (cardTitle != null) cardTitle.text = playerMode ? "STATS" : "ITEM DETAILS";
            if (!playerLayoutConfigured) return;
            if (descriptionText != null) descriptionText.gameObject.SetActive(!playerMode);
            if (statsText != null)
            {
                statsText.rectTransform.anchoredPosition = playerMode && descriptionText != null
                    ? descriptionText.rectTransform.anchoredPosition + Vector2.down * 24f : itemStatsPosition;
                statsText.fontSize = playerMode ? 16f : itemStatsFontSize;
                statsText.lineSpacing = playerMode ? 12f : itemStatsLineSpacing;
            }
            if (detailsRule != null)
                detailsRule.anchoredPosition = playerMode && descriptionText != null
                    ? descriptionText.rectTransform.anchoredPosition : itemRulePosition;
        }

        // -----------------------------------------------------------------------

        public void Configure(
            Image icon,
            TMP_Text itemName,
            TMP_Text description,
            TMP_Text stats,
            GameObject emptyState)
        {
            iconImage = icon;
            itemNameText = itemName;
            descriptionText = description;
            statsText = stats;
            emptyLabel = emptyState;
        }

        // -----------------------------------------------------------------------

        public void ShowWeapon(WeaponInstance instance)
        {
            SetPlayerMode(false);
            if (instance == null || instance.weaponData == null)
            {
                Clear();
                return;
            }

            var data = instance.weaponData;

            SetEmptyState(false);

            if (iconImage != null)
            {
                iconImage.sprite = data.icon;
                iconImage.enabled = data.icon != null;
            }

            if (itemNameText != null)
                itemNameText.text = string.IsNullOrEmpty(data.displayName) ? "Unknown Weapon" : data.displayName;

            if (descriptionText != null)
                descriptionText.text = string.IsNullOrEmpty(data.description) ? "" : data.description;

            if (statsText != null)
            {
                int combos = data.GetComboLength(AttackDirection.Side, true);

                statsText.text =
                    StatLine("BASE DAMAGE", data.baseDamage.ToString()) + "\n" +
                    StatLine("COMBOS", combos.ToString()) + "\n" +
                    StatLine("MAX DURABILITY", data.maxWeaponDurability.ToString()) + "\n" +
                    StatLine("CURRENT", instance.CurrentDurability.ToString("F0")) + "\n" +
                    StatLine("PER HIT", data.durabilityPerHit.ToString());
            }
        }

        // -----------------------------------------------------------------------

        public void ShowMod(ModInstance instance)
        {
            SetPlayerMode(false);
            if (instance == null || instance.Data == null)
            {
                Clear();
                return;
            }

            var data = instance.Data;

            SetEmptyState(false);

            if (iconImage != null)
            {
                iconImage.sprite = data.icon;
                iconImage.enabled = data.icon != null;
            }

            if (itemNameText != null)
                itemNameText.text = string.IsNullOrEmpty(data.modName) ? "Unknown Mod" : data.modName;

            if (descriptionText != null)
                descriptionText.text = string.IsNullOrEmpty(data.description) ? "" : data.description;

            if (statsText != null)
            {
                var sb = new System.Text.StringBuilder();

                if (data.baseDamage > 0f)
                    sb.AppendLine(StatLine("BASE DAMAGE", data.baseDamage.ToString()));

                sb.AppendLine(StatLine("MAX DURABILITY", data.maxDurability.ToString()));
                sb.AppendLine(StatLine("CURRENT", instance.CurrentDurability.ToString("F0")));
                sb.AppendLine(StatLine("PER USE", data.durabilityPerUse.ToString()));

                // Active-mod-only stats
                if (data is ActiveModData activeMod)
                {
                    if (activeMod.cooldown > 0f)
                        sb.AppendLine(StatLine("COOLDOWN", $"{activeMod.cooldown}s"));

                    if (activeMod.chargesRequired > 0)
                        sb.AppendLine(StatLine("CHARGES", activeMod.chargesRequired.ToString()));
                }

                statsText.text = sb.ToString().TrimEnd();
            }
        }

        // -----------------------------------------------------------------------

        public void Clear()
        {
            SetPlayerMode(false);
            SetEmptyState(true);

            if (iconImage != null) { iconImage.sprite = null; iconImage.enabled = false; }
            if (itemNameText != null) itemNameText.text = "";
            if (descriptionText != null) descriptionText.text = "";
            if (statsText != null) statsText.text = "";
        }

        // -----------------------------------------------------------------------

        private void SetEmptyState(bool isEmpty)
        {
            if (emptyLabel != null)
                emptyLabel.SetActive(isEmpty);
        }
    }
}
