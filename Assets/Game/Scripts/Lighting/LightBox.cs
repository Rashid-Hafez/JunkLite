using UnityEngine;

public class LightBox : MonoBehaviour
{
    [SerializeField] private Light playerLight;
    [SerializeField] private float dimIntensity = 0f;
    [SerializeField] private float brightIntensity = 2f;
    private void OnTriggerEnter(Collider other)
    {
        if (other.CompareTag("Light"))
        {
            playerLight.intensity = brightIntensity;
        }
    }

    private void OnTriggerExit(Collider other)
    {
        if (other.CompareTag("Light"))
        {
            playerLight.intensity = dimIntensity;
        }
    }
}
