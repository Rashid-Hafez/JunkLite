using UnityEngine;

public class AlarmFlash : MonoBehaviour
{
    [SerializeField] private Light alarmLight;
    [SerializeField] private float minIntensity = 0f;
    [SerializeField] private float maxIntensity = 4f;
    [SerializeField] private float fadeDuration = 0.35f;

    private float timer;

    private void Awake()
    {
        if (alarmLight == null)
            alarmLight = GetComponentInChildren<Light>(true);
    }

    private void OnEnable()
    {
        timer = 0f;
        ApplyIntensity(minIntensity);
    }

    private void Update()
    {
        if (alarmLight == null || fadeDuration <= 0f)
            return;

        timer += Time.deltaTime;

        float cyclePosition = Mathf.PingPong(timer, fadeDuration) / fadeDuration;
        float smoothedPosition = Mathf.SmoothStep(0f, 1f, cyclePosition);

        ApplyIntensity(Mathf.Lerp(minIntensity, maxIntensity, smoothedPosition));
    }

    private void ApplyIntensity(float intensity)
    {
        if (alarmLight != null)
            alarmLight.intensity = intensity;
    }

    private void OnValidate()
    {
        fadeDuration = Mathf.Max(0.01f, fadeDuration);
        minIntensity = Mathf.Max(0f, minIntensity);
        maxIntensity = Mathf.Max(minIntensity, maxIntensity);
    }
}
