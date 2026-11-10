using Junklite.Inventory;
using Junklite.Pause;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;

namespace junklite
{
    /// <summary>
    /// Controller support for the UI Toolkit screens (Assets/Junklite): they only react to UI Toolkit
    /// navigation events, which a gamepad never produces here. While the screen is live this reads the
    /// gamepad and sends the focused element the same events the keyboard does: D-pad / left stick =
    /// NavigationMoveEvent (with a hold delay and repeat, so the stick doesn't race), A = NavigationSubmitEvent.
    /// Inventory: LB / RB = previous / next tab, B = back / close. The pause screen's B and Start are
    /// handled by <see cref="PauseScreenBridge"/>. Keyboard and mouse are untouched.
    /// Lives on the screen's prefab next to its UIDocument.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class UIToolkitGamepadInput : MonoBehaviour
    {
        private static readonly InventoryTab[] TabOrder = { InventoryTab.Inventory, InventoryTab.Upgrades, InventoryTab.Missions };

        [Tooltip("Set on the inventory screen: enables LB/RB tabs and B to close, and pauses input while the pause menu dims it.")]
        [SerializeField] private InventoryScreen screen;
        [Tooltip("Set on the pause screen: input only runs while it is open.")]
        [SerializeField] private PauseMenu pause;
        [SerializeField, Range(0.2f, 0.9f)] private float stickThreshold = 0.5f;
        [Tooltip("Hold time before a held direction starts repeating.")]
        [SerializeField, Min(0.05f)] private float repeatDelay = 0.35f;
        [SerializeField, Min(0.03f)] private float repeatInterval = 0.14f;

        private VisualElement root;
        private NavigationMoveEvent.Direction heldDirection = NavigationMoveEvent.Direction.None;
        private float nextRepeat;
        private bool sending;
        private int sentFrame = -1;

        private void Awake()
        {
            if (screen == null) screen = GetComponent<InventoryScreen>();
            if (pause == null) pause = GetComponent<PauseMenu>();
        }

        private void OnEnable()
        {
            heldDirection = NavigationMoveEvent.Direction.None;
            root = GetComponent<UIDocument>() != null ? GetComponent<UIDocument>().rootVisualElement : null;
            // If UI Toolkit ever does get the pad's navigation too (other input setups), drop that copy so
            // one press never moves twice.
            root?.RegisterCallback<NavigationMoveEvent>(DropDuplicate, TrickleDown.TrickleDown);
            root?.RegisterCallback<NavigationSubmitEvent>(DropDuplicate, TrickleDown.TrickleDown);
        }

        private void OnDisable()
        {
            root?.UnregisterCallback<NavigationMoveEvent>(DropDuplicate, TrickleDown.TrickleDown);
            root?.UnregisterCallback<NavigationSubmitEvent>(DropDuplicate, TrickleDown.TrickleDown);
            root = null;
        }

        private void Update()
        {
            Gamepad pad = Gamepad.current;
            if (pad == null || root == null || root.panel == null || !IsLive) return;

            if (screen != null)
            {
                if (pad.leftShoulder.wasPressedThisFrame) CycleTab(-1);
                else if (pad.rightShoulder.wasPressedThisFrame) CycleTab(1);
                if (pad.buttonEast.wasPressedThisFrame) GameUIManager.Instance?.TryClosePlayerInventory();
            }

            UpdateMove(pad);

            if (pad.buttonSouth.wasPressedThisFrame)
            {
                using var submit = NavigationSubmitEvent.GetPooled();
                Send(submit);
            }
        }

        // The inventory hands control to the pause menu while it is dimmed under it.
        private bool IsLive => (screen == null || !screen.Dimmed) && (pause == null || pause.IsOpen);

        private void UpdateMove(Gamepad pad)
        {
            Vector2 value = pad.dpad.ReadValue();
            if (value.sqrMagnitude < 0.01f) value = pad.leftStick.ReadValue();
            NavigationMoveEvent.Direction direction = ToDirection(value);

            if (direction == NavigationMoveEvent.Direction.None)
            {
                heldDirection = direction;
                return;
            }

            float now = Time.unscaledTime;
            if (direction != heldDirection) nextRepeat = now + repeatDelay;
            else if (now >= nextRepeat) nextRepeat = now + repeatInterval;
            else return;

            heldDirection = direction;
            using var move = NavigationMoveEvent.GetPooled(direction);
            Send(move);
        }

        private NavigationMoveEvent.Direction ToDirection(Vector2 value)
        {
            if (value.magnitude < stickThreshold) return NavigationMoveEvent.Direction.None;
            if (Mathf.Abs(value.x) > Mathf.Abs(value.y))
                return value.x > 0f ? NavigationMoveEvent.Direction.Right : NavigationMoveEvent.Direction.Left;
            return value.y > 0f ? NavigationMoveEvent.Direction.Up : NavigationMoveEvent.Direction.Down;
        }

        private void Send(EventBase evt)
        {
            // Sent to the focused element, like the keyboard; the screens' handlers trickle down from their roots.
            var target = root.panel.focusController.focusedElement as VisualElement ?? root;
            sending = true;
            sentFrame = Time.frameCount;
            target.SendEvent(evt);
            sending = false;
        }

        private void DropDuplicate(EventBase evt)
        {
            if (!sending && sentFrame == Time.frameCount) evt.StopImmediatePropagation();
        }

        private void CycleTab(int step)
        {
            int current = System.Array.IndexOf(TabOrder, screen.Tab);
            for (int i = 1; i <= TabOrder.Length; i++)
            {
                InventoryTab next = TabOrder[(current + step * i + TabOrder.Length * 2) % TabOrder.Length];
                if (next == screen.Tab) return;
                // ShowTab ignores tabs this screen doesn't have; stop at the first one that took.
                screen.ShowTab(next);
                if (screen.Tab == next) return;
            }
        }
    }
}
