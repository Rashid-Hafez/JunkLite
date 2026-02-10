using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace junklite
{
    /// <summary>
    /// Lightweight manager for tutorial scenes.
    /// Spawns the player as invincible and transitions to the next scene
    /// when the target enemy is killed.
    /// 
    /// Does NOT use DontDestroyOnLoad — lives and dies with the tutorial scene.
    /// Place in the tutorial scene alongside a spawn point and the target enemy.
    /// </summary>
    [DefaultExecutionOrder(1)]
    public class TutorialManager : MonoBehaviour
    {
        [Header("Player")]
        [SerializeField] private GameObject playerPrefab;
        [SerializeField] private Transform spawnPoint;

        [Header("UI")]
        [SerializeField] private GameObject playerUIPrefab;
        [SerializeField] private Transform gameplayCanvasTransform;

        [Header("Tutorial Objective")]
        [Tooltip("The enemy that must be killed to complete the tutorial.")]
        [SerializeField] private EnemyCharacter targetEnemy;

        [Header("Scene Transition")]
        [Tooltip("Scene to load when the objective is complete. Uses scene name or next build index if empty.")]
        [SerializeField] private string nextSceneName;
        [SerializeField] private float transitionDelay = 1.5f;

        [Header("Debug")]
        [SerializeField] private bool showDebugInfo = true;

        private PlayerCharacter currentPlayer;
        private PlayerUI playerUIInstance;
        private bool objectiveComplete;

        private void Start()
        {
            EnsurePlayerUI();
            SpawnPlayer();
            ListenForObjective();
        }

        // ============================================================
        // PLAYER SPAWN
        // ============================================================

        private void SpawnPlayer()
        {
            if (playerPrefab == null)
            {
                Debug.LogError("[TutorialManager] No player prefab assigned!");
                return;
            }

            Vector3 pos = spawnPoint != null ? spawnPoint.position : Vector3.zero;

            GameObject playerObject = Instantiate(playerPrefab, pos, Quaternion.identity);
            currentPlayer = playerObject.GetComponent<PlayerCharacter>();

            if (currentPlayer == null)
            {
                Debug.LogError("[TutorialManager] Player prefab missing PlayerCharacter component!");
                return;
            }

            currentPlayer.ReviveAt(pos);
            currentPlayer.Activate();

            // Make player invincible for the tutorial
            if (currentPlayer.PlayerState != null)
                currentPlayer.PlayerState.SetInvincible(true);

            // Bind UI
            if (playerUIInstance != null)
                playerUIInstance.BindToPlayer(currentPlayer);

            if (CameraManager.Instance != null)
                CameraManager.Instance.ConnectToPlayer(currentPlayer);

            Debug.Log($"[TutorialManager] Player spawned (invincible) at {pos}");
        }

        // ============================================================
        // UI
        // ============================================================

        private void EnsurePlayerUI()
        {
            if (playerUIInstance != null) return;

            if (gameplayCanvasTransform == null || playerUIPrefab == null)
            {
                Debug.LogWarning("[TutorialManager] UI prefab or canvas not assigned, skipping UI.");
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
                Debug.LogWarning("[TutorialManager] No target enemy assigned!");
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
                var sm = targetEnemy.GetComponent<StateMachine>();
                if (sm != null)
                    sm.OnStateChanged -= OnEnemyStateChanged;

                Debug.Log("[TutorialManager] Objective complete! Transitioning...");
                StartCoroutine(TransitionAfterDelay());
            }
        }

        private IEnumerator TransitionAfterDelay()
        {
            yield return new WaitForSeconds(transitionDelay);

            // Remove invincibility before leaving
            if (currentPlayer != null && currentPlayer.PlayerState != null)
                currentPlayer.PlayerState.SetInvincible(false);

            if (!string.IsNullOrEmpty(nextSceneName))
            {
                SceneManager.LoadScene(nextSceneName);
            }
            else
            {
                int nextIndex = SceneManager.GetActiveScene().buildIndex + 1;
                if (nextIndex < SceneManager.sceneCountInBuildSettings)
                    SceneManager.LoadScene(nextIndex);
                else
                    Debug.LogWarning("[TutorialManager] No next scene in build settings!");
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
        }

        // ============================================================
        // DEBUG
        // ============================================================

#if UNITY_EDITOR
        private void OnGUI()
        {
            if (!showDebugInfo) return;

            GUILayout.BeginArea(new Rect(10, 10, 250, 120));
            GUILayout.Label("=== TUTORIAL ===");
            GUILayout.Label($"Player: {(currentPlayer != null ? "Spawned (Invincible)" : "None")}");
            GUILayout.Label($"Target: {(targetEnemy != null ? (targetEnemy.IsAlive ? "Alive" : "Dead") : "None")}");
            GUILayout.Label($"Objective: {(objectiveComplete ? "COMPLETE" : "Kill the enemy")}");
            GUILayout.Label($"Next: {(string.IsNullOrEmpty(nextSceneName) ? "Next Build Index" : nextSceneName)}");
            GUILayout.EndArea();
        }
#endif
    }
}