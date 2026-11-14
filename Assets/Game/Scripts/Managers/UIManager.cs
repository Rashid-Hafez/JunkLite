using UnityEngine;

namespace junklite
{
    /// <summary>
    /// Main UI controller that manages all UI systems
    /// </summary>
    [DefaultExecutionOrder(0)]
    public class UIManager : MonoBehaviour
    {
        public static UIManager Instance { get; private set; }

        [Header("UI Panels")]
        [SerializeField] private GameObject hudPanel;
        [SerializeField] private GameObject pausePanel;
        [SerializeField] private GameObject gameOverPanel;

        
        

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }

            Instance = this;
            DontDestroyOnLoad(gameObject);

            // Get HUD manager if not assigned
          
        }

        private void Start()
        {
            // Global game state stays on GameManager.
            if (GameManager.Instance != null)
            {
                GameManager.Instance.OnGameStateChanged += OnGameStateChanged;
            }

            // Initialize UI
            ShowHUD();
        }

        private void OnGameStateChanged(GameManager.GameState newState)
        {
            switch (newState)
            {
                case GameManager.GameState.Playing:
                    ShowHUD();
                    HidePause();
                    HideGameOver();
                    break;

                case GameManager.GameState.Paused:
                    ShowPause();
                    break;

                case GameManager.GameState.GameOver:
                    HideHUD();
                    ShowGameOver();
                    break;
            }
        }

        public void ShowHUD()
        {
            if (hudPanel != null) hudPanel.SetActive(true);
        }

        public void HideHUD()
        {
            if (hudPanel != null) hudPanel.SetActive(false);
        }

        public void ShowPause()
        {
            if (pausePanel != null) pausePanel.SetActive(true);
        }

        public void HidePause()
        {
            if (pausePanel != null) pausePanel.SetActive(false);
        }

        public void ShowGameOver()
        {
            if (gameOverPanel != null) gameOverPanel.SetActive(true);
        }

        public void HideGameOver()
        {
            if (gameOverPanel != null) gameOverPanel.SetActive(false);
        }

        private void OnDestroy()
        {
            if (GameManager.Instance != null)
                GameManager.Instance.OnGameStateChanged -= OnGameStateChanged;

        }
    }
}
