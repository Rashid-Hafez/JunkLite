using UnityEngine;
using UnityEngine.UI;
using TMPro;

namespace junklite
{
    public class CombatModSlotUI : MonoBehaviour
    {
        #region Fields

        [Header("References")]
        [SerializeField] private Image iconImage;
        [SerializeField] private Image durabilityFill;
        [SerializeField] private GameObject readyIndicator;
        [SerializeField] private TMP_Text inputHintText;

        private ModInstance boundMod;

        #endregion

        #region Bind

        public void Bind(ModInstance mod, string inputHint = null)
        {
            boundMod = mod;

            if (inputHintText != null)
                inputHintText.text = inputHint ?? "";

            Refresh();
        }

        public void Clear()
        {
            boundMod = null;
            Refresh();
        }

        public void Refresh()
        {
            bool hasMod = boundMod != null && !boundMod.IsBroken;

            if (iconImage != null)
            {
                iconImage.enabled = hasMod && boundMod.Data.icon != null;
                if (iconImage.enabled)
                    iconImage.sprite = boundMod.Data.icon;
            }

            if (durabilityFill != null)
                durabilityFill.enabled = hasMod;

            if (readyIndicator != null)
                readyIndicator.SetActive(false);
        }

        #endregion

        #region Update

        private void Update()
        {
            if (boundMod == null) return;

            if (durabilityFill != null && durabilityFill.enabled)
            {
                float max = boundMod.Data.maxDurability;
                durabilityFill.fillAmount = max > 0f ? boundMod.CurrentDurability / max : 0f;
            }

            if (readyIndicator != null && boundMod.Data is ActiveModData active)
                readyIndicator.SetActive(active.CanActivate(boundMod, null));
        }

        #endregion
    }
}