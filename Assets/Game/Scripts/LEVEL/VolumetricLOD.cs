using UnityEngine;

namespace junklite
{
    public class VolumetricLOD : MonoBehaviour
    {
        [Header("References")]
        [Tooltip("The expensive volumetric light script component.")]
        public MonoBehaviour realVolumetric;
        [Tooltip("The cheap God Ray mesh GameObject.")]
        public GameObject fakeVolumetric;

        [Header("Settings")]
        [Tooltip("Distance at which we swap to the real volumetric light.")]
        public float highQualityRange = 5f;
        [Tooltip("Hysteresis to prevent flickering at the boundary.")]
        public float bufferZone = 0.5f;
        [Tooltip("How often to check the distance (seconds).")]
        public float updateInterval = 0.2f;

        private Transform playerTransform;
        private bool isHighQuality;
        private float nextCheckTime;

        private void Start()
        {
            var player = GameObject.FindWithTag("Player");
            if (player != null) playerTransform = player.transform;

            // Initial state: start as fake to save performance
            SetQuality(false);
        }

        private void Update()
        {
            if (playerTransform == null) return;
            if (Time.time < nextCheckTime) return;

            nextCheckTime = Time.time + updateInterval;

            float distSq = (transform.position - playerTransform.position).sqrMagnitude;
            float threshold = isHighQuality ? (highQualityRange + bufferZone) : highQualityRange;
            
            bool shouldBeHighQuality = distSq < (threshold * threshold);

            if (shouldBeHighQuality != isHighQuality)
            {
                SetQuality(shouldBeHighQuality);
            }
        }

        private void SetQuality(bool highQuality)
        {
            isHighQuality = highQuality;

            if (realVolumetric != null)
                realVolumetric.enabled = highQuality;

            if (fakeVolumetric != null)
                fakeVolumetric.SetActive(!highQuality);
        }

        private void OnDrawGizmosSelected()
        {
            Gizmos.color = Color.yellow;
            Gizmos.DrawWireSphere(transform.position, highQualityRange);
            Gizmos.color = Color.red;
            Gizmos.DrawWireSphere(transform.position, highQualityRange + bufferZone);
        }
    }
}
