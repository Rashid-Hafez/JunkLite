using UnityEngine;

/// <summary>
/// Generic enemy class for testing behaviour.
/// </summary>
public class GenericEnemy : MonoBehaviour
{
    public int health = 100;
    private EnemyDropManager dropHandler;

    void Awake()
    {
        dropHandler = GetComponent<EnemyDropManager>();
    }

    public void TakeDamage(int damage)
    {
        health -= damage;
        if (health <= 0)
            Die();
    }

    void Die()
    {
        // Call the drop manager attached to THIS enemy
        if (dropHandler != null)
            dropHandler.DropMod();

        Destroy(gameObject); // remove enemy from scene
    }
}
