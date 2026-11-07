using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace junklite
{
    /// <summary>Run statistics grouped above gameplay, clear of the developer overlays.</summary>
    public class LevelHUD : MonoBehaviour
    {
        [Header("Layout")]
        [Tooltip("Opt in to separate scene positions. By default both statistics share a safe-area, top-center strip.")]
        [SerializeField] private bool useCustomAnchors;
        [Tooltip("Optional timer position with Use Custom Anchors. Existing graphics are replaced at runtime.")]
        [SerializeField] private RectTransform timerAnchor;
        [Tooltip("Optional kills position with Use Custom Anchors. Existing graphics are replaced at runtime.")]
        [SerializeField] private RectTransform killAnchor;

        [Header("Kill Feedback")]
        [SerializeField] private bool pulseOnKill = true;
        [Min(0.01f)] [SerializeField] private float pulseDuration = 0.18f;
        [Range(1f, 1.1f)] [SerializeField] private float pulseScale = 1.06f;

        private static readonly Color Frame = new(0.032f, 0.045f, 0.087f, 0.94f);
        private static readonly Color Cyan = new(0.24f, 0.91f, 0.98f, 1f);
        private static readonly Color Magenta = new(0.86f, 0.25f, 0.89f, 1f);
        private static readonly Color Paper = new(0.9f, 0.94f, 1f, 1f);
        private static readonly Color Line = new(0.18f, 0.27f, 0.38f, 0.75f);
        private const float CardWidth = 196f;
        private const float CardHeight = 80f;
        private const float CardGap = 8f;
        private const float StripWidth = CardWidth * 2f + CardGap;

        private readonly List<Graphic> replacedGraphics = new();
        private LevelStatsTracker tracker;
        private RectTransform safeAreaRoot;
        private RectTransform strip;
        private RectTransform timerPanel;
        private RectTransform killPanel;
        private TextMeshProUGUI timerValue;
        private TextMeshProUGUI killValue;
        private CanvasGroup presentation;
        private float pulseRemaining;
        private int displayedSecond = -1;
        private int lastScreenWidth;
        private int lastScreenHeight;
        private Rect lastSafeArea;

        private void Awake() => BuildUI();

        private void OnEnable()
        {
            if (presentation != null) presentation.gameObject.SetActive(true);
            SetPanelsActive(true);
            BindTracker();
        }

        private void Start()
        {
            BindTracker();
            if (tracker == null)
                Debug.LogWarning("[LevelHUD] No LevelStatsTracker found in scene.", this);
        }

        private void OnDisable()
        {
            Unsubscribe();
            ResetPulse();
            SetPanelsActive(false);
            if (presentation != null) presentation.gameObject.SetActive(false);
        }

        private void OnDestroy()
        {
            // Custom anchors can live outside this component's hierarchy.
            if (timerPanel != null) Destroy(timerPanel.gameObject);
            if (killPanel != null) Destroy(killPanel.gameObject);
            if (presentation != null) Destroy(presentation.gameObject);
            foreach (Graphic graphic in replacedGraphics)
                if (graphic != null) graphic.enabled = true;
        }

        private void LateUpdate()
        {
            ApplySafeArea();
            // Keep this scene-local HUD behind the game's modal screens.
            bool visible = GameManager.Instance == null || GameManager.Instance.IsPlaying;
            presentation.alpha = visible ? 1f : 0f;
            if (useCustomAnchors) SetPanelsActive(visible);

            if (pulseRemaining <= 0f) return;
            pulseRemaining = Mathf.Max(0f, pulseRemaining - Time.deltaTime);
            float progress = 1f - pulseRemaining / Mathf.Max(0.01f, pulseDuration);
            float pulse = Mathf.Sin(progress * Mathf.PI);
            // Clamp legacy scene values too, so feedback stays inside the card.
            killValue.rectTransform.localScale = Vector3.one *
                Mathf.Lerp(1f, Mathf.Clamp(pulseScale, 1f, 1.1f), pulse);
            killValue.color = Color.Lerp(Paper, Magenta, pulse * 0.65f);
            if (pulseRemaining <= 0f) ResetPulse();
        }

        private void BindTracker()
        {
            LevelStatsTracker current = LevelStatsTracker.Instance;
            if (tracker != current)
            {
                Unsubscribe();
                tracker = current;
                if (tracker != null)
                {
                    tracker.OnTimerTick += RefreshTimer;
                    tracker.OnEnemyKilled += HandleEnemyKilled;
                    tracker.OnLevelStarted += HandleLevelStarted;
                    tracker.OnLevelCompleted += HandleLevelCompleted;
                    tracker.OnTotalEnemiesSet += HandleTotalEnemiesSet;
                }
            }
            if (tracker == null) return;
            RefreshTimer(tracker.ElapsedTime);
            RefreshKills(tracker.TotalKills);
        }

        private void Unsubscribe()
        {
            if (tracker != null)
            {
                tracker.OnTimerTick -= RefreshTimer;
                tracker.OnEnemyKilled -= HandleEnemyKilled;
                tracker.OnLevelStarted -= HandleLevelStarted;
                tracker.OnLevelCompleted -= HandleLevelCompleted;
                tracker.OnTotalEnemiesSet -= HandleTotalEnemiesSet;
            }
            tracker = null;
        }

        private void HandleLevelStarted()
        {
            ResetPulse();
            RefreshTimer(0f);
            RefreshKills(0);
        }

        private void HandleLevelCompleted(LevelStats stats) => RefreshTimer(stats.CompletionTime);
        private void HandleTotalEnemiesSet(int total) => RefreshKills(tracker.TotalKills);

        private void HandleEnemyKilled(EnemyType type, int typeCount, int totalKills)
        {
            RefreshKills(totalKills);
            if (pulseOnKill) pulseRemaining = Mathf.Max(0.01f, pulseDuration);
        }

        private void ResetPulse()
        {
            pulseRemaining = 0f;
            if (killValue == null) return;
            killValue.rectTransform.localScale = Vector3.one;
            killValue.color = Paper;
        }

        private void RefreshTimer(float seconds)
        {
            int elapsedSecond = Mathf.Max(0, Mathf.FloorToInt(seconds));
            if (timerValue == null || elapsedSecond == displayedSecond) return;
            displayedSecond = elapsedSecond;
            // Fixed digit advances prevent horizontal jitter as the seconds change.
            timerValue.SetText("<mspace=0.65em>{0:00}:{1:00}</mspace>", elapsedSecond / 60, elapsedSecond % 60);
        }

        private void RefreshKills(int count)
        {
            if (killValue == null) return;
            int total = tracker != null ? tracker.TotalEnemies : 0;
            if (total > 0)
                killValue.SetText("{0}<size=62%><color=#829CBD> / {1}</color></size>", count, total);
            else
                killValue.SetText("{0}", count);
        }

        private void BuildUI()
        {
            if (safeAreaRoot != null) return;
            // Legacy anchors contain placeholder text/icons as well as placement data.
            // Retire only their graphics, preserving authored transforms and scene files.
            ReplaceAnchorGraphics(timerAnchor);
            ReplaceAnchorGraphics(killAnchor);

            var canvasRect = NewRect(transform, "Level Stats Canvas");
            // LevelHUD is nested under a Canvas in authored scenes. A nested Canvas
            // keeps a local 100x100 rect and its CanvasScaler does not run. Detach
            // before adding Canvas so it owns screen coordinates and scaling.
            // Parenting first preserves the owner's scene; OnDisable/OnDestroy
            // explicitly manage this scene-local runtime root.
            canvasRect.SetParent(null, false);
            var canvas = canvasRect.gameObject.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.overrideSorting = true;
            canvas.sortingOrder = 10;
            presentation = canvasRect.gameObject.AddComponent<CanvasGroup>();
            presentation.interactable = false;
            presentation.blocksRaycasts = false;

            var scaler = canvasRect.gameObject.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 0.5f;

            safeAreaRoot = NewRect(canvasRect, "Safe Area");
            Stretch(safeAreaRoot);
            strip = NewRect(safeAreaRoot, "Run Statistics");
            strip.anchorMin = strip.anchorMax = strip.pivot = new Vector2(0.5f, 1f);
            strip.anchoredPosition = new Vector2(0f, -32f);
            strip.sizeDelta = new Vector2(StripWidth, CardHeight);

            timerPanel = BuildCard("Timer", null, Cyan, timerAnchor, 0f, out timerValue);
            killPanel = BuildCard("Kills", "KILLS", Magenta, killAnchor, CardWidth + CardGap, out killValue);
            RefreshTimer(0f);
            RefreshKills(0);
            ApplySafeArea();
        }

        private RectTransform BuildCard(string name, string label, Color accent,
            RectTransform customAnchor, float x, out TextMeshProUGUI value)
        {
            bool custom = useCustomAnchors && customAnchor != null;
            RectTransform panel = NewRect(custom ? customAnchor : strip, name);
            panel.sizeDelta = new Vector2(CardWidth, CardHeight);
            if (custom)
                panel.anchorMin = panel.anchorMax = panel.pivot = new Vector2(0.5f, 0.5f);
            else
            {
                panel.anchorMin = panel.anchorMax = panel.pivot = new Vector2(0f, 1f);
                panel.anchoredPosition = new Vector2(x, 0f);
            }

            AddImage(panel, Frame);
            Rule(panel, "Top Border", 0f, 0f, CardWidth, 1f, Line);
            Rule(panel, "Bottom Border", 0f, CardHeight - 1f, CardWidth, 1f, Line);
            Rule(panel, "Left Border", 0f, 0f, 1f, CardHeight, Line);
            Rule(panel, "Right Border", CardWidth - 1f, 0f, 1f, CardHeight, Line);
            Rule(panel, "Signal Rail", 0f, 0f, 48f, 2f, accent);
            Rule(panel, "Signal Corner", 0f, 0f, 2f, 18f, accent);

            if (label == null)
            {
                RectTransform iconRect = NewRect(panel, "Clock Icon");
                Place(iconRect, 16f, 10f, 20f, 20f);
                var icon = iconRect.gameObject.AddComponent<LevelHudClockIcon>();
                icon.color = accent;
                icon.raycastTarget = false;
            }
            else
            {
                TextMeshProUGUI title = MakeText(panel, "Label", label, 20f, accent, true);
                Place(title.rectTransform, 16f, 8f, CardWidth - 32f, 24f);
                title.characterSpacing = 1.5f;
            }

            value = MakeText(panel, "Value", "0", 30f, Paper, false);
            Place(value.rectTransform, 16f, 31f, CardWidth - 32f, 40f);
            value.fontStyle = FontStyles.Bold;
            // No AutoSize: the timer (monospaced 00:00) and kill count fit at 30pt, and AutoSize
            // would re-run its fitting passes on every update. Ellipsis covers any overflow.
            return panel;
        }

        private void ApplySafeArea()
        {
            int width = Screen.width;
            int height = Screen.height;
            Rect safe = Screen.safeArea;
            if (width <= 0 || height <= 0) return;
            if (width != lastScreenWidth || height != lastScreenHeight || safe != lastSafeArea)
            {
                lastScreenWidth = width;
                lastScreenHeight = height;
                lastSafeArea = safe;
                safeAreaRoot.anchorMin = new Vector2(safe.xMin / width, safe.yMin / height);
                safeAreaRoot.anchorMax = new Vector2(safe.xMax / width, safe.yMax / height);
                safeAreaRoot.offsetMin = safeAreaRoot.offsetMax = Vector2.zero;
            }
            float scale = Mathf.Clamp01((safeAreaRoot.rect.width - 48f) / StripWidth);
            strip.localScale = Vector3.one * scale;
        }

        private void ReplaceAnchorGraphics(RectTransform anchor)
        {
            if (anchor == null) return;
            foreach (Graphic graphic in anchor.GetComponentsInChildren<Graphic>(true))
            {
                if (!graphic.enabled) continue;
                replacedGraphics.Add(graphic);
                graphic.enabled = false;
            }
        }

        private void SetPanelsActive(bool active)
        {
            if (timerPanel != null) timerPanel.gameObject.SetActive(active);
            if (killPanel != null) killPanel.gameObject.SetActive(active);
        }

        private static RectTransform NewRect(Transform parent, string name)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.layer = parent.gameObject.layer;
            go.transform.SetParent(parent, false);
            return (RectTransform)go.transform;
        }

        private static void AddImage(RectTransform rect, Color color)
        {
            Image image = rect.gameObject.AddComponent<Image>();
            image.color = color;
            image.raycastTarget = false;
        }

        private static void Rule(Transform parent, string name, float x, float y, float width, float height, Color color)
        {
            RectTransform rect = NewRect(parent, name);
            Place(rect, x, y, width, height);
            AddImage(rect, color);
        }

        private static TextMeshProUGUI MakeText(Transform parent, string name, string text,
            float size, Color color, bool title)
        {
            var tmp = NewRect(parent, name).gameObject.AddComponent<TextMeshProUGUI>();
            if (title) UIFonts.ApplyHudTitle(tmp);
            else UIFonts.ApplyHudBody(tmp);
            tmp.text = text;
            tmp.fontSize = size;
            tmp.color = color;
            tmp.raycastTarget = false;
            tmp.alignment = TextAlignmentOptions.MidlineLeft;
            tmp.textWrappingMode = TextWrappingModes.NoWrap;
            tmp.overflowMode = TextOverflowModes.Ellipsis;
            return tmp;
        }

        private static void Place(RectTransform rect, float x, float y, float width, float height)
        {
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0f, 1f);
            rect.anchoredPosition = new Vector2(x, -y);
            rect.sizeDelta = new Vector2(width, height);
        }

        private static void Stretch(RectTransform rect)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = rect.offsetMax = Vector2.zero;
        }
    }

    /// <summary>A small UI clock drawn in one mesh, with no font glyph or texture dependency.</summary>
    [AddComponentMenu("")]
    [RequireComponent(typeof(CanvasRenderer))]
    internal sealed class LevelHudClockIcon : MaskableGraphic
    {
        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();
            Rect rect = GetPixelAdjustedRect();
            float size = Mathf.Min(rect.width, rect.height);
            if (size <= 0f) return;

            Vector2 center = rect.center;
            float outer = size * 0.46f;
            float inner = outer - size * 0.09f;
            const int segments = 32;
            for (int i = 0; i < segments; i++)
            {
                float angle = i * Mathf.PI * 2f / segments;
                float nextAngle = (i + 1) * Mathf.PI * 2f / segments;
                Vector2 from = new(Mathf.Cos(angle), Mathf.Sin(angle));
                Vector2 to = new(Mathf.Cos(nextAngle), Mathf.Sin(nextAngle));
                AddQuad(vh, center + from * outer, center + from * inner,
                    center + to * inner, center + to * outer);
            }

            // Fixed hands at three o'clock remain readable at small HUD sizes.
            float halfStroke = size * 0.045f;
            AddQuad(vh, center + new Vector2(-halfStroke, -halfStroke),
                center + new Vector2(-halfStroke, size * 0.28f),
                center + new Vector2(halfStroke, size * 0.28f),
                center + new Vector2(halfStroke, -halfStroke));
            AddQuad(vh, center + new Vector2(-halfStroke, -halfStroke),
                center + new Vector2(-halfStroke, halfStroke),
                center + new Vector2(size * 0.22f, halfStroke),
                center + new Vector2(size * 0.22f, -halfStroke));
        }

        private void AddQuad(VertexHelper vh, Vector2 a, Vector2 b, Vector2 c, Vector2 d)
        {
            int first = vh.currentVertCount;
            vh.AddVert(a, color, Vector2.zero);
            vh.AddVert(b, color, Vector2.zero);
            vh.AddVert(c, color, Vector2.zero);
            vh.AddVert(d, color, Vector2.zero);
            vh.AddTriangle(first, first + 1, first + 2);
            vh.AddTriangle(first, first + 2, first + 3);
        }
    }
}
