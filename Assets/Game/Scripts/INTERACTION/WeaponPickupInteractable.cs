using UnityEngine;

namespace junklite
{
    /// <summary>
    /// Makes a WorldWeaponPickup interactable: entering its radius shows the shared
    /// TutorialPromptHUD interact prompt; PlayerUI handles the Interact press via Current.
    /// </summary>
    [RequireComponent(typeof(WorldWeaponPickup))]
    public class WeaponPickupInteractable : MonoBehaviour
    {
        [SerializeField] private float interactRadius = 2.5f;
        [SerializeField] private string playerTag = "Player";

        private WorldWeaponPickup weaponPickup;

        public static WeaponPickupInteractable Current { get; private set; }

        public WorldWeaponPickup WeaponPickup => weaponPickup;

        private void Awake()
        {
            weaponPickup = GetComponent<WorldWeaponPickup>();

            var zone = new GameObject("ProximityZone");
            zone.transform.SetParent(transform, false);
            zone.layer = gameObject.layer;
            var col = zone.AddComponent<SphereCollider>();
            col.isTrigger = true;
            col.radius = interactRadius;
            zone.AddComponent<ProximityRelay>().Init(this);
        }

        private void OnDisable()
        {
            if (Current == this) Deactivate(instant: true);
        }

        internal void OnPlayerEnter(Collider other)
        {
            if (!other.CompareTag(playerTag)) return;
            Current = this;
            ReEnablePrompt();
        }

        internal void OnPlayerExit(Collider other)
        {
            if (other.CompareTag(playerTag) && Current == this)
                Deactivate(instant: false);
        }

        /// <summary>Shows the prompt again, e.g. after the weapon menu closes without picking up.</summary>
        public void ReEnablePrompt()
        {
            if (Current != this) return;
            var data = weaponPickup != null && weaponPickup.weaponInstance != null ? weaponPickup.weaponInstance.weaponData : null;
            string weaponName = data != null && !string.IsNullOrEmpty(data.displayName) ? data.displayName : "Weapon";
            TutorialPromptHUD.ShowInteract(this, $"Pick up {weaponName}");
        }

        private void Deactivate(bool instant)
        {
            Current = null;
            TutorialPromptHUD.HideInteract(this, instant: instant);
        }
    }

    internal class ProximityRelay : MonoBehaviour
    {
        private WeaponPickupInteractable owner;

        public void Init(WeaponPickupInteractable parent) => owner = parent;
        private void OnTriggerEnter(Collider other) => owner?.OnPlayerEnter(other);
        private void OnTriggerExit(Collider other) => owner?.OnPlayerExit(other);
    }
}
