using UnityEngine;

public class ActivateTestManager : MonoBehaviour
{
    [SerializeField] GameObject testManager;

   void OnTriggerEnter(Collider other)
    {
        if (other.CompareTag("Player"))
        {
            Debug.Log("Player entered the trigger zone. Activating Test Manager.");
            testManager.SetActive(true);
        }
    }
}
