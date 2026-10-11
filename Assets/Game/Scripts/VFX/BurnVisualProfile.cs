using UnityEngine;

namespace junklite
{
    /// <summary>Shared, read-only presentation settings. Burn timing remains on StatusEffectHandler.</summary>
    [CreateAssetMenu(menuName = "Junklite/VFX/Burn Visual Profile")]
    public sealed class BurnVisualProfile : ScriptableObject
    {
        public const string ResourceName = "Burn Visual Profile";

        public Material heatMaterial;
        public Material flameMaterial;
        [ColorUsage(false, true)] public Color heatColor = new Color(2.5f, 0.38f, 0.01f);
        [Range(0f, 2f)] public float heatStrength = 0.7f;
        [Range(0f, 2f)] public float tickPulseStrength = 0.5f;
        [Min(0.02f)] public float tickPulseDuration = 0.18f;
        [Range(0f, 1f)] public float flameOpacity = 0.7f;
        [Min(0.1f)] public float flameScale = 1f;
        [Tooltip("Caps flame spread for wide poses, weapons and transparent sprite padding. An explicit anchor bypasses this fit.")]
        [Min(0.1f)] public float flameMaxWidthToHeight = 0.8f;
        [Tooltip("Optional larger flame tongues. The default look uses the body heat shader and small embers.")]
        public bool showFlames = false;

        [Header("Embers")]
        [ColorUsage(false, true)] public Color emberColor = new Color(3f, 0.45f, 0.015f);
        [Range(0f, 1f)] public float emberOpacity = 1f;
        [Range(0.25f, 2f)] public float emberSize = 1.6f;
        public bool showEmbers = true;

        public bool IsConfigured => heatMaterial != null && flameMaterial != null;
    }
}
