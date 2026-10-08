using UnityEngine;
using UnityEngine.Rendering;

namespace junklite
{
    /// <summary>
    /// Short screen glitch when the player toggles mod/combat mode.
    /// Owns a runtime global Volume with its own profile and animates only its weight,
    /// so shared Volume Profile assets (e.g. the Level 0 end glitch) are never modified.
    /// Add to the Player prefab next to WeaponManager.
    /// </summary>
    public class CombatModeScreenGlitch : MonoBehaviour
    {
        // CinemachineVolumeSettings volumes sit just under 1000 and override glitch values to 0,
        // so this volume must outrank them or the glitch never reaches the screen.
        private const float VolumePriority = 10000f;

        [Header("Triggers")]
        [SerializeField] private bool playOnEnter = true;
        [SerializeField] private bool playOnExit = true;

        [Header("Timing")]
        [Tooltip("Total glitch length in seconds (unscaled, so hit-stop does not stretch it).")]
        [SerializeField, Range(0.05f, 0.5f)] private float duration = 0.3f;
        [Tooltip("Volume weight over normalized time (0..1).")]
        [SerializeField] private AnimationCurve weightCurve = new AnimationCurve(
            new Keyframe(0f, 0f), new Keyframe(0.15f, 1f), new Keyframe(1f, 0f));

        [Header("Peak strength (at weight 1)")]
        [SerializeField, Range(0f, 1f)] private float scanLineJitter = 0.35f;
        [SerializeField, Range(0f, 1f)] private float verticalJump = 0f;
        [SerializeField, Range(0f, 1f)] private float horizontalShake = 0.12f;
        [SerializeField, Range(0f, 1f)] private float colorDrift = 0.3f;
        [SerializeField, Range(0f, 1f)] private float digitalIntensity = 0.2f;

        [Header("Volume")]
        [Tooltip("Layer for the runtime volume. Must be in the camera's Volume Mask (Default works for all current cameras).")]
        [SerializeField] private int volumeLayer = 0;

        private WeaponManager weaponManager;
        private Volume volume;
        private VolumeProfile profile;
        private URPGlitch.AnalogGlitchVolume analog;
        private URPGlitch.DigitalGlitchVolume digital;
        private float elapsed = -1f;

        private void Awake()
        {
            weaponManager = GetComponent<WeaponManager>();
            if (weaponManager == null)
                Debug.LogWarning($"[{nameof(CombatModeScreenGlitch)}] No WeaponManager on {name}; glitch will never play.", this);

            CreateVolume();
        }

        private void OnEnable()
        {
            if (weaponManager == null) return;
            weaponManager.OnCombatModeChanged += HandleCombatModeChanged;
        }

        private void OnDisable()
        {
            if (weaponManager != null)
                weaponManager.OnCombatModeChanged -= HandleCombatModeChanged;
            Stop();
        }

        private void OnDestroy()
        {
            if (volume != null) Destroy(volume.gameObject);
            if (profile != null) Destroy(profile);
        }

        private void OnValidate()
        {
            if (profile != null) ApplyPeaks();
        }

        private void Update()
        {
            if (elapsed < 0f) return;

            elapsed += Time.unscaledDeltaTime;
            float t = elapsed / Mathf.Max(0.01f, duration);
            if (t >= 1f)
            {
                Stop();
                return;
            }

            volume.weight = Mathf.Clamp01(weightCurve.Evaluate(t));
        }

        /// <summary>Plays the glitch once, restarting it if already playing.</summary>
        public void Play()
        {
            if (volume == null) return;
            elapsed = 0f;
            volume.enabled = true;
            volume.weight = Mathf.Clamp01(weightCurve.Evaluate(0f));
        }

        private void Stop()
        {
            elapsed = -1f;
            if (volume == null) return;
            volume.weight = 0f;
            volume.enabled = false;
        }

        private void HandleCombatModeChanged()
        {
            if (weaponManager.IsModCombat ? playOnEnter : playOnExit)
                Play();
        }

        private void CreateVolume()
        {
            profile = ScriptableObject.CreateInstance<VolumeProfile>();
            profile.name = "CombatModeGlitch (runtime)";
            analog = profile.Add<URPGlitch.AnalogGlitchVolume>(overrides: true);
            digital = profile.Add<URPGlitch.DigitalGlitchVolume>(overrides: true);
            ApplyPeaks();

            var go = new GameObject("CombatModeGlitchVolume") { layer = volumeLayer };
            go.transform.SetParent(transform, false);

            volume = go.AddComponent<Volume>();
            volume.isGlobal = true;
            volume.priority = VolumePriority;
            volume.sharedProfile = profile;
            volume.weight = 0f;
            volume.enabled = false;
        }

        private void ApplyPeaks()
        {
            analog.scanLineJitter.value = scanLineJitter;
            analog.verticalJump.value = verticalJump;
            analog.horizontalShake.value = horizontalShake;
            analog.colorDrift.value = colorDrift;
            digital.intensity.value = digitalIntensity;
        }
    }
}
