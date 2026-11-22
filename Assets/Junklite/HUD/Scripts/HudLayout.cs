using UnityEngine;
using UnityEngine.UIElements;

namespace Junklite.HUD
{
    /// <summary>How big the HUD is, scaled from each corner. 70% is the chosen size.</summary>
    public enum HudScale
    {
        [InspectorName("70%")] Percent70,
        [InspectorName("80%")] Percent80,
        [InspectorName("90%")] Percent90,
        [InspectorName("100%")] Percent100,
    }

    /// <summary>
    /// The chosen layout (HUD/new version): vitals top left, the objective popping up top right, the bare mod bar
    /// centred along the bottom, the dialogue bottom right, at 70%. Hud.uxml already puts each element in its corner
    /// zone; this sets the size, the objective's pop-up, the fade when quiet and reduced motion. Change any field in
    /// Play mode to see it live. In a project that renders in Linear colour space it also adds .hud--linear, so
    /// HudLinear.uss can swap in see-through colours that look like the prototype there.
    /// </summary>
    [RequireComponent(typeof(UIDocument))]
    [DefaultExecutionOrder(100)]
    public class HudLayout : MonoBehaviour
    {
        [SerializeField] HudScale size = HudScale.Percent70;

        [Tooltip("The objective opens in full when it changes, then folds to its title.")]
        [SerializeField] bool objectivePopsUp = true;

        [Min(1f)] [SerializeField] float popUpSeconds = 5f;

        [Tooltip("The HUD drops to 45% after a few quiet seconds; any hit, mod or step brings it back.")]
        [SerializeField] bool fadeWhenQuiet;

        [Min(1f)] [SerializeField] float quietSeconds = 4f;

        [Tooltip("Fewer glitch frames and no sideways movement.")]
        [SerializeField] bool reduceMotion;

        VisualElement _hud;
        HudObjective _objective;
        float _lastActivity;

        public HudScale Size { get => size; set { size = value; Apply(); } }
        public bool ObjectivePopsUp { get => objectivePopsUp; set { objectivePopsUp = value; Apply(); } }
        public bool FadeWhenQuiet { get => fadeWhenQuiet; set { fadeWhenQuiet = value; Apply(); } }
        public bool ReduceMotion { get => reduceMotion; set { reduceMotion = value; Apply(); } }
        public bool IsIdle => _hud != null && _hud.ClassListContains("hud--idle");
        public VisualElement Hud => _hud;

        void OnEnable()
        {
            var root = GetComponent<UIDocument>().rootVisualElement;
            _hud = root?.Q("hud");
            if (_hud == null)
            {
                Debug.LogError("HUD UI is missing. Check that the UI Document's Source Asset is Hud.uxml.", this);
                return;
            }

            // The UI Document root can be full width but zero height in the Game view (see PIPELINE.md).
            root.StretchToParentSize();
            root.pickingMode = PickingMode.Ignore;

            _objective = GetComponent<HudObjective>();
            HudFx.Activity += Wake;
            HudInput.AnyInput += Wake;
            Apply();
        }

        void OnDisable()
        {
            HudFx.Activity -= Wake;
            HudInput.AnyInput -= Wake;
        }

        void OnValidate()
        {
            if (Application.isPlaying && isActiveAndEnabled && _hud != null) Apply();
        }

        void Update()
        {
            if (_hud == null || !fadeWhenQuiet || IsIdle) return;
            if (Time.unscaledTime - _lastActivity >= quietSeconds) _hud.AddToClassList("hud--idle");
        }

        /// <summary>Sets the size and colour-space classes, the objective's pop-up time and reduced motion.</summary>
        public void Apply()
        {
            if (_hud == null) return;
            HudFx.ReducedMotion = reduceMotion;
            _hud.EnableInClassList("hud--linear", QualitySettings.activeColorSpace == ColorSpace.Linear);

            _hud.EnableInClassList("size-70", size == HudScale.Percent70);
            _hud.EnableInClassList("size-80", size == HudScale.Percent80);
            _hud.EnableInClassList("size-90", size == HudScale.Percent90);

            if (_objective != null) _objective.SetAutoCompact(objectivePopsUp ? popUpSeconds : 0f);
            Wake();
        }

        /// <summary>The next size up (70, 80, 90, 100, then back to 70), with a light glitch so the change shows.</summary>
        public void CycleSize()
        {
            Size = (HudScale)(((int)size + 1) % 4);
            HudFx.Tear(new[] { _hud.Q("ob"), _hud.Q("hp"), _hud.Q("mods-wrap") },
                new TearOptions { frames = 4, amp = 14, hot = 0f, cold = 0.4f });
        }

        /// <summary>Brings a faded HUD back to full and restarts the quiet timer.</summary>
        public void Wake()
        {
            _lastActivity = Time.unscaledTime;
            _hud?.RemoveFromClassList("hud--idle");
        }

        /// <summary>Fades the HUD back now, without waiting for the quiet timer.</summary>
        public void Fade() => _hud?.AddToClassList("hud--idle");
    }
}
