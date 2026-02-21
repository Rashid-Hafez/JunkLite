using UnityEngine;
using UnityEngine.UI;

namespace junklite
{
    public class WeaponSlotUI : MonoBehaviour
    {
        #region Fields

        [Header("UI References")]
        [SerializeField] private Image iconImage;
        [SerializeField] private Image durabilityFill;
        [SerializeField] private GameObject activeIndicator;

        private WeaponInstance weapon;
        private bool showDurability;

        #endregion

        #region Bind

        /// <summary>
        /// Bind to a weapon. Shows icon and optionally durability.
        /// </summary>
        public void Bind(WeaponInstance weaponInstance, bool withDurability = true)
        {
            weapon = weaponInstance;
            showDurability = withDurability;

            bool hasWeapon = weapon != null && weapon.weaponData != null;

            if (iconImage != null)
            {
                iconImage.enabled = hasWeapon && weapon.weaponData.icon != null;
                if (iconImage.enabled)
                    iconImage.sprite = weapon.weaponData.icon;
            }

            if (durabilityFill != null)
                durabilityFill.enabled = hasWeapon && showDurability;

            SetActive(false);
        }

        /// <summary>
        /// Show just a static icon with no durability (for fists).
        /// </summary>
        public void BindIcon(Sprite icon)
        {
            weapon = null;
            showDurability = false;

            if (iconImage != null)
            {
                iconImage.enabled = icon != null;
                iconImage.sprite = icon;
            }

            if (durabilityFill != null)
                durabilityFill.enabled = false;

            SetActive(false);
        }

        /// <summary>
        /// Toggle child content visibility. Root stays active, children hide.
        /// Used for empty weapon slot 2 that's visible but has no weapon.
        /// </summary>
        public void SetContentActive(bool active)
        {
            if (iconImage != null) iconImage.enabled = active;
            if (durabilityFill != null) durabilityFill.enabled = active;
            if (activeIndicator != null) activeIndicator.SetActive(false);
        }

        public void SetActive(bool active)
        {
            if (activeIndicator != null)
                activeIndicator.SetActive(active);
        }

        #endregion

        #region Update

        private void Update()
        {
            if (weapon == null || durabilityFill == null || !showDurability) return;
            durabilityFill.fillAmount = weapon.MaxDurability > 0f
                ? weapon.CurrentDurability / weapon.MaxDurability
                : 0f;
        }

        #endregion
    }
}