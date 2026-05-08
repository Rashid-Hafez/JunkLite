using System;
using System.Collections.Generic;
using UnityEngine;

namespace junklite
{
    public readonly struct LevelStats
    {
        public readonly float CompletionTime;
        public readonly int TotalKills;
        public readonly IReadOnlyDictionary<EnemyType, int> KillsByType;
        public readonly PerformanceGrade Grade;

        public LevelStats(float time, int totalKills, Dictionary<EnemyType, int> killsByType, PerformanceGrade grade)
        {
            CompletionTime = time;
            TotalKills = totalKills;
            KillsByType = new Dictionary<EnemyType, int>(killsByType);
            Grade = grade;
        }
    }

    public enum PerformanceGrade { S, A, B, C, D }
    public enum LevelState { Idle, Running, Completed }

    [DefaultExecutionOrder(-100)]
    public class LevelStatsTracker : MonoBehaviour
    {
        public static LevelStatsTracker Instance { get; private set; }

        [Header("Level Settings")]
        [SerializeField] private bool autoStartOnAwake = true;

        [Header("Performance Thresholds (seconds)")]
        [SerializeField] private float sRankTime = 60f;
        [SerializeField] private float aRankTime = 120f;
        [SerializeField] private float bRankTime = 200f;
        [SerializeField] private float cRankTime = 300f;

        public LevelState State { get; private set; } = LevelState.Idle;
        public float ElapsedTime { get; private set; }
        public int TotalKills { get; private set; }
        public int TotalEnemies { get; private set; }
        public LevelStats? LastStats { get; private set; }

        private Dictionary<EnemyType, int> _killsByType = new();

        public event Action<float> OnTimerTick;
        public event Action<EnemyType, int, int> OnEnemyKilled;
        public event Action<LevelStats> OnLevelCompleted;
        public event Action OnLevelStarted;
        public event Action<int> OnTotalEnemiesSet;

        #region Unity Lifecycle

        private void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
            ResetStats();
        }

        private void Start()
        {
            if (autoStartOnAwake) StartLevel();
        }

        private void Update()
        {
            if (State != LevelState.Running) return;
            ElapsedTime += Time.deltaTime;
            OnTimerTick?.Invoke(ElapsedTime);
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        #endregion

        #region Public API

        public void StartLevel()
        {
            ResetStats();
            State = LevelState.Running;
            OnLevelStarted?.Invoke();
        }

        public void CompleteLevel()
        {
            if (State != LevelState.Running)
            {
                Debug.LogWarning("[LevelStatsTracker] CompleteLevel() called but level is not running.");
                return;
            }

            State = LevelState.Completed;
            var grade = CalculateGrade(ElapsedTime);
            LastStats = new LevelStats(ElapsedTime, TotalKills, _killsByType, grade);
            OnLevelCompleted?.Invoke(LastStats.Value);
        }

        public void SetTotalEnemies(int count) { TotalEnemies = count; OnTotalEnemiesSet?.Invoke(count); }

        // Call this from EnemyCharacter.HandleDeath():
        //   LevelStatsTracker.Instance?.NotifyEnemyKilled(this);
        public void NotifyEnemyKilled(EnemyCharacter enemy)
        {
            if (State != LevelState.Running || enemy == null) return;

            var type = enemy.EnemyType;
            if (!_killsByType.ContainsKey(type)) _killsByType[type] = 0;
            _killsByType[type]++;
            TotalKills++;

            OnEnemyKilled?.Invoke(type, _killsByType[type], TotalKills);
        }

        public int GetKillCount(EnemyType type) =>
            _killsByType.TryGetValue(type, out int count) ? count : 0;

        public IReadOnlyDictionary<EnemyType, int> GetAllKills() =>
            new Dictionary<EnemyType, int>(_killsByType);

        #endregion

        #region Helpers

        private void ResetStats()
        {
            ElapsedTime = 0f;
            TotalKills = 0;
            TotalEnemies = 0;
            _killsByType.Clear();
            LastStats = null;
        }

        private PerformanceGrade CalculateGrade(float time)
        {
            if (time <= sRankTime) return PerformanceGrade.S;
            if (time <= aRankTime) return PerformanceGrade.A;
            if (time <= bRankTime) return PerformanceGrade.B;
            if (time <= cRankTime) return PerformanceGrade.C;
            return PerformanceGrade.D;
        }

        public static string FormatTime(float seconds)
        {
            int m = Mathf.FloorToInt(seconds / 60f);
            int s = Mathf.FloorToInt(seconds % 60f);
            return $"{m}:{s:00}";
        }

        public static string FormatTimeVerbose(float seconds)
        {
            int m = Mathf.FloorToInt(seconds / 60f);
            int s = Mathf.FloorToInt(seconds % 60f);
            return $"{m} mins {s} secs";
        }

        #endregion
    }
}