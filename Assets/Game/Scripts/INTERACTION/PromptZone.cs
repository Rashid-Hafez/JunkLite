using UnityEngine;

namespace junklite
{
    /// <summary>Marks a string field as an input action path ("Player/Jump") picked from a dropdown of the project's mapped actions.</summary>
    public sealed class InputActionPathAttribute : PropertyAttribute { }

    /// <summary>
    /// Drop-in HUD prompt. While the player stands inside this trigger box the optic prompt shows
    /// "[KEY] PROMPT". Pick the System Button from the dropdown and type the Prompt text. The key chip
    /// follows the active device (keyboard or gamepad) through <see cref="TutorialPromptHUD"/>.
    /// Display only: it never consumes the input itself.
    /// </summary>
    [RequireComponent(typeof(BoxCollider))]
    public class PromptZone : MonoBehaviour
    {
        [Tooltip("The input shown in the key chip. Pick from the mapped actions; the key changes with the active device.")]
        [SerializeField, InputActionPath] private string systemButton = "Player/Interact";
        [Tooltip("Text shown next to the key chip.")]
        [SerializeField] private string prompt = "Interact";

        private static readonly Color GizmoColor = new Color(1f, 0.5f, 0f, 1f);

        private static TutorialPromptHUD sharedHud;
        // One prompt on screen; the last zone entered owns it.
        private static PromptZone owner;

        private int playerContacts;
        private PlayerLifecycle subscribedLifecycle;

        private static TutorialPromptHUD Hud
        {
            get
            {
                if (sharedHud == null) sharedHud = new GameObject("PromptZoneHUD").AddComponent<TutorialPromptHUD>();
                return sharedHud;
            }
        }

        private void Reset()
        {
            var box = GetComponent<BoxCollider>();
            box.isTrigger = true;
            box.size = new Vector3(4f, 3f, 4f);
            box.center = new Vector3(0f, 1.5f, 0f);
        }

        private void Awake() => GetComponent<BoxCollider>().isTrigger = true;

        private void OnEnable() => Rebind();

        private void Start() => Rebind();

        private void OnDisable()
        {
            Unbind();
            playerContacts = 0;
            HidePrompt(instant: true);
        }

        private void OnTriggerEnter(Collider other)
        {
            if (!other.CompareTag("Player")) return;
            if (playerContacts++ == 0) ShowPrompt();
        }

        private void OnTriggerExit(Collider other)
        {
            if (!other.CompareTag("Player") || playerContacts == 0) return;
            if (--playerContacts == 0) HidePrompt(instant: false);
        }

        // Respawn re-sends OnTriggerEnter for a player that spawns inside the box, so just reset.
        private void HandlePlayerDied(PlayerCharacter _)
        {
            playerContacts = 0;
            HidePrompt(instant: true);
        }

        private void ShowPrompt()
        {
            owner = this;
            Hud.Show(new TutorialPromptHUD.Entry(systemButton, prompt));
        }

        private void HidePrompt(bool instant)
        {
            if (owner != this) return;
            owner = null;
            if (sharedHud != null) sharedHud.Hide(instant);
        }

        private void Rebind()
        {
            PlayerLifecycle lifecycle = PlayerLifecycle.Instance;
            if (lifecycle == subscribedLifecycle) return;
            if (subscribedLifecycle != null) subscribedLifecycle.PlayerDied -= HandlePlayerDied;
            subscribedLifecycle = lifecycle;
            if (subscribedLifecycle != null) subscribedLifecycle.PlayerDied += HandlePlayerDied;
        }

        private void Unbind()
        {
            if (subscribedLifecycle == null) return;
            subscribedLifecycle.PlayerDied -= HandlePlayerDied;
            subscribedLifecycle = null;
        }

        private void OnDrawGizmos()
        {
            var box = GetComponent<BoxCollider>();
            if (box == null) return;
            Gizmos.matrix = transform.localToWorldMatrix;
            Color fill = GizmoColor;
            fill.a = 0.25f;
            Gizmos.color = fill;
            Gizmos.DrawCube(box.center, box.size);
            Gizmos.color = GizmoColor;
            Gizmos.DrawWireCube(box.center, box.size);
        }
    }
}
