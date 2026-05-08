using System.Collections.Generic;
using UnityEngine;

namespace junklite
{
    public enum LevelGoalMode { KillAll, ReachZone, Manual }

    public class LevelGoal : MonoBehaviour
    {
        public static LevelGoal Instance { get; private set; }

        [Header("Win Condition")]
        [SerializeField] private LevelGoalMode mode = LevelGoalMode.KillAll;

        [Header("KillAll")]
        [SerializeField] private bool autoRegisterAllEnemies = true;
        [SerializeField] private List<EnemyCharacter> trackedEnemies = new();

        [Header("ReachZone")]
        [SerializeField] private string playerTag = "Player";

        private readonly HashSet<EnemyCharacter> _aliveEnemies = new();
        private bool _completed;

        #region Unity Lifecycle

        private void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
        }

        private void Start()
        {
            if (mode != LevelGoalMode.KillAll) return;

            var enemies = autoRegisterAllEnemies
                ? FindObjectsByType<EnemyCharacter>(FindObjectsSortMode.None)
                : trackedEnemies.ToArray();

            foreach (var e in enemies)
            {
                if (e != null && e.IsAlive) RegisterEnemy(e);
            }

            Debug.Log($"[LevelGoal] Tracking {_aliveEnemies.Count} enemies.");
            LevelStatsTracker.Instance?.SetTotalEnemies(_aliveEnemies.Count);
        }

        private void OnDestroy()
        {
            foreach (var e in _aliveEnemies)
            {
                if (e != null && e.attributes != null)
                    e.attributes.OnDeath -= HandleEnemyDeath;
            }

            _aliveEnemies.Clear();
            if (Instance == this) Instance = null;
        }

        #endregion

        #region Enemy Registration

        // Call from your wave spawner: LevelGoal.Instance?.RegisterEnemy(enemy);
        public void RegisterEnemy(EnemyCharacter enemy)
        {
            if (enemy == null || enemy.attributes == null || _aliveEnemies.Contains(enemy)) return;
            _aliveEnemies.Add(enemy);
            enemy.attributes.OnDeath += HandleEnemyDeath;
        }

        private void HandleEnemyDeath()
        {
            // Clean up any dead enemies from the set
            _aliveEnemies.RemoveWhere(e =>
            {
                if (e == null || !e.IsAlive)
                {
                    if (e != null && e.attributes != null)
                        e.attributes.OnDeath -= HandleEnemyDeath;
                    return true;
                }
                return false;
            });

            Debug.Log($"[LevelGoal] Enemy died. Remaining: {_aliveEnemies.Count}");

            if (_aliveEnemies.Count == 0) Trigger();
        }

        #endregion

        #region Zone Trigger

        private void OnTriggerEnter(Collider other)
        {
            if (mode != LevelGoalMode.ReachZone) return;
            if (string.IsNullOrEmpty(playerTag) || other.CompareTag(playerTag)) Trigger();
        }

        #endregion

        #region Complete

        public void Trigger()
        {
            if (_completed) return;
            _completed = true;

            Debug.Log("[LevelGoal] Level complete!");
            LevelStatsTracker.Instance?.CompleteLevel();
        }

        #endregion

        #region Gizmos

        private void OnDrawGizmosSelected()
        {
            if (mode != LevelGoalMode.ReachZone) return;
            Gizmos.color = new Color(0f, 1f, 0.4f, 0.25f);
            var col = GetComponent<Collider>();
            if (col != null) Gizmos.DrawCube(col.bounds.center, col.bounds.size);
        }

        #endregion
    }
}