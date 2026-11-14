using UnityEngine;
using junklite;

[DisallowMultipleComponent]
public class SpawnDrone : MonoBehaviour
{
    [SerializeField] private GameObject dronePrefab;
    [SerializeField] private Transform spawnPoint;

    private PlayerCharacter currentPlayer;
    private PlayerState playerState;
    private GameObject currentDrone;

    public GameObject CurrentDrone => currentDrone;

    private void Awake()
    {
        currentPlayer = GetComponent<PlayerCharacter>();
        playerState = GetComponent<PlayerState>();

        if (spawnPoint == null)
            spawnPoint = transform;
    }

    private void OnEnable()
    {
        if (playerState != null)
        {
            playerState.OnHasDroneChanged += OnHasDroneChanged;
            playerState.OnDeath += HandlePlayerDeath;
        }

        if (currentPlayer != null)
        {
            currentPlayer.OnActivated += HandlePlayerActivated;
            currentPlayer.OnDeactivated += HandlePlayerDeactivated;
            currentPlayer.OnRevived += HandlePlayerRevived;
        }

        SynchronizeDrone();
    }

    private void Start()
    {
        SynchronizeDrone();
    }

    private void OnDisable()
    {
        if (playerState != null)
        {
            playerState.OnHasDroneChanged -= OnHasDroneChanged;
            playerState.OnDeath -= HandlePlayerDeath;
        }

        if (currentPlayer != null)
        {
            currentPlayer.OnActivated -= HandlePlayerActivated;
            currentPlayer.OnDeactivated -= HandlePlayerDeactivated;
            currentPlayer.OnRevived -= HandlePlayerRevived;
        }

        Despawn();
    }

    private void OnHasDroneChanged(bool hasDrone)
    {
        if (hasDrone)
            SynchronizeDrone();
        else
            Despawn();
    }

    private void HandlePlayerActivated() => SynchronizeDrone();
    private void HandlePlayerRevived() => SynchronizeDrone();
    private void HandlePlayerDeactivated() => Despawn();
    private void HandlePlayerDeath() => Despawn();

    private void SynchronizeDrone()
    {
        if (playerState == null || !playerState.HasDrone ||
            currentPlayer == null || !currentPlayer.IsAlive || !currentPlayer.IsActive)
        {
            Despawn();
            return;
        }

        Spawn();
    }

    private void Spawn()
    {
        if (currentDrone != null || dronePrefab == null || spawnPoint == null)
            return;

        currentDrone = Instantiate(dronePrefab, spawnPoint.position, spawnPoint.rotation);
        PetDrone petDrone = currentDrone.GetComponent<PetDrone>();
        if (petDrone != null)
            petDrone.SetPlayer(currentPlayer);
    }

    private void Despawn()
    {
        if (currentDrone == null)
            return;

        Destroy(currentDrone);
        currentDrone = null;
    }
}
