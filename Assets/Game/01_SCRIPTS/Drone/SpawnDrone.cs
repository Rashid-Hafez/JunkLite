using junklite;
using UnityEngine;


public class SpawnDrone : MonoBehaviour
{
    public GameObject dronePrefab;
    public Transform spawnPoint;
    private PlayerCharacter currentPlayer;


    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
        currentPlayer = GetComponent<PlayerCharacter>();
        if (currentPlayer != null && currentPlayer.State != null)
        {
            Debug.Log("SpawnDrone: PlayerCharacter and State found, subscribing to OnHasDroneChanged event.");
            // Subscribe to drone unlock event
            currentPlayer.State.OnHasDroneChanged += OnHasDroneChanged;
            spawnPoint = currentPlayer.transform; // Set spawn point to player's position

            // If drone is already unlocked at spawn, spawn it immediately
            if (currentPlayer.State.HasDrone)
            {
                Spawn();
            }
        }
    }

    /// <summary>
    /// Called when HasDrone changes. Spawns the drone if unlocked.
    /// THIS IS SUBSCRIBED TO THE EVENT IN CHARACTER STATE!!!!
    /// </summary>
    private void OnHasDroneChanged(bool hasDrone)
    {
        if (hasDrone)
            Spawn();
    }

    /// <summary>
    /// Instantiates the drone prefab and passes the player reference to it.
    /// </summary>
    void Spawn()
    {
        if (dronePrefab != null && spawnPoint != null)
        {
            GameObject drone = Instantiate(dronePrefab, spawnPoint.position, spawnPoint.rotation);
            PetDrone petDrone = drone.GetComponent<PetDrone>();
            if (petDrone != null)
            {
                petDrone.SetPlayer(currentPlayer.gameObject); // Pass player reference for following
            }
        }
    }
}