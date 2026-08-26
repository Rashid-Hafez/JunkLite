using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace junklite
{
    /// <summary>
    /// Debug/Test manager that provides UI buttons for common testing operations.
    /// Attach to a GameObject in your scene and configure in the inspector.
    /// </summary>
    public class TestManager : MonoBehaviour
    {
        public static TestManager Instance { get; private set; }

        [Header("Test Panel Settings")]
        [SerializeField] private bool showTestPanel = true;
        [SerializeField] private KeyCode toggleKey = KeyCode.F1;

        [Header("Panel Appearance")]
        [SerializeField] private Color panelBackgroundColor = new Color(0.1f, 0.1f, 0.1f, 0.9f);
        [SerializeField] private Color buttonColor = new Color(0.3f, 0.3f, 0.3f, 1f);
        [SerializeField] private Color buttonTextColor = Color.white;
        [SerializeField] private Color labelColor = Color.cyan;
        [SerializeField] private int fontSize = 14;
        [SerializeField] private int buttonHeight = 35;
        [SerializeField] private int panelWidth = 280;
        [SerializeField] private int panelHeight = 560;

        [Header("UI Scaling")]
        [Tooltip("Reference resolution for scaling (e.g., 1920x1080)")]
        [SerializeField] private Vector2 referenceResolution = new Vector2(1920, 1080);
        [SerializeField] private bool autoScale = true;
        [Range(0.5f, 2f)]
        [SerializeField] private float manualScaleMultiplier = 1f;

        [Header("Enemy Prefabs")]
        [SerializeField] private GameObject robotPrefab;
        [SerializeField] private GameObject hyenaPrefab;

        [Header("Enemy Spawning")]
        [SerializeField] private Transform[] enemySpawnPoints;
        [SerializeField] private float spawnRadius = 5f;

        [Header("Item Spawning")]
        [SerializeField] private GameObject[] itemPrefabs;

        // Runtime state
        private List<GameObject> spawnedEnemies = new List<GameObject>();
        private Vector2 scrollPosition;
        private Rect panelRect;

        // Cached styles
        private GUIStyle windowStyle;
        private GUIStyle buttonStyle;
        private GUIStyle labelStyle;
        private GUIStyle headerStyle;
        private GUIStyle statusStyle;
        private Texture2D backgroundTexture;
        private Texture2D buttonTexture;
        private Texture2D buttonHoverTexture;
        private bool stylesInitialized = false;

        // Events for external systems to hook into
        public event Action OnSceneReset;
        public event Action<GameObject> OnEnemySpawned;
        public event Action OnAllEnemiesKilled;

        void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;
        }

        void Start()
        {
            // Initialize panel position (top-left with some padding)
            panelRect = new Rect(10, 10, panelWidth, panelHeight);
        }

        void Update()
        {
            if (Input.GetKeyDown(toggleKey))
                showTestPanel = !showTestPanel;
        }

        // ---- Enemy Actions ----

        public void SpawnRobot()
        {
            if (robotPrefab == null)
            {
                Debug.LogWarning("[TestManager] Robot prefab not assigned.");
                return;
            }

            SpawnEnemy(robotPrefab, "Robot");
        }

        public void SpawnHyena()
        {
            if (hyenaPrefab == null)
            {
                Debug.LogWarning("[TestManager] Hyena prefab not assigned.");
                return;
            }

            SpawnEnemy(hyenaPrefab, "Hyena");
        }

        private void SpawnEnemy(GameObject prefab, string name)
        {
            Vector3 spawnPos = GetEnemySpawnPosition();
            GameObject enemy = Instantiate(prefab, spawnPos, Quaternion.identity);
            spawnedEnemies.Add(enemy);

            OnEnemySpawned?.Invoke(enemy);
            Debug.Log($"[TestManager] Spawned {name} at {spawnPos}");
        }

        public void KillAllEnemies()
        {
            int count = 0;
            foreach (var enemy in spawnedEnemies)
            {
                if (enemy != null)
                {
                    var health = enemy.GetComponent<IHealth>();
                    if (health != null)
                        health.TakeDamage(99999);
                    else
                        Destroy(enemy);

                    count++;
                }
            }
            spawnedEnemies.Clear();

            var sceneEnemies = GameObject.FindGameObjectsWithTag("Enemy");
            foreach (var enemy in sceneEnemies)
            {
                var health = enemy.GetComponent<IHealth>();
                if (health != null)
                    health.TakeDamage(99999);
                else
                    Destroy(enemy);

                count++;
            }

            OnAllEnemiesKilled?.Invoke();
            Debug.Log($"[TestManager] Killed {count} enemies.");
        }

        private Vector3 GetEnemySpawnPosition()
        {
            if (enemySpawnPoints != null && enemySpawnPoints.Length > 0)
            {
                int index = UnityEngine.Random.Range(0, enemySpawnPoints.Length);
                return enemySpawnPoints[index].position;
            }

            var player = GetPlayer();
            if (player != null)
            {
                Vector2 randomOffset = UnityEngine.Random.insideUnitCircle * spawnRadius;
                return player.transform.position + new Vector3(randomOffset.x, 0, randomOffset.y);
            }

            return new Vector3(
                UnityEngine.Random.Range(-spawnRadius, spawnRadius),
                0,
                UnityEngine.Random.Range(-spawnRadius, spawnRadius)
            );
        }

        // ---- Scene/Game Actions ----

        public void ResetScene()
        {
            OnSceneReset?.Invoke();

            // Full scene reload
            Scene currentScene = SceneManager.GetActiveScene();
            Debug.Log($"[TestManager] Reloading scene: {currentScene.name}");
            SceneManager.LoadScene(currentScene.buildIndex);
        }

        public void QuitGame()
        {
            Debug.Log("[TestManager] Quitting game...");

#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }

        public void PauseGame()
        {
            if (GameManager.Instance != null)
                GameManager.Instance.PauseGame();
            else
                Time.timeScale = 0f;

            Debug.Log("[TestManager] Game paused.");
        }

        public void ResumeGame()
        {
            if (GameManager.Instance != null)
                GameManager.Instance.ResumeGame();
            else
                Time.timeScale = 1f;

            Debug.Log("[TestManager] Game resumed.");
        }

        public void SetTimeScale(float scale)
        {
            Time.timeScale = Mathf.Clamp(scale, 0f, 10f);
            Debug.Log($"[TestManager] Time scale set to {Time.timeScale}");
        }

        // ---- Item Spawning ----

        public void SpawnItem(int prefabIndex)
        {
            if (itemPrefabs == null || prefabIndex < 0 || prefabIndex >= itemPrefabs.Length)
            {
                Debug.LogWarning("[TestManager] Invalid item prefab index.");
                return;
            }

            var player = GetPlayer();
            Vector3 spawnPos = player != null
                ? player.transform.position + player.transform.forward * 2f
                : Vector3.zero;

            Instantiate(itemPrefabs[prefabIndex], spawnPos, Quaternion.identity);
            Debug.Log($"[TestManager] Spawned item at {spawnPos}");
        }

        // ---- Utility ----

        private PlayerCharacter GetPlayer()
        {
            if (PlayerLifecycle.Instance?.Player != null)
                return PlayerLifecycle.Instance.Player;

            return FindFirstObjectByType<PlayerCharacter>();
        }

        public void ToggleTestPanel()
        {
            showTestPanel = !showTestPanel;
        }

        // ---- GUI Styling ----

        private float GetScaleFactor()
        {
            if (!autoScale)
                return manualScaleMultiplier;

            float widthRatio = Screen.width / referenceResolution.x;
            float heightRatio = Screen.height / referenceResolution.y;
            return Mathf.Min(widthRatio, heightRatio) * manualScaleMultiplier;
        }

        private Texture2D MakeSolidTexture(Color color)
        {
            Texture2D tex = new Texture2D(1, 1);
            tex.SetPixel(0, 0, color);
            tex.Apply();
            return tex;
        }

        private void InitializeStyles()
        {
            if (stylesInitialized) return;

            // Create textures
            backgroundTexture = MakeSolidTexture(panelBackgroundColor);
            buttonTexture = MakeSolidTexture(buttonColor);

            Color hoverColor = buttonColor * 1.3f;
            hoverColor.a = 1f;
            buttonHoverTexture = MakeSolidTexture(hoverColor);

            // Window style
            windowStyle = new GUIStyle(GUI.skin.window);
            windowStyle.normal.background = backgroundTexture;
            windowStyle.onNormal.background = backgroundTexture;
            windowStyle.normal.textColor = Color.white;
            windowStyle.fontSize = fontSize + 2;
            windowStyle.fontStyle = FontStyle.Bold;
            windowStyle.alignment = TextAnchor.UpperCenter;
            windowStyle.padding = new RectOffset(10, 10, 25, 10);

            // Button style
            buttonStyle = new GUIStyle(GUI.skin.button);
            buttonStyle.normal.background = buttonTexture;
            buttonStyle.hover.background = buttonHoverTexture;
            buttonStyle.active.background = buttonHoverTexture;
            buttonStyle.normal.textColor = buttonTextColor;
            buttonStyle.hover.textColor = Color.yellow;
            buttonStyle.active.textColor = Color.yellow;
            buttonStyle.fontSize = fontSize;
            buttonStyle.fontStyle = FontStyle.Bold;
            buttonStyle.alignment = TextAnchor.MiddleCenter;
            buttonStyle.border = new RectOffset(4, 4, 4, 4);
            buttonStyle.margin = new RectOffset(2, 2, 2, 2);

            // Header label style
            headerStyle = new GUIStyle(GUI.skin.label);
            headerStyle.normal.textColor = labelColor;
            headerStyle.fontSize = fontSize;
            headerStyle.fontStyle = FontStyle.Bold;
            headerStyle.alignment = TextAnchor.MiddleLeft;
            headerStyle.margin = new RectOffset(0, 0, 8, 4);

            // Normal label style
            labelStyle = new GUIStyle(GUI.skin.label);
            labelStyle.normal.textColor = new Color(0.9f, 0.9f, 0.9f, 1f);
            labelStyle.fontSize = fontSize - 1;
            labelStyle.alignment = TextAnchor.MiddleLeft;

            // Status label style
            statusStyle = new GUIStyle(GUI.skin.label);
            statusStyle.normal.textColor = Color.green;
            statusStyle.fontSize = fontSize - 1;
            statusStyle.alignment = TextAnchor.MiddleLeft;

            stylesInitialized = true;
        }

        private void RefreshStyles()
        {
            // Recreate background texture if color changed
            if (backgroundTexture != null)
                Destroy(backgroundTexture);
            if (buttonTexture != null)
                Destroy(buttonTexture);
            if (buttonHoverTexture != null)
                Destroy(buttonHoverTexture);

            stylesInitialized = false;
            InitializeStyles();
        }

        // ---- Debug GUI ----

        void OnGUI()
        {
            if (!showTestPanel) return;

            InitializeStyles();

            // Apply scaling
            float scale = GetScaleFactor();
            Matrix4x4 originalMatrix = GUI.matrix;
            GUI.matrix = Matrix4x4.TRS(Vector3.zero, Quaternion.identity, new Vector3(scale, scale, 1f));

            // Adjust panel rect for scaling
            float scaledWidth = Screen.width / scale;
            float scaledHeight = Screen.height / scale;

            // Clamp panel position within screen bounds
            panelRect.x = Mathf.Clamp(panelRect.x, 0, scaledWidth - panelRect.width);
            panelRect.y = Mathf.Clamp(panelRect.y, 0, scaledHeight - panelRect.height);
            panelRect.width = panelWidth;
            panelRect.height = panelHeight;

            panelRect = GUI.Window(9999, panelRect, DrawTestPanel, "TEST MANAGER (F1)", windowStyle);

            // Restore original matrix
            GUI.matrix = originalMatrix;
        }

        private void DrawTestPanel(int windowID)
        {
            scrollPosition = GUILayout.BeginScrollView(scrollPosition);

            // Enemy section
            GUILayout.Label("ENEMIES", headerStyle);

            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Spawn Robot", buttonStyle, GUILayout.Height(buttonHeight)))
                SpawnRobot();
            if (GUILayout.Button("Spawn Hyena", buttonStyle, GUILayout.Height(buttonHeight)))
                SpawnHyena();
            GUILayout.EndHorizontal();

            // Red button for kill all
            var redTex = MakeSolidTexture(new Color(0.7f, 0.2f, 0.2f, 1f));
            var redHoverTex = MakeSolidTexture(new Color(0.9f, 0.3f, 0.3f, 1f));
            buttonStyle.normal.background = redTex;
            buttonStyle.hover.background = redHoverTex;

            if (GUILayout.Button("Kill All Enemies", buttonStyle, GUILayout.Height(buttonHeight)))
                KillAllEnemies();

            buttonStyle.normal.background = buttonTexture;
            buttonStyle.hover.background = buttonHoverTexture;

            GUILayout.Space(10);

            // Time section
            GUILayout.Label("TIME", headerStyle);

            GUILayout.BeginHorizontal();
            if (GUILayout.Button("0.5x", buttonStyle, GUILayout.Height(buttonHeight)))
                SetTimeScale(0.5f);
            if (GUILayout.Button("1x", buttonStyle, GUILayout.Height(buttonHeight)))
                SetTimeScale(1f);
            if (GUILayout.Button("2x", buttonStyle, GUILayout.Height(buttonHeight)))
                SetTimeScale(2f);
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Pause", buttonStyle, GUILayout.Height(buttonHeight)))
                PauseGame();
            if (GUILayout.Button("Resume", buttonStyle, GUILayout.Height(buttonHeight)))
                ResumeGame();
            GUILayout.EndHorizontal();

            GUILayout.Space(10);

            // Scene section
            GUILayout.Label("SCENE", headerStyle);

            // Yellow button for reset
            var yellowTex = MakeSolidTexture(new Color(0.7f, 0.6f, 0.1f, 1f));
            var yellowHoverTex = MakeSolidTexture(new Color(0.9f, 0.8f, 0.2f, 1f));
            buttonStyle.normal.background = yellowTex;
            buttonStyle.hover.background = yellowHoverTex;
            buttonStyle.normal.textColor = Color.black;
            buttonStyle.hover.textColor = Color.black;

            if (GUILayout.Button("Reset Scene", buttonStyle, GUILayout.Height(buttonHeight)))
                ResetScene();

            // Red button for quit
            buttonStyle.normal.background = redTex;
            buttonStyle.hover.background = redHoverTex;
            buttonStyle.normal.textColor = buttonTextColor;
            buttonStyle.hover.textColor = Color.yellow;

            if (GUILayout.Button("Quit Game", buttonStyle, GUILayout.Height(buttonHeight)))
                QuitGame();

            // Restore button style
            buttonStyle.normal.background = buttonTexture;
            buttonStyle.hover.background = buttonHoverTexture;
            buttonStyle.normal.textColor = buttonTextColor;

            GUILayout.Space(10);

            // Status section
            GUILayout.Label("STATUS", headerStyle);
            var player = GetPlayer();
            if (player != null && player.State != null)
            {
                GUILayout.Label($"Player HP: {player.attributes.Health.Current}  /  {player.attributes.Health.maxValue}", statusStyle);
                GUILayout.Label($"Position: {player.transform.position:F1}", labelStyle);
            }
            else
            {
                GUILayout.Label("Player: Not found", labelStyle);
            }

            GUILayout.Label($"Spawned Enemies: {spawnedEnemies.Count}", labelStyle);
            GUILayout.Label($"Time Scale: {Time.timeScale:F2}", labelStyle);

            GUILayout.Space(10);

            // Hotkey reference
            GUILayout.Label("CONTROLS", headerStyle);
            GUILayout.Label("LMB - Attack", labelStyle);
            GUILayout.Label("Shift - Dash", labelStyle);
            GUILayout.Label("Double Space - Jump", labelStyle);

            GUILayout.Space(5);

            GUILayout.Label("DEBUG KEYS", headerStyle);
            GUILayout.Label("H - Heal Player", labelStyle);
            GUILayout.Label("T - Take Damage", labelStyle);
            GUILayout.Label("Y - Instant Death", labelStyle);
            GUILayout.Label("G - Drop Weapon", labelStyle);
            GUILayout.Label("I - Inventory", labelStyle);
            GUILayout.Label("ESC - Pause/Resume", labelStyle);
            GUILayout.Label("R - Restart Level", labelStyle);
            GUILayout.Space(5);
            GUILayout.Label("F1 - Toggle This Panel", labelStyle);

            GUILayout.EndScrollView();

            GUI.DragWindow();
        }

        void OnDestroy()
        {
            if (Instance == this)
                Instance = null;

            // Cleanup textures
            if (backgroundTexture != null)
                Destroy(backgroundTexture);
            if (buttonTexture != null)
                Destroy(buttonTexture);
            if (buttonHoverTexture != null)
                Destroy(buttonHoverTexture);
        }

        // Call this if you change colors at runtime
        public void UpdateAppearance()
        {
            RefreshStyles();
        }
    }

    /// <summary>
    /// Interface for health systems - implement on your enemies/NPCs
    /// </summary>
    public interface IHealth
    {
        void TakeDamage(int amount);
        void Heal(int amount);
        int CurrentHealth { get; }
        int MaxHealth { get; }
    }
}
