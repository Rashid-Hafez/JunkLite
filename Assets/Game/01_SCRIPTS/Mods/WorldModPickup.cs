using UnityEngine;

namespace junklite
{
    /// <summary>
    /// Place in world for player to pick up.
    /// Holds a reference to the ModData asset and spawns its visual prefab.
    /// </summary>
    public class WorldModPickup : MonoBehaviour
    {
        public ModData modData;

        [Header("Default Visual")]
        [Tooltip("The default visual to show if modData has no visualPrefab")]
        public GameObject defaultVisual;

        private GameObject spawnedVisual;

        private void Start()
        {
            SpawnVisual();
        }

        private void SpawnVisual()
        {
            if (modData != null && modData.visualPrefab != null)
            {
                // Disable default visual
                if (defaultVisual != null)
                    defaultVisual.SetActive(false);

                // Spawn mod's visual as child, centered on parent
                spawnedVisual = Instantiate(modData.visualPrefab, transform);
                spawnedVisual.transform.localPosition = Vector3.zero;
            }
            else
            {
                // Keep default visual enabled
                if (defaultVisual != null)
                    defaultVisual.SetActive(true);
            }
        }

        private void OnDestroy()
        {
            if (spawnedVisual != null)
                Destroy(spawnedVisual);
        }
    }
}