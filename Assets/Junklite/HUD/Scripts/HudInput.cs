using System;
using UnityEngine;

namespace Junklite.HUD
{
    /// <summary>Everything the HUD shows a button prompt for.</summary>
    public enum HudAction
    {
        Next,
        ModState,
        ModHold,
        Slot0,
        Slot1,
        Slot2,
        Slot3,
    }

    public enum HudDevice
    {
        Keyboard,
        Gamepad,
    }

    /// <summary>Gamepad buttons the HUD draws: the GDD's PlayStation face buttons, the D-pad, L2 and L3.</summary>
    public enum PadButton
    {
        None,
        Cross,
        Circle,
        Square,
        Triangle,
        L2,
        L3,
        DpadUp,
        DpadDown,
        DpadLeft,
        DpadRight,
    }

    /// <summary>
    /// One binding table for keyboard and gamepad (HUD.BINDS in the prototype), and which device the prompts
    /// are drawn for. The last device used wins: <see cref="HudInputReader"/> switches it, or call
    /// <see cref="SetDevice"/>. Every <see cref="KeyPrompt"/> redraws itself when either changes.
    /// </summary>
    public static class HudInput
    {
        struct Binding
        {
            public string Key;
            public PadButton Pad;

            public Binding(string key, PadButton pad)
            {
                Key = key;
                Pad = pad;
            }
        }

        static readonly Binding[] Defaults =
        {
            new Binding("E", PadButton.Cross),        // Next
            new Binding("Q", PadButton.L3),           // ModState
            new Binding(null, PadButton.L2),          // ModHold (gamepad only: hold L2 + a face button)
            new Binding("Z", PadButton.Square),       // Slot0
            new Binding("X", PadButton.Triangle),     // Slot1
            new Binding("C", PadButton.Circle),       // Slot2
            new Binding("V", PadButton.Cross),        // Slot3
        };

        static readonly Binding[] Bindings = (Binding[])Defaults.Clone();

        public static HudDevice Device { get; private set; } = HudDevice.Keyboard;

        /// <summary>The prompts switched between keyboard and gamepad.</summary>
        public static event Action<HudDevice> DeviceChanged;

        /// <summary>A key or button changed with <see cref="SetKey"/> or <see cref="SetPadButton"/>.</summary>
        public static event Action BindingsChanged;

        /// <summary>The player pressed a HUD action. <see cref="HudInputReader"/> raises it; game code can too, with <see cref="Press"/>.</summary>
        public static event Action<HudAction> Pressed;

        /// <summary>Any key or button at all. The idle fade wakes on it.</summary>
        public static event Action AnyInput;

        public static void SetDevice(HudDevice device)
        {
            if (device == Device) return;
            Device = device;
            DeviceChanged?.Invoke(device);
        }

        public static void ToggleDevice() => SetDevice(Device == HudDevice.Gamepad ? HudDevice.Keyboard : HudDevice.Gamepad);

        /// <summary>The keyboard key shown for an action, e.g. "E". Null when it has none (ModHold).</summary>
        public static string KeyFor(HudAction action) => Bindings[(int)action].Key;

        public static PadButton PadFor(HudAction action) => Bindings[(int)action].Pad;

        /// <summary>Changes the key a prompt shows (and the key the reader listens for), e.g. after the player rebinds it.</summary>
        public static void SetKey(HudAction action, string key)
        {
            Bindings[(int)action].Key = string.IsNullOrEmpty(key) ? null : key.ToUpperInvariant();
            BindingsChanged?.Invoke();
        }

        public static void SetPadButton(HudAction action, PadButton button)
        {
            Bindings[(int)action].Pad = button;
            BindingsChanged?.Invoke();
        }

        public static void ResetBindings()
        {
            Array.Copy(Defaults, Bindings, Defaults.Length);
            BindingsChanged?.Invoke();
        }

        /// <summary>Tells the HUD an action was pressed, for games that read input themselves.</summary>
        public static void Press(HudAction action) => Pressed?.Invoke(action);

        internal static void RaiseAnyInput() => AnyInput?.Invoke();

        /// <summary>Buttons drawn as a glyph in a round cap: the face buttons and the D-pad. The rest get a pill with their name.</summary>
        public static bool IsFaceButton(PadButton button) =>
            button == PadButton.Cross || button == PadButton.Circle || button == PadButton.Square || button == PadButton.Triangle
            || IsDpad(button);

        public static bool IsDpad(PadButton button) =>
            button == PadButton.DpadUp || button == PadButton.DpadDown || button == PadButton.DpadLeft || button == PadButton.DpadRight;

        public static HudAction SlotAction(int slot) => (HudAction)((int)HudAction.Slot0 + Mathf.Clamp(slot, 0, 3));

        /// <summary>
        /// The button to draw for an Input System display name: "A" or "Cross" is the cross, "D-Pad Up" the up arrow,
        /// and so on. Returns None for buttons the HUD names in a pill instead (LB, RT, L-Stick, Start).
        /// </summary>
        public static PadButton PadButtonFromName(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return PadButton.None;
            string h = name.Trim().ToLowerInvariant().Replace(" ", string.Empty).Replace("-", string.Empty);
            if (h.StartsWith("dpad"))
            {
                if (h.Contains("up")) return PadButton.DpadUp;
                if (h.Contains("down")) return PadButton.DpadDown;
                if (h.Contains("left")) return PadButton.DpadLeft;
                if (h.Contains("right")) return PadButton.DpadRight;
                return PadButton.None;
            }
            switch (h)
            {
                case "a": case "cross": case "buttonsouth": return PadButton.Cross;
                case "b": case "circle": case "buttoneast": return PadButton.Circle;
                case "x": case "square": case "buttonwest": return PadButton.Square;
                case "y": case "triangle": case "buttonnorth": return PadButton.Triangle;
                default: return PadButton.None;
            }
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            Array.Copy(Defaults, Bindings, Defaults.Length);
            Device = HudDevice.Keyboard;
            DeviceChanged = null;
            BindingsChanged = null;
            Pressed = null;
            AnyInput = null;
        }
    }
}
