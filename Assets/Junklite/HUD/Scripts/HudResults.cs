using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace Junklite.HUD
{
    /// <summary>
    /// The level results screen (HUD/new version/shared/elements/results.js, the fourth take): its own screen, edge to
    /// edge, in the guideline's darker tones and white. The grade, the time and the total kills on the left; the kill
    /// breakdown, Retry and Continue on the right. It fades in once and holds still.
    /// The game says what was picked: <see cref="Select"/> moves the highlight, <see cref="ShowConfirm"/> names the
    /// confirm button on it. The mouse can pick too: <see cref="Hovered"/> and <see cref="Clicked"/> report it
    /// (0 is Retry, 1 is Continue). It sits on its own UI Document from Resources/JunkliteHUD; make one with
    /// <see cref="Create"/>. All the words are the game's own.
    /// </summary>
    [RequireComponent(typeof(UIDocument))]
    public class HudResults : MonoBehaviour
    {
        public const int Retry = 0;
        public const int Continue = 1;
        const string TreePath = "JunkliteHUD/Results";
        const string PanelPath = "JunkliteHUD/ResultsPanelSettings";
        static readonly string[] Grades = { "S", "A", "B", "C", "D" };

        VisualElement _res;
        Label _grade;
        Label _mins;
        Label _secs;
        Label _total;
        VisualElement _killList;
        readonly VisualElement[] _buttons = new VisualElement[2];
        readonly VisualElement[] _keys = new VisualElement[2];
        readonly Label[] _keyText = new Label[2];
        readonly PadGlyph[] _keyGlyph = new PadGlyph[2];
        HudFx.Timer _in;
        int _selected = Continue;

        /// <summary>The mouse is over a button (0 Retry, 1 Continue).</summary>
        public event Action<int> Hovered;

        /// <summary>A button was clicked (0 Retry, 1 Continue).</summary>
        public event Action<int> Clicked;

        public bool IsShown { get; private set; }
        public int Selected => _selected;

        /// <summary>Makes the screen on its own UI Document, as a child of <paramref name="parent"/>.</summary>
        public static HudResults Create(Transform parent, string name = "Junklite Results")
        {
            var go = new GameObject(name);
            go.SetActive(false);
            if (parent != null) go.transform.SetParent(parent, false);
            var document = go.AddComponent<UIDocument>();
            document.panelSettings = Resources.Load<PanelSettings>(PanelPath);
            document.visualTreeAsset = Resources.Load<VisualTreeAsset>(TreePath);
            if (document.panelSettings == null || document.visualTreeAsset == null)
                Debug.LogError($"The results screen's UI is missing. Check Resources/{TreePath}.uxml and {PanelPath}.asset in the Junklite HUD folder.");
            var results = go.AddComponent<HudResults>();
            go.SetActive(true);
            return results;
        }

        void OnEnable()
        {
            var root = GetComponent<UIDocument>().rootVisualElement;
            _res = root?.Q("res");
            if (_res == null) return;
            root.StretchToParentSize();
            root.pickingMode = PickingMode.Ignore;
            _grade = _res.Q<Label>("res-grade");
            _mins = _res.Q<Label>("res-mins");
            _secs = _res.Q<Label>("res-secs");
            _total = _res.Q<Label>("res-total");
            _killList = _res.Q("res-kill-list");
            _buttons[Retry] = _res.Q("res-retry");
            _buttons[Continue] = _res.Q("res-continue");

            for (int i = 0; i < 2; i++)
            {
                int index = i;
                var key = new VisualElement { pickingMode = PickingMode.Ignore };
                key.AddToClassList("res-key");
                var text = new Label { pickingMode = PickingMode.Ignore, enableRichText = false };
                text.AddToClassList("res-key__txt");
                var glyph = new PadGlyph();
                glyph.AddToClassList("res-key__glyph");
                key.Add(text);
                key.Add(glyph);
                _buttons[i].Add(key);
                _keys[i] = key;
                _keyText[i] = text;
                _keyGlyph[i] = glyph;
                _buttons[i].RegisterCallback<PointerEnterEvent>(_ => { if (IsShown) Hovered?.Invoke(index); });
                _buttons[i].RegisterCallback<ClickEvent>(_ => { if (IsShown) Clicked?.Invoke(index); });
            }

            ShowConfirm("ENTER", PadButton.None, false);
            Select(_selected);
            IsShown = false;
        }

        void OnDisable()
        {
            _in?.Cancel();
            IsShown = false;
        }

        /// <summary>
        /// Shows the screen. <paramref name="kills"/>: each enemy's name and count, in order (only counts above zero
        /// are listed). <paramref name="grade"/>: S, A, B, C or D. Continue starts selected.
        /// </summary>
        public void Show(float seconds, int totalKills, IEnumerable<KeyValuePair<string, int>> kills, string grade)
        {
            if (_res == null) return;
            string g = Array.IndexOf(Grades, grade) >= 0 ? grade : "D";
            _grade.text = g;
            _mins.text = Mathf.FloorToInt(seconds / 60f).ToString();
            _secs.text = Mathf.FloorToInt(seconds % 60f).ToString();
            _total.text = totalKills.ToString();

            _killList.Clear();
            if (kills != null)
            {
                foreach (var pair in kills)
                {
                    if (pair.Value <= 0) continue;
                    var row = new VisualElement { pickingMode = PickingMode.Ignore };
                    row.AddToClassList("res-kill");
                    var label = new Label((pair.Key ?? string.Empty).ToUpperInvariant()) { pickingMode = PickingMode.Ignore, enableRichText = false };
                    label.AddToClassList("res-kill__label");
                    var count = new Label(pair.Value.ToString()) { pickingMode = PickingMode.Ignore };
                    count.AddToClassList("res-kill__count");
                    row.Add(label);
                    row.Add(count);
                    _killList.Add(row);
                }
            }

            foreach (var x in Grades) _res.EnableInClassList($"res--{x.ToLowerInvariant()}", x == g);
            Select(Continue);

            _res.RemoveFromClassList("res--on");
            _res.AddToClassList("res--shown");
            _in?.Cancel();
            _in = HudFx.NextFrames(() => _res.AddToClassList("res--on"));
            IsShown = true;
        }

        /// <summary>Highlights a button: 0 Retry, 1 Continue.</summary>
        public void Select(int index)
        {
            _selected = Mathf.Clamp(index, 0, 1);
            for (int i = 0; i < 2; i++)
                _buttons[i]?.EnableInClassList("res-btn--on", i == _selected);
        }

        public void Move(int direction) => Select(_selected + direction);

        /// <summary>
        /// Names the confirm button on the selected one: a key ("ENTER") on a keyboard; on a gamepad a face button's
        /// glyph, or the button's name when it has none.
        /// </summary>
        public void ShowConfirm(string key, PadButton button, bool gamepad)
        {
            bool glyph = gamepad && HudInput.IsFaceButton(button);
            for (int i = 0; i < 2; i++)
            {
                if (_keys[i] == null) continue;
                _keys[i].EnableInClassList("res-key--pad", glyph);
                _keyGlyph[i].Glyph = glyph ? button : PadButton.None;
                _keyText[i].text = glyph ? string.Empty : (key ?? string.Empty).ToUpperInvariant();
            }
        }

        /// <summary>Takes the screen away at once (the game is moving on), or fades it out.</summary>
        public void Hide(bool instant = true)
        {
            _in?.Cancel();
            IsShown = false;
            if (_res == null) return;
            _res.RemoveFromClassList("res--on");
            if (instant) _res.RemoveFromClassList("res--shown");
            else _in = HudFx.After(0.26f, () => _res.RemoveFromClassList("res--shown"));
        }
    }
}
