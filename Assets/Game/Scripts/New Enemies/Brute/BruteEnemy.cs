using System.Collections;
using UnityEngine;

namespace junklite
{
    /// <summary>
    /// Brute boss identity. Decisions live in the required BruteBrain. This class
    /// owns the boss-only damage rules: armor while charging (hits are blocked
    /// with clank feedback) and the long stun when the charged dash is parried.
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

        public override DamageResult ReceiveDamage(DamageRequest request)
        {
            if (IsArmored && !request.BypassesDefenses)
            {
                PlayArmorBlockFeedback(request);
                return DamageResult.Rejected(DamageOutcome.Blocked, request.Amount);
            }

            return base.ReceiveDamage(request);
        }

        public override void OnParryStunned(float duration)
        {
            if (StateMachine != null && StateMachine.CurrentState is DashState)
                duration = Mathf.Max(duration, parriedDashStunDuration);

            base.OnParryStunned(duration);
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
