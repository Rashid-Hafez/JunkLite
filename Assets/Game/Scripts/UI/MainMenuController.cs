using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace junklite
{
    /// <summary>
    /// Title screen. Binds the Menu scene's authored buttons to scene loads; layout and look
    /// live on the Canvas so they stay editable in the Inspector. Play starts the full run
    /// (Simulation → Service Floor → End Card); the level buttons jump straight to a level.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class MainMenuController : MonoBehaviour
    {
        [Header("Buttons")]
        [SerializeField] private Button playButton;
        [SerializeField] private Button rooftopButton;
        [SerializeField] private Button serviceFloorButton;
        [SerializeField] private Button sandboxButton;
        [SerializeField] private Button quitButton;

        [Header("Scenes (names as listed in Build Settings)")]
        [SerializeField] private string playScene = "SimulationCinematicDemo";
        [SerializeField] private string rooftopScene = "Rooftop Scene v2";
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
        }

        private void OnDisable()
        {
            Unbind(playButton, OnPlay);
            Unbind(rooftopButton, OnRooftop);
            Unbind(serviceFloorButton, OnServiceFloor);
            Unbind(sandboxButton, OnSandbox);
            Unbind(quitButton, OnQuit);
        }

        private void Start()
        {
            // Play is the starting focus; UINavigationGroup on Menu Column owns
            // controller/keyboard navigation and selection recovery.
            if (playButton != null && EventSystem.current != null)
                EventSystem.current.SetSelectedGameObject(playButton.gameObject);
        }

        private void OnPlay() => Load(playScene);
        private void OnRooftop() => Load(rooftopScene);
        private void OnServiceFloor() => Load(serviceFloorScene);
        private void OnSandbox() => Load(sandboxScene);

        private void OnQuit()
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
