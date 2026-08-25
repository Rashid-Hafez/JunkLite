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
        [SerializeField] private GameObject gameInputManagerPrefab;

        // Supplied by GameRoot, with a tagged-scene fallback for legacy levels.
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

        /// <summary>The active <see cref="GameInputManager"/> subscribed for pause input.</summary>
        private GameInputManager pauseInputSubscriber;

        // Scene-local configuration. SceneSettings remains as a legacy fallback.
        private LevelContext currentLevelContext;
        private SceneSettings currentSceneSettings;

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
            EnsureGameInputManager();
            // Rebinding is duplicate-safe and also supports legacy scene-local input managers.
            RebindPauseInputSubscription();

            InitializeGame();
            SubscribeToCombatTracker();
            PlayLevelMusic();
        }

        void OnDestroy()
        {
            SceneManager.sceneLoaded -= OnSceneLoaded;
            UnsubscribeFromPlayer(currentPlayer);
            UnsubscribeFromCombatTracker();

            UnsubscribePauseInput();
        }

        #endregion

        #region Pause input

        private void RebindPauseInputSubscription()
        {
            EnsureGameInputManager();
            var input = GameInputManager.Instance;
            if (pauseInputSubscriber == input) return;

            UnsubscribePauseInput();

            pauseInputSubscriber = input;
            if (pauseInputSubscriber != null)
                pauseInputSubscriber.OnPauseToggle += HandlePauseToggle;
        }

        private void UnsubscribePauseInput()
        {
            if (pauseInputSubscriber == null) return;
            pauseInputSubscriber.OnPauseToggle -= HandlePauseToggle;
            pauseInputSubscriber = null;
        }

        #endregion

        #region Scene Loading

        private void EnsureGameInputManager()
        {
            if (GameInputManager.Instance != null) return;

            if (gameInputManagerPrefab != null)
            {
                var inputObject = Instantiate(gameInputManagerPrefab, transform);
                inputObject.name = "Game Input Manager";
                return;
            }

            var inputGO = new GameObject("GameInputManager");
            inputGO.transform.SetParent(transform, false);
            inputGO.AddComponent<GameInputManager>();
        }

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
            currentLevelContext = null;
            currentSceneSettings = null;

            FindGameplayCanvas();
            FindLevelConfiguration();
            FindSpawnPoints();
        }

        private void FindGameplayCanvas()
        {
            if (GameRoot.Instance != null && GameRoot.Instance.GameplayUIRoot != null)
            {
                gameplayCanvasTransform = GameRoot.Instance.GameplayUIRoot;
                return;
            }

            if (gameplayCanvasTransform != null)
                return;

            GameObject canvas = null;
            try
            {
                canvas = GameObject.FindWithTag("GameplayCanvas");
            }
            catch (UnityException)
            {
                // Older projects may not define the tag. The fallback canvas below is sufficient.
            }

            if (canvas != null)
            {
                gameplayCanvasTransform = canvas.transform;
                return;
            }

            var canvasObject = new GameObject(
                "Persistent Gameplay UI",
                typeof(RectTransform),
                typeof(Canvas),
                typeof(CanvasScaler),
                typeof(GraphicRaycaster));

            canvasObject.layer = LayerMask.NameToLayer("UI");
            canvasObject.transform.SetParent(transform, false);

            var runtimeCanvas = canvasObject.GetComponent<Canvas>();
            runtimeCanvas.renderMode = RenderMode.ScreenSpaceOverlay;
            runtimeCanvas.sortingOrder = 10;

            var scaler = canvasObject.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 0.5f;

            gameplayCanvasTransform = canvasObject.transform;
        }

        /// <summary>
        /// Reads the active scene's LevelContext. SceneSettings is supported while
        /// older levels are migrated to the reusable GameRoot workflow.
        /// </summary>
        private void FindLevelConfiguration()
        {
            Scene activeScene = SceneManager.GetActiveScene();
            var contexts = FindObjectsByType<LevelContext>(
                FindObjectsInactive.Exclude,
                FindObjectsSortMode.None);

            foreach (LevelContext context in contexts)
            {
                if (context.gameObject.scene != activeScene) continue;
                currentLevelContext = context;
                break;
            }

            currentSceneSettings = FindFirstObjectByType<SceneSettings>();

            if (currentLevelContext != null)
            {
                Debug.Log(
                    $"[GameManager] LevelContext '{currentLevelContext.DisplayName}' found " +
                    $"(id: {currentLevelContext.LevelId}, training: {currentLevelContext.IsTrainingLevel}, " +
                    $"spawnPlayer: {currentLevelContext.SpawnPlayer}).");
            }
            else if (currentSceneSettings != null)
                Debug.Log($"[GameManager] SceneSettings found — spawnPlayer: {currentSceneSettings.SpawnPlayer}");
            else
                Debug.Log("[GameManager] No LevelContext in scene — using legacy defaults (player spawns).");
        }

        /// <summary>
        /// Returns true if the player and Player HUD should be created in the current scene.
        /// Defaults to true when no SceneSettings component is present.
        /// </summary>
        private bool ShouldSpawnPlayerInScene()
        {
            if (currentLevelContext != null)
                return currentLevelContext.SpawnPlayer;

            return currentSceneSettings == null || currentSceneSettings.SpawnPlayer;
        }

        private void InitializeForNewScene()
        {
            loadingScreenUIInstance?.Hide();
            HideGameOverUI();
            isLoadingScene = false;

            // Re-enable gameplay input now that the scene is ready.
            GameInputManager.Instance?.SetGameplayInputEnabled(true);

            RebindPauseInputSubscription();

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

            // Pause menu and game-over UI are always created (they handle their own visibility).
            EnsurePauseMenuUI();
            EnsureGameOverUI();

            // Player and Player HUD are conditional on this scene's settings.
            if (ShouldSpawnPlayerInScene())
            {
                EnsurePlayerUI();
                SpawnPlayer();
            }
            else
            {
                Debug.Log("[GameManager] Player spawn skipped by the active level configuration.");
            }

            SetGameState(GameState.Playing);
            PlayerCombatTracker.Instance?.ClearCombatState();
            PlayLevelMusic();
        }

        #endregion

        #region Initialization

        private void InitializeGame()
        {
            FindGameplayCanvas();
            FindLevelConfiguration();
            FindSpawnPoints();
            EnsureLoadingScreenUI();
            EnsurePauseMenuUI();
            EnsureGameOverUI();

            if (ShouldSpawnPlayerInScene())
            {
                EnsurePlayerUI();
                SpawnPlayer();
            }
            else
            {
                Debug.Log("[GameManager] Player spawn skipped by the initial level configuration.");
            }

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
                return;

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
                return;

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
            if (currentLevelContext != null)
            {
                var configuredSpawns = currentLevelContext.GetPlayerSpawns();
                if (configuredSpawns.Count > 0)
                {
                    spawnPoints = new Transform[configuredSpawns.Count];
                    for (int i = 0; i < configuredSpawns.Count; i++)
                        spawnPoints[i] = configuredSpawns[i];

                    Debug.Log($"[GameManager] Using {spawnPoints.Length} LevelContext spawn point(s).");
                    return;
                }

                Debug.LogWarning(
                    $"[GameManager] LevelContext '{currentLevelContext.DisplayName}' has no player spawn assigned.");
            }

            Scene activeScene = SceneManager.GetActiveScene();
            var markers = FindObjectsByType<SpawnPoint>(
                FindObjectsInactive.Exclude,
                FindObjectsSortMode.None);
            var sceneMarkers = new System.Collections.Generic.List<SpawnPoint>();

            foreach (SpawnPoint marker in markers)
            {
                if (marker.gameObject.scene == activeScene)
                    sceneMarkers.Add(marker);
            }

            if (sceneMarkers.Count > 0)
            {
                sceneMarkers.Sort((a, b) =>
                {
                    int orderComparison = a.Priority.CompareTo(b.Priority);
                    return orderComparison != 0
                        ? orderComparison
                        : string.CompareOrdinal(a.name, b.name);
                });

                spawnPoints = new Transform[sceneMarkers.Count];
                for (int i = 0; i < sceneMarkers.Count; i++)
                    spawnPoints[i] = sceneMarkers[i].transform;

                Debug.Log($"[GameManager] Found {spawnPoints.Length} typed spawn point(s).");
                return;
            }

            GameObject[] taggedSpawns = Array.Empty<GameObject>();
            try
            {
                taggedSpawns = GameObject.FindGameObjectsWithTag("SpawnPoint");
            }
            catch (UnityException)
            {
                // Typed SpawnPoint components replace the legacy tag.
            }

            if (taggedSpawns.Length > 0)
            {
                Array.Sort(taggedSpawns, (a, b) => string.CompareOrdinal(a.name, b.name));
                spawnPoints = new Transform[taggedSpawns.Length];
                for (int i = 0; i < taggedSpawns.Length; i++)
                    spawnPoints[i] = taggedSpawns[i].transform;

                Debug.Log($"[GameManager] Found {spawnPoints.Length} legacy tagged spawn point(s).");
                return;
            }

            spawnPoints = Array.Empty<Transform>();
            Debug.LogWarning("[GameManager] No spawn points found; player will spawn at world origin.");
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
            currentPlayer.Kill();
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

            // Detach before the scene unloads so we are not left subscribed to a destroyed GameInputManager.
            UnsubscribePauseInput();

            // Lock all gameplay input for the duration of the transition.
            // Input is restored in InitializeForNewScene() once the new scene is ready.
            GameInputManager.Instance?.SetGameplayInputEnabled(false);

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
                // Restore input on the abort path so the player isn't permanently locked.
                GameInputManager.Instance?.SetGameplayInputEnabled(true);
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

            GUILayout.BeginArea(new Rect(Screen.width - 210, 10, 200, 230));
            GUILayout.Label("=== GAME MANAGER ===");
            GUILayout.Label($"State:       {currentState}");
            GUILayout.Label($"Loading:     {isLoadingScene}");
            GUILayout.Label($"Input:       {(GameInputManager.Instance != null ? (GameInputManager.Instance.IsGameplayInputEnabled ? "Enabled" : "LOCKED") : "N/A")}");
            GUILayout.Label($"SpawnPlayer: {ShouldSpawnPlayerInScene()}");
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
