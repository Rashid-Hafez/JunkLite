using System.Collections;
using UnityEngine;

namespace junklite
{
    /// <summary>
    /// Brute boss identity. Decisions live in the required BruteBrain. This class
    /// owns the boss-only damage rules: armor while charging (hits are blocked
    /// with clank feedback), the long stun when the charged dash is parried, and
    /// the knockdown: every knockdownDamage he takes outside a knockdown puts him on
    /// the floor, where hits deal extra damage and don't count toward the next one.
    /// </summary>
    [RequireComponent(typeof(BruteBrain))]
    public sealed class BruteEnemy : EnemyCharacter
    {
        [Header("Brute - Armor (Charge)")]
        [Tooltip("Camera shake force when a hit is blocked by the charge armor.")]
        [SerializeField] private float armorHitShakeForce = 0.6f;
        [Tooltip("Visual root that jitters when the armor blocks a hit (usually the Spine child).")]
        [SerializeField] private Transform armorShakeRoot;
        [SerializeField] private float armorShakeDuration = 0.12f;
        [SerializeField] private float armorShakeAmplitude = 0.08f;
        [Tooltip("Height above the pivot where the armor spark spawns.")]
        [SerializeField] private float armorSparkHeight = 1.5f;

        [Header("Brute - Parried Dash")]
        [Tooltip("Stun duration when the player parries the charged dash (the big punish window).")]
        [SerializeField] private float parriedDashStunDuration = 2.5f;
        [Tooltip("Push applied when the dash is parried (normal parries use the ParryHandler values).")]
        [SerializeField] private float parriedDashPushForce = 28f;
        [SerializeField] private float parriedDashPushDuration = 0.6f;
        [SerializeField] private float parriedDashHitstop = 0.12f;
        [SerializeField] private float parriedDashShakeForce = 1.2f;
        [Tooltip("Dust/scrape particles at his feet, played while he slides back.")]
        [SerializeField] private ParticleSystem[] parrySlideParticles;

        [Header("Brute - Knockdown")]
        [Tooltip("Damage he takes outside a knockdown that knocks him down. 0 = never. Blocked hits and damage-over-time ticks don't count.")]
        [SerializeField, Min(0f)] private float knockdownDamage = 200f;
        [Tooltip("Seconds on the floor before he stands up.")]
        [SerializeField, Min(0f)] private float knockdownDuration = 3f;
        [Tooltip("Damage multiplier on hits while he is down.")]
        [SerializeField, Min(1f)] private float downedDamageMultiplier = 1.5f;
        [SerializeField] private float knockdownShakeForce = 1f;

        private bool heavyParried;
        // World direction back along the parried dash, taken while he is still dashing.
        private Vector3 parriedDashBackDirection;
        private bool knockedDown;
        private float knockdownMeter;

        /// <summary>Raised for every applied hit (not damage-over-time ticks).</summary>
        public event System.Action HitLanded;

        /// <summary>True from the knockdown until he leaves the stun to stand up.</summary>
        public bool IsKnockedDown => knockedDown;

        /// <summary>0-1 toward the next knockdown (for a boss bar).</summary>
        public float KnockdownProgress => knockdownDamage > 0f ? Mathf.Clamp01(knockdownMeter / knockdownDamage) : 0f;

        /// <summary>True from a parried dash until the parry stun ends.</summary>
        public bool IsHeavyParried => heavyParried;

        private Coroutine armorShakeRoutine;
        private Vector3 armorShakeRestPosition;
        private EnemyAudioHandler audioHandler;

        /// <summary>True while hits are blocked (during the charge-up).</summary>
        public bool IsArmored => IsAlive && StateMachine != null && StateMachine.CurrentState is ChargeState;

        protected override void Awake()
        {
            base.Awake();
            enemyType = EnemyType.Brute;
            audioHandler = GetComponentInChildren<EnemyAudioHandler>();
        }

        protected override void OnEnable()
        {
            base.OnEnable();
            if (StateMachine != null)
                StateMachine.OnStateChanged += HandleStateChanged;
        }

        public override DamageResult ReceiveDamage(DamageRequest request)
        {
            if (IsArmored && !request.BypassesDefenses)
            {
                PlayArmorBlockFeedback(request);
                return DamageResult.Rejected(DamageOutcome.Blocked, request.Amount);
            }

            if (knockedDown && !request.IsTickDamage)
                request = request.WithAmount(request.Amount * downedDamageMultiplier);

            DamageResult result = base.ReceiveDamage(request);
            if (!result.WasApplied || request.IsTickDamage)
                return result;

            HitLanded?.Invoke();

            if (!knockedDown && IsAlive && knockdownDamage > 0f)
            {
                knockdownMeter += result.AppliedDamage;
                if (knockdownMeter >= knockdownDamage)
                    KnockDown();
            }

            return result;
        }

        /// <summary>Forced stun for knockdownDuration; interrupts any move (a held grab is freed).</summary>
        private void KnockDown()
        {
            knockdownMeter = 0f;

            IStunnable stunnable = GetCapability<IStunnable>();
            if (StateMachine == null || stunnable == null || !StateMachine.HasState<StunnedState>())
                return;

            knockedDown = true;
            stunnable.ForcedStunDuration = knockdownDuration;
            StateMachine.RestartState<StunnedState>();
            FeedbackManager.Instance?.DoCameraShake(knockdownShakeForce);
        }

        private void HandleStateChanged(IState from, IState to)
        {
            // Standing up (or dying) ends the downed bonus.
            if (knockedDown && from is StunnedState && to is not StunnedState)
                knockedDown = false;
        }

        public override void OnParryStunned(float duration)
        {
            heavyParried = StateMachine != null && StateMachine.CurrentState is DashState;
            if (heavyParried)
            {
                duration = Mathf.Max(duration, parriedDashStunDuration);
                // Read before the parry state turns him: he faces along the dash until now.
                parriedDashBackDirection = movement != null
                    ? -movement.MovementAxis * movement.FacingDirection
                    : Vector3.zero;
            }

            base.OnParryStunned(duration);
        }

        public override void OnParryComplete()
        {
            heavyParried = false;
            base.OnParryComplete();
        }

        // Only parries move him. Normal hits never knock him back.
        protected override void ApplyKnockback(DamageRequest request) { }

        public override void ApplyParryPush(Vector3 direction, float force, float upwardForce, float duration)
        {
            if (!heavyParried)
            {
                base.ApplyParryPush(direction, force, upwardForce, duration);
                return;
            }

            // A fast dash can carry his centre past hers before the parry lands, so the
            // position-based direction would shove him behind her. Send him back the way he came.
            if (parriedDashBackDirection != Vector3.zero)
                direction = parriedDashBackDirection;

            base.ApplyParryPush(direction, parriedDashPushForce, 0f, parriedDashPushDuration);
            FeedbackManager.Instance?.DoHitstop(parriedDashHitstop);
            FeedbackManager.Instance?.DoCameraShake(parriedDashShakeForce);
            if (slideRoutine != null)
                StopCoroutine(slideRoutine);
            slideRoutine = StartCoroutine(PlaySlideParticles(parriedDashPushDuration));
        }

        private Coroutine slideRoutine;

        private IEnumerator PlaySlideParticles(float duration)
        {
            if (parrySlideParticles == null)
                yield break;

            foreach (var ps in parrySlideParticles)
                if (ps != null) ps.Play();

            yield return new WaitForSeconds(duration);

            foreach (var ps in parrySlideParticles)
                if (ps != null) ps.Stop(true, ParticleSystemStopBehavior.StopEmitting);
            slideRoutine = null;
        }

        private void PlayArmorBlockFeedback(DamageRequest request)
        {
            // Ticks (burn, shock) are blocked silently so status effects don't spam clanks.
            if (request.IsTickDamage)
                return;

            Vector3 sparkPosition = transform.position + Vector3.up * armorSparkHeight;
            Vector3 attackDirection = request.Source != null
                ? (transform.position - request.Source.transform.position).normalized
                : -transform.right;

            CombatEffectsManager.Instance?.SpawnEnvHitParticle(sparkPosition, attackDirection);
            FeedbackManager.Instance?.DoCameraShake(armorHitShakeForce);
            audioHandler?.PlayArmorClank();

            if (damageFlashUniversal != null)
                damageFlashUniversal.Flash();

            if (armorShakeRoot != null)
            {
                if (armorShakeRoutine != null)
                    StopCoroutine(armorShakeRoutine);
                else
                    armorShakeRestPosition = armorShakeRoot.localPosition;

                armorShakeRoutine = StartCoroutine(ArmorShake());
            }
        }

        private IEnumerator ArmorShake()
        {
            float elapsed = 0f;
            while (elapsed < armorShakeDuration)
            {
                elapsed += Time.deltaTime;
                float falloff = 1f - Mathf.Clamp01(elapsed / armorShakeDuration);
                Vector2 jitter = Random.insideUnitCircle * armorShakeAmplitude * falloff;
                armorShakeRoot.localPosition = armorShakeRestPosition + new Vector3(jitter.x, jitter.y, 0f);
                yield return null;
            }

            armorShakeRoot.localPosition = armorShakeRestPosition;
            armorShakeRoutine = null;
        }

        protected override void OnDisable()
        {
            if (StateMachine != null)
                StateMachine.OnStateChanged -= HandleStateChanged;
            knockedDown = false;

            if (armorShakeRoutine != null)
            {
                StopCoroutine(armorShakeRoutine);
                armorShakeRoutine = null;
                if (armorShakeRoot != null)
                    armorShakeRoot.localPosition = armorShakeRestPosition;
            }

            base.OnDisable();
        }
    }
}
