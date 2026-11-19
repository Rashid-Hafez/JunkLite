using UnityEngine;

namespace junklite
{
    /// <summary>
    /// World key the player takes with Interact. The key id is this GameObject's Unity tag
    /// (e.g. "Z Key"), so doors match it with DoorAccessRule.requiredKeyTag.
    /// Completes an ObjectiveLink on the same object; stays silent once that objective is done.
    /// </summary>
    public class KeyPickup : MonoBehaviour
    {
        [Tooltip("Name shown in the prompt (\"Take Z Key\"). Leave empty to use the tag.")]
        [SerializeField] private string displayName;
        [SerializeField] private float interactRadius = 2f;
        [SerializeField] private string playerTag = "Player";

        private GameInputManager subscribedInput;
        private ObjectiveLink objectiveLink;
        private Collider playerInside;

        public string KeyTag => gameObject.tag;

        private void Awake()
        {
            objectiveLink = GetComponent<ObjectiveLink>();

            var zone = new GameObject("ProximityZone");
            zone.transform.SetParent(transform, false);
            zone.layer = gameObject.layer;
            var col = zone.AddComponent<SphereCollider>();
            col.isTrigger = true;
            col.radius = interactRadius;
            zone.AddComponent<KeyPickupProximityRelay>().Init(this);
        }

        private void OnEnable() => Rebind();
        private void Start() => Rebind();

        private void OnDisable()
        {
            Unbind();
            playerInside = null;
            TutorialPromptHUD.HideInteract(this, instant: true);
        }

        internal void OnPlayerEnter(Collider other)
        {
            if (!other.CompareTag(playerTag) || (objectiveLink != null && objectiveLink.IsComplete)) return;
            playerInside = other;
            Rebind();
            string label = string.IsNullOrEmpty(displayName) ? KeyTag : displayName;
            TutorialPromptHUD.ShowInteract(this, $"Take {label}");
        }

        internal void OnPlayerExit(Collider other)
        {
            if (other != playerInside) return;
            playerInside = null;
            TutorialPromptHUD.HideInteract(this);
        }

        private void HandleInteract()
        {
            if (playerInside == null || (objectiveLink != null && objectiveLink.IsComplete)) return;
            PlayerKeyring keyring = playerInside.GetComponentInParent<PlayerKeyring>();
            if (keyring == null)
            {
                Debug.LogWarning($"[KeyPickup] {name}: player has no PlayerKeyring.", this);
                return;
            }

            keyring.Add(KeyTag);
            if (objectiveLink != null) objectiveLink.TryComplete();
            TutorialPromptHUD.HideInteract(this, completed: true);
            playerInside = null;
            gameObject.SetActive(false);
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

    internal class KeyPickupProximityRelay : MonoBehaviour
    {
        private KeyPickup owner;

        public void Init(KeyPickup parent) => owner = parent;

        private void OnTriggerEnter(Collider other) => owner?.OnPlayerEnter(other);
        private void OnTriggerExit(Collider other) => owner?.OnPlayerExit(other);
    }
}
