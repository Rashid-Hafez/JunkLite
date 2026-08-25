using junklite;
using UnityEngine;

public class HealPickUp : MonoBehaviour
{
    [SerializeField] private float healAmount = 20f;
    void OnTriggerEnter(Collider other)
    {
        if (other.CompareTag("Player"))
        {
            PlayerCharacter player = other.GetComponent<PlayerCharacter>()
                                  ?? other.GetComponentInParent<PlayerCharacter>();
            if (player == null) return;

            player.Heal(healAmount);
            Destroy(gameObject);
        }
    }
}
