using System;
using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace junklite
{
    [DefaultExecutionOrder(1)]
    [RequireComponent(typeof(PlayerLifecycle))]
    [RequireComponent(typeof(GameUIManager))]
    public class GameManager : MonoBehaviour
    {
        public static GameManager Instance { get; private set; }

        #region Fields

        [Header("Legacy Player Lifecycle Migration")]
        [Tooltip("Temporary serialized fallback for scenes not yet rebuilt with PlayerLifecycle.")]
        [SerializeField, HideInInspector] private GameObject playerPrefab;
        [SerializeField, HideInInspector] private float respawnDelay = 3f;
        [SerializeField, HideInInspector, Min(0f)] private float deathScreenFallbackDelay = 1.25f;

        [Header("Legacy UI Lifecycle Migration")]
        [Tooltip("Temporary serialized fallbacks for scenes not yet rebuilt with GameUIManager.")]
        [SerializeField, HideInInspector] private GameObject playerUIPrefab;
        [SerializeField, HideInInspector] private GameObject pauseMenuUIPrefab;
        [SerializeField, HideInInspector] private GameObject loadingScreenUIPrefab;

        [Header("Debug")]
        [SerializeField] private bool showDebugInfo = true;
        [SerializeField] private GameObject gameInputManagerPrefab;

        // Game state
        private GameState currentState = GameState.Playing;
        private Coroutine sceneInitializationRoutine;
        private bool gameInitialized = false;
        private bool isLoadingScene = false;
        private AsyncOperation preloadOperation;
        private PlayerLifecycle playerLifecycle;
        private GameUIManager gameUIManager;

        /// <summary>The active <see cref="GameInputManager"/> subscribed for pause input.</summary>
        private GameInputManager pauseInputSubscriber;

        // Scene-local configuration. SceneSettings remains as a legacy fallback.
        private LevelContext currentLevelContext;
        private SceneSettings currentSceneSettings;

        // Events
        public event Action<GameState> OnGameStateChanged;

        public enum GameState { Playing, Paused, GameOver }

        public GameState CurrentState => currentState;
        public bool IsPlaying => currentState == GameState.Playing;

        #endregion

        #region Lifecycle

        void Awake()
        {
            if (Instance != null && Instance != this)
            {
                // GameManager may be composed onto GameRoot with other services.
                // A duplicate manager must never destroy that entire host object.
                enabled = false;
                Destroy(this);
                return;
            }
            Instance = this;
            DontDestroyOnLoad(gameObject);

            bool lifecycleAdded = GetComponent<PlayerLifecycle>() == null;
            playerLifecycle = GetComponent<PlayerLifecycle>() ?? gameObject.AddComponent<PlayerLifecycle>();
            playerLifecycle.ApplyDefaultsIfMissing(
                playerPrefab,
                respawnDelay,
                deathScreenFallbackDelay,
                lifecycleAdded);

            gameUIManager = GetComponent<GameUIManager>() ?? gameObject.AddComponent<GameUIManager>();
            gameUIManager.ApplyDefaultsIfMissing(
                playerUIPrefab,
                pauseMenuUIPrefab,
                loadingScreenUIPrefab);

            SubscribeToPlayerLifecycle();
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
            UnsubscribeFromPlayerLifecycle();
            UnsubscribeFromCombatTracker();

            UnsubscribePauseInput();

            // Domain reload is disabled; never leave a destroyed manager as Instance.
            if (Instance == this)
                Instance = null;
        }

        private void SubscribeToPlayerLifecycle()
        {
            if (playerLifecycle == null)
                return;

            playerLifecycle.PlayerDeathPresentationCompleted += HandlePlayerDeathPresentationCompleted;
        }

        private void UnsubscribeFromPlayerLifecycle()
        {
            if (playerLifecycle == null)
                return;

            playerLifecycle.PlayerDeathPresentationCompleted -= HandlePlayerDeathPresentationCompleted;
        }

        private void HandlePlayerDeathPresentationCompleted(PlayerCharacter player)
        {
            if (isLoadingScene)
                return;

            SetGameState(GameState.Playing);
            playerLifecycle?.RespawnAtCurrentSpawn();
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

            // Keep the loading screen up for one more frame so cameras and the HUD
            // rebind to the spawned player before the first visible frame.
            yield return null;
            gameUIManager?.EndSceneTransition();
            sceneInitializationRoutine = null;
        }

        private void RefreshSceneReferences()
        {
            currentLevelContext = null;
            currentSceneSettings = null;

            FindLevelConfiguration();
            playerLifecycle?.ConfigureScene(currentLevelContext, currentSceneSettings);
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
        }

        /// <summary>
        /// Returns true if the player and Player HUD should be created in the current scene.
        /// Defaults to true when no SceneSettings component is present.
        /// </summary>
        private bool ShouldSpawnPlayerInScene()
        {
            return playerLifecycle != null && playerLifecycle.ShouldSpawnPlayer;
        }

        private void InitializeForNewScene()
        {
            isLoadingScene = false;

            // Re-enable gameplay input now that the scene is ready.
            GameInputManager.Instance?.SetGameplayInputEnabled(true);

            RebindPauseInputSubscription();

            bool shouldSpawnPlayer = ShouldSpawnPlayerInScene();
            gameUIManager?.InitializeForScene(shouldSpawnPlayer);

            if (shouldSpawnPlayer)
            {
                playerLifecycle?.SpawnPlayer();
            }

            SetGameState(GameState.Playing);
            PlayLevelMusic();
        }

        #endregion

        #region Initialization

        private void InitializeGame()
        {
            FindLevelConfiguration();
            playerLifecycle?.ConfigureScene(currentLevelContext, currentSceneSettings);

            bool shouldSpawnPlayer = ShouldSpawnPlayerInScene();
            gameUIManager?.InitializeForScene(shouldSpawnPlayer);

            if (shouldSpawnPlayer)
            {
                playerLifecycle?.SpawnPlayer();
            }

            SetGameState(GameState.Playing);
            gameInitialized = true;
        }

        #endregion

        #region Game State

        public void SetGameState(GameState newState)
        {
            if (currentState == newState) return;

            currentState = newState;

            switch (newState)
            {
                case GameState.Playing: Time.timeScale = 1f; break;
                case GameState.Paused: Time.timeScale = 0f; break;
                case GameState.GameOver: Time.timeScale = 1f; break;
            }

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
                bool closedInventory = gameUIManager != null && gameUIManager.TryClosePlayerInventory();
                if (!closedInventory)
                    PauseGame();
            }
        }

        #endregion

        #region Level Management

        public void RestartCurrentScene()
        {
            LoadLevel(SceneManager.GetActiveScene().buildIndex);
        }

        public void RestartGame()
        {
            LoadLevel(0);
        }

        public void LoadLevel(string sceneName)
        {
            StartCoroutine(LoadLevelWithScreen(sceneName, -1));
        }

        public void LoadLevel(int sceneIndex)
        {
            StartCoroutine(LoadLevelWithScreen(null, sceneIndex));
        }

        /// <summary>
        /// Starts loading a level in the background with no loading screen (e.g. under a story video).
        /// The current level keeps running; <see cref="ActivatePreloadedLevel"/> switches to it.
        /// </summary>
        public void PreloadLevel(string sceneName, int sceneIndex = -1)
        {
            if (isLoadingScene || preloadOperation != null) return;
            // Low priority keeps the current scene (and its video) smooth while the level streams in.
            Application.backgroundLoadingPriority = ThreadPriority.Low;
            preloadOperation = string.IsNullOrEmpty(sceneName)
                ? SceneManager.LoadSceneAsync(sceneIndex)
                : SceneManager.LoadSceneAsync(sceneName);
            if (preloadOperation == null)
            {
                Debug.LogError("[GameManager] Failed to preload scene — is it added to Build Settings?");
                Application.backgroundLoadingPriority = ThreadPriority.Normal;
                return;
            }
            preloadOperation.allowSceneActivation = false;
        }

        public bool HasPreloadedLevel => preloadOperation != null;

        /// <summary>Switches to the level started by <see cref="PreloadLevel"/>, without the loading screen.</summary>
        public void ActivatePreloadedLevel()
        {
            if (preloadOperation == null || isLoadingScene) return;
            StartCoroutine(ActivatePreloadedLevelRoutine());
        }

        private IEnumerator ActivatePreloadedLevelRoutine()
        {
            isLoadingScene = true;
            PrepareForLevelChange();
            playerLifecycle?.PrepareForSceneChange();

            AsyncOperation operation = preloadOperation;
            Application.backgroundLoadingPriority = ThreadPriority.High;
            while (operation.progress < 0.9f)
                yield return null;

            preloadOperation = null;
            Application.backgroundLoadingPriority = ThreadPriority.Normal;
            operation.allowSceneActivation = true;
        }

        // Shared by every level change: the old level's input, HUD and combat state must not carry over.
        private void PrepareForLevelChange()
        {
            // Detach before the scene unloads so we are not left subscribed to a destroyed GameInputManager.
            UnsubscribePauseInput();

            // Lock all gameplay input for the duration of the transition.
            // Input is restored in InitializeForNewScene() once the new scene is ready.
            GameInputManager.Instance?.SetGameplayInputEnabled(false);

            if (currentState != GameState.Playing) SetGameState(GameState.Playing);
            // Persistent HUD/combat state would otherwise carry into the reloaded level.
            if (gameUIManager != null && gameUIManager.PlayerHUD != null && gameUIManager.PlayerHUD.IsInventoryOpen)
                gameUIManager.PlayerHUD.CloseInventory();
            PlayerCombatTracker.Instance?.ClearCombatState();
        }

        private IEnumerator LoadLevelWithScreen(string sceneName, int sceneIndex)
        {
            // A pending preload holds Unity's async queue, so it must be the level that loads.
            if (isLoadingScene || preloadOperation != null) yield break;
            isLoadingScene = true;

            PrepareForLevelChange();
            gameUIManager?.BeginSceneTransition();

            // Start loading immediately so disk/scene work overlaps the transition video.
            // Activation waits for both, rather than adding video time and load time together.
            var asyncOp = string.IsNullOrEmpty(sceneName)
                ? SceneManager.LoadSceneAsync(sceneIndex)
                : SceneManager.LoadSceneAsync(sceneName);

            if (asyncOp == null)
            {
                Debug.LogError($"[GameManager] Failed to load scene — is it added to Build Settings?");
                gameUIManager?.CancelSceneTransition();
                isLoadingScene = false;
                // Restore input on the abort path so the player isn't permanently locked.
                GameInputManager.Instance?.SetGameplayInputEnabled(true);
                RebindPauseInputSubscription();
                yield break;
            }

            // Preserve the current player on the abort path. Once Unity confirms
            // the request is valid, detach lifecycle state before activation.
            playerLifecycle?.PrepareForSceneChange();
            asyncOp.allowSceneActivation = false;

            // With activation held, Unity stops loading at 0.9; map that to 0-1.
            while (asyncOp.progress < 0.9f ||
                   (gameUIManager != null && !gameUIManager.IsLoadingPresentationFinished))
            {
                gameUIManager?.SetLoadingProgress(asyncOp.progress / 0.9f);
                yield return null;
            }

            gameUIManager?.SetLoadingProgress(1f);
            asyncOp.allowSceneActivation = true;
        }

        // Soft-respawn without reloading.
        public void RestartLevel()
        {
            SetGameState(GameState.Playing);
            playerLifecycle?.RestartAtPrimarySpawn();
        }

        public void QuitGame()
        {
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
            LevelMusicSettings levelMusic = GetLevelMusicOverride();
            if (levelMusic != null && levelMusic.playThroughCombat) return;

            var entry = GetCombatMusicEntry();
            if (entry != null && entry.IsValid)
                AudioManager.Instance?.CrossfadeToMusic(entry);
        }

        private void OnCombatEnded() => PlayLevelMusic();

        private void PlayLevelMusic()
        {
            LevelMusicSettings levelMusic = GetLevelMusicOverride();
            if (levelMusic != null)
            {
                AudioManager audio = AudioManager.Instance;
                if (audio == null) return;
                // From silence (e.g. after a story video) the track starts at full volume so its opening is heard.
                if (audio.IsMusicPlaying) audio.CrossfadeToMusic(levelMusic.music, -1f, levelMusic.intro);
                else audio.PlayMusic(levelMusic.music, levelMusic.intro);
                return;
            }

            // First enemy detection switches music from a physics trigger. Prepare
            // the combat track alongside level music, before that gameplay callback
            // can synchronously decompress an entire song on the movement frame.
            AudioClip combatClip = GetCombatMusicEntry()?.clip;
            if (combatClip != null && combatClip.loadState == AudioDataLoadState.Unloaded)
                combatClip.LoadAudioData();

            var entry = GetLevelMusicEntry();
            if (entry != null && entry.IsValid)
                AudioManager.Instance?.CrossfadeToMusic(entry);
        }

        /// <summary>The active level's own music (LevelContext, else legacy SceneSettings), or null.</summary>
        private LevelMusicSettings GetLevelMusicOverride()
        {
            LevelMusicSettings music = currentLevelContext != null ? currentLevelContext.LevelMusic
                : currentSceneSettings != null ? currentSceneSettings.LevelMusic : null;
            return music != null && music.IsSet ? music : null;
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
            GUILayout.Label($"Player:      {(playerLifecycle?.Player != null ? "Alive" : "None")}");
            GUILayout.Label($"Spawn Index: {playerLifecycle?.CurrentSpawnIndex ?? 0}");
            GUILayout.Label($"HUD:         {(gameUIManager?.IsPlayerHUDReady == true ? "Ready" : "Missing")}");
            GUILayout.Label($"PauseMenu:   {(gameUIManager?.IsPauseMenuReady == true ? "Ready" : "Missing")}");
            GUILayout.Label($"LoadScreen:  {(gameUIManager?.IsLoadingScreenReady == true ? "Ready" : "Missing")}");
            GUILayout.Space(6);
            GUILayout.Label("ESC / Start - Pause / Resume");
            GUILayout.EndArea();
        }

        #endregion
    }
}
