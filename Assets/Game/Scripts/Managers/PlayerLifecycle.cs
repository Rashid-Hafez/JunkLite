using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace junklite
{
    /// <summary>
    /// Persistent owner of the single-player actor lifecycle. LevelContext supplies
    /// scene-local spawn configuration; consumers observe spawn/death events.
    /// </summary>
    [DefaultExecutionOrder(0)]
    [DisallowMultipleComponent]
    public sealed class PlayerLifecycle : MonoBehaviour
    {
        public static PlayerLifecycle Instance { get; private set; }

        [Header("Player")]
        [SerializeField] private GameObject playerPrefab;
        [SerializeField, Min(0f)] private float respawnDelay = 2f;
        [SerializeField, Min(0f)] private float deathPresentationFallbackDelay = 1.25f;

        private readonly List<Transform> spawnPoints = new();
        private LevelContext levelContext;
        private SceneSettings legacySceneSettings;
        private PlayerCharacter currentPlayer;
        private int currentSpawnIndex;
        private Coroutine deathRoutine;
        private Coroutine respawnRoutine;

        public PlayerCharacter Player => currentPlayer;
        public GameObject PlayerPrefab => playerPrefab;
        public int CurrentSpawnIndex => currentSpawnIndex;
        public bool ShouldSpawnPlayer => levelContext != null
            ? levelContext.SpawnPlayer
            : legacySceneSettings == null || legacySceneSettings.SpawnPlayer;

        public event Action<PlayerCharacter> PlayerSpawned;
        public event Action<PlayerCharacter> PlayerDied;

        /// <summary>
        /// Presentation consumers use this after the death animation has completed
        /// and the player has been deactivated.
        /// </summary>
        public event Action<PlayerCharacter> PlayerDeathPresentationCompleted;

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                enabled = false;
                Destroy(this);
                return;
            }

            Instance = this;
            DontDestroyOnLoad(gameObject);
        }

        private void OnDestroy()
        {
            CancelPendingOperations();
            UnsubscribeFromPlayer(currentPlayer);

            if (Instance == this)
                Instance = null;
        }

        /// <summary>
        /// Used by the prefab migration and by GameManager's temporary legacy
        /// compatibility path. Existing lifecycle configuration takes priority.
        /// </summary>
        public void ApplyDefaultsIfMissing(
            GameObject fallbackPlayerPrefab,
            float fallbackRespawnDelay,
            float fallbackDeathPresentationDelay,
            bool overwriteTiming = false)
        {
            if (playerPrefab == null)
                playerPrefab = fallbackPlayerPrefab;

            if ((overwriteTiming || respawnDelay <= 0f) && fallbackRespawnDelay > 0f)
                respawnDelay = fallbackRespawnDelay;

            if ((overwriteTiming || deathPresentationFallbackDelay <= 0f) &&
                fallbackDeathPresentationDelay > 0f)
            {
                deathPresentationFallbackDelay = fallbackDeathPresentationDelay;
            }
        }

        /// <summary>Refreshes scene-local configuration after a scene becomes active.</summary>
        public void ConfigureScene(LevelContext context, SceneSettings legacySettings)
        {
            CancelPendingOperations();
            UnsubscribeFromPlayer(currentPlayer);
            currentPlayer = null;
            currentSpawnIndex = 0;
            levelContext = context;
            legacySceneSettings = legacySettings;
            ResolveSpawnPoints();
        }

        public PlayerCharacter SpawnPlayer()
        {
            if (!ShouldSpawnPlayer)
            {
                Debug.Log("[PlayerLifecycle] Player spawn skipped by scene configuration.");
                return null;
            }

            CancelDeathRoutine();
            Vector3 spawnPosition = GetSpawnPosition();

            if (currentPlayer == null)
            {
                if (playerPrefab == null)
                {
                    Debug.LogError("[PlayerLifecycle] No player prefab assigned.");
                    return null;
                }

                GameObject playerObject = Instantiate(playerPrefab, spawnPosition, Quaternion.identity);
                currentPlayer = playerObject.GetComponent<PlayerCharacter>();
                if (currentPlayer == null)
                {
                    Debug.LogError("[PlayerLifecycle] Player prefab is missing PlayerCharacter.");
                    Destroy(playerObject);
                    return null;
                }

                SubscribeToPlayer(currentPlayer);
            }

            currentPlayer.ReviveAt(spawnPosition);
            currentPlayer.Activate();
            ResetPlayerMovementAxis();

            if (currentPlayer.Stats != null)
                currentPlayer.PlayerState.HasDrone = currentPlayer.Stats.HasDrone;

            PlayerCombatTracker.Instance?.ClearCombatState();
            PlayerSpawned?.Invoke(currentPlayer);
            Debug.Log($"[PlayerLifecycle] Player ready at {spawnPosition}.");
            return currentPlayer;
        }

        public void KillPlayer()
        {
            if (currentPlayer == null || !currentPlayer.IsAlive)
                return;

            currentPlayer.Kill();
        }

        public void RestartAtPrimarySpawn()
        {
            currentSpawnIndex = 0;
            currentPlayer?.Deactivate();
            CancelDeathRoutine();

            if (respawnRoutine != null)
                StopCoroutine(respawnRoutine);

            respawnRoutine = StartCoroutine(RespawnAfterDelay());
        }

        public void SetSpawnPoint(int index)
        {
            if (index >= 0 && index < spawnPoints.Count)
                currentSpawnIndex = index;
        }

        /// <summary>Detaches runtime state before the active scene is replaced.</summary>
        public void PrepareForSceneChange()
        {
            CancelPendingOperations();
            UnsubscribeFromPlayer(currentPlayer);
            currentPlayer = null;
            spawnPoints.Clear();
            levelContext = null;
            legacySceneSettings = null;
            currentSpawnIndex = 0;
        }

        private void SubscribeToPlayer(PlayerCharacter player)
        {
            if (player?.State != null)
                player.State.OnDeath += HandlePlayerDeath;
        }

        private void UnsubscribeFromPlayer(PlayerCharacter player)
        {
            if (player?.State != null)
                player.State.OnDeath -= HandlePlayerDeath;
        }

        private void HandlePlayerDeath()
        {
            if (deathRoutine != null)
                return;

            PlayerCharacter deadPlayer = currentPlayer;
            Debug.Log("[PlayerLifecycle] Player died.");
            PlayerDied?.Invoke(deadPlayer);
            deathRoutine = StartCoroutine(CompleteDeathPresentation(deadPlayer));
        }

        private IEnumerator CompleteDeathPresentation(PlayerCharacter deadPlayer)
        {
            float delay = deathPresentationFallbackDelay;

            // Allow animation and other death subscribers to react first.
            yield return null;

            SpineAnimationController spineAnimation = deadPlayer != null
                ? deadPlayer.GetComponentInChildren<SpineAnimationController>(true)
                : null;

            if (spineAnimation != null &&
                spineAnimation.TryGetDeathAnimationDuration(out float animationDuration))
            {
                delay = animationDuration;
            }

            float endTime = Time.unscaledTime + delay;
            while (Time.unscaledTime < endTime)
                yield return null;

            deadPlayer?.Deactivate();
            deathRoutine = null;
            PlayerDeathPresentationCompleted?.Invoke(deadPlayer);
        }

        private IEnumerator RespawnAfterDelay()
        {
            float endTime = Time.realtimeSinceStartup + respawnDelay;
            while (Time.realtimeSinceStartup < endTime)
                yield return null;

            respawnRoutine = null;
            SpawnPlayer();
        }

        private void ResolveSpawnPoints()
        {
            spawnPoints.Clear();

            if (levelContext != null)
            {
                IReadOnlyList<Transform> configuredSpawns = levelContext.GetPlayerSpawns();
                foreach (Transform spawn in configuredSpawns)
                {
                    if (spawn != null && !spawnPoints.Contains(spawn))
                        spawnPoints.Add(spawn);
                }

                if (spawnPoints.Count > 0)
                {
                    Debug.Log($"[PlayerLifecycle] Using {spawnPoints.Count} LevelContext spawn point(s).");
                    return;
                }

                Debug.LogWarning(
                    $"[PlayerLifecycle] LevelContext '{levelContext.DisplayName}' has no player spawn assigned.");
            }

            Scene activeScene = SceneManager.GetActiveScene();
            SpawnPoint[] markers = FindObjectsByType<SpawnPoint>(
                FindObjectsInactive.Exclude,
                FindObjectsSortMode.None);
            var sceneMarkers = new List<SpawnPoint>();

            foreach (SpawnPoint marker in markers)
            {
                if (marker.gameObject.scene == activeScene)
                    sceneMarkers.Add(marker);
            }

            sceneMarkers.Sort((a, b) =>
            {
                int priority = a.Priority.CompareTo(b.Priority);
                return priority != 0 ? priority : string.CompareOrdinal(a.name, b.name);
            });

            foreach (SpawnPoint marker in sceneMarkers)
                spawnPoints.Add(marker.transform);

            if (spawnPoints.Count > 0)
            {
                Debug.Log($"[PlayerLifecycle] Found {spawnPoints.Count} typed spawn point(s).");
                return;
            }

            GameObject[] taggedSpawns = Array.Empty<GameObject>();
            try
            {
                taggedSpawns = GameObject.FindGameObjectsWithTag("SpawnPoint");
            }
            catch (UnityException)
            {
                // Unmigrated projects may not define the legacy tag.
            }

            Array.Sort(taggedSpawns, (a, b) => string.CompareOrdinal(a.name, b.name));
            foreach (GameObject taggedSpawn in taggedSpawns)
            {
                if (taggedSpawn.scene == activeScene)
                    spawnPoints.Add(taggedSpawn.transform);
            }

            if (spawnPoints.Count == 0)
                Debug.LogWarning("[PlayerLifecycle] No spawn points found; using world origin.");
        }

        private Vector3 GetSpawnPosition()
        {
            Transform spawn = GetActiveSpawnPoint();
            return spawn != null ? spawn.position : Vector3.zero;
        }

        private float GetSpawnRotation()
        {
            Transform spawn = GetActiveSpawnPoint();
            return spawn != null ? spawn.eulerAngles.y : 0f;
        }

        private Transform GetActiveSpawnPoint()
        {
            if (spawnPoints.Count == 0)
                return null;

            currentSpawnIndex = Mathf.Clamp(currentSpawnIndex, 0, spawnPoints.Count - 1);
            return spawnPoints[currentSpawnIndex];
        }

        private void ResetPlayerMovementAxis()
        {
            if (currentPlayer?.Controller == null)
                return;

            float rotation = GetSpawnRotation();
            currentPlayer.Controller.ResetToSpawnOrientation(rotation);
            Debug.Log($"[PlayerLifecycle] Movement axis reset from spawn rotation {rotation}°.");
        }

        private void CancelPendingOperations()
        {
            CancelDeathRoutine();

            if (respawnRoutine != null)
            {
                StopCoroutine(respawnRoutine);
                respawnRoutine = null;
            }
        }

        private void CancelDeathRoutine()
        {
            if (deathRoutine == null)
                return;

            StopCoroutine(deathRoutine);
            deathRoutine = null;
        }
    }
}
