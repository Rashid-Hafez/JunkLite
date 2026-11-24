using UnityEngine;
using UnityEngine.UIElements;

namespace Junklite.HUD
{
    public enum PromptSize
    {
        Medium,
        Large,
        Small,
        ExtraSmall,
    }

    /// <summary>
    /// A button prompt (.kp): a solid, raised cap, the only light filled shape in the HUD, so it reads at a glance.
    /// Keyboard is a square keycap with the letter; gamepad is a round face button with a drawn glyph, or a pill
    /// for L2 and L3. It redraws itself when the device or a binding changes.
    /// In UXML: &lt;hud:KeyPrompt action="Next" size="Large" /&gt;.
    /// </summary>
    [UxmlElement]
    public partial class KeyPrompt : VisualElement
    {
        readonly VisualElement _ring;
        readonly VisualElement _cap;
        readonly Label _text;
        readonly PadGlyph _glyph;

        HudAction _action;
        PromptSize _size;
        bool _hot;
        bool _fixed;
        string _fixedKey;
        PadButton _fixedPad;
        bool _fixedGamepad;
        HudFx.Timer _down;
        HudFx.Timer _refuse;
        HudFx.Timer _attention;
        HudFx.Timer _ping;

        [UxmlAttribute]
        public HudAction Action
        {
            get => _action;
            set
            {
                _action = value;
                Paint();
            }
        }

        [UxmlAttribute]
        public PromptSize Size
        {
            get => _size;
            set
            {
                _size = value;
                EnableInClassList("kp--lg", value == PromptSize.Large);
                EnableInClassList("kp--sm", value == PromptSize.Small);
                EnableInClassList("kp--xs", value == PromptSize.ExtraSmall);
            }
        }

        /// <summary>Hot: asks for a press now (a yellow cap; cyan on the mod bar, whose caps are yellow already).</summary>
        [UxmlAttribute]
        public bool Hot
        {
            get => _hot;
            set
            {
                _hot = value;
                EnableInClassList("kp--hot", value);
            }
        }

        /// <summary>True when the current device has no binding for the action, so the prompt is hidden.</summary>
        public bool IsUnbound => ClassListContains("kp--none");

        public bool IsAttention => ClassListContains("kp--attention");

        /// <summary>What the cap shows right now: a key ("E", "L2") or a glyph name ("Cross").</summary>
        public string Shown => _glyph.style.display == DisplayStyle.Flex ? _glyph.Glyph.ToString() : _text.text;

        public KeyPrompt()
        {
            AddToClassList("kp");
            pickingMode = PickingMode.Ignore;

            _ring = new VisualElement { pickingMode = PickingMode.Ignore };
            _ring.AddToClassList("kp__ring");
            Add(_ring);

            _cap = new VisualElement { pickingMode = PickingMode.Ignore };
            _cap.AddToClassList("kp__cap");
            Add(_cap);

            _text = new Label { pickingMode = PickingMode.Ignore, enableRichText = false };
            _text.AddToClassList("kp__txt");
            _cap.Add(_text);

            _glyph = new PadGlyph();
            _glyph.AddToClassList("kp__glyph");
            _cap.Add(_glyph);

            RegisterCallback<AttachToPanelEvent>(_ =>
            {
                HudInput.DeviceChanged += OnDeviceChanged;
                HudInput.BindingsChanged += Paint;
                Paint();
            });
            RegisterCallback<DetachFromPanelEvent>(_ =>
            {
                HudInput.DeviceChanged -= OnDeviceChanged;
                HudInput.BindingsChanged -= Paint;
                _attention?.Cancel();
            });
            Paint();
        }

        public KeyPrompt(HudAction action, PromptSize size = PromptSize.Medium, bool hot = false) : this()
        {
            Action = action;
            Size = size;
            Hot = hot;
        }

        void OnDeviceChanged(HudDevice device)
        {
            Paint();
            if (!IsUnbound)
                HudFx.Tear(new VisualElement[] { this }, new TearOptions { frames = 3, amp = 6, chance = 1f, hot = 0f, cold = 0.5f });
        }

        /// <summary>
        /// Shows a given key instead of an action's binding, for prompts whose keys come from the game's own input
        /// (the C.M.A. prompt, the results screen). On a gamepad, a face button or D-pad arrow is drawn as a glyph;
        /// any other button (LB, L-STICK) is a pill with its name. An empty key and no button hides the cap.
        /// </summary>
        public void ShowKey(string key, PadButton button, bool gamepad)
        {
            _fixed = true;
            _fixedKey = key;
            _fixedPad = button;
            _fixedGamepad = gamepad;
            Paint();
        }

        /// <summary>Draws the cap for the current device and binding.</summary>
        public void Paint()
        {
            if (_fixed)
            {
                PaintFixed();
                return;
            }

            bool pad = HudInput.Device == HudDevice.Gamepad;
            string key = HudInput.KeyFor(_action);
            var button = HudInput.PadFor(_action);
            bool none = pad ? button == PadButton.None : string.IsNullOrEmpty(key);
            bool face = pad && HudInput.IsFaceButton(button);

            EnableInClassList("kp--none", none);
            EnableInClassList("kp--round", face);
            EnableInClassList("kp--pill", pad && !none && !face);

            _glyph.Glyph = face ? button : PadButton.None;
            _glyph.style.display = face ? DisplayStyle.Flex : DisplayStyle.None;
            _text.style.display = face ? DisplayStyle.None : DisplayStyle.Flex;
            _text.text = none ? string.Empty : pad ? button.ToString() : key;
        }

        void PaintFixed()
        {
            bool face = _fixedGamepad && HudInput.IsFaceButton(_fixedPad);
            bool none = !face && string.IsNullOrEmpty(_fixedKey);

            EnableInClassList("kp--none", none);
            EnableInClassList("kp--round", face);
            EnableInClassList("kp--pill", _fixedGamepad && !none && !face);

            _glyph.Glyph = face ? _fixedPad : PadButton.None;
            _glyph.style.display = face ? DisplayStyle.Flex : DisplayStyle.None;
            _text.style.display = face ? DisplayStyle.None : DisplayStyle.Flex;
            _text.text = none || face ? string.Empty : _fixedKey.ToUpperInvariant();
        }

        /// <summary>The cap drops onto its base for 120 ms: the press the player just made.</summary>
        public void Press()
        {
            AddToClassList("kp--down");
            _down?.Cancel();
            _down = HudFx.After(0.12f, () => RemoveFromClassList("kp--down"));
        }

        /// <summary>A press that did nothing: the cap turns red for 320 ms.</summary>
        public void Refuse()
        {
            AddToClassList("kp--refuse");
            _refuse?.Cancel();
            _refuse = HudFx.After(0.32f, () => RemoveFromClassList("kp--refuse"));
        }

        /// <summary>One ping of the ring.</summary>
        public void Ping()
        {
            if (HudFx.ReducedMotion) return;
            _ping?.Cancel();
            _ring.RemoveFromClassList("kp__ring--go");
            _ring.AddToClassList("kp__ring--arm");
            _ping = HudFx.NextFrames(() =>
            {
                _ring.RemoveFromClassList("kp__ring--arm");
                _ring.AddToClassList("kp__ring--go");
            });
        }

        /// <summary>Waiting on the player: pings every 1.5 s until switched off.</summary>
        public void Attention(bool on)
        {
            _attention?.Cancel();
            _attention = null;
            EnableInClassList("kp--attention", on);
            if (!on) return;
            Ping();
            _attention = HudFx.Every(1.5f, 1.5f, () =>
            {
                if (panel == null) return false;
                Ping();
                return true;
            });
        }
    }

    /// <summary>
    /// A PlayStation face-button glyph (cross, circle, square, triangle) or a D-pad arrow, drawn with strokes so it
    /// stays sharp at any HUD size. The face buttons are the shapes of HUD/shared/img/glyphs/pad-*.svg; the colour is
    /// the element's USS color.
    /// </summary>
    public class PadGlyph : VisualElement
    {
        PadButton _glyph;

        public PadButton Glyph
        {
            get => _glyph;
            set
            {
                if (_glyph == value) return;
                _glyph = value;
                MarkDirtyRepaint();
            }
        }

        public PadGlyph()
        {
            pickingMode = PickingMode.Ignore;
            generateVisualContent += Draw;
        }

        void Draw(MeshGenerationContext context)
        {
            var rect = contentRect;
            float size = Mathf.Min(rect.width, rect.height);
            if (size <= 0f || _glyph == PadButton.None) return;

            // The SVGs use a 24 x 24 box with a 2.8 stroke.
            float k = size / 24f;
            var origin = new Vector2(rect.x + (rect.width - size) / 2f, rect.y + (rect.height - size) / 2f);
            Vector2 P(float x, float y) => origin + new Vector2(x * k, y * k);

            var p = context.painter2D;
            p.lineWidth = 2.8f * k;
            p.strokeColor = resolvedStyle.color;
            p.lineCap = LineCap.Round;
            p.lineJoin = LineJoin.Round;
            p.BeginPath();

            switch (_glyph)
            {
                case PadButton.Cross:
                    p.MoveTo(P(6f, 6f));
                    p.LineTo(P(18f, 18f));
                    p.MoveTo(P(18f, 6f));
                    p.LineTo(P(6f, 18f));
                    break;
                case PadButton.Circle:
                    p.Arc(P(12f, 12f), 7f * k, new Angle(0f, AngleUnit.Degree), new Angle(360f, AngleUnit.Degree));
                    p.ClosePath();
                    break;
                case PadButton.Square:
                    p.MoveTo(P(5.5f, 5.5f));
                    p.LineTo(P(18.5f, 5.5f));
                    p.LineTo(P(18.5f, 18.5f));
                    p.LineTo(P(5.5f, 18.5f));
                    p.ClosePath();
                    break;
                case PadButton.Triangle:
                    p.MoveTo(P(12f, 4.8f));
                    p.LineTo(P(19.5f, 18f));
                    p.LineTo(P(4.5f, 18f));
                    p.ClosePath();
                    break;
                // D-pad: an arrow, a stem and a head
                case PadButton.DpadUp:
                    p.MoveTo(P(12f, 19f));
                    p.LineTo(P(12f, 5.5f));
                    p.MoveTo(P(6.5f, 11f));
                    p.LineTo(P(12f, 5.5f));
                    p.LineTo(P(17.5f, 11f));
                    break;
                case PadButton.DpadDown:
                    p.MoveTo(P(12f, 5f));
                    p.LineTo(P(12f, 18.5f));
                    p.MoveTo(P(6.5f, 13f));
                    p.LineTo(P(12f, 18.5f));
                    p.LineTo(P(17.5f, 13f));
                    break;
                case PadButton.DpadLeft:
                    p.MoveTo(P(19f, 12f));
                    p.LineTo(P(5.5f, 12f));
                    p.MoveTo(P(11f, 6.5f));
                    p.LineTo(P(5.5f, 12f));
                    p.LineTo(P(11f, 17.5f));
                    break;
                case PadButton.DpadRight:
                    p.MoveTo(P(5f, 12f));
                    p.LineTo(P(18.5f, 12f));
                    p.MoveTo(P(13f, 6.5f));
                    p.LineTo(P(18.5f, 12f));
                    p.LineTo(P(13f, 17.5f));
                    break;
            }
            p.Stroke();
        }
    }
}
