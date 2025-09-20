using System;
using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace junklite
{
    [DefaultExecutionOrder(1)]
    public class GameManager : MonoBehaviour
    {
        public static GameManager Instance { get; private set; }

        [Header("Game Settings")]
        [SerializeField] private GameObject playerPrefab;
        [SerializeField] private Transform[] spawnPoints;
        [SerializeField] private float respawnDelay = 3f; // seconds (real time)

        [Header("UI")]
        [Tooltip("Player HUD prefab (must have a PlayerUI component on root).")]
        [SerializeField] private GameObject playerUIPrefab;
        [Tooltip("Optional: if null, the first Canvas found in the scene will be used.")]
        [SerializeField] private Canvas uiCanvasOverride;

        // Keep a single instance of the Player UI around
        private PlayerUI playerUIInstance;

        [Header("Debug")]
        [SerializeField] private bool showDebugInfo = true;

        // Game state
        private GameState currentState = GameState.Playing;
        private PlayerCharacter currentPlayer;
        private int currentSpawnIndex = 0;
        private Coroutine respawnRoutine;

        // Events
        public event Action<GameState> OnGameStateChanged;
        public event Action<PlayerCharacter> OnPlayerSpawned;
        public event Action OnPlayerDied;

        public enum GameState { Playing, Paused, GameOver }

        // Properties
        public GameState CurrentState => currentState;
        public PlayerCharacter Player => currentPlayer;
        public bool IsPlaying => currentState == GameState.Playing;

        void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;
            DontDestroyOnLoad(gameObject);

            // If you change scenes, make sure the UI is parented to the new scene's Canvas
            SceneManager.sceneLoaded += OnSceneLoaded;
        }

        void Start()
        {
            InitializeGame();
        }

        void Update()
        {
            HandleInput();
        }

        // ---- Init & Spawning -------------------------------------------------

        private void InitializeGame()
        {
            if (spawnPoints == null || spawnPoints.Length == 0)
                FindSpawnPoints();

            // Create the Player UI once
            EnsurePlayerUI();

            // Spawn first player
            SpawnPlayer();
            SetGameState(GameState.Playing);

            Debug.Log("Game initialized successfully!");
        }

        private void EnsurePlayerUI()
        {
            if (playerUIInstance != null) return;

            var canvas = uiCanvasOverride != null ? uiCanvasOverride : FindObjectOfType<Canvas>();
            if (canvas == null)
            {
                Debug.LogWarning("No Canvas found for Player UI. HUD will not be created.");
                return;
            }

            if (playerUIPrefab == null)
            {
                Debug.LogWarning("No Player UI Prefab assigned in GameManager.");
                return;
            }

            var uiGO = Instantiate(playerUIPrefab, canvas.transform);
            uiGO.name = "Player UI";
            playerUIInstance = uiGO.GetComponent<PlayerUI>();
            if (playerUIInstance == null)
                Debug.LogError("Player UI prefab is missing a PlayerUI component.");
        }

        private void ReparentUIToActiveCanvas()
        {
            if (playerUIInstance == null) return;

            var canvas = uiCanvasOverride != null ? uiCanvasOverride : FindObjectOfType<Canvas>();
            if (canvas != null && playerUIInstance.transform.parent != canvas.transform)
            {
                playerUIInstance.transform.SetParent(canvas.transform, worldPositionStays: false);
            }
        }

        private void FindSpawnPoints()
        {
            var spawnObjects = GameObject.FindGameObjectsWithTag("SpawnPoint");
            spawnPoints = new Transform[spawnObjects.Length];
            for (int i = 0; i < spawnObjects.Length; i++)
                spawnPoints[i] = spawnObjects[i].transform;

            if (spawnPoints.Length == 0)
                Debug.LogWarning("No spawn points found! Tag some objects as 'SpawnPoint' or assign manually.");
        }

        public void SpawnPlayer()
        {
            Vector3 spawnPosition = GetSpawnPosition();

            // Clean up previous player (and its subscriptions) if any
            if (currentPlayer != null)
            {
                UnsubscribeFromPlayer(currentPlayer);
                Destroy(currentPlayer.gameObject);
                currentPlayer = null;
            }

            if (playerPrefab == null)
            {
                Debug.LogError("No player prefab assigned to GameManager!");
                return;
            }

            GameObject playerObject = Instantiate(playerPrefab, spawnPosition, Quaternion.identity);
            currentPlayer = playerObject.GetComponent<PlayerCharacter>();

            if (currentPlayer == null)
            {
                Debug.LogError("Player prefab doesn't have PlayerCharacter component!");
                return;
            }

            // Subscribe to the new player's death (CharacterState forwards Attribute death)
            SubscribeToPlayer(currentPlayer);

            // Bind the existing Player UI to this new player (no new UI instances)
            if (playerUIInstance != null)
                playerUIInstance.BindToPlayer(currentPlayer);

            OnPlayerSpawned?.Invoke(currentPlayer);
            Debug.Log($"Player spawned at {spawnPosition}");
        }

        private void SubscribeToPlayer(PlayerCharacter player)
        {
            if (player != null && player.State != null)
                player.State.OnDeath += HandlePlayerDeath;
        }

        private void UnsubscribeFromPlayer(PlayerCharacter player)
        {
            if (player != null && player.State != null)
                player.State.OnDeath -= HandlePlayerDeath;
        }

        private Vector3 GetSpawnPosition()
        {
            if (spawnPoints != null && spawnPoints.Length > 0)
            {
                currentSpawnIndex = Mathf.Clamp(currentSpawnIndex, 0, spawnPoints.Length - 1);
                return spawnPoints[currentSpawnIndex].position;
            }
            Debug.LogWarning("No spawn points available, spawning at origin!");
            return Vector3.zero;
        }

        public void SetSpawnPoint(int index)
        {
            if (spawnPoints != null && index >= 0 && index < spawnPoints.Length)
            {
                currentSpawnIndex = index;
                Debug.Log($"Spawn point set to index {index}");
            }
        }

        // ---- Game State ------------------------------------------------------

        public void SetGameState(GameState newState)
        {
            if (currentState == newState) return;

            GameState previous = currentState;
            currentState = newState;

            HandleStateChange(previous, newState);
            OnGameStateChanged?.Invoke(newState);
        }

        private void HandleStateChange(GameState from, GameState to)
        {
            switch (to)
            {
                case GameState.Playing:
                    Time.timeScale = 1f;
                    break;

                case GameState.Paused:
                    Time.timeScale = 0f;
                    break;

                case GameState.GameOver:
                    Time.timeScale = 1f;
                    Debug.Log("Game Over!");
                    break;
            }
            Debug.Log($"Game state changed from {from} to {to}");
        }

        // ---- Death & Respawn -------------------------------------------------

        private void HandlePlayerDeath()
        {
            Debug.Log("Player died!");
            OnPlayerDied?.Invoke();

            if (respawnRoutine != null) StopCoroutine(respawnRoutine);
            respawnRoutine = StartCoroutine(RespawnAfterDelay(respawnDelay));
        }

        private IEnumerator RespawnAfterDelay(float delaySeconds)
        {
            // Wait in real-time so pausing won't block respawn
            float end = Time.realtimeSinceStartup + Mathf.Max(0f, delaySeconds);
            while (Time.realtimeSinceStartup < end)
                yield return null;

            if (currentState == GameState.Playing)
                SpawnPlayer();

            respawnRoutine = null;
        }

        // ---- Input -----------------------------------------------------------

        private void HandleInput()
        {
            if (Input.GetKeyDown(KeyCode.Escape))
            {
                if (currentState == GameState.Playing) SetGameState(GameState.Paused);
                else if (currentState == GameState.Paused) SetGameState(GameState.Playing);
            }

            if (Input.GetKeyDown(KeyCode.R))
                RestartLevel();
        }

        public void PauseGame() => SetGameState(GameState.Paused);
        public void ResumeGame() => SetGameState(GameState.Playing);

        public void RestartLevel()
        {
            Debug.Log("Restarting level...");

            currentSpawnIndex = 0;

            // Ensure UI exists and is under the current scene's Canvas
            EnsurePlayerUI();
            ReparentUIToActiveCanvas();

            // Respawn player immediately
            SpawnPlayer();

            SetGameState(GameState.Playing);
        }

        public void LoadLevel(string sceneName)
        {
            Debug.Log($"Loading level: {sceneName}");
            SceneManager.LoadScene(sceneName);
        }

        public void LoadLevel(int sceneIndex)
        {
            Debug.Log($"Loading level: {sceneIndex}");
            SceneManager.LoadScene(sceneIndex);
        }

        public void QuitGame()
        {
            Debug.Log("Quitting game...");
            Application.Quit();
        }

        private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            // After scene load, make sure the UI lives under the new scene's Canvas
            ReparentUIToActiveCanvas();

            // If player exists, re-bind (useful when you load a scene that already has a player)
            if (playerUIInstance != null && currentPlayer != null)
                playerUIInstance.BindToPlayer(currentPlayer);
        }

        void OnDestroy()
        {
            SceneManager.sceneLoaded -= OnSceneLoaded;

            if (currentPlayer != null)
                UnsubscribeFromPlayer(currentPlayer);
        }

        // ---- Debug GUI -------------------------------------------------------

        void OnGUI()
        {
            if (!showDebugInfo) return;

            GUILayout.BeginArea(new Rect(Screen.width - 200, 10, 190, 180));
            GUILayout.Label("=== GAME MANAGER ===");
            GUILayout.Label($"State: {currentState}");
            GUILayout.Label($"Player: {(currentPlayer != null ? "Alive" : "None")}");
            GUILayout.Label($"Spawn Point: {currentSpawnIndex}");
            GUILayout.Label($"UI: {(playerUIInstance != null ? "Ready" : "Missing")}");
            GUILayout.Space(10);
            GUILayout.Label("Controls:");
            GUILayout.Label("ESC - Pause/Resume");
            GUILayout.Label("R   - Restart Level");
            GUILayout.EndArea();
        }
    }
}
