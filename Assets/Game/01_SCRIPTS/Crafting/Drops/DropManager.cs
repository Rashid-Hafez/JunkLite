using UnityEditor.Experimental.GraphView;
using UnityEngine;

namespace junklite
{
    /// <summary>
    /// Centralized drop manager. Enemies report death, manager handles drops.
    /// </summary>
    public class DropManager : MonoBehaviour
    {
        public static DropManager Instance { get; private set; }

        [Header("Drop Table")]
        [SerializeField] private DropTable defaultDropTable;

        [Header("Spawn Settings")]
        [SerializeField] private GameObject modPickupPrefab;
        [SerializeField] private Vector3 dropOffset = new Vector3(0f, 0.5f, 0f);
        [SerializeField] private float dropForce = 3f;

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }

            Instance = this;
            DontDestroyOnLoad(gameObject);
        }

        private void OnDestroy()
        {
            if (Instance == this)
                Instance = null;
        }

        /// <summary>
        /// Request a drop at position using default drop table.
        /// </summary>
        public void RequestDrop(Vector3 position, float dropChance = 1f)
        {
            RequestDrop(position, defaultDropTable, dropChance);
        }

        /// <summary>
        /// Request a drop at position using specific drop table.
        /// </summary>
        public void RequestDrop(Vector3 position, DropTable dropTable, float dropChance = 1f)
        {
            if (dropTable == null)
            {
                Debug.LogWarning("DropManager: No drop table provided!");
                return;
            }

            // Roll for drop
            if (Random.value > dropChance)
                return;

            ModData mod = dropTable.GetRandomMod();
            if (mod != null)
                SpawnModPickup(mod, position);
        }

        /// <summary>
        /// Request a specific mod drop at position (guaranteed drop).
        /// </summary>
        public void RequestDrop(Vector3 position, ModData specificMod)
        {
            if (specificMod != null)
                SpawnModPickup(specificMod, position);
        }

        private void SpawnModPickup(ModData modData, Vector3 position)
        {
            if (modPickupPrefab == null)
            {
                Debug.LogWarning("DropManager: No mod pickup prefab assigned!");
                return;
            }

            Vector3 spawnPos = position + dropOffset;
            GameObject pickup = Instantiate(modPickupPrefab, spawnPos, Quaternion.identity);

            var modPickup = pickup.GetComponent<WorldModPickup>();
            if (modPickup != null)
                modPickup.modData = modData;

            var rb = pickup.GetComponent<Rigidbody>();
            if (rb != null)
            {
                Vector3 randomDir = new Vector3(
                    Random.Range(-0.5f, 0.5f),
                    1f,
                    Random.Range(-0.2f, 0.2f)
                ).normalized;

                rb.AddForce(randomDir * dropForce, ForceMode.Impulse);
            }

            Debug.Log($"DropManager: Spawned {modData.modName}");
        }
    }
}