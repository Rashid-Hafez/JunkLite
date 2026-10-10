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
        [Tooltip("Optional override. All slots otherwise use the shared ModCooldownStyle resource.")]
        [SerializeField] private ModCooldownStyle cooldownStyle;
        [SerializeField, HideInInspector] private Image cooldownFill;

        [Header("Not-Ready Dimming")]
        [SerializeField] private Color readyColor = Color.white;
        [SerializeField] private Color notReadyColor = new Color(0.35f, 0.35f, 0.35f, 1f);

        private ModInstance boundMod;
        private PlayerCharacter boundPlayer;
        private ModCooldownUI cooldownUI;
        private float nextDynamicRefresh;
        private const float DynamicRefreshInterval = 0.1f;

        public ModCooldownStyle CooldownStyle => cooldownStyle;

        public void Configure(
            Image icon,
            Image durability,
            TMP_Text inputHint,
            Image cooldown,
            Color ready,
            Color notReady,
            ModCooldownStyle style = null)
        {
            iconImage = icon;
            durabilityFill = durability;
            inputHintText = inputHint;
            cooldownFill = cooldown;
            readyColor = ready;
            notReadyColor = notReady;
            cooldownStyle = style;
        }

        public void Bind(ModInstance mod, PlayerCharacter player, string inputHint = null)
        {
            boundMod = mod;
            boundPlayer = player;

            if (inputHintText != null)
                inputHintText.text = inputHint ?? "";

            Refresh();
        }

        public void Clear()
        {
            boundMod = null;
            boundPlayer = null;
            cooldownUI?.Reset();
            if (inputHintText != null) inputHintText.text = "";
            Refresh();
        }

        public void Refresh()
        {
            bool hasMod = boundMod != null && boundMod.Data != null && !boundMod.IsBroken;

            if (iconImage != null)
            {
                iconImage.enabled = hasMod && boundMod.Data.icon != null;
                if (iconImage.enabled)
                    iconImage.sprite = boundMod.Data.icon;
                iconImage.color = readyColor;
            }

            if (durabilityFill != null)
            {
                durabilityFill.enabled = hasMod;
                RefreshDurability();
            }

            if (cooldownFill != null)
            {
                cooldownFill.fillAmount = 0f;
                cooldownFill.enabled = false;
            }

            RefreshCooldown();
        }

        private void Update()
        {
            if (boundMod == null) return;
            RefreshCooldown();
            if (Time.unscaledTime < nextDynamicRefresh) return;
            nextDynamicRefresh = Time.unscaledTime + DynamicRefreshInterval;
            if (boundMod.IsBroken)
            {
                Clear();
                return;
            }
            RefreshDurability();
        }

        private void RefreshDurability()
        {
            if (durabilityFill != null && durabilityFill.enabled && boundMod != null)
            {
                float max = boundMod.Data.maxDurability;
                float fill = max > 0f ? boundMod.CurrentDurability / max : 0f;
                if (!Mathf.Approximately(durabilityFill.fillAmount, fill))
                    durabilityFill.fillAmount = fill;
            }
        }

        private void RefreshCooldown()
        {
            bool isReady = boundMod != null && !boundMod.IsBroken &&
                boundMod.Data is ActiveModData active && active.CanActivate(boundMod, boundPlayer);
            if (iconImage != null && iconImage.enabled && boundMod?.Data is ActiveModData)
            {
                // The radial overlay supplies timer dimming; its cleared area reveals the full icon.
                bool hasTimer = boundMod.HasActiveEffect || boundMod.IsExecuting || boundMod.IsOnCooldown;
                Color color = isReady || hasTimer ? readyColor : notReadyColor;
                if (iconImage.color != color)
                    iconImage.color = color;
            }

            if (cooldownUI == null && iconImage != null)
                cooldownUI = new ModCooldownUI(iconImage, cooldownStyle);
            cooldownUI?.Refresh(boundMod, isReady);
        }

        private void OnDisable() => cooldownUI?.Reset();
    }
}
