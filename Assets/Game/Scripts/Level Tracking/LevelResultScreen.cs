using System.Collections;
using System.Collections.Generic;
using DG.Tweening;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace junklite
{
    /// <summary>
    /// Fully code-generated level results screen.
    /// Attach to any GameObject — no prefab or Inspector wiring required.
    ///
    /// Optional: assign DisplayFont (e.g. Orbitron) and BodyFont (e.g. Exo 2)
    /// TMP Font Assets in the Inspector for the best look.
    /// </summary>
    public class LevelResultsScreen : MonoBehaviour
    {
        #region Inspector

        [Header("Navigation")]
        [SerializeField] private string continueSceneName = "";
        [SerializeField] private float showDelay = 1.2f;

        [Header("Fonts  (optional — assign TMP Font Assets for best look)")]
        [Tooltip("Grade letter, numbers, button labels. Orbitron works well.")]
        [SerializeField] private TMP_FontAsset displayFont;
        [Tooltip("Panel labels and stat names. Exo 2 works well.")]
        [SerializeField] private TMP_FontAsset bodyFont;

        [Header("Debug")]
        [Tooltip("Press in Play Mode to preview the animation with fake data.")]
        [SerializeField] private KeyCode debugKey = KeyCode.F8;

        [Header("Background")]
        [SerializeField] private Image fullScreenBackground;

        #endregion

        #region Palette

        static Color Hex(string h) { ColorUtility.TryParseHtmlString(h, out var c); return c; }

        static readonly Color C_BG = Hex("#060910");
        static readonly Color C_PANEL = Hex("#0D1623");
        static readonly Color C_ACCENT = Hex("#1AFF7A");
        static readonly Color C_TXT_PRI = Hex("#E8F4FF");
        static readonly Color C_TXT_MUT = Hex("#7A9AB0");
        static readonly Color C_TXT_LBL = Hex("#3A7055");
        static readonly Color C_CORNER = new Color(0.102f, 1f, 0.478f, 0.45f);
        static readonly Color C_TOTAL_BG = new Color(0.102f, 1f, 0.478f, 0.07f);

        Color GradeColor(PerformanceGrade g) => g switch
        {
            PerformanceGrade.S => Hex("#FFD700"),
            PerformanceGrade.A => Hex("#1AFF7A"),
            PerformanceGrade.B => Hex("#4DABFF"),
            PerformanceGrade.C => Hex("#FF8C42"),
            _ => Hex("#FF4242"),
        };

        #endregion

        #region Runtime References

        LevelStatsTracker _tracker;
        LevelStats _pendingStats;

        CanvasGroup _rootCG;
        CanvasGroup _headerCG;
        RectTransform _headerRT;
        RectTransform _underlineRT;

        // [0] = Time row, [1] = Total Kills row
        (RectTransform rt, CanvasGroup cg, TMP_Text val)[] _statRows;

        CanvasGroup _gradeFrameCG;
        Image _gradeGlowImg;       // tinted with grade color
        TMP_Text _gradeLetter;
        RectTransform _gradeLetterRT;
        CanvasGroup _gradeSubCG;

        // kill rows rebuilt on every show
        Transform _killListParent;
        readonly List<(RectTransform rt, CanvasGroup cg, TMP_Text cnt, int killCount)> _killRows = new();
        (RectTransform rt, CanvasGroup cg, TMP_Text val) _totalRow;

        CanvasGroup _buttonsCG;
        RectTransform _buttonsRT;

        // background beam objects (so we can skip rebuilding them on re-show)
        bool _uiBuilt;

        // GameObject to enable/disable results screen
        private GameObject cGO;

        #endregion

        #region Unity Lifecycle

        void Awake() => BuildUI();

        void Start()
        {
            // Safety net: re-subscribe if OnEnable fired before LevelStatsTracker.Awake
            if (_tracker == null)
            {
                _tracker = LevelStatsTracker.Instance;
                if (_tracker != null) _tracker.OnLevelCompleted += HandleCompleted;
                else Debug.LogWarning("[LevelResultsScreen] LevelStatsTracker not found in scene.");
            }
        }

        void OnEnable()
        {
            _tracker = LevelStatsTracker.Instance;
            if (_tracker != null) _tracker.OnLevelCompleted += HandleCompleted;
        }

        void OnDisable()
        {
            if (_tracker != null) _tracker.OnLevelCompleted -= HandleCompleted;
        }

        void Update()
        {
#if UNITY_EDITOR
            if (Input.GetKeyDown(debugKey)) TestShow();
#endif
        }

        #endregion

        #region Debug / Test

        [ContextMenu("Test Results Screen")]
        void TestShow()
        {
            cGO.SetActive(true);
            var fakeKills = new Dictionary<EnemyType, int>
            {
                { EnemyType.FlyingDummy, 12 },
            };

            var fakeStats = new LevelStats(
                time: 143f,
                totalKills: 12,
                killsByType: fakeKills,
                grade: PerformanceGrade.A
            );

            StopAllCoroutines();
            DOTween.KillAll();
            Time.timeScale = 0f;
            Populate(fakeStats);
            Animate(fakeStats);
        }

        #endregion

        #region Entry Point

        void HandleCompleted(LevelStats stats)
        {
            _pendingStats = stats;
            StartCoroutine(DelayedShow());
        }

        IEnumerator DelayedShow()
        {
            yield return new WaitForSecondsRealtime(showDelay);
            Time.timeScale = 0f;
            cGO.SetActive(true);
            Populate(_pendingStats);
            Animate(_pendingStats);
        }

        #endregion

        // ═══════════════════════════════════════════════════════════════════
        #region Build UI

        void BuildUI()
        {
            if (_uiBuilt) return;
            _uiBuilt = true;

            // ── Canvas ──────────────────────────────────────────────────────
            cGO = new GameObject("ResultsCanvas");
            cGO.transform.SetParent(transform, false);

            var canvas = cGO.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 100;

            var scaler = cGO.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 0.5f;

            cGO.AddComponent<GraphicRaycaster>();

           

            // ── Root ────────────────────────────────────────────────────────
            var root = NewRT(cGO.transform, "Root");
            Stretch(root);
            _rootCG = AddCG(root, 0f);
            AddImg(root, new Color(0f, 0f, 0f, 0f));

            // ── Background layers ────────────────────────────────────────────
            BuildBackground(root);

            // ── Screen corner decorations ────────────────────────────────────
            AddScreenCorners(root, 40f, 64f, 2f, C_CORNER);

            // ── Content container — 1376 × 720, centred ─────────────────────
            var ct = NewRT(root, "Content");
            Centre(ct, new Vector2(1376f, 720f), Vector2.zero);

            // ── Sections ─────────────────────────────────────────────────────
            BuildHeader(ct);
            BuildDivider(ct);
            BuildStatsPanel(ct);
            BuildGradePanel(ct);
            BuildKillsPanel(ct);
            BuildButtonBar(ct);

            cGO.SetActive(false);
        }

        // ── Background ───────────────────────────────────────────────────────
        void BuildBackground(RectTransform root)
        {
            var bgRoot = NewRT(root, "BG");
            Stretch(bgRoot);

            // Grid lines
            AddGridLines(bgRoot);

            // Diagonal light beams — two large rotated rectangles
            // These simulate the "spotlight from top-left" effect common in game UIs
            AddBeam(bgRoot, "Beam1", new Vector2(-560f, 80f), new Vector2(180f, 2200f), 18f, 0.028f);
            AddBeam(bgRoot, "Beam2", new Vector2(-200f, 80f), new Vector2(80f, 2200f), 18f, 0.014f);
            AddBeam(bgRoot, "Beam3", new Vector2(820f, -100f), new Vector2(140f, 2200f), -14f, 0.018f);

            // Horizontal glow line — thin accent stripe across middle of screen
            var hline = NewRT(bgRoot, "HGlow");
            hline.anchorMin = hline.anchorMax = hline.pivot = new Vector2(0.5f, 0.5f);
            hline.sizeDelta = new Vector2(1920f, 1f);
            hline.anchoredPosition = new Vector2(0f, 60f);
            AddImg(hline, new Color(C_ACCENT.r, C_ACCENT.g, C_ACCENT.b, 0.06f));

            // Vignette — four dark edge panels to darken the corners
            AddVignettePanel(bgRoot, new Vector2(0f, 0.5f), new Vector2(400f, 0f), new Vector2(-200f, 0f));
            AddVignettePanel(bgRoot, new Vector2(1f, 0.5f), new Vector2(400f, 0f), new Vector2(200f, 0f));
            AddVignettePanel(bgRoot, new Vector2(0.5f, 0f), new Vector2(0f, 300f), new Vector2(0f, -150f));
            AddVignettePanel(bgRoot, new Vector2(0.5f, 1f), new Vector2(0f, 300f), new Vector2(0f, 150f));
        }

        void AddBeam(RectTransform parent, string name, Vector2 pos, Vector2 size, float angle, float alpha)
        {
            var rt = NewRT(parent, name);
            rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = size;
            rt.anchoredPosition = pos;
            rt.localRotation = Quaternion.Euler(0f, 0f, angle);
            AddImg(rt, new Color(C_ACCENT.r, C_ACCENT.g, C_ACCENT.b, alpha));
        }

        void AddVignettePanel(RectTransform parent, Vector2 anchor, Vector2 extra, Vector2 offset)
        {
            var rt = NewRT(parent, "Vignette");
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.sizeDelta = extra;
            rt.anchoredPosition = offset;
            AddImg(rt, new Color(0f, 0f, 0f, 0.35f));
        }

        void AddGridLines(RectTransform parent)
        {
            Color lineColor = new Color(0.1f, 1f, 0.47f, 0.022f);
            var g = NewRT(parent, "Grid");
            Stretch(g);

            for (int i = 0; i <= 18; i++)
            {
                var l = NewRT(g, $"H{i}");
                l.anchorMin = new Vector2(0f, 0.5f); l.anchorMax = new Vector2(1f, 0.5f);
                l.pivot = new Vector2(0.5f, 0.5f);
                l.sizeDelta = new Vector2(0f, 1f);
                l.anchoredPosition = new Vector2(0f, -540f + i * 60f);
                AddImg(l, lineColor);
            }
            for (int i = 0; i <= 32; i++)
            {
                var l = NewRT(g, $"V{i}");
                l.anchorMin = new Vector2(0.5f, 0f); l.anchorMax = new Vector2(0.5f, 1f);
                l.pivot = new Vector2(0.5f, 0.5f);
                l.sizeDelta = new Vector2(1f, 0f);
                l.anchoredPosition = new Vector2(-960f + i * 60f, 0f);
                AddImg(l, lineColor);
            }
        }

        // ── Header ───────────────────────────────────────────────────────────
        void BuildHeader(RectTransform ct)
        {
            var h = NewRT(ct, "Header");
            h.anchorMin = h.anchorMax = h.pivot = new Vector2(0.5f, 1f);
            h.sizeDelta = new Vector2(1376f, 155f);
            h.anchoredPosition = Vector2.zero;
            _headerRT = h;
            _headerCG = AddCG(h, 0f);

            // "Mission Complete" sub-label
            var sub = MakeTMP(h, "Sub", "Mission Complete", 14f, C_TXT_LBL, bodyFont);
            sub.characterSpacing = 10f;
            sub.alignment = TextAlignmentOptions.Center;
            Stretch(sub.rectTransform);
            sub.rectTransform.offsetMin = new Vector2(0f, 120f);
            sub.rectTransform.offsetMax = new Vector2(0f, -4f);

            // "Level Results" title
            var ttl = MakeTMP(h, "Title", "Level  Results", 62f, Color.white, displayFont);
            ttl.fontStyle = FontStyles.Bold;
            ttl.characterSpacing = 10f;
            ttl.alignment = TextAlignmentOptions.Center;
            Stretch(ttl.rectTransform);
            ttl.rectTransform.offsetMin = new Vector2(0f, 20f);
            ttl.rectTransform.offsetMax = new Vector2(0f, 0f);

            // Underline (starts width=0, expands in animation)
            _underlineRT = NewRT(h, "Underline");
            _underlineRT.anchorMin = _underlineRT.anchorMax = _underlineRT.pivot = new Vector2(0.5f, 0f);
            _underlineRT.sizeDelta = new Vector2(0f, 2f);
            _underlineRT.anchoredPosition = Vector2.zero;
            AddImg(_underlineRT, C_ACCENT);
        }

        // ── Divider between header and panels ────────────────────────────────
        void BuildDivider(RectTransform ct)
        {
            var line = NewRT(ct, "Divider");
            line.anchorMin = line.anchorMax = line.pivot = new Vector2(0.5f, 1f);
            line.sizeDelta = new Vector2(1376f, 1f);
            line.anchoredPosition = new Vector2(0f, -134f);
            AddImg(line, new Color(C_ACCENT.r, C_ACCENT.g, C_ACCENT.b, 0.15f));
        }

        // ── Stat panel (left) ─────────────────────────────────────────────────
        void BuildStatsPanel(RectTransform ct)
        {
            var p = NewRT(ct, "StatsPanel");
            Centre(p, new Vector2(480f, 300f), new Vector2(-448f, -20f));

            AddPanelLabel(p, "Performance", Vector2.zero, new Vector2(0f, 1f));

            _statRows = new (RectTransform, CanvasGroup, TMP_Text)[2];
            _statRows[0] = MakeStatRow(p, "Time", -26f);
            _statRows[1] = MakeStatRow(p, "Total Kills", -114f);
        }

        (RectTransform rt, CanvasGroup cg, TMP_Text val) MakeStatRow(RectTransform parent, string label, float y)
        {
            var row = NewRT(parent, "SR_" + label);
            row.anchorMin = row.anchorMax = row.pivot = new Vector2(0f, 1f);
            row.sizeDelta = new Vector2(480f, 74f);
            row.anchoredPosition = new Vector2(0f, y);

            AddImg(row, C_PANEL);

            // Left green accent bar
            var bar = NewRT(row, "Bar");
            bar.anchorMin = bar.anchorMax = bar.pivot = new Vector2(0f, 0.5f);
            bar.sizeDelta = new Vector2(3f, 74f);
            bar.anchoredPosition = Vector2.zero;
            AddImg(bar, C_ACCENT);

            // Label
            var lbl = MakeTMP(row, "Lbl", label.ToUpper(), 12f, C_TXT_MUT, bodyFont);
            lbl.characterSpacing = 3f;
            lbl.alignment = TextAlignmentOptions.Left;
            var lr = lbl.rectTransform;
            lr.anchorMin = new Vector2(0f, 0f); lr.anchorMax = new Vector2(0.55f, 1f);
            lr.offsetMin = new Vector2(22f, 0f); lr.offsetMax = Vector2.zero;

            // Value
            var val = MakeTMP(row, "Val", "—", 24f, C_TXT_PRI, displayFont);
            val.fontStyle = FontStyles.Bold;
            val.alignment = TextAlignmentOptions.Right;
            var vr = val.rectTransform;
            vr.anchorMin = new Vector2(0.5f, 0f); vr.anchorMax = new Vector2(1f, 1f);
            vr.offsetMin = Vector2.zero; vr.offsetMax = new Vector2(-22f, 0f);

            var cg = AddCG(row, 0f);
            return (row, cg, val);
        }

        // ── Grade panel (centre) ──────────────────────────────────────────────
        void BuildGradePanel(RectTransform ct)
        {
            var p = NewRT(ct, "GradePanel");
            Centre(p, new Vector2(310f, 420f), new Vector2(0f, -20f));

            AddPanelLabel(p, "Rating", Vector2.zero, new Vector2(0.5f, 1f),
                          TextAlignmentOptions.Center);

            // Glow plate — sits behind the frame, tinted with grade colour at runtime
            var glow = NewRT(p, "GradeGlow");
            glow.anchorMin = glow.anchorMax = glow.pivot = new Vector2(0.5f, 1f);
            glow.sizeDelta = new Vector2(370f, 370f);
            glow.anchoredPosition = new Vector2(0f, -10f);
            _gradeGlowImg = AddImg(glow, Color.clear);

            // Frame
            var frame = NewRT(p, "Frame");
            frame.anchorMin = frame.anchorMax = frame.pivot = new Vector2(0.5f, 1f);
            frame.sizeDelta = new Vector2(290f, 290f);
            frame.anchoredPosition = new Vector2(0f, -26f);
            AddImg(frame, C_PANEL);
            _gradeFrameCG = AddCG(frame, 0f);

            // Corner brackets on frame
            AddCornerBrackets(frame, 22f, 2.5f, C_ACCENT);

            // Grade letter
            var gl = MakeTMP(frame, "Grade", "A", 158f, C_ACCENT, displayFont);
            gl.fontStyle = FontStyles.Bold;
            gl.alignment = TextAlignmentOptions.Center;
            _gradeLetter = gl;
            _gradeLetterRT = gl.rectTransform;
            Stretch(_gradeLetterRT);

            // "Performance Grade" sub label
            var subRT = NewRT(p, "GradeSubRT");
            subRT.anchorMin = subRT.anchorMax = subRT.pivot = new Vector2(0.5f, 1f);
            subRT.sizeDelta = new Vector2(280f, 24f);
            subRT.anchoredPosition = new Vector2(0f, -326f);
            _gradeSubCG = AddCG(subRT, 0f);

            var subTxt = MakeTMP(subRT, "Sub", "Performance Grade", 11f, C_TXT_LBL, bodyFont);
            subTxt.characterSpacing = 4f;
            subTxt.alignment = TextAlignmentOptions.Center;
            Stretch(subTxt.rectTransform);
        }

        // ── Kill panel (right) ────────────────────────────────────────────────
        void BuildKillsPanel(RectTransform ct)
        {
            var p = NewRT(ct, "KillsPanel");
            p.anchorMin = p.anchorMax = p.pivot = new Vector2(0.5f, 1f);
            p.sizeDelta = new Vector2(480f, 420f);
            p.anchoredPosition = new Vector2(448f, 160f);

            AddPanelLabel(p, "Kill Breakdown", Vector2.zero, new Vector2(0f, 1f));
            _killListParent = p.transform;

            // Total row — repositioned in Populate()
            var totalRow = NewRT(p, "TotalRow");
            totalRow.anchorMin = totalRow.anchorMax = totalRow.pivot = new Vector2(0f, 1f);
            totalRow.sizeDelta = new Vector2(480f, 74f);
            totalRow.anchoredPosition = new Vector2(0f, -26f);
            AddImg(totalRow, C_TOTAL_BG);

            var bar = NewRT(totalRow, "Bar");
            bar.anchorMin = bar.anchorMax = bar.pivot = new Vector2(1f, 0.5f);
            bar.sizeDelta = new Vector2(3f, 74f);
            bar.anchoredPosition = Vector2.zero;
            AddImg(bar, C_ACCENT);

            var lbl = MakeTMP(totalRow, "Lbl", "TOTAL KILLS", 13f, Hex("#C8E8D8"), bodyFont);
            lbl.fontStyle = FontStyles.Bold;
            lbl.characterSpacing = 3f;
            lbl.alignment = TextAlignmentOptions.Left;
            var lr = lbl.rectTransform;
            lr.anchorMin = new Vector2(0f, 0f); lr.anchorMax = new Vector2(0.6f, 1f);
            lr.offsetMin = new Vector2(22f, 0f); lr.offsetMax = Vector2.zero;

            var val = MakeTMP(totalRow, "Val", "0", 28f, C_ACCENT, displayFont);
            val.fontStyle = FontStyles.Bold;
            val.alignment = TextAlignmentOptions.Right;
            var vr = val.rectTransform;
            vr.anchorMin = new Vector2(0.55f, 0f); vr.anchorMax = new Vector2(1f, 1f);
            vr.offsetMin = Vector2.zero; vr.offsetMax = new Vector2(-22f, 0f);

            var cg = AddCG(totalRow, 0f);
            _totalRow = (totalRow, cg, val);
        }

        // ── Button bar (bottom) ───────────────────────────────────────────────
        void BuildButtonBar(RectTransform ct)
        {
            var b = NewRT(ct, "Buttons");
            b.anchorMin = b.anchorMax = b.pivot = new Vector2(0.5f, 0f);
            b.sizeDelta = new Vector2(460f, 56f);
            b.anchoredPosition = Vector2.zero;
            _buttonsRT = b;
            _buttonsCG = AddCG(b, 0f);

            var retry = MakeButton(b, "Retry", "Retry", new Vector2(-120f, 0f), secondary: true);
            var cont = MakeButton(b, "Continue", "Continue", new Vector2(120f, 0f), secondary: false);

            retry.onClick.AddListener(() =>
            {
                Time.timeScale = 1f;
                GameManager.Instance?.RestartCurrentScene();
            });

            cont.onClick.AddListener(() =>
            {
                Time.timeScale = 1f;
                if (!string.IsNullOrWhiteSpace(continueSceneName))
                {
                    if (GameManager.Instance != null)
                        GameManager.Instance.LoadLevel(continueSceneName);
                    else
                        SceneManager.LoadScene(continueSceneName);
                }
                else
                {
                    int next = SceneManager.GetActiveScene().buildIndex + 1;
                    if (next < SceneManager.sceneCountInBuildSettings)
                    {
                        if (GameManager.Instance != null)
                            GameManager.Instance.LoadLevel(next);
                        else
                            SceneManager.LoadScene(next);
                    }
                    else
                        Debug.LogWarning("[LevelResultsScreen] No next scene. Assign continueSceneName.");
                }
            });
        }

        Button MakeButton(RectTransform parent, string name, string label, Vector2 pos, bool secondary)
        {
            var rt = NewRT(parent, name);
            rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = new Vector2(200f, 54f);
            rt.anchoredPosition = pos;

            var bg = AddImg(rt, secondary
                ? new Color(C_ACCENT.r, C_ACCENT.g, C_ACCENT.b, 0.08f)
                : C_ACCENT);

            if (secondary)
            {
                var outline = rt.gameObject.AddComponent<Outline>();
                outline.effectColor = new Color(C_ACCENT.r, C_ACCENT.g, C_ACCENT.b, 0.35f);
                outline.effectDistance = new Vector2(1f, -1f);
            }

            var btn = rt.gameObject.AddComponent<Button>();
            btn.targetGraphic = bg;
            var colors = btn.colors;
            colors.normalColor = Color.white;
            colors.highlightedColor = secondary
                ? new Color(1f, 1f, 1f, 0.72f)
                : new Color(0.88f, 1f, 0.93f);
            colors.pressedColor = new Color(0.72f, 0.72f, 0.72f);
            btn.colors = colors;

            var txt = MakeTMP(rt, "Lbl", label.ToUpper(), 12f,
                secondary ? C_ACCENT : Hex("#030A06"), displayFont);
            txt.fontStyle = FontStyles.Bold;
            txt.characterSpacing = 5f;
            txt.alignment = TextAlignmentOptions.Center;
            Stretch(txt.rectTransform);

            return btn;
        }

        #endregion

        // ═══════════════════════════════════════════════════════════════════
        #region Populate

        void Populate(LevelStats stats)
        {
            _rootCG.alpha = 1f;

            if (fullScreenBackground != null)
            {
                fullScreenBackground.gameObject.SetActive(true);
                fullScreenBackground.color = Hex("#060910");
            }

            _statRows[0].val.text = "—";
            _statRows[1].val.text = "0";

            // Grade letter + color
            _gradeLetter.text = stats.Grade.ToString();
            _gradeLetter.color = GradeColor(stats.Grade);
            _gradeLetterRT.localScale = Vector3.one * 2f;

            // Glow plate — match grade color, start invisible
            var gc = GradeColor(stats.Grade);
            _gradeGlowImg.color = new Color(gc.r, gc.g, gc.b, 0f);

            RebuildKillRows(stats);

            // Reset animated starting states
            _headerCG.alpha = 0f;
            _headerRT.anchoredPosition = new Vector2(0f, 40f);
            _underlineRT.sizeDelta = new Vector2(0f, 2f);

            foreach (var (rt, cg, _) in _statRows)
            {
                cg.alpha = 0f;
                rt.anchoredPosition = new Vector2(rt.anchoredPosition.x - 30f, rt.anchoredPosition.y);
            }

            _gradeFrameCG.alpha = 0f;
            _gradeSubCG.alpha = 0f;
            _buttonsCG.alpha = 0f;
            _buttonsRT.anchoredPosition = new Vector2(0f, -20f);
        }

        void RebuildKillRows(LevelStats stats)
        {
            foreach (var r in _killRows) Destroy(r.rt.gameObject);
            _killRows.Clear();

            const float rowH = 64f;
            const float rowGap = 12f;
            const float startY = -26f;

            int index = 0;
            if (stats.KillsByType != null)
            {
                foreach (var kvp in stats.KillsByType)
                {
                    if (kvp.Value <= 0) continue;
                    float y = startY - index * (rowH + rowGap);
                    _killRows.Add(MakeKillRow(FormatEnemyType(kvp.Key), kvp.Value, y));
                    index++;
                }
            }

            // Push total row below all kill rows
            _totalRow.rt.anchoredPosition = new Vector2(0f, startY - index * (rowH + rowGap) - 4f);
            _totalRow.val.text = "0";
            _totalRow.cg.alpha = 0f;

            // Reset kill row start positions (slide from right in animation)
            foreach (var (rt, cg, _, _) in _killRows)
            {
                cg.alpha = 0f;
                rt.anchoredPosition = new Vector2(rt.anchoredPosition.x + 30f, rt.anchoredPosition.y);
            }
        }

        (RectTransform rt, CanvasGroup cg, TMP_Text cnt, int killCount)
            MakeKillRow(string label, int count, float y)
        {
            var row = NewRT(_killListParent, "KR_" + label);
            row.SetSiblingIndex(Mathf.Max(0, _killListParent.childCount - 2));
            row.anchorMin = row.anchorMax = row.pivot = new Vector2(0f, 1f);
            row.sizeDelta = new Vector2(480f, 64f);
            row.anchoredPosition = new Vector2(0f, y);

            AddImg(row, C_PANEL);

            // Right faint accent bar
            var bar = NewRT(row, "Bar");
            bar.anchorMin = bar.anchorMax = bar.pivot = new Vector2(1f, 0.5f);
            bar.sizeDelta = new Vector2(3f, 64f);
            bar.anchoredPosition = Vector2.zero;
            AddImg(bar, new Color(C_ACCENT.r, C_ACCENT.g, C_ACCENT.b, 0.35f));

            // Enemy type label
            var lbl = MakeTMP(row, "Lbl", label.ToUpper(), 12f, C_TXT_MUT, bodyFont);
            lbl.characterSpacing = 2f;
            lbl.alignment = TextAlignmentOptions.Left;
            var lr = lbl.rectTransform;
            lr.anchorMin = new Vector2(0f, 0f); lr.anchorMax = new Vector2(0.65f, 1f);
            lr.offsetMin = new Vector2(20f, 0f); lr.offsetMax = Vector2.zero;

            // Kill count
            var cnt = MakeTMP(row, "Cnt", count.ToString(), 20f, C_ACCENT, displayFont);
            cnt.fontStyle = FontStyles.Bold;
            cnt.alignment = TextAlignmentOptions.Right;
            var cr = cnt.rectTransform;
            cr.anchorMin = new Vector2(0.6f, 0f); cr.anchorMax = new Vector2(1f, 1f);
            cr.offsetMin = Vector2.zero; cr.offsetMax = new Vector2(-20f, 0f);

            var cg = AddCG(row, 0f);
            return (row, cg, cnt, count);
        }

        #endregion

        // ═══════════════════════════════════════════════════════════════════
        #region Animate

        void Animate(LevelStats stats)
        {
            var seq = DOTween.Sequence().SetUpdate(true);

            // ── Header slides down ────────────────────────────────────────────
            seq.Insert(0.2f, _headerCG.DOFade(1f, 0.5f).SetUpdate(true));
            seq.Insert(0.2f, _headerRT.DOAnchorPosY(0f, 0.5f)
                .SetEase(Ease.OutCubic).SetUpdate(true));

            // ── Underline expands ─────────────────────────────────────────────
            seq.Insert(0.65f, DOTween.To(
                () => _underlineRT.sizeDelta.x,
                x => _underlineRT.sizeDelta = new Vector2(x, 2f),
                1376f, 0.7f).SetEase(Ease.OutCubic).SetUpdate(true));

            // ── Stat rows slide in from left ──────────────────────────────────
            for (int i = 0; i < _statRows.Length; i++)
            {
                float t = 0.9f + i * 0.2f;
                var (rt, cg, _) = _statRows[i];
                var target = new Vector2(rt.anchoredPosition.x + 30f, rt.anchoredPosition.y);
                seq.Insert(t, cg.DOFade(1f, 0.4f).SetUpdate(true));
                seq.Insert(t, rt.DOAnchorPos(target, 0.45f).SetEase(Ease.OutCubic).SetUpdate(true));
            }

            // Time set directly; kills count up
            seq.InsertCallback(1.2f, () =>
            {
                _statRows[0].val.text = LevelStatsTracker.FormatTimeVerbose(stats.CompletionTime);
                CountUp(_statRows[1].val, stats.TotalKills, 0.8f);
            });

            // ── Kill rows slide in from right ─────────────────────────────────
            for (int i = 0; i < _killRows.Count; i++)
            {
                float t = 1.35f + i * 0.18f;
                var (rt, cg, cnt, killCount) = _killRows[i];
                var target = new Vector2(rt.anchoredPosition.x - 30f, rt.anchoredPosition.y);
                int captured = killCount;

                seq.Insert(t, cg.DOFade(1f, 0.4f).SetUpdate(true));
                seq.Insert(t, rt.DOAnchorPos(target, 0.45f).SetEase(Ease.OutCubic).SetUpdate(true));
                seq.InsertCallback(t + 0.2f, () => CountUp(cnt, captured, 0.5f));
            }

            // ── Total row ─────────────────────────────────────────────────────
            float totalT = 1.35f + _killRows.Count * 0.18f + 0.12f;
            var (totalRT, totalCG, totalVal) = _totalRow;
            var totalTarget = new Vector2(totalRT.anchoredPosition.x - 30f, totalRT.anchoredPosition.y);
            totalRT.anchoredPosition = new Vector2(totalRT.anchoredPosition.x + 30f, totalRT.anchoredPosition.y);
            seq.Insert(totalT, totalCG.DOFade(1f, 0.4f).SetUpdate(true));
            seq.Insert(totalT, totalRT.DOAnchorPos(totalTarget, 0.45f).SetEase(Ease.OutCubic).SetUpdate(true));
            seq.InsertCallback(totalT + 0.2f, () => CountUp(totalVal, stats.TotalKills, 0.7f));

            // ── Grade frame fades in ──────────────────────────────────────────
            float gradeT = totalT + 0.35f;
            seq.Insert(gradeT, _gradeFrameCG.DOFade(1f, 0.4f).SetUpdate(true));

            // ── Grade glow plate fades in (tinted to grade colour) ────────────
            var gc = GradeColor(stats.Grade);
            seq.Insert(gradeT + 0.15f, DOTween.To(
                () => _gradeGlowImg.color,
                c => _gradeGlowImg.color = c,
                new Color(gc.r, gc.g, gc.b, 0.09f), 0.6f).SetUpdate(true));

            // ── Grade letter scales down with pop ─────────────────────────────
            seq.Insert(gradeT + 0.2f, _gradeLetterRT.DOScale(Vector3.one, 0.6f)
                .SetEase(Ease.OutBack).SetUpdate(true));

            // ── After pop: subtle looping scale pulse on grade letter ─────────
            seq.InsertCallback(gradeT + 0.85f, () =>
            {
                _gradeLetterRT.DOScale(Vector3.one * 1.04f, 1.4f)
                    .SetEase(Ease.InOutSine)
                    .SetLoops(-1, LoopType.Yoyo)
                    .SetUpdate(true);
            });

            // ── Grade sub label ───────────────────────────────────────────────
            seq.Insert(gradeT + 0.75f, _gradeSubCG.DOFade(1f, 0.35f).SetUpdate(true));

            // ── Buttons slide up ──────────────────────────────────────────────
            float btnT = gradeT + 1.0f;
            seq.Insert(btnT, _buttonsCG.DOFade(1f, 0.4f).SetUpdate(true));
            seq.Insert(btnT, _buttonsRT.DOAnchorPosY(0f, 0.4f)
                .SetEase(Ease.OutCubic).SetUpdate(true));
        }

        void CountUp(TMP_Text label, float target, float duration)
        {
            float current = 0f;
            DOTween.To(
                () => current,
                x => { current = x; label.text = Mathf.RoundToInt(x).ToString(); },
                target, duration)
                .SetEase(Ease.OutCubic)
                .SetUpdate(true);
        }

        #endregion

        // ═══════════════════════════════════════════════════════════════════
        #region Decoration Helpers

        void AddScreenCorners(RectTransform parent, float inset, float len, float thick, Color col)
        {
            void H(Vector2 anchor, Vector2 pos, float w)
            {
                var rt = NewRT(parent, "Cor");
                rt.anchorMin = rt.anchorMax = rt.pivot = anchor;
                rt.sizeDelta = new Vector2(Mathf.Abs(w), thick);
                rt.anchoredPosition = w < 0 ? new Vector2(pos.x + w, pos.y) : pos;
                AddImg(rt, col);
            }
            void V(Vector2 anchor, Vector2 pos, float h)
            {
                var rt = NewRT(parent, "Cor");
                rt.anchorMin = rt.anchorMax = rt.pivot = anchor;
                rt.sizeDelta = new Vector2(thick, Mathf.Abs(h));
                rt.anchoredPosition = h < 0 ? new Vector2(pos.x, pos.y + h) : pos;
                AddImg(rt, col);
            }
            H(new Vector2(0f, 1f), new Vector2(inset, -inset), len);
            V(new Vector2(0f, 1f), new Vector2(inset, -inset), -len);
            H(new Vector2(1f, 1f), new Vector2(-inset, -inset), -len);
            V(new Vector2(1f, 1f), new Vector2(-inset, -inset), -len);
            H(new Vector2(0f, 0f), new Vector2(inset, inset), len);
            V(new Vector2(0f, 0f), new Vector2(inset, inset), len);
            H(new Vector2(1f, 0f), new Vector2(-inset, inset), -len);
            V(new Vector2(1f, 0f), new Vector2(-inset, inset), len);
        }

        void AddCornerBrackets(RectTransform frame, float len, float thick, Color col)
        {
            void H(Vector2 anchor, float w)
            {
                var rt = NewRT(frame, "Br");
                rt.anchorMin = rt.anchorMax = rt.pivot = anchor;
                rt.sizeDelta = new Vector2(len, thick);
                rt.anchoredPosition = w < 0 ? new Vector2(-len, 0f) : Vector2.zero;
                AddImg(rt, col);
            }
            void V(Vector2 anchor, float h)
            {
                var rt = NewRT(frame, "Br");
                rt.anchorMin = rt.anchorMax = rt.pivot = anchor;
                rt.sizeDelta = new Vector2(thick, len);
                rt.anchoredPosition = h < 0 ? new Vector2(0f, -len) : Vector2.zero;
                AddImg(rt, col);
            }
            H(new Vector2(0f, 1f), 1f); V(new Vector2(0f, 1f), 1f);
            H(new Vector2(1f, 1f), -1f); V(new Vector2(1f, 1f), 1f);
            H(new Vector2(0f, 0f), 1f); V(new Vector2(0f, 0f), -1f);
            H(new Vector2(1f, 0f), -1f); V(new Vector2(1f, 0f), -1f);
        }

        void AddPanelLabel(RectTransform parent, string text, Vector2 pos, Vector2 anchor,
                           TextAlignmentOptions align = TextAlignmentOptions.Left)
        {
            var lbl = MakeTMP(parent, "PanelLbl", text.ToUpper(), 11f, C_TXT_LBL, bodyFont);
            lbl.characterSpacing = 5f;
            lbl.alignment = align;
            var rt = lbl.rectTransform;
            rt.anchorMin = rt.anchorMax = rt.pivot = anchor;
            rt.sizeDelta = new Vector2(parent.sizeDelta.x, 20f);
            rt.anchoredPosition = pos;
        }

        #endregion

        // ═══════════════════════════════════════════════════════════════════
        #region Low-Level UI Helpers

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

        CanvasGroup AddCG(RectTransform rt, float alpha)
        {
            var cg = rt.gameObject.AddComponent<CanvasGroup>();
            cg.alpha = alpha;
            return cg;
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
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
        }

        void Centre(RectTransform rt, Vector2 size, Vector2 pos)
        {
            rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = size;
            rt.anchoredPosition = pos;
        }

        #endregion

        // ═══════════════════════════════════════════════════════════════════
        #region Utility

        static string FormatEnemyType(EnemyType type) => type switch
        {
            EnemyType.FlyingDummy => "Flying Dummy",
            _ => type.ToString(),
        };

        #endregion
    }
}