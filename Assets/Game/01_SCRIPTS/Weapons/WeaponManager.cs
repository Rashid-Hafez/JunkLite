using UnityEngine;
using System.Collections.Generic;
using System.Collections;

namespace junklite
{
    [RequireComponent(typeof(Collider))]
    public class WeaponManager : MonoBehaviour
    {
        [Header("Weapon Holder")] public Transform weaponHolder;
        
        [Header("Attack Transforms (Scene Anchors)")]
        [SerializeField] private Transform sideAttack;
        [SerializeField] private Transform upAttack;
        [SerializeField] private Transform downAttack;

        [Header("Fallback Hit Radii")]
        [SerializeField] private float sideRadius = 1f;
        [SerializeField] private float upRadius = 1f;
        [SerializeField] private float downRadius = 1f;

        [Header("Hit Detection")]
        [SerializeField] private LayerMask enemyLayer;
        [SerializeField] private LayerMask environmentLayer;
        [Header("Hit Effects")]
        [SerializeField] private GameObject hitParticlePrefab;


        [Header("Recoil")]
        [SerializeField] private float sideRecoil = 6f;

        [Header("Slash Pooling")]
        [SerializeField] private int poolSizePerSlash = 5;
        [SerializeField] private Transform slashPoolRoot;
        private readonly Dictionary<GameObject, Queue<GameObject>> slashPools = new();
        // ================== INTERNAL ==================
        private Rigidbody playerRb;
        private Transform playerTransform;

        public WeaponInstance CurrentWeapon { get; private set; }
        private WorldWeaponPickup storedPickup;

        public event System.Action OnWeaponChanged;

        // ================== PROPERTIES ==================
        public float Facing => Mathf.Sign(playerTransform.localScale.x);

        // ================== UNITY ==================
        private void Awake()
        {
            playerRb = GetComponentInParent<Rigidbody>();
            playerTransform = transform.parent ?? transform;
        }

        private void Update()
        {
            if (UnityEngine.InputSystem.Keyboard.current?.gKey.wasPressedThisFrame == true)
                DropWeapon();

            // Debug: consume mod durability (UNCHANGED)
            if (UnityEngine.InputSystem.Keyboard.current?.hKey.wasPressedThisFrame == true && CurrentWeapon != null)
            {
                var mods = CurrentWeapon.GetActiveMods();
                if (mods.Count > 0)
                    CurrentWeapon.ConsumeModDurability(mods[0], 5f);
            }
        }

        // ================== ATTACK ==================
        public bool Attack(AttackDirection dir)
        {
            if (CurrentWeapon == null)
                return false;

            Transform anchor = GetAttackTransform(dir);
            float radius = GetFallbackRadius(dir);

            if (anchor == null)
                return false;

            AttackHitResult hitResult = CurrentWeapon.TryAttack(
                dir,
                anchor.position,
                radius,
                enemyLayer,
                environmentLayer,
                Facing
            );

            ApplyRecoil(dir, hitResult);

            return hitResult != AttackHitResult.None;
        }


        public Transform GetAttackTransform(AttackDirection dir)
        {
            switch (dir)
            {
                case AttackDirection.Up:
                    return upAttack;
                case AttackDirection.Down:
                    return downAttack;
                default:
                    return sideAttack;
            }
        }

        private float GetFallbackRadius(AttackDirection dir)
        {
            switch (dir)
            {
                case AttackDirection.Up: return upRadius;
                case AttackDirection.Down: return downRadius;
                default: return sideRadius;
            }
        }

        // ================== RECOIL ==================
        private void ApplyRecoil(AttackDirection dir, AttackHitResult hit)
        {
            if (hit == AttackHitResult.None || playerRb == null)
                return;

            switch (dir)
            {
                case AttackDirection.Side:
                    playerRb.AddForce(
                        Vector3.left * Facing * sideRecoil,
                        ForceMode.Impulse
                    );
                    break;

                case AttackDirection.Up:
                   /* playerRb.AddForce(
                        Vector3.down * upRecoil,
                        ForceMode.Impulse
                    );*/
                    break;

                case AttackDirection.Down:
                    // Pogo only on enemies (industry standard)
                   /* if (hit == AttackHitResult.Enemy)
                    {
                        playerRb.AddForce(
                            Vector3.up * downPogoForce,
                            ForceMode.Impulse
                        );
                    }*/
                    break;
            }
        }


        // ================== PICKUPS (UNCHANGED LOGIC) ==================
        private void OnTriggerEnter(Collider other)
        {
            var weaponPickup = other.GetComponent<WorldWeaponPickup>();
            if (weaponPickup != null && CurrentWeapon == null)
            {
                PickupWeapon(weaponPickup);
                return;
            }

            var modPickup = other.GetComponent<WorldModPickup>();
            if (modPickup != null)
                PickupMod(modPickup);
        }

        private void PickupWeapon(WorldWeaponPickup pickup)
        {
            storedPickup = pickup;
            pickup.gameObject.SetActive(false);

            CurrentWeapon = pickup.weaponInstance;
            CurrentWeapon.gameObject.SetActive(true);
            CurrentWeapon.transform.SetParent(transform, false);
            CurrentWeapon.SetOwnerRigidbody(playerRb);

            InitializeSlashPools(CurrentWeapon.weaponData.comboData);
            OnWeaponChanged?.Invoke();
        }

        public void DropWeapon()
        {
            if (CurrentWeapon == null || storedPickup == null)
                return;

            CurrentWeapon.transform.SetParent(storedPickup.transform, false);
            CurrentWeapon.gameObject.SetActive(false);
            CurrentWeapon = null;

            storedPickup.transform.position =
                transform.position + Vector3.right * Facing * 1.2f;

            storedPickup.gameObject.SetActive(true);
            storedPickup = null;

            OnWeaponChanged?.Invoke();
        }

        private void PickupMod(WorldModPickup pickup)
        {
            var inventory = GetComponent<InventoryComponent>();
            if (inventory == null) return;

            inventory.PickupMod(pickup.modData);
            Destroy(pickup.gameObject);
        }


        private void InitializeSlashPools(WeaponComboData comboData)
        {
            slashPools.Clear();

            void Register(GameObject prefab)
            {
                if (prefab == null || slashPools.ContainsKey(prefab))
                    return;

                Queue<GameObject> pool = new();

                for (int i = 0; i < poolSizePerSlash; i++)
                {
                    GameObject slash = Instantiate(prefab, slashPoolRoot);
                    slash.SetActive(false);
                    pool.Enqueue(slash);
                }

                slashPools.Add(prefab, pool);
            }

            foreach (var step in comboData.sideComboSteps)
                Register(step.slashPrefab);

            Register(comboData.upAttack.slashPrefab);
            Register(comboData.downAttack.slashPrefab);
        }



        public GameObject GetSlash(GameObject prefab, Transform attackAnchor)
        {
            if (prefab == null || !slashPools.TryGetValue(prefab, out var pool))
                return null;

            GameObject slash = pool.Count > 0
                ? pool.Dequeue()
                : Instantiate(prefab, slashPoolRoot);

            slash.transform.SetParent(attackAnchor, false);
            slash.transform.localPosition = Vector3.zero;
            slash.transform.localRotation = Quaternion.identity;
            slash.SetActive(true);

            return slash;
        }

        public void ReturnSlash(GameObject prefab, GameObject slash)
        {
            slash.SetActive(false);
            slash.transform.SetParent(slashPoolRoot, false);

            if (slashPools.TryGetValue(prefab, out var pool))
                pool.Enqueue(slash);
        }

        public void PlaySlash(GameObject prefab, Transform attackAnchor, float lifetime = 0.2f)
        {
            GameObject slash = GetSlash(prefab, attackAnchor);
            if (slash == null) return;

            StartCoroutine(ReturnSlashAfterTime(prefab, slash, lifetime));
        }

        public void PlaySlashAt(GameObject prefab, Transform attackAnchor, Vector3 worldContactPoint,float lifetime = 0.12f)
        {
            GameObject slash = GetSlash(prefab, attackAnchor);
            if (slash == null) return;

            // Convert world contact → local space of anchor
            Vector3 localPoint = attackAnchor.InverseTransformPoint(worldContactPoint);
            slash.transform.localPosition = localPoint;

            StartCoroutine(ReturnSlashAfterTime(prefab, slash, lifetime));
        }

        public void PlayHitEffect(Vector3 worldPosition)
        {
            if (hitParticlePrefab == null)
                     return;

            Instantiate(hitParticlePrefab, worldPosition, Quaternion.identity);
            Debug.Log("Hit particle spawned");
        }


        private IEnumerator ReturnSlashAfterTime(GameObject prefab, GameObject slash, float time)
        {
            yield return new WaitForSeconds(time);
            ReturnSlash(prefab, slash);
        }


#if UNITY_EDITOR
        // ================== GIZMOS ==================
        private void OnDrawGizmosSelected()
        {
            if (sideAttack == null || upAttack == null || downAttack == null)
                return;

            Gizmos.color = Color.red;
            Gizmos.DrawWireSphere(sideAttack.position, sideRadius);

            Gizmos.color = Color.green;
            Gizmos.DrawWireSphere(upAttack.position, upRadius);

            Gizmos.color = Color.cyan;
            Gizmos.DrawWireSphere(downAttack.position, downRadius);
        }
#endif
    }

    public enum AttackDirection
    {
        Side,
        Up,
        Down
    }
}
