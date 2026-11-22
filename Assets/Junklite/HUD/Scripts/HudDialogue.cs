using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.Serialization;
using UnityEngine.UIElements;

namespace Junklite.HUD
{
    [Serializable]
    public class DialogueSpeaker
    {
        [Tooltip("What lines use to name this speaker, e.g. cassidy.")]
        public string id;
        public string displayName;
        public string unit;

        [Tooltip("A yellow name chip instead of the usual sky blue one, to tell a second speaker apart.")]
        [FormerlySerializedAs("cyanName")]
        public bool accentName;
    }

    [Serializable]
    public class DialogueLine
    {
        public string speaker;
        [TextArea(2, 4)] public string text;

        public DialogueLine() { }

        public DialogueLine(string speaker, string text)
        {
            this.speaker = speaker;
            this.text = text;
        }
    }

    /// <summary>
    /// The dialogue box (HUD/dialogue), bottom right, with no picture: the speaker's name chip and unit, and the line.
    /// The line types out under a quiet SKIP prompt; once it's out, the E (cross) prompt turns hot and pings every
    /// 1.5 seconds. A new speaker tears the box and scrambles the name in; the same speaker tears only the line.
    /// </summary>
    [RequireComponent(typeof(UIDocument))]
    public class HudDialogue : MonoBehaviour
    {
        [SerializeField] List<DialogueSpeaker> speakers = DefaultSpeakers();
        [SerializeField] List<DialogueLine> lines = new List<DialogueLine>();

        [SerializeField] bool openOnStart;

        [Tooltip("E opens the conversation when the box is closed. Off in the game, where E also interacts.")]
        [SerializeField] bool nextOpensWhenClosed;

        [Tooltip("Passes the line's index.")]
        [SerializeField] UnityEvent<int> onLineShown = new UnityEvent<int>();

        [SerializeField] UnityEvent onClosed = new UnityEvent();

        VisualElement _root;
        VisualElement _bound;
        VisualElement _head;
        VisualElement _lineRow;
        VisualElement _box;
        Label _name;
        Label _unit;
        Label _text;
        Label _count;
        Label _label;
        KeyPrompt _prompt;

        List<DialogueLine> _script;
        int _index = -1;
        DialogueLine _current;
        bool _open;
        bool _typing;
        string _full = string.Empty;
        int _shown;
        HudFx.Timer _type;

        public event Action<int, DialogueLine> LineShown;
        public event Action Closed;

        public bool IsOpen => _open;
        public bool IsTyping => _typing;
        public int Index => _index;
        public string VisibleText => _full.Substring(0, Mathf.Clamp(_shown, 0, _full.Length));
        public VisualElement Root => _root;
        public KeyPrompt Prompt => _prompt;

        void OnEnable()
        {
            var doc = GetComponent<UIDocument>().rootVisualElement;
            _root = doc?.Q("dlg");
            if (_root == null)
            {
                Debug.LogError("Dialogue UI is missing. Check that the UI Document's Source Asset is Hud.uxml.", this);
                return;
            }

            if (_root != _bound)
            {
                _bound = _root;
                _head = _root.Q("dlg-head");
                _lineRow = _root.Q("dlg-line");
                _name = _root.Q<Label>("dlg-name");
                _unit = _root.Q<Label>("dlg-unit");
                _text = _root.Q<Label>("dlg-text");
                _count = _root.Q<Label>("dlg-count");
                _label = _root.Q<Label>("dlg-label");
                _prompt = _root.Q<KeyPrompt>("dlg-key");
                _root.Query<Label>().ForEach(l => l.enableRichText = false);
                _text.PostProcessTextVertices = HideUntyped;

                _box = _root.Q("dlg-box");
                _box.RegisterCallback<ClickEvent>(_ =>
                {
                    if (!_open) return;
                    _prompt.Press();
                    Advance();
                });
            }

            _box.pickingMode = PickingMode.Ignore;
            HudInput.Pressed += OnPressed;
            _open = false;
            _root.RemoveFromClassList("dlg--open");
            _text.text = string.Empty;
            if (openOnStart) HudFx.After(0.5f, () => { if (isActiveAndEnabled) Open(); });
        }

        void OnDisable()
        {
            HudInput.Pressed -= OnPressed;
            _type?.Cancel();
            _prompt?.Attention(false);
        }

        // ---------- public calls ----------

        /// <summary>Opens the box on the first line of the Inspector's lines.</summary>
        public void Open() => Open(lines);

        /// <summary>Opens the box on the first of these lines.</summary>
        public void Open(IList<DialogueLine> script)
        {
            if (_root == null || script == null || script.Count == 0) return;
            if (_open && ReferenceEquals(script, _script)) return;
            _script = new List<DialogueLine>(script);
            _open = true;
            _current = null;
            _root.AddToClassList("dlg--open");
            // the box takes clicks only while it's up, so it never blocks the game underneath
            _box.pickingMode = PickingMode.Position;
            Show(0);
        }

        /// <summary>Skips the typing, or moves to the next line, or closes after the last.</summary>
        public void Advance()
        {
            if (_root == null) return;
            if (!_open)
            {
                Open();
                return;
            }
            if (_typing)
            {
                Finish();
                return;
            }
            if (_index < _script.Count - 1) Show(_index + 1);
            else Close();
        }

        public void Close()
        {
            if (!_open) return;
            _open = false;
            _type?.Cancel();
            _typing = false;
            _box.pickingMode = PickingMode.Ignore;
            _prompt.Attention(false);
            HudFx.Tear(new[] { _head, _lineRow }, new TearOptions { frames = 4, amp = 20 }, () =>
            {
                if (_open) return;
                _root.RemoveFromClassList("dlg--open");
                Closed?.Invoke();
                onClosed?.Invoke();
            });
        }

        public void Restart()
        {
            _open = false;
            _root.RemoveFromClassList("dlg--open");
            HudFx.After(0.24f, Open);
        }

        public void SetLines(IEnumerable<DialogueLine> script) => lines = new List<DialogueLine>(script);

        /// <summary>Replaces the speakers lines can name. Cassidy is the only one set up by default.</summary>
        public void SetSpeakers(IEnumerable<DialogueSpeaker> list) => speakers = new List<DialogueSpeaker>(list);

        // ---------- internals ----------

        void OnPressed(HudAction action)
        {
            if (action != HudAction.Next || !isActiveAndEnabled) return;
            if (!_open && !nextOpensWhenClosed) return;
            _prompt.Press();
            Advance();
        }

        DialogueSpeaker Speaker(DialogueLine line)
        {
            foreach (var s in speakers)
            {
                if (s.id == line.speaker) return s;
            }
            return new DialogueSpeaker { id = line.speaker, displayName = (line.speaker ?? "").ToUpperInvariant() };
        }

        void Show(int i)
        {
            var line = _script[i];
            var speaker = Speaker(line);
            var previous = _current;
            _index = i;
            _current = line;
            HudFx.SetText(_count, $"{HudFx.Pad(i + 1)} / {HudFx.Pad(_script.Count)}");

            if (previous == null || previous.speaker != line.speaker)
            {
                // new speaker: tear the box and scramble the name and unit in
                HudFx.Tear(new[] { _head, _lineRow }, new TearOptions { frames = 5, amp = 24, chance = 0.8f });
                _name.EnableInClassList("dlg-name--accent", speaker.accentName);
                HudFx.Scramble(_name, 260, speaker.displayName);
                HudFx.Scramble(_unit, 260, speaker.unit ?? string.Empty);
            }
            else
            {
                // same speaker, next line
                HudFx.Tear(new[] { _lineRow }, new TearOptions { frames = 3, amp = 8, hot = 0.2f, cold = 0.1f });
            }

            _text.text = string.Empty;
            Type(line.text ?? string.Empty);
            LineShown?.Invoke(i, line);
            onLineShown?.Invoke(i);
        }

        void Type(string text)
        {
            _type?.Cancel();
            _full = text;
            _shown = 0;
            _typing = true;
            _root.AddToClassList("dlg--typing");
            HudFx.SetText(_label, "SKIP");
            _prompt.Hot = false;
            _prompt.Attention(false);
            PaintText();
            if (HudFx.ReducedMotion || !Application.isPlaying)
            {
                Finish();
                return;
            }
            _type = HudFx.After(0.12f, Step);
        }

        void Step()
        {
            _shown++;
            PaintText();
            if (_shown >= _full.Length)
            {
                Finish();
                return;
            }
            char c = _full[_shown - 1];
            float wait = c == '.' || c == '!' || c == '?' ? 0.15f : c == ',' ? 0.08f : 0.024f;
            _type = HudFx.After(wait, Step);
        }

        void Finish()
        {
            _type?.Cancel();
            _typing = false;
            _shown = _full.Length;
            PaintText();
            _root.RemoveFromClassList("dlg--typing");
            HudFx.SetText(_label, _index >= _script.Count - 1 ? "CLOSE" : "NEXT");
            // the line is out: the prompt goes hot and pings until the player presses
            _prompt.Hot = true;
            _prompt.Attention(true);
        }

        // The whole line is laid out from the start, so words never jump lines while typing. The letters not typed yet
        // are hidden in the text's vertices (HideUntyped), which also hides their shadow; a rich-text <alpha=#00> tag
        // would leave the shadow showing. Works on Unity 6000.3 and later.
        void PaintText()
        {
            if (_text.text != _full) _text.text = _full;
            else _text.MarkDirtyText();
        }

        // Glyphs come in reading order, and only characters that draw something have one (spaces don't), so the first
        // N glyphs are the typed letters. Each glyph after those is collapsed to a point, which hides the letter and
        // its shadow, and the layout stays the whole line's.
        void HideUntyped(TextElement.GlyphsEnumerable glyphs)
        {
            int shown = Mathf.Clamp(_shown, 0, _full.Length);
            if (shown >= _full.Length) return;
            int typed = 0;
            for (int i = 0; i < shown; i++)
                if (!char.IsWhiteSpace(_full[i])) typed++;

            int index = 0;
            foreach (var glyph in glyphs)
            {
                if (index++ < typed) continue;
                var vertices = glyph.vertices;
                if (vertices.Length == 0) continue;
                var point = vertices[0].position;
                for (int i = 0; i < vertices.Length; i++)
                {
                    var v = vertices[i];
                    v.position = point;
                    v.tint.a = 0;
                    vertices[i] = v;
                }
            }
        }

        // Cassidy, with her unit from the title screen. Add other speakers in the Inspector.
        static List<DialogueSpeaker> DefaultSpeakers() => new List<DialogueSpeaker>
        {
            new DialogueSpeaker { id = "cassidy", displayName = "CASSIDY", unit = "K.R.F. / DIREHOUND" },
        };
    }
}
