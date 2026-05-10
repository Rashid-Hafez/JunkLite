using System;
using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

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
        [SerializeField, Min(0f)] private float deathScreenFallbackDelay = 1.25f;

        [Header("UI - Player HUD")]
        [SerializeField] private GameObject playerUIPrefab;

        [Header("UI - Pause Menu")]
        [SerializeField] private GameObject pauseMenuUIPrefab;

        [Header("UI - Game Over")]
        [SerializeField] private GameObject gameOverUIPrefab;

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
        private GameObject gameOverUIInstance;
        private Button gameOverRestartButton;
        private LoadingScreenUI loadingScreenUIInstance;

        // Game state
        private GameState currentState = GameState.Playing;
        private PlayerCharacter currentPlayer;
        private int currentSpawnIndex = 0;
        private Coroutine respawnRoutine;
        private Coroutine deathRoutine;
        private Coroutine sceneInitializationRoutine;
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

        void OnEnable()
        {
            SceneManager.sceneLoaded += OnSceneLoaded;
        }

        void OnDisable()
        {
            SceneManager.sceneLoaded -= OnSceneLoaded;
        }

        void Start()
        {
            // Subscribe here (not OnEnable) so GameInputManager.Instance is guaranteed to exist
            if (GameInputManager.Instance != null)
                GameInputManager.Instance.OnPauseToggle += HandlePauseToggle;

            InitializeGame();
            SubscribeToCombatTracker();
            PlayLevelMusic();
        }

        void OnDestroy()
        {
            SceneManager.sceneLoaded -= OnSceneLoaded;
            UnsubscribeFromPlayer(currentPlayer);
            UnsubscribeFromCombatTracker();

            if (GameInputManager.Instance != null)
                GameInputManager.Instance.OnPauseToggle -= HandlePauseToggle;
        }

        #endregion

        #region Scene Loading

        private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            Debug.Log($"[GameManager] Scene loaded: {scene.name}");
            if (!gameInitialized) return;

            if (sceneInitializationRoutine != null)
                StopCoroutine(sceneInitializationRoutine);

            sceneInitializationRoutine = StartCoroutine(InitializeForNewSceneAfterLoad());
        }

        private IEnumerator InitializeForNewSceneAfterLoad()
        {
            // Give scene-local managers one frame to finish Awake/Start subscriptions.
            yield return null;

            RefreshSceneReferences();
            InitializeForNewScene();
            sceneInitializationRoutine = null;
        }

        private void RefreshSceneReferences()
        {
            spawnPoints = null;
            if (playerUIInstance != null)
                Destroy(playerUIInstance.gameObject);
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
            HideGameOverUI();
            isLoadingScene = false;

            currentSpawnIndex = 0;

            if (respawnRoutine != null)
            {
                StopCoroutine(respawnRoutine);
                respawnRoutine = null;
            }

            if (deathRoutine != null)
            {
                StopCoroutine(deathRoutine);
                deathRoutine = null;
            }

            UnsubscribeFromPlayer(currentPlayer);
            currentPlayer = null;

            EnsurePlayerUI();
            EnsurePauseMenuUI();
            EnsureGameOverUI();
            SpawnPlayer();
            SetGameState(GameState.Playing);
            PlayerCombatTracker.Instance?.ClearCombatState();
            PlayLevelMusic();
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
            EnsureGameOverUI();
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

        private void EnsureGameOverUI()
        {
            if (gameOverUIPrefab == null)
            {
                Debug.LogWarning("[GameManager] No GameOverUI prefab assigned.");
                return;
            }

            if (gameOverUIInstance != null)
                Destroy(gameOverUIInstance);

            gameOverUIInstance = Instantiate(gameOverUIPrefab, gameplayCanvasTransform);
            gameOverUIInstance.name = "Game Over UI";
            gameOverRestartButton = gameOverUIInstance.GetComponentInChildren<Button>(true);
            if (gameOverRestartButton != null)
                gameOverRestartButton.onClick.AddListener(RestartCurrentScene);
            else
                Debug.LogWarning("[GameManager] GameOverUI prefab has no Button child for restart.");

            HideGameOverUI();
        }

        private void ShowGameOverUI()
        {
            if (gameOverUIInstance != null)
                gameOverUIInstance.SetActive(true);
        }

        private void HideGameOverUI()
        {
            if (gameOverUIInstance != null)
                gameOverUIInstance.SetActive(false);
            else
            {
                gameOverUIInstance = null;
                gameOverRestartButton = null;
            }
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

            // Reset movement axis to match the spawn point's orientation.
            // This ensures XY/ZY config is always correct regardless of where the player died.
            ResetPlayerMovementAxis();

            if (CameraManager.Instance != null)
                CameraManager.Instance.ConnectToPlayer(currentPlayer);

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

        /// <summary>
        /// Returns the Y rotation of the active spawn point.
        /// This is the source of truth for which movement axis the player should start on.
        /// Rotate your SpawnPoint GameObject in the Inspector to set the correct initial facing.
        /// </summary>
        private float GetSpawnRotation()
        {
            if (spawnPoints != null && spawnPoints.Length > 0)
            {
                currentSpawnIndex = Mathf.Clamp(currentSpawnIndex, 0, spawnPoints.Length - 1);
                return spawnPoints[currentSpawnIndex].eulerAngles.y;
            }
            return 0f; // default: XY plane (FreezePositionZ)
        }

        /// <summary>
        /// Resets the player's movement axis and rotation to match the current spawn point.
        /// Fixes the bug where dying in a ZY section respawns the player with ZY config.
        /// </summary>
        private void ResetPlayerMovementAxis()
        {
            if (currentPlayer?.Controller == null) return;
            currentPlayer.Controller.ResetToSpawnOrientation(GetSpawnRotation());
            Debug.Log($"[GameManager] Player movement axis reset to spawn rotation: {GetSpawnRotation()}°");
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

        #region Input

        // All pause input — both Keyboard/Escape and Gamepad/Start — is routed through
        // the 'Pause' action in the Player action map of your Input Actions asset.
        // Make sure both bindings are added there.
        private void HandlePauseToggle()
        {
            if (isLoadingScene) return;

            if (currentState == GameState.Paused)
            {
                ResumeGame();
            }
            else if (currentState == GameState.Playing)
            {
                if (playerUIInstance != null && playerUIInstance.IsInventoryOpen)
                    playerUIInstance.CloseInventory();
                else
                    PauseGame();
            }
        }

        #endregion

        #region Death & Respawn

        private void HandlePlayerDeath()
        {
            if (deathRoutine != null) return;

            Debug.Log("[GameManager] Player died!");
            OnPlayerDied?.Invoke();

            deathRoutine = StartCoroutine(PlayerDeathSequence(currentPlayer));
        }

        private IEnumerator PlayerDeathSequence(PlayerCharacter deadPlayer)
        {
            float delay = deathScreenFallbackDelay;

            // Let all PlayerState.OnDeath subscribers, including animation, react this frame first.
            yield return null;

            var spineAnimation = deadPlayer != null
                ? deadPlayer.GetComponentInChildren<SpineAnimationController>(true)
                : null;

            if (spineAnimation != null && spineAnimation.TryGetDeathAnimationDuration(out float animationDuration))
                delay = animationDuration;

            float endTime = Time.unscaledTime + delay;
            while (Time.unscaledTime < endTime)
                yield return null;

            deadPlayer?.Deactivate();
            SetGameState(GameState.GameOver);
            ShowGameOverUI();
            deathRoutine = null;
        }

        public void KillPlayer()
        {
            if (currentPlayer == null || !currentPlayer.IsAlive) return;
            currentPlayer.Health?.SetToZero();
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

            // Reset movement axis to match the spawn point's orientation.
            // This fixes respawning in ZY config after dying in a ZY camera section.
            ResetPlayerMovementAxis();

            OnPlayerSpawned?.Invoke(currentPlayer);
            Debug.Log($"[GameManager] Player respawned at {spawnPosition}");
            respawnRoutine = null;
        }

        #endregion

        #region Level Management

        public void RestartCurrentScene()
        {
            Debug.Log("[GameManager] Restarting current scene...");

            if (respawnRoutine != null) { StopCoroutine(respawnRoutine); respawnRoutine = null; }
            if (deathRoutine != null) { StopCoroutine(deathRoutine); deathRoutine = null; }

            UnsubscribeFromPlayer(currentPlayer);
            currentPlayer = null;

            LoadLevel(SceneManager.GetActiveScene().buildIndex);
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

            if (currentState != GameState.Playing) SetGameState(GameState.Playing);
            HideGameOverUI();

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

        // Soft-respawn without reloading.
        public void RestartLevel()
        {
            currentSpawnIndex = 0;
            currentPlayer?.Deactivate();

            if (respawnRoutine != null) StopCoroutine(respawnRoutine);
            respawnRoutine = StartCoroutine(SoftRespawnAfterDelay(respawnDelay));
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
            GUILayout.Label("ESC / Start - Pause / Resume");
            GUILayout.EndArea();
        }

        #endregion
    }
}