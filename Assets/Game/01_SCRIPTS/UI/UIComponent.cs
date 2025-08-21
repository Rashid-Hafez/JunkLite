using UnityEngine;
using UnityEngine.UI;
using TMPro;

namespace junklite.UI
{
    /// <summary>
    /// Base class for all UI components
    /// </summary>
    public abstract class UIComponent : MonoBehaviour
    {
        [Header("Animation")]
        [SerializeField] protected float animationSpeed = 5f;

        public virtual void UpdateValue(float value, UIBinding binding) { }
        public virtual void UpdateValue(float current, float max, UIBinding binding) { }
        public virtual void UpdateState(bool state, UIBinding binding) { }
        public virtual void UpdateText(string text, UIBinding binding) { }
        public virtual void Clear() { }

        protected Color GetColorForValue(float ratio, UIBinding binding)
        {
            if (ratio <= binding.dangerThreshold)
                return binding.dangerColor;
            else if (ratio <= binding.warningThreshold)
                return binding.warningColor;
            else
                return binding.normalColor;
        }
    }

   

    /// <summary>
    /// Text display component (shows any text value)
    /// </summary>
    public class TextDisplay : UIComponent
    {
        [Header("Text Components")]
        [SerializeField] private TextMeshProUGUI displayText;

        public override void UpdateText(string text, UIBinding binding)
        {
            if (displayText != null)
            {
                displayText.text = string.Format(binding.displayFormat, text);
                displayText.color = binding.normalColor;
            }
        }

        public override void UpdateValue(float value, UIBinding binding)
        {
            if (displayText != null)
            {
                displayText.text = string.Format(binding.displayFormat, value.ToString("F0"));
                displayText.color = binding.normalColor;
            }
        }

        public override void Clear()
        {
            if (displayText != null) displayText.text = "";
        }
    }

    /// <summary>
    /// Circular progress component (for cooldowns, abilities)
    /// </summary>
    public class ProgressRing : UIComponent
    {
        [Header("Progress Components")]
        [SerializeField] private Image progressImage;
        [SerializeField] private TextMeshProUGUI progressText;

        [Header("Progress Settings")]
        [SerializeField] private bool clockwise = false;

        private float targetProgress;

        public override void UpdateValue(float current, float max, UIBinding binding)
        {
            if (max <= 0) return;

            float progress = current / max;
            targetProgress = clockwise ? progress : 1f - progress;

            // Update text
            if (progressText != null)
            {
                if (current > 0)
                {
                    progressText.text = current.ToString("F1");
                }
                else
                {
                    progressText.text = "";
                }
            }

            // Update color
            if (progressImage != null)
            {
                progressImage.color = GetColorForValue(progress, binding);
            }
        }

        private void Update()
        {
            if (progressImage != null)
            {
                progressImage.fillAmount = Mathf.Lerp(progressImage.fillAmount, targetProgress, animationSpeed * Time.deltaTime);
            }
        }

        public override void Clear()
        {
            if (progressImage != null) progressImage.fillAmount = 0f;
            if (progressText != null) progressText.text = "";
        }
    }
}