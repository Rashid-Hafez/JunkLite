using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace junklite
{
    /// <summary>Reusable view owned by either HUD or inventory slots; never advances a timer.</summary>
    public sealed class ModCooldownUI
    {
        private readonly Image icon;
        private readonly ModCooldownStyle style;
        private RectTransform root;
        private ModCooldownGraphic overlay;
        private TMP_Text countdown;
        private TMP_Text phaseLabel;
        private ModInstance boundMod;
        private bool wasBusy;
        private float flashRemaining;
        private int lastDisplayValue = -1;
        private bool lastUsedDecimals;

        public ModCooldownUI(Image icon, ModCooldownStyle style)
        {
            this.icon = icon;
            this.style = style != null ? style : ModCooldownStyle.Default;
        }

        public void Refresh(ModInstance mod, bool canActivate = false)
        {
            if (boundMod != mod)
            {
                Reset();
                boundMod = mod;
            }

            if (icon == null || style == null || mod == null || mod.IsBroken)
            {
                Reset();
                return;
            }

            bool active = mod.HasActiveEffect || mod.IsExecuting;
            float remaining = active ? mod.ActiveDurationRemaining : mod.CooldownRemaining;
            bool busy = active || remaining > 0f;
            if (wasBusy && !busy && canActivate)
                flashRemaining = style.readyFlashDuration;
            wasBusy = busy;
            if (busy || !canActivate)
                flashRemaining = 0f;

            if (!busy && flashRemaining <= 0f)
            {
                SetVisible(false);
                return;
            }

            EnsureVisuals();
            SetVisible(true);
            // Match the square icon's preserve-aspect alignment, including top-left anchored HUD icons.
            float side = Mathf.Min(icon.rectTransform.rect.width, icon.rectTransform.rect.height);
            Vector2 pivot = icon.rectTransform.pivot;
            root.anchorMin = root.anchorMax = pivot;
            root.sizeDelta = Vector2.one * side;
            root.anchoredPosition = (Vector2.one * 0.5f - pivot) * side;
            root.localScale = Vector3.one * Mathf.Clamp(style.sizeScale, 0.5f, 1.2f);
            float flash = flashRemaining / Mathf.Max(0.001f, style.readyFlashDuration);
            float fraction = active ? mod.ActiveDurationNormalized : mod.CooldownNormalized;
            // An untimed execution still needs a clear active state, never a false 0s/ready signal.
            if (active && !mod.HasActiveEffect) fraction = 1f;
            overlay.SetAppearance(fraction, style, Mathf.Clamp01(flash), active);
            countdown.enabled = busy && remaining > 0f && style.showSeconds;
            countdown.color = style.textColor;
            float fontSize = Mathf.Max(8f, style.fontSize);
            if (!Mathf.Approximately(countdown.fontSizeMax, fontSize))
                countdown.fontSizeMax = countdown.fontSize = fontSize;

            phaseLabel.enabled = busy && (style.showPhaseLabel || !countdown.enabled);
            string phase = active ? style.activeLabel : style.cooldownLabel;
            if (phaseLabel.text != phase) phaseLabel.SetText(phase);
            phaseLabel.color = active ? style.activeColor : style.textColor;
            phaseLabel.fontSizeMax = Mathf.Max(5f, style.phaseLabelSize);
            phaseLabel.rectTransform.sizeDelta = new Vector2(side * 0.7f, 9f);
            phaseLabel.rectTransform.anchoredPosition = countdown.enabled ? new Vector2(0f, -9f) : Vector2.zero;
            countdown.rectTransform.anchoredPosition = phaseLabel.enabled ? new Vector2(0f, 3f) : Vector2.zero;

            if (countdown.enabled)
            {
                bool decimals = remaining < style.decimalThreshold;
                // Round up: never say zero while activation is still blocked.
                int value = Mathf.CeilToInt(remaining * (decimals ? 10f : 1f));
                if (value != lastDisplayValue || decimals != lastUsedDecimals)
                {
                    countdown.SetText(decimals ? "{0:1}s" : "{0}s", decimals ? value * 0.1f : value);
                    lastDisplayValue = value;
                    lastUsedDecimals = decimals;
                }
            }

            // Match the gameplay clock, including pause and slow motion.
            flashRemaining = Mathf.Max(0f, flashRemaining - Time.deltaTime);
        }

        public void Reset()
        {
            wasBusy = false;
            flashRemaining = 0f;
            lastDisplayValue = -1;
            SetVisible(false);
        }

        private void EnsureVisuals()
        {
            if (root != null) return;
            var go = new GameObject("Mod Timer", typeof(RectTransform), typeof(ModCooldownGraphic));
            go.layer = icon.gameObject.layer;
            root = (RectTransform)go.transform;
            root.SetParent(icon.transform, false);
            Stretch(root);
            overlay = go.GetComponent<ModCooldownGraphic>();
            overlay.raycastTarget = false;

            var label = new GameObject("Seconds Remaining", typeof(RectTransform), typeof(TextMeshProUGUI));
            label.layer = go.layer;
            label.transform.SetParent(root, false);
            countdown = label.GetComponent<TextMeshProUGUI>();
            Stretch(countdown.rectTransform);
            countdown.rectTransform.offsetMin = new Vector2(5f, 3f);
            countdown.rectTransform.offsetMax = new Vector2(-5f, -3f);
            UIFonts.ApplyHudBody(countdown);
            countdown.fontStyle = FontStyles.Bold;
            countdown.alignment = TextAlignmentOptions.Center;
            countdown.textWrappingMode = TextWrappingModes.NoWrap;
            countdown.enableAutoSizing = true;
            countdown.fontSizeMin = 8f;
            countdown.fontSizeMax = Mathf.Max(8f, style.fontSize);
            countdown.fontSize = countdown.fontSizeMax;
            countdown.raycastTarget = false;

            var phase = new GameObject("Timer Phase", typeof(RectTransform), typeof(TextMeshProUGUI));
            phase.layer = go.layer;
            phase.transform.SetParent(root, false);
            phaseLabel = phase.GetComponent<TextMeshProUGUI>();
            RectTransform phaseRect = phaseLabel.rectTransform;
            phaseRect.anchorMin = phaseRect.anchorMax = new Vector2(0.5f, 0.5f);
            UIFonts.ApplyHudBody(phaseLabel);
            phaseLabel.fontStyle = FontStyles.Bold;
            phaseLabel.alignment = TextAlignmentOptions.Center;
            phaseLabel.textWrappingMode = TextWrappingModes.NoWrap;
            phaseLabel.enableAutoSizing = true;
            phaseLabel.fontSizeMin = 4f;
            phaseLabel.fontSizeMax = Mathf.Max(5f, style.phaseLabelSize);
            phaseLabel.fontSize = phaseLabel.fontSizeMax;
            phaseLabel.raycastTarget = false;
        }

        private void SetVisible(bool visible)
        {
            if (root != null && root.gameObject.activeSelf != visible)
                root.gameObject.SetActive(visible);
        }

        private static void Stretch(RectTransform rect)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = rect.offsetMax = Vector2.zero;
        }
    }
}
