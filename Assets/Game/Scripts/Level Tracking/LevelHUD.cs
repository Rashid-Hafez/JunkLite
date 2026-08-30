using DG.Tweening;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace junklite
{
    /// <summary>
    /// Fully code-generated level HUD. Attach to any GameObject — no prefab wiring needed.
    /// Assign TimerAnchor and KillAnchor RectTransforms in the Inspector to control placement.
    /// </summary>
    public class LevelHUD : MonoBehaviour
    {
        #region Inspector

        [Header("Anchor Points  (assign RectTransforms to control placement)")]
        [Tooltip("The timer panel will be built centred on this RectTransform.")]
        [SerializeField] private RectTransform timerAnchor;
        [Tooltip("The kill counter panel will be built centred on this RectTransform.")]
        [SerializeField] private RectTransform killAnchor;

        [Header("Fonts  (optional — assign TMP Font Assets for best look)")]
        [Tooltip("Used for numbers. Orbitron works well.")]
        [SerializeField] private TMP_FontAsset displayFont;
        [Tooltip("Used for labels. Exo 2 works well.")]
        [SerializeField] private TMP_FontAsset bodyFont;

        [Header("Kill Counter")]
        [SerializeField] private string killPrefix = "☠";

        [Header("Kill Pulse")]
        [SerializeField] private bool pulseOnKill = true;
        [SerializeField] private float pulseDuration = 0.15f;
        [SerializeField] private float pulseScale = 1.25f;

        #endregion

        #region Palette

        static Color Hex(string h) { ColorUtility.TryParseHtmlString(h, out var c); return c; }

        static readonly Color C_PANEL = new Color(0.051f, 0.086f, 0.137f, 0.85f);
        static readonly Color C_ACCENT = Hex("#1AFF7A");
        static readonly Color C_TXT = Hex("#E8F4FF");
        static readonly Color C_LBL = Hex("#3A7055");

        #endregion

        #region Runtime

        LevelStatsTracker _tracker;

        TextMeshProUGUI _timerValue;
        TextMeshProUGUI _killValue;
        RectTransform _killValueRT;
        Vector3 _killBaseScale;

        float _pulseTimer;
        bool _isPulsing;
        bool _uiBuilt;

        #endregion

        #region Unity Lifecycle

        void Awake() => BuildUI();

        void Start()
        {
            if (_tracker == null)
            {
                _tracker = LevelStatsTracker.Instance;
                if (_tracker != null) Subscribe();
                else Debug.LogWarning("[LevelHUD] No LevelStatsTracker found in scene.", this);
            }
        }

        void OnEnable()
        {
            _tracker = LevelStatsTracker.Instance;
            if (_tracker != null) Subscribe();
        }

        void OnDisable()
        {
            if (_tracker != null) Unsubscribe();
        }

        void Update()
        {
            if (!_isPulsing) return;

            _pulseTimer -= Time.deltaTime;
            float t = 1f - (_pulseTimer / pulseDuration);
            float scale = t < 0.5f
                ? Mathf.Lerp(1f, pulseScale, t * 2f)
                : Mathf.Lerp(pulseScale, 1f, (t - 0.5f) * 2f);

            if (_killValueRT != null)
                _killValueRT.localScale = _killBaseScale * scale;

            if (_pulseTimer <= 0f)
            {
                _isPulsing = false;
                if (_killValueRT != null)
                    _killValueRT.localScale = _killBaseScale;
            }
        }

        #endregion

        #region Subscriptions

        void Subscribe()
        {
            _tracker.OnTimerTick += HandleTimerTick;
            _tracker.OnEnemyKilled += HandleEnemyKilled;
            _tracker.OnLevelStarted += HandleLevelStarted;
            _tracker.OnLevelCompleted += HandleLevelCompleted;
            _tracker.OnTotalEnemiesSet += HandleTotalEnemiesSet;

            RefreshTimer(_tracker.ElapsedTime);
            RefreshKills(_tracker.TotalKills);
        }

        void Unsubscribe()
        {
            _tracker.OnTimerTick -= HandleTimerTick;
            _tracker.OnEnemyKilled -= HandleEnemyKilled;
            _tracker.OnLevelStarted -= HandleLevelStarted;
            _tracker.OnLevelCompleted -= HandleLevelCompleted;
            _tracker.OnTotalEnemiesSet -= HandleTotalEnemiesSet;
        }

        #endregion

        #region Event Handlers

        void HandleTimerTick(float elapsed) => RefreshTimer(elapsed);
        void HandleLevelStarted() { RefreshTimer(0f); RefreshKills(0); }
        void HandleLevelCompleted(LevelStats s) => RefreshTimer(s.CompletionTime);
        void HandleTotalEnemiesSet(int total) => RefreshKills(_tracker.TotalKills);

        void HandleEnemyKilled(EnemyType type, int typeCount, int totalKills)
        {
            RefreshKills(totalKills);
            if (pulseOnKill && _killValueRT != null)
            {
                _pulseTimer = pulseDuration;
                _isPulsing = true;
            }
        }

        #endregion

        #region Display

        void RefreshTimer(float seconds)
        {
            if (_timerValue != null)
                _timerValue.text = LevelStatsTracker.FormatTime(seconds);
        }

        void RefreshKills(int count)
        {
            if (_killValue == null) return;
            int total = _tracker != null ? _tracker.TotalEnemies : 0;
            _killValue.text = total > 0 ? $"{count}/{total}" : count.ToString();
        }

        #endregion

        // ═══════════════════════════════════════════════════════════════════
        #region Build UI

        void BuildUI()
        {
            if (_uiBuilt) return;
            _uiBuilt = true;

            // ── Canvas ──────────────────────────────────────────────────────
            var cGO = new GameObject("HUDCanvas");
            cGO.transform.SetParent(transform, false);

            var canvas = cGO.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 10;

            var scaler = cGO.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 0.5f;

            cGO.AddComponent<GraphicRaycaster>();

            // ── Root ─────────────────────────────────────────────────────────
            var root = NewRT(cGO.transform, "Root");
            Stretch(root);

            // Use anchors if assigned, otherwise fall back to root with default corners
            var timerParent = timerAnchor != null ? timerAnchor : root;
            var killParent = killAnchor != null ? killAnchor : root;

            BuildTimerWidget(timerParent, timerAnchor == null);
            BuildKillWidget(killParent, killAnchor == null);
        }

        // ── Timer widget ──────────────────────────────────────────────────
        void BuildTimerWidget(Transform parent, bool useDefaultCorner)
        {
            var panel = NewRT(parent, "TimerPanel");
            panel.sizeDelta = new Vector2(240f, 56f);

            if (useDefaultCorner)
            {
                panel.anchorMin = panel.anchorMax = panel.pivot = new Vector2(1f, 1f);
                panel.anchoredPosition = new Vector2(-40f, -40f);
            }
            else
            {
                panel.anchorMin = panel.anchorMax = panel.pivot = new Vector2(0.5f, 0.5f);
                panel.anchoredPosition = Vector2.zero;
            }

            // Pill background
            var bg = AddImg(panel, C_PANEL);
            bg.raycastTarget = false;

            // Bottom accent line only
            var line = NewRT(panel, "BottomLine");
            line.anchorMin = new Vector2(0f, 0f); line.anchorMax = new Vector2(1f, 0f);
            line.pivot = new Vector2(0.5f, 0f);
            line.sizeDelta = new Vector2(0f, 2f);
            line.anchoredPosition = Vector2.zero;
            AddImg(line, C_ACCENT);

            // Timer value — centred
            var val = MakeTMP(panel, "Value", "0:00", 28f, C_TXT, displayFont);
            val.fontStyle = FontStyles.Bold;
            val.alignment = TextAlignmentOptions.Center;
            Stretch(val.rectTransform);
            val.rectTransform.offsetMin = new Vector2(16f, 4f);
            val.rectTransform.offsetMax = new Vector2(-16f, -4f);

            _timerValue = val;
        }

        // ── Kill counter widget ───────────────────────────────────────────
        void BuildKillWidget(Transform parent, bool useDefaultCorner)
        {
            var panel = NewRT(parent, "KillPanel");
            panel.sizeDelta = new Vector2(220f, 72f);

            if (useDefaultCorner)
            {
                // Default: top-left corner
                panel.anchorMin = panel.anchorMax = panel.pivot = new Vector2(0f, 1f);
                panel.anchoredPosition = new Vector2(40f, -40f);
            }
            else
            {
                panel.anchorMin = panel.anchorMax = panel.pivot = new Vector2(0.5f, 0.5f);
                panel.anchoredPosition = Vector2.zero;
            }

            AddImg(panel, C_PANEL);

            // Left accent bar
            var bar = NewRT(panel, "Bar");
            bar.anchorMin = bar.anchorMax = bar.pivot = new Vector2(0f, 0.5f);
            bar.sizeDelta = new Vector2(3f, 72f);
            bar.anchoredPosition = Vector2.zero;
            AddImg(bar, C_ACCENT);

            // Kill icon
            var icon = MakeTMP(panel, "Icon", killPrefix, 14f, C_ACCENT, bodyFont);
            icon.alignment = TextAlignmentOptions.Left;
            var ir = icon.rectTransform;
            ir.anchorMin = new Vector2(0f, 1f); ir.anchorMax = new Vector2(0f, 1f);
            ir.pivot = new Vector2(0f, 1f);
            ir.sizeDelta = new Vector2(30f, 20f);
            ir.anchoredPosition = new Vector2(16f, -8f);

            // "KILLS" label
            var lbl = MakeTMP(panel, "Lbl", "KILLS", 10f, C_LBL, bodyFont);
            lbl.characterSpacing = 5f;
            lbl.alignment = TextAlignmentOptions.Left;
            var lr = lbl.rectTransform;
            lr.anchorMin = new Vector2(0f, 1f); lr.anchorMax = new Vector2(1f, 1f);
            lr.pivot = new Vector2(0f, 1f);
            lr.sizeDelta = new Vector2(0f, 20f);
            lr.anchoredPosition = new Vector2(42f, -8f);

            // Kill value
            var val = MakeTMP(panel, "Value", "0", 32f, C_ACCENT, displayFont);
            val.fontStyle = FontStyles.Bold;
            val.alignment = TextAlignmentOptions.Left;
            var vr = val.rectTransform;
            vr.anchorMin = Vector2.zero; vr.anchorMax = new Vector2(1f, 1f);
            vr.offsetMin = new Vector2(16f, 0f);
            vr.offsetMax = new Vector2(-16f, -22f);

            _killValue = val;
            _killValueRT = val.rectTransform;
            _killBaseScale = _killValueRT.localScale;
        }

        #endregion

        #region Low-Level Helpers

        RectTransform NewRT(Transform parent, string name)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            return go.GetComponent<RectTransform>();
        }

        Image AddImg(RectTransform rt, Color color)
        {
            var img = rt.gameObject.AddComponent<Image>();
            img.color = color;
            return img;
        }

        TextMeshProUGUI MakeTMP(Transform parent, string name, string text,
                                 float size, Color color, TMP_FontAsset font)
        {
            var rt = NewRT(parent, name);
            var tmp = rt.gameObject.AddComponent<TextMeshProUGUI>();
            tmp.text = text;
            tmp.fontSize = size;
            tmp.color = color;
            if (font != null) tmp.font = font;
            return tmp;
        }

        void Stretch(RectTransform rt)
        {
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = rt.offsetMax = Vector2.zero;
        }

        #endregion
    }
}