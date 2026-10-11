#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System;
using System.Globalization;
using System.IO;
using System.Text;
using Unity.Profiling;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;

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
        private const float SpikeLogCooldownSeconds = 0.15f;
        private const int FramesBeforeLogging = 10;
        private const long AllocationWarningBytes = 128 * 1024;

        private static readonly Color32 GraphBackground = new(8, 13, 22, 245);
        private static readonly Color32 GraphGrid16 = new(42, 110, 82, 190);
        private static readonly Color32 GraphGrid33 = new(139, 102, 38, 220);
        private static readonly Color32 GraphGood = new(45, 224, 131, 255);
        private static readonly Color32 GraphSlow = new(255, 190, 64, 255);
        private static readonly Color32 GraphSpike = new(255, 76, 91, 255);

        private static PerformanceOverlay instance;

        public static string CurrentLogPath => instance?.spikeLogPath;

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
        private float nextSpikeLogTime;
        private bool isVisible = true;
        private int observedFrameCount;
        private int spikeLogCount;
        private string lastCommand;
        private int lastCommandFrame = -1;
        private double lastCommandMilliseconds;
        private int lastLogWriteFrame = -1;
        private double lastLogWriteMilliseconds;
        private string fpsText = "FPS  --";
        private string frameText = "FRAME  -- ms";
        private string spikeText = "Collecting frame history...";

        private readonly FrameTiming[] frameTimings = new FrameTiming[1];
        private ProfilerRecorder gcAllocationRecorder;
        private ProfilerRecorder scriptUpdateRecorder;
        private ProfilerRecorder physicsRecorder;
        private ProfilerRecorder animationRecorder;
        private ProfilerRecorder renderRecorder;
        private ProfilerRecorder gcCollectRecorder;
        private ProfilerRecorder audioLoadRecorder;
        private ProfilerRecorder assetLoadRecorder;
        private ProfilerRecorder drawCallsRecorder;
        private ProfilerRecorder setPassCallsRecorder;
        private ProfilerRecorder trianglesRecorder;
        private StreamWriter spikeLogWriter;
        private string spikeLogPath;

        public static void RecordDeveloperCommand(string label, double milliseconds)
        {
            if (instance == null) return;
            instance.lastCommand = label;
            instance.lastCommandFrame = Time.frameCount;
            instance.lastCommandMilliseconds = milliseconds;
        }

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
            StartSpikeLogging();
        }

        private void Update()
        {
            if (Keyboard.current?.f9Key.wasPressedThisFrame == true)
                isVisible = !isVisible;

            float deltaTime = Mathf.Max(Time.unscaledDeltaTime, 0.000001f);
            float frameMilliseconds = deltaTime * 1000f;
            float baselineMilliseconds = smoothedDeltaTime > 0f
                ? smoothedDeltaTime * 1000f
                : frameMilliseconds;
            frameHistory[historyHead] = frameMilliseconds;
            historyHead = (historyHead + 1) % HistoryCapacity;
            historyCount = Mathf.Min(historyCount + 1, HistoryCapacity);
            observedFrameCount++;

            FrameTimingManager.CaptureFrameTimings();
            if (ShouldRecordSpike(frameMilliseconds, baselineMilliseconds))
            {
                RecordSpike(frameMilliseconds, baselineMilliseconds);
            }

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
            spikeText = $"SPIKES > 33.3 ms   {spikeCount} / {historyCount}     LOGGED {spikeLogCount}";
            if (isVisible)
                RebuildGraph();
        }

        private void StartSpikeLogging()
        {
            gcAllocationRecorder = StartRecorder(ProfilerCategory.Memory, "GC Allocated In Frame");
            scriptUpdateRecorder = StartRecorder(ProfilerCategory.Scripts, "BehaviourUpdate");
            physicsRecorder = StartTimer("FixedUpdate.PhysicsFixedUpdate");
            animationRecorder = StartTimer("PreLateUpdate.DirectorUpdateAnimationBegin");
            renderRecorder = StartTimer("Inl_UniversalRenderTotal");
            gcCollectRecorder = StartTimer("GC.Collect");
            audioLoadRecorder = StartTimer("SoundManager.LoadFMODSound");
            assetLoadRecorder = StartTimer("Loading.ReadObject");
            drawCallsRecorder = StartRecorder(ProfilerCategory.Render, "Draw Calls Count");
            setPassCallsRecorder = StartRecorder(ProfilerCategory.Render, "SetPass Calls Count");
            trianglesRecorder = StartRecorder(ProfilerCategory.Render, "Triangles Count");

            try
            {
                string directory = Path.Combine(Application.persistentDataPath, "PerformanceLogs");
                Directory.CreateDirectory(directory);
                spikeLogPath = Path.Combine(
                    directory,
                    $"frame-spikes-{DateTime.Now:yyyyMMdd-HHmmss}.csv");
                spikeLogWriter = new StreamWriter(
                    spikeLogPath,
                    false,
                    new UTF8Encoding(false),
                    16384);
                spikeLogWriter.WriteLine(
                    "timestamp,frame,scene,frame_ms,baseline_ms,delayed_cpu_main_ms,delayed_cpu_render_ms,delayed_gpu_ms," +
                    "gc_alloc_bytes,script_update_ms,physics_ms,animation_ms,render_phase_ms," +
                    "draw_calls,setpass_calls,triangles,quality,likely_cause," +
                    "gc_collect_ms,audio_load_ms,asset_load_ms,last_dev_command,dev_command_age_frames,dev_command_ms,logger_io_ms");
                spikeLogWriter.Flush();
                Debug.Log($"[PerformanceOverlay] Recording frame spikes to: {spikeLogPath}", this);
            }
            catch (Exception exception)
            {
                spikeLogWriter?.Dispose();
                spikeLogWriter = null;
                Debug.LogWarning($"[PerformanceOverlay] Could not create spike log: {exception.Message}", this);
            }
        }

        private static ProfilerRecorder StartRecorder(ProfilerCategory category, string markerName)
        {
            try
            {
                return ProfilerRecorder.StartNew(
                    category,
                    markerName,
                    1,
                    ProfilerRecorderOptions.WrapAroundWhenCapacityReached |
                    ProfilerRecorderOptions.SumAllSamplesInFrame);
            }
            catch
            {
                return default;
            }
        }

        private static ProfilerRecorder StartTimer(string markerName)
        {
            try
            {
                // The constructor resolves the marker's category. Restrict durations
                // to this thread so background worker totals cannot look like a stall.
                return new ProfilerRecorder(markerName, 1,
                    ProfilerRecorderOptions.StartImmediately |
                    ProfilerRecorderOptions.CollectOnlyOnCurrentThread |
                    ProfilerRecorderOptions.WrapAroundWhenCapacityReached |
                    ProfilerRecorderOptions.SumAllSamplesInFrame);
            }
            catch { return default; }
        }

        private bool ShouldRecordSpike(float frameMilliseconds, float baselineMilliseconds)
        {
            if (spikeLogWriter == null || observedFrameCount <= FramesBeforeLogging)
                return false;
            if (frameMilliseconds < 100f && Time.realtimeSinceStartup < nextSpikeLogTime)
                return false;

            bool exceedsBudget = frameMilliseconds >= SpikeThresholdMs;
            bool exceedsBaseline = frameMilliseconds >= baselineMilliseconds * 1.5f;
            bool severeHitch = frameMilliseconds >= 100f;
            return exceedsBudget && (exceedsBaseline || severeHitch);
        }

        private void RecordSpike(float frameMilliseconds, float baselineMilliseconds)
        {
            nextSpikeLogTime = Time.realtimeSinceStartup + SpikeLogCooldownSeconds;

            frameTimings[0] = default;
            uint timingCount = FrameTimingManager.GetLatestTimings(1, frameTimings);
            // FrameTimingManager arrives several frames late. These columns provide
            // context only; never use them to attribute the current wall-clock hitch.
            double cpuMainMilliseconds = timingCount > 0 ? frameTimings[0].cpuMainThreadFrameTime : -1d;
            double cpuRenderMilliseconds = timingCount > 0 ? frameTimings[0].cpuRenderThreadFrameTime : -1d;
            double gpuMilliseconds = timingCount > 0 ? frameTimings[0].gpuFrameTime : -1d;

            long gcAllocationBytes = ReadCounter(gcAllocationRecorder);
            double scriptMilliseconds = ReadMilliseconds(scriptUpdateRecorder);
            double physicsMilliseconds = ReadMilliseconds(physicsRecorder);
            double animationMilliseconds = ReadMilliseconds(animationRecorder);
            double renderMilliseconds = ReadMilliseconds(renderRecorder);
            double gcCollectMilliseconds = ReadMilliseconds(gcCollectRecorder);
            double audioLoadMilliseconds = ReadMilliseconds(audioLoadRecorder);
            double assetLoadMilliseconds = ReadMilliseconds(assetLoadRecorder);
            double loggerMilliseconds = lastLogWriteFrame == Time.frameCount - 1 ? lastLogWriteMilliseconds : 0d;
            long drawCalls = ReadCounter(drawCallsRecorder);
            long setPassCalls = ReadCounter(setPassCallsRecorder);
            long triangles = ReadCounter(trianglesRecorder);
            string likelyCause = DetermineLikelyCause(
                frameMilliseconds,
                gcAllocationBytes,
                gcCollectMilliseconds,
                audioLoadMilliseconds,
                assetLoadMilliseconds,
                scriptMilliseconds,
                physicsMilliseconds,
                animationMilliseconds,
                renderMilliseconds,
                loggerMilliseconds);

            string sceneName = EscapeCsv(SceneManager.GetActiveScene().name);
            string cause = EscapeCsv(likelyCause);
            string line = string.Format(
                CultureInfo.InvariantCulture,
                "{0:O},{1},\"{2}\",{3:0.000},{4:0.000},{5:0.000},{6:0.000},{7:0.000}," +
                "{8},{9:0.000},{10:0.000},{11:0.000},{12:0.000},{13},{14},{15},\"{16}\",\"{17}\"," +
                "{18:0.000},{19:0.000},{20:0.000},\"{21}\",{22},{23:0.000},{24:0.000}",
                DateTime.Now,
                Time.frameCount - 1,
                sceneName,
                frameMilliseconds,
                baselineMilliseconds,
                cpuMainMilliseconds,
                cpuRenderMilliseconds,
                gpuMilliseconds,
                gcAllocationBytes,
                scriptMilliseconds,
                physicsMilliseconds,
                animationMilliseconds,
                renderMilliseconds,
                drawCalls,
                setPassCalls,
                triangles,
                EscapeCsv(QualitySettings.names[QualitySettings.GetQualityLevel()]),
                cause,
                gcCollectMilliseconds,
                audioLoadMilliseconds,
                assetLoadMilliseconds,
                EscapeCsv(lastCommand),
                lastCommandFrame >= 0 ? Time.frameCount - 1 - lastCommandFrame : -1,
                lastCommandMilliseconds,
                loggerMilliseconds);

            try
            {
                long started = System.Diagnostics.Stopwatch.GetTimestamp();
                spikeLogWriter.WriteLine(line);
                spikeLogCount++;

                // Keep periodic persistence, but measure it rather than hiding the
                // following frames (which could contain real, adjacent gameplay stalls).
                if (spikeLogCount % 10 == 0)
                    spikeLogWriter.Flush();
                lastLogWriteFrame = Time.frameCount;
                lastLogWriteMilliseconds = (System.Diagnostics.Stopwatch.GetTimestamp() - started) *
                                          1000d / System.Diagnostics.Stopwatch.Frequency;
            }
            catch (Exception exception)
            {
                Debug.LogWarning($"[PerformanceOverlay] Spike logging stopped: {exception.Message}", this);
                CloseSpikeLog();
            }
        }

        private static long ReadCounter(ProfilerRecorder recorder)
        {
            // -1 means unavailable, 0 means a supported marker had no work this frame.
            return !recorder.Valid ? -1L : recorder.Count > 0 ? recorder.LastValue : 0L;
        }

        private static double ReadMilliseconds(ProfilerRecorder recorder)
        {
            long value = ReadCounter(recorder);
            return value < 0 ? -1d : value * 0.000001d;
        }

        private static string DetermineLikelyCause(
            float frameMilliseconds,
            long gcAllocationBytes,
            double gcCollectMilliseconds,
            double audioLoadMilliseconds,
            double assetLoadMilliseconds,
            double scriptMilliseconds,
            double physicsMilliseconds,
            double animationMilliseconds,
            double renderMilliseconds,
            double loggerMilliseconds)
        {
            double significant = Math.Max(4d, frameMilliseconds * 0.25d);
            if (audioLoadMilliseconds >= significant) return "Synchronous audio loading candidate";
            if (assetLoadMilliseconds >= significant) return "Asset loading candidate";
            if (gcCollectMilliseconds >= significant) return "Measured garbage collection candidate";
            if (loggerMilliseconds >= significant) return "Spike-log disk I/O candidate";

            double largestSubsystem = scriptMilliseconds;
            string subsystem = "Script update";
            if (physicsMilliseconds > largestSubsystem)
            {
                largestSubsystem = physicsMilliseconds;
                subsystem = "Physics / trigger callbacks";
            }
            if (animationMilliseconds > largestSubsystem)
            {
                largestSubsystem = animationMilliseconds;
                subsystem = "Animation";
            }
            if (renderMilliseconds > largestSubsystem)
            {
                largestSubsystem = renderMilliseconds;
                subsystem = "Render phase / waits";
            }
            if (largestSubsystem >= 4d && largestSubsystem >= frameMilliseconds * 0.25d)
                return $"{subsystem} candidate";

            if (gcAllocationBytes >= AllocationWarningBytes)
                return "High allocation volume; collection not established as cause";

            return "Unclassified - inspect this frame in Unity Profiler";
        }

        private static string EscapeCsv(string value)
        {
            return string.IsNullOrEmpty(value) ? string.Empty : value.Replace("\"", "\"\"");
        }

        private void CloseSpikeLog()
        {
            if (spikeLogWriter == null)
                return;

            try
            {
                spikeLogWriter.Flush();
                spikeLogWriter.Dispose();
            }
            catch (Exception exception)
            {
                Debug.LogWarning($"[PerformanceOverlay] Could not close spike log: {exception.Message}", this);
            }
            finally
            {
                spikeLogWriter = null;
            }
        }

        private static void DisposeRecorder(ref ProfilerRecorder recorder)
        {
            if (recorder.Valid)
                recorder.Dispose();
            recorder = default;
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

        private void OnApplicationQuit()
        {
            CloseSpikeLog();
        }

        private void OnDestroy()
        {
            CloseSpikeLog();
            DisposeRecorder(ref gcAllocationRecorder);
            DisposeRecorder(ref scriptUpdateRecorder);
            DisposeRecorder(ref physicsRecorder);
            DisposeRecorder(ref animationRecorder);
            DisposeRecorder(ref renderRecorder);
            DisposeRecorder(ref gcCollectRecorder);
            DisposeRecorder(ref audioLoadRecorder);
            DisposeRecorder(ref assetLoadRecorder);
            DisposeRecorder(ref drawCallsRecorder);
            DisposeRecorder(ref setPassCallsRecorder);
            DisposeRecorder(ref trianglesRecorder);

            if (graphTexture != null)
                Destroy(graphTexture);
            if (instance == this)
                instance = null;
        }
    }
}
#endif
