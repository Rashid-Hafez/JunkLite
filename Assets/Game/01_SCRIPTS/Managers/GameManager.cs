using System;
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
        [SerializeField] private float respawnDelay = 3f;

        [Header("Debug")]
        [SerializeField] private bool showDebugInfo = true;

        // Game state
        private GameState currentState = GameState.Playing;
        private PlayerCharacter currentPlayer;
        private int currentSpawnIndex = 0;

        // Events
        public event Action<GameState> OnGameStateChanged;
        public event Action<PlayerCharacter> OnPlayerSpawned;
        public event Action OnPlayerDied;

        public enum GameState
        {
            Playing,
            Paused,
            GameOver
        }

        // Properties
        public GameState CurrentState => currentState;
        public PlayerCharacter Player => currentPlayer;
        public bool IsPlaying => currentState == GameState.Playing;

        private void Awake()
        {
            // Singleton pattern
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }

            Instance = this;
            DontDestroyOnLoad(gameObject);
        }

        private void Start()
        {
            InitializeGame();
        }

        private void Update()
        {
            HandleInput();
        }

        private void InitializeGame()
        {
            // Find spawn points if not assigned
            if (spawnPoints == null || spawnPoints.Length == 0)
            {
                FindSpawnPoints();
            }

            // Spawn player
            SpawnPlayer();

            // Set initial state
            SetGameState(GameState.Playing);

            Debug.Log("Game initialized successfully!");
        }

        private void FindSpawnPoints()
        {
            // Auto-find spawn points tagged as "SpawnPoint"
            GameObject[] spawnObjects = GameObject.FindGameObjectsWithTag("SpawnPoint");
            spawnPoints = new Transform[spawnObjects.Length];

            for (int i = 0; i < spawnObjects.Length; i++)
            {
                spawnPoints[i] = spawnObjects[i].transform;
            }

            if (spawnPoints.Length == 0)
            {
                Debug.LogWarning("No spawn points found! Create GameObjects with 'SpawnPoint' tag or assign spawn points manually.");
            }
        }

        public void SpawnPlayer()
        {
            // Get spawn position
            Vector3 spawnPosition = GetSpawnPosition();

            // Destroy existing player if any
            if (currentPlayer != null)
            {
                Destroy(currentPlayer.gameObject);
            }

            // Spawn new player
            if (playerPrefab != null)
            {
                GameObject playerObject = Instantiate(playerPrefab, spawnPosition, Quaternion.identity);
                currentPlayer = playerObject.GetComponent<PlayerCharacter>();

                if (currentPlayer != null)
                {
                    // Subscribe to player death
                    currentPlayer.System.OnDeath += HandlePlayerDeath;
                    OnPlayerSpawned?.Invoke(currentPlayer);
                    Debug.Log($"Player spawned at {spawnPosition}");
                }
                else
                {
                    Debug.LogError("Player prefab doesn't have PlayerCharacter component!");
                }
            }
            else
            {
                Debug.LogError("No player prefab assigned to GameManager!");
            }
        }

        private Vector3 GetSpawnPosition()
        {
            if (spawnPoints != null && spawnPoints.Length > 0)
            {
                // Use current spawn index (for checkpoints)
                currentSpawnIndex = Mathf.Clamp(currentSpawnIndex, 0, spawnPoints.Length - 1);
                return spawnPoints[currentSpawnIndex].position;
            }

            // Fallback to world origin
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

        public void SetGameState(GameState newState)
        {
            if (currentState != newState)
            {
                GameState previousState = currentState;
                currentState = newState;

                HandleStateChange(previousState, newState);
                OnGameStateChanged?.Invoke(newState);
            }
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

        private void HandlePlayerDeath()
        {
            Debug.Log("Player died!");
            OnPlayerDied?.Invoke();

            // Respawn after delay
            Invoke(nameof(RespawnPlayer), respawnDelay);
        }

        private void RespawnPlayer()
        {
            if (currentState == GameState.Playing)
            {
                SpawnPlayer();
            }
        }

        private void HandleInput()
        {
            // Pause/Unpause with Escape
            if (Input.GetKeyDown(KeyCode.Escape))
            {
                if (currentState == GameState.Playing)
                {
                    SetGameState(GameState.Paused);
                }
                else if (currentState == GameState.Paused)
                {
                    SetGameState(GameState.Playing);
                }
            }

            // Restart with R
            if (Input.GetKeyDown(KeyCode.R))
            {
                RestartLevel();
            }
        }

        public void PauseGame()
        {
            SetGameState(GameState.Paused);
        }

        public void ResumeGame()
        {
            SetGameState(GameState.Playing);
        }

        public void RestartLevel()
        {
            Debug.Log("Restarting level...");

            // Reset spawn point to beginning
            currentSpawnIndex = 0;

            // Respawn player immediately
            SpawnPlayer();

            // Resume game if paused
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

        private void OnDestroy()
        {
            // Clean up player death subscription
            if (currentPlayer != null)
            {
                currentPlayer.System.OnDeath -= HandlePlayerDeath;
            }
        }

        // Debug GUI
        private void OnGUI()
        {
            if (!showDebugInfo) return;

            GUILayout.BeginArea(new Rect(Screen.width - 200, 10, 190, 150));
            GUILayout.Label("=== GAME MANAGER ===");
            GUILayout.Label($"State: {currentState}");
            GUILayout.Label($"Player: {(currentPlayer != null ? "Alive" : "None")}");
            GUILayout.Label($"Spawn Point: {currentSpawnIndex}");

            GUILayout.Space(10);
            GUILayout.Label("Controls:");
            GUILayout.Label("ESC - Pause/Resume");
            GUILayout.Label("R - Restart Level");

            GUILayout.EndArea();
        }
    }
}