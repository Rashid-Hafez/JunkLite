using System;
using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace junklite
{
    /// <summary>
    /// Cassidy's optic-HUD tutorial prompt: a small bracketed box with key chips and labels.
    /// A prompt stays on screen until the caller completes or hides it, and key labels follow
    /// the active device (keyboard vs gamepad). Builds its own overlay canvas; animations use
    /// unscaled time so hit-stop and slow-mo don't stall it.
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

        private const int MaxRows = 3;

        [Header("Style")]
        [SerializeField] private string header = "// C.M.A. OPTIC";
        [SerializeField] private string completeHeader = "// SYNC OK";
        [SerializeField] private Color accent = new Color(1f, 0.42f, 0.1f, 1f);
        [SerializeField] private Color completeColor = new Color(0.25f, 0.85f, 0.78f, 1f);
        [SerializeField] private Color panelColor = new Color(0.03f, 0.045f, 0.05f, 0.82f);
        [SerializeField] private Color labelColor = new Color(0.88f, 0.92f, 0.92f, 1f);
        [SerializeField] private Color keyTextColor = new Color(0.04f, 0.05f, 0.06f, 1f);
        [SerializeField] private float topOffset = 280f; // below the level-up toast
        [Tooltip("Top offset used while a full-screen menu (inventory) is open, so the prompt clears its tabs.")]
        [SerializeField] private float menuTopOffset = 6f;
        [SerializeField] private int sortingOrder = 60;

        [Header("Timing")]
        [SerializeField] private float showDuration = 0.22f;
        [SerializeField] private float completeHold = 0.45f;
        [SerializeField] private float hideDuration = 0.2f;

        private readonly List<Row> rows = new List<Row>(MaxRows);
        private readonly List<Entry> current = new List<Entry>(MaxRows);

        private CanvasGroup group;
        private RectTransform panel;
        private TextMeshProUGUI headerText;
        private Image[] brackets;
        private RectTransform scanline;
        private Coroutine animRoutine;
        private GameInputManager input;
        private bool built;

        public bool IsShowing { get; private set; }

        private sealed class Row
        {
            public GameObject root;
            public Image keyBg;
            public TextMeshProUGUI key;
            public TextMeshProUGUI suffix;
            public TextMeshProUGUI label;
        }

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
            if (this == interactPrompt && interactOwner != null && GameInputManager.Instance != null &&
                GameInputManager.Instance.IsInteractBlocked)
            {
                interactOwner = null;
                Hide(instant: true);
            }

            if (!IsShowing || scanline == null) return;

            // Slow scanline sweep and bracket pulse; cheap per-frame work on a few graphics.
            float t = Time.unscaledTime;
            float h = panel.rect.height;
            scanline.anchoredPosition = new Vector2(0f, -Mathf.Repeat(t * 40f, Mathf.Max(1f, h)));

            if (animRoutine == null)
            {
                float a = 0.65f + 0.35f * Mathf.Sin(t * 4f);
                for (int i = 0; i < brackets.Length; i++)
                {
                    Color c = brackets[i].color;
                    c.a = a;
                    brackets[i].color = c;
                }
            }
        }

        #endregion

        #region Public API

        private static TutorialPromptHUD interactPrompt;
        private static object interactOwner;

        /// <summary>
        /// The one on-screen prompt every world interactable shares ("[E] Pick up", "[LB] Talk"...).
        /// The last caller owns it; HideInteract from a stale owner is ignored.
        /// </summary>
        public static void ShowInteract(object owner, string label)
        {
            // No interact prompt while the interact input is blocked (player in combat).
            if (GameInputManager.Instance != null && GameInputManager.Instance.IsInteractBlocked) return;
            if (interactPrompt == null)
            {
                interactPrompt = new GameObject("InteractPrompt").AddComponent<TutorialPromptHUD>();
                interactPrompt.topOffset = 420f; // below a tutorial prompt shown at the same time
            }
            interactOwner = owner;
            interactPrompt.Show(new Entry("Player/Interact", label));
        }

        public static void HideInteract(object owner, bool completed = false, bool instant = false)
        {
            if (interactPrompt == null || !ReferenceEquals(interactOwner, owner)) return;
            interactOwner = null;
            if (completed) interactPrompt.Complete();
            else interactPrompt.Hide(instant);
        }

        public void Show(params Entry[] entries)
        {
            Build();
            SubscribeInput();

            current.Clear();
            int count = Mathf.Min(entries?.Length ?? 0, MaxRows);
            for (int i = 0; i < count; i++)
                current.Add(entries[i]);

            panel.anchoredPosition = new Vector2(0f, -topOffset);
            headerText.text = header;
            headerText.color = accent;
            SetBracketColor(accent);
            for (int i = 0; i < rows.Count; i++)
            {
                rows[i].root.SetActive(i < current.Count);
                rows[i].keyBg.color = accent;
                rows[i].label.color = labelColor;
            }
            Refresh();

            IsShowing = true;
            Play(ShowRoutine());
        }

        /// <summary>Flashes the prompt as completed, then hides it.</summary>
        public void Complete()
        {
            if (!IsShowing) return;
            IsShowing = false;
            Play(CompleteRoutine());
        }

        public void Hide(bool instant = false)
        {
            IsShowing = false;
            if (instant || !isActiveAndEnabled)
            {
                StopAnim();
                if (group != null) group.alpha = 0f;
                return;
            }
            Play(FadeRoutine(group.alpha, 0f, hideDuration));
        }

        /// <summary>Moves the current prompt to the top edge while a full-screen menu is open. Show() resets it.</summary>
        public void SetMenuDocked(bool docked)
        {
            Build();
            panel.anchoredPosition = new Vector2(0f, -(docked ? menuTopOffset : topOffset));
        }

        #endregion

        #region Text

        private void HandleDeviceChanged(bool _) => Refresh();

        private void Refresh()
        {
            bool pad = input != null && input.IsUsingGamepad;
            for (int i = 0; i < current.Count; i++)
            {
                Entry e = current[i];
                string key = pad ? e.gamepadOverride : e.keyboardOverride;
                if (string.IsNullOrEmpty(key) && input != null && !string.IsNullOrEmpty(e.action))
                    key = input.GetBindingHint(e.action);
                // Label-only entries (e.g. "Collect the mod") hide the key chip.
                Row r = rows[i];
                bool hasKey = !string.IsNullOrEmpty(key);
                r.keyBg.gameObject.SetActive(hasKey);
                r.key.text = hasKey ? key.ToUpperInvariant() : string.Empty;
                r.suffix.text = string.IsNullOrEmpty(e.suffix) ? string.Empty : e.suffix;
                r.suffix.gameObject.SetActive(!string.IsNullOrEmpty(e.suffix));
                r.label.text = string.IsNullOrEmpty(e.label) ? string.Empty : e.label.ToUpperInvariant();
            }
        }

        private void SubscribeInput()
        {
            GameInputManager next = GameInputManager.Instance;
            if (next == input) return;
            if (input != null) input.OnInputDeviceChanged -= HandleDeviceChanged;
            input = next;
            if (input != null) input.OnInputDeviceChanged += HandleDeviceChanged;
        }

        #endregion

        #region Animation

        private void Play(IEnumerator routine)
        {
            StopAnim();
            if (isActiveAndEnabled)
                animRoutine = StartCoroutine(routine);
        }

        private void StopAnim()
        {
            if (animRoutine != null) StopCoroutine(animRoutine);
            animRoutine = null;
        }

        private IEnumerator ShowRoutine()
        {
            float t = 0f;
            while (t < showDuration)
            {
                t += Time.unscaledDeltaTime;
                float k = Mathf.Clamp01(t / showDuration);
                // Glitchy flicker on the way in, then settle.
                group.alpha = k < 0.85f ? (UnityEngine.Random.value < 0.3f ? 0.25f : k) : 1f;
                panel.localScale = new Vector3(1f, Mathf.Lerp(0.15f, 1f, 1f - (1f - k) * (1f - k)), 1f);
                yield return null;
            }
            group.alpha = 1f;
            panel.localScale = Vector3.one;
            animRoutine = null;
        }

        private IEnumerator CompleteRoutine()
        {
            headerText.text = completeHeader;
            headerText.color = completeColor;
            SetBracketColor(completeColor);
            for (int i = 0; i < current.Count; i++)
            {
                rows[i].keyBg.color = completeColor;
                rows[i].label.color = completeColor;
            }
            group.alpha = 1f;

            float t = 0f;
            while (t < completeHold)
            {
                t += Time.unscaledDeltaTime;
                yield return null;
            }

            yield return FadeRoutine(1f, 0f, hideDuration);
        }

        private IEnumerator FadeRoutine(float from, float to, float duration)
        {
            float t = 0f;
            while (t < duration)
            {
                t += Time.unscaledDeltaTime;
                group.alpha = Mathf.Lerp(from, to, Mathf.Clamp01(t / duration));
                yield return null;
            }
            group.alpha = to;
            animRoutine = null;
        }

        private void SetBracketColor(Color c)
        {
            for (int i = 0; i < brackets.Length; i++)
                brackets[i].color = c;
        }

        #endregion

        #region Build

        private void Build()
        {
            if (built) return;
            built = true;

            var canvasGo = new GameObject("TutorialPromptCanvas", typeof(RectTransform));
            canvasGo.transform.SetParent(transform, false);
            var canvas = canvasGo.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = sortingOrder;
            var scaler = canvasGo.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0.5f;

            group = canvasGo.AddComponent<CanvasGroup>();
            group.interactable = false;
            group.blocksRaycasts = false;
            group.alpha = 0f;

            // Panel: auto-sized vertical stack, anchored top-centre.
            panel = NewRect("Panel", canvasGo.transform);
            panel.anchorMin = panel.anchorMax = new Vector2(0.5f, 1f);
            panel.pivot = new Vector2(0.5f, 1f);
            panel.anchoredPosition = new Vector2(0f, -topOffset);
            var bg = panel.gameObject.AddComponent<Image>();
            bg.color = panelColor;
            bg.raycastTarget = false;
            var layout = panel.gameObject.AddComponent<VerticalLayoutGroup>();
            layout.padding = new RectOffset(26, 30, 14, 18);
            layout.spacing = 10f;
            layout.childAlignment = TextAnchor.UpperLeft;
            layout.childControlWidth = layout.childControlHeight = true;
            layout.childForceExpandWidth = layout.childForceExpandHeight = false;
            var fitter = panel.gameObject.AddComponent<ContentSizeFitter>();
            fitter.horizontalFit = fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            headerText = NewText("Header", panel, 15f);
            UIFonts.ApplyHudBody(headerText);
            headerText.fontSize = 15f;
            headerText.characterSpacing = 18f;
            headerText.color = accent;

            for (int i = 0; i < MaxRows; i++)
                rows.Add(BuildRow(i));

            // Corner brackets and scanline sit outside the layout.
            brackets = new Image[8];
            const float len = 16f, thick = 2f;
            int b = 0;
            for (int corner = 0; corner < 4; corner++)
            {
                Vector2 anchor = new Vector2(corner % 2, corner / 2);
                brackets[b++] = Bracket(anchor, new Vector2(len, thick));
                brackets[b++] = Bracket(anchor, new Vector2(thick, len));
            }

            scanline = NewRect("Scanline", panel);
            scanline.gameObject.AddComponent<LayoutElement>().ignoreLayout = true;
            scanline.anchorMin = new Vector2(0f, 1f);
            scanline.anchorMax = new Vector2(1f, 1f);
            scanline.pivot = new Vector2(0.5f, 1f);
            scanline.sizeDelta = new Vector2(0f, 2f);
            var scan = scanline.gameObject.AddComponent<Image>();
            scan.color = new Color(completeColor.r, completeColor.g, completeColor.b, 0.12f);
            scan.raycastTarget = false;
        }

        private Row BuildRow(int index)
        {
            var row = new Row();
            RectTransform rt = NewRect($"Row{index}", panel);
            row.root = rt.gameObject;
            var h = row.root.AddComponent<HorizontalLayoutGroup>();
            h.spacing = 12f;
            h.childAlignment = TextAnchor.MiddleLeft;
            h.childControlWidth = h.childControlHeight = true;
            h.childForceExpandWidth = h.childForceExpandHeight = false;

            RectTransform chip = NewRect("Key", rt);
            row.keyBg = chip.gameObject.AddComponent<Image>();
            row.keyBg.color = accent;
            row.keyBg.raycastTarget = false;
            var chipLayout = chip.gameObject.AddComponent<HorizontalLayoutGroup>();
            chipLayout.padding = new RectOffset(10, 10, 3, 3);
            chipLayout.childControlWidth = chipLayout.childControlHeight = true;
            chipLayout.childForceExpandWidth = chipLayout.childForceExpandHeight = false;
            chip.gameObject.AddComponent<LayoutElement>().minWidth = 34f;

            row.key = NewText("KeyText", chip, 22f);
            UIFonts.ApplyHudBody(row.key);
            row.key.fontSize = 22f;
            row.key.fontStyle = FontStyles.Bold;
            row.key.color = keyTextColor;
            row.key.alignment = TextAlignmentOptions.Center;

            row.suffix = NewText("Suffix", rt, 22f);
            UIFonts.ApplyHudBody(row.suffix);
            row.suffix.fontSize = 22f;
            row.suffix.fontStyle = FontStyles.Bold;
            row.suffix.color = accent;

            row.label = NewText("Label", rt, 24f);
            UIFonts.ApplyHudBody(row.label);
            row.label.fontSize = 24f;
            row.label.characterSpacing = 6f;
            row.label.color = labelColor;

            row.root.SetActive(false);
            return row;
        }

        private Image Bracket(Vector2 anchor, Vector2 size)
        {
            RectTransform rt = NewRect("Bracket", panel);
            rt.gameObject.AddComponent<LayoutElement>().ignoreLayout = true;
            rt.anchorMin = rt.anchorMax = anchor;
            rt.pivot = anchor;
            rt.anchoredPosition = new Vector2(anchor.x < 0.5f ? -4f : 4f, anchor.y < 0.5f ? -4f : 4f);
            rt.sizeDelta = size;
            var img = rt.gameObject.AddComponent<Image>();
            img.color = accent;
            img.raycastTarget = false;
            return img;
        }

        private static RectTransform NewRect(string name, Transform parent)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            return (RectTransform)go.transform;
        }

        private static TextMeshProUGUI NewText(string name, Transform parent, float size)
        {
            RectTransform rt = NewRect(name, parent);
            var t = rt.gameObject.AddComponent<TextMeshProUGUI>();
            t.fontSize = size;
            t.raycastTarget = false;
            t.textWrappingMode = TextWrappingModes.NoWrap;
            return t;
        }

        #endregion
    }
}
