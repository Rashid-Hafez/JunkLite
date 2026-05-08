using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace junklite
{
    public class LevelResultsScreen : MonoBehaviour
    {
        [Header("Panel")]
        [SerializeField] private GameObject overlayPanel;
        [SerializeField] private float showDelay = 1.2f;
        [SerializeField] private float fadeInDuration = 0.4f;

        [Header("Labels")]
        [SerializeField] private TextMeshProUGUI completionTimeLabel;
        [SerializeField] private TextMeshProUGUI gradeLabel;
        [SerializeField] private TextMeshProUGUI totalKillsLabel;

        [Header("Kill Breakdown")]
        [SerializeField] private Transform killBreakdownContainer;
        [SerializeField] private GameObject killRowPrefab;

        [Header("Buttons")]
        [SerializeField] private Button continueButton;
        [SerializeField] private Button retryButton;
        [SerializeField] private string continueSceneName = "";
        [SerializeField] private string retrySceneName = "";

        [Header("Grade Colours")]
        [SerializeField] private Color gradeS = new Color(1f, 0.84f, 0f);
        [SerializeField] private Color gradeA = new Color(0.55f, 0.9f, 0.3f);
        [SerializeField] private Color gradeB = new Color(0.3f, 0.7f, 1f);
        [SerializeField] private Color gradeC = new Color(1f, 0.6f, 0.2f);
        [SerializeField] private Color gradeD = new Color(0.7f, 0.3f, 0.3f);

        private CanvasGroup _canvasGroup;
        private LevelStatsTracker _tracker;

        #region Unity Lifecycle

        private void Awake()
        {
            if (overlayPanel != null) overlayPanel.SetActive(false);

            _canvasGroup = overlayPanel != null
                ? overlayPanel.GetComponent<CanvasGroup>()
                : GetComponent<CanvasGroup>();

            if (continueButton != null) continueButton.onClick.AddListener(OnContinueClicked);
            if (retryButton != null) retryButton.onClick.AddListener(OnRetryClicked);
        }

        private void OnEnable()
        {
            _tracker = LevelStatsTracker.Instance;
            if (_tracker != null) _tracker.OnLevelCompleted += HandleLevelCompleted;
        }

        private void OnDisable()
        {
            if (_tracker != null) _tracker.OnLevelCompleted -= HandleLevelCompleted;
        }

        #endregion

        #region Show Panel

        private void HandleLevelCompleted(LevelStats stats) => StartCoroutine(ShowWithDelay(stats));

        private IEnumerator ShowWithDelay(LevelStats stats)
        {
            yield return new WaitForSecondsRealtime(showDelay);

            Time.timeScale = 0f;
            PopulateUI(stats);

            if (overlayPanel != null) overlayPanel.SetActive(true);

            if (_canvasGroup != null)
            {
                _canvasGroup.alpha = 0f;
                float elapsed = 0f;
                while (elapsed < fadeInDuration)
                {
                    elapsed += Time.unscaledDeltaTime;
                    _canvasGroup.alpha = Mathf.Clamp01(elapsed / fadeInDuration);
                    yield return null;
                }
                _canvasGroup.alpha = 1f;
            }
        }

        #endregion

        #region Populate UI

        private void PopulateUI(LevelStats stats)
        {
            if (completionTimeLabel != null)
                completionTimeLabel.text = $"Time taken: {LevelStatsTracker.FormatTimeVerbose(stats.CompletionTime)}";

            if (totalKillsLabel != null)
                totalKillsLabel.text = $"Total kills: {stats.TotalKills}";

            if (gradeLabel != null)
            {
                gradeLabel.text = $"Performance: {stats.Grade}";
                gradeLabel.color = GradeToColour(stats.Grade);
            }

            BuildKillBreakdown(stats.KillsByType);
        }

        private void BuildKillBreakdown(IReadOnlyDictionary<EnemyType, int> kills)
        {
            if (killBreakdownContainer == null || killRowPrefab == null) return;

            foreach (Transform child in killBreakdownContainer)
                Destroy(child.gameObject);

            if (kills == null || kills.Count == 0) { SpawnRow("No enemies killed", "—"); return; }

            foreach (var kvp in kills)
            {
                if (kvp.Value > 0) SpawnRow(FormatTypeName(kvp.Key), kvp.Value.ToString());
            }
        }

        private void SpawnRow(string left, string right)
        {
            var row = Instantiate(killRowPrefab, killBreakdownContainer);
            var labels = row.GetComponentsInChildren<TextMeshProUGUI>(true);

            if (labels.Length >= 2) { labels[0].text = left; labels[1].text = right; }
            else if (labels.Length == 1) labels[0].text = $"{left}: {right}";
        }

        #endregion

        #region Buttons

        private void OnContinueClicked()
        {
            Time.timeScale = 1f;

            if (!string.IsNullOrWhiteSpace(continueSceneName))
            {
                SceneManager.LoadScene(continueSceneName);
            }
            else
            {
                int next = SceneManager.GetActiveScene().buildIndex + 1;
                if (next < SceneManager.sceneCountInBuildSettings)
                    SceneManager.LoadScene(next);
                else
                    Debug.LogWarning("[LevelResultsScreen] No next scene in build settings. Assign continueSceneName.");
            }
        }

        private void OnRetryClicked()
        {
            Time.timeScale = 1f;
            GameManager.Instance?.RestartCurrentScene();
        }

        #endregion

        #region Helpers

        private Color GradeToColour(PerformanceGrade grade) => grade switch
        {
            PerformanceGrade.S => gradeS,
            PerformanceGrade.A => gradeA,
            PerformanceGrade.B => gradeB,
            PerformanceGrade.C => gradeC,
            _ => gradeD,
        };

        private static string FormatTypeName(EnemyType type) => type switch
        {
            EnemyType.FlyingDummy => "Flying Dummy",
            _ => type.ToString(),
        };

        #endregion
    }
}