using System;
using System.Collections;
using UnityEngine;

namespace junklite
{
    public class PngSequencePlayer : MonoBehaviour
    {
        #region Fields

        [SerializeField] private Animator animator;

        private static readonly int PlayTrigger = Animator.StringToHash("Play");

        public event Action OnComplete;

        #endregion

        #region Lifecycle

        private void Awake() => gameObject.SetActive(false);

        #endregion

        #region Public API

        /// <summary>
        /// Triggers the animation and waits until the last frame. Holds visible — caller decides when to fade out.
        /// </summary>
        public IEnumerator Play()
        {
            gameObject.SetActive(true);
            animator.SetTrigger(PlayTrigger);

            // Wait one frame for the Animator to transition into the clip
            yield return null;

            // Wait until the clip reaches its last frame
            while (true)
            {
                var state = animator.GetCurrentAnimatorStateInfo(0);
                if (state.normalizedTime >= 1f && !animator.IsInTransition(0))
                    break;
                yield return null;
            }
        }

        /// <summary>
        /// Fades out and hides. Call this after your hold delay.
        /// </summary>
        public IEnumerator FadeOutAndHide()
        {
            yield return StartCoroutine(FadeOut());
            gameObject.SetActive(false);
            OnComplete?.Invoke();
        }

        public void Stop()
        {
            StopAllCoroutines();
            gameObject.SetActive(false);
        }

        #endregion

        #region Internal

        private IEnumerator FadeOut()
        {
            // Drive the fade via a CanvasGroup if present, otherwise rely on the animation clip's alpha
            var canvasGroup = GetComponentInChildren<CanvasGroup>();
            if (canvasGroup == null) yield break;

            float elapsed = 0f;
            float duration = 0.4f;

            while (elapsed < duration)
            {
                elapsed += Time.deltaTime;
                canvasGroup.alpha = 1f - Mathf.Clamp01(elapsed / duration);
                yield return null;
            }

            canvasGroup.alpha = 0f;
        }

        #endregion
    }
}