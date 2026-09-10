using UnityEngine;
using UnityEngine.UI;
using TMPro;

namespace junklite
{
    public class CombatModSlotUI : MonoBehaviour
    {
        [Header("References")]
        [SerializeField] private Image iconImage;
        [SerializeField] private Image durabilityFill;
        [SerializeField] private TMP_Text inputHintText;

        [Header("Cooldown")]
        [SerializeField] private Image cooldownFill;

        [Header("Not-Ready Dimming")]
        [SerializeField] private Color readyColor = Color.white;
        [SerializeField] private Color notReadyColor = new Color(0.35f, 0.35f, 0.35f, 1f);

        private ModInstance boundMod;
        private float nextDynamicRefresh;
        private const float DynamicRefreshInterval = 0.1f;

        public void Configure(
            Image icon,
            Image durability,
            TMP_Text inputHint,
            Image cooldown,
            Color ready,
            Color notReady)
        {
            iconImage = icon;
            durabilityFill = durability;
            inputHintText = inputHint;
            cooldownFill = cooldown;
            readyColor = ready;
            notReadyColor = notReady;
        }

        public void Bind(ModInstance mod, PlayerCharacter player, string inputHint = null)
        {
            Clear();
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
                iconImage.color = readyColor;
            }

            if (durabilityFill != null)
            {
                durabilityFill.fillAmount = 0f;
                durabilityFill.enabled = hasMod;
            }

            if (cooldownFill != null)
            {
                cooldownFill.fillAmount = 0f;
                cooldownFill.enabled = false;
            }
        }

        private void Update()
        {
            if (boundMod == null) return;
            if (Time.unscaledTime < nextDynamicRefresh) return;
            nextDynamicRefresh = Time.unscaledTime + DynamicRefreshInterval;

            if (durabilityFill != null && durabilityFill.enabled)
            {
                float max = boundMod.Data.maxDurability;
                float fill = max > 0f ? boundMod.CurrentDurability / max : 0f;
                if (!Mathf.Approximately(durabilityFill.fillAmount, fill))
                    durabilityFill.fillAmount = fill;
            }

            if (iconImage != null && iconImage.enabled && boundMod.Data is ActiveModData active)
            {
                bool isReady = active.CanActivate(boundMod, null);
                Color color = isReady ? readyColor : notReadyColor;
                if (iconImage.color != color)
                    iconImage.color = color;
            }

            if (cooldownFill != null)
            {
                float normalized = boundMod.CooldownNormalized;
                bool onCooldown = normalized > 0f;
                if (cooldownFill.enabled != onCooldown)
                    cooldownFill.enabled = onCooldown;
                if (!Mathf.Approximately(cooldownFill.fillAmount, normalized))
                    cooldownFill.fillAmount = normalized;
            }
        }
    }
}
