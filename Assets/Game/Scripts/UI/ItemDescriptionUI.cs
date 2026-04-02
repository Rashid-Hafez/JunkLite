using UnityEngine;
using TMPro;

namespace junklite
{
    /// <summary>
    /// Populates the shared description box in the inventory screen.
    /// Call ShowWeapon() or ShowMod() when a slot is selected.
    /// Call Clear() when nothing is selected.
    /// </summary>
    public class ItemDescriptionUI : MonoBehaviour
    {
        #region Fields

        [Header("Text References")]
        [SerializeField] private TMP_Text itemNameText;
        [SerializeField] private TMP_Text descriptionText;
        [SerializeField] private TMP_Text statsText;

        [Header("Empty State")]
        [SerializeField] private GameObject emptyLabel; // Optional "Select an item" label

        #endregion

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

            if (itemNameText != null)
                itemNameText.text = string.IsNullOrEmpty(data.displayName) ? "Unknown Weapon" : data.displayName;

            if (descriptionText != null)
                descriptionText.text = string.IsNullOrEmpty(data.description) ? "" : data.description;

            if (statsText != null)
            {
                int combos = data.GetComboLength(AttackDirection.Side, true);

                statsText.text =
                    $"Damage: {data.baseDamage}\n" +
                    $"Combos Available: {combos}\n" +
                    $"Max Durability: {data.maxWeaponDurability}\n" +
                    $"Current Durability: {instance.CurrentDurability:F0}\n" +
                    $"Durability Per Hit: {data.durabilityPerHit}";
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

            if (itemNameText != null)
                itemNameText.text = string.IsNullOrEmpty(data.modName) ? "Unknown Mod" : data.modName;

            if (descriptionText != null)
                descriptionText.text = string.IsNullOrEmpty(data.description) ? "" : data.description;

            if (statsText != null)
            {
                var sb = new System.Text.StringBuilder();

                if (data.baseDamage > 0f)
                    sb.AppendLine($"Damage: {data.baseDamage}");

                sb.AppendLine($"Max Durability: {data.maxDurability}");
                sb.AppendLine($"Current Durability: {instance.CurrentDurability:F0}");
                sb.AppendLine($"Durability Per Use: {data.durabilityPerUse}");

                // Active-mod-only stats
                if (data is ActiveModData activeMod)
                {
                    if (activeMod.cooldown > 0f)
                        sb.AppendLine($"Cooldown: {activeMod.cooldown}s");

                    if (activeMod.chargesRequired > 0)
                        sb.AppendLine($"Charges Required: {activeMod.chargesRequired}");
                }

                statsText.text = sb.ToString().TrimEnd();
            }
        }

        // -----------------------------------------------------------------------

        public void Clear()
        {
            SetEmptyState(true);

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