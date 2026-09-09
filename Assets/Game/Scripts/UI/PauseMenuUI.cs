using System;
using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace junklite
{
    /// <summary>Responsive pause overlay with an editor-only switch for the live dev console.</summary>
    public class PauseMenuUI : MonoBehaviour
    {
        [SerializeField] private GameObject panel;
        [SerializeField] private MenuButton restartLevelButton;
        [SerializeField] private MenuButton restartGameButton;
        [SerializeField] private MenuButton quitButton;
        [SerializeField] private bool showOnGameOver = true;
        [SerializeField] private TMP_FontAsset headingFont;
        [SerializeField] private TMP_FontAsset bodyFont;
        [SerializeField] private Font headingSourceFont;

        private static readonly Color Backdrop = new(0.012f, 0.018f, 0.042f, 0.72f);
        private static readonly Color Frame = new(0.032f, 0.045f, 0.087f, 0.97f);
        private static readonly Color Card = new(0.055f, 0.073f, 0.125f, 0.96f);
        private static readonly Color Hover = new(0.075f, 0.18f, 0.24f, 1f);
        private static readonly Color Cyan = new(0.24f, 0.91f, 0.98f, 1f);
        private static readonly Color Magenta = new(0.86f, 0.25f, 0.89f, 1f);
        private static readonly Color Paper = new(0.9f, 0.94f, 1f, 1f);
        private static readonly Color Muted = new(0.51f, 0.61f, 0.74f, 1f);
        private static readonly Color Line = new(0.18f, 0.27f, 0.38f, 0.75f);
        private static readonly Color Danger = new(1f, 0.4f, 0.5f, 1f);
        private static readonly Vector2 DesignSize = new(1360f, 800f);

        private readonly List<MenuButton> systemButtons = new();
        private readonly List<MenuButton> editorButtons = new();
        private MenuButton[] buttons = Array.Empty<MenuButton>();
        private MenuButton systemTabButton;
        private MenuButton editorTabButton;
        private GameObject systemPage;
        private GameObject editorPage;
        private TMP_Text sceneStatusText;
        private TMP_Text playerStatusText;
        private CanvasGroup panelCanvasGroup;
        private RectTransform frameTransform;
        private Coroutine revealRoutine;
        private GameInputManager subscribedInput;
        private int focusedIndex;
        private bool isVisible;
        private bool showingEditorPage;
        private float revealScale = 1f;
        private TMP_FontAsset generatedHeadingFont;

#if UNITY_EDITOR
        private Toggle devToolsToggle;
        private Image toggleTrack;
        private RectTransform toggleThumb;
        private TMP_Text toggleStateText;
        private TMP_Text editorStatusText;
#endif

        private void Awake()
        {
            if (headingSourceFont != null)
            {
                generatedHeadingFont = TMP_FontAsset.CreateFontAsset(headingSourceFont);
                headingFont = generatedHeadingFont;
            }
            if (bodyFont == null)
                bodyFont = TMP_Settings.defaultFontAsset;
            if (headingFont == null)
                headingFont = bodyFont;
            BuildInterface();
            Hide();
        }

        private void OnEnable()
        {
            if (GameManager.Instance != null)
                GameManager.Instance.OnGameStateChanged += OnGameStateChanged;
#if UNITY_EDITOR
            EditorRuntimeDevToolsPanel.FeatureEnabledChanged += RefreshEditorState;
#endif
        }

        private void OnDisable()
        {
            if (GameManager.Instance != null)
                GameManager.Instance.OnGameStateChanged -= OnGameStateChanged;
#if UNITY_EDITOR
            EditorRuntimeDevToolsPanel.FeatureEnabledChanged -= RefreshEditorState;
            if (isVisible)
                EditorRuntimeDevToolsPanel.SetPauseMenuOpen(false);
#endif
            UnsubscribeInput();
        }

        private void LateUpdate()
        {
            if (isVisible)
                FitFrame();
        }

        private void OnDestroy()
        {
#if UNITY_EDITOR
            EditorRuntimeDevToolsPanel.FeatureEnabledChanged -= RefreshEditorState;
#endif
            if (generatedHeadingFont != null)
            {
                foreach (Texture2D atlas in generatedHeadingFont.atlasTextures)
                    if (atlas != null)
                        Destroy(atlas);
                Destroy(generatedHeadingFont.material);
                Destroy(generatedHeadingFont);
            }
        }

        private void OnGameStateChanged(GameManager.GameState state)
        {
            if (state == GameManager.GameState.Paused ||
                (showOnGameOver && state == GameManager.GameState.GameOver))
                Show();
            else
                Hide();
        }

        private void Show()
        {
            if (isVisible)
                return;
            isVisible = true;
#if UNITY_EDITOR
            EditorRuntimeDevToolsPanel.SetPauseMenuOpen(true);
#endif
            panel.SetActive(true);
            RefreshStatusReadout();
            ShowSystemPage();
            SubscribeInput();
            GameInputManager.Instance?.SwitchToUIActionMap();
            Canvas.ForceUpdateCanvases();
            FitFrame();
            revealRoutine = StartCoroutine(PlayReveal());
        }

        private void Hide()
        {
            if (revealRoutine != null)
                StopCoroutine(revealRoutine);
            revealRoutine = null;
            bool wasVisible = isVisible;
            isVisible = false;
            revealScale = 1f;
            UnsubscribeInput();
            panelCanvasGroup.alpha = 1f;
            panel.SetActive(false);
            // Initial construction must not change a loading screen or inventory's input map.
            if (wasVisible)
            {
                GameInputManager.Instance?.SwitchToPlayerActionMap();
#if UNITY_EDITOR
                EditorRuntimeDevToolsPanel.SetPauseMenuOpen(false);
#endif
            }
        }

        private IEnumerator PlayReveal()
        {
            const float duration = 0.16f;
            float elapsed = 0f;
            while (elapsed < duration)
            {
                elapsed += Time.unscaledDeltaTime;
                float t = 1f - Mathf.Pow(1f - Mathf.Clamp01(elapsed / duration), 3f);
                panelCanvasGroup.alpha = t;
                revealScale = Mathf.Lerp(0.985f, 1f, t);
                FitFrame();
                yield return null;
            }
            panelCanvasGroup.alpha = 1f;
            revealScale = 1f;
            revealRoutine = null;
        }

        private void FitFrame()
        {
            Rect available = ((RectTransform)panel.transform).rect;
            // Fixed design coordinates keep typography, gaps and controls proportional
            // even when the parent HUD uses a different CanvasScaler configuration.
            float scale = Mathf.Min(available.width / (DesignSize.x + 120f),
                available.height / (DesignSize.y + 100f));
            frameTransform.localScale = Vector3.one * Mathf.Max(0.01f, scale) * revealScale;
        }

        private void SubscribeInput()
        {
            UnsubscribeInput();
            subscribedInput = GameInputManager.Instance;
            if (subscribedInput == null)
                return;
            subscribedInput.OnUINavigate += HandleNavigate;
            subscribedInput.OnUISubmit += HandleSubmit;
            subscribedInput.OnUICancel += HandleCancel;
        }

        private void UnsubscribeInput()
        {
            if (subscribedInput == null)
                return;
            subscribedInput.OnUINavigate -= HandleNavigate;
            subscribedInput.OnUISubmit -= HandleSubmit;
            subscribedInput.OnUICancel -= HandleCancel;
            subscribedInput = null;
        }

        private void HandleNavigate(Vector2 direction)
        {
#if UNITY_EDITOR
            if (direction.x > 0.3f && !showingEditorPage)
            {
                ShowEditorPage();
                return;
            }
            if (direction.x < -0.3f && showingEditorPage)
            {
                ShowSystemPage();
                return;
            }
#endif
            if (buttons.Length == 0 || Mathf.Abs(direction.y) < 0.3f)
                return;
            focusedIndex = Mathf.Clamp(focusedIndex + (direction.y > 0f ? -1 : 1), 0, buttons.Length - 1);
            UpdateFocus();
        }

        private void HandleSubmit()
        {
            if (focusedIndex >= 0 && focusedIndex < buttons.Length)
                buttons[focusedIndex]?.Click();
        }

        private void HandleCancel() => GameManager.Instance?.ResumeGame();

        private void ShowSystemPage()
        {
            showingEditorPage = false;
            systemPage.SetActive(true);
            editorPage?.SetActive(false);
            buttons = systemButtons.ToArray();
            focusedIndex = 0;
            UpdateFocus();
        }

#if UNITY_EDITOR
        private void ShowEditorPage()
        {
            showingEditorPage = true;
            systemPage.SetActive(false);
            editorPage.SetActive(true);
            RefreshEditorState();
            buttons = editorButtons.ToArray();
            focusedIndex = 0;
            UpdateFocus();
        }
#endif

        private void UpdateFocus()
        {
            foreach (MenuButton button in systemButtons)
                button.SetSelected(false);
            foreach (MenuButton button in editorButtons)
                button.SetSelected(false);
            if (focusedIndex >= 0 && focusedIndex < buttons.Length)
                buttons[focusedIndex].SetSelected(true);
            systemTabButton.SetSelected(!showingEditorPage);
            editorTabButton?.SetSelected(showingEditorPage);
        }

        private void BuildInterface()
        {
            Stretch((RectTransform)transform);
            transform.localScale = Vector3.one;
            if (panel == null)
                panel = CreateRect("Panel", transform).gameObject;
            Stretch((RectTransform)panel.transform);
            panel.transform.localScale = Vector3.one;
            for (int i = panel.transform.childCount - 1; i >= 0; i--)
            {
                GameObject child = panel.transform.GetChild(i).gameObject;
                child.SetActive(false);
                Destroy(child);
            }
            Image backdrop = panel.GetComponent<Image>();
            if (backdrop == null)
                backdrop = panel.AddComponent<Image>();
            backdrop.color = Backdrop;
            backdrop.sprite = null;
            backdrop.raycastTarget = true;
            panelCanvasGroup = panel.GetComponent<CanvasGroup>();
            if (panelCanvasGroup == null)
                panelCanvasGroup = panel.AddComponent<CanvasGroup>();

            Image frame = CreateImage("Pause Interface", panel.transform, Frame);
            frameTransform = frame.rectTransform;
            frameTransform.anchorMin = frameTransform.anchorMax = new Vector2(0.5f, 0.5f);
            frameTransform.pivot = new Vector2(0.5f, 0.5f);
            frameTransform.anchoredPosition = Vector2.zero;
            frameTransform.sizeDelta = DesignSize;
            AddBorder(frameTransform, Line);

            // Fine architectural lines and split neon accents echo the city and HUD.
            for (int i = 1; i < 12; i++)
                RectImage("Grid Line", frameTransform, new Color(Cyan.r, Cyan.g, Cyan.b, 0.025f),
                    i * 116f, 0f, 1f, 800f);
            RectImage("Cyan Edge", frameTransform, Cyan, 0f, 0f, 390f, 3f);
            RectImage("Magenta Edge", frameTransform, Magenta, 1140f, 797f, 220f, 3f);
            RectImage("Left Mark", frameTransform, Cyan, 0f, 0f, 3f, 28f);
            RectImage("Right Mark", frameTransform, Magenta, 1357f, 772f, 3f, 28f);
            BuildHeader();
            BuildFooter();
            BuildSystemPage();
#if UNITY_EDITOR
            BuildEditorPage();
#endif
        }

        private void BuildHeader()
        {
            TextAt("Brand", frameTransform, "JUNKLITE  /  NEURAL LINK", 17f, Cyan, 44, 28, 720, 26);
            TextAt("Title", frameTransform, "RUN PAUSED", 52f, Paper, 42, 65, 790, 74, true);
            systemTabButton = CreateButton(frameTransform, "SYSTEM", ShowSystemPage, 17f);
            Place((RectTransform)systemTabButton.transform, 964, 93, 154, 52);
#if UNITY_EDITOR
            editorTabButton = CreateButton(frameTransform, "EDITOR", ShowEditorPage, 17f);
            Place((RectTransform)editorTabButton.transform, 1130, 93, 184, 52);
#endif
            RectImage("Header Rule", frameTransform, Line, 44, 200, 1272, 1);
            RectImage("Header Signal", frameTransform, Magenta, 44, 200, 100, 3);
        }

        private void BuildFooter()
        {
            RectImage("Footer Rule", frameTransform, Line, 44, 735, 1272, 1);
            TextAt("Input Hints", frameTransform,
                "ESC / B  RESUME     UP / DOWN  NAVIGATE     ENTER / A  SELECT", 14f, Muted,
                44, 749, 920, 30);
        }

        private void BuildSystemPage()
        {
            systemPage = CreateRect("System Page", frameTransform).gameObject;
            Place((RectTransform)systemPage.transform, 44, 228, 1272, 480);
            TextAt("Commands", systemPage.transform, "SESSION", 16f, Muted, 0, 0, 520, 28);
            AddSystemAction("CONTINUE RUN", "Return to the streets", () => GameManager.Instance?.ResumeGame(), 46);
            restartLevelButton = AddSystemAction("RESTART SECTOR", "Start this scene again", () => GameManager.Instance?.RestartCurrentScene(), 142);
            restartGameButton = AddSystemAction("RETURN TO TITLE", "Leave the current run", () => GameManager.Instance?.RestartGame(), 238);
            quitButton = AddSystemAction("QUIT TO DESKTOP", "Disconnect from JunkLite", () => GameManager.Instance?.QuitGame(), 334, true);

            Image status = RectImage("Session Card", systemPage.transform, Card, 588, 0, 684, 454);
            AddBorder(status.rectTransform, Line);
            RectImage("Status Accent", status.transform, Magenta, 0, 0, 3, 454);
            TextAt("Status Label", status.transform, "LIVE SESSION", 16f, Magenta, 34, 27, 610, 26);
            TextAt("Status Title", status.transform, "STAND BY.", 46f, Paper, 32, 67, 620, 66, true);
            RectImage("Status Rule", status.transform, Line, 34, 220, 616, 1);
            TextAt("Sector Label", status.transform, "SECTOR", 15f, Muted, 34, 246, 172, 28);
            sceneStatusText = TextAt("Sector Value", status.transform, "--", 21f, Paper, 212, 240, 438, 40);
            TextAt("Player Label", status.transform, "PLAYER SIGNAL", 15f, Muted, 34, 306, 180, 28);
            playerStatusText = TextAt("Player Value", status.transform, "--", 21f, Cyan, 212, 300, 438, 40);
        }

        private MenuButton AddSystemAction(string label, string subtitle, Action action, float y, bool danger = false)
        {
            MenuButton button = CreateButton(systemPage.transform, label, action, 24f, danger);
            Place((RectTransform)button.transform, 0, y, 548, 80);
            TMP_Text text = button.GetComponentInChildren<TMP_Text>();
            Place(text.rectTransform, 24, 7, 470, 37);
            TextAt("Description", button.transform, subtitle, 16f, Muted, 24, 44, 470, 27);
            // Keep subtext readable on the selected tinted card.
            systemButtons.Add(button);
            return button;
        }

#if UNITY_EDITOR
        private void BuildEditorPage()
        {
            editorPage = CreateRect("Editor Page", frameTransform).gameObject;
            Place((RectTransform)editorPage.transform, 44, 228, 1272, 480);
            TextAt("Editor Heading", editorPage.transform, "DEVELOPER ACCESS", 32f, Paper, 0, 0, 820, 48, true);

            MenuButton toggleRow = CreateButton(editorPage.transform, "ENABLE DEV TOOLS",
                () => devToolsToggle.isOn = !devToolsToggle.isOn, 24f);
            Place((RectTransform)toggleRow.transform, 0, 96, 798, 96);
            Place(toggleRow.GetComponentInChildren<TMP_Text>().rectTransform, 24, 27, 520, 42);
            editorButtons.Add(toggleRow);

            toggleTrack = RectImage("Enable Dev Tools Toggle", toggleRow.transform, Line, 656, 16, 112, 40);
            toggleTrack.raycastTarget = true;
            devToolsToggle = toggleTrack.gameObject.AddComponent<Toggle>();
            devToolsToggle.targetGraphic = toggleTrack;
            devToolsToggle.transition = Selectable.Transition.None;
            devToolsToggle.navigation = new Navigation { mode = Navigation.Mode.None };
            toggleThumb = RectImage("Switch Thumb", toggleTrack.transform, Paper, 4, 4, 32, 32).rectTransform;
            toggleStateText = TextAt("Switch State", toggleRow.transform, "OFF", 14f, Muted, 656, 60, 112, 24);
            toggleStateText.horizontalAlignment = HorizontalAlignmentOptions.Center;
            devToolsToggle.onValueChanged.AddListener(EditorRuntimeDevToolsPanel.SetFeatureEnabled);

            editorStatusText = TextAt("Console Status", editorPage.transform, "", 18f, Cyan, 0, 208, 798, 35);
            TextAt("Available Label", editorPage.transform, "TOOLS", 14f, Magenta, 0, 296, 798, 26);
            TextAt("Available Tools", editorPage.transform,
                "Player / spawns / mods / run controls", 18f, Muted, 0, 332, 798, 35);

            Image help = RectImage("Console Guide", editorPage.transform, Card, 844, 0, 428, 454);
            AddBorder(help.rectTransform, Line);
            TextAt("Guide Label", help.transform, "CONSOLE CONTROLS", 15f, Magenta, 26, 26, 376, 30);
            TextAt("Shortcut", help.transform, "F10  /  HIDE OR SHOW", 22f, Paper, 26, 104, 376, 42);
            TextAt("Shortcut Help", help.transform, "Hiding keeps enabled tools active.", 17f, Muted, 26, 152, 376, 52);
            RectImage("Guide Rule", help.transform, Line, 26, 238, 376, 1);
            TextAt("Preference Help", help.transform, "Dev tools preference is saved\nbetween play sessions.", 17f, Muted, 26, 268, 376, 60);

            MenuButton resume = CreateButton(editorPage.transform, "RESUME GAME  >", HandleCancel, 20f);
            Place((RectTransform)resume.transform, 0, 420, 798, 54);
            editorButtons.Add(resume);
            RefreshEditorState();
            editorPage.SetActive(false);
        }

        private void RefreshEditorState()
        {
            if (devToolsToggle == null)
                return;
            bool enabled = EditorRuntimeDevToolsPanel.IsFeatureEnabled;
            devToolsToggle.SetIsOnWithoutNotify(enabled);
            toggleTrack.color = enabled ? Cyan : Line;
            Place(toggleThumb, enabled ? 76 : 4, 4, 32, 32);
            toggleThumb.GetComponent<Image>().color = enabled ? Frame : Muted;
            toggleStateText.text = enabled ? "ON" : "OFF";
            toggleStateText.color = enabled ? Cyan : Muted;
            editorStatusText.text = enabled
                ? "Resume to use the console."
                : "Enable to use the console during play.";
            editorStatusText.color = enabled ? Cyan : Muted;
        }
#endif

        private void RefreshStatusReadout()
        {
            sceneStatusText.text = SceneManager.GetActiveScene().name.ToUpperInvariant();
            PlayerCharacter player = PlayerLifecycle.Instance != null
                ? PlayerLifecycle.Instance.Player : FindFirstObjectByType<PlayerCharacter>();
            playerStatusText.text = player != null && player.IsAlive ? "ONLINE" : "NO SIGNAL";
        }

        private MenuButton CreateButton(Transform parent, string label, Action action, float size, bool danger = false)
        {
            Image background = CreateImage(label, parent, Card);
            AddBorder(background.rectTransform, Line);
            TMP_Text text = TextAt("Label", background.transform, label, size, danger ? Danger : Paper, 0, 0, 100, 40);
            Stretch(text.rectTransform);
            text.rectTransform.offsetMin = new Vector2(22f, 2f);
            text.rectTransform.offsetMax = new Vector2(-22f, -2f);
            MenuButton button = background.gameObject.AddComponent<MenuButton>();
            button.Configure(text, background, Card, danger ? Danger : Paper,
                new Color(0.08f, 0.21f, 0.29f, 1f), danger ? Danger : Cyan,
                Hover, new Color(0.12f, 0.29f, 0.36f, 1f), size, size);
            button.OnClick += action;
            return button;
        }

        private static RectTransform CreateRect(string name, Transform parent)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.layer = parent.gameObject.layer;
            RectTransform rect = (RectTransform)go.transform;
            rect.SetParent(parent, false);
            return rect;
        }

        private static Image CreateImage(string name, Transform parent, Color color)
        {
            Image graphic = CreateRect(name, parent).gameObject.AddComponent<Image>();
            graphic.color = color;
            return graphic;
        }

        private static Image RectImage(string name, Transform parent, Color color, float x, float y, float w, float h)
        {
            Image graphic = CreateImage(name, parent, color);
            Place(graphic.rectTransform, x, y, w, h);
            graphic.raycastTarget = false;
            return graphic;
        }

        private TMP_Text TextAt(string name, Transform parent, string value, float size, Color color,
            float x, float y, float w, float h, bool heading = false)
        {
            var text = CreateRect(name, parent).gameObject.AddComponent<TextMeshProUGUI>();
            text.font = heading ? headingFont : bodyFont;
            text.text = value;
            text.fontSize = size;
            text.color = color;
            text.raycastTarget = false;
            text.textWrappingMode = TextWrappingModes.Normal;
            text.overflowMode = TextOverflowModes.Ellipsis;
            text.verticalAlignment = VerticalAlignmentOptions.Middle;
            Place(text.rectTransform, x, y, w, h);
            return text;
        }

        private static void AddBorder(RectTransform parent, Color color)
        {
            Image top = CreateImage("Top Border", parent, color);
            Stretch(top.rectTransform);
            top.rectTransform.anchorMin = new Vector2(0f, 1f);
            top.rectTransform.sizeDelta = new Vector2(0f, 1f);
            Image bottom = CreateImage("Bottom Border", parent, color);
            Stretch(bottom.rectTransform);
            bottom.rectTransform.anchorMax = new Vector2(1f, 0f);
            bottom.rectTransform.sizeDelta = new Vector2(0f, 1f);
            Image left = CreateImage("Left Border", parent, color);
            Stretch(left.rectTransform);
            left.rectTransform.anchorMax = new Vector2(0f, 1f);
            left.rectTransform.sizeDelta = new Vector2(1f, 0f);
            Image right = CreateImage("Right Border", parent, color);
            Stretch(right.rectTransform);
            right.rectTransform.anchorMin = new Vector2(1f, 0f);
            right.rectTransform.sizeDelta = new Vector2(1f, 0f);
            foreach (Image edge in new[] { top, bottom, left, right })
                edge.raycastTarget = false;
        }

        private static void Place(RectTransform rect, float x, float y, float w, float h)
        {
            rect.anchorMin = rect.anchorMax = new Vector2(0f, 1f);
            rect.pivot = new Vector2(0f, 1f);
            rect.anchoredPosition = new Vector2(x, -y);
            rect.sizeDelta = new Vector2(w, h);
        }

        private static void Stretch(RectTransform rect)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = rect.offsetMax = Vector2.zero;
        }
    }
}
