using System.Collections;
using TMPro;
using UnityEngine;

namespace junklite
{
    /// <summary>
    /// HUD "current objective" panel. Binds the scene ObjectiveTracker's Current objective to an
    /// authored prefab: sets text, toggles the complete mark and fades the CanvasGroup.
    /// All layout, fonts and colours live on the prefab. Lives in the persistent HUD, so it
    /// rebinds whenever a new scene's tracker appears.
    /// </summary>
    public class ObjectiveTrackerView : MonoBehaviour
    {
        [SerializeField] private CanvasGroup group;
        [SerializeField] private TMP_Text title;
        [SerializeField] private TMP_Text body;
        [Tooltip("Shown when the displayed objective completes.")]
        [SerializeField] private GameObject completeMark;
        [Tooltip("Optional. Shown briefly when a new objective appears (e.g. a NEW OBJECTIVE tag).")]
        [SerializeField] private GameObject newMark;

        [Header("Timing (seconds, unscaled)")]
        [SerializeField] private float fadeDuration = 0.3f;
        [SerializeField] private float completeHold = 1.4f;
        [SerializeField] private float newMarkHold = 2.5f;

        private ObjectiveTracker subscribedTracker;
        private ObjectiveDefinition shown;
        private Coroutine routine;

        private void Awake()
        {
            if (group != null) group.alpha = 0f;
            if (completeMark != null) completeMark.SetActive(false);
            if (newMark != null) newMark.SetActive(false);
        }

        private void OnEnable() => Rebind();

        private void OnDisable()
        {
            Unbind();
            routine = null;
            shown = null;
            if (group != null) group.alpha = 0f;
        }

        // Scene trackers come and go under a persistent HUD; the reference check is cheap.
        private void Update()
        {
            if (ObjectiveTracker.Instance != subscribedTracker) Rebind();
        }

        private void Rebind()
        {
            ObjectiveTracker tracker = ObjectiveTracker.Instance;
            if (tracker == subscribedTracker) return;
            Unbind();
            subscribedTracker = tracker;
            if (subscribedTracker != null) subscribedTracker.StateChanged += HandleStateChanged;
            Show(subscribedTracker != null ? subscribedTracker.Current : null, isNew: false);
        }

        private void Unbind()
        {
            if (subscribedTracker != null) subscribedTracker.StateChanged -= HandleStateChanged;
            subscribedTracker = null;
        }

        private void HandleStateChanged(ObjectiveDefinition objective, ObjectiveState state)
        {
            if (state == ObjectiveState.Complete && objective == shown)
                Play(CompleteThenNext());
            else if (state == ObjectiveState.Active && routine == null)
                Show(subscribedTracker.Current, isNew: true);
            // While a completion is playing, CompleteThenNext picks up the newest objective itself.
        }

        private void Show(ObjectiveDefinition objective, bool isNew)
        {
            if (objective == shown && objective != null) return;
            Play(SwapTo(objective, isNew));
        }

        private IEnumerator SwapTo(ObjectiveDefinition objective, bool isNew)
        {
            if (shown != null) yield return Fade(0f);
            shown = objective;
            if (objective == null)
            {
                routine = null;
                yield break;
            }

            Bind(objective);
            if (completeMark != null) completeMark.SetActive(false);
            if (newMark != null) newMark.SetActive(isNew);
            yield return Fade(1f);
            routine = null;

            if (isNew && newMark != null)
            {
                yield return new WaitForSecondsRealtime(newMarkHold);
                if (shown == objective && routine == null) newMark.SetActive(false);
            }
        }

        private IEnumerator CompleteThenNext()
        {
            if (newMark != null) newMark.SetActive(false);
            if (completeMark != null) completeMark.SetActive(true);
            yield return new WaitForSecondsRealtime(completeHold);
            yield return Fade(0f);
            shown = null;

            ObjectiveDefinition next = subscribedTracker != null ? subscribedTracker.Current : null;
            routine = null;
            if (next != null) Play(SwapTo(next, isNew: true));
        }

        private void Bind(ObjectiveDefinition objective)
        {
            if (title != null) title.text = objective.Title;
            if (body != null)
            {
                body.text = objective.Body;
                body.gameObject.SetActive(!string.IsNullOrEmpty(objective.Body));
            }
        }

        private IEnumerator Fade(float to)
        {
            if (group == null) yield break;
            float from = group.alpha;
            for (float t = 0f; t < fadeDuration; t += Time.unscaledDeltaTime)
            {
                group.alpha = Mathf.Lerp(from, to, t / fadeDuration);
                yield return null;
            }
            group.alpha = to;
        }

        private void Play(IEnumerator next)
        {
            if (routine != null) StopCoroutine(routine);
            routine = isActiveAndEnabled ? StartCoroutine(next) : null;
        }
    }
}
