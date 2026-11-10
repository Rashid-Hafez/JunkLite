using System;
using System.Collections;
using System.Collections.Generic;
using Junklite.Inventory;
using Junklite.Shared;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UIElements;
#if JUNKLITE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace Junklite.Pause
{
    public enum PauseCommand
    {
        Resume,
        Map,
        Settings,
        QuitToTitle,
    }

    /// <summary>
    /// The pause menu. Opens over whatever is on screen with Esc or a gamepad's Start button,
    /// or from code with <see cref="Open"/>. Map and Settings aren't built yet, so they only fire
    /// <c>onLockedItem</c>.
    /// </summary>
    [RequireComponent(typeof(UIDocument))]
    public class PauseMenu : MonoBehaviour
    {
        const float ToastSeconds = 2.4f;
        const float CloseSeconds = 0.26f;

        static readonly string[] RowNames = { "pause-resume", "pause-map", "pause-settings", "pause-quit" };

        static readonly Color[] RarityColors =
        {
            new Color32(0xC9, 0xD1, 0xE6, 0xFF),
            new Color32(0x86, 0xE6, 0x8C, 0xFF),
            new Color32(0x6F, 0xD0, 0xFF, 0xFF),
            new Color32(0xFF, 0x5F, 0x74, 0xFF),
        };

        [Tooltip("Shown in the current build strip. Found in the scene if left empty.")]
        [SerializeField] ModInventory inventory;

        [Tooltip("Turn off if the game opens the menu itself.")]
        [SerializeField] bool openWithEscape = true;

        [Tooltip("Sets Time.timeScale to 0 while open.")]
        [SerializeField] bool pauseTime = true;

        [SerializeField] bool openOnStart;

        [Tooltip("Draws the light field behind the menu, for scenes with nothing else on screen.")]
        [SerializeField] bool showBackdrop;

        [SerializeField] string buildLabel = "DEMO 1";

        [SerializeField] UnityEvent onOpened = new UnityEvent();
        [SerializeField] UnityEvent onClosed = new UnityEvent();
        [SerializeField] UnityEvent onQuitToTitle = new UnityEvent();

        [Tooltip("Map or Settings was chosen. Passes the item's name.")]
        [SerializeField] UnityEvent<string> onLockedItem = new UnityEvent<string>();

        VisualElement _pause;
        VisualElement _boundPause;
        VisualElement _glide;
        VisualElement _toast;
        VisualElement _buildTiles;
        Label _toastMessage;
        Label _meta;
        Label _countLinked;
        Label _countStorage;
        Label _countElite;
        Label _countClassified;
        readonly List<Button> _rows = new List<Button>();
        InventoryScreen _inventoryScreen;
        Coroutine _toastHide;
        float _timeScaleBefore = 1f;
        int _selected = -1;
        bool _closing;

        public event Action<PauseCommand> CommandExecuted;

        public bool IsOpen { get; private set; }
        public PauseCommand? Selected => _selected >= 0 ? (PauseCommand)_selected : (PauseCommand?)null;
        public string ToastText => _toast != null && _toast.ClassListContains("toast--on") ? _toastMessage.text : null;

        void OnEnable()
        {
            var root = GetComponent<UIDocument>().rootVisualElement;
            _pause = root?.Q("pause");
            if (_pause == null)
            {
                Debug.LogError("Pause UI is missing. Check that the UI Document's Source Asset is Pause.uxml.", this);
                return;
            }

            root.StretchToParentSize();
            if (inventory == null) inventory = FindAnyObjectByType<ModInventory>();
            if (inventory != null) inventory.Changed += RenderBuild;

            if (_pause != _boundPause)
            {
                _boundPause = _pause;
                Bind();
            }

            // With a backdrop there is nothing underneath to dim.
            _pause.Q("pause-backdrop").EnableInClassList("hidden", !showBackdrop);
            _pause.Q("pause-dim").EnableInClassList("hidden", showBackdrop);
            SetClosed();
            if (openOnStart) Open();
        }

        void OnDisable()
        {
            StopAllCoroutines();
            if (inventory != null) inventory.Changed -= RenderBuild;
            if (IsOpen) SetClosed();
        }

        void Update()
        {
            if (openWithEscape && _pause != null && BackPressed()) HandleBack();
        }

        /// <summary>
        /// What Esc does: closes the menu if it's open. Otherwise the screen underneath gets the press
        /// first (to drop a picked-up mod, say), and the menu opens only if nothing used it.
        /// </summary>
        public void HandleBack()
        {
            if (IsOpen)
            {
                Close();
                return;
            }

            foreach (var behaviour in FindObjectsByType<MonoBehaviour>(FindObjectsSortMode.None))
            {
                if (behaviour != this && behaviour.isActiveAndEnabled && behaviour is IBackHandler handler && handler.HandleBack()) return;
            }
            Open();
        }

        public void Open()
        {
            if (_pause == null || IsOpen) return;
            IsOpen = true;
            _closing = false;

            if (pauseTime)
            {
                _timeScaleBefore = Time.timeScale;
                Time.timeScale = 0f;
            }

            _inventoryScreen = FindAnyObjectByType<InventoryScreen>();
            if (_inventoryScreen != null) _inventoryScreen.Dimmed = true;

            _meta.text = $"{DateTime.Now:hh:mm tt} · {buildLabel}".ToUpperInvariant();
            RenderBuild();

            // start off screen, then let the transitions bring the sidebar in
            _pause.RemoveFromClassList("pause--closed");
            _pause.RemoveFromClassList("pause--leaving");
            _pause.RemoveFromClassList("pause--fading");
            _pause.AddToClassList("pause--entering");
            _pause.schedule.Execute(() => _pause.RemoveFromClassList("pause--entering")).ExecuteLater(16);

            Select(0);
            onOpened?.Invoke();
        }

        public void Close()
        {
            if (!IsOpen || _closing) return;
            StartCoroutine(CloseRoutine());
        }

        public void Toggle()
        {
            if (IsOpen) Close();
            else Open();
        }

        IEnumerator CloseRoutine()
        {
            _closing = true;
            _pause.AddToClassList("pause--leaving");
            _pause.AddToClassList("pause--fading");
            yield return new WaitForSecondsRealtime(CloseSeconds);
            SetClosed();
            onClosed?.Invoke();
        }

        void SetClosed()
        {
            bool wasOpen = IsOpen;
            IsOpen = false;
            _closing = false;
            _selected = -1;
            _pause.AddToClassList("pause--closed");
            _pause.RemoveFromClassList("pause--leaving");
            _pause.RemoveFromClassList("pause--fading");
            _glide.RemoveFromClassList("pause-menu__glide--on");
            foreach (var row in _rows) row.RemoveFromClassList("pause-row--selected");

            if (wasOpen && pauseTime) Time.timeScale = _timeScaleBefore;
            if (_inventoryScreen != null) _inventoryScreen.Dimmed = false;
        }

        void Bind()
        {
            _glide = _pause.Q("pause-glide");
            _toast = _pause.Q("toast");
            _toastMessage = _pause.Q<Label>("toast-message");
            _meta = _pause.Q<Label>("pause-meta");
            _buildTiles = _pause.Q("build-tiles");
            _countLinked = _pause.Q<Label>("count-linked");
            _countStorage = _pause.Q<Label>("count-storage");
            _countElite = _pause.Q<Label>("count-elite");
            _countClassified = _pause.Q<Label>("count-classified");

            _rows.Clear();
            for (int i = 0; i < RowNames.Length; i++)
            {
                int index = i;
                var row = _pause.Q<Button>(RowNames[i]);
                row.clicked += () => Activate(index);
                row.RegisterCallback<PointerEnterEvent>(evt =>
                {
                    if (evt.pointerType == UnityEngine.UIElements.PointerType.mouse) Select(index);
                });
                row.RegisterCallback<FocusInEvent>(_ =>
                {
                    if (_selected != index) Select(index, false);
                });
                _rows.Add(row);
            }

            _pause.RegisterCallback<NavigationMoveEvent>(OnNavigationMove, TrickleDown.TrickleDown);
            _pause.RegisterCallback<KeyDownEvent>(OnKeyDown, TrickleDown.TrickleDown);

            _buildTiles.Clear();
            for (int i = 0; i < ModInventory.SlotCount; i++)
            {
                var tile = new VisualElement { pickingMode = PickingMode.Ignore };
                tile.AddToClassList("build-tile");
                foreach (var part in new[] { "build-tile__image", "build-tile__bar", "build-tile__dash" })
                {
                    var child = new VisualElement { pickingMode = PickingMode.Ignore };
                    child.AddToClassList(part);
                    tile.Add(child);
                }
                _buildTiles.Add(tile);
            }
        }

        void Select(int index, bool focus = true)
        {
            if (!IsOpen || _closing) return;
            _selected = (index + _rows.Count) % _rows.Count;
            var row = _rows[_selected];
            for (int i = 0; i < _rows.Count; i++) _rows[i].EnableInClassList("pause-row--selected", i == _selected);

            _glide.style.top = row.layout.y;
            _glide.EnableInClassList("pause-menu__glide--locked", row.ClassListContains("pause-row--locked"));
            _glide.AddToClassList("pause-menu__glide--on");
            if (focus) row.Focus();
        }

        void Activate(int index)
        {
            if (!IsOpen || _closing) return;
            var command = (PauseCommand)index;
            switch (command)
            {
                case PauseCommand.Resume:
                    Close();
                    break;
                case PauseCommand.QuitToTitle:
                    SetClosed();
                    onQuitToTitle?.Invoke();
                    break;
                default:
                    var row = _rows[index];
                    string item = row.Q<Label>(className: "pause-row__label").text;
                    StartCoroutine(Shake(row));
                    ShowToast($"{item} ISN'T AVAILABLE YET");
                    onLockedItem?.Invoke(item);
                    break;
            }
            CommandExecuted?.Invoke(command);
        }

        void OnNavigationMove(NavigationMoveEvent evt)
        {
            bool up = evt.direction == NavigationMoveEvent.Direction.Up;
            bool down = evt.direction == NavigationMoveEvent.Direction.Down;
            if (!IsOpen || (!up && !down)) return;

            // Handled here because the default navigation doesn't wrap at the ends of the menu.
            evt.StopImmediatePropagation();
            _pause.focusController?.IgnoreEvent(evt);
            Select(_selected + (down ? 1 : -1));
        }

        void OnKeyDown(KeyDownEvent evt)
        {
            // The default UI input map only submits on Enter.
            if (evt.keyCode != KeyCode.Space || !IsOpen || _selected < 0) return;
            evt.StopImmediatePropagation();
            Activate(_selected);
        }

        void RenderBuild()
        {
            if (_pause == null || inventory == null) return;

            int elite = 0, classified = 0;
            foreach (var mod in inventory.All)
            {
                if (mod.Rarity == ModRarity.Elite) elite++;
                else if (mod.Rarity == ModRarity.Classified) classified++;
            }

            for (int i = 0; i < ModInventory.SlotCount; i++)
            {
                var mod = inventory.GetEquipped((ModSlot)i);
                var tile = _buildTiles[i];
                tile.EnableInClassList("build-tile--empty", mod == null);
                tile[0].style.backgroundImage = mod != null ? mod.Icon : null;
                tile[1].style.backgroundColor = mod != null ? RarityColors[Mathf.Clamp(mod.Tier, 0, 3)] : Color.clear;
            }

            _countLinked.text = $"{inventory.EquippedCount}/{ModInventory.SlotCount}";
            _countStorage.text = $"{inventory.StoredCount}/{inventory.Capacity}";
            _countElite.text = elite.ToString();
            _countClassified.text = classified.ToString();
        }

        void ShowToast(string message)
        {
            _toastMessage.text = message;
            _toast.AddToClassList("toast--on");
            if (_toastHide != null) StopCoroutine(_toastHide);
            _toastHide = StartCoroutine(HideToast());
        }

        IEnumerator HideToast()
        {
            yield return new WaitForSecondsRealtime(ToastSeconds);
            _toast.RemoveFromClassList("toast--on");
            _toastHide = null;
        }

        static IEnumerator Shake(VisualElement element)
        {
            var wait = new WaitForSecondsRealtime(0.06f);
            foreach (float x in new[] { -6f, 5f, -2f, 0f })
            {
                element.style.translate = new Translate(x, 0);
                yield return wait;
            }
            element.style.translate = StyleKeyword.Null;
        }

        static bool BackPressed()
        {
#if JUNKLITE_INPUT_SYSTEM
            var keyboard = Keyboard.current;
            if (keyboard != null && keyboard.escapeKey.wasPressedThisFrame) return true;
            var gamepad = Gamepad.current;
            return gamepad != null && gamepad.startButton.wasPressedThisFrame;
#elif ENABLE_LEGACY_INPUT_MANAGER
            return Input.GetKeyDown(KeyCode.Escape);
#else
            return false;
#endif
        }
    }
}
