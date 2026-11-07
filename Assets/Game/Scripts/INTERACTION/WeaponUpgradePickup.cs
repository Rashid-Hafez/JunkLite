using UnityEngine;

namespace junklite
{
    /// <summary>
    /// World pickup that permanently upgrades Cassidy's arm to <see cref="weaponData"/> when the
    /// player presses Interact inside its radius. Same pattern as <see cref="KeyPickup"/>: a
    /// proximity trigger shows the shared interact prompt and this component listens to Interact.
    /// Disables itself when the profile already owns the weapon (second play-through).
    /// </summary>
    public class WeaponUpgradePickup : MonoBehaviour
    {
        [Tooltip("The weapon this pickup grants. Permanent; replaces the current weapon.")]
        [SerializeField] private WeaponData weaponData;
        [Tooltip("Prompt verb, e.g. \"Take\" → \"Take Blades\".")]
        [SerializeField] private string promptVerb = "Take";
        [SerializeField] private float interactRadius = 2.5f;
        [SerializeField] private string playerTag = "Player";

        private GameInputManager subscribedInput;
        private ObjectiveLink objectiveLink;
        private Collider playerInside;

        public WeaponData WeaponData => weaponData;

        private void Awake()
        {
            objectiveLink = GetComponent<ObjectiveLink>();

            var zone = new GameObject("ProximityZone");
            zone.transform.SetParent(transform, false);
            zone.layer = gameObject.layer;
            var col = zone.AddComponent<SphereCollider>();
            col.isTrigger = true;
            col.radius = interactRadius;
            zone.AddComponent<WeaponUpgradePickupProximityRelay>().Init(this);
        }

        private void OnEnable() => Rebind();

        private void Start()
        {
            if (weaponData == null)
            {
                Debug.LogWarning($"[WeaponUpgradePickup] {name}: no WeaponData assigned.", this);
                return;
            }

            if (AlreadyOwned())
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
            if (!other.CompareTag(playerTag) || weaponData == null) return;
            playerInside = other;
            Rebind();
            string label = string.IsNullOrEmpty(weaponData.displayName) ? weaponData.name : weaponData.displayName;
            TutorialPromptHUD.ShowInteract(this, $"{promptVerb} {label}");
        }

        internal void OnPlayerExit(Collider other)
        {
            if (other != playerInside) return;
            playerInside = null;
            TutorialPromptHUD.HideInteract(this);
        }

        private void HandleInteract()
        {
            if (playerInside == null || weaponData == null) return;
            PlayerArmament armament = playerInside.GetComponentInParent<PlayerArmament>();
            if (armament == null)
            {
                Debug.LogWarning($"[WeaponUpgradePickup] {name}: player has no PlayerArmament.", this);
                return;
            }

            if (!armament.TryAcquire(weaponData) && !armament.Owns(weaponData))
                return;

            if (objectiveLink != null) objectiveLink.TryComplete();
            TutorialPromptHUD.HideInteract(this, completed: true);
            playerInside = null;
            gameObject.SetActive(false);
        }

        private bool AlreadyOwned()
        {
            PlayerUpgrades upgrades = PlayerUpgrades.Instance;
            if (upgrades != null) return upgrades.OwnsWeapon(weaponData);
            PlayerArmament armament = PlayerLifecycle.Instance != null && PlayerLifecycle.Instance.Player != null
                ? PlayerLifecycle.Instance.Player.GetComponentInChildren<PlayerArmament>(true)
                : null;
            return armament != null && armament.Owns(weaponData) && armament.Current == weaponData;
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

    internal class WeaponUpgradePickupProximityRelay : MonoBehaviour
    {
        private WeaponUpgradePickup owner;

        public void Init(WeaponUpgradePickup parent) => owner = parent;
        private void OnTriggerEnter(Collider other) => owner?.OnPlayerEnter(other);
        private void OnTriggerExit(Collider other) => owner?.OnPlayerExit(other);
    }
}
