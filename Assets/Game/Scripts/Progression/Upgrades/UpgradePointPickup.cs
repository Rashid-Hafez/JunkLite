using UnityEngine;

namespace junklite
{
    /// <summary>
    /// World pickup that grants upgrade points with Interact. Same pattern as <see cref="KeyPickup"/>.
    /// Collected once per profile: the source id (scene + object name unless overridden) is recorded in
    /// <see cref="PlayerUpgrades"/>, and an already-collected pickup disables itself on Start.
    /// </summary>
    public class UpgradePointPickup : MonoBehaviour
    {
        [SerializeField, Min(1)] private int points = 1;
        [Tooltip("Profile key for this pickup. Leave empty to use \"<scene>/<object name>\".")]
        [SerializeField] private string sourceId;
        [Tooltip("Prompt text, e.g. \"Take Upgrade Point\".")]
        [SerializeField] private string promptLabel = "Take Upgrade Point";
        [SerializeField] private float interactRadius = 2f;
        [SerializeField] private string playerTag = "Player";

        private GameInputManager subscribedInput;
        private ObjectiveLink objectiveLink;
        private Collider playerInside;

        public int Points => points;
        public string SourceId => string.IsNullOrEmpty(sourceId) ? $"{gameObject.scene.name}/{gameObject.name}" : sourceId;

        private void Awake()
        {
            objectiveLink = GetComponent<ObjectiveLink>();

            var zone = new GameObject("ProximityZone");
            zone.transform.SetParent(transform, false);
            zone.layer = gameObject.layer;
            var col = zone.AddComponent<SphereCollider>();
            col.isTrigger = true;
            col.radius = interactRadius;
            zone.AddComponent<UpgradePointPickupProximityRelay>().Init(this);
        }

        private void OnEnable() => Rebind();

        private void Start()
        {
            PlayerUpgrades upgrades = PlayerUpgrades.Instance;
            if (upgrades != null && upgrades.HasCollected(SourceId))
            {
                gameObject.SetActive(false);
                return;
            }
            Rebind();
        }

        private void OnDisable()
        {
            Unbind();
            playerInside = null;
            TutorialPromptHUD.HideInteract(this, instant: true);
        }

        internal void OnPlayerEnter(Collider other)
        {
            if (!other.CompareTag(playerTag)) return;
            playerInside = other;
            Rebind();
            TutorialPromptHUD.ShowInteract(this, promptLabel);
        }

        internal void OnPlayerExit(Collider other)
        {
            if (other != playerInside) return;
            playerInside = null;
            TutorialPromptHUD.HideInteract(this);
        }

        private void HandleInteract()
        {
            if (playerInside == null) return;
            PlayerUpgrades upgrades = PlayerUpgrades.Instance;
            if (upgrades == null)
            {
                Debug.LogWarning($"[UpgradePointPickup] {name}: no PlayerUpgrades service.", this);
                return;
            }

            upgrades.TryCollectPoints(SourceId, points);
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

    internal class UpgradePointPickupProximityRelay : MonoBehaviour
    {
        private UpgradePointPickup owner;

        public void Init(UpgradePointPickup parent) => owner = parent;
        private void OnTriggerEnter(Collider other) => owner?.OnPlayerEnter(other);
        private void OnTriggerExit(Collider other) => owner?.OnPlayerExit(other);
    }
}
