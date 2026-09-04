using UnityEngine;

namespace junklite
{
    [RequireComponent(typeof(BoxCollider))]
    public class VolumetricLOD : MonoBehaviour
    {
        [Header("References")]
        [SerializeField] GameObject realVolumetric;
        [SerializeField] GameObject fakeVolumetric;
        [Tooltip("Scene player instance (not the prefab asset).")]
        [SerializeField] Transform player;

        bool highQuality;
        int playerOverlaps;

        void Reset()
        {
            var box = GetComponent<BoxCollider>();
            box.isTrigger = true;
        }

        void Awake()
        {
            var box = GetComponent<BoxCollider>();
            box.isTrigger = true;
        }

        void Start()
        {
            highQuality = false;
            Apply();
        }

         private void OnTriggerEnter(Collider other)
        {
            if (!other.CompareTag("Player"))
            {
                Debug.Log("Volumetric LOD: NOT Player entered trigger, highQuality set to false");
                return;
            }

            playerOverlaps++;
            if (playerOverlaps == 1)
            {
                highQuality = true;
                Apply();
            }
        }

        void OnTriggerExit(Collider other)
        {
            if (!other.CompareTag("Player"))
            {
                Debug.Log("Volumetric LOD: NOT Player exited trigger, highQuality set to false");
                return;
            }

            playerOverlaps--;
            if (playerOverlaps <= 0)
            {
                playerOverlaps = 0;
                highQuality = false;
                Debug.Log("Volumetric LOD: Player exited trigger, highQuality set to false");
                Apply();
            }
        }

        bool IsPlayer(Collider other)
        {
            if (player == null)
                return other.GetComponentInParent<PlayerCharacter>() != null;

            return other.transform == player || other.transform.IsChildOf(player);
        }

        void Apply()
        {
            if (realVolumetric != null)
                realVolumetric.SetActive(highQuality);

            if (fakeVolumetric != null)
                fakeVolumetric.SetActive(!highQuality);
        }
    }
}
