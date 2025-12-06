using UnityEngine;

/// <summary>
/// Generic enemy class for testing behaviour.
/// </summary>
public class GenericEnemy : MonoBehaviour
{
    public int health = 100;
    private EnemyDropManager dropHandler;

    [SerializeField] private GameObject lootPrefab; // Assign your prefab in the inspector

    void Update()
    {
        // For testing purposes, press the G key to simulate taking damage
        if (Input.GetKeyDown(KeyCode.G))
        {
            TakeDamage(25); // Simulate taking 25 damage
            Debug.Log("Enemy took damage, current health: " + health);
        }

    }

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
        

        //////////// LOOT DROP HANDLER ////////////
        // Drop loot
        if (dropHandler != null)
            Debug.Log("EnemyDropManager found, proceeding to drop loot.");
        else
            Debug.LogWarning("No EnemyDropManager found on this enemy!");

        // Spawn physical object at enemy's position
        if (lootPrefab != null)
        {
            GameObject lootObj = Instantiate(lootPrefab, transform.position, Quaternion.identity);
            lootObj.GetComponent<ModDrop_Instance>().modData = dropHandler.DropMod();
            lootObj.name = lootObj.GetComponent<ModDrop_Instance>().modData.displayName; //set name of object to the mod name
        }
        else
        {
            Debug.LogWarning("No loot prefab assigned to the enemy!");
        }
        ///////////////////////////////////////////////////
        /// 
        // Destroy or deactivate enemy
        gameObject.SetActive(false);
        Debug.Log("Enemy died");
    }
}
