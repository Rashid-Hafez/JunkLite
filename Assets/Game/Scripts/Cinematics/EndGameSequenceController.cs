using System.Collections;
using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.UI;

namespace junklite
{
    public class EndGameSequenceController : MonoBehaviour
    {
        [Header("Trigger")]
        [SerializeField] private string playerTag = "Player";
        [SerializeField, Min(0f)] private float armTriggerAfterSeconds = 1f;

        [Header("Timeline")]
        [SerializeField] private PlayableDirector director;
        [SerializeField] private PlayableAsset cinematic;

        [Header("Fade To Black")]
        [SerializeField] private Image fadeImage;
        [SerializeField] private CanvasGroup fadeCanvasGroup;
        [SerializeField] private string fallbackFadeImagePath = "FaderCanvas/Image";
        [SerializeField, Min(0f)] private float fadeToBlackDuration = 1f;
        [SerializeField, Min(0f)] private float postFadeHoldDuration = 0f;
        [SerializeField] private bool resetFadeOnAwake = true;
        [SerializeField] private bool moveFadeBehindUiBeforeCompletion = true;

        [Header("Completion")]
        [SerializeField] private bool completeThroughLevelGoal = true;

        private bool sequenceStarted;
        private bool directorStopped;
        private float triggerArmedAt;

        private void Awake()
        {
            if (director == null)
            {
                Debug.LogError("[EndGameSequenceController] PlayableDirector is not assigned. Disable this component to avoid cross-wired timelines.", this);
                enabled = false;
                return;
            }

            if (cinematic == null)
                Debug.LogWarning("[EndGameSequenceController] No cinematic asset assigned. Director default playableAsset will be used.", this);

            ResolveFadeTarget();

            if (resetFadeOnAwake)
                SetFadeAlpha(0f);

            triggerArmedAt = Time.unscaledTime + armTriggerAfterSeconds;
        }

        private void OnDestroy()
        {
            if (director != null)
                director.stopped -= HandleDirectorStopped;
        }

        private void OnTriggerEnter(Collider other)
        {
            if (Time.unscaledTime < triggerArmedAt)
                return;

            if (!string.IsNullOrEmpty(playerTag) && !other.CompareTag(playerTag))
                return;

            BeginSequence();
        }

        public void BeginSequence()
        {
            if (sequenceStarted)
                return;

            sequenceStarted = true;
            StartCoroutine(RunSequence());
        }

        private IEnumerator RunSequence()
        {
            yield return PlayTimeline();
            yield return FadeToBlack();

            if (postFadeHoldDuration > 0f)
                yield return new WaitForSecondsRealtime(postFadeHoldDuration);

            if (moveFadeBehindUiBeforeCompletion)
                MoveFadeBehindUi();

            CompleteLevel();
        }

        private IEnumerator PlayTimeline()
        {
            if (director == null)
                yield break;

            directorStopped = false;
            director.stopped -= HandleDirectorStopped;
            director.stopped += HandleDirectorStopped;

            if (cinematic != null)
                director.Play(cinematic);
            else
                director.Play();

            double duration = director.duration;
            if ((double.IsInfinity(duration) || duration <= 0d) && director.playableAsset != null)
                duration = director.playableAsset.duration;

            while (!directorStopped && director.state == PlayState.Playing)
            {
                if (!double.IsInfinity(duration) && duration > 0d && director.time >= duration - 0.01d)
                    break;

                yield return null;
            }

            director.stopped -= HandleDirectorStopped;
        }

        private IEnumerator FadeToBlack()
        {
            ResolveFadeTarget();

            if (fadeImage == null && fadeCanvasGroup == null)
                yield break;

            if (fadeImage != null)
                fadeImage.gameObject.SetActive(true);

            if (fadeCanvasGroup != null)
                fadeCanvasGroup.gameObject.SetActive(true);

            float startAlpha = GetFadeAlpha();
            if (fadeToBlackDuration <= 0f)
            {
                SetFadeAlpha(1f);
                yield break;
            }

            float elapsed = 0f;
            while (elapsed < fadeToBlackDuration)
            {
                elapsed += Time.unscaledDeltaTime;
                SetFadeAlpha(Mathf.Lerp(startAlpha, 1f, Mathf.Clamp01(elapsed / fadeToBlackDuration)));
                yield return null;
            }

            SetFadeAlpha(1f);
        }

        private void CompleteLevel()
        {
            if (completeThroughLevelGoal && LevelGoal.Instance != null)
            {
                LevelGoal.Instance.Trigger();
                return;
            }

            LevelStatsTracker.Instance?.CompleteLevel();
        }

        private void HandleDirectorStopped(PlayableDirector stoppedDirector)
        {
            if (stoppedDirector == director)
                directorStopped = true;
        }

        private void ResolveFadeTarget()
        {
            if (fadeImage != null || fadeCanvasGroup != null || string.IsNullOrWhiteSpace(fallbackFadeImagePath))
                return;

            GameObject fadeObject = GameObject.Find(fallbackFadeImagePath);
            if (fadeObject == null)
                return;

            fadeImage = fadeObject.GetComponent<Image>();
            fadeCanvasGroup = fadeObject.GetComponent<CanvasGroup>();
        }

        private float GetFadeAlpha()
        {
            if (fadeCanvasGroup != null)
                return fadeCanvasGroup.alpha;

            if (fadeImage != null)
                return fadeImage.color.a;

            return 0f;
        }

        private void SetFadeAlpha(float alpha)
        {
            if (fadeCanvasGroup != null)
                fadeCanvasGroup.alpha = alpha;

            if (fadeImage != null)
            {
                Color color = fadeImage.color;
                color.a = alpha;
                fadeImage.color = color;
            }
        }

        private void MoveFadeBehindUi()
        {
            Transform fadeTransform = fadeCanvasGroup != null
                ? fadeCanvasGroup.transform
                : fadeImage != null
                    ? fadeImage.transform
                    : null;

            if (fadeTransform == null)
                return;

            Transform rootUiElement = fadeImage != null && fadeImage.transform.parent != null
                ? fadeImage.transform.parent
                : fadeTransform;

            rootUiElement.SetAsFirstSibling();
        }
    }
}
