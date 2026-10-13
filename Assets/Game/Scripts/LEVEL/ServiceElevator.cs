using UnityEngine;

namespace junklite
{
    /// <summary>
    /// Level exit: while the player stands in this trigger the interact prompt shows, and Interact
    /// completes the ObjectiveLink on this object (e.g. GET TO THE ELEVATOR), then ends the level:
    /// through LevelGoal (results screen, whose Continue loads the next scene) when the level is
    /// tracked, otherwise by loading the next scene directly through GameManager.
    /// </summary>
    [RequireComponent(typeof(Collider))]
    public class ServiceElevator : MonoBehaviour
    {
        [SerializeField] private string promptLabel = "Take Elevator";
        [Tooltip("Complete the level through LevelGoal so the results screen shows. Off: load the next scene directly.")]
        [SerializeField] private bool completeLevelGoal = true;
        [Tooltip("Direct load only. Scene to load. When empty, nextSceneBuildIndex is used.")]
        [SerializeField] private string nextSceneName;
        [Tooltip("Used when nextSceneName is empty. -1 loads the next build index after this scene.")]
        [SerializeField] private int nextSceneBuildIndex = -1;

        private GameInputManager subscribedInput;
        private ObjectiveLink objectiveLink;
        private Collider playerInside;
        private bool used;

        private void Awake()
        {
            GetComponent<Collider>().isTrigger = true;
            objectiveLink = GetComponent<ObjectiveLink>();
        }

        private void OnEnable() => Rebind();
        private void Start() => Rebind();

        private void OnDisable()
        {
            Unbind();
            playerInside = null;
            TutorialPromptHUD.HideInteract(this, instant: true);
        }

        private void OnTriggerEnter(Collider other)
        {
            if (used || !other.CompareTag("Player")) return;
            playerInside = other;
            Rebind();
            TutorialPromptHUD.ShowInteract(this, promptLabel);
        }

        private void OnTriggerExit(Collider other)
        {
            if (other != playerInside) return;
            playerInside = null;
            TutorialPromptHUD.HideInteract(this);
        }

        private void HandleInteract()
        {
            if (used || playerInside == null) return;
            used = true;
            playerInside = null;
            TutorialPromptHUD.HideInteract(this, completed: true);
            if (objectiveLink != null) objectiveLink.TryComplete();

            LevelStatsTracker stats = LevelStatsTracker.Instance;
            if (completeLevelGoal && LevelGoal.Instance != null && stats != null && stats.State == LevelState.Running)
            {
                LevelGoal.Instance.Trigger();
                return;
            }

            GameManager manager = GameManager.Instance;
            if (manager == null)
            {
                Debug.LogWarning($"[ServiceElevator] {name}: no GameManager, can't load the next scene.", this);
                return;
            }

            if (!string.IsNullOrEmpty(nextSceneName)) manager.LoadLevel(nextSceneName);
            else manager.LoadLevel(nextSceneBuildIndex >= 0
                ? nextSceneBuildIndex
                : gameObject.scene.buildIndex + 1);
        }

        private void Rebind()
        {
            GameInputManager input = GameInputManager.Instance;
            if (input == subscribedInput) return;
            if (subscribedInput != null) subscribedInput.OnInteract -= HandleInteract;
            subscribedInput = input;
            if (subscribedInput != null) subscribedInput.OnInteract += HandleInteract;
        }

        private void Unbind()
        {
            if (subscribedInput == null) return;
            subscribedInput.OnInteract -= HandleInteract;
            subscribedInput = null;
        }
    }
}
