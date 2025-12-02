using UnityEngine;
using UnityEngine.InputSystem;

namespace junklite
{
    [RequireComponent(typeof(Collider))]
    public class WeaponHolder : MonoBehaviour
    {
        [Header("Where the weapon sits on the player")]
        [SerializeField] private Transform weaponSocket;

        public WeaponInstance CurrentWeapon { get; private set; }

        // Reference to the world pickup that represents this weapon in the world
        private WorldWeaponPickup storedPickup;

        public event System.Action OnWeaponChanged;

        private void Awake()
        {
            // Make sure our collider is trigger-based
            var col = GetComponent<Collider>();
            //col.isTrigger = true;
        }

        private void Update()
        {
            if (Keyboard.current?.gKey.wasPressedThisFrame == true)
            {
                Debug.Log("[DEBUG] Dropping weapon (G pressed)");
                DropWeapon();
            }

            if (Keyboard.current?.hKey.wasPressedThisFrame == true)
            {
                if (CurrentWeapon != null)
                {
                    var mods = CurrentWeapon.GetActiveMods();

                    if (mods.Count > 0)
                    {
                        Debug.Log("[DEBUG] Consuming durability from first mod (H pressed)");
                        CurrentWeapon.ConsumeModDurability(mods[0], 5f);
                    }
                    else
                    {
                        Debug.Log("[DEBUG] No mods to consume.");
                    }
                }
                else
                {
                    Debug.Log("[DEBUG] No weapon equipped, cannot consume mod.");
                }
            }
        }
        private void OnTriggerEnter(Collider other)
        {
            // ---- WEAPON PICKUP ----
            var weaponPickup = other.GetComponent<WorldWeaponPickup>();
            if (weaponPickup != null && CurrentWeapon == null)
            {
                PickupWeapon(weaponPickup);
                return;
            }

            // ---- MOD PICKUP ----
            var modPickup = other.GetComponent<WorldModPickup>();
            if (modPickup != null)
            {
                PickupMod(modPickup);
                return;
            }
        }
        private void PickupWeapon(WorldWeaponPickup pickup)
        {
            storedPickup = pickup;

            // Disable the world object
            pickup.gameObject.SetActive(false);

            // Move WeaponInstance to player
            CurrentWeapon = pickup.weaponInstance;
            CurrentWeapon.gameObject.SetActive(true);
            CurrentWeapon.transform.SetParent(weaponSocket, false);

            OnWeaponChanged?.Invoke();
        }

        public void DropWeapon()
        {
            if (CurrentWeapon == null || storedPickup == null)
                return;

            // Move weapon instance back under pickup
            CurrentWeapon.transform.SetParent(storedPickup.transform, false);
            CurrentWeapon.gameObject.SetActive(false);
            CurrentWeapon = null;

            // Drop direction based on facing (localScale.x)
            float facing = Mathf.Sign(transform.localScale.x);
            float dropDistance = 1.2f;

            Vector3 dropPos = transform.position + new Vector3(facing * dropDistance, 0f, 0f);

            // Place pickup
            storedPickup.transform.SetParent(null);
            storedPickup.transform.position = dropPos;
            storedPickup.gameObject.SetActive(true);

            storedPickup = null;

            OnWeaponChanged?.Invoke();
        }



        private void PickupMod(WorldModPickup pickup)
        {
            var inventory = GetComponent<InventoryComponent>();
            if (inventory == null)
            {
                Debug.LogWarning("No InventoryComponent found!");
                return;
            }

            // Add to inventory (this will auto-equip if weapon has slots)
            inventory.PickupMod(pickup.modData);

            // Hide or destroy mod pickup
            Destroy(pickup.gameObject);
        }


        public void Attack()
        {
            CurrentWeapon?.Attack();
        }

        public bool TryAddMod(Mod_Data mod)
        {
            if (CurrentWeapon == null) return false;
            return CurrentWeapon.TryAddMod(mod);
        }

        public void RemoveMod(ModRuntimeInstance runtime)
        {
            CurrentWeapon?.RemoveMod(runtime);
        }
    }
}
