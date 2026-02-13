using UnityEngine;

namespace junklite
{
    public class WorldWeaponPickup : MonoBehaviour
    {
        [Header("Weapon Instance Prefab Inside")]
        public WeaponInstance weaponInstance;

        private void Awake()
        {
            // Auto find the WeaponInstance child
            if (weaponInstance == null)
                weaponInstance = GetComponentInChildren<WeaponInstance>(true);
        }

        private void OnEnable()
        {
            // When in the world, the weaponInstance must be disabled
            if (weaponInstance != null)
                weaponInstance.gameObject.SetActive(false);
        }
    }
}
