using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace Junklite.HUD
{
    /// <summary>One row of a <see cref="HudPrompt"/>: a key or button, and what it does.</summary>
    public struct PromptRow
    {
        /// <summary>The key's name on a keyboard ("E", "SPACE"), or the button's name on a gamepad when it has no glyph ("LB").</summary>
        public string Key;

        /// <summary>On a gamepad: a face button or D-pad arrow to draw. None draws <see cref="Key"/> in a pill.</summary>
        public PadButton Pad;

        public bool Gamepad;
        public string Label;

        /// <summary>Optional, after the label ("x2").</summary>
        public string Suffix;

        public PromptRow(string key, PadButton pad, bool gamepad, string label, string suffix = null)
        {
            Key = key;
            Pad = pad;
            Gamepad = gamepad;
            Label = label;
            Suffix = suffix;
        }
    }

    /// <summary>
    /// The C.M.A. prompt (HUD/new version/shared/elements/popups.js, .pop): a small card from the objective's family,
    /// centred near the top of the screen. A header over up to three rows of a yellow button cap and what it does.
    /// When the player does it, the header changes (the game's "// SYNC OK"), the caps drop and the card leaves.
    /// A single "press to use" prompt (a pickup, a door) pings its cap until it goes.
    /// It sits on its own UI Document from Resources/JunkliteHUD, so game code can make one anywhere with
    /// <see cref="Create"/>. It follows the HUD's size (70% unless the scene's <see cref="HudLayout"/> says otherwise).
    /// All of its words come from the caller.
    /// </summary>
    [RequireComponent(typeof(UIDocument))]
    public class HudPrompt : MonoBehaviour
    {
        public const int MaxRows = 3;
        const string TreePath = "JunkliteHUD/Prompt";
        const string PanelPath = "JunkliteHUD/PromptPanelSettings";

        VisualElement _screen;
        VisualElement _zone;
        VisualElement _pop;
        Label _head;
        VisualElement _rows;
        readonly List<KeyPrompt> _keys = new List<KeyPrompt>(MaxRows);
        readonly List<VisualElement> _rowElements = new List<VisualElement>(MaxRows);
        HudFx.Timer _leave;
        HudFx.Timer _in;
        float _top = 280f;
        bool _interact;

        /// <summary>On screen and waiting on the player (false once it's completed or hidden).</summary>
        public bool IsShowing { get; private set; }

        /// <summary>The card's top edge, in pixels of the 1920 x 1080 frame.</summary>
        public float Top
        {
            get => _top;
            set
            {
                _top = value;
                if (_zone != null) _zone.style.top = value;
            }
        }

        /// <summary>Makes a prompt on its own UI Document, as a child of <paramref name="parent"/>.</summary>
        public static HudPrompt Create(Transform parent, string name = "Junklite Prompt")
        {
            var go = new GameObject(name);
            go.SetActive(false);
            if (parent != null) go.transform.SetParent(parent, false);
            var document = go.AddComponent<UIDocument>();
            document.panelSettings = Resources.Load<PanelSettings>(PanelPath);
            document.visualTreeAsset = Resources.Load<VisualTreeAsset>(TreePath);
            if (document.panelSettings == null || document.visualTreeAsset == null)
                Debug.LogError($"The prompt's UI is missing. Check Resources/{TreePath}.uxml and {PanelPath}.asset in the Junklite HUD folder.");
            var prompt = go.AddComponent<HudPrompt>();
            go.SetActive(true);
            return prompt;
        }

        void OnEnable()
        {
            var root = GetComponent<UIDocument>().rootVisualElement;
            _screen = root?.Q("hud");
            if (_screen == null) return;
            root.StretchToParentSize();
            root.pickingMode = PickingMode.Ignore;
            _zone = _screen.Q("pop-zone");
            _pop = _screen.Q("pop");
            _head = _screen.Q<Label>("pop-head");
            _rows = _screen.Q("pop-rows");
            _zone.style.top = _top;
            _screen.EnableInClassList("hud--linear", QualitySettings.activeColorSpace == ColorSpace.Linear);
            IsShowing = false;
        }

        void OnDisable()
        {
            _leave?.Cancel();
            _in?.Cancel();
            IsShowing = false;
        }

        /// <summary>Shows the card with a header and up to three rows. <paramref name="interact"/>: one "press to use" row that pings until it goes.</summary>
        public void Show(string header, IReadOnlyList<PromptRow> rows, bool interact = false)
        {
            if (_pop == null) return;
            _leave?.Cancel();
            _interact = interact;
            bool wasOn = _pop.ClassListContains("pop--on");

            MatchHudSize();
            _pop.RemoveFromClassList("pop--done");
            _pop.RemoveFromClassList("pop--instant");
            HudFx.SetText(_head, header ?? string.Empty);
            BuildRows(rows);

            IsShowing = true;
            // Next frames, so the card has its size before it fades and slides in.
            _in?.Cancel();
            _in = HudFx.NextFrames(() => _pop.AddToClassList("pop--on"));

            var torn = new List<VisualElement> { _head };
            torn.AddRange(_rowElements);
            HudFx.Tear(torn, new TearOptions { frames = wasOn ? 4 : 6, amp = wasOn ? 14f : 24f, chance = 0.7f });
            foreach (var row in _rowElements) HudFx.Scramble(row.Q<Label>(className: "pop-row__label"), 300);
            for (int i = 0; i < _keys.Count; i++)
            {
                if (interact && i == 0) _keys[i].Attention(true);
                else _keys[i].Ping();
            }
            HudFx.RaiseActivity();
        }

        /// <summary>Redraws the rows' keys (the player switched between keyboard and gamepad) without showing the card again.</summary>
        public void Repaint(IReadOnlyList<PromptRow> rows)
        {
            if (rows == null) return;
            for (int i = 0; i < _keys.Count && i < rows.Count; i++)
                _keys[i].ShowKey(rows[i].Key, rows[i].Pad, rows[i].Gamepad);
        }

        /// <summary>The player did it: the header changes, the caps drop, and after <paramref name="hold"/> seconds the card leaves.</summary>
        public void Complete(string header, float hold = 0.45f)
        {
            if (_pop == null || !IsShowing) return;
            IsShowing = false;
            foreach (var key in _keys)
            {
                key.Attention(false);
                key.Press();
                key.Hot = false;
            }
            _pop.AddToClassList("pop--done");
            HudFx.Scramble(_head, 220, header ?? string.Empty);
            var torn = new List<VisualElement> { _head };
            torn.AddRange(_rowElements);
            HudFx.Tear(torn, new TearOptions { frames = 4, amp = 12f, hot = 0f, cold = 0.7f });
            _leave?.Cancel();
            _leave = HudFx.After(Mathf.Max(0f, hold), () => Hide());
        }

        /// <summary>The card fades out, or goes at once.</summary>
        public void Hide(bool instant = false)
        {
            IsShowing = false;
            _leave?.Cancel();
            _in?.Cancel();
            if (_pop == null) return;
            foreach (var key in _keys) key.Attention(false);
            if (!instant && _pop.ClassListContains("pop--on"))
                HudFx.Tear(new VisualElement[] { _pop }, new TearOptions { frames = 3, amp = 16f, hot = 0f, cold = 0.4f });
            _pop.EnableInClassList("pop--instant", instant);
            _pop.RemoveFromClassList("pop--on");
        }

        void BuildRows(IReadOnlyList<PromptRow> rows)
        {
            foreach (var key in _keys) key.Attention(false);
            _rows.Clear();
            _keys.Clear();
            _rowElements.Clear();
            int count = rows == null ? 0 : Mathf.Min(rows.Count, MaxRows);
            for (int i = 0; i < count; i++)
            {
                var data = rows[i];
                var row = new VisualElement { pickingMode = PickingMode.Ignore };
                row.AddToClassList("pop-row");
                row.AddToClassList("tear");

                var key = new KeyPrompt(HudAction.Next, PromptSize.Large, hot: true);
                key.AddToClassList("pop-row__kp");
                key.ShowKey(data.Key, data.Pad, data.Gamepad);
                row.Add(key);
                _keys.Add(key);

                var label = new Label((data.Label ?? string.Empty).ToUpperInvariant()) { pickingMode = PickingMode.Ignore, enableRichText = false };
                label.AddToClassList("pop-row__label");
                row.Add(label);

                if (!string.IsNullOrEmpty(data.Suffix))
                {
                    var suffix = new Label(data.Suffix) { pickingMode = PickingMode.Ignore, enableRichText = false };
                    suffix.AddToClassList("pop-row__suffix");
                    row.Add(suffix);
                }

                _rows.Add(row);
                _rowElements.Add(row);
            }
        }

        // The card is as big as the rest of the HUD.
        void MatchHudSize()
        {
            var layout = FindAnyObjectByType<HudLayout>();
            var size = layout != null ? layout.Size : HudScale.Percent70;
            _screen.EnableInClassList("size-70", size == HudScale.Percent70);
            _screen.EnableInClassList("size-80", size == HudScale.Percent80);
            _screen.EnableInClassList("size-90", size == HudScale.Percent90);
        }
    }
}
