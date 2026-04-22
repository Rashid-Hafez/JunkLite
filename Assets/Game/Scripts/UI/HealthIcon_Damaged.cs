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

        [Header("Debug")]
        [SerializeField] private bool enableDebugLogs = true;
        [SerializeField] private bool includeCallStack = true;

        private static readonly int DistortionID = Shader.PropertyToID("_DistortionAmount");
        private static readonly int TintColorID  = Shader.PropertyToID("_Color");

        private Material boxMat;
        private Material iconMat;
        private Attribute boundAttribute;
        private float previousHealth = -1f;
        private float baselineDistortion;
        private Coroutine pulseRoutine;
        private Color originalBoxColor;
        private Color originalIconColor;
        private float originalBoxDistortion;
        private float originalIconDistortion;

        // -----------------------------------------------------------------------
        // Public API — called by PlayerUI
        // -----------------------------------------------------------------------

        public void Bind(Attribute health)
        {
            Unbind();

            boundAttribute = health;
            if (boundAttribute == null)
            {
                LogDebug("Bind called with null health attribute.", true);
                return;
            }

            InitMaterials();

            previousHealth = boundAttribute.Current;
            LogDebug(
                $"Bind -> current={boundAttribute.Current:0.###}, max={boundAttribute.Max:0.###}, " +
                $"box={DescribeGraphic(boxGraphic, boxMat)}, icon={DescribeGraphic(iconGraphic, iconMat)}",
                true);
            ApplyState(boundAttribute.Current, boundAttribute.Max);

            boundAttribute.OnValueChanged += OnHealthChanged;
            boundAttribute.OnMaxChanged   += OnMaxChanged;
        }

        public void Unbind()
        {
            if (boundAttribute != null)
            {
                LogDebug($"Unbind -> health current={boundAttribute.Current:0.###}, max={boundAttribute.Max:0.###}");
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
            float oldValue = previousHealth;
            previousHealth = newValue;

            LogDebug(
                $"OnHealthChanged -> old={oldValue:0.###}, new={newValue:0.###}, max={max:0.###}, damaged={damaged}",
                damaged);
            ApplyState(newValue, max);

            if (damaged)
                TriggerHitPulse();
        }

        private void OnMaxChanged(float newMax)
        {
            float current = boundAttribute != null ? boundAttribute.Current : 0f;
            LogDebug($"OnMaxChanged -> current={current:0.###}, newMax={newMax:0.###}", true);
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

            LogDebug(
                $"ApplyState -> current={current:0.###}, max={max:0.###}, pct={pct:0.###}, critical={critical}, " +
                $"threshold={criticalThreshold:0.###}, baselineDistortion={baselineDistortion:0.###}, tint={FormatColor(tint)}, " +
                $"boxMaterial={DescribeMaterial(boxMat)}, iconMaterial={DescribeMaterial(iconMat)}");
        }

        private void SetDistortion(float value)
        {
            if (boxMat  != null) boxMat.SetFloat(DistortionID, value);
            if (iconMat != null) iconMat.SetFloat(DistortionID, value);
            LogDebug(
                $"SetDistortion -> requested={value:0.###}, boxNow={GetDistortion(boxMat):0.###}, iconNow={GetDistortion(iconMat):0.###}");
        }

        // -----------------------------------------------------------------------
        // Hit pulse
        // -----------------------------------------------------------------------

        private void TriggerHitPulse()
        {
            StopPulse();
            LogDebug(
                $"TriggerHitPulse -> baseline={baselineDistortion:0.###}, peak={hitDistortionPeak:0.###}, duration={hitPulseDuration:0.###}",
                true);
            pulseRoutine = StartCoroutine(HitPulseRoutine());
        }

        private IEnumerator HitPulseRoutine()
        {
            LogDebug("HitPulseRoutine started.");
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
            LogDebug("HitPulseRoutine finished.");
        }

        private void StopPulse()
        {
            if (pulseRoutine != null)
            {
                StopCoroutine(pulseRoutine);
                pulseRoutine = null;
                LogDebug("Existing hit pulse stopped before starting a new one.");
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
                if (boxMat != null)
                {
                    originalBoxColor = boxMat.GetColor(TintColorID);
                    originalBoxDistortion = boxMat.GetFloat(DistortionID);
                }
            }

            if (iconGraphic != null)
            {
                iconMat = iconGraphic.materialForRendering != null
                    ? iconGraphic.material
                    : null;
                if (iconMat != null)
                {
                    originalIconColor = iconMat.GetColor(TintColorID);
                    originalIconDistortion = iconMat.GetFloat(DistortionID);
                }
            }

            LogDebug(
                $"InitMaterials -> boxOriginalColor={FormatColor(originalBoxColor)}, boxOriginalDistortion={originalBoxDistortion:0.###}, " +
                $"iconOriginalColor={FormatColor(originalIconColor)}, iconOriginalDistortion={originalIconDistortion:0.###}, " +
                $"boxGraphicColor={FormatGraphicColor(boxGraphic)}, iconGraphicColor={FormatGraphicColor(iconGraphic)}",
                true);
        }

        private void CleanupMaterials()
        {
            if (boxMat != null)  { Destroy(boxMat);  boxMat  = null; }
            if (iconMat != null) { Destroy(iconMat); iconMat = null; }
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

        private float GetDistortion(Material material)
        {
            return material != null && material.HasProperty(DistortionID)
                ? material.GetFloat(DistortionID)
                : float.NaN;
        }

        private string DescribeGraphic(Graphic graphic, Material material)
        {
            if (graphic == null)
                return "null";

            return $"{graphic.name} (graphicColor={FormatColor(graphic.color)}, material={DescribeMaterial(material)})";
        }

        private string DescribeMaterial(Material material)
        {
            if (material == null)
                return "null";

            string shaderName = material.shader != null ? material.shader.name : "null-shader";
            Color tint = material.HasProperty(TintColorID) ? material.GetColor(TintColorID) : Color.clear;
            float distortion = material.HasProperty(DistortionID) ? material.GetFloat(DistortionID) : float.NaN;
            return $"{material.name} [shader={shaderName}, tint={FormatColor(tint)}, distortion={distortion:0.###}]";
        }

        private string FormatGraphicColor(Graphic graphic)
        {
            return graphic == null ? "null" : FormatColor(graphic.color);
        }

        private static string FormatColor(Color color)
        {
            return $"RGBA({color.r:0.###}, {color.g:0.###}, {color.b:0.###}, {color.a:0.###})";
        }

        private void LogDebug(string message, bool forceStackTrace = false)
        {
            if (!enableDebugLogs)
                return;

            if (includeCallStack || forceStackTrace)
                Debug.Log($"[HealthIcon_Damaged] {message}\n{StackTraceUtility.ExtractStackTrace()}", this);
            else
                Debug.Log($"[HealthIcon_Damaged] {message}", this);
        }
    }
}
