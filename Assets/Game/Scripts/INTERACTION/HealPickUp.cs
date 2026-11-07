using junklite;
using UnityEngine;

/// <summary>
/// Health pickup. By default it heals on touch. With requiresInteract ticked, touching it shows the
/// interact prompt and Interact heals, completes an ObjectiveLink on this object, then removes it.
/// Needs a trigger collider on this object.
/// </summary>
public class HealPickUp : MonoBehaviour
{
    [SerializeField] private float healAmount = 20f;
    [Tooltip("Off: heal on touch. On: show a prompt and heal on Interact.")]
    [SerializeField] private bool requiresInteract;
    [SerializeField] private string promptLabel = "Take Health";

    private GameInputManager subscribedInput;
    private ObjectiveLink objectiveLink;
    private PlayerCharacter playerInside;

    private void Awake() => objectiveLink = GetComponent<ObjectiveLink>();

    private void OnEnable()
    {
        if (requiresInteract) Rebind();
    }

    private void Start()
    {
        if (requiresInteract) Rebind();
    }

    private void OnDisable()
    {
        Unbind();
        playerInside = null;
        TutorialPromptHUD.HideInteract(this, instant: true);
    }

    void OnTriggerEnter(Collider other)
    {
        if (!other.CompareTag("Player")) return;
        PlayerCharacter player = FindPlayer(other);
        if (player == null) return;

        if (!requiresInteract)
        {
            player.Heal(healAmount);
            Destroy(gameObject);
            return;
        }

        if (objectiveLink != null && objectiveLink.IsComplete) return;
        playerInside = player;
        Rebind();
        TutorialPromptHUD.ShowInteract(this, promptLabel);
    }

    void OnTriggerExit(Collider other)
    {
        if (playerInside == null || FindPlayer(other) != playerInside) return;
        playerInside = null;
        TutorialPromptHUD.HideInteract(this);
    }

    private void HandleInteract()
    {
        if (playerInside == null || (objectiveLink != null && objectiveLink.IsComplete)) return;

        playerInside.Heal(healAmount);
        if (objectiveLink != null) objectiveLink.TryComplete();
        TutorialPromptHUD.HideInteract(this, completed: true);
        playerInside = null;
        Destroy(gameObject);
    }

    private static PlayerCharacter FindPlayer(Collider other)
    {
        PlayerCharacter player = other.GetComponent<PlayerCharacter>();
        return player != null ? player : other.GetComponentInParent<PlayerCharacter>();
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
