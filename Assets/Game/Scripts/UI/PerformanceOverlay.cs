#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System;
using UnityEngine;
using UnityEngine.InputSystem;

namespace junklite
{
    /// <summary>
    /// Lightweight runtime frame-time monitor for profiling in the Editor and
    /// development builds. It creates itself, survives scene loads, and requires
    /// no scene or prefab setup.
    /// </summary>
    [DefaultExecutionOrder(-1000)]
    public sealed class PerformanceOverlay : MonoBehaviour
    {
        private const int HistoryCapacity = 180;
        private const int GraphWidth = 180;
        private const int GraphHeight = 48;
        private const float RefreshInterval = 0.25f;
        private const float SpikeThresholdMs = 33.333f;
        private const float GraphMaximumMs = 50f;

        private static readonly Color32 GraphBackground = new(8, 13, 22, 245);
        private static readonly Color32 GraphGrid16 = new(42, 110, 82, 190);
        private static readonly Color32 GraphGrid33 = new(139, 102, 38, 220);
        private static readonly Color32 GraphGood = new(45, 224, 131, 255);
        private static readonly Color32 GraphSlow = new(255, 190, 64, 255);
        private static readonly Color32 GraphSpike = new(255, 76, 91, 255);

        private static PerformanceOverlay instance;

        private readonly float[] frameHistory = new float[HistoryCapacity];
        private readonly Color32[] graphPixels = new Color32[GraphWidth * GraphHeight];

        private Texture2D graphTexture;
        private GUIStyle titleStyle;
        private GUIStyle valueStyle;
        private GUIStyle hintStyle;
        private int historyHead;
        private int historyCount;
        private float smoothedDeltaTime;
        private float refreshTimer;
        private bool isVisible = true;
        private string fpsText = "FPS  --";
        private string frameText = "FRAME  -- ms";
        private string spikeText = "Collecting frame history...";

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            instance = null;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void EnsureInstance()
        {
            if (instance != null)
                return;

            var root = new GameObject("[DEBUG] Performance Overlay");
            instance = root.AddComponent<PerformanceOverlay>();
            DontDestroyOnLoad(root);
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
            CreateGraphTexture();
        }

        private void Update()
        {
            if (Keyboard.current?.f9Key.wasPressedThisFrame == true)
                isVisible = !isVisible;

            float deltaTime = Mathf.Max(Time.unscaledDeltaTime, 0.000001f);
            float frameMilliseconds = deltaTime * 1000f;
            frameHistory[historyHead] = frameMilliseconds;
            historyHead = (historyHead + 1) % HistoryCapacity;
            historyCount = Mathf.Min(historyCount + 1, HistoryCapacity);

            if (smoothedDeltaTime <= 0f)
            {
                smoothedDeltaTime = deltaTime;
            }
            else
            {
                float smoothing = 1f - Mathf.Exp(-deltaTime / 0.35f);
                smoothedDeltaTime = Mathf.Lerp(smoothedDeltaTime, deltaTime, smoothing);
            }

            refreshTimer += deltaTime;
            if (refreshTimer >= RefreshInterval)
            {
                refreshTimer %= RefreshInterval;
                RefreshDisplay();
            }
        }

        private void RefreshDisplay()
        {
            if (historyCount == 0)
                return;

            float totalMilliseconds = 0f;
            float worstMilliseconds = 0f;
            int spikeCount = 0;

            for (int i = 0; i < historyCount; i++)
            {
                float milliseconds = frameHistory[i];
                totalMilliseconds += milliseconds;
                worstMilliseconds = Mathf.Max(worstMilliseconds, milliseconds);
                if (milliseconds > SpikeThresholdMs)
                    spikeCount++;
            }

            float averageMilliseconds = totalMilliseconds / historyCount;
            float currentFps = 1f / Mathf.Max(smoothedDeltaTime, 0.000001f);
            float averageFps = 1000f / Mathf.Max(averageMilliseconds, 0.001f);

            fpsText = $"FPS  {currentFps,3:0}     AVG  {averageFps,3:0}";
            frameText = $"FRAME  {smoothedDeltaTime * 1000f,5:0.0} ms     WORST  {worstMilliseconds,5:0.0} ms";
            spikeText = $"SPIKES > 33.3 ms   {spikeCount} / {historyCount} frames";
            if (isVisible)
                RebuildGraph();
        }

        private void CreateGraphTexture()
        {
            graphTexture = new Texture2D(GraphWidth, GraphHeight, TextureFormat.RGBA32, false)
            {
                name = "Performance Frame-Time Graph",
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp,
                hideFlags = HideFlags.HideAndDontSave
            };
            RebuildGraph();
        }

        private void RebuildGraph()
        {
            Array.Fill(graphPixels, GraphBackground);

            int sixteenMsRow = MillisecondsToGraphRow(16.667f);
            int thirtyThreeMsRow = MillisecondsToGraphRow(33.333f);
            DrawGraphGridRow(sixteenMsRow, GraphGrid16);
            DrawGraphGridRow(thirtyThreeMsRow, GraphGrid33);

            int samplesToDraw = Mathf.Min(historyCount, GraphWidth);
            int firstSample = (historyHead - samplesToDraw + HistoryCapacity) % HistoryCapacity;
            int xOffset = GraphWidth - samplesToDraw;

            for (int i = 0; i < samplesToDraw; i++)
            {
                float milliseconds = frameHistory[(firstSample + i) % HistoryCapacity];
                int height = MillisecondsToGraphRow(milliseconds) + 1;
                Color32 color = milliseconds > SpikeThresholdMs
                    ? GraphSpike
                    : milliseconds > 16.667f
                        ? GraphSlow
                        : GraphGood;

                int x = xOffset + i;
                for (int y = 0; y < height; y++)
                    graphPixels[y * GraphWidth + x] = color;
            }

            graphTexture.SetPixels32(graphPixels);
            graphTexture.Apply(false, false);
        }

        private static int MillisecondsToGraphRow(float milliseconds)
        {
            float normalized = Mathf.Clamp01(milliseconds / GraphMaximumMs);
            return Mathf.Clamp(Mathf.RoundToInt(normalized * (GraphHeight - 1)), 0, GraphHeight - 1);
        }

        private void DrawGraphGridRow(int row, Color32 color)
        {
            int offset = row * GraphWidth;
            for (int x = 0; x < GraphWidth; x++)
                graphPixels[offset + x] = color;
        }

        private void CreateStyles()
        {
            titleStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 12,
                fontStyle = FontStyle.Bold,
                normal = { textColor = new Color(0.26f, 0.9f, 1f) }
            };
            valueStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 13,
                fontStyle = FontStyle.Bold,
                normal = { textColor = new Color(0.92f, 0.96f, 1f) }
            };
            hintStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 10,
                normal = { textColor = new Color(0.58f, 0.67f, 0.78f) }
            };
        }

        private void OnGUI()
        {
            if (!isVisible || Event.current.type != EventType.Repaint)
                return;

            if (titleStyle == null)
                CreateStyles();

            float scale = Mathf.Clamp(Screen.height / 1080f, 0.8f, 1.5f);
            Rect safeArea = Screen.safeArea;
            float x = safeArea.xMin / scale + 12f;
            float y = (Screen.height - safeArea.yMax) / scale + 12f;
            Rect panel = new(x, y, 310f, 154f);

            Matrix4x4 previousMatrix = GUI.matrix;
            GUI.matrix = Matrix4x4.Scale(new Vector3(scale, scale, 1f));

            DrawSolidRect(new Rect(panel.x + 3f, panel.y + 3f, panel.width, panel.height),
                new Color(0f, 0f, 0f, 0.45f));
            DrawSolidRect(panel, new Color(0.025f, 0.04f, 0.07f, 0.93f));
            DrawSolidRect(new Rect(panel.x, panel.y, 3f, panel.height),
                new Color(0.22f, 0.84f, 0.95f, 1f));

            GUI.Label(new Rect(panel.x + 12f, panel.y + 7f, 200f, 18f), "PERFORMANCE", titleStyle);
            GUI.Label(new Rect(panel.x + 246f, panel.y + 7f, 52f, 18f), "F9 HIDE", hintStyle);
            GUI.Label(new Rect(panel.x + 12f, panel.y + 25f, 286f, 20f), fpsText, valueStyle);
            GUI.Label(new Rect(panel.x + 12f, panel.y + 45f, 286f, 20f), frameText, valueStyle);
            GUI.Label(new Rect(panel.x + 12f, panel.y + 65f, 286f, 18f), spikeText, hintStyle);

            Rect graphRect = new(panel.x + 12f, panel.y + 86f, 286f, 50f);
            GUI.DrawTexture(graphRect, graphTexture, ScaleMode.StretchToFill, false);
            GUI.Label(new Rect(panel.x + 12f, panel.y + 136f, 286f, 15f),
                "green <16.7ms   yellow <33.3ms   red spike   F9 hide", hintStyle);

            GUI.matrix = previousMatrix;
        }

        private static void DrawSolidRect(Rect rect, Color color)
        {
            Color previousColor = GUI.color;
            GUI.color = color;
            GUI.DrawTexture(rect, Texture2D.whiteTexture);
            GUI.color = previousColor;
        }

        private void OnDestroy()
        {
            if (graphTexture != null)
                Destroy(graphTexture);
            if (instance == this)
                instance = null;
        }
    }
}
#endif
