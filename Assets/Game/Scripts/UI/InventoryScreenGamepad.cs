using Junklite.Inventory;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;

namespace junklite
{
    /// <summary>
    /// Controller support for the UI Toolkit inventory (Assets/Junklite/Inventory). The screen only reacts to
    /// UI Toolkit navigation events, which a gamepad never produces here, so this reads the gamepad while the
    /// screen is open and sends the same events the keyboard does: D-pad / left stick = NavigationMoveEvent
    /// (with key-repeat), A = NavigationSubmitEvent, LB / RB = previous / next tab. B (back/close) is already
    /// handled by PlayerUI through the UI Cancel action. Keyboard and mouse are untouched.
    /// Lives on the inventory screen prefab next to <see cref="InventoryScreen"/>.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class InventoryScreenGamepad : MonoBehaviour
    {
        private static readonly InventoryTab[] TabOrder = { InventoryTab.Inventory, InventoryTab.Upgrades, InventoryTab.Missions };

        [SerializeField] private InventoryScreen screen;
        [SerializeField, Range(0.2f, 0.9f)] private float stickThreshold = 0.5f;
        [Tooltip("Hold time before a held direction starts repeating.")]
        [SerializeField, Min(0.05f)] private float repeatDelay = 0.35f;
        [SerializeField, Min(0.03f)] private float repeatInterval = 0.11f;

        private VisualElement root;
        private NavigationMoveEvent.Direction heldDirection = NavigationMoveEvent.Direction.None;
        private float nextRepeat;
        private bool sending;
        private int sentFrame = -1;

        private void Awake()
        {
            if (screen == null) screen = GetComponent<InventoryScreen>();
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
            if (pad == null || screen == null || root == null || root.panel == null) return;

            if (pad.leftShoulder.wasPressedThisFrame) CycleTab(-1);
            else if (pad.rightShoulder.wasPressedThisFrame) CycleTab(1);

            UpdateMove(pad);

            if (pad.buttonSouth.wasPressedThisFrame)
            {
                using var submit = NavigationSubmitEvent.GetPooled();
                Send(submit);
            }
        }

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
            // Sent to the focused element, like the keyboard; the screen's handlers trickle down from its root.
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
