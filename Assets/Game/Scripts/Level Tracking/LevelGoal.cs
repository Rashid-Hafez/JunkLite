using System.Collections.Generic;
using UnityEngine;

namespace junklite
{
    public enum LevelGoalMode
    {
        KillAll,
        ReachZone,
        Manual,
        RequiredEncounters
    }

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

        [Header("Required Encounters")]
        [SerializeField] private List<EncounterController> requiredEncounters = new();

        private readonly HashSet<EnemyCharacter> _aliveEnemies = new();
        private readonly HashSet<EncounterController> _subscribedEncounters = new();
        private readonly HashSet<EncounterController> _completedEncounters = new();
        private bool _initialized;
        private bool _completed;

        public LevelGoalMode Mode => mode;
        public bool IsCompleted => _completed;
        public int RequiredEncounterCount => _subscribedEncounters.Count;
        public int CompletedEncounterCount => _completedEncounters.Count;

        #region Unity Lifecycle

        private void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
        }

        private void Start()
        {
            _initialized = true;

            if (mode == LevelGoalMode.RequiredEncounters)
            {
                BindRequiredEncounters();
                return;
            }

            if (mode != LevelGoalMode.KillAll)
                return;

            var enemies = autoRegisterAllEnemies
                ? FindObjectsByType<EnemyCharacter>(FindObjectsSortMode.None)
                : trackedEnemies.ToArray();

            foreach (var e in enemies)
            {
                if (e != null && e.IsAlive) RegisterEnemy(e);
            }

            LevelStatsTracker.Instance?.SetTotalEnemies(_aliveEnemies.Count);
        }

        private void OnEnable()
        {
            if (_initialized && mode == LevelGoalMode.RequiredEncounters)
                BindRequiredEncounters();
        }

        private void OnDisable()
        {
            UnsubscribeFromRequiredEncounters();
        }

        private void OnDestroy()
        {
            UnsubscribeFromRequiredEncounters();

            foreach (var e in _aliveEnemies)
            {
                if (e != null && e.attributes != null)
                    e.attributes.OnDeath -= HandleEnemyDeath;
            }

            _aliveEnemies.Clear();
            if (Instance == this) Instance = null;
        }

        #endregion

        #region Encounter Registration

        private void BindRequiredEncounters()
        {
            UnsubscribeFromRequiredEncounters();
            _completedEncounters.Clear();

            if (requiredEncounters == null || requiredEncounters.Count == 0)
            {
                Debug.LogWarning(
                    $"[LevelGoal] '{name}' requires encounters but none are assigned. " +
                    "The level goal will remain incomplete.",
                    this);
                return;
            }

            for (int index = 0; index < requiredEncounters.Count; index++)
            {
                EncounterController encounter = requiredEncounters[index];
                if (encounter == null)
                {
                    Debug.LogWarning(
                        $"[LevelGoal] '{name}' has a null required encounter at index {index}.",
                        this);
                    continue;
                }

                if (!_subscribedEncounters.Add(encounter))
                {
                    Debug.LogWarning(
                        $"[LevelGoal] '{name}' assigns encounter '{encounter.name}' more than once.",
                        this);
                    continue;
                }

                encounter.EncounterCompleted += HandleEncounterCompleted;
                if (encounter.State == EncounterState.Completed)
                    _completedEncounters.Add(encounter);
            }

            EvaluateRequiredEncounters();
        }

        private void UnsubscribeFromRequiredEncounters()
        {
            foreach (EncounterController encounter in _subscribedEncounters)
            {
                if (encounter != null)
                    encounter.EncounterCompleted -= HandleEncounterCompleted;
            }

            _subscribedEncounters.Clear();
        }

        private void HandleEncounterCompleted(EncounterController encounter)
        {
            if (_completed || encounter == null || !_subscribedEncounters.Contains(encounter))
                return;

            if (!_completedEncounters.Add(encounter))
                return;

            EvaluateRequiredEncounters();
        }

        private void EvaluateRequiredEncounters()
        {
            if (!_completed &&
                _subscribedEncounters.Count > 0 &&
                _completedEncounters.Count == _subscribedEncounters.Count)
            {
                Trigger();
            }
        }

        public int ValidateConfiguration(bool logWarnings = true)
        {
            if (mode != LevelGoalMode.RequiredEncounters)
                return 0;

            int issueCount = 0;

            void Report(string message)
            {
                issueCount++;
                if (logWarnings)
                    Debug.LogWarning($"[LevelGoal] '{name}' {message}", this);
            }

            if (requiredEncounters == null || requiredEncounters.Count == 0)
            {
                Report("requires encounters but none are assigned.");
                return issueCount;
            }

            HashSet<EncounterController> uniqueEncounters = new();
            for (int index = 0; index < requiredEncounters.Count; index++)
            {
                EncounterController encounter = requiredEncounters[index];
                if (encounter == null)
                {
                    Report($"has a null required encounter at index {index}.");
                }
                else if (!uniqueEncounters.Add(encounter))
                {
                    Report($"assigns encounter '{encounter.name}' more than once.");
                }
            }

            return issueCount;
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


#if UNITY_EDITOR
        private void OnValidate()
        {
            ValidateConfiguration();
        }
#endif
    }
}
