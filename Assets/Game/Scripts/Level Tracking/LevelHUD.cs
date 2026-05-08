using TMPro;
using UnityEngine;

namespace junklite
{
    public class LevelHUD : MonoBehaviour
    {
        [Header("Timer")]
        [SerializeField] private TextMeshProUGUI timerLabel;

        [Header("Kill Counter")]
        [SerializeField] private TextMeshProUGUI killCountLabel;
        [SerializeField] private string killPrefix = "☠ x ";

        [Header("Kill Pulse")]
        [SerializeField] private bool pulseOnKill = true;
        [SerializeField] private float pulseDuration = 0.15f;
        [SerializeField] private float pulseScale = 1.3f;

        private LevelStatsTracker _tracker;
        private float _pulseTimer;
        private bool _isPulsing;
        private Vector3 _killLabelBaseScale;

        #region Unity Lifecycle

        private void Start()
        {
            if (killCountLabel != null)
                _killLabelBaseScale = killCountLabel.transform.localScale;
        }

        private void OnEnable()
        {
            _tracker = LevelStatsTracker.Instance;
            if (_tracker == null) { Debug.LogWarning("[LevelHUD] No LevelStatsTracker in scene.", this); return; }

            _tracker.OnTimerTick += HandleTimerTick;
            _tracker.OnEnemyKilled += HandleEnemyKilled;
            _tracker.OnLevelStarted += HandleLevelStarted;
            _tracker.OnLevelCompleted += HandleLevelCompleted;
            _tracker.OnTotalEnemiesSet += HandleTotalEnemiesSet;

            RefreshTimer(_tracker.ElapsedTime);
            RefreshKills(_tracker.TotalKills);
        }

        private void OnDisable()
        {
            if (_tracker == null) return;
            _tracker.OnTimerTick -= HandleTimerTick;
            _tracker.OnEnemyKilled -= HandleEnemyKilled;
            _tracker.OnLevelStarted -= HandleLevelStarted;
            _tracker.OnLevelCompleted -= HandleLevelCompleted;
            _tracker.OnTotalEnemiesSet -= HandleTotalEnemiesSet;
        }

        private void Update()
        {
            if (!_isPulsing) return;

            _pulseTimer -= Time.deltaTime;
            float t = 1f - (_pulseTimer / pulseDuration);
            float scale = t < 0.5f
                ? Mathf.Lerp(1f, pulseScale, t * 2f)
                : Mathf.Lerp(pulseScale, 1f, (t - 0.5f) * 2f);

            if (killCountLabel != null)
                killCountLabel.transform.localScale = _killLabelBaseScale * scale;

            if (_pulseTimer <= 0f)
            {
                _isPulsing = false;
                if (killCountLabel != null)
                    killCountLabel.transform.localScale = _killLabelBaseScale;
            }
        }

        #endregion

        #region Event Handlers

        private void HandleTimerTick(float elapsed) => RefreshTimer(elapsed);
        private void HandleLevelStarted() { RefreshTimer(0f); RefreshKills(0); }
        private void HandleLevelCompleted(LevelStats s) => RefreshTimer(s.CompletionTime);
        private void HandleTotalEnemiesSet(int total) => RefreshKills(_tracker.TotalKills);

        private void HandleEnemyKilled(EnemyType type, int typeCount, int totalKills)
        {
            RefreshKills(totalKills);
            if (pulseOnKill && killCountLabel != null) { _pulseTimer = pulseDuration; _isPulsing = true; }
        }

        #endregion

        #region Display

        private void RefreshTimer(float seconds)
        {
            if (timerLabel != null)
                timerLabel.text = LevelStatsTracker.FormatTime(seconds);
        }

        private void RefreshKills(int count)
        {
            if (killCountLabel == null) return;
            int total = _tracker != null ? _tracker.TotalEnemies : 0;
            killCountLabel.text = total > 0 ? $"{killPrefix}{count}/{total}" : $"{killPrefix}{count}";
        }

        #endregion
    }
}