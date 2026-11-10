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

        // The one owner of gameplay time scale effects. Hit-stop and slow motion are timers
        // (real time); the scale is rebuilt from them every frame, never saved and restored,
        // so overlapping effects (a parry slow-mo starting inside a hit-stop) can't leave
        // time stuck at 0. Pause (GameManager) wins while it lasts.
        private float hitstopUntil = float.NegativeInfinity;
        private float slowMoUntil = float.NegativeInfinity;
        private float slowMoScale = 1f;
        private bool ownsTimeScale;
        private float baseFixedDeltaTime;

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }

            Instance = this;
            DontDestroyOnLoad(gameObject);
            baseFixedDeltaTime = Time.fixedDeltaTime;
        }

        private void OnDestroy()
        {
            if (Instance == this)
            {
                Instance = null;
                ReleaseTimeScale();
            }
        }

        /// <summary>
        /// Triggers a brief hitstop effect by freezing time.
        /// </summary>
        public void DoHitstop(float duration = -1f)
        {
            if (duration < 0f)
                duration = defaultHitstopDuration;
            if (duration <= 0f) return;

            hitstopUntil = Mathf.Max(hitstopUntil, Time.realtimeSinceStartup + duration);
            ApplyTimeScale();
        }

        /// <summary>Slows time to <paramref name="scale"/> for <paramref name="realtimeDuration"/> real seconds. The slowest active request wins.</summary>
        public void DoSlowMotion(float scale, float realtimeDuration)
        {
            if (realtimeDuration <= 0f) return;
            float now = Time.realtimeSinceStartup;
            scale = Mathf.Clamp(scale, 0.01f, 1f);
            slowMoScale = now < slowMoUntil ? Mathf.Min(slowMoScale, scale) : scale;
            slowMoUntil = Mathf.Max(slowMoUntil, now + realtimeDuration);
            ApplyTimeScale();
        }

        public bool IsHitstopping => Time.realtimeSinceStartup < hitstopUntil;

        private void Update() => ApplyTimeScale();

        private void ApplyTimeScale()
        {
            if (GameManager.Instance != null && GameManager.Instance.CurrentState == GameManager.GameState.Paused)
                return;

            float now = Time.realtimeSinceStartup;
            bool hitstop = now < hitstopUntil;
            bool slow = now < slowMoUntil;
            if (!hitstop && !slow)
            {
                ReleaseTimeScale();
                return;
            }

            ownsTimeScale = true;
            float scale = hitstop ? 0f : slowMoScale;
            Time.timeScale = scale;
            // Physics steps shrink with slow motion so movement stays smooth; a freeze keeps the base step.
            Time.fixedDeltaTime = scale > 0f ? baseFixedDeltaTime * scale : baseFixedDeltaTime;
        }

        // Back to normal speed, only if an effect set the scale (other systems own it otherwise).
        private void ReleaseTimeScale()
        {
            if (!ownsTimeScale) return;
            ownsTimeScale = false;
            Time.timeScale = 1f;
            Time.fixedDeltaTime = baseFixedDeltaTime;
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

    }
}
