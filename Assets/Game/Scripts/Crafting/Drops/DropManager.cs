using UnityEngine;

namespace junklite
{
    public class DropManager : MonoBehaviour
    {
        public static DropManager Instance { get; private set; }

        [Header("Drop Table")]
        [SerializeField] private DropTable defaultDropTable;

        [Header("Spawn Settings")]
        [SerializeField] private GameObject modPickupPrefab;
        [SerializeField] private Vector3 dropOffset = new Vector3(0f, 0.5f, 0f);
        [SerializeField] private float dropForce = 3f;

        private ModData _lastDroppedMod;

        #region Lifecycle

        private void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
            DontDestroyOnLoad(gameObject);
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        #endregion

        #region Public API

        public void RequestDrop(Vector3 position, float dropChance = 1f)
            => RequestDrop(position, defaultDropTable, dropChance);

        public void RequestDrop(Vector3 position, DropTable dropTable, float dropChance = 1f)
        {
            if (dropTable == null) { Debug.LogWarning("DropManager: No drop table provided!"); return; }
            if (Random.value > dropChance) return;

            ModData mod = GetNonRepeatMod(dropTable);
            if (mod != null) SpawnModPickup(mod, position);
        }

        public void RequestDrop(Vector3 position, ModData specificMod)
        {
            if (specificMod != null) SpawnModPickup(specificMod, position);
        }

        #endregion

        #region Drop Logic

        private ModData GetNonRepeatMod(DropTable dropTable)
        {
            const int maxAttempts = 10;
            ModData mod = null;

            for (int i = 0; i < maxAttempts; i++)
            {
                mod = dropTable.GetRandomMod();
                if (mod == null || mod != _lastDroppedMod) break;
            }

            return mod;
        }

        private void SpawnModPickup(ModData modData, Vector3 position)
        {
            if (modPickupPrefab == null) { Debug.LogWarning("DropManager: No mod pickup prefab assigned!"); return; }

            _lastDroppedMod = modData;

            Vector3 spawnPos = position + dropOffset;
            GameObject pickup = Instantiate(modPickupPrefab, spawnPos, Quaternion.identity);

            var modPickup = pickup.GetComponent<WorldModPickup>();
            if (modPickup != null) modPickup.modData = modData;

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

        }

        #endregion
    }
}
