using UnityEngine;
using System.Collections;

public class LightBox : MonoBehaviour
{
    [SerializeField] private Light playerLight;
    [SerializeField] private float transitionDuration = 0.25f;
    [SerializeField] private float dimIntensity = 0f;
    [SerializeField] private float brightIntensity = 2f;
    private void OnTriggerEnter(Collider other)
    {
        if (other.CompareTag("Light"))
        {
            StartCoroutine(FadeLight(brightIntensity, transitionDuration));
        }
    }

    private void OnTriggerExit(Collider other)
    {
        if (other.CompareTag("Light"))
        {
            StartCoroutine(FadeLight(dimIntensity, transitionDuration));
        }
    }

    private IEnumerator FadeLight(float targetIntensity, float duration)
    {
        float startIntensity = playerLight.intensity;
        float elapsedTime = 0f;
        while (elapsedTime < duration)
        {
            playerLight.intensity = Mathf.Lerp(startIntensity, targetIntensity, elapsedTime / duration);
            elapsedTime += Time.deltaTime;
            yield return null;
        }
        playerLight.intensity = targetIntensity; // Ensure it ends at the exact target intensity
    }
}
