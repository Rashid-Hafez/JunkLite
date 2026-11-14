using Junklite.TitleScreen;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace junklite
{
    /// <summary>
    /// Title screen logic. Drives the Junklite UI Toolkit title (Assets/Junklite/TitleScreen):
    /// BEGIN PROCEDURE starts a new run (Simulation → Service Floor → End Card), RESUME SUBJECT
    /// skips the tutorial and loads Service Floor with the current profile, TERMINATE SESSION quits. The legacy uGUI buttons below
    /// are optional and still work if assigned (level select for testing).
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class MainMenuController : MonoBehaviour
    {
        [Header("Title Screen (UI Toolkit)")]
        [Tooltip("The TitleScreen prefab instance in this scene. Disable its Scene Link so it doesn't load the demo Inventory scene.")]
        [SerializeField] private TitleScreenController titleScreen;

        [Header("Legacy Buttons (optional)")]
        [SerializeField] private Button playButton;
        [SerializeField] private Button rooftopButton;
        [SerializeField] private Button serviceFloorButton;
        [SerializeField] private Button sandboxButton;
        [SerializeField] private Button quitButton;

        [Header("Scenes (names as listed in Build Settings)")]
        [SerializeField] private string playScene = "SimulationCinematicDemo";
        [SerializeField] private string rooftopScene = "Rooftop Scene v3";
        [SerializeField] private string serviceFloorScene = "Service Floor";
        [SerializeField] private string sandboxScene = "SANDBOX";

        [Header("Transition")]
        [Tooltip("Persistent Game Root spawned on the first click when the game starts on this menu, " +
                 "so every scene change goes through GameManager's async loading screen.")]
        [SerializeField] private GameObject gameRootPrefab;

        private bool loading;

        private void OnEnable()
        {
            loading = false;
            Time.timeScale = 1f;
            Cursor.visible = true;
            Cursor.lockState = CursorLockMode.None;

            Bind(playButton, OnPlay);
            Bind(rooftopButton, OnRooftop);
            Bind(serviceFloorButton, OnServiceFloor);
            Bind(sandboxButton, OnSandbox);
            Bind(quitButton, OnQuit);

            if (titleScreen != null) titleScreen.CommandExecuted += HandleTitleCommand;
        }

        private void OnDisable()
        {
            if (titleScreen != null) titleScreen.CommandExecuted -= HandleTitleCommand;

            Unbind(playButton, OnPlay);
            Unbind(rooftopButton, OnRooftop);
            Unbind(serviceFloorButton, OnServiceFloor);
            Unbind(sandboxButton, OnSandbox);
            Unbind(quitButton, OnQuit);
        }

        private void Start()
        {
            // Legacy menu: Play is the starting focus; UINavigationGroup on Menu Column owns
            // controller/keyboard navigation and selection recovery. The UI Toolkit title handles its own focus.
            if (titleScreen == null && playButton != null && EventSystem.current != null)
                EventSystem.current.SetSelectedGameObject(playButton.gameObject);
        }

        private void HandleTitleCommand(TitleCommand command)
        {
            switch (command)
            {
                case TitleCommand.Begin: OnPlay(); break;
                case TitleCommand.Resume: OnContinue(); break;
                case TitleCommand.Rooftop: OnRooftop(); break;
                case TitleCommand.Terminate: OnQuit(); break;
                // Calibrate (settings) has no screen yet; the title plays its press feedback only.
            }
        }

        public void OnPlay()
        {
            // Play is a new run: no upgrades, weapons or XP carried over from the last one.
            if (!loading) PlayerProgression.Instance?.ResetProfile();
            Load(playScene);
        }

        /// <summary>Continues with the saved profile (upgrades, weapons, XP), skipping the tutorial: starts on Service Floor.</summary>
        public void OnContinue() => Load(serviceFloorScene);

        public void OnRooftop() => Load(rooftopScene);
        public void OnServiceFloor() => Load(serviceFloorScene);
        public void OnSandbox() => Load(sandboxScene);

        public void OnQuit()
        {
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }

        private void Load(string sceneName)
        {
            if (loading || string.IsNullOrEmpty(sceneName)) return;
            loading = true;
            MenuMusic.FadeOutCurrent();

            // Coming back from a run the persistent root already exists. On a cold start
            // the menu creates it, and the level's own root removes itself as a duplicate.
            if (GameManager.Instance == null && gameRootPrefab != null)
                Instantiate(gameRootPrefab).name = gameRootPrefab.name;

            // GameManager owns async loading, the loading screen and the reveal after spawn.
            if (GameManager.Instance != null)
            {
                GameManager.Instance.LoadLevel(sceneName);
                return;
            }

            Debug.LogWarning("[MainMenu] No GameManager or Game Root prefab; loading without a loading screen.", this);
            SceneManager.LoadScene(sceneName);
        }

        private static void Bind(Button button, UnityEngine.Events.UnityAction action)
        {
            if (button != null) button.onClick.AddListener(action);
        }

        private static void Unbind(Button button, UnityEngine.Events.UnityAction action)
        {
            if (button != null) button.onClick.RemoveListener(action);
        }
    }
}
