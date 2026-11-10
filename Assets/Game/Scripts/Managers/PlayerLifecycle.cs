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
        [Tooltip("Optional. Take the player prefabs and Use New Player from this other lifecycle instead of the fields below. " +
                 "The Game Root prefab points at the Game Manager prefab, so the Game Manager prefab's checkbox decides the player in every scene.")]
        [SerializeField] private PlayerLifecycle playerSettingsSource;
        [Tooltip("Old player (single mirrored skeleton). Used while Use New Player is off.")]
        [SerializeField] private GameObject playerPrefab;
        [Tooltip("New Cass player (left/right skeletons). Used while Use New Player is on.")]
        [SerializeField] private GameObject newPlayerPrefab;
        [Tooltip("Spawn the new Cass player. Turn off to playtest with the old player. Applies on the next spawn after a scene load.")]
        [SerializeField] private bool useNewPlayer;
        [SerializeField, Min(0f)] private float respawnDelay = 2f;
        [SerializeField, Min(0f)] private float deathPresentationFallbackDelay = 1.25f;

        private readonly List<Transform> spawnPoints = new();
        private readonly HashSet<Checkpoint> checkpoints = new();
        private Checkpoint activeCheckpoint;
        private int activeCheckpointOrder = -1;
        private LevelContext levelContext;
        private SceneSettings legacySceneSettings;
        private PlayerCharacter currentPlayer;
        private int currentSpawnIndex;
        private Coroutine deathRoutine;
        private Coroutine respawnRoutine;

        public PlayerCharacter Player => currentPlayer;
        /// <summary>
        /// The prefab the next spawn uses: the new player when selected and assigned, else the old one.
        /// Read from <see cref="playerSettingsSource"/> when one is assigned (one level deep, never itself).
        /// </summary>
        public GameObject PlayerPrefab
        {
            get
            {
                PlayerLifecycle settings = playerSettingsSource != null && playerSettingsSource != this
                    ? playerSettingsSource : this;
                GameObject chosen = settings.OwnPlayerPrefab;
                return chosen != null ? chosen : OwnPlayerPrefab;
            }
        }

        private GameObject OwnPlayerPrefab => useNewPlayer && newPlayerPrefab != null ? newPlayerPrefab : playerPrefab;

        /// <summary>The prefab the current level spawns: its LevelContext/SceneSettings override, else <see cref="PlayerPrefab"/>.</summary>
        public GameObject LevelPlayerPrefab
        {
            get
            {
                LevelPlayerChoice choice = levelContext != null ? levelContext.PlayerChoice
                    : legacySceneSettings != null ? legacySceneSettings.PlayerChoice
                    : LevelPlayerChoice.GameDefault;
                if (choice == LevelPlayerChoice.GameDefault) return PlayerPrefab;

                PlayerLifecycle settings = playerSettingsSource != null && playerSettingsSource != this
                    ? playerSettingsSource : this;
                GameObject chosen = choice == LevelPlayerChoice.NewPlayer
                    ? (settings.newPlayerPrefab != null ? settings.newPlayerPrefab : newPlayerPrefab)
                    : (settings.playerPrefab != null ? settings.playerPrefab : playerPrefab);
                return chosen != null ? chosen : PlayerPrefab;
            }
        }
        public int CurrentSpawnIndex => currentSpawnIndex;
        /// <summary>The reached checkpoint the next respawn uses; null = the scene spawn.</summary>
        public Checkpoint ActiveCheckpoint => activeCheckpoint;
        /// <summary>True when the level has checkpoints: only a reached one is ever used, never the nearest marker.</summary>
        public bool HasCheckpoints => checkpoints.Count > 0;
        public bool ShouldSpawnPlayer => levelContext != null
            ? levelContext.SpawnPlayer
            : legacySceneSettings == null || legacySceneSettings.SpawnPlayer;

        public event Action<PlayerCharacter> PlayerSpawned;
        public event Action<PlayerCharacter> PlayerDied;
        /// <summary>A checkpoint became the respawn point (progress only, once per checkpoint per run).</summary>
        public event Action<Checkpoint> CheckpointReached;

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
                // A co-located GameManager [RequireComponent]s this, so Unity refuses to
                // remove it; the disabled duplicate unloads with its scene instead.
                if (!TryGetComponent(out GameManager _))
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
            ResolveCheckpoints();
            ResetCheckpointProgress();
        }

        public PlayerCharacter SpawnPlayer()
        {
            if (!ShouldSpawnPlayer)
            {
                return null;
            }

            CancelDeathRoutine();
            Vector3 spawnPosition = GetSpawnPosition();

            if (currentPlayer == null)
            {
                GameObject prefab = LevelPlayerPrefab;
                if (prefab == null)
                {
                    Debug.LogError("[PlayerLifecycle] No player prefab assigned.");
                    return null;
                }

                GameObject playerObject = Instantiate(prefab, spawnPosition, Quaternion.identity);
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
            SpawnedAtCheckpoint = activeCheckpoint != null;
            if (SpawnedAtCheckpoint)
                SnapToNearestLane(currentPlayer.Controller, spawnPosition);

            if (currentPlayer.Stats != null)
                currentPlayer.PlayerState.HasDrone = currentPlayer.Stats.HasDrone;

            PlayerCombatTracker.Instance?.ClearCombatState();
            PlayerSpawned?.Invoke(currentPlayer);

            // After the listeners: CameraManager has just snapped to the spawn camera and the corners
            // were reset on death, so a checkpoint spawn puts the route back the way it was there.
            if (activeCheckpoint != null)
                activeCheckpoint.RestoreRoute();

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
            ResetCheckpointProgress();
            RespawnAtCurrentSpawn();
        }

        /// <summary>Respawns using the currently selected scene spawn point.</summary>
        public void RespawnAtCurrentSpawn()
        {
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

        // ---------------- Checkpoints ----------------

        public void RegisterCheckpoint(Checkpoint checkpoint)
        {
            if (checkpoint != null)
                checkpoints.Add(checkpoint);
        }

        public void UnregisterCheckpoint(Checkpoint checkpoint)
        {
            if (checkpoint != null)
                checkpoints.Remove(checkpoint);
        }

        /// <summary>
        /// The player walked into a checkpoint. It becomes the respawn point only if it is
        /// progress: its Order is not below the active checkpoint's. Returns false (and
        /// changes nothing) for a checkpoint behind the current one, so backtracking never
        /// moves the respawn back. Equal orders (unnumbered checkpoints) are accepted.
        /// </summary>
        public bool TryReachCheckpoint(Checkpoint checkpoint)
        {
            if (checkpoint == null)
                return false;

            checkpoints.Add(checkpoint);
            if (activeCheckpoint != null && checkpoint.Order < activeCheckpointOrder)
                return false;

            activeCheckpoint = checkpoint;
            activeCheckpointOrder = checkpoint.Order;
            CheckpointReached?.Invoke(checkpoint);
            return true;
        }

        /// <summary>
        /// Every checkpoint unreached and the primary spawn active again (new run of the level).
        /// Restart Level does this before respawning; the dev tools use it to jump backwards.
        /// </summary>
        public void ResetCheckpointProgress()
        {
            activeCheckpoint = null;
            activeCheckpointOrder = -1;
            checkpoints.RemoveWhere(checkpoint => checkpoint == null);
            foreach (Checkpoint checkpoint in checkpoints)
                checkpoint.ResetProgress();
        }

        /// <summary>Checkpoints already enabled before this lifecycle looked at the scene (scene objects wake first).</summary>
        private void ResolveCheckpoints()
        {
            checkpoints.RemoveWhere(checkpoint => checkpoint == null);

            Scene activeScene = SceneManager.GetActiveScene();
            Checkpoint[] found = FindObjectsByType<Checkpoint>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
            foreach (Checkpoint checkpoint in found)
            {
                if (checkpoint.gameObject.scene == activeScene)
                    checkpoints.Add(checkpoint);
            }
        }

        /// <summary>Detaches runtime state before the active scene is replaced.</summary>
        public void PrepareForSceneChange()
        {
            CancelPendingOperations();
            UnsubscribeFromPlayer(currentPlayer);
            currentPlayer = null;
            spawnPoints.Clear();
            checkpoints.Clear();
            activeCheckpoint = null;
            activeCheckpointOrder = -1;
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
            if (deadPlayer != null)
                SelectRespawnPoint(deadPlayer.transform.position);
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

            // The death animation runs on scaled time, so hit-stop or slow-mo stretches it; wait
            // the same scaled time so it always finishes. The unscaled cap stops a stuck timeScale
            // from blocking respawn.
            float played = 0f;
            float capTime = Time.unscaledTime + delay + 5f;
            while (played < delay && Time.unscaledTime < capTime)
            {
                played += Time.deltaTime;
                yield return null;
            }

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
            if (activeCheckpoint != null)
                return activeCheckpoint.SpawnTransform;

            if (spawnPoints.Count == 0)
                return null;

            currentSpawnIndex = Mathf.Clamp(currentSpawnIndex, 0, spawnPoints.Count - 1);
            return spawnPoints[currentSpawnIndex];
        }

        /// <summary>
        /// A reached checkpoint wins. A level with checkpoints never respawns at the nearest
        /// marker (that would reward dying next to one): before the first checkpoint it is
        /// the primary spawn. Levels without checkpoints keep the nearest-marker rule.
        /// </summary>
        private void SelectRespawnPoint(Vector3 deathPosition)
        {
            checkpoints.RemoveWhere(checkpoint => checkpoint == null);
            if (activeCheckpoint != null)
                return;

            if (HasCheckpoints)
            {
                currentSpawnIndex = 0;
                return;
            }

            SelectNearestSpawnPoint(deathPosition);
        }

        private void SelectNearestSpawnPoint(Vector3 deathPosition)
        {
            if (spawnPoints.Count == 0)
                return;

            int nearestIndex = currentSpawnIndex;
            float nearestDistance = float.PositiveInfinity;
            for (int i = 0; i < spawnPoints.Count; i++)
            {
                Transform spawn = spawnPoints[i];
                if (spawn == null)
                    continue;

                float distance = (spawn.position - deathPosition).sqrMagnitude;
                if (distance >= nearestDistance)
                    continue;

                nearestDistance = distance;
                nearestIndex = i;
            }

            currentSpawnIndex = nearestIndex;
        }

        /// <summary>The last spawn was at a checkpoint: she starts on the lane, and a ZMovementZone she spawned inside waits for her to walk out and back in.</summary>
        public bool SpawnedAtCheckpoint { get; private set; }

        // Furthest a checkpoint may sit from its lane and still be pulled onto it (m).
        private const float CheckpointLaneSnapRange = 4f;

        /// <summary>A checkpoint spawn lands on the closest lane path, locked to it.</summary>
        private void SnapToNearestLane(Character2D5Controller controller, Vector3 spawnPosition)
        {
            if (controller == null) return;

            float bestSqr = CheckpointLaneSnapRange * CheckpointLaneSnapRange;
            Vector3 bestPoint = spawnPosition;
            float bestYaw = 0f;
            bool found = false;
            foreach (LanePath path in FindObjectsByType<LanePath>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
            {
                if (!path.TryGetClosestPoint(spawnPosition, out Vector3 point, out float pathYaw)) continue;
                Vector3 d = point - spawnPosition;
                d.y = 0f;
                if (d.sqrMagnitude >= bestSqr) continue;
                bestSqr = d.sqrMagnitude;
                bestPoint = point;
                bestYaw = pathYaw;
                found = true;
            }
            if (!found) return;

            // Keep the checkpoint's facing when it runs along the same lane axis (it may face either way).
            float spawnYaw = GetSpawnRotation();
            float yaw = Mathf.Abs(Mathf.DeltaAngle(spawnYaw, bestYaw)) % 180f < 1f ? spawnYaw : bestYaw;
            controller.SnapToLane(bestPoint, yaw);
        }

        private void ResetPlayerMovementAxis()
        {
            if (currentPlayer?.Controller == null)
                return;

            float rotation = GetSpawnRotation();
            currentPlayer.Controller.ResetToSpawnOrientation(rotation);
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
