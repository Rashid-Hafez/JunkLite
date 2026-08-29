using System.Collections;
using UnityEngine;
using UnityEngine.UI;

namespace junklite
{
    /// <summary>
    /// Drives hologram-shader feedback on the health box and icon images.
    /// Attach to the health box parent; assign both the box Image (this object)
    /// and the child icon Image in the inspector.
    /// </summary>
    public class HealthIcon_Damaged : MonoBehaviour
    {
        [Header("UI References")]
        [SerializeField] private Graphic boxGraphic;
        [SerializeField] private Graphic iconGraphic;

        [Header("Health Colors")]
        [Tooltip("Material tint applied when health is above critical threshold.")]
        [SerializeField] private Color normalColor = Color.white;
        [Tooltip("Material tint applied when health is at or below critical threshold.")]
        [SerializeField] private Color criticalColor = new Color(1f, 0.15f, 0.15f, 1f);

        [Header("Critical Health")]
        [Tooltip("HP percentage at or below which health is considered critical.")]
        [SerializeField, Range(0f, 1f)] private float criticalThreshold = 0.25f;

        [Header("Distortion")]
        [Tooltip("Baseline distortion when at critical health.")]
        [SerializeField] private float criticalDistortion = 0.2f;
        [Tooltip("Peak distortion added on top of baseline when hit.")]
        [SerializeField] private float hitDistortionPeak = 0.6f;
        [Tooltip("Duration of the hit distortion pulse in seconds.")]
        [SerializeField] private float hitPulseDuration = 0.8f;
        [SerializeField] private AnimationCurve hitPulseCurve = AnimationCurve.EaseInOut(0f, 1f, 1f, 0f);

        private static readonly int DistortionID = Shader.PropertyToID("_DistortionAmount");
        private static readonly int TintColorID  = Shader.PropertyToID("_Color");

        private Material boxMat;
        private Material iconMat;
        private Attribute boundAttribute;
        private float previousHealth = -1f;
        private float baselineDistortion;
        private Coroutine pulseRoutine;

        // -----------------------------------------------------------------------
        // Public API — called by PlayerUI
        // -----------------------------------------------------------------------

        public void Bind(Attribute health)
        {
            Unbind();

            boundAttribute = health;
            if (boundAttribute == null)
                return;

            InitMaterials();

            previousHealth = boundAttribute.Current;
            ApplyState(boundAttribute.Current, boundAttribute.Max);

            boundAttribute.OnValueChanged += OnHealthChanged;
            boundAttribute.OnMaxChanged   += OnMaxChanged;
        }

        public void Unbind()
        {
            if (boundAttribute != null)
            {
                boundAttribute.OnValueChanged -= OnHealthChanged;
                boundAttribute.OnMaxChanged   -= OnMaxChanged;
            }
            boundAttribute = null;

            StopPulse();
            CleanupMaterials();
        }

        private void OnDestroy() => Unbind();

        // -----------------------------------------------------------------------
        // Event handlers
        // -----------------------------------------------------------------------

        private void OnHealthChanged(float newValue)
        {
            float max = boundAttribute != null ? boundAttribute.Max : 1f;
            bool damaged = previousHealth > 0f && newValue < previousHealth;
            previousHealth = newValue;

            ApplyState(newValue, max);

            if (damaged)
                TriggerHitPulse();
        }

        private void OnMaxChanged(float newMax)
        {
            float current = boundAttribute != null ? boundAttribute.Current : 0f;
            ApplyState(current, newMax);
        }

        // -----------------------------------------------------------------------
        // State application
        // -----------------------------------------------------------------------

        private void ApplyState(float current, float max)
        {
            float pct = max > 0f ? current / max : 0f;
            bool critical = pct <= criticalThreshold && pct > 0f;

            baselineDistortion = critical ? criticalDistortion : 0f;

            SetDistortion(baselineDistortion);

            Color tint = critical ? criticalColor : normalColor;
            if (boxMat != null)
                boxMat.SetColor(TintColorID, tint);
            if (iconMat != null)
                iconMat.SetColor(TintColorID, tint);

            MarkGraphicsDirty();
        }

        private void SetDistortion(float value)
        {
            if (boxMat  != null) boxMat.SetFloat(DistortionID, value);
            if (iconMat != null) iconMat.SetFloat(DistortionID, value);
        }

        // -----------------------------------------------------------------------
        // Hit pulse
        // -----------------------------------------------------------------------

        private void TriggerHitPulse()
        {
            StopPulse();
            pulseRoutine = StartCoroutine(HitPulseRoutine());
        }

        private IEnumerator HitPulseRoutine()
        {
            float elapsed = 0f;
            while (elapsed < hitPulseDuration)
            {
                elapsed += Time.unscaledDeltaTime;
                float t = Mathf.Clamp01(elapsed / hitPulseDuration);
                float curveValue = hitPulseCurve.Evaluate(t);
                float distortion = baselineDistortion + hitDistortionPeak * curveValue;
                SetDistortion(distortion);
                yield return null;
            }

            SetDistortion(baselineDistortion);
            pulseRoutine = null;
            MarkGraphicsDirty();
        }

        private void StopPulse()
        {
            if (pulseRoutine != null)
            {
                StopCoroutine(pulseRoutine);
                pulseRoutine = null;
            }
        }

        // -----------------------------------------------------------------------
        // Material management
        // -----------------------------------------------------------------------

        private void InitMaterials()
        {
            if (boxGraphic != null)
            {
                boxMat = boxGraphic.materialForRendering != null
                    ? boxGraphic.material
                    : null;
            }

            if (iconGraphic != null)
            {
                iconMat = iconGraphic.materialForRendering != null
                    ? iconGraphic.material
                    : null;
            }
        }

        private void CleanupMaterials()
        {
            if (boxMat != null)  { /*Destroy(boxMat);*/  boxMat  = null; }
            if (iconMat != null) { /*Destroy(iconMat);*/ iconMat = null; }
        }

        private void MarkGraphicsDirty()
        {
            if (boxGraphic != null)
            {
                boxGraphic.SetMaterialDirty();
                boxGraphic.SetVerticesDirty();
            }

            if (iconGraphic != null)
            {
                iconGraphic.SetMaterialDirty();
                iconGraphic.SetVerticesDirty();
            }
        }
    }
}
