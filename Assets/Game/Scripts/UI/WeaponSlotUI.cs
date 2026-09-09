using UnityEngine;
using UnityEngine.UI;
using TMPro;

namespace junklite
{
    public class WeaponSlotUI : MonoBehaviour
    {
        #region Fields

        [Header("Weapon Display")]
        [SerializeField] private Image iconImage;
        [SerializeField] private Image durabilityFill;
        [SerializeField] private GameObject activeIndicator;
        [SerializeField] private GameObject durabilityTrack;
        [SerializeField] private TMP_Text emptyLabel;

        [Header("Mouse Button Icons")]
        [SerializeField] private GameObject mousePressedIcon;
        [SerializeField] private GameObject mouseUnpressedIcon;

        private WeaponInstance weapon;
        private bool showDurability;

        #endregion

        #region Bind

        public void Configure(
            Image icon,
            Image durability,
            GameObject active,
            GameObject track = null,
            TMP_Text empty = null,
            GameObject pressedIcon = null,
            GameObject unpressedIcon = null)
        {
            iconImage = icon;
            durabilityFill = durability;
            activeIndicator = active;
            durabilityTrack = track;
            emptyLabel = empty;
            mousePressedIcon = pressedIcon;
            mouseUnpressedIcon = unpressedIcon;
        }

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
            if (durabilityTrack != null)
                durabilityTrack.SetActive(hasWeapon && showDurability);
            if (emptyLabel != null)
                emptyLabel.gameObject.SetActive(!hasWeapon);

            SetActive(false);
            SetMousePressed(false);
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
            if (durabilityTrack != null)
                durabilityTrack.SetActive(false);
            if (emptyLabel != null)
                emptyLabel.gameObject.SetActive(false);

            SetActive(false);
            SetMousePressed(false);
        }

        /// <summary>
        /// Toggle child content visibility. Root stays active, children hide.
        /// Used for empty weapon slot 2 that's visible but has no weapon.
        /// </summary>
        public void SetContentActive(bool active)
        {
            if (iconImage != null) iconImage.enabled = active;
            if (durabilityFill != null) durabilityFill.enabled = active;
            if (durabilityTrack != null) durabilityTrack.SetActive(active && showDurability);
            if (emptyLabel != null) emptyLabel.gameObject.SetActive(!active);
            if (activeIndicator != null) activeIndicator.SetActive(false);

            if (!active) SetMousePressed(false);
        }

        public void SetActive(bool active)
        {
            if (activeIndicator != null)
                activeIndicator.SetActive(active);
        }

        /// <summary>
        /// Swap between pressed and unpressed mouse button icons.
        /// </summary>
        public void SetMousePressed(bool pressed)
        {
            if (mousePressedIcon != null) mousePressedIcon.SetActive(pressed);
            if (mouseUnpressedIcon != null) mouseUnpressedIcon.SetActive(!pressed);
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
