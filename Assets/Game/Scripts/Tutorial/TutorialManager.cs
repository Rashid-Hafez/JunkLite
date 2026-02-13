using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace junklite
{
    /// <summary>
    /// Lightweight manager for tutorial scenes. Replaces GameManager
    /// </summary>
    [DefaultExecutionOrder(1)]
    public class TutorialManager : MonoBehaviour
    {
        [Header("Player")]
        [SerializeField] private GameObject playerPrefab;
        [SerializeField] private string playerSpawnPointName = "PlayerSpawn";
        [SerializeField] private string demoSpawnPointName = "DemoSpawn";
        [SerializeField] private float respawnDelay = 2f;

        [Header("Enemy")]
        [SerializeField] private GameObject hyenaPrefab;
        [SerializeField] private string enemySpawnPointName = "HyenaSpawn";

        [Header("UI")]
        [SerializeField] private GameObject playerUIPrefab;

        [Header("On Objective Complete")]
        [Tooltip("Colliders to enable when the hyena is killed (e.g. gate, bridge, platform).")]
        [SerializeField] private Collider[] enableOnComplete;
        [Tooltip("GameObjects to enable when the hyena is killed (e.g. VFX, arrows, door open visuals).")]
        [SerializeField] private GameObject[] activateOnComplete;

        [Header("Debug")]
        [SerializeField] private bool showDebugInfo = true;

        // Runtime — found at startup, not serialized
        private PlayerCharacter currentPlayer;
        private PlayerUI playerUIInstance;
        private EnemyCharacter targetEnemy;
        private TestManager testManager;
        private bool objectiveComplete;
        private Coroutine respawnRoutine;
        private Transform playerSpawnTransform;
        private Transform demoSpawnTransform;
        private Transform enemySpawnTransform;
        private Transform gameplayCanvasTransform;

        // Events
        public event System.Action<PlayerCharacter> OnPlayerSpawned;

        // ============================================================
        // STARTUP — split so references exist before anything spawns
        // ============================================================

        private void Awake()
        {

            Debug.Log($"[TutorialManager] Parent: {(transform.parent != null ? transform.parent.name : "ROOT")}");
            Debug.Log($"[TutorialManager] gameObject.scene: {gameObject.scene.name}");

            Time.timeScale = 1f;
            FindGameplayCanvas();
            FindSpawnPoints();

            // Find and disable TestManager until objective complete
            testManager = FindFirstObjectByType<TestManager>(FindObjectsInactive.Include);
            if (testManager != null)
                testManager.gameObject.SetActive(false);
        }

        private void Start()
        {
            SetGateState(false);
            EnsurePlayerUI();
            SpawnEnemy();
            SpawnPlayer();
            ListenForObjective();

            var entry = GetLevelMusicEntry();
            if (entry != null && entry.IsValid)
                AudioManager.Instance?.CrossfadeToMusic(entry);
        }

        // ============================================================
        // SCENE REFERENCE LOOKUP
        // ============================================================

        private SoundEntry GetLevelMusicEntry()
        {
            if (AudioManager.Instance?.Music == null) return null;
            var m = AudioManager.Instance.Music;
            return m.level != null && m.level.IsValid ? m.level : m.gameplay;
        }
        private void FindGameplayCanvas()
        {
            var canvas = GameObject.FindWithTag("GameplayCanvas");
            if (canvas != null)
            {
                gameplayCanvasTransform = canvas.transform;
                return;
            }

            // Fallback: find any Canvas in scene
            var canvasComponent = FindFirstObjectByType<Canvas>();
            if (canvasComponent != null)
            {
                gameplayCanvasTransform = canvasComponent.transform;
                Debug.Log($"[TutorialManager] Found canvas: {canvasComponent.name}");
            }
            else
            {
                Debug.LogWarning("[TutorialManager] No Canvas found in scene for Player UI.");
            }
        }

        private void FindSpawnPoints()
        {
            var spawnPoints = FindObjectsByType<SpawnPoint>(FindObjectsSortMode.None);
            foreach (var sp in spawnPoints)
            {
                if (sp.SpawnPointName == playerSpawnPointName)
                    playerSpawnTransform = sp.transform;
                else if (sp.SpawnPointName == demoSpawnPointName)
                    demoSpawnTransform = sp.transform;
                else if (sp.SpawnPointName == enemySpawnPointName)
                    enemySpawnTransform = sp.transform;
            }

            if (playerSpawnTransform == null)
                Debug.LogWarning($"[TutorialManager] SpawnPoint '{playerSpawnPointName}' not found!");
            if (demoSpawnTransform == null)
                Debug.LogWarning($"[TutorialManager] SpawnPoint '{demoSpawnPointName}' not found!");
            if (enemySpawnTransform == null)
                Debug.LogWarning($"[TutorialManager] SpawnPoint '{enemySpawnPointName}' not found!");
        }

        private Vector3 GetSpawnPosition()
        {
            if (objectiveComplete && demoSpawnTransform != null)
                return demoSpawnTransform.position;

            return playerSpawnTransform != null ? playerSpawnTransform.position : Vector3.zero;
        }

        // ============================================================
        // ENEMY SPAWN
        // ============================================================

        private void SpawnEnemy()
        {
            if (hyenaPrefab == null)
            {
                Debug.LogError("[TutorialManager] No hyena prefab assigned!");
                return;
            }

            Vector3 pos = enemySpawnTransform != null ? enemySpawnTransform.position : Vector3.zero;
            GameObject enemyObj = Instantiate(hyenaPrefab, pos, Quaternion.identity);
            targetEnemy = enemyObj.GetComponent<EnemyCharacter>();

            if (targetEnemy == null)
                Debug.LogError("[TutorialManager] Hyena prefab missing EnemyCharacter component!");
            else
                Debug.Log($"[TutorialManager] Hyena spawned at {pos}");
        }

        // ============================================================
        // PLAYER SPAWN & RESPAWN
        // ============================================================

        private void SpawnPlayer()
        {
            if (playerPrefab == null)
            {
                Debug.LogError("[TutorialManager] No player prefab assigned!");
                return;
            }

            Vector3 pos = GetSpawnPosition();

            GameObject playerObject = Instantiate(playerPrefab, pos, Quaternion.identity);
            currentPlayer = playerObject.GetComponent<PlayerCharacter>();

            if (currentPlayer == null)
            {
                Debug.LogError("[TutorialManager] Player prefab missing PlayerCharacter component!");
                return;
            }

            currentPlayer.ReviveAt(pos);
            currentPlayer.Activate();

            // Invincible until objective complete
            if (currentPlayer.PlayerState != null)
                currentPlayer.PlayerState.SetInvincible(!objectiveComplete);

            // Subscribe to death
            if (currentPlayer.State != null)
                currentPlayer.State.OnDeath += HandlePlayerDeath;

            // Bind UI
            if (playerUIInstance != null)
                playerUIInstance.BindToPlayer(currentPlayer);

            // Notify camera and other systems
            if (CameraManager.Instance != null)
                CameraManager.Instance.ConnectToPlayer(currentPlayer);

            OnPlayerSpawned?.Invoke(currentPlayer);

            Debug.Log($"[TutorialManager] Player spawned at {pos} (invincible: {!objectiveComplete})");
        }

        private void HandlePlayerDeath()
        {
            Debug.Log("[TutorialManager] Player died!");

            if (currentPlayer != null)
                currentPlayer.Deactivate();

            if (respawnRoutine != null)
                StopCoroutine(respawnRoutine);

            respawnRoutine = StartCoroutine(RespawnAfterDelay());
        }

        private IEnumerator RespawnAfterDelay()
        {
            float end = Time.realtimeSinceStartup + respawnDelay;
            while (Time.realtimeSinceStartup < end)
                yield return null;

            Vector3 pos = GetSpawnPosition();

            if (currentPlayer == null)
            {
                // Player object was destroyed — recreate
                GameObject playerObject = Instantiate(playerPrefab, pos, Quaternion.identity);
                currentPlayer = playerObject.GetComponent<PlayerCharacter>();

                if (currentPlayer == null)
                {
                    Debug.LogError("[TutorialManager] Player prefab missing PlayerCharacter!");
                    respawnRoutine = null;
                    yield break;
                }

                if (currentPlayer.State != null)
                    currentPlayer.State.OnDeath += HandlePlayerDeath;

                EnsurePlayerUI();
                if (playerUIInstance != null)
                    playerUIInstance.BindToPlayer(currentPlayer);
            }

            currentPlayer.ReviveAt(pos);
            currentPlayer.Activate();

            // Invincible only while hyena is alive
            if (currentPlayer.PlayerState != null)
                currentPlayer.PlayerState.SetInvincible(!objectiveComplete);

            if (CameraManager.Instance != null)
                CameraManager.Instance.ConnectToPlayer(currentPlayer);

            OnPlayerSpawned?.Invoke(currentPlayer);

            Debug.Log($"[TutorialManager] Player respawned at {pos} (invincible: {!objectiveComplete})");
            respawnRoutine = null;
        }

        // ============================================================
        // UI
        // ============================================================

        private void EnsurePlayerUI()
        {
            if (playerUIInstance != null) return;

            if (gameplayCanvasTransform == null)
            {
                Debug.LogWarning("[TutorialManager] No canvas found, skipping UI.");
                return;
            }

            if (playerUIPrefab == null)
            {
                Debug.LogWarning("[TutorialManager] No UI prefab assigned, skipping UI.");
                return;
            }

            var uiGO = Instantiate(playerUIPrefab, gameplayCanvasTransform);
            uiGO.name = "Player UI";
            playerUIInstance = uiGO.GetComponent<PlayerUI>();
        }

        // ============================================================
        // OBJECTIVE — kill the target enemy
        // ============================================================

        private void ListenForObjective()
        {
            if (targetEnemy == null)
            {
                Debug.LogWarning("[TutorialManager] No target enemy to track!");
                return;
            }

            var sm = targetEnemy.GetComponent<StateMachine>();
            if (sm != null)
                sm.OnStateChanged += OnEnemyStateChanged;
        }

        private void OnEnemyStateChanged(IState from, IState to)
        {
            if (objectiveComplete) return;
            if (to is DeadState)
            {
                objectiveComplete = true;

                // Unsub immediately
                if (targetEnemy != null)
                {
                    var sm = targetEnemy.GetComponent<StateMachine>();
                    if (sm != null)
                        sm.OnStateChanged -= OnEnemyStateChanged;
                }

                // Remove invincibility
                if (currentPlayer != null && currentPlayer.PlayerState != null)
                    currentPlayer.PlayerState.SetInvincible(false);

                // Enable TestManager
                if (testManager != null)
                    testManager.gameObject.SetActive(true);

                Debug.Log("[TutorialManager] Objective complete! Gate opened, invincibility removed, TestManager enabled.");
                SetGateState(true);
            }
        }

        // ============================================================
        // GATE — enable/disable colliders and objects on objective complete
        // ============================================================

        private void SetGateState(bool open)
        {
            if (enableOnComplete != null)
            {
                foreach (var col in enableOnComplete)
                {
                    if (col != null)
                        col.enabled = open;
                }
            }

            if (activateOnComplete != null)
            {
                foreach (var go in activateOnComplete)
                {
                    if (go != null)
                        go.SetActive(open);
                }
            }
        }

        // ============================================================
        // CLEANUP
        // ============================================================

        private void OnDestroy()
        {
            if (targetEnemy != null)
            {
                var sm = targetEnemy.GetComponent<StateMachine>();
                if (sm != null)
                    sm.OnStateChanged -= OnEnemyStateChanged;
            }

            if (currentPlayer != null && currentPlayer.State != null)
                currentPlayer.State.OnDeath -= HandlePlayerDeath;

            if (respawnRoutine != null)
                StopCoroutine(respawnRoutine);
        }

        // ============================================================
        // DEBUG
        // ============================================================

#if UNITY_EDITOR
        private void OnGUI()
        {
            if (!showDebugInfo) return;

            GUILayout.BeginArea(new Rect(10, 10, 320, 180));
            GUILayout.Label("=== TUTORIAL ===");
            GUILayout.Label($"Player: {(currentPlayer != null ? (currentPlayer.IsAlive ? $"Alive (Invincible: {!objectiveComplete})" : "Dead") : "None")}");
            GUILayout.Label($"Hyena: {(targetEnemy != null ? (targetEnemy.IsAlive ? "Alive" : "Dead") : "None")}");
            GUILayout.Label($"Objective: {(objectiveComplete ? "COMPLETE — gate open" : "Kill the hyena")}");
            GUILayout.Label($"TestManager: {(testManager != null ? (testManager.gameObject.activeSelf ? "Enabled" : "Disabled") : "Not found")}");
            GUILayout.Label($"Spawn: {(playerSpawnTransform != null ? playerSpawnTransform.position.ToString("F1") : "?")}");
            GUILayout.Label($"Demo Spawn: {(demoSpawnTransform != null ? demoSpawnTransform.position.ToString("F1") : "?")}");
            GUILayout.Label($"Respawn → {(objectiveComplete ? "Demo spawn (mortal)" : "Start spawn (invincible)")}");
            GUILayout.EndArea();
        }
#endif
    }
}