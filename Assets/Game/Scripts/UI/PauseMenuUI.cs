using UnityEngine;

namespace junklite
{
    public class PauseMenuUI : MonoBehaviour
    {
        #region Fields

        [SerializeField] private GameObject panel;
        [SerializeField] private MenuButton restartLevelButton;
        [SerializeField] private MenuButton restartGameButton;
        [SerializeField] private MenuButton quitButton;
        [SerializeField] private bool showOnGameOver = true;

        #endregion

        #region Lifecycle

        private void Awake()
        {
            if (restartLevelButton != null) restartLevelButton.OnClick += () => GameManager.Instance?.RestartCurrentScene();
            if (restartGameButton != null) restartGameButton.OnClick += () => GameManager.Instance?.RestartGame();
            if (quitButton != null) quitButton.OnClick += () => GameManager.Instance?.QuitGame();

            Hide();
        }

        private void OnEnable()
        {
            if (GameManager.Instance != null)
                GameManager.Instance.OnGameStateChanged += OnGameStateChanged;
        }

        private void OnDisable()
        {
            if (GameManager.Instance != null)
                GameManager.Instance.OnGameStateChanged -= OnGameStateChanged;
        }

        #endregion

        #region State

        private void OnGameStateChanged(GameManager.GameState state)
        {
            if (state == GameManager.GameState.Paused || (showOnGameOver && state == GameManager.GameState.GameOver))
                Show();
            else
                Hide();
        }

        private void Show() => panel?.SetActive(true);
        private void Hide() => panel?.SetActive(false);

        #endregion
    }
}