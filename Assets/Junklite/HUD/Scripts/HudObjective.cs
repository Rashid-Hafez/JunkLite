using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UIElements;

namespace Junklite.HUD
{
    [Serializable]
    public class DirectiveStep
    {
        public string label;

        [Min(1)] public int need = 1;

        [Tooltip("Show the button this step asks for, hot, until the step is done.")]
        public bool showPrompt;

        public HudAction prompt = HudAction.ModState;

        public DirectiveStep() { }

        public DirectiveStep(string label, int need = 1, HudAction? prompt = null)
        {
            this.label = label;
            this.need = need;
            showPrompt = prompt.HasValue;
            if (prompt.HasValue) this.prompt = prompt.Value;
        }
    }

    [Serializable]
    public class Directive
    {
        public string level = "LVL 0 / C.M.A. TRAINING";
        public string title;
        [TextArea(2, 4)] public string brief;
        public List<DirectiveStep> steps = new List<DirectiveStep>();

        public Directive() { }

        public Directive(string level, string title, string brief, params DirectiveStep[] steps)
        {
            this.level = level;
            this.title = title;
            this.brief = brief;
            this.steps = new List<DirectiveStep>(steps);
        }
    }

    /// <summary>
    /// The objective card (HUD/objective). A directive from the C.M.A. slides in with a torn, scrambled title and
    /// a yellow NEW DIRECTIVE chip that settles after a few seconds. Steps tick one by one, and finishing the last
    /// completes the directive by itself: a cyan COMPLETE chip, then the next directive slides in.
    /// With a pop-up time set (HudLayout does this), the card opens in full when it changes and folds to its title after.
    /// </summary>
    [RequireComponent(typeof(UIDocument))]
    public class HudObjective : MonoBehaviour
    {
        // A long title shrinks to fit the card on one line, down to this share of its size; past that it wraps.
        const float TitleMinScale = 0.7f;

        const float SettleSeconds = 2.6f;
        const float CompleteHoldSeconds = 1.5f;
        const float SlideOutSeconds = 0.36f;

        [SerializeField] List<Directive> directives = DefaultDirectives();

        [Tooltip("The directive shown first (0 = the first in the list).")]
        [Min(0)] [SerializeField] int startIndex;

        [SerializeField] bool showOnStart = true;
        [SerializeField] float startDelay = 0.3f;

        [Tooltip("After the last directive, start again from the first. Off: the card slides out and stays hidden.")]
        [SerializeField] bool loop;

        [Tooltip("After completing a directive, show the next one in the list. Off when the game decides what comes next: the card slides out until Show is called.")]
        [SerializeField] bool advanceAfterComplete = true;

        [Tooltip("Passes the directive's title.")]
        [SerializeField] UnityEvent<string> onDirectiveShown = new UnityEvent<string>();

        [Tooltip("Passes the directive's title.")]
        [SerializeField] UnityEvent<string> onDirectiveCompleted = new UnityEvent<string>();

        VisualElement _root;
        VisualElement _bound;
        VisualElement _head;
        VisualElement _title;
        VisualElement _steps;
        VisualElement _fill;
        Label _chip;
        Label _level;
        Label _count;
        Label _titleText;
        bool _fitTitle;
        Label _brief;
        readonly List<VisualElement> _stepRows = new List<VisualElement>();

        int _index;
        int[] _done = Array.Empty<int>();
        bool _busy;
        bool _shown;
        float _autoCompact;
        HudFx.Timer _settle;
        HudFx.Timer _fold;
        HudFx.Timer _autoComplete;
        Coroutine _routine;

        public event Action<Directive> DirectiveShown;
        public event Action<Directive> DirectiveCompleted;

        public IReadOnlyList<Directive> Directives => directives;
        public int Index => _index;
        public Directive Current => _index >= 0 && _index < directives.Count ? directives[_index] : null;
        public string Title => Current?.title;
        public bool IsShown => _shown;
        public bool IsBusy => _busy;
        public bool IsCompact => _root != null && _root.ClassListContains("ob--compact");
        public bool AdvanceAfterComplete { get => advanceAfterComplete; set => advanceAfterComplete = value; }
        public VisualElement Root => _root;

        void OnEnable()
        {
            var doc = GetComponent<UIDocument>().rootVisualElement;
            _root = doc?.Q("ob");
            if (_root == null)
            {
                Debug.LogError("Objective UI is missing. Check that the UI Document's Source Asset is Hud.uxml.", this);
                return;
            }

            if (_root != _bound)
            {
                _bound = _root;
                _head = _root.Q("ob-head");
                _title = _root.Q("ob-title");
                _steps = _root.Q("ob-steps");
                _fill = _root.Q("ob-fill");
                _chip = _root.Q<Label>("ob-chip");
                _level = _root.Q<Label>("ob-level");
                _count = _root.Q<Label>("ob-count");
                _titleText = _title.Q<Label>(className: "chroma__text");
                _titleText.RegisterCallback<GeometryChangedEvent>(_ => FitTitle());
                _brief = _root.Q<Label>("ob-brief");
                _root.Query<Label>().ForEach(l => l.enableRichText = false);
            }

            _busy = false;
            _shown = false;
            _root.RemoveFromClassList("ob--shown");
            _index = directives.Count > 0 ? Mathf.Clamp(startIndex, 0, directives.Count - 1) : 0;
            if (directives.Count > 0) Render();
            if (showOnStart && directives.Count > 0) HudFx.After(startDelay, () => { if (isActiveAndEnabled) Arrive(true); });
        }

        void OnDisable()
        {
            _settle?.Cancel();
            _fold?.Cancel();
            _autoComplete?.Cancel();
            _routine = null;
            _busy = false;
        }

        // ---------- public calls ----------

        /// <summary>Replaces the directive list and shows <paramref name="index"/>.</summary>
        public void SetDirectives(IEnumerable<Directive> list, int index = 0)
        {
            directives = new List<Directive>(list);
            if (directives.Count == 0)
            {
                Hide();
                return;
            }
            Show(index);
        }

        /// <summary>Replaces the directive list without showing one, and hides the card. Show picks one.</summary>
        public void LoadDirectives(IEnumerable<Directive> list)
        {
            StopRoutine();
            _settle?.Cancel();
            _autoComplete?.Cancel();
            _busy = false;
            directives = new List<Directive>(list);
            _index = 0;
            Hide();
        }

        /// <summary>Shows a directive, sliding it in with the NEW DIRECTIVE chip.</summary>
        public void Show(int index)
        {
            if (_root == null || directives.Count == 0) return;
            StopRoutine();
            _busy = false;
            _index = Mathf.Clamp(index, 0, directives.Count - 1);
            Arrive(true);
        }

        /// <summary>Ticks the next step. Finishing the last step completes the directive.</summary>
        public void Progress()
        {
            if (_busy || !_shown || Current == null) return;
            var steps = Current.steps;
            int i = -1;
            for (int k = 0; k < steps.Count; k++)
            {
                if (_done[k] < steps[k].need)
                {
                    i = k;
                    break;
                }
            }
            if (i < 0)
            {
                Complete();
                return;
            }

            PopOpen();
            var row = _stepRows[i];
            _done[i]++;
            HudFx.SetText(row.Q<Label>(className: "ob-step__value"), $"{_done[i]} / {steps[i].need}");
            HudFx.Tear(new[] { row }, new TearOptions { frames = 3, amp = 10, chance = 1f, hot = 0.6f, cold = 0f });

            if (_done[i] >= steps[i].need)
            {
                var prompt = row.Q<KeyPrompt>();
                if (prompt != null)
                {
                    prompt.Press();
                    prompt.Hot = false;
                }
                row.AddToClassList("ob-step--done");
                row.AddToClassList("ob-step--flash");
                HudFx.NextFrames(() => row.RemoveFromClassList("ob-step--flash"));
            }

            UpdateProgress();
            if (AllDone()) _autoComplete = HudFx.After(0.65f, Complete);
        }

        /// <summary>Completes the directive: cyan chip, dimmed title, then the next one slides in.</summary>
        public void Complete()
        {
            if (_busy || !_shown || Current == null) return;
            StopRoutine();
            _routine = StartCoroutine(CompleteRoutine());
        }

        /// <summary>The same directive, changed: it glitches in again with the NEW DIRECTIVE chip.</summary>
        public void Refresh()
        {
            if (_busy || !_shown) return;
            Arrive(true);
        }

        /// <summary>Folds the brief and steps away (true), opens them (false), or flips them (null).</summary>
        public void SetCompact(bool? compact = null)
        {
            if (_root == null) return;
            Fold(compact ?? !IsCompact);
            HudFx.Tear(Rows(), new TearOptions { frames = 3, amp = 10, hot = 0f, cold = 0.3f });
        }

        /// <summary>Seconds after which the card folds to its title; 0 keeps it open. HudLayout sets this.</summary>
        public void SetAutoCompact(float seconds)
        {
            _autoCompact = Mathf.Max(0f, seconds);
            if (_root == null) return;
            if (_autoCompact > 0f) ArmFold();
            else
            {
                _fold?.Cancel();
                Fold(false);
            }
        }

        /// <summary>For cutscenes: the card slides out, then back in with a short tear.</summary>
        public void Toggle()
        {
            if (_busy || _root == null) return;
            if (_shown) Hide();
            else Unhide();
        }

        public void Hide()
        {
            if (_root == null) return;
            _shown = false;
            _root.RemoveFromClassList("ob--shown");
        }

        public void Unhide()
        {
            if (_root == null || Current == null) return;
            _shown = true;
            _root.AddToClassList("ob--shown");
            HudFx.Tear(Rows(), new TearOptions { frames = 4, amp = 18 });
        }

        // ---------- internals ----------

        IEnumerable<VisualElement> Rows()
        {
            yield return _head;
            yield return _title;
            yield return _brief;
            foreach (var row in _stepRows) yield return row;
        }

        void Fold(bool compact)
        {
            if (_root.ClassListContains("ob--compact") == compact) return;
            _root.EnableInClassList("ob--compact", compact);
            QueueTitleFit();   // the folded title is smaller, so it fits again from its own size
        }

        // Back to the USS size; FitTitle runs once the new size is laid out.
        void QueueTitleFit()
        {
            _fitTitle = true;
            foreach (var label in _title.Query<Label>().ToList())
            {
                label.style.fontSize = StyleKeyword.Null;
                label.style.height = StyleKeyword.Null;
                label.style.width = StyleKeyword.Null;
                label.style.whiteSpace = StyleKeyword.Null;
            }
        }

        // A title wider than the card shrinks to fit on one line; below TitleMinScale it wraps instead,
        // and the card grows to hold the second line. The two chroma ghosts follow the text.
        void FitTitle()
        {
            if (!_fitTitle || _titleText == null) return;
            float room = _title.contentRect.width;
            float size = _titleText.resolvedStyle.fontSize;
            float height = _titleText.resolvedStyle.height;
            if (room <= 0f || size <= 0f) return;
            _fitTitle = false;

            float width = _titleText.MeasureTextSize(_titleText.text, 0f, VisualElement.MeasureMode.Undefined, 0f, VisualElement.MeasureMode.Undefined).x;
            if (width <= room) return;

            float fitted = Mathf.Floor(size * room / width * 0.98f);
            bool wrap = fitted < size * TitleMinScale;
            if (wrap) fitted = Mathf.Floor(size * TitleMinScale);
            foreach (var label in _title.Query<Label>().ToList())
            {
                label.style.fontSize = fitted;
                if (wrap)
                {
                    label.style.whiteSpace = WhiteSpace.Normal;
                    label.style.width = room;
                    label.style.height = StyleKeyword.Auto;
                }
                else
                {
                    label.style.height = Mathf.Round(height * fitted / size);
                }
            }
        }

        void ArmFold()
        {
            _fold?.Cancel();
            if (_autoCompact <= 0f) return;
            _fold = HudFx.After(_autoCompact, () =>
            {
                if (_root == null) return;
                Fold(true);
                HudFx.Tear(Rows(), new TearOptions { frames = 3, amp = 8, hot = 0f, cold = 0.3f, quiet = true });
            });
        }

        void PopOpen()
        {
            if (_autoCompact > 0f && IsCompact) Fold(false);
            ArmFold();
        }

        void SetChip(string text, string modifier, bool scramble)
        {
            _chip.RemoveFromClassList("chip--hot");
            _chip.RemoveFromClassList("chip--action");
            _chip.RemoveFromClassList("chip--alert");
            if (modifier != null) _chip.AddToClassList(modifier);
            if (scramble) HudFx.Scramble(_chip, 240, text);
            else HudFx.SetText(_chip, text);
        }

        bool AllDone()
        {
            var steps = Current.steps;
            for (int k = 0; k < steps.Count; k++)
            {
                if (_done[k] < steps[k].need) return false;
            }
            return true;
        }

        void UpdateProgress()
        {
            int need = 0, have = 0;
            var steps = Current.steps;
            for (int k = 0; k < steps.Count; k++)
            {
                need += steps[k].need;
                have += Mathf.Min(_done[k], steps[k].need);
            }
            _fill.style.width = Length.Percent(need > 0 ? have * 100f / need : 0f);
        }

        void Render()
        {
            var d = Current;
            _done = new int[d.steps.Count];
            _root.RemoveFromClassList("ob--done");
            HudFx.SetText(_level, d.level);
            HudFx.SetText(_count, $"{HudFx.Pad(_index + 1)} / {HudFx.Pad(directives.Count)}");
            HudFx.SetChroma(_title, d.title);
            QueueTitleFit();
            HudFx.SetText(_brief, d.brief);
            _brief.EnableInClassList("ob-brief--none", string.IsNullOrEmpty(d.brief));

            _steps.Clear();
            _stepRows.Clear();
            foreach (var step in d.steps)
            {
                var row = new VisualElement { pickingMode = PickingMode.Ignore };
                row.AddToClassList("ob-step");
                row.AddToClassList("tear");

                var box = new VisualElement { pickingMode = PickingMode.Ignore };
                box.AddToClassList("ob-step__box");
                var fill = new VisualElement { pickingMode = PickingMode.Ignore };
                fill.AddToClassList("ob-step__fill");
                box.Add(fill);
                row.Add(box);

                row.Add(MakeLabel("ob-step__label", step.label));
                if (step.showPrompt)
                {
                    var prompt = new KeyPrompt(step.prompt, PromptSize.Small, true);
                    prompt.AddToClassList("ob-step__kp");
                    row.Add(prompt);
                }
                row.Add(MakeLabel("ob-step__value", $"0 / {step.need}"));

                _steps.Add(row);
                _stepRows.Add(row);
            }
            _steps.EnableInClassList("ob-steps--none", d.steps.Count == 0);
            UpdateProgress();
        }

        static Label MakeLabel(string className, string text)
        {
            var label = new Label(text) { pickingMode = PickingMode.Ignore, enableRichText = false };
            label.AddToClassList(className);
            return label;
        }

        void Arrive(bool fresh)
        {
            StopRoutine();
            _routine = StartCoroutine(ArriveRoutine(fresh));
        }

        IEnumerator ArriveRoutine(bool fresh)
        {
            _settle?.Cancel();
            _autoComplete?.Cancel();
            Render();
            if (_autoCompact > 0f) Fold(false);
            SetChip(fresh ? "NEW DIRECTIVE" : $"DIRECTIVE {HudFx.Pad(_index + 1)}", fresh ? "chip--hot" : null, false);
            _root.AddToClassList("ob--shown");
            _shown = true;
            var shown = Current;
            DirectiveShown?.Invoke(shown);
            onDirectiveShown?.Invoke(shown.title);

            yield return new WaitForSecondsRealtime(0.09f);
            HudFx.Scramble(_titleText, 380);
            HudFx.Chroma(_title, 460, 7f);

            bool torn = false;
            HudFx.Tear(Rows(), new TearOptions { frames = 7, amp = 30, chance = 0.6f }, () => torn = true);
            while (!torn) yield return null;

            // a step that needs a button shows it; ping it twice so it is noticed
            foreach (var prompt in _steps.Query<KeyPrompt>().ToList())
            {
                prompt.Ping();
                HudFx.After(0.9f, prompt.Ping);
            }

            if (fresh)
            {
                int index = _index;
                _settle = HudFx.After(SettleSeconds, () =>
                {
                    if (_index == index) SetChip($"DIRECTIVE {HudFx.Pad(index + 1)}", null, true);
                });
            }
            ArmFold();
            _routine = null;
        }

        IEnumerator CompleteRoutine()
        {
            _busy = true;
            _settle?.Cancel();
            _fold?.Cancel();
            _autoComplete?.Cancel();
            if (_autoCompact > 0f) Fold(false);

            var finished = Current;
            for (int k = 0; k < finished.steps.Count; k++)
            {
                _done[k] = finished.steps[k].need;
                var row = _stepRows[k];
                row.AddToClassList("ob-step--done");
                HudFx.SetText(row.Q<Label>(className: "ob-step__value"), $"{finished.steps[k].need} / {finished.steps[k].need}");
            }
            _fill.style.width = Length.Percent(100f);
            _root.AddToClassList("ob--done");
            SetChip("COMPLETE", "chip--action", true);
            HudFx.Tear(Rows(), new TearOptions { frames = 4, amp = 12, hot = 0f, cold = 0.5f });
            DirectiveCompleted?.Invoke(finished);
            onDirectiveCompleted?.Invoke(finished.title);

            yield return new WaitForSecondsRealtime(CompleteHoldSeconds);
            _root.RemoveFromClassList("ob--shown");
            yield return new WaitForSecondsRealtime(SlideOutSeconds);

            bool last = _index >= directives.Count - 1;
            if ((last && !loop) || !advanceAfterComplete)
            {
                _shown = false;
                _busy = false;
                _routine = null;
                yield break;
            }

            _index = last ? 0 : _index + 1;
            _busy = false;
            _routine = null;
            Arrive(true);
        }

        void StopRoutine()
        {
            if (_routine != null) StopCoroutine(_routine);
            _routine = null;
        }

        // The build's own objective, word for word. Add the game's other directives in the Inspector or with SetDirectives.
        static List<Directive> DefaultDirectives() => new List<Directive>
        {
            new Directive("LVL 0 / C.M.A. TRAINING", "PATCH UP", "Take the health pack from The KRF Porta Box."),
        };
    }
}
