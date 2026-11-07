using System.Collections;
using UnityEngine;

namespace junklite
{
    /// <summary>
    /// The "OBJECTIVE BASED" switch for any item or interactable. When ticked, interacting with
    /// the object completes its objective. Pair it with the object's own interaction code; this
    /// component only links the object to <see cref="ObjectiveTracker"/>.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class ObjectiveLink : MonoBehaviour
    {
        [SerializeField] private bool objectiveBased;
        [SerializeField] private ObjectiveDefinition objective;
        [Tooltip("Optional. Played once, when this object completes its objective.")]
        [SerializeField] private DialogueSequence completedDialogue;

        public bool IsObjectiveBased => objectiveBased && objective != null;
        public ObjectiveDefinition Objective => objective;

        /// <summary>True when the objective is already done. Callers should stay silent in that case.</summary>
        public bool IsComplete => IsObjectiveBased && ObjectiveTracker.Instance != null && ObjectiveTracker.Instance.IsComplete(objective);

        /// <summary>Completes the linked objective. Returns true only when this call changed state.</summary>
        public bool TryComplete()
        {
            if (!IsObjectiveBased || ObjectiveTracker.Instance == null) return false;
            if (!ObjectiveTracker.Instance.Complete(objective)) return false;

            DialogueManager dialogue = DialogueManager.instance;
            if (completedDialogue != null && dialogue != null)
            {
                // Runs on the manager: this object is often hidden right after completing (pickups).
                if (dialogue.IsInDialogue) dialogue.StartCoroutine(PlayWhenFree(dialogue, completedDialogue));
                else dialogue.StartDialogue(completedDialogue);
            }
            return true;
        }

        private static IEnumerator PlayWhenFree(DialogueManager dialogue, DialogueSequence sequence)
        {
            while (dialogue != null && dialogue.IsInDialogue) yield return null;
            if (dialogue != null) dialogue.StartDialogue(sequence);
        }
    }
}
