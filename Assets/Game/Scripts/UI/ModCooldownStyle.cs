using UnityEngine;
using UnityEngine.Serialization;

namespace junklite
{
    /// <summary>Shared active/cooldown presentation; timing state belongs to ModInstance.</summary>
    [CreateAssetMenu(menuName = "JunkLite/UI/Mod Cooldown Style")]
    public sealed class ModCooldownStyle : ScriptableObject
    {
        [Header("Square Radial Overlay")]
        [FormerlySerializedAs("diameterScale")]
        [Range(0.5f, 1.2f)] public float sizeScale = 1f;
        [Range(0f, 360f)] public float startAngle = 0f;
        [Tooltip("Direction in which the overlay clears as time runs out. Off clears counterclockwise.")]
        public bool wipeClockwise = true;
        [Tooltip("Full square behind the timer. Stays dark until the active/cooldown phase ends.")]
        public Color timerBackgroundColor = new(0.012f, 0.018f, 0.035f, 0.9f);
        [FormerlySerializedAs("backdropColor")]
        [Tooltip("Lighter square that clears radially over the dark timer background.")]
        public Color overlayColor = new(0.46f, 0.52f, 0.62f, 0.5f);
        [Tooltip("Blend the phase color into the translucent overlay. Opacity comes from Overlay Color.")]
        [Range(0f, 1f)] public float tintStrength = 0.18f;
        [FormerlySerializedAs("ringColor")]
        public Color cooldownColor = new(0.86f, 0.25f, 0.89f, 1f);
        public Color finishingColor = new(0.24f, 0.91f, 0.98f, 1f);
        [Tooltip("Blend to the finishing color during the last portion of the cooldown.")]
        [Range(0.01f, 1f)] public float finishingFraction = 0.25f;

        [Header("Active Effect")]
        [FormerlySerializedAs("activeRingColor")]
        public Color activeColor = new(0.24f, 0.91f, 0.98f, 1f);

        [Header("Phase Labels")]
        public bool showPhaseLabel = true;
        public string activeLabel = "ACTIVE";
        public string cooldownLabel = "COOLDOWN";
        [Min(5f)] public float phaseLabelSize = 6f;

        [Header("Countdown")]
        public bool showSeconds = true;
        [Min(8f)] public float fontSize = 15f;
        public Color textColor = new(0.9f, 0.94f, 1f, 1f);
        [Tooltip("Show tenths below this many seconds. Zero keeps whole seconds.")]
        [Min(0f)] public float decimalThreshold = 3f;

        [Header("Ready Feedback")]
        [Tooltip("Brief square flash only if the mod can actually activate. Zero disables it.")]
        [Range(0f, 1f)] public float readyFlashDuration = 0.3f;
        [Range(0f, 1f)] public float readyFlashOpacity = 0.25f;

        private static ModCooldownStyle shared;
        public static ModCooldownStyle Default => shared != null
            ? shared
            : shared = Resources.Load<ModCooldownStyle>("ModCooldownStyle");
    }
}
