using UnityEngine;
#if JUNKLITE_INPUT_SYSTEM && ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;
#endif

namespace Junklite.HUD
{
    /// <summary>
    /// Reads the player's keyboard and gamepad for the HUD: Z X C V (or hold L2 + square, triangle, circle,
    /// cross) use a mod, Q (L3) toggles the mod state, E (cross) moves the dialogue on. The last device
    /// used decides which prompts are drawn. Turn it off if the game reads input itself and calls
    /// <see cref="HudInput.Press"/> and <see cref="HudInput.SetDevice"/>.
    /// </summary>
    public class HudInputReader : MonoBehaviour
    {
        [Tooltip("Switch the prompts to whichever device the player touched last.")]
        [SerializeField] bool followLastDevice = true;

        void Update()
        {
#if JUNKLITE_INPUT_SYSTEM && ENABLE_INPUT_SYSTEM
            ReadKeyboard();
            ReadGamepad();
#elif ENABLE_LEGACY_INPUT_MANAGER
            ReadLegacy();
#endif
        }

#if JUNKLITE_INPUT_SYSTEM && ENABLE_INPUT_SYSTEM
        static readonly HudAction[] KeyboardActions =
        {
            HudAction.Next, HudAction.ModState, HudAction.Slot0, HudAction.Slot1, HudAction.Slot2, HudAction.Slot3,
        };

        void ReadKeyboard()
        {
            var keyboard = Keyboard.current;
            if (keyboard == null) return;
            if (keyboard.anyKey.wasPressedThisFrame) HudInput.RaiseAnyInput();

            foreach (var action in KeyboardActions)
            {
                var control = KeyControl(keyboard, HudInput.KeyFor(action));
                if (control == null || !control.wasPressedThisFrame) continue;
                if (keyboard.ctrlKey.isPressed || keyboard.altKey.isPressed) continue;
                if (followLastDevice) HudInput.SetDevice(HudDevice.Keyboard);
                HudInput.Press(action);
            }
        }

        static KeyControl KeyControl(Keyboard keyboard, string key)
        {
            if (string.IsNullOrEmpty(key)) return null;
            return System.Enum.TryParse(key, true, out Key parsed) && parsed != Key.None ? keyboard[parsed] : null;
        }

        void ReadGamepad()
        {
            var pad = Gamepad.current;
            if (pad == null) return;

            bool any = false;
            foreach (var control in pad.allControls)
            {
                if (control is ButtonControl button && !button.synthetic && button.wasPressedThisFrame)
                {
                    any = true;
                    break;
                }
            }
            if (!any) return;

            HudInput.RaiseAnyInput();
            if (followLastDevice) HudInput.SetDevice(HudDevice.Gamepad);

            bool l2 = Button(pad, HudInput.PadFor(HudAction.ModHold))?.isPressed ?? false;
            for (int slot = 0; slot < 4; slot++)
            {
                var action = HudInput.SlotAction(slot);
                if (l2 && Pressed(pad, HudInput.PadFor(action))) HudInput.Press(action);
            }

            if (Pressed(pad, HudInput.PadFor(HudAction.ModState))) HudInput.Press(HudAction.ModState);

            // Cross also fires slot V while L2 is held, so it only moves the dialogue on without L2.
            if (!l2 && Pressed(pad, HudInput.PadFor(HudAction.Next))) HudInput.Press(HudAction.Next);
        }

        static bool Pressed(Gamepad pad, PadButton button) => Button(pad, button)?.wasPressedThisFrame ?? false;

        static ButtonControl Button(Gamepad pad, PadButton button)
        {
            switch (button)
            {
                case PadButton.Cross: return pad.buttonSouth;
                case PadButton.Circle: return pad.buttonEast;
                case PadButton.Square: return pad.buttonWest;
                case PadButton.Triangle: return pad.buttonNorth;
                case PadButton.L2: return pad.leftTrigger;
                case PadButton.L3: return pad.leftStickButton;
                default: return null;
            }
        }
#elif ENABLE_LEGACY_INPUT_MANAGER
        void ReadLegacy()
        {
            if (Input.anyKeyDown) HudInput.RaiseAnyInput();
            ReadLegacyKey(HudAction.Next);
            ReadLegacyKey(HudAction.ModState);
            for (int slot = 0; slot < 4; slot++) ReadLegacyKey(HudInput.SlotAction(slot));
        }

        void ReadLegacyKey(HudAction action)
        {
            string key = HudInput.KeyFor(action);
            if (string.IsNullOrEmpty(key) || !System.Enum.TryParse(key, true, out KeyCode code)) return;
            if (!Input.GetKeyDown(code)) return;
            if (followLastDevice) HudInput.SetDevice(HudDevice.Keyboard);
            HudInput.Press(action);
        }
#endif
    }
}
