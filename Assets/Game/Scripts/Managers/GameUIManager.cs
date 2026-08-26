using UnityEngine;
using UnityEngine.UI;

namespace junklite
{
    /// <summary>
    /// Persistent owner of runtime game UI creation and visibility. GameManager
    /// publishes global state and coordinates scene loading; this component owns
    /// the HUD, pause menu, game-over presentation, and loading presentation.
    /// </summary>
    [DefaultExecutionOrder(0)]
    [DisallowMultipleComponent]
    public sealed class GameUIManager : MonoBehaviour
    {
        public static GameUIManager Instance { get; private set; }

        [Header("UI Prefabs")]
        [SerializeField] private GameObject playerUIPrefab;
        [SerializeField] private GameObject pauseMenuUIPrefab;
        [SerializeField] private GameObject gameOverUIPrefab;
        [SerializeField] private GameObject loadingScreenUIPrefab;

        private Transform gameplayCanvasTransform;
        private PlayerUI playerUIInstance;
        private PauseMenuUI pauseMenuUIInstance;
        private GameObject gameOverUIInstance;
        private Button gameOverRestartButton;
        private LoadingScreenUI loadingScreenUIInstance;
        private GameManager subscribedGameManager;
        private PlayerLifecycle subscribedPlayerLifecycle;
        private bool playerHudAllowed;

        public GameObject PlayerUIPrefab => playerUIPrefab;
        public GameObject PauseMenuUIPrefab => pauseMenuUIPrefab;
        public GameObject GameOverUIPrefab => gameOverUIPrefab;
        public GameObject LoadingScreenUIPrefab => loadingScreenUIPrefab;
        public bool IsPlayerHUDReady => playerUIInstance != null;
        public bool IsPauseMenuReady => pauseMenuUIInstance != null;
        public bool IsGameOverUIReady => gameOverUIInstance != null;
        public bool IsLoadingScreenReady => loadingScreenUIInstance != null;
        public bool IsLoadingPresentationFinished =>
            loadingScreenUIInstance == null || loadingScreenUIInstance.IsVideoFinished;

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                enabled = false;
                Destroy(this);
                return;
            }

            Instance = this;
            DontDestroyOnLoad(gameObject);
        }

        private void OnEnable()
        {
            RebindGameManager();
            RebindPlayerLifecycle();
        }

        private void Start()
        {
            // GameManager may have a later execution order on the same root.
            RebindGameManager();
            RebindPlayerLifecycle();
        }

        private void OnDisable()
        {
            UnsubscribeFromGameManager();
            UnsubscribeFromPlayerLifecycle();
        }

        private void OnDestroy()
        {
            UnsubscribeFromGameManager();
            UnsubscribeFromPlayerLifecycle();

            if (gameOverRestartButton != null)
                gameOverRestartButton.onClick.RemoveListener(HandleRestartRequested);

            if (loadingScreenUIInstance != null)
                Destroy(loadingScreenUIInstance.gameObject);

            if (Instance == this)
                Instance = null;
        }

        /// <summary>
        /// Compatibility bridge for old manager prefabs. A configured
        /// GameUIManager always takes priority over these fallback values.
        /// </summary>
        public void ApplyDefaultsIfMissing(
            GameObject fallbackPlayerUI,
            GameObject fallbackPauseMenu,
            GameObject fallbackGameOver,
            GameObject fallbackLoadingScreen)
        {
            if (playerUIPrefab == null)
                playerUIPrefab = fallbackPlayerUI;
            if (pauseMenuUIPrefab == null)
                pauseMenuUIPrefab = fallbackPauseMenu;
            if (gameOverUIPrefab == null)
                gameOverUIPrefab = fallbackGameOver;
            if (loadingScreenUIPrefab == null)
                loadingScreenUIPrefab = fallbackLoadingScreen;
        }

        /// <summary>Creates the UI required by the active level.</summary>
        public void InitializeForScene(bool allowPlayerHud)
        {
            RebindGameManager();
            RebindPlayerLifecycle();
            ResolveGameplayCanvas();

            playerHudAllowed = allowPlayerHud;
            EnsureLoadingScreenUI();
            EnsurePauseMenuUI();
            EnsureGameOverUI();

            loadingScreenUIInstance?.Hide();
            SetGameOverVisible(false);

            if (!playerHudAllowed)
            {
                SetPlayerHUDActive(false);
                return;
            }

            EnsurePlayerUI();
            PlayerCharacter player = subscribedPlayerLifecycle?.Player;
            if (player != null && player.IsAlive)
                HandlePlayerSpawned(player);
            else
                SetPlayerHUDActive(false);
        }

        public void BeginSceneTransition()
        {
            ResolveGameplayCanvas();
            EnsureLoadingScreenUI();
            SetPlayerHUDActive(false);
            SetGameOverVisible(false);
            loadingScreenUIInstance?.Show();
        }

        public void CancelSceneTransition()
        {
            loadingScreenUIInstance?.Hide();

            if (subscribedGameManager != null)
                HandleGameStateChanged(subscribedGameManager.CurrentState);
        }

        public bool TryClosePlayerInventory()
        {
            if (playerUIInstance == null || !playerUIInstance.IsInventoryOpen)
                return false;

            playerUIInstance.CloseInventory();
            return true;
        }

        private void RebindGameManager()
        {
            GameManager manager = GameManager.Instance;
            if (subscribedGameManager == manager)
                return;

            UnsubscribeFromGameManager();
            subscribedGameManager = manager;

            if (subscribedGameManager != null)
                subscribedGameManager.OnGameStateChanged += HandleGameStateChanged;
        }

        private void UnsubscribeFromGameManager()
        {
            if (subscribedGameManager == null)
                return;

            subscribedGameManager.OnGameStateChanged -= HandleGameStateChanged;
            subscribedGameManager = null;
        }

        private void RebindPlayerLifecycle()
        {
            PlayerLifecycle lifecycle = PlayerLifecycle.Instance;
            if (subscribedPlayerLifecycle == lifecycle)
                return;

            UnsubscribeFromPlayerLifecycle();
            subscribedPlayerLifecycle = lifecycle;

            if (subscribedPlayerLifecycle != null)
                subscribedPlayerLifecycle.PlayerSpawned += HandlePlayerSpawned;
        }

        private void UnsubscribeFromPlayerLifecycle()
        {
            if (subscribedPlayerLifecycle == null)
                return;

            subscribedPlayerLifecycle.PlayerSpawned -= HandlePlayerSpawned;
            subscribedPlayerLifecycle = null;
        }

        private void HandlePlayerSpawned(PlayerCharacter player)
        {
            if (!playerHudAllowed || player == null)
                return;

            EnsurePlayerUI();
            SetPlayerHUDActive(true);
        }

        private void HandleGameStateChanged(GameManager.GameState state)
        {
            switch (state)
            {
                case GameManager.GameState.Playing:
                    SetGameOverVisible(false);
                    PlayerCharacter player = subscribedPlayerLifecycle?.Player;
                    SetPlayerHUDActive(playerHudAllowed && player != null && player.IsAlive);
                    break;

                case GameManager.GameState.Paused:
                    break;

                case GameManager.GameState.GameOver:
                    SetPlayerHUDActive(false);
                    SetGameOverVisible(true);
                    break;
            }
        }

        private void HandleRestartRequested()
        {
            GameManager.Instance?.RestartLevel();
        }

        private void ResolveGameplayCanvas()
        {
            if (GameRoot.Instance != null && GameRoot.Instance.GameplayUIRoot != null)
            {
                gameplayCanvasTransform = GameRoot.Instance.GameplayUIRoot;
                return;
            }

            if (gameplayCanvasTransform != null)
                return;

            GameObject taggedCanvas = null;
            try
            {
                taggedCanvas = GameObject.FindWithTag("GameplayCanvas");
            }
            catch (UnityException)
            {
                // The reusable root does not require the legacy tag.
            }

            if (taggedCanvas != null)
            {
                gameplayCanvasTransform = taggedCanvas.transform;
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

            var canvas = canvasObject.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 10;

            var scaler = canvasObject.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 0.5f;

            gameplayCanvasTransform = canvasObject.transform;
        }

        private void EnsurePlayerUI()
        {
            if (playerUIInstance != null)
                return;

            if (gameplayCanvasTransform == null)
            {
                Debug.LogError("[GameUIManager] Gameplay canvas is unavailable.");
                return;
            }

            if (playerUIPrefab == null)
            {
                Debug.LogError("[GameUIManager] Player UI prefab is not assigned.");
                return;
            }

            GameObject instance = Instantiate(playerUIPrefab, gameplayCanvasTransform);
            instance.name = "Player UI";
            playerUIInstance = instance.GetComponent<PlayerUI>();

            if (playerUIInstance == null)
            {
                Debug.LogError("[GameUIManager] Player UI prefab is missing PlayerUI.");
                Destroy(instance);
            }
        }

        private void EnsurePauseMenuUI()
        {
            if (pauseMenuUIInstance != null)
                return;

            if (pauseMenuUIPrefab == null)
            {
                Debug.LogWarning("[GameUIManager] Pause menu prefab is not assigned.");
                return;
            }

            GameObject instance = Instantiate(pauseMenuUIPrefab, gameplayCanvasTransform);
            instance.name = "Pause Menu UI";
            pauseMenuUIInstance = instance.GetComponent<PauseMenuUI>();

            if (pauseMenuUIInstance == null)
            {
                Debug.LogError("[GameUIManager] Pause menu prefab is missing PauseMenuUI.");
                Destroy(instance);
            }
        }

        private void EnsureGameOverUI()
        {
            if (gameOverUIInstance != null)
                return;

            if (gameOverUIPrefab == null)
            {
                Debug.LogWarning("[GameUIManager] Game-over prefab is not assigned.");
                return;
            }

            gameOverUIInstance = Instantiate(gameOverUIPrefab, gameplayCanvasTransform);
            gameOverUIInstance.name = "Game Over UI";
            gameOverRestartButton = gameOverUIInstance.GetComponentInChildren<Button>(true);

            if (gameOverRestartButton != null)
                gameOverRestartButton.onClick.AddListener(HandleRestartRequested);
            else
                Debug.LogWarning("[GameUIManager] Game-over prefab has no restart Button.");

            SetGameOverVisible(false);
        }

        private void EnsureLoadingScreenUI()
        {
            if (loadingScreenUIInstance != null)
                return;

            if (loadingScreenUIPrefab == null)
            {
                Debug.LogWarning("[GameUIManager] Loading-screen prefab is not assigned.");
                return;
            }

            GameObject instance = Instantiate(loadingScreenUIPrefab);
            instance.name = "Loading Screen UI";
            DontDestroyOnLoad(instance);
            loadingScreenUIInstance = instance.GetComponent<LoadingScreenUI>();

            if (loadingScreenUIInstance == null)
            {
                Debug.LogError("[GameUIManager] Loading-screen prefab is missing LoadingScreenUI.");
                Destroy(instance);
            }
        }

        private void SetPlayerHUDActive(bool active)
        {
            if (playerUIInstance != null && playerUIInstance.gameObject.activeSelf != active)
                playerUIInstance.gameObject.SetActive(active);
        }

        private void SetGameOverVisible(bool visible)
        {
            if (gameOverUIInstance != null && gameOverUIInstance.activeSelf != visible)
                gameOverUIInstance.SetActive(visible);
        }
    }
}
