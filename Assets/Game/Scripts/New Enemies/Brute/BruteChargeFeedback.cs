using System.Collections;
using UnityEngine;

namespace junklite
{
    /// <summary>
    /// Brute charge presentation (Hollow Knight style): glowing particles are
    /// pulled into his body faster and faster, the camera shake ramps up, and he
    /// flashes white the moment the charge releases into the dash. Presentation
    /// only; ChargeState still owns timing and toggles the charge VFX object.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class BruteChargeFeedback : MonoBehaviour
    {
        [Header("Absorb Particles")]
        [Tooltip("Particles emitted on a shell around the body, moving inward.")]
        [SerializeField] private ParticleSystem absorbParticles;
        [SerializeField] private float startEmissionRate = 25f;
        [SerializeField] private float endEmissionRate = 140f;
        [Tooltip("Simulation speed ramps from 1 to this, so particles get sucked in faster.")]
        [SerializeField] private float endSimulationSpeed = 3.5f;

        [Header("Core Glow")]
        [Tooltip("Optional glow at the body that swells as energy is absorbed.")]
        [SerializeField] private ParticleSystem coreGlow;
        [SerializeField] private float coreStartScale = 0.4f;
        [SerializeField] private float coreEndScale = 1.4f;

        [Header("Camera Shake")]
        [SerializeField] private float shakeInterval = 0.08f;
        [SerializeField] private float startShakeForce = 0.05f;
        [SerializeField] private float endShakeForce = 0.35f;

        [Header("Release (into the dash)")]
        [SerializeField] private DamageFlashUniversal flash;
        [SerializeField] private Color releaseFlashColor = Color.white;
        [Tooltip("0 = fully white.")]
        [SerializeField, Range(0f, 1f)] private float releaseFlashAmount = 0f;
        [SerializeField] private float releaseFlashDuration = 0.15f;
        [SerializeField] private float releaseShakeForce = 0.9f;

        private StateMachine stateMachine;
        private EnemyCharacter enemy;
        private Coroutine rampRoutine;
        private Vector3 coreBaseScale = Vector3.one;

        private void Awake()
        {
            stateMachine = GetComponent<StateMachine>();
            enemy = GetComponent<EnemyCharacter>();
            if (flash == null)
                flash = GetComponent<DamageFlashUniversal>();
            if (coreGlow != null)
                coreBaseScale = coreGlow.transform.localScale;
        }

        private void OnEnable()
        {
            if (stateMachine != null)
                stateMachine.OnStateChanged += HandleStateChanged;
        }

        private void OnDisable()
        {
            if (stateMachine != null)
                stateMachine.OnStateChanged -= HandleStateChanged;
            StopRamp();
        }

        private void HandleStateChanged(IState from, IState to)
        {
            if (to is ChargeState)
            {
                StopRamp();
                float chargeTime = enemy != null ? enemy.GetCapability<ICharger>()?.ChargeTime ?? 1f : 1f;
                rampRoutine = StartCoroutine(Ramp(Mathf.Max(0.1f, chargeTime)));
                return;
            }

            if (from is ChargeState)
            {
                StopRamp();
                if (to is DashState)
                    PlayRelease();
            }
        }

        private IEnumerator Ramp(float duration)
        {
            float elapsed = 0f;
            float nextShake = 0f;

            while (elapsed < duration)
            {
                // Ease-in: slow pull at first, violent at the end.
                float t = Mathf.Clamp01(elapsed / duration);
                float eased = t * t;

                if (absorbParticles != null)
                {
                    var main = absorbParticles.main;
                    main.simulationSpeed = Mathf.Lerp(1f, endSimulationSpeed, eased);
                    var emission = absorbParticles.emission;
                    emission.rateOverTime = Mathf.Lerp(startEmissionRate, endEmissionRate, eased);
                }

                if (coreGlow != null)
                    coreGlow.transform.localScale = coreBaseScale * Mathf.Lerp(coreStartScale, coreEndScale, eased);

                if (elapsed >= nextShake)
                {
                    FeedbackManager.Instance?.DoCameraShake(Mathf.Lerp(startShakeForce, endShakeForce, eased));
                    nextShake = elapsed + shakeInterval;
                }

                elapsed += Time.deltaTime;
                yield return null;
            }

            rampRoutine = null;
        }

        private void PlayRelease()
        {
            if (flash != null)
                flash.Flash(releaseFlashColor, releaseFlashAmount, releaseFlashDuration);

            FeedbackManager.Instance?.DoCameraShake(releaseShakeForce);
        }

        private void StopRamp()
        {
            if (rampRoutine != null)
            {
                StopCoroutine(rampRoutine);
                rampRoutine = null;
            }

            if (absorbParticles != null)
            {
                var main = absorbParticles.main;
                main.simulationSpeed = 1f;
            }

            if (coreGlow != null)
                coreGlow.transform.localScale = coreBaseScale;
        }
    }
}
