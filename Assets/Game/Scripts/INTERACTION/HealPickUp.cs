using junklite;
using UnityEngine;

public class HealPickUp : MonoBehaviour
{
    [SerializeField] private float healAmount = 20f;
    void OnTriggerEnter(Collider other)
    {
        if (other.CompareTag("Player"))
        {
            CharacterBase player = other.GetComponent<CharacterBase>();
            player.Heal(healAmount);
            Destroy(gameObject);
        }
    }
}
