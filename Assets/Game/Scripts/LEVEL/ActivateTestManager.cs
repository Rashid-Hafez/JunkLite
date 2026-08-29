using UnityEngine;

public class ActivateTestManager : MonoBehaviour
{
    [SerializeField] GameObject testManager;

   void OnTriggerEnter(Collider other)
    {
        if (other.CompareTag("Player"))
        {
            testManager.SetActive(true);
        }
    }
}
