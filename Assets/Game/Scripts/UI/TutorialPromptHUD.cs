using System;
using System.Collections.Generic;
using Junklite.HUD;
using UnityEngine;

namespace junklite
{
    /// <summary>
    /// Cassidy's optic-HUD tutorial prompt: a header ("// C.M.A. OPTIC") over up to three rows of a key and what it
    /// does. A prompt stays on screen until the caller completes or hides it, and key labels follow the active device
    /// (keyboard vs gamepad). It's drawn by the Junklite HUD's C.M.A. prompt card (Junklite.HUD.HudPrompt, UI Toolkit,
    /// in the HUD's look), which it makes as a child object. Animations run on real time, so hit-stop and slow-mo
    /// don't stall it.
    /// </summary>
    public class TutorialPromptHUD : MonoBehaviour
    {
        [Serializable]
        public struct Entry
        {
            [Tooltip("Input action path, e.g. Player/Jump. Used when no override is set.")]
            public string action;
            public string label;
            [Tooltip("Optional text after the key chip, e.g. x2.")]
            public string suffix;
            public string keyboardOverride;
            public string gamepadOverride;

            public Entry(string action, string label, string suffix = null,
                string keyboardOverride = null, string gamepadOverride = null)
            {
                this.action = action;
                this.label = label;
                this.suffix = suffix;
                this.keyboardOverride = keyboardOverride;
                this.gamepadOverride = gamepadOverride;
            }
        }

        private const int MaxRows = HudPrompt.MaxRows;

        [Header("Text")]
        [SerializeField] private string header = "// C.M.A. OPTIC";
        [SerializeField] private string completeHeader = "// SYNC OK";

        [Header("Position")]
        [Tooltip("Top edge of the card, in pixels of a 1920x1080 screen.")]
        [SerializeField] private float topOffset = 280f; // below the level-up toast
        [Tooltip("Top offset used while a full-screen menu (inventory) is open, so the prompt clears its tabs.")]
        [SerializeField] private float menuTopOffset = 6f;

        [Header("Timing")]
        [Tooltip("How long the completed prompt stays before it leaves, in seconds.")]
        [SerializeField] private float completeHold = 0.45f;

        private readonly List<Entry> current = new List<Entry>(MaxRows);
        private readonly List<PromptRow> rows = new List<PromptRow>(MaxRows);

        private HudPrompt view;
        private GameInputManager input;

        public bool IsShowing => view != null && view.IsShowing;

        #region Lifecycle

        private void Awake() => Build();

        private void OnEnable() => SubscribeInput();

        private void OnDisable()
        {
            if (input != null)
                input.OnInputDeviceChanged -= HandleDeviceChanged;
            input = null;
        }

        private void Update()
        {
            // Entering combat drops a visible interact prompt (only the shared one; tutorial prompts stay).
            if (this == interactPrompt && interactOwner != null && !interactAllowedInCombat && GameInputManager.Instance != null &&
                GameInputManager.Instance.IsInteractBlocked)
            {
                interactOwner = null;
                Hide(instant: true);
            }
        }

        #endregion

        #region Public API

        private static TutorialPromptHUD interactPrompt;
        private static object interactOwner;
        // The current owner's press still works in combat (room-exit doors), so its prompt stays up.
        private static bool interactAllowedInCombat;

        /// <summary>
        /// The one on-screen prompt every world interactable shares ("[E] Pick up", "[LB] Talk"...).
        /// The last caller owns it; HideInteract from a stale owner is ignored.
        /// </summary>
        public static void ShowInteract(object owner, string label, bool allowInCombat = false)
        {
            // No interact prompt while the interact input is blocked (player in combat), except for room exits.
            if (!allowInCombat && GameInputManager.Instance != null && GameInputManager.Instance.IsInteractBlocked) return;
            interactAllowedInCombat = allowInCombat;
            if (interactPrompt == null)
            {
                // GameInputManager finds this one by its name.
                interactPrompt = new GameObject("InteractPrompt").AddComponent<TutorialPromptHUD>();
                interactPrompt.topOffset = 420f; // below a tutorial prompt shown at the same time
            }
            interactOwner = owner;
            interactPrompt.Show(true, new[] { new Entry("Player/Interact", label) });
        }

        public static void HideInteract(object owner, bool completed = false, bool instant = false)
        {
            if (interactPrompt == null || !ReferenceEquals(interactOwner, owner)) return;
            interactOwner = null;
            if (completed) interactPrompt.Complete();
            else interactPrompt.Hide(instant);
        }

        public void Show(params Entry[] entries) => Show(false, entries);

        /// <summary>Shows the done state ("// SYNC OK"), then hides the prompt.</summary>
        public void Complete()
        {
            if (!IsShowing) return;
            view.Complete(completeHeader, completeHold);
        }

        public void Hide(bool instant = false)
        {
            if (view == null) return;
            view.Hide(instant || !isActiveAndEnabled);
        }

        /// <summary>Moves the current prompt to the top edge while a full-screen menu is open. Show() resets it.</summary>
        public void SetMenuDocked(bool docked)
        {
            Build();
            view.Top = docked ? menuTopOffset : topOffset;
        }

        #endregion

        #region Rows

        // interact: a single "press to use" row, whose key pings until it's used.
        private void Show(bool interact, Entry[] entries)
        {
            Build();
            SubscribeInput();

            current.Clear();
            int count = Mathf.Min(entries?.Length ?? 0, MaxRows);
            for (int i = 0; i < count; i++)
                current.Add(entries[i]);

            view.Top = topOffset;
            view.Show(header, BuildRows(), interact);
        }

        private void HandleDeviceChanged(bool _)
        {
            if (view != null && current.Count > 0)
                view.Repaint(BuildRows());
        }

        // Each entry's key for the active device: its override, or the action's binding. On a gamepad, face buttons
        // and the D-pad are drawn as glyphs; other buttons (LB, L-Stick) show their names. Label-only entries
        // (e.g. "Collect the mod") have no key.
        private List<PromptRow> BuildRows()
        {
            rows.Clear();
            bool pad = input != null && input.IsUsingGamepad;
            for (int i = 0; i < current.Count; i++)
            {
                Entry e = current[i];
                string key = pad ? e.gamepadOverride : e.keyboardOverride;
                if (string.IsNullOrEmpty(key) && input != null && !string.IsNullOrEmpty(e.action))
                    key = input.GetBindingHint(e.action);
                PadButton button = pad ? HudInput.PadButtonFromName(key) : PadButton.None;
                rows.Add(new PromptRow(key, button, pad, e.label, e.suffix));
            }
            return rows;
        }

        private void SubscribeInput()
        {
            GameInputManager next = GameInputManager.Instance;
            if (next == input) return;
            if (input != null) input.OnInputDeviceChanged -= HandleDeviceChanged;
            input = next;
            if (input != null) input.OnInputDeviceChanged += HandleDeviceChanged;
        }

        private void Build()
        {
            if (view != null) return;
            view = HudPrompt.Create(transform, "C.M.A. Prompt");
            view.Top = topOffset;
        }

        #endregion
    }
}
