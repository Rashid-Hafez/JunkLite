using System;
using System.Collections;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UIElements;

namespace Junklite.TitleScreen
{
    [RequireComponent(typeof(UIDocument))]
    public class TitleScreenController : MonoBehaviour
    {
        const float PowerCollapseSeconds = 0.231f;
        const float PowerOutSeconds = 0.209f;
        const float FirstSelectDelay = 0.62f;
        const float CaretBlinkSeconds = 0.55f;

        const string SelectedClass = "menu-item--selected";
        const string CaretOffClass = "menu-item__caret--off";
        const string HiddenClass = "hidden";
        const string ScreenHiddenClass = "title-screen--hidden";
        const string PowerCollapseClass = "title-screen--power-collapse";
        const string PowerOutClass = "title-screen--power-out";

        static readonly string[] ButtonNames = { "menu-resume", "menu-begin", "menu-rooftop", "menu-calibrate", "menu-terminate" };

        [SerializeField] UnityEvent onResume = new UnityEvent();
        [SerializeField] UnityEvent onBegin = new UnityEvent();
        [SerializeField] UnityEvent onRooftop = new UnityEvent();
        [SerializeField] UnityEvent onCalibrate = new UnityEvent();
        [SerializeField] UnityEvent onTerminate = new UnityEvent();

        [Tooltip("Off shows the RECONNECT screen after TERMINATE SESSION. On quits a built game instead.")]
        [SerializeField] bool quitOnTerminate;

        VisualElement _panelRoot;
        VisualElement _screen;
        Button _reconnect;
        Button[] _buttons;
        Label[] _carets;
        TitleScreenEffects _effects;
        int _selected = -1;
        bool _poweringOff;

        public event Action<TitleCommand> CommandExecuted;

        public TitleCommand? Selected => _selected >= 0 ? (TitleCommand)_selected : (TitleCommand?)null;
        public bool IsOffline { get; private set; }

        void OnEnable()
        {
            // UIDocument rebuilds its tree and re-enables this component whenever the UXML is
            // reimported, so everything is looked up again here rather than once in Awake.
            var root = GetComponent<UIDocument>().rootVisualElement;
            _effects = GetComponent<TitleScreenEffects>();
            _screen = root?.Q("screen");
            _reconnect = root?.Q<Button>("reconnect");
            if (_screen == null || _reconnect == null || root.panel == null)
            {
                Debug.LogError("Title screen UI is missing. Check that the UI Document's Source Asset is TitleScreen.uxml.", this);
                return;
            }

            // In the Game view the root can come out zero height, which pushes the top half off screen.
            root.StretchToParentSize();
            _reconnect.clicked += Reconnect;

            _buttons = new Button[ButtonNames.Length];
            _carets = new Label[ButtonNames.Length];
            for (int i = 0; i < ButtonNames.Length; i++)
            {
                int index = i;
                _buttons[i] = root.Q<Button>(ButtonNames[i]);
                _carets[i] = _buttons[i].Q<Label>(className: "menu-item__caret");
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

        void StartScreen()
        {
            StopAllCoroutines();
            _selected = -1;
            _poweringOff = false;
            IsOffline = false;

            foreach (var button in _buttons) button.RemoveFromClassList(SelectedClass);
            _screen.RemoveFromClassList(PowerCollapseClass);
            _screen.RemoveFromClassList(PowerOutClass);
            _screen.AddToClassList(ScreenHiddenClass);
            _reconnect.AddToClassList(HiddenClass);

            StartCoroutine(FadeIn());
            StartCoroutine(BlinkCaret());
            if (_effects != null && _effects.enabled) _effects.Restart();
        }

        IEnumerator FadeIn()
        {
            // The transition needs one laid-out frame to start from.
            yield return null;
            yield return null;
            _screen.RemoveFromClassList(ScreenHiddenClass);

            yield return new WaitForSecondsRealtime(FirstSelectDelay);
            if (_selected < 0) Select(0);
        }

        IEnumerator BlinkCaret()
        {
            bool off = false;
            var wait = new WaitForSecondsRealtime(CaretBlinkSeconds);
            while (true)
            {
                yield return wait;
                off = !off;
                foreach (var caret in _carets) caret.EnableInClassList(CaretOffClass, off);
            }
        }

        void Select(int index, bool focus = true)
        {
            if (IsOffline || _poweringOff) return;

            _selected = (index + _buttons.Length) % _buttons.Length;
            for (int i = 0; i < _buttons.Length; i++)
            {
                _buttons[i].EnableInClassList(SelectedClass, i == _selected);
            }
            if (focus) _buttons[_selected].Focus();
        }

        void OnNavigationMove(NavigationMoveEvent evt)
        {
            bool up = evt.direction == NavigationMoveEvent.Direction.Up;
            bool down = evt.direction == NavigationMoveEvent.Direction.Down;
            if (!up && !down) return;

            // Handled here because the default navigation doesn't wrap at the ends of the menu.
            evt.StopImmediatePropagation();
            _panelRoot.focusController?.IgnoreEvent(evt);
            Select(_selected + (down ? 1 : -1));
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
            if (IsOffline || _poweringOff) return;

            var command = (TitleCommand)index;
            if (command == TitleCommand.Terminate) StartCoroutine(PowerOff());
            else Execute(command);
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
                case TitleCommand.Calibrate: onCalibrate?.Invoke(); break;
                case TitleCommand.Terminate: onTerminate?.Invoke(); break;
            }
            CommandExecuted?.Invoke(command);
        }
    }
}
