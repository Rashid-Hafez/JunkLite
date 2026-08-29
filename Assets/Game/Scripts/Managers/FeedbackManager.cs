using System.Collections;
using Unity.Cinemachine;
using UnityEngine;

namespace junklite
{
    /// <summary>
    /// Simple feedback manager for combat feel: hitstop and camera shake.
    /// </summary>
    public class FeedbackManager : MonoBehaviour
    {
        public static FeedbackManager Instance { get; private set; }

        [Header("Hitstop Settings")]
        [SerializeField] private float defaultHitstopDuration = 0.05f;

        [Header("Camera Shake")]
        [SerializeField] private float defaultShakeForce = 1f;

        private Coroutine hitstopCoroutine;

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }

            Instance = this;
            DontDestroyOnLoad(gameObject);
        }

        private void OnDestroy()
        {
            if (Instance == this)
                Instance = null;
        }

        /// <summary>
        /// Triggers a brief hitstop effect by freezing time.
        /// </summary>
        public void DoHitstop(float duration = -1f)
        {
            if (duration < 0f)
                duration = defaultHitstopDuration;

            if (hitstopCoroutine != null)
                StopCoroutine(hitstopCoroutine);

            hitstopCoroutine = StartCoroutine(HitstopRoutine(duration));
        }

        /// <summary>
        /// Triggers camera shake via Cinemachine Impulse.
        /// </summary>
        public void DoCameraShake(CinemachineImpulseSource impulseSource, float force = -1f)
        {
            if (impulseSource == null)
                return;

            if (force < 0f)
                force = defaultShakeForce;

            impulseSource.GenerateImpulse(force);
        }

        /// <summary>
        /// Convenience version that finds a suitable impulse source automatically.
        /// Uses the first CINEMACHINE source found on the player or main camera.
        /// Falls back to doing nothing if none exists.
        /// </summary>
        public void DoCameraShake(float force = -1f)
        {
            CinemachineImpulseSource src = null;
            // try player character first
            var player = PlayerLifecycle.Instance?.Player;
            if (player != null)
                src = player.GetComponent<CinemachineImpulseSource>()
                      ?? player.GetComponentInChildren<CinemachineImpulseSource>();

            // try main camera second
            if (src == null && Camera.main != null)
                src = Camera.main.GetComponent<CinemachineImpulseSource>()
                      ?? Camera.main.GetComponentInChildren<CinemachineImpulseSource>();

            if (src == null)
                return;

            DoCameraShake(src, force);
        }

        /// <summary>
        /// Convenience method to trigger both hitstop and camera shake together.
        /// </summary>
        public void DoHitFeedback(CinemachineImpulseSource impulseSource, float hitstopDuration = -1f, float shakeForce = -1f)
        {
            DoHitstop(hitstopDuration);
            DoCameraShake(impulseSource, shakeForce);
        }

        private IEnumerator HitstopRoutine(float duration)
        {
            float originalTimeScale = Time.timeScale;
            Time.timeScale = 0f;

            yield return new WaitForSecondsRealtime(duration);
            Time.timeScale = originalTimeScale;
            hitstopCoroutine = null;
        }
    }
}
