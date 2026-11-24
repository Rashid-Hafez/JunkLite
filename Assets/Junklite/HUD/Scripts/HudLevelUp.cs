using UnityEngine;
using UnityEngine.UIElements;

namespace Junklite.HUD
{
    /// <summary>
    /// The level-up notice (HUD/new version/shared/elements/popups.js, .lvl), text only: a yellow chip and the new
    /// level, the rewards, and where to spend them. It comes in from the left under the vitals, holds, and leaves on
    /// its own. It sits on its own UI Document from Resources/JunkliteHUD; make one with <see cref="Create"/>. It
    /// follows the HUD's size. All of its words come from the caller.
    /// </summary>
    [RequireComponent(typeof(UIDocument))]
    public class HudLevelUp : MonoBehaviour
    {
        const string TreePath = "JunkliteHUD/LevelUp";
        const string PanelPath = "JunkliteHUD/PromptPanelSettings";

        // where the vitals end at 100%: the zone's top (48) plus the vitals' height and a gap, scaled with the HUD
        const float ZoneTop = 48f;
        const float BelowVitals = 167f;

        VisualElement _screen;
        VisualElement _zone;
        VisualElement _lvl;
        Label _chip;
        Label _level;
        Label _reward;
        Label _hint;
        HudFx.Timer _in;
        HudFx.Timer _out;

        public bool IsShown { get; private set; }

        /// <summary>Makes the notice on its own UI Document, as a child of <paramref name="parent"/>.</summary>
        public static HudLevelUp Create(Transform parent, string name = "Junklite Level Up")
        {
            var go = new GameObject(name);
            go.SetActive(false);
            if (parent != null) go.transform.SetParent(parent, false);
            var document = go.AddComponent<UIDocument>();
            document.panelSettings = Resources.Load<PanelSettings>(PanelPath);
            document.visualTreeAsset = Resources.Load<VisualTreeAsset>(TreePath);
            if (document.panelSettings == null || document.visualTreeAsset == null)
                Debug.LogError($"The level-up notice's UI is missing. Check Resources/{TreePath}.uxml and {PanelPath}.asset in the Junklite HUD folder.");
            var notice = go.AddComponent<HudLevelUp>();
            go.SetActive(true);
            return notice;
        }

        void OnEnable()
        {
            var root = GetComponent<UIDocument>().rootVisualElement;
            _screen = root?.Q("hud");
            if (_screen == null) return;
            root.StretchToParentSize();
            root.pickingMode = PickingMode.Ignore;
            _zone = _screen.Q("lvl-zone");
            _lvl = _screen.Q("lvl");
            _chip = _screen.Q<Label>("lvl-chip");
            _level = _screen.Q<Label>("lvl-level");
            _reward = _screen.Q<Label>("lvl-reward");
            _hint = _screen.Q<Label>("lvl-hint");
            _screen.EnableInClassList("hud--linear", QualitySettings.activeColorSpace == ColorSpace.Linear);
            IsShown = false;
        }

        void OnDisable()
        {
            _in?.Cancel();
            _out?.Cancel();
            IsShown = false;
        }

        /// <summary>
        /// Shows the notice for <paramref name="seconds"/> (then it leaves). <paramref name="rewards"/> and
        /// <paramref name="hint"/> can be empty, which hides that line.
        /// </summary>
        public void Show(string chip, string level, string rewards, string hint, float seconds)
        {
            if (_lvl == null) return;
            _in?.Cancel();
            _out?.Cancel();
            MatchHudSize();

            _chip.text = chip ?? string.Empty;
            HudFx.SetText(_level, level ?? string.Empty);
            HudFx.SetText(_reward, string.Empty);
            _hint.text = hint ?? string.Empty;
            _reward.style.display = string.IsNullOrEmpty(rewards) ? DisplayStyle.None : DisplayStyle.Flex;
            _hint.style.display = string.IsNullOrEmpty(hint) ? DisplayStyle.None : DisplayStyle.Flex;

            _lvl.RemoveFromClassList("lvl--instant");
            _in = HudFx.NextFrames(() => _lvl.AddToClassList("lvl--on"));
            HudFx.Scramble(_level, 360);
            HudFx.Tear(_lvl.Query(className: "tear").ToList(), new TearOptions { frames = 6, amp = 22f, hot = 0.4f, cold = 0f });
            if (!string.IsNullOrEmpty(rewards))
                HudFx.After(0.24f, () => HudFx.Scramble(_reward, 260, rewards));
            HudFx.RaiseActivity();

            IsShown = true;
            _out = HudFx.After(Mathf.Max(0.5f, seconds), () => Hide());
        }

        /// <summary>The notice fades out, or goes at once.</summary>
        public void Hide(bool instant = false)
        {
            _in?.Cancel();
            _out?.Cancel();
            IsShown = false;
            if (_lvl == null) return;
            _lvl.EnableInClassList("lvl--instant", instant);
            _lvl.RemoveFromClassList("lvl--on");
        }

        void MatchHudSize()
        {
            var layout = FindAnyObjectByType<HudLayout>();
            var size = layout != null ? layout.Size : HudScale.Percent70;
            _screen.EnableInClassList("size-70", size == HudScale.Percent70);
            _screen.EnableInClassList("size-80", size == HudScale.Percent80);
            _screen.EnableInClassList("size-90", size == HudScale.Percent90);
            float scale = size == HudScale.Percent70 ? 0.7f : size == HudScale.Percent80 ? 0.8f : size == HudScale.Percent90 ? 0.9f : 1f;
            _zone.style.top = ZoneTop + BelowVitals * scale;
        }
    }
}
