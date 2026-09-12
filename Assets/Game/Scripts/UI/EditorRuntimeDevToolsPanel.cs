#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace junklite
{
    /// <summary>
    /// Editor-only, always-available play-mode console. The panel is created on its own
    /// overlay canvas, scales from a 1920x1080 reference resolution, and survives scene loads.
    /// </summary>
    [DefaultExecutionOrder(-50)]
    public sealed class EditorRuntimeDevToolsPanel : MonoBehaviour
    {
        public const string EditorPreferenceKey = "JunkLite.RuntimeDevTools.Enabled";

        private static readonly Color Backdrop = new(0.032f, 0.045f, 0.087f, 0.975f);
        private static readonly Color Card = new(0.07f, 0.11f, 0.18f, 1f);
        private static readonly Color Acid = new(0.24f, 0.91f, 0.98f, 1f);
        private static readonly Color Magenta = new(0.86f, 0.25f, 0.89f, 1f);
        private static readonly Color Paper = new(0.9f, 0.94f, 1f, 1f);
        private static readonly Color Muted = new(0.51f, 0.61f, 0.74f, 1f);

        private static EditorRuntimeDevToolsPanel instance;
        private static bool pauseMenuOpen;
        public static event Action FeatureEnabledChanged;

        private readonly List<ToggleVisual> toggleVisuals = new();
        private readonly List<RectTransform> categoryPages = new();
        private readonly List<Button> categoryButtons = new();
        private PauseMenuDeveloperTools toolSource;
        private TMP_FontAsset themeFont;
        private RectTransform safeAreaRoot;
        private RectTransform toolContent;
        private VerticalLayoutGroup toolContentLayout;
        private ScrollRect toolScrollRect;
        private GameObject panelRoot;
        private GameObject collapsedChip;
        private TMP_Text feedbackText;
        private bool infiniteAmmo;
        private bool infiniteDurability;
        private int lastScreenWidth;
        private int lastScreenHeight;
        private Rect lastSafeArea;
        private bool panelExpanded = true;
        private bool wasSuppressed;

        public static bool IsFeatureEnabled => EditorPrefs.GetBool(EditorPreferenceKey, false);
        public static bool IsPanelVisible => instance != null &&
                                             instance.panelRoot != null &&
                                             instance.panelRoot.activeInHierarchy;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            instance = null;
            pauseMenuOpen = false;
            FeatureEnabledChanged = null;
        }

        public static void SetPauseMenuOpen(bool open)
        {
            pauseMenuOpen = open;
            instance?.UpdatePresentation();
        }

        // This uses current pointer coordinates, so input callbacks do not depend on
        // the EventSystem's previous-frame hover state and cannot shoot through the UI.
        public static bool ContainsPointer()
        {
            if (instance == null || Mouse.current == null || instance.safeAreaRoot == null ||
                !instance.safeAreaRoot.gameObject.activeInHierarchy)
                return false;
            GameObject target = instance.panelExpanded ? instance.panelRoot : instance.collapsedChip;
            return target != null && target.activeInHierarchy &&
                RectTransformUtility.RectangleContainsScreenPoint(
                    (RectTransform)target.transform, Mouse.current.position.ReadValue());
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void CreateFromEditorPreference()
        {
            if (EditorPrefs.GetBool(EditorPreferenceKey, false))
                SetFeatureEnabled(true);
        }

        public static void SetFeatureEnabled(bool enabled)
        {
            EditorPrefs.SetBool(EditorPreferenceKey, enabled);
            if (!Application.isPlaying)
            {
                FeatureEnabledChanged?.Invoke();
                return;
            }

            if (enabled)
            {
                EnsureInstance();
                instance?.SetPanelVisibleInternal(true);
                FeatureEnabledChanged?.Invoke();
                return;
            }

            EditorRuntimeDevToolsPanel existing = FindFirstObjectByType<EditorRuntimeDevToolsPanel>();
            if (existing != null)
            {
                // Clear overrides immediately, before the deferred Destroy completes.
                existing.ClearOverrides();
                existing.gameObject.SetActive(false);
                Destroy(existing.gameObject);
            }
            instance = null;
            FeatureEnabledChanged?.Invoke();
        }

        public static void SetPanelVisible(bool visible)
        {
            if (!Application.isPlaying || !IsFeatureEnabled)
                return;

            if (visible)
                EnsureInstance();
            instance?.SetPanelVisibleInternal(visible);
        }

        private static void EnsureInstance()
        {
            if (instance != null)
                return;

            instance = FindFirstObjectByType<EditorRuntimeDevToolsPanel>();
            if (instance != null)
                return;

            var root = new GameObject(
                "[EDITOR] Runtime Dev Tools",
                typeof(RectTransform),
                typeof(EditorRuntimeDevToolsPanel));
            instance = root.GetComponent<EditorRuntimeDevToolsPanel>();
        }

        private void Awake()
        {
            if (instance != null && instance != this)
            {
                Destroy(gameObject);
                return;
            }

            instance = this;
            DontDestroyOnLoad(gameObject);
            toolSource = new PauseMenuDeveloperTools();
            themeFont = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(
                "Assets/Game/New UI/Fonts/Lekton-Regular SDF 2.asset");
            if (themeFont == null)
                themeFont = TMP_Settings.defaultFontAsset;

            BuildCanvas();
            BuildInterface();
            ApplySafeArea(true);
            UpdatePresentation();
        }

        private void Update()
        {
            if (!pauseMenuOpen && Keyboard.current?.f10Key.wasPressedThisFrame == true)
                SetPanelVisibleInternal(!panelExpanded);

            ApplySafeArea(false);
            UpdatePresentation();

            if (infiniteAmmo || infiniteDurability)
                ApplyDurabilityOverrides();
        }

        private void OnDestroy()
        {
            // A replaced instance must not clear overrides owned by the new panel.
            if (instance != this)
                return;
            ClearOverrides();
            instance = null;
        }

        private void ClearOverrides()
        {
            infiniteAmmo = false;
            infiniteDurability = false;
            ApplyDurabilityOverrides();
        }

        private void UpdatePresentation()
        {
            if (safeAreaRoot == null)
                return;
            bool suppressed = pauseMenuOpen ||
                (GameManager.Instance != null && !GameManager.Instance.IsPlaying);
            safeAreaRoot.gameObject.SetActive(!suppressed);
            if (!suppressed && wasSuppressed && panelExpanded)
                RefreshToolScrollLayout();
            wasSuppressed = suppressed;
        }

        private void LateUpdate()
        {
            if (panelRoot == null || safeAreaRoot == null)
                return;
            Rect available = safeAreaRoot.rect;
            float scale = Mathf.Min(1f, (available.width - 32f) / 470f,
                (available.height - 36f) / 740f);
            panelRoot.transform.localScale = Vector3.one * Mathf.Max(0.1f, scale);
        }

        private void BuildCanvas()
        {
            Canvas canvas = gameObject.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.overrideSorting = true;
            canvas.sortingOrder = 9000;

            CanvasScaler scaler = gameObject.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 0.5f;
            scaler.referencePixelsPerUnit = 100f;

            gameObject.AddComponent<GraphicRaycaster>();

            RectTransform rootRect = (RectTransform)transform;
            Stretch(rootRect);

            safeAreaRoot = CreateRect("Safe Area", transform);
            Stretch(safeAreaRoot);
        }

        private void BuildInterface()
        {
            Image shadow = CreateImage("Panel Shadow", safeAreaRoot, new Color(0f, 0f, 0f, 0.6f));
            panelRoot = shadow.gameObject;
            RectTransform panelRect = shadow.rectTransform;
            panelRect.anchorMin = Vector2.one;
            panelRect.anchorMax = Vector2.one;
            panelRect.pivot = Vector2.one;
            panelRect.anchoredPosition = new Vector2(-16f, -18f);
            panelRect.sizeDelta = new Vector2(470f, 740f);

            Image panel = CreateImage("Dev Console", panelRect, Backdrop);
            Stretch(panel.rectTransform);
            panel.rectTransform.offsetMin = new Vector2(0f, 0f);
            panel.rectTransform.offsetMax = new Vector2(0f, 0f);
            AddBorder(panel.rectTransform, new Color(Acid.r, Acid.g, Acid.b, 0.5f), 1.5f);

            Image rail = CreateImage("Signal Rail", panel.transform, Magenta);
            Anchor(rail.rectTransform, 0f, 0.996f, 0.42f, 1f);
            rail.raycastTarget = false;

            BuildHeader(panel.transform);
            BuildToolScroller(panel.transform);
            BuildCategoryTabs(panel.transform);
            BuildFooter(panel.transform);
            BuildCollapsedChip();
        }

        private void BuildHeader(Transform parent)
        {
            RectTransform header = CreateRect("Header", parent);
            Anchor(header, 0.055f, 0.895f, 0.965f, 0.985f);

            TMP_Text title = CreateText("Title", header, "DEV CONSOLE", 26f, Paper, FontStyles.Bold);
            Anchor(title.rectTransform, 0f, 0.17f, 0.7f, 0.9f);

            CreateButton(header, "HIDE", () => SetPanelVisibleInternal(false), true,
                new Vector2(0.76f, 0.17f), new Vector2(1f, 0.78f));

            Image rule = CreateImage("Header Rule", header, Acid);
            Anchor(rule.rectTransform, 0f, 0f, 1f, 0f);
            rule.rectTransform.sizeDelta = new Vector2(0f, 2f);
            rule.raycastTarget = false;
        }

        private void BuildToolScroller(Transform parent)
        {
            // RectMask2D remains reliable with an invisible raycast surface. A stencil
            // Mask on a zero-alpha Image can clip the tool buttons out completely.
            Image viewportImage = CreateImage("Viewport", parent, new Color(0f, 0f, 0f, 0.001f));
            Anchor(viewportImage.rectTransform, 0.055f, 0.10f, 0.945f, 0.805f);
            viewportImage.raycastTarget = true;
            viewportImage.gameObject.AddComponent<RectMask2D>();

            toolScrollRect = viewportImage.gameObject.AddComponent<ScrollRect>();
            toolScrollRect.viewport = viewportImage.rectTransform;
            toolScrollRect.horizontal = false;
            toolScrollRect.vertical = true;
            toolScrollRect.movementType = ScrollRect.MovementType.Clamped;
            toolScrollRect.scrollSensitivity = 36f;

            string[] names = { "Player", "Spawn", "Mods", "Run" };
            for (int category = 0; category < names.Length; category++)
            {
            toolContent = CreateRect(names[category] + " Content", viewportImage.transform);
            toolContent.anchorMin = new Vector2(0f, 1f);
            toolContent.anchorMax = new Vector2(1f, 1f);
            toolContent.pivot = new Vector2(0.5f, 1f);
            toolContent.anchoredPosition = Vector2.zero;
            toolContent.sizeDelta = Vector2.zero;

            toolContentLayout = toolContent.gameObject.AddComponent<VerticalLayoutGroup>();
            toolContentLayout.padding = new RectOffset(0, 10, 4, 16);
            toolContentLayout.spacing = 8f;
            toolContentLayout.childAlignment = TextAnchor.UpperLeft;
            toolContentLayout.childControlWidth = true;
            toolContentLayout.childControlHeight = true;
            toolContentLayout.childForceExpandWidth = true;
            toolContentLayout.childForceExpandHeight = false;

            switch (category)
            {
                case 0:
                    BuildPlayerSection(toolContent);
                    break;
                case 1:
                    BuildActionSection(toolContent, "SPAWN ENEMIES", toolSource.EnemyActions);
                    BuildActionSection(toolContent, "SPAWN WEAPONS", toolSource.WeaponActions);
                    break;
                case 2:
                    BuildActionSection(toolContent, "SPAWN MODS", toolSource.ModActions);
                    break;
                case 3:
                    BuildActionSection(toolContent, "RUN CONTROL", toolSource.RunActions);
                    break;
            }
            categoryPages.Add(toolContent);
            toolContent.gameObject.SetActive(false);
            }

            Image track = CreateImage("Scroll Track", parent, new Color(0.13f, 0.19f, 0.29f, 0.5f));
            Anchor(track.rectTransform, 0.954f, 0.10f, 0.963f, 0.805f);
            Image handle = CreateImage("Scroll Handle", track.transform, Acid);
            Stretch(handle.rectTransform);
            Scrollbar scrollbar = track.gameObject.AddComponent<Scrollbar>();
            scrollbar.handleRect = handle.rectTransform;
            scrollbar.targetGraphic = handle;
            scrollbar.direction = Scrollbar.Direction.BottomToTop;
            scrollbar.navigation = new Navigation { mode = Navigation.Mode.None };
            toolScrollRect.verticalScrollbar = scrollbar;
            SelectCategory(0);
        }

        private void BuildCategoryTabs(Transform parent)
        {
            string[] labels = { "PLAYER", "SPAWN", "MODS", "RUN" };
            for (int i = 0; i < labels.Length; i++)
            {
                int index = i;
                float x = 0.055f + i * 0.23f;
                Button button = CreateButton(parent, labels[i], () => SelectCategory(index), true,
                    new Vector2(x, 0.827f), new Vector2(x + 0.216f, 0.875f));
                categoryButtons.Add(button);
            }
            SelectCategory(0);
        }

        private void SelectCategory(int index)
        {
            toolScrollRect.StopMovement();
            for (int i = 0; i < categoryPages.Count; i++)
                categoryPages[i].gameObject.SetActive(i == index);
            toolContent = categoryPages[index];
            toolContentLayout = toolContent.GetComponent<VerticalLayoutGroup>();
            toolScrollRect.content = toolContent;
            for (int i = 0; i < categoryButtons.Count; i++)
            {
                categoryButtons[i].targetGraphic.color = i == index ? Acid : Card;
                categoryButtons[i].GetComponentInChildren<TMP_Text>().color = i == index ? Backdrop : Paper;
            }
            RefreshToolScrollLayout();
            toolScrollRect.verticalNormalizedPosition = 1f;
        }

        private void RefreshToolScrollLayout()
        {
            if (toolContent == null || toolContentLayout == null)
                return;

            float height = toolContentLayout.padding.top + toolContentLayout.padding.bottom;
            int includedChildren = 0;
            for (int i = 0; i < toolContent.childCount; i++)
            {
                RectTransform child = toolContent.GetChild(i) as RectTransform;
                if (child == null || !child.gameObject.activeSelf)
                    continue;

                float preferredHeight = LayoutUtility.GetPreferredHeight(child);
                height += Mathf.Max(0f, preferredHeight > 0f ? preferredHeight : child.sizeDelta.y);
                includedChildren++;
            }

            if (includedChildren > 1)
                height += toolContentLayout.spacing * (includedChildren - 1);

            toolContent.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, Mathf.Max(1f, height));
            Canvas.ForceUpdateCanvases();
            LayoutRebuilder.ForceRebuildLayoutImmediate(toolContent);

        }

        private void BuildPlayerSection(Transform parent)
        {
            var actions = new List<PanelAction>
            {
                PanelAction.Toggle("INFINITE AMMO", () => infiniteAmmo, ToggleInfiniteAmmo),
                PanelAction.Toggle("INFINITE DURABILITY", () => infiniteDurability, ToggleInfiniteDurability)
            };

            foreach (PauseMenuDeveloperTools.ToolAction tool in toolSource.PlayerActions)
            {
                PauseMenuDeveloperTools.ToolAction captured = tool;
                actions.Add(PanelAction.Command(captured.Label, captured.Execute));
            }

            BuildPanelActionSection(parent, "PLAYER / LOADOUT", actions);
        }

        private void BuildActionSection(
            Transform parent,
            string title,
            IReadOnlyList<PauseMenuDeveloperTools.ToolAction> source)
        {
            var actions = new List<PanelAction>(source.Count);
            foreach (PauseMenuDeveloperTools.ToolAction tool in source)
            {
                PauseMenuDeveloperTools.ToolAction captured = tool;
                actions.Add(PanelAction.Command(captured.Label, captured.Execute));
            }
            BuildPanelActionSection(parent, title, actions);
        }

        private void BuildPanelActionSection(
            Transform parent,
            string title,
            IReadOnlyList<PanelAction> actions)
        {
            TMP_Text heading = CreateText(title + " Heading", parent,
                title, 16f, Paper, FontStyles.Bold);
            heading.gameObject.AddComponent<LayoutElement>().preferredHeight = 27f;

            RectTransform grid = CreateRect(title + " Grid", parent);
            GridLayoutGroup gridLayout = grid.gameObject.AddComponent<GridLayoutGroup>();
            gridLayout.cellSize = new Vector2(198f, 56f);
            gridLayout.spacing = new Vector2(8f, 8f);
            gridLayout.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
            gridLayout.constraintCount = 2;
            gridLayout.childAlignment = TextAnchor.UpperLeft;

            int rows = Mathf.Max(1, Mathf.CeilToInt(actions.Count / 2f));
            grid.gameObject.AddComponent<LayoutElement>().preferredHeight = rows * 56f + (rows - 1) * 8f;

            foreach (PanelAction action in actions)
            {
                PanelAction captured = action;
                Button button = CreateButton(grid, captured.Label, () => Execute(captured), false);
                if (captured.IsToggle)
                {
                    toggleVisuals.Add(new ToggleVisual(
                        button,
                        button.GetComponentInChildren<TMP_Text>(),
                        captured.Label,
                        captured.State));
                }
            }

            Image divider = CreateImage(title + " Divider", parent, new Color(Paper.r, Paper.g, Paper.b, 0.12f));
            divider.gameObject.AddComponent<LayoutElement>().preferredHeight = 1f;
            divider.raycastTarget = false;
        }

        private void BuildFooter(Transform parent)
        {
            RectTransform footer = CreateRect("Footer", parent);
            Anchor(footer, 0.055f, 0.015f, 0.965f, 0.075f);

            feedbackText = CreateText("Feedback", footer, "READY", 14f, Muted, FontStyles.Bold);
            Anchor(feedbackText.rectTransform, 0f, 0f, 0.7f, 1f);

            TMP_Text shortcut = CreateText("Shortcut", footer, "F10  /  HIDE", 14f, Acid, FontStyles.Bold);
            shortcut.horizontalAlignment = HorizontalAlignmentOptions.Right;
            Anchor(shortcut.rectTransform, 0.7f, 0f, 1f, 1f);
        }

        private void BuildCollapsedChip()
        {
            Button chip = CreateButton(safeAreaRoot, "DEV  /  F10", () => SetPanelVisibleInternal(true), false);
            collapsedChip = chip.gameObject;
            RectTransform rect = (RectTransform)chip.transform;
            rect.anchorMin = Vector2.one;
            rect.anchorMax = Vector2.one;
            rect.pivot = Vector2.one;
            rect.anchoredPosition = new Vector2(-16f, -18f);
            rect.sizeDelta = new Vector2(132f, 44f);
            collapsedChip.SetActive(false);
        }

        private void Execute(PanelAction action)
        {
            string result = action.Execute != null ? action.Execute() : "Command complete";
            SetFeedback(result);
            RefreshToggleVisuals();
        }

        private string ToggleInfiniteAmmo()
        {
            infiniteAmmo = !infiniteAmmo;
            ApplyDurabilityOverrides();
            return infiniteAmmo
                ? "Infinite ranged ammo enabled"
                : "Infinite ranged ammo disabled";
        }

        private string ToggleInfiniteDurability()
        {
            infiniteDurability = !infiniteDurability;
            ApplyDurabilityOverrides();
            return infiniteDurability
                ? "Infinite weapon and mod durability enabled"
                : "Infinite weapon and mod durability disabled";
        }

        private void ApplyDurabilityOverrides()
        {
            WeaponInstance[] weapons = FindObjectsByType<WeaponInstance>(
                FindObjectsInactive.Include,
                FindObjectsSortMode.None);
            foreach (WeaponInstance weapon in weapons)
            {
                if (weapon == null)
                    continue;

                bool isRanged = weapon.weaponData is RangedWeaponData;
                bool shouldLock = infiniteDurability || (infiniteAmmo && isRanged);
                weapon.EditorInfiniteDurability = shouldLock;
                if (shouldLock)
                    weapon.EditorRestoreDurability();
            }

            PlayerCharacter player = PlayerLifecycle.Instance != null
                ? PlayerLifecycle.Instance.Player
                : FindFirstObjectByType<PlayerCharacter>();
            if (player == null)
                return;

            ModManager modManager = player.GetComponent<ModManager>();
            if (modManager != null)
            {
                for (int i = 0; i < modManager.MaxActiveSlots; i++)
                    ApplyModOverride(modManager.GetActiveMod(i));
                for (int i = 0; i < modManager.MaxPassiveSlots; i++)
                    ApplyModOverride(modManager.GetPassiveMod(i));
            }

            InventoryComponent inventory = player.GetComponent<InventoryComponent>();
            if (inventory?.Slots == null)
                return;
            foreach (ModInstance mod in inventory.Slots)
                ApplyModOverride(mod);
        }

        private void ApplyModOverride(ModInstance mod)
        {
            if (mod == null)
                return;

            mod.EditorInfiniteDurability = infiniteDurability;
            if (infiniteDurability)
                mod.EditorRestoreDurability();
        }

        private void SetPanelVisibleInternal(bool visible)
        {
            panelExpanded = visible;
            panelRoot?.SetActive(visible);
            collapsedChip?.SetActive(!visible);
            UpdatePresentation();

            if (visible)
                RefreshToolScrollLayout();
        }

        private void SetFeedback(string message)
        {
            if (feedbackText != null)
                feedbackText.text = string.IsNullOrWhiteSpace(message)
                    ? "COMMAND COMPLETE"
                    : message.ToUpperInvariant();
        }

        private void RefreshToggleVisuals()
        {
            foreach (ToggleVisual toggle in toggleVisuals)
                toggle.Refresh();
        }

        private void ApplySafeArea(bool force)
        {
            Rect safeArea = Screen.safeArea;
            if (!force &&
                lastScreenWidth == Screen.width &&
                lastScreenHeight == Screen.height &&
                lastSafeArea == safeArea)
            {
                return;
            }

            lastScreenWidth = Screen.width;
            lastScreenHeight = Screen.height;
            lastSafeArea = safeArea;

            if (Screen.width <= 0 || Screen.height <= 0 || safeAreaRoot == null)
                return;

            safeAreaRoot.anchorMin = new Vector2(safeArea.xMin / Screen.width, safeArea.yMin / Screen.height);
            safeAreaRoot.anchorMax = new Vector2(safeArea.xMax / Screen.width, safeArea.yMax / Screen.height);
            safeAreaRoot.offsetMin = Vector2.zero;
            safeAreaRoot.offsetMax = Vector2.zero;
        }

        #region UI primitives

        private RectTransform CreateRect(string name, Transform parent)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.layer = 5;
            RectTransform rect = (RectTransform)go.transform;
            rect.SetParent(parent, false);
            rect.localScale = Vector3.one;
            return rect;
        }

        private Image CreateImage(string name, Transform parent, Color color)
        {
            RectTransform rect = CreateRect(name, parent);
            Image image = rect.gameObject.AddComponent<Image>();
            image.color = color;
            return image;
        }

        private TMP_Text CreateText(
            string name,
            Transform parent,
            string value,
            float size,
            Color color,
            FontStyles style = FontStyles.Normal)
        {
            RectTransform rect = CreateRect(name, parent);
            TextMeshProUGUI text = rect.gameObject.AddComponent<TextMeshProUGUI>();
            text.text = value;
            text.font = themeFont;
            text.fontSize = size;
            text.color = color;
            text.fontStyle = style;
            text.raycastTarget = false;
            text.textWrappingMode = TextWrappingModes.NoWrap;
            text.overflowMode = TextOverflowModes.Ellipsis;
            text.horizontalAlignment = HorizontalAlignmentOptions.Left;
            text.verticalAlignment = VerticalAlignmentOptions.Middle;
            return text;
        }

        private Button CreateButton(
            Transform parent,
            string label,
            Action action,
            bool compact,
            Vector2? anchorMin = null,
            Vector2? anchorMax = null)
        {
            Image image = CreateImage(label, parent, Card);
            if (anchorMin.HasValue && anchorMax.HasValue)
                SetAnchors(image.rectTransform, anchorMin.Value, anchorMax.Value);

            Button button = image.gameObject.AddComponent<Button>();
            button.targetGraphic = image;
            button.transition = Selectable.Transition.ColorTint;
            ColorBlock colors = button.colors;
            colors.normalColor = Color.white;
            colors.highlightedColor = new Color(1.4f, 1.4f, 1.4f, 1f);
            colors.pressedColor = new Color(0.7f, 0.85f, 1f, 1f);
            colors.selectedColor = Color.white;
            colors.disabledColor = new Color(1f, 1f, 1f, 0.22f);
            colors.colorMultiplier = 1f;
            colors.fadeDuration = 0.06f;
            button.colors = colors;
            // Keyboard/gamepad input continues controlling the player while this
            // mouse-operated overlay is open. Enter must not repeat the last command.
            button.navigation = new Navigation { mode = Navigation.Mode.None };
            button.onClick.AddListener(() => action?.Invoke());

            TMP_Text text = CreateText("Label", image.transform, label.ToUpperInvariant(),
                compact ? 14f : 15f, Paper, FontStyles.Bold);
            text.horizontalAlignment = HorizontalAlignmentOptions.Center;
            text.textWrappingMode = TextWrappingModes.Normal;
            Stretch(text.rectTransform);
            text.rectTransform.offsetMin = new Vector2(5f, 2f);
            text.rectTransform.offsetMax = new Vector2(-5f, -2f);
            return button;
        }

        private void AddBorder(RectTransform parent, Color color, float thickness)
        {
            AddEdge("Top", parent, color, 0f, 1f, 1f, 1f, 0f, thickness);
            AddEdge("Bottom", parent, color, 0f, 0f, 1f, 0f, 0f, thickness);
            AddEdge("Left", parent, color, 0f, 0f, 0f, 1f, thickness, 0f);
            AddEdge("Right", parent, color, 1f, 0f, 1f, 1f, thickness, 0f);
        }

        private void AddEdge(
            string name,
            RectTransform parent,
            Color color,
            float minX,
            float minY,
            float maxX,
            float maxY,
            float width,
            float height)
        {
            Image edge = CreateImage(name, parent, color);
            Anchor(edge.rectTransform, minX, minY, maxX, maxY);
            edge.rectTransform.sizeDelta = new Vector2(width, height);
            edge.raycastTarget = false;
        }

        private static void SetAnchors(RectTransform rect, Vector2 min, Vector2 max)
        {
            rect.anchorMin = min;
            rect.anchorMax = max;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
        }

        private static void Anchor(RectTransform rect, float minX, float minY, float maxX, float maxY)
        {
            SetAnchors(rect, new Vector2(minX, minY), new Vector2(maxX, maxY));
        }

        private static void Stretch(RectTransform rect) => Anchor(rect, 0f, 0f, 1f, 1f);

        #endregion

        private readonly struct PanelAction
        {
            private PanelAction(string label, Func<string> execute, bool isToggle, Func<bool> state)
            {
                Label = label;
                Execute = execute;
                IsToggle = isToggle;
                State = state;
            }

            public string Label { get; }
            public Func<string> Execute { get; }
            public bool IsToggle { get; }
            public Func<bool> State { get; }

            public static PanelAction Command(string label, Func<string> execute)
                => new(label, execute, false, null);

            public static PanelAction Toggle(string label, Func<bool> state, Func<string> execute)
                => new(label, execute, true, state);
        }

        private sealed class ToggleVisual
        {
            private readonly Button button;
            private readonly TMP_Text label;
            private readonly string baseLabel;
            private readonly Func<bool> state;

            public ToggleVisual(Button button, TMP_Text label, string baseLabel, Func<bool> state)
            {
                this.button = button;
                this.label = label;
                this.baseLabel = baseLabel;
                this.state = state;
                Refresh();
            }

            public void Refresh()
            {
                bool enabled = state?.Invoke() == true;
                if (label != null)
                {
                    label.text = $"{baseLabel}\n{(enabled ? "ON" : "OFF")}";
                    label.color = enabled ? new Color(0.025f, 0.03f, 0.028f, 1f) : Paper;
                }

                if (button?.targetGraphic is Image image)
                    image.color = enabled ? Acid : Card;
            }
        }
    }
}
#endif
