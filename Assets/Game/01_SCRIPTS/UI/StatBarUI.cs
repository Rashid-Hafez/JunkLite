using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System.Collections;

namespace junklite
{
    /// <summary>
    /// Generic UI bar for displaying an Attribute (e.g., Health, Armor, Mana).
    /// Smoothly animates to new values when the Attribute changes.
    /// </summary>
    [DisallowMultipleComponent]
    public class StatBarUI : MonoBehaviour
    {
        [Header("UI References")]
        [SerializeField] private Image fillImage;       // For fillAmount-based bar
        [SerializeField] private Slider slider;         // For slider-style bar
        [SerializeField] private TMP_Text valueText;    // Shows "75 / 100"

        [Header("Animation")]
        [Tooltip("Enable smooth tweening for value/max updates.")]
        [SerializeField] private bool animate = true;

        [Tooltip("Seconds for the tween animation.")]
        [SerializeField, Min(0f)] private float duration = 0.2f;

        [Tooltip("Easing over the tween's normalized time (x:0..1 -> y:0..1).")]
        [SerializeField] private AnimationCurve ease = AnimationCurve.EaseInOut(0, 0, 1, 1);

        [Tooltip("Use unscaled time (ignores Time.timeScale).")]
        [SerializeField] private bool useUnscaledTime = false;

        [Tooltip("Also tween the numbers shown in the label (if present).")]
        [SerializeField] private bool animateText = true;

        [Tooltip("Format for the value text. {0}=current, {1}=max.")]
        [SerializeField] private string valueFormat = "{0} / {1}";

        private Attribute boundAttribute;

        // tween state
        private Coroutine tweenRoutine;
        private float visualCurrent;
        private float visualMax;

        // ---------- Public API ----------

        public void Bind(Attribute attribute)
        {
            Unbind();

            boundAttribute = attribute;
            if (boundAttribute == null)
            {
                SetVisualImmediate(0f, 1f);
                return;
            }

            // initialize visuals to current attribute state
            SetVisualImmediate(boundAttribute.Current, boundAttribute.Max);

            // subscribe to changes
            boundAttribute.OnValueChanged += OnValueChanged;
            boundAttribute.OnMaxChanged += OnMaxChanged;
        }

        public void Unbind()
        {
            if (boundAttribute != null)
            {
                boundAttribute.OnValueChanged -= OnValueChanged;
                boundAttribute.OnMaxChanged -= OnMaxChanged;
            }
            boundAttribute = null;

            StopTween();
        }

        private void OnDestroy() => Unbind();

        // ---------- Event Handlers ----------

        private void OnValueChanged(float newValue)
        {
            float targetMax = boundAttribute != null ? boundAttribute.Max : visualMax;
            AnimateTo(newValue, targetMax);
        }

        private void OnMaxChanged(float newMax)
        {
            float targetValue = boundAttribute != null ? boundAttribute.Current : visualCurrent;
            AnimateTo(targetValue, newMax);
        }

        // ---------- Rendering / Animation ----------

        private void SetVisualImmediate(float current, float max)
        {
            visualCurrent = Mathf.Max(0f, current);
            visualMax = Mathf.Max(1f, max); // avoid divide by zero
            ApplyToUI(visualCurrent, visualMax);
        }

        private void AnimateTo(float newCurrent, float newMax)
        {
            newCurrent = Mathf.Max(0f, newCurrent);
            newMax = Mathf.Max(1f, newMax);

            if (!animate || duration <= 0f)
            {
                SetVisualImmediate(newCurrent, newMax);
                return;
            }

            // if max changes, we want to tween both current and max to avoid jumps
            StartTween(visualCurrent, visualMax, newCurrent, newMax, duration);
        }

        private void ApplyToUI(float current, float max)
        {
            float pct = max > 0f ? current / max : 0f;

            if (fillImage != null)
                fillImage.fillAmount = pct;

            if (slider != null)
            {
                // update slider first so its value clamps correctly
                slider.maxValue = max;
                slider.value = current;
            }

            if (valueText != null)
            {
                int c = Mathf.CeilToInt(current);
                int m = Mathf.CeilToInt(max);
                valueText.text = string.Format(valueFormat, c, m);
            }
        }

        private void StartTween(float fromValue, float fromMax, float toValue, float toMax, float time)
        {
            StopTween();
            tweenRoutine = StartCoroutine(TweenRoutine(fromValue, fromMax, toValue, toMax, time));
        }

        private void StopTween()
        {
            if (tweenRoutine != null)
            {
                StopCoroutine(tweenRoutine);
                tweenRoutine = null;
            }
        }

        private IEnumerator TweenRoutine(float fromValue, float fromMax, float toValue, float toMax, float time)
        {
            float t = 0f;

            // cache starting values so label can tween independently if desired
            float startValue = visualCurrent = fromValue;
            float startMax = visualMax = fromMax;

            while (t < time)
            {
                t += useUnscaledTime ? Time.unscaledDeltaTime : Time.deltaTime;
                float u = Mathf.Clamp01(t / time);
                float e = ease != null ? Mathf.Clamp01(ease.Evaluate(u)) : u;

                float cur = Mathf.LerpUnclamped(fromValue, toValue, e);
                float max = Mathf.LerpUnclamped(fromMax, toMax, e);

                visualCurrent = cur;
                visualMax = max;

                if (animateText)
                {
                    // Apply fully each frame so text counts up/down smoothly
                    ApplyToUI(visualCurrent, visualMax);
                }
                else
                {
                    // If not animating text, animate bar only; freeze text at target at the end.
                    float pct = max > 0f ? cur / max : 0f;
                    if (fillImage != null) fillImage.fillAmount = pct;
                    if (slider != null) { slider.maxValue = max; slider.value = cur; }
                }

                yield return null;
            }

            // snap to final target to avoid drift
            visualCurrent = toValue;
            visualMax = toMax;
            ApplyToUI(visualCurrent, visualMax);
            tweenRoutine = null;
        }
    }
}
