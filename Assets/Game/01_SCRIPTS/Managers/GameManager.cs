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

        [Tooltip("Canvas to parent the Player UI under. Assign in inspector.")]
        [SerializeField] private Transform gameplayCanvasTransform;

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

            EnsurePlayerUI();

            SpawnPlayer();
            SetGameState(GameState.Playing);
        }

        private void EnsurePlayerUI()
        {
            if (playerUIInstance != null) return;

            if (gameplayCanvasTransform == null)
            {
                Debug.LogError("GameManager: uiCanvas is not assigned. Assign a Canvas in the inspector.");
                return;
            }

            if (playerUIPrefab == null)
            {
                Debug.LogError("GameManager: playerUIPrefab is not assigned.");
                return;
            }

            var uiGO = Instantiate(playerUIPrefab, gameplayCanvasTransform.transform);
            uiGO.name = "Player UI";
            playerUIInstance = uiGO.GetComponent<PlayerUI>();
            if (playerUIInstance == null)
                Debug.LogError("Player UI prefab is missing a PlayerUI component.");
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

            // Instantiate only once
            if (currentPlayer == null)
            {
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

                // Subscribe once
                SubscribeToPlayer(currentPlayer);

                // Bind the existing UI once
                EnsurePlayerUI();
                if (playerUIInstance != null)
                    playerUIInstance.BindToPlayer(currentPlayer);
            }

            // Place + revive mechanics (resets health/state/velocity)
            currentPlayer.ReviveAt(spawnPosition);

            // Explicitly hand control back (enables input/move; applies i-frames)
            currentPlayer.Activate();

            OnPlayerSpawned?.Invoke(currentPlayer);

            if (currentPlayer.Stats != null)
            {

                currentPlayer.State.HasDrone = currentPlayer.Stats.HasDrone;
               // Debug.Log("Game Manager checking drone = " + currentPlayer.State.HasDrone);
            }

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

            if (currentPlayer != null) currentPlayer.Deactivate();

            if (respawnRoutine != null) StopCoroutine(respawnRoutine);
            respawnRoutine = StartCoroutine(SoftRespawnAfterDelay(respawnDelay));
        }

        private IEnumerator SoftRespawnAfterDelay(float delaySeconds)
        {
            // Wait in real-time (unaffected by Time.timeScale)
            float end = Time.realtimeSinceStartup + Mathf.Max(0f, delaySeconds);
            while (Time.realtimeSinceStartup < end)
                yield return null;

            if (currentState != GameState.Playing)
            {
                respawnRoutine = null;
                yield break;
            }

            Vector3 spawnPosition = GetSpawnPosition();

            // If somehow the player got destroyed, recreate it 
            if (currentPlayer == null)
            {
                if (playerPrefab == null)
                {
                    Debug.LogError("No player prefab assigned to GameManager!");
                    respawnRoutine = null;
                    yield break;
                }

                GameObject playerObject = Instantiate(playerPrefab, spawnPosition, Quaternion.identity);
                currentPlayer = playerObject.GetComponent<PlayerCharacter>();
                if (currentPlayer == null)
                {
                    Debug.LogError("Player prefab doesn't have PlayerCharacter component!");
                    respawnRoutine = null;
                    yield break;
                }

                SubscribeToPlayer(currentPlayer);

                // Make sure UI is ready/bound
                EnsurePlayerUI();
                if (playerUIInstance != null)
                    playerUIInstance.BindToPlayer(currentPlayer);
            }

            // Reset gameplay state at spawn (HP, flags, teleport, velocity)
            currentPlayer.ReviveAt(spawnPosition);

            // Give control back (enables input/move; applies i-frames)
            currentPlayer.Activate();

            OnPlayerSpawned?.Invoke(currentPlayer);
            Debug.Log($"Player respawned at {spawnPosition}");

            respawnRoutine = null;
        }

        //Input 

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

        // Use soft respawn instead of re-instantiating
        public void RestartLevel()
        {
            Debug.Log("Restarting level...");

            currentSpawnIndex = 0;

            // Ensure UI exists and is parented under the assigned canvas
            EnsurePlayerUI();

            // If player is alive, deactivate first
            if (currentPlayer != null)
                currentPlayer.Deactivate();

            // Cancel any pending respawns
            if (respawnRoutine != null)
                StopCoroutine(respawnRoutine);

            // Soft-respawn immediately (delay = 0)
            respawnRoutine = StartCoroutine(SoftRespawnAfterDelay(0f));

            // Ensure game resumes in play state
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

        void OnDestroy()
        {
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
