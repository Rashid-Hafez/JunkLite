using System;
using System.Collections;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UIElements;

namespace Junklite.TitleScreen
{
    /// <summary>
    /// The title screen's menu (Title screen/junklite-title). A pressed command shows what it's doing on the status line
    /// under the menu, fills its bar, then runs (CommandExecuted; MainMenuController loads the level).
    /// Demo Menu names the buttons after where they go (TUTORIAL, SERVICE FLOOR, TRAINING) and hides CALIBRATE, which
    /// has no screen yet. Turn it off for the story names (RESUME SUBJECT, BEGIN PROCEDURE, CALIBRATE ...).
    /// </summary>
    [RequireComponent(typeof(UIDocument))]
    public class TitleScreenController : MonoBehaviour
    {
        const float PowerCollapseSeconds = 0.231f;
        const float PowerOutSeconds = 0.209f;
        const float FirstSelectDelay = 0.52f;
        const float LaunchSeconds = 0.9f;     // the status bar's fill (TitleScreen.uss .status--run)
        const float DoneHoldSeconds = 2.2f;   // the status line stays this long when nothing loads

        const string SelectedClass = "menu-item--selected";
        const string HiddenClass = "hidden";
        const string ScreenHiddenClass = "title-screen--hidden";
        const string PowerCollapseClass = "title-screen--power-collapse";
        const string PowerOutClass = "title-screen--power-out";
        const string StatusRunClass = "status--run";
        const string StatusDoneClass = "status--done";

        static readonly string[] ButtonNames = { "menu-resume", "menu-begin", "menu-rooftop", "menu-training", "menu-calibrate", "menu-terminate" };

        // By TitleCommand: the button's name and the dim hint shown when it's selected.
        // The story names, as designed for the full game.
        static readonly string[] StoryLabels = { "RESUME SUBJECT", "BEGIN PROCEDURE", "ROOFTOP", "TRAINING", "CALIBRATE", "TERMINATE SESSION" };
        static readonly string[] StoryHints = { "CONTINUE", "NEW GAME", "LEVEL", "PRACTICE", "SETTINGS", "QUIT" };
        // The demo: each level button named after where it goes (MainMenuController: Begin loads the tutorial
        // simulation, Resume Service Floor, Training the SANDBOX "Training Level").
        static readonly string[] DemoLabels = { "SERVICE FLOOR", "TUTORIAL", "ROOFTOP", "TRAINING", "CALIBRATE", "TERMINATE SESSION" };
        static readonly string[] DemoHints = StoryHints;

        // Top to bottom.
        static readonly TitleCommand[] StoryOrder = { TitleCommand.Resume, TitleCommand.Begin, TitleCommand.Rooftop, TitleCommand.Training, TitleCommand.Calibrate, TitleCommand.Terminate };
        static readonly TitleCommand[] DemoOrder = { TitleCommand.Begin, TitleCommand.Resume, TitleCommand.Rooftop, TitleCommand.Training, TitleCommand.Calibrate, TitleCommand.Terminate };

        // The status line: what the command is doing, then what's ready (the prototype's words).
        const string Initiating = "INITIATING PROCEDURE";
        const string Ready = " // READY";

        [Tooltip("On: the buttons are named after where they go (TUTORIAL, SERVICE FLOOR, TRAINING) and CALIBRATE is hidden. Off: the story names.")]
        [SerializeField] bool demoMenu = true;

        [SerializeField] UnityEvent onResume = new UnityEvent();
        [SerializeField] UnityEvent onBegin = new UnityEvent();
        [SerializeField] UnityEvent onRooftop = new UnityEvent();
        [SerializeField] UnityEvent onTraining = new UnityEvent();
        [SerializeField] UnityEvent onCalibrate = new UnityEvent();
        [SerializeField] UnityEvent onTerminate = new UnityEvent();

        [Tooltip("Off shows the RECONNECT screen after TERMINATE SESSION. On quits a built game instead.")]
        [SerializeField] bool quitOnTerminate;

        VisualElement _panelRoot;
        VisualElement _screen;
        VisualElement _menu;
        VisualElement _status;
        Label _statusText;
        Button _reconnect;
        Button[] _buttons;
        TitleCommand[] _order;
        readonly bool[] _hidden = new bool[ButtonNames.Length];
        readonly bool[] _hiddenByGame = new bool[ButtonNames.Length];
        TitleScreenEffects _effects;
        int _selected = -1;
        bool _poweringOff;
        bool _busy;

        public event Action<TitleCommand> CommandExecuted;

        public TitleCommand? Selected => _selected >= 0 ? (TitleCommand)_selected : (TitleCommand?)null;
        public bool IsOffline { get; private set; }
        public bool DemoMenu => demoMenu;

        void OnEnable()
        {
            // UIDocument rebuilds its tree and re-enables this component whenever the UXML is
            // reimported, so everything is looked up again here rather than once in Awake.
            var root = GetComponent<UIDocument>().rootVisualElement;
            _effects = GetComponent<TitleScreenEffects>();
            _screen = root?.Q("screen");
            _menu = root?.Q("menu");
            _status = root?.Q("status");
            _statusText = root?.Q<Label>("status-text");
            _reconnect = root?.Q<Button>("reconnect");
            if (_screen == null || _menu == null || _status == null || _reconnect == null || root.panel == null)
            {
                Debug.LogError("Title screen UI is missing. Check that the UI Document's Source Asset is TitleScreen.uxml.", this);
                return;
            }

            // In the Game view the root can come out zero height, which pushes the top half off screen.
            root.StretchToParentSize();
            _reconnect.clicked += Reconnect;

            _buttons = new Button[ButtonNames.Length];
            for (int i = 0; i < ButtonNames.Length; i++)
            {
                int index = i;
                _buttons[i] = root.Q<Button>(ButtonNames[i]);
                _buttons[i].clicked += () => Run(index);
                _buttons[i].RegisterCallback<PointerEnterEvent>(evt =>
                {
                    if (evt.pointerType == UnityEngine.UIElements.PointerType.mouse) Select(index);
                });
                _buttons[i].RegisterCallback<FocusInEvent>(_ =>
                {
                    if (_selected != index) Select(index, false);
                });
            }
            ApplyMenu();

            _panelRoot = root.panel.visualTree;
            _panelRoot.RegisterCallback<NavigationMoveEvent>(OnNavigationMove, TrickleDown.TrickleDown);
            _panelRoot.RegisterCallback<NavigationSubmitEvent>(OnNavigationSubmit, TrickleDown.TrickleDown);
            _panelRoot.RegisterCallback<KeyDownEvent>(OnKeyDown, TrickleDown.TrickleDown);

            StartScreen();
        }

        void OnDisable()
        {
            StopAllCoroutines();
            if (_panelRoot == null) return;
            _panelRoot.UnregisterCallback<NavigationMoveEvent>(OnNavigationMove, TrickleDown.TrickleDown);
            _panelRoot.UnregisterCallback<NavigationSubmitEvent>(OnNavigationSubmit, TrickleDown.TrickleDown);
            _panelRoot.UnregisterCallback<KeyDownEvent>(OnKeyDown, TrickleDown.TrickleDown);
            _panelRoot = null;
        }

        void OnValidate()
        {
            if (Application.isPlaying && isActiveAndEnabled && _buttons != null) ApplyMenu();
        }

        // The labels, the order and which commands show, for the demo or the story menu.
        void ApplyMenu()
        {
            var labels = demoMenu ? DemoLabels : StoryLabels;
            var hints = demoMenu ? DemoHints : StoryHints;
            _order = demoMenu ? DemoOrder : StoryOrder;
            for (int i = 0; i < _buttons.Length; i++)
            {
                _buttons[i].Q<Label>(className: "menu-item__label").text = labels[i];
                _buttons[i].Q<Label>(className: "menu-item__hint").text = hints[i];
            }
            foreach (var command in _order) _menu.Add(_buttons[(int)command]);   // re-adding moves it to the end
            Renumber();
        }

        void StartScreen()
        {
            StopAllCoroutines();
            _selected = -1;
            _poweringOff = false;
            _busy = false;
            IsOffline = false;

            foreach (var button in _buttons) button.RemoveFromClassList(SelectedClass);
            SetStatus(null, string.Empty);
            _screen.RemoveFromClassList(PowerCollapseClass);
            _screen.RemoveFromClassList(PowerOutClass);
            _screen.AddToClassList(ScreenHiddenClass);
            _reconnect.AddToClassList(HiddenClass);

            StartCoroutine(FadeIn());
            if (_effects != null && _effects.enabled) _effects.Restart();
        }

        IEnumerator FadeIn()
        {
            // The transition needs one laid-out frame to start from.
            yield return null;
            yield return null;
            _screen.RemoveFromClassList(ScreenHiddenClass);

            yield return new WaitForSecondsRealtime(FirstSelectDelay);
            if (_selected < 0) Select((int)_order[0]);
        }

        /// <summary>Hides a menu item (e.g. a level left out of the build). Navigation skips it and the visible items renumber.</summary>
        public void SetCommandVisible(TitleCommand command, bool visible)
        {
            if (_buttons == null) return;
            _hiddenByGame[(int)command] = !visible;
            Renumber();
            if (_selected >= 0 && IsHidden(_selected)) Select(_selected, true, 1);
        }

        void Renumber()
        {
            int number = 1;
            foreach (var command in _order)
            {
                int i = (int)command;
                // CALIBRATE has no settings screen yet, so the demo leaves it out.
                _hidden[i] = _hiddenByGame[i] || (demoMenu && command == TitleCommand.Calibrate);
                _buttons[i].style.display = _hidden[i] ? DisplayStyle.None : DisplayStyle.Flex;
                if (_hidden[i]) continue;
                var index = _buttons[i].Q<Label>(className: "menu-item__index");
                if (index != null) index.text = (number++).ToString("00");
            }
        }

        bool IsHidden(int i) => _hidden[i];

        // index: a TitleCommand. step: which way to look when it's hidden (along the menu, top to bottom).
        void Select(int index, bool focus = true, int step = 1)
        {
            if (IsOffline || _poweringOff) return;

            int count = _order.Length;
            int position = Array.IndexOf(_order, (TitleCommand)index);
            for (int tries = 0; tries < count && IsHidden((int)_order[Wrap(position, count)]); tries++)
                position += step;
            _selected = (int)_order[Wrap(position, count)];
            for (int i = 0; i < _buttons.Length; i++)
            {
                _buttons[i].EnableInClassList(SelectedClass, i == _selected);
            }
            if (focus) _buttons[_selected].Focus();
        }

        static int Wrap(int i, int count) => ((i % count) + count) % count;

        void Move(int step)
        {
            int position = _selected >= 0 ? Array.IndexOf(_order, (TitleCommand)_selected) : -step;
            Select((int)_order[Wrap(position + step, _order.Length)], true, step);
        }

        void OnNavigationMove(NavigationMoveEvent evt)
        {
            bool up = evt.direction == NavigationMoveEvent.Direction.Up;
            bool down = evt.direction == NavigationMoveEvent.Direction.Down;
            if (!up && !down) return;

            // Handled here because the default navigation doesn't wrap at the ends of the menu.
            evt.StopImmediatePropagation();
            _panelRoot.focusController?.IgnoreEvent(evt);
            Move(down ? 1 : -1);
        }

        void OnNavigationSubmit(NavigationSubmitEvent evt)
        {
            // A focused button handles submit itself. This covers focus lost by clicking empty space.
            if (_panelRoot.focusController?.focusedElement is Button) return;
            evt.StopImmediatePropagation();
            ActivateCurrent();
        }

        void OnKeyDown(KeyDownEvent evt)
        {
            // The default UI input map only submits on Enter.
            if (evt.keyCode != KeyCode.Space) return;
            evt.StopImmediatePropagation();
            ActivateCurrent();
        }

        void ActivateCurrent()
        {
            if (IsOffline) Reconnect();
            else if (_selected >= 0) Run(_selected);
        }

        void Run(int index)
        {
            if (IsOffline || _poweringOff || _busy) return;

            var command = (TitleCommand)index;
            if (command == TitleCommand.Terminate) StartCoroutine(PowerOff());
            else StartCoroutine(Launch(command));
        }

        // The status line runs its bar, says what's ready, then the command runs. If nothing loads (CALIBRATE has no
        // screen yet) the line clears after a while and the menu takes presses again.
        IEnumerator Launch(TitleCommand command)
        {
            _busy = true;
            SetStatus(StatusRunClass, "> " + RunText(command));
            yield return new WaitForSecondsRealtime(LaunchSeconds);
            SetStatus(StatusDoneClass, DoneText(command) + Ready);
            Execute(command);

            yield return new WaitForSecondsRealtime(DoneHoldSeconds);
            SetStatus(null, string.Empty);
            _busy = false;
        }

        // The prototype's words for the story menu; the demo starts every level the same way.
        string RunText(TitleCommand command)
        {
            if (demoMenu) return Initiating;
            switch (command)
            {
                case TitleCommand.Resume: return "RESTORING SUBJECT STATE";
                case TitleCommand.Calibrate: return "OPENING CALIBRATION";
                default: return Initiating;
            }
        }

        // What's ready: the tutorial is LVL 0's C.M.A. training simulation; the demo names the other levels.
        string DoneText(TitleCommand command)
        {
            if (command == TitleCommand.Begin) return "LVL 0 — C.M.A. TRAINING SIMULATION";
            if (!demoMenu)
            {
                switch (command)
                {
                    case TitleCommand.Resume: return "LVL 1 — THE RAID ON KRAISNER";
                    case TitleCommand.Calibrate: return "AUDIO · VIDEO · INPUT";
                }
            }
            return DemoLabels[(int)command];
        }

        void SetStatus(string state, string text)
        {
            _status.RemoveFromClassList(StatusRunClass);
            _status.RemoveFromClassList(StatusDoneClass);
            if (state != null) _status.AddToClassList(state);
            _statusText.text = text;
        }

        IEnumerator PowerOff()
        {
            _poweringOff = true;

            _screen.AddToClassList(PowerCollapseClass);
            yield return new WaitForSecondsRealtime(PowerCollapseSeconds);
            _screen.RemoveFromClassList(PowerCollapseClass);
            _screen.AddToClassList(PowerOutClass);
            yield return new WaitForSecondsRealtime(PowerOutSeconds);

            IsOffline = true;
            _reconnect.RemoveFromClassList(HiddenClass);
            _reconnect.Focus();
            Execute(TitleCommand.Terminate);

            if (quitOnTerminate && !Application.isEditor) Application.Quit();
        }

        void Reconnect()
        {
            if (IsOffline) StartScreen();
        }

        void Execute(TitleCommand command)
        {
            switch (command)
            {
                case TitleCommand.Resume: onResume?.Invoke(); break;
                case TitleCommand.Begin: onBegin?.Invoke(); break;
                case TitleCommand.Rooftop: onRooftop?.Invoke(); break;
                case TitleCommand.Training: onTraining?.Invoke(); break;
                case TitleCommand.Calibrate: onCalibrate?.Invoke(); break;
                case TitleCommand.Terminate: onTerminate?.Invoke(); break;
            }
            CommandExecuted?.Invoke(command);
        }
    }
}
