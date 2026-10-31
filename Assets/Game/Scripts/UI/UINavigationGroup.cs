using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;

namespace junklite
{
    /// <summary>
    /// Gamepad/keyboard navigation for a list of buttons, built from the hierarchy.
    /// Every active, interactable Selectable under this object is linked in hierarchy
    /// order (Explicit navigation), so adding, removing, reordering, hiding or disabling
    /// buttons in the Editor or at runtime needs no code or Inspector changes.
    /// Put it on the layout parent of the buttons (e.g. a VerticalLayoutGroup).
    /// A Selectable authored with Navigation = None is left out of the chain.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class UINavigationGroup : MonoBehaviour
    {
        public enum Axis { Vertical, Horizontal }

        [Tooltip("Vertical: Up/Down move through the list. Horizontal: Left/Right.")]
        [SerializeField] private Axis axis = Axis.Vertical;
        [Tooltip("Moving past the last entry returns to the first, and the reverse.")]
        [SerializeField] private bool wrapAround = true;
        [Tooltip("Selected when the group opens and when a controller resumes with nothing selected. " +
                 "Empty = first entry.")]
        [SerializeField] private Selectable defaultSelection;
        [SerializeField] private bool selectOnEnable = true;
        [Tooltip("Moving the mouse over an entry selects it, so mouse and controller never highlight two buttons.")]
        [SerializeField] private bool selectOnHover = true;

        private readonly List<Selectable> entries = new();
        private readonly List<Selectable> scanBuffer = new();
        private readonly List<bool> entryNavigable = new();
        private readonly List<Selectable> chain = new();
        private int scannedHierarchyCount = -1;
        private bool selectPending;
        private Selectable lastSelected;

        public IReadOnlyList<Selectable> Entries => entries;

        private void OnEnable()
        {
            Rebuild();
            selectPending = selectOnEnable;
        }

        private void OnTransformChildrenChanged() => Rebuild();

        /// <summary>Re-links the entries. Changes are also picked up automatically each frame.</summary>
        public void Rebuild()
        {
            entries.Clear();
            entryNavigable.Clear();
            scannedHierarchyCount = transform.hierarchyCount;

            // Hierarchy (depth-first) order matches the visual order of layout groups.
            GetComponentsInChildren(true, scanBuffer);
            foreach (Selectable selectable in scanBuffer)
            {
                // Navigation = None is an authored opt-out. Ours are Explicit after linking.
                if (selectable.navigation.mode == Navigation.Mode.None) continue;
                entries.Add(selectable);
                entryNavigable.Add(IsNavigable(selectable));
            }
            scanBuffer.Clear();

            Link();
        }

        private void Update()
        {
            if (HierarchyChanged())
                Rebuild();

            EventSystem events = EventSystem.current;
            if (events == null || entries.Count == 0) return;

            if (selectPending)
            {
                selectPending = false;
                if (!Contains(events.currentSelectedGameObject))
                    Select(events, DefaultTarget());
            }

            if (selectOnHover && TryGetHoveredEntry(events, out Selectable hovered))
            {
                if (events.currentSelectedGameObject != hovered.gameObject)
                    Select(events, hovered);
                return;
            }

            GameObject selected = events.currentSelectedGameObject;
            if (selected != null && Contains(selected))
            {
                Selectable current = selected.GetComponent<Selectable>();
                if (IsNavigable(current))
                {
                    lastSelected = current;
                    return;
                }

                // The selected entry was disabled or hidden: hand focus to a live neighbour.
                Select(events, DefaultTarget());
                return;
            }

            // Mouse clicks on empty space clear the selection, and UI navigation only moves
            // from a selected object. Give it back on the next move or submit.
            if (selected == null && NavigationRequested(events))
                Select(events, lastSelected != null && IsNavigable(lastSelected) ? lastSelected : DefaultTarget());
        }

        private bool HierarchyChanged()
        {
            if (transform.hierarchyCount != scannedHierarchyCount)
                return true;

            for (int i = 0; i < entries.Count; i++)
            {
                if (entries[i] == null || IsNavigable(entries[i]) != entryNavigable[i])
                    return true;
            }
            return false;
        }

        private void Link()
        {
            chain.Clear();
            foreach (Selectable entry in entries)
            {
                if (IsNavigable(entry))
                    chain.Add(entry);
            }

            int count = chain.Count;
            bool wraps = wrapAround && count > 1;
            for (int i = 0; i < count; i++)
            {
                Selectable back = i > 0 ? chain[i - 1] : wraps ? chain[count - 1] : null;
                Selectable forward = i < count - 1 ? chain[i + 1] : wraps ? chain[0] : null;

                var nav = new Navigation { mode = Navigation.Mode.Explicit };
                if (axis == Axis.Vertical)
                {
                    nav.selectOnUp = back;
                    nav.selectOnDown = forward;
                }
                else
                {
                    nav.selectOnLeft = back;
                    nav.selectOnRight = forward;
                }
                chain[i].navigation = nav;
            }
            chain.Clear();
        }

        private Selectable DefaultTarget()
        {
            if (defaultSelection != null && IsNavigable(defaultSelection) && Contains(defaultSelection.gameObject))
                return defaultSelection;

            foreach (Selectable entry in entries)
            {
                if (IsNavigable(entry))
                    return entry;
            }
            return null;
        }

        private bool Contains(GameObject target)
        {
            if (target == null) return false;
            foreach (Selectable entry in entries)
            {
                if (entry != null && entry.gameObject == target)
                    return true;
            }
            return false;
        }

        private static void Select(EventSystem events, Selectable target)
        {
            if (target != null)
                events.SetSelectedGameObject(target.gameObject);
        }

        private static bool IsNavigable(Selectable selectable) =>
            selectable != null && selectable.isActiveAndEnabled && selectable.IsInteractable();

        private bool TryGetHoveredEntry(EventSystem events, out Selectable hovered)
        {
            hovered = null;
            Mouse mouse = Mouse.current;
            if (mouse == null || mouse.delta.ReadValue().sqrMagnitude < 0.01f) return false;
            if (events.currentInputModule is not InputSystemUIInputModule module) return false;

            GameObject hit = module.GetLastRaycastResult(mouse.deviceId).gameObject;
            if (hit == null) return false;

            hovered = hit.GetComponentInParent<Selectable>();
            return hovered != null && IsNavigable(hovered) && Contains(hovered.gameObject);
        }

        private static bool NavigationRequested(EventSystem events)
        {
            if (events.currentInputModule is not InputSystemUIInputModule module) return false;

            InputAction move = module.move != null ? module.move.action : null;
            InputAction submit = module.submit != null ? module.submit.action : null;
            return (move != null && move.ReadValue<Vector2>().sqrMagnitude > 0.25f) ||
                   (submit != null && submit.WasPressedThisFrame());
        }
    }
}
