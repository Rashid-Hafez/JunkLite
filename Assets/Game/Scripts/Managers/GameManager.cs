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

        #region Fields

        [Header("Game Settings")]
        [SerializeField] private GameObject playerPrefab;
        [SerializeField] private Transform[] spawnPoints;
        [SerializeField] private float respawnDelay = 3f;

        [Header("UI - Player HUD")]
        [SerializeField] private GameObject playerUIPrefab;

        [Header("UI - Pause Menu")]
        [SerializeField] private GameObject pauseMenuUIPrefab;

        [Header("UI - Loading Screen")]
        [SerializeField] private GameObject loadingScreenUIPrefab;

        [Header("Debug")]
        [SerializeField] private bool showDebugInfo = true;
        [SerializeField] private bool reloadSceneOnDeathTemp = false;
        [SerializeField] private float debugLoadDelay = 2f;

        // Found at runtime via 'GameplayCanvas' tag
        private Transform gameplayCanvasTransform;

        // UI instances
        private PlayerUI playerUIInstance;
        private PauseMenuUI pauseMenuUIInstance;
        private LoadingScreenUI loadingScreenUIInstance;

        // Game state
        private GameState currentState = GameState.Playing;
        private PlayerCharacter currentPlayer;
        private int currentSpawnIndex = 0;
        private Coroutine respawnRoutine;
        private bool gameInitialized = false;
        private bool isLoadingScene = false;

        // Events
        public event Action<GameState> OnGameStateChanged;
        public event Action<PlayerCharacter> OnPlayerSpawned;
        public event Action OnPlayerDied;

        public enum GameState { Playing, Paused, GameOver }

        public GameState CurrentState => currentState;
        public PlayerCharacter Player => currentPlayer;
        public bool IsPlaying => currentState == GameState.Playing;

        #endregion

        #region Lifecycle

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

        void OnEnable() => SceneManager.sceneLoaded += OnSceneLoaded;
        void OnDisable() => SceneManager.sceneLoaded -= OnSceneLoaded;

        void Start()
        {
            InitializeGame();
            SubscribeToCombatTracker();
            PlayLevelMusic();
        }

        void Update() => HandleInput();

        void OnDestroy()
        {
            UnsubscribeFromPlayer(currentPlayer);
            UnsubscribeFromCombatTracker();
        }

        #endregion

        #region Scene Loading

        private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            Debug.Log($"[GameManager] Scene loaded: {scene.name}");
            if (!gameInitialized) return;

            RefreshSceneReferences();
            InitializeForNewScene();
        }

        private void RefreshSceneReferences()
        {
            spawnPoints = null;
            playerUIInstance = null;
            gameplayCanvasTransform = null;

            FindSpawnPoints();
            FindGameplayCanvas();
        }

        private void FindGameplayCanvas()
        {
            var canvas = GameObject.FindWithTag("GameplayCanvas");
            if (canvas != null)
            {
                gameplayCanvasTransform = canvas.transform;
                return;
            }
            Debug.LogWarning("[GameManager] No GameObject tagged 'GameplayCanvas' found in scene.");
        }

        private void InitializeForNewScene()
        {
            loadingScreenUIInstance?.Hide();
            isLoadingScene = false;
            
            currentSpawnIndex = 0;

            if (respawnRoutine != null)
            {
                StopCoroutine(respawnRoutine);
                respawnRoutine = null;
            }

            UnsubscribeFromPlayer(currentPlayer);
            currentPlayer = null;

            EnsurePlayerUI();
            EnsurePauseMenuUI();
            SpawnPlayer();
            SetGameState(GameState.Playing);
        }

        #endregion

        #region Initialization

        private void InitializeGame()
        {
            if (spawnPoints == null || spawnPoints.Length == 0)
                FindSpawnPoints();

            FindGameplayCanvas();
            EnsureLoadingScreenUI();
            EnsurePauseMenuUI();
            EnsurePlayerUI();

            SpawnPlayer();
            SetGameState(GameState.Playing);
            gameInitialized = true;
        }

        private void EnsurePlayerUI()
        {
            if (playerUIInstance != null) return;

            if (gameplayCanvasTransform == null)
            {
                Debug.LogError("[GameManager] GameplayCanvas not found — cannot create Player UI.");
                return;
            }

            if (playerUIPrefab == null)
            {
                Debug.LogError("[GameManager] playerUIPrefab is not assigned.");
                return;
            }

            var uiGO = Instantiate(playerUIPrefab, gameplayCanvasTransform);
            uiGO.name = "Player UI";
            playerUIInstance = uiGO.GetComponent<PlayerUI>();
            if (playerUIInstance == null)
                Debug.LogError("[GameManager] Player UI prefab is missing a PlayerUI component.");
        }

        private void EnsurePauseMenuUI()
        {
            if (pauseMenuUIPrefab == null)
            {
                Debug.LogWarning("[GameManager] No PauseMenuUI prefab assigned.");
                return;
            }

            if (pauseMenuUIInstance != null)
                Destroy(pauseMenuUIInstance.gameObject);

            var go = Instantiate(pauseMenuUIPrefab, gameplayCanvasTransform);
            go.name = "Pause Menu UI";
            pauseMenuUIInstance = go.GetComponent<PauseMenuUI>();
            if (pauseMenuUIInstance == null)
                Debug.LogError("[GameManager] PauseMenuUI prefab is missing a PauseMenuUI component.");
        }

        private void EnsureLoadingScreenUI()
        {
            if (loadingScreenUIInstance != null) return;

            if (loadingScreenUIPrefab == null)
            {
                Debug.LogWarning("[GameManager] No LoadingScreenUI prefab assigned.");
                return;
            }

            var go = Instantiate(loadingScreenUIPrefab);
            go.name = "Loading Screen UI";
            DontDestroyOnLoad(go);
            loadingScreenUIInstance = go.GetComponent<LoadingScreenUI>();
            if (loadingScreenUIInstance == null)
                Debug.LogError("[GameManager] LoadingScreenUI prefab is missing a LoadingScreenUI component.");
        }

        private void FindSpawnPoints()
        {
            var spawnObjects = GameObject.FindGameObjectsWithTag("SpawnPoint");
            if (spawnObjects != null && spawnObjects.Length > 0)
            {
                Array.Sort(spawnObjects, (a, b) => string.CompareOrdinal(a.name, b.name));
                spawnPoints = new Transform[spawnObjects.Length];
                for (int i = 0; i < spawnObjects.Length; i++)
                    spawnPoints[i] = spawnObjects[i].transform;

                Debug.Log($"[GameManager] Found {spawnPoints.Length} spawn points.");
                return;
            }

            spawnPoints = Array.Empty<Transform>();
            Debug.LogWarning("[GameManager] No spawn points found! Tag at least one object as 'SpawnPoint'.");
        }

        #endregion

        #region Player

        public void SpawnPlayer()
        {
            Vector3 spawnPosition = GetSpawnPosition();

            if (currentPlayer == null)
            {
                if (playerPrefab == null)
                {
                    Debug.LogError("[GameManager] No player prefab assigned!");
                    return;
                }

                var playerObject = Instantiate(playerPrefab, spawnPosition, Quaternion.identity);
                currentPlayer = playerObject.GetComponent<PlayerCharacter>();
                if (currentPlayer == null)
                {
                    Debug.LogError("[GameManager] Player prefab missing PlayerCharacter component!");
                    return;
                }

                SubscribeToPlayer(currentPlayer);
                EnsurePlayerUI();
                playerUIInstance?.BindToPlayer(currentPlayer);
            }

            currentPlayer.ReviveAt(spawnPosition);
            currentPlayer.Activate();
            OnPlayerSpawned?.Invoke(currentPlayer);

            if (currentPlayer.Stats != null)
                currentPlayer.PlayerState.HasDrone = currentPlayer.Stats.HasDrone;

            Debug.Log($"[GameManager] Player spawned at {spawnPosition}");
        }

        private void SubscribeToPlayer(PlayerCharacter player)
        {
            if (player?.State != null)
                player.State.OnDeath += HandlePlayerDeath;
        }

        private void UnsubscribeFromPlayer(PlayerCharacter player)
        {
            if (player?.State != null)
                player.State.OnDeath -= HandlePlayerDeath;
        }

        private Vector3 GetSpawnPosition()
        {
            if (spawnPoints != null && spawnPoints.Length > 0)
            {
                currentSpawnIndex = Mathf.Clamp(currentSpawnIndex, 0, spawnPoints.Length - 1);
                return spawnPoints[currentSpawnIndex].position;
            }
            Debug.LogWarning("[GameManager] No spawn points available, spawning at origin!");
            return Vector3.zero;
        }

        public void SetSpawnPoint(int index)
        {
            if (spawnPoints != null && index >= 0 && index < spawnPoints.Length)
                currentSpawnIndex = index;
        }

        #endregion

        #region Game State

        public void SetGameState(GameState newState)
        {
            if (currentState == newState) return;

            GameState previous = currentState;
            currentState = newState;

            switch (newState)
            {
                case GameState.Playing: Time.timeScale = 1f; break;
                case GameState.Paused: Time.timeScale = 0f; break;
                case GameState.GameOver: Time.timeScale = 1f; break;
            }

            Debug.Log($"[GameManager] State: {previous} -> {newState}");
            OnGameStateChanged?.Invoke(newState);
        }

        public void PauseGame() => SetGameState(GameState.Paused);
        public void ResumeGame() => SetGameState(GameState.Playing);

        #endregion

        #region Death & Respawn

        private void HandlePlayerDeath()
        {
            Debug.Log("[GameManager] Player died!");
            OnPlayerDied?.Invoke();

            if (ShouldReloadSceneOnDeath())
            {
                SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);
                return;
            }

            currentPlayer?.Deactivate();

            if (respawnRoutine != null) StopCoroutine(respawnRoutine);
            respawnRoutine = StartCoroutine(SoftRespawnAfterDelay(respawnDelay));
        }

        private bool ShouldReloadSceneOnDeath()
        {
            if (reloadSceneOnDeathTemp) return true;
            if (currentPlayer != null && currentPlayer.ReloadSceneOnDeathTemp) return true;
            return false;
        }

        private IEnumerator SoftRespawnAfterDelay(float delaySeconds)
        {
            float end = Time.realtimeSinceStartup + Mathf.Max(0f, delaySeconds);
            while (Time.realtimeSinceStartup < end)
                yield return null;

            if (currentState != GameState.Playing)
            {
                respawnRoutine = null;
                yield break;
            }

            Vector3 spawnPosition = GetSpawnPosition();

            if (currentPlayer == null)
            {
                if (playerPrefab == null)
                {
                    Debug.LogError("[GameManager] No player prefab assigned!");
                    respawnRoutine = null;
                    yield break;
                }

                var playerObject = Instantiate(playerPrefab, spawnPosition, Quaternion.identity);
                currentPlayer = playerObject.GetComponent<PlayerCharacter>();
                if (currentPlayer == null)
                {
                    Debug.LogError("[GameManager] Player prefab missing PlayerCharacter component!");
                    respawnRoutine = null;
                    yield break;
                }

                SubscribeToPlayer(currentPlayer);
                EnsurePlayerUI();
                playerUIInstance?.BindToPlayer(currentPlayer);
            }

            currentPlayer.ReviveAt(spawnPosition);
            currentPlayer.Activate();
            OnPlayerSpawned?.Invoke(currentPlayer);
            Debug.Log($"[GameManager] Player respawned at {spawnPosition}");
            respawnRoutine = null;
        }

        #endregion

        #region Input

        private void HandleInput()
        {
            if (isLoadingScene) return;

            if (Input.GetKeyDown(KeyCode.Escape))
            {
                if (currentState == GameState.Paused) ResumeGame();
                else if (currentState == GameState.Playing) PauseGame();
            }
        }

        #endregion

        #region Level Management

        public void RestartCurrentScene()
        {
            Debug.Log("[GameManager] Restarting current scene...");

            if (respawnRoutine != null)
            {
                StopCoroutine(respawnRoutine);
                respawnRoutine = null;
            }

            UnsubscribeFromPlayer(currentPlayer);
            currentPlayer = null;

            if (currentState == GameState.Paused) ResumeGame();

            SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);
        }

        public void RestartGame()
        {
            Debug.Log("[GameManager] Restarting game from beginning...");
            LoadLevel(0);
        }

        public void LoadLevel(string sceneName)
        {
            Debug.Log($"[GameManager] Loading level: {sceneName}");
            StartCoroutine(LoadLevelWithScreen(sceneName, -1));
        }

        public void LoadLevel(int sceneIndex)
        {
            Debug.Log($"[GameManager] Loading level index: {sceneIndex}");
            StartCoroutine(LoadLevelWithScreen(null, sceneIndex));
        }

        private IEnumerator LoadLevelWithScreen(string sceneName, int sceneIndex)
        {
            if (isLoadingScene) yield break;
            isLoadingScene = true;

            if (currentState == GameState.Paused) ResumeGame();

            loadingScreenUIInstance?.Show();

            // Wait for the video to finish before starting the scene load
            while (loadingScreenUIInstance != null && !loadingScreenUIInstance.IsVideoFinished)
                yield return null;

            var asyncOp = string.IsNullOrEmpty(sceneName)
                ? SceneManager.LoadSceneAsync(sceneIndex)
                : SceneManager.LoadSceneAsync(sceneName);

            if (asyncOp == null)
            {
                Debug.LogError($"[GameManager] Failed to load scene — is it added to Build Settings?");
                loadingScreenUIInstance?.Hide();
                isLoadingScene = false;
                yield break;
            }

            asyncOp.allowSceneActivation = false;

            float elapsed = 0f;
            while (!(asyncOp.progress >= 0.9f && elapsed >= debugLoadDelay))
            {
                elapsed += Time.unscaledDeltaTime;
                yield return null;
            }

            asyncOp.allowSceneActivation = true;
        }

        // Soft-respawn without reloading — used for death respawn only
        public void RestartLevel()
        {
            currentSpawnIndex = 0;
            currentPlayer?.Deactivate();

            if (respawnRoutine != null) StopCoroutine(respawnRoutine);
            respawnRoutine = StartCoroutine(SoftRespawnAfterDelay(0f));
            SetGameState(GameState.Playing);
        }

        public void QuitGame()
        {
            Debug.Log("[GameManager] Quitting game...");
            Application.Quit();
        }

        #endregion

        #region Music

        private void SubscribeToCombatTracker()
        {
            if (PlayerCombatTracker.Instance == null) return;
            PlayerCombatTracker.Instance.OnCombatStarted += OnCombatStarted;
            PlayerCombatTracker.Instance.OnCombatEnded += OnCombatEnded;
        }

        private void UnsubscribeFromCombatTracker()
        {
            if (PlayerCombatTracker.Instance == null) return;
            PlayerCombatTracker.Instance.OnCombatStarted -= OnCombatStarted;
            PlayerCombatTracker.Instance.OnCombatEnded -= OnCombatEnded;
        }

        private void OnCombatStarted()
        {
            var entry = GetCombatMusicEntry();
            if (entry != null && entry.IsValid)
                AudioManager.Instance?.CrossfadeToMusic(entry);
        }

        private void OnCombatEnded() => PlayLevelMusic();

        private void PlayLevelMusic()
        {
            var entry = GetLevelMusicEntry();
            if (entry != null && entry.IsValid)
                AudioManager.Instance?.CrossfadeToMusic(entry);
        }

        private SoundEntry GetLevelMusicEntry()
        {
            if (AudioManager.Instance?.Music == null) return null;
            var m = AudioManager.Instance.Music;
            return m.level != null && m.level.IsValid ? m.level : m.gameplay;
        }

        private SoundEntry GetCombatMusicEntry()
        {
            if (AudioManager.Instance?.Music == null) return null;
            var m = AudioManager.Instance.Music;
            return m.combat != null && m.combat.IsValid ? m.combat : m.boss;
        }

        #endregion

        #region Debug GUI

        void OnGUI()
        {
            if (!showDebugInfo) return;

            GUILayout.BeginArea(new Rect(Screen.width - 210, 10, 200, 210));
            GUILayout.Label("=== GAME MANAGER ===");
            GUILayout.Label($"State:       {currentState}");
            GUILayout.Label($"Loading:     {isLoadingScene}");
            GUILayout.Label($"Player:      {(currentPlayer != null ? "Alive" : "None")}");
            GUILayout.Label($"Spawn Index: {currentSpawnIndex}");
            GUILayout.Label($"HUD:         {(playerUIInstance != null ? "Ready" : "Missing")}");
            GUILayout.Label($"PauseMenu:   {(pauseMenuUIInstance != null ? "Ready" : "Missing")}");
            GUILayout.Label($"LoadScreen:  {(loadingScreenUIInstance != null ? "Ready" : "Missing")}");
            GUILayout.Space(6);
            GUILayout.Label("ESC - Pause / Resume");
            GUILayout.EndArea();
        }

        #endregion
    }
}