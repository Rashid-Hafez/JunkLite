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

        [Header("Empty State")]
        [SerializeField] private GameObject emptyLabel; // Optional "Select an item" label

        #endregion

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
                    $"DAMAGE  {data.baseDamage}\n" +
                    $"COMBOS  {combos}\n" +
                    $"MAX DURABILITY  {data.maxWeaponDurability}\n" +
                    $"CURRENT  {instance.CurrentDurability:F0}\n" +
                    $"PER HIT  {data.durabilityPerHit}";
            }
        }

        // -----------------------------------------------------------------------

        public void ShowMod(ModInstance instance)
        {
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
                    sb.AppendLine($"DAMAGE  {data.baseDamage}");

                sb.AppendLine($"MAX DURABILITY  {data.maxDurability}");
                sb.AppendLine($"CURRENT  {instance.CurrentDurability:F0}");
                sb.AppendLine($"PER USE  {data.durabilityPerUse}");

                // Active-mod-only stats
                if (data is ActiveModData activeMod)
                {
                    if (activeMod.cooldown > 0f)
                        sb.AppendLine($"COOLDOWN  {activeMod.cooldown}s");

                    if (activeMod.chargesRequired > 0)
                        sb.AppendLine($"CHARGES  {activeMod.chargesRequired}");
                }

                statsText.text = sb.ToString().TrimEnd();
            }
        }

        // -----------------------------------------------------------------------

        public void Clear()
        {
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
