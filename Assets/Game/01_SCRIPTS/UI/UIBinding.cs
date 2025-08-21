using UnityEngine;

namespace junklite.UI
{
 
    [System.Serializable]
    public class UIBinding
    {
        [Header("Binding Configuration")]
        public string bindingName;
        public UIBindingType bindingType;
        public UIComponent targetComponent;

        [Header("Value Binding Settings")]
        public bool useCurrentAndMax = false; // For health bars, mana bars, etc.

        [Header("Display Settings")]
        public string displayFormat = "{0}"; // For text formatting

        [Header("Color Settings")]
        public Color normalColor = Color.white;
        public Color warningColor = Color.yellow;
        public Color dangerColor = Color.red;

        [Header("Color Thresholds")]
        [Range(0f, 1f)] public float warningThreshold = 0.3f;
        [Range(0f, 1f)] public float dangerThreshold = 0.1f;
    }

    /// <summary>
    /// Types of UI bindings available
    /// </summary>
    public enum UIBindingType
    {
        HealthBar,      // Current/Max health (uses ValueBar)
        StateIcon,      // Boolean states like stunned, buffed (uses StateIcon)
        StatusText,     // Text display (uses TextDisplay)
        ValueBar,       // Generic current/max bar (uses ValueBar)
        Cooldown        // Ability cooldowns (uses ProgressRing)
    }
}