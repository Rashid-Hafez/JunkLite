using System.Collections;
using Unity.Cinemachine;
using UnityEngine;
using UnityEngine.InputSystem;

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
        [Tooltip("Scales every camera shake. 0 = off (comfort option).")]
        [SerializeField, Range(0f, 1f)] private float shakeIntensity = 1f;

        [Header("Haptics")]
        [Tooltip("Scales every gamepad rumble. 0 = off.")]
        [SerializeField, Range(0f, 1f)] private float rumbleIntensity = 1f;

        public float ShakeIntensity { get => shakeIntensity; set => shakeIntensity = Mathf.Clamp01(value); }
        public float RumbleIntensity
        {
            get => rumbleIntensity;
            set
            {
                rumbleIntensity = Mathf.Clamp01(value);
                if (rumbleIntensity <= 0f) StopRumble();
            }
        }

        // Rumble is a real-time timer like the time effects: the strongest request wins
        // while one runs, and the motors always stop (timeout, pause, focus loss, disable).
        private Gamepad rumblePad;
        private float rumbleUntil = float.NegativeInfinity;
        private float rumbleLow;
        private float rumbleHigh;

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

        private void OnDisable() => StopRumble();

        private void OnApplicationFocus(bool hasFocus)
        {
            if (!hasFocus) StopRumble();
        }

        /// <summary>
        /// Short gamepad rumble (motor speeds 0..1, real seconds). Does nothing on keyboard
        /// and mouse. While one runs, the stronger motors and the later end win.
        /// </summary>
        public void DoRumble(float lowFrequency, float highFrequency, float duration)
        {
            if (duration <= 0f || rumbleIntensity <= 0f) return;
            if (IsPaused) return;

            GameInputManager input = GameInputManager.Instance;
            if (input == null || !input.IsUsingGamepad) return;

            Gamepad pad = Gamepad.current;
            if (pad == null) return;

            float now = Time.realtimeSinceStartup;
            if (rumblePad != pad) StopRumble();
            bool running = rumblePad != null && now < rumbleUntil;

            float low = Mathf.Clamp01(lowFrequency) * rumbleIntensity;
            float high = Mathf.Clamp01(highFrequency) * rumbleIntensity;
            rumbleLow = running ? Mathf.Max(rumbleLow, low) : low;
            rumbleHigh = running ? Mathf.Max(rumbleHigh, high) : high;
            rumbleUntil = running ? Mathf.Max(rumbleUntil, now + duration) : now + duration;
            rumblePad = pad;
            pad.SetMotorSpeeds(rumbleLow, rumbleHigh);
        }

        private void UpdateRumble()
        {
            if (rumblePad == null) return;
            if (Time.realtimeSinceStartup >= rumbleUntil || IsPaused || !rumblePad.added)
                StopRumble();
        }

        private void StopRumble()
        {
            if (rumblePad == null) return;
            if (rumblePad.added) rumblePad.SetMotorSpeeds(0f, 0f);
            rumblePad = null;
            rumbleUntil = float.NegativeInfinity;
        }

        private static bool IsPaused =>
            GameManager.Instance != null && GameManager.Instance.CurrentState == GameManager.GameState.Paused;

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

        private void Update()
        {
            UpdateRumble();
            ApplyTimeScale();
        }

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

            impulseSource.GenerateImpulse(force * shakeIntensity);
        }

        /// <summary>
        /// Camera kick along a world direction (the swing), at the source's authored strength
        /// times <paramref name="force"/>. A zero direction falls back to the plain shake.
        /// </summary>
        public void DoCameraShake(CinemachineImpulseSource impulseSource, Vector3 direction, float force = -1f)
        {
            if (impulseSource == null)
                return;

            float strength = impulseSource.DefaultVelocity.magnitude;
            if (direction.sqrMagnitude < 0.0001f || strength <= 0f)
            {
                DoCameraShake(impulseSource, force);
                return;
            }

            if (force < 0f)
                force = defaultShakeForce;

            impulseSource.GenerateImpulseWithVelocity(direction.normalized * (strength * force * shakeIntensity));
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
