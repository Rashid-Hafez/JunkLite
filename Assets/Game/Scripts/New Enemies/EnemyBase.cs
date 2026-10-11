using UnityEngine;

namespace junklite
{
    /// <summary>
    /// Minimal enemy-specific lifecycle and damage boundary. Enemy AI and state
    /// decisions remain in EnemyCharacter and its FSM components.
    /// </summary>
    [RequireComponent(typeof(AttributeManager))]
    [RequireComponent(typeof(Damageable))]
    public abstract class EnemyBase : MonoBehaviour, IDamageReceiver
    {
        [Header("Config")]
        [SerializeField] protected CharacterStats baseStats;

        [Header("Progression Reward")]
        [Tooltip("XP for a player-attributed lethal hit. Use 0 for training targets or scripted enemies.")]
        [SerializeField, Min(0)] private int experienceReward = 25;
        public int ExperienceReward => experienceReward;

        [HideInInspector] public AttributeManager attributes;
        protected Damageable damageable;

        public bool IsAlive => attributes ? attributes.IsAlive : true;
        public CharacterStats Stats => baseStats;
        public Attribute Health => attributes ? attributes.Health : null;

        protected virtual void Awake()
        {
            attributes = GetComponent<AttributeManager>();
            damageable = GetComponent<Damageable>();

            if (attributes != null && baseStats != null)
                attributes.Initialize(baseStats);

            if (damageable != null)
                damageable.Bind(baseStats, attributes, null);

            if (attributes != null)
                attributes.OnDeath += HandleDeath;
        }

        protected virtual void Start() { }

        protected virtual void OnDestroy()
        {
            if (attributes != null)
                attributes.OnDeath -= HandleDeath;
        }

        public virtual DamageResult ReceiveDamage(DamageRequest request)
        {
            bool wasAlive = IsAlive;
            DamageResult result = damageable != null
                ? damageable.ReceiveDamage(request)
                : DamageResult.Rejected(DamageOutcome.Invalid, request.Amount);
            // Resolve after health/death callbacks, using this lethal request's source.
            // Dead receivers reject subsequent hits; despawns and forced source-less deaths never reward XP.
            if (wasAlive && !IsAlive && result.WasApplied && experienceReward > 0 && request.Source != null)
            {
                var owner = request.Source.GetComponentInParent<PlayerRuntimeStats>(true);
                owner?.Progression?.AddExperience(experienceReward);
            }
            return result;
        }

        public void Heal(float amount)
        {
            attributes?.Heal(amount);
        }

        protected void InstantDeath()
        {
            if (!IsAlive || attributes?.Health == null) return;

            ReceiveDamage(DamageRequest.Forced(attributes.Health.Current));
        }

        public Attribute GetAttribute(AttributeType type) => attributes ? attributes.Get(type) : null;

        protected virtual void HandleDeath()
        {
        }

        public virtual void Activate() { }

        public virtual void Deactivate() { }
    }
}
