using System;
using UnityEngine;

namespace junklite
{
    /// <summary>
    /// Keeps a SlidingDoor locked until an objective is complete and/or the player carries a key
    /// (matched by Unity tag, see PlayerKeyring). While the rule is unmet and the player stands in the
    /// door's proximity trigger, the interact prompt shows and Interact plays the locked dialogue.
    /// Once met, the rule releases its lock and stays silent.
    /// </summary>
    [RequireComponent(typeof(SlidingDoor))]
    public class DoorAccessRule : MonoBehaviour
    {
        [Tooltip("Optional. Door stays locked until this objective is complete.")]
        [SerializeField] private ObjectiveDefinition requiredObjective;
        [Tooltip("Optional. Door stays locked until the player carries a key with this tag.")]
        [SerializeField] private string requiredKeyTag;
        [Tooltip("Optional. Played on Interact while the door is locked.")]
        [SerializeField] private DialogueSequence lockedDialogue;
        [SerializeField] private string lockedPromptLabel = "Locked";
        [Tooltip("Never unlocks (a door the player can see through but not use).")]
        [SerializeField] private bool lockedForever;

        private SlidingDoor door;
        private ObjectiveTracker subscribedTracker;
        private GameInputManager subscribedInput;
        private PlayerKeyring keyring;
        private int playerContacts;
        private bool promptShown;
        private bool? lastSatisfied;

        /// <summary>Raised when the rule flips between met and unmet (e.g. a LaneEntryTrigger gating the same door).</summary>
        public event Action SatisfiedChanged;

        private void Awake()
        {
            door = GetComponent<SlidingDoor>();
            Refresh();
        }

        private void OnEnable()
        {
            Rebind();
            Refresh();
        }

        private void Start()
        {
            Rebind();
            Refresh();
        }

        private void OnDisable()
        {
            Unbind();
            SetKeyring(null);
            playerContacts = 0;
            HidePrompt(instant: true);
            if (door != null) door.RemoveLock(this);
        }

        /// <summary>True when nothing stops the door from opening.</summary>
        public bool IsSatisfied()
        {
            if (lockedForever) return false;
            if (requiredObjective != null &&
                (ObjectiveTracker.Instance == null || !ObjectiveTracker.Instance.IsComplete(requiredObjective)))
                return false;
            return string.IsNullOrEmpty(requiredKeyTag) || (keyring != null && keyring.Has(requiredKeyTag));
        }

        /// <summary>Re-checks the rule, then holds or releases this rule's lock on the door.</summary>
        public void Refresh()
        {
            if (door == null) return;
            bool satisfied = IsSatisfied();
            if (satisfied) door.RemoveLock(this);
            else door.AddLock(this);

            bool changed = lastSatisfied.HasValue && lastSatisfied.Value != satisfied;
            lastSatisfied = satisfied;

            if (playerContacts > 0 && !satisfied) ShowPrompt();
            else HidePrompt(instant: false);

            if (changed) SatisfiedChanged?.Invoke();
        }

        private void OnTriggerEnter(Collider other)
        {
            if (!other.CompareTag("Player")) return;
            PlayerKeyring found = other.GetComponentInParent<PlayerKeyring>();
            if (found != null) SetKeyring(found);
            playerContacts++;
            Rebind();
            Refresh();
        }

        private void OnTriggerExit(Collider other)
        {
            if (!other.CompareTag("Player") || playerContacts == 0) return;
            if (--playerContacts == 0) HidePrompt(instant: false);
        }

        private void HandleInteract()
        {
            if (playerContacts == 0 || lockedDialogue == null || IsSatisfied()) return;
            DialogueManager dialogue = DialogueManager.instance;
            if (dialogue == null || dialogue.IsInDialogue) return;
            dialogue.StartDialogue(lockedDialogue);
        }

        private void HandleKeyringChanged(string _) => Refresh();
        private void HandleObjectivesChanged() => Refresh();

        private void SetKeyring(PlayerKeyring value)
        {
            if (keyring == value) return;
            if (keyring != null) keyring.Changed -= HandleKeyringChanged;
            keyring = value;
            if (keyring != null) keyring.Changed += HandleKeyringChanged;
        }

        private void ShowPrompt()
        {
            if (promptShown) return;
            promptShown = true;
            TutorialPromptHUD.ShowInteract(this, lockedPromptLabel);
        }

        private void HidePrompt(bool instant)
        {
            if (!promptShown) return;
            promptShown = false;
            TutorialPromptHUD.HideInteract(this, instant: instant);
        }

        #region Tracker / input binding

        private void Rebind()
        {
            ObjectiveTracker tracker = ObjectiveTracker.Instance;
            if (tracker != subscribedTracker)
            {
                if (subscribedTracker != null) subscribedTracker.Changed -= HandleObjectivesChanged;
                subscribedTracker = tracker;
                if (subscribedTracker != null) subscribedTracker.Changed += HandleObjectivesChanged;
            }

            GameInputManager input = GameInputManager.Instance;
            if (input != subscribedInput)
            {
                if (subscribedInput != null) subscribedInput.OnInteract -= HandleInteract;
                subscribedInput = input;
                if (subscribedInput != null) subscribedInput.OnInteract += HandleInteract;
            }
        }

        private void Unbind()
        {
            if (subscribedTracker != null)
            {
                subscribedTracker.Changed -= HandleObjectivesChanged;
                subscribedTracker = null;
            }

            if (subscribedInput != null)
            {
                subscribedInput.OnInteract -= HandleInteract;
                subscribedInput = null;
            }
        }

        #endregion
    }
}
