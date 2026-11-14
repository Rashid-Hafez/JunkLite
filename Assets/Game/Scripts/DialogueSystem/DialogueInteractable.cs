/*using junklite;
using System;
using UnityEngine;

public class DialogueInteractable : MonoBehaviour
{
    public DialogueSequence sequence;
    private void OnTriggerEnter(Collider other)
    {
        if (other.GetComponent<Character2D5Controller>())
        {
            if (!DialogueManager.instance.dialogueText.IsActive())
            {
                DialogueManager.instance.StartDialogue(sequence);
            }

        }
        
    }
}
*/

using UnityEngine;

namespace junklite
{
    /// <summary>
    /// Talk-to trigger: entering its radius shows the shared TutorialPromptHUD interact prompt;
    /// Interact starts the dialogue.
    /// </summary>
    public class DialogueInteractable : MonoBehaviour
    {
        [SerializeField] private DialogueSequence dialogue;
        [SerializeField] private float interactRadius = 2.5f;
        [SerializeField] private string playerTag = "Player";

        private GameInputManager subscribedInputManager;

        public static DialogueInteractable Current { get; private set; }

        private void Awake()
        {
            var zone = new GameObject("ProximityZone");
            zone.transform.SetParent(transform, false);
            zone.layer = gameObject.layer;
            var col = zone.AddComponent<SphereCollider>();
            col.isTrigger = true;
            col.radius = interactRadius;
            zone.AddComponent<DialogueProximityRelay>().Init(this);
        }

        private void OnEnable() => RebindInputManager();
        private void Start() => RebindInputManager();

        private void OnDisable()
        {
            if (subscribedInputManager != null) subscribedInputManager.OnInteract -= HandleInteract;
            subscribedInputManager = null;
            if (Current == this) Deactivate(instant: true);
        }

        internal void OnPlayerEnter(Collider other)
        {
            if (!other.CompareTag(playerTag)) return;
            Current = this;
            RebindInputManager();
            string speaker = dialogue != null && dialogue.dialogueLines.Length > 0 ? dialogue.dialogueLines[0].speakerName : null;
            TutorialPromptHUD.ShowInteract(this, string.IsNullOrEmpty(speaker) ? "Talk" : $"Talk to {speaker}");
        }

        internal void OnPlayerExit(Collider other)
        {
            if (other.CompareTag(playerTag) && Current == this)
                Deactivate(instant: false);
        }

        private void Deactivate(bool instant)
        {
            Current = null;
            TutorialPromptHUD.HideInteract(this, instant: instant);
        }

        private void HandleInteract()
        {
            if (Current != this || dialogue == null || DialogueManager.instance == null) return;
            DialogueManager.instance.StartDialogue(dialogue);
            Deactivate(instant: false);
        }

        private void RebindInputManager()
        {
            GameInputManager inputManager = GameInputManager.Instance;
            if (subscribedInputManager == inputManager) return;
            if (subscribedInputManager != null) subscribedInputManager.OnInteract -= HandleInteract;
            subscribedInputManager = inputManager;
            if (subscribedInputManager != null) subscribedInputManager.OnInteract += HandleInteract;
        }
    }

    internal class DialogueProximityRelay : MonoBehaviour
    {
        private DialogueInteractable owner;

        public void Init(DialogueInteractable parent) => owner = parent;

        private void OnTriggerEnter(Collider other) => owner?.OnPlayerEnter(other);
        private void OnTriggerExit(Collider other) => owner?.OnPlayerExit(other);
    }
}
