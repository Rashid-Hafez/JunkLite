using UnityEngine;

namespace junklite
{
    /// <summary>
    /// Plays a dialogue sequence when the player walks into this trigger (e.g. Crow on room entry).
    /// If another dialogue is running it waits until that ends, while the player is still inside.
    /// </summary>
    [RequireComponent(typeof(Collider))]
    public class DialogueZone : MonoBehaviour
    {
        [SerializeField] private DialogueSequence sequence;
        [SerializeField] private bool playOnce = true;

        private bool played;

        private void Awake() => GetComponent<Collider>().isTrigger = true;

        private void OnTriggerEnter(Collider other) => TryPlay(other);
        private void OnTriggerStay(Collider other) => TryPlay(other);
        private void OnTriggerExit(Collider other)
        {
            if (!playOnce && other.CompareTag("Player")) played = false;
        }

        private void TryPlay(Collider other)
        {
            if (played || sequence == null || !other.CompareTag("Player")) return;
            DialogueManager dialogue = DialogueManager.instance;
            if (dialogue == null || dialogue.IsInDialogue) return;

            played = true;
            dialogue.StartDialogue(sequence);
        }
    }
}
