using UnityEngine;

namespace junklite
{
    [ExecuteAlways]
    [RequireComponent(typeof(Light))]
    public class MonitorFlicker : MonoBehaviour
    {
        [SerializeField] private float baseIntensity = 1.2f;
        [SerializeField] private float flickerRange = 0.35f;
        [SerializeField] private float speed = 12f;
        [SerializeField] private float microSpikeChance = 0.08f;
        [SerializeField] private float microSpikeMultiplier = 0.5f;

        private Light targetLight;
        private float noiseOffset;

        private void Awake()
        {
            targetLight = GetComponent<Light>();
            noiseOffset = Random.Range(0f, 100f);
            if (targetLight != null && baseIntensity <= 0f)
            {
                baseIntensity = targetLight.intensity;
            }
        }

        private void OnEnable()
        {
            if (targetLight == null) targetLight = GetComponent<Light>();
            if (targetLight != null && baseIntensity > 0f)
            {
                targetLight.intensity = baseIntensity;
            }
        }

        private void Update()
        {
            if (targetLight == null) return;

            float time = Application.isPlaying ? Time.time : (float)UnityEditor.EditorApplication.timeSinceStartup;
            float n = Mathf.PerlinNoise(noiseOffset, time * speed);
            float current = baseIntensity + (n - 0.5f) * 2f * flickerRange;

            if (Random.value < microSpikeChance)
            {
                current *= (1f - microSpikeMultiplier);
            }

            targetLight.intensity = Mathf.Max(0.05f, current);
        }
    }
}
