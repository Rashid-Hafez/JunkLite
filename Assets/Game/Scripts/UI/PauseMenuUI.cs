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

        private MenuButton[] buttons;
        private int focusedIndex = 0;
        private bool isVisible = false;

        #endregion

        #region Lifecycle

        private void Awake()
        {
            buttons = new[] { restartLevelButton, restartGameButton, quitButton };

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

            if (isVisible)
                UnsubscribeInput();
        }

        #endregion

        #region State

        private void OnGameStateChanged(GameManager.GameState state)
        {
            if (state == GameManager.GameState.Paused ||
                (showOnGameOver && state == GameManager.GameState.GameOver))
                Show();
            else
                Hide();
        }

        private void Show()
        {
            if (isVisible) return;
            isVisible = true;

            panel?.SetActive(true);

            focusedIndex = 0;
            UpdateFocus();
            SubscribeInput();

            GameInputManager.Instance?.SwitchToUIActionMap();
        }

        private void Hide()
        {
            if (!isVisible && (panel == null || !panel.activeSelf))
            {
                panel?.SetActive(false);
                return;
            }

            isVisible = false;
            panel?.SetActive(false);

            ClearFocus();
            UnsubscribeInput();

            GameInputManager.Instance?.SwitchToPlayerActionMap();
        }

        #endregion

        #region Controller Input

        private void SubscribeInput()
        {
            var input = GameInputManager.Instance;
            if (input == null) return;
            input.OnUINavigate += HandleNavigate;
            input.OnUISubmit += HandleSubmit;
            input.OnUICancel += HandleCancel;
        }

        private void UnsubscribeInput()
        {
            var input = GameInputManager.Instance;
            if (input == null) return;
            input.OnUINavigate -= HandleNavigate;
            input.OnUISubmit -= HandleSubmit;
            input.OnUICancel -= HandleCancel;
        }

        private void HandleNavigate(Vector2 dir)
        {
            if (dir.y > 0.3f)       // up
                focusedIndex = (focusedIndex - 1 + buttons.Length) % buttons.Length;
            else if (dir.y < -0.3f) // down
                focusedIndex = (focusedIndex + 1) % buttons.Length;
            else return;

            UpdateFocus();
        }

        private void HandleSubmit()
        {
            GetFocusedButton()?.Click();
        }

        private void HandleCancel()
        {
            GameManager.Instance?.ResumeGame();
        }

        #endregion

        #region Focus

        private void UpdateFocus()
        {
            for (int i = 0; i < buttons.Length; i++)
            {
                if (buttons[i] == null) continue;
                buttons[i].SetSelected(i == focusedIndex);
            }
        }

        private void ClearFocus()
        {
            foreach (var btn in buttons)
                btn?.SetSelected(false);
        }

        private MenuButton GetFocusedButton()
        {
            if (focusedIndex < 0 || focusedIndex >= buttons.Length) return null;
            return buttons[focusedIndex];
        }

        #endregion
    }
}