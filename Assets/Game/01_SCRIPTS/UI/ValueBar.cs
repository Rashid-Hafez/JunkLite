using junklite.UI;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace junklite.UI
{
    /// <summary>
    /// Generic value bar component (health, mana, stamina, etc.)
    /// </summary>
    public class ValueBar : UIComponent
    {
        [Header("Bar Components")]
        [SerializeField] private Slider mainSlider;
        [SerializeField] private Slider backgroundSlider; // Optional damage delay effect
        [SerializeField] private TextMeshProUGUI valueText;
        [SerializeField] private Image fillImage;

        [Header("Color Settings")]
        [SerializeField] private bool useColorGradient = true;
        [SerializeField] private Color highColor = Color.green;    // 100% health
        [SerializeField] private Color mediumColor = Color.yellow; // 50% health
        [SerializeField] private Color lowColor = Color.red;       // 0% health
        [SerializeField] private float mediumThreshold = 0.5f;     // When to switch to medium color
        [SerializeField] private float lowThreshold = 0.25f;       // When to switch to low color

        [Header("Background Effect")]
        [SerializeField] private float backgroundDelay = 0.5f;

        private float targetValue;
        private float backgroundTargetValue;
        private float backgroundDelayTimer;
        private float currentValue;
        private float maxValue;

        public override void UpdateValue(float current, float max, UIBinding binding)
        {
            currentValue = current;
            maxValue = max;
            float ratio = max > 0 ? current / max : 0f;

            targetValue = ratio;

            // Start background delay timer when value decreases
            if (ratio < (mainSlider != null ? mainSlider.value : 1f))
            {
                backgroundDelayTimer = backgroundDelay;
            }

            // Update text immediately
            if (valueText != null)
            {
                if (binding != null && binding.useCurrentAndMax)
                {
                    valueText.text = $"{current:F0}/{max:F0}";
                }
                else
                {
                    valueText.text = $"{current:F0}";
                }
            }

            // Update color immediately
            UpdateFillColor(ratio);
        }

        private void Update()
        {
            // Animate main slider
            if (mainSlider != null)
            {
                float oldValue = mainSlider.value;
                mainSlider.value = Mathf.Lerp(mainSlider.value, targetValue, animationSpeed * Time.deltaTime);

                // Update color as slider animates
                if (Mathf.Abs(oldValue - mainSlider.value) > 0.001f)
                {
                    UpdateFillColor(mainSlider.value);
                }
            }

            // Handle background slider
            if (backgroundSlider != null)
            {
                if (backgroundDelayTimer > 0f)
                {
                    backgroundDelayTimer -= Time.deltaTime;
                }
                else
                {
                    backgroundTargetValue = targetValue;
                }

                backgroundSlider.value = Mathf.Lerp(backgroundSlider.value, backgroundTargetValue, animationSpeed * 0.5f * Time.deltaTime);
            }
        }

        private void UpdateFillColor(float ratio)
        {
            if (!useColorGradient || fillImage == null) return;

            Color targetColor;

            if (ratio > mediumThreshold)
            {
                // High to medium range
                float t = (ratio - mediumThreshold) / (1f - mediumThreshold);
                targetColor = Color.Lerp(mediumColor, highColor, t);
            }
            else if (ratio > lowThreshold)
            {
                // Medium to low range
                float t = (ratio - lowThreshold) / (mediumThreshold - lowThreshold);
                targetColor = Color.Lerp(lowColor, mediumColor, t);
            }
            else
            {
                // Low range
                targetColor = lowColor;
            }

            fillImage.color = targetColor;
        }

        public override void Clear()
        {
            if (mainSlider != null) mainSlider.value = 0f;
            if (backgroundSlider != null) backgroundSlider.value = 0f;
            if (valueText != null) valueText.text = "";
            if (fillImage != null) fillImage.color = lowColor;
        }

        // Method to set custom colors at runtime
        public void SetColorGradient(Color high, Color medium, Color low)
        {
            highColor = high;
            mediumColor = medium;
            lowColor = low;

            // Update current color
            if (mainSlider != null)
            {
                UpdateFillColor(mainSlider.value);
            }
        }

        // Method to disable/enable color changes
        public void SetUseColorGradient(bool use)
        {
            useColorGradient = use;
            if (!use && fillImage != null)
            {
                fillImage.color = Color.white; // Reset to default
            }
        }
    }
}