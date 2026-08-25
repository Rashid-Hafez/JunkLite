using UnityEngine;

namespace junklite
{
    /// <summary>
    /// Minimal enemy-specific lifecycle and damage boundary. Enemy AI and state
    /// decisions remain in EnemyCharacter and its FSM components.
    /// </summary>
    [RequireComponent(typeof(AttributeManager))]
    [RequireComponent(typeof(Damageable))]
    public abstract class EnemyBase : MonoBehaviour, IDamageReceiver, IDamageable
    {
        [Header("Config")]
        [SerializeField] protected CharacterStats baseStats;

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
            return damageable != null
                ? damageable.ReceiveDamage(request)
                : DamageResult.Rejected(DamageOutcome.Invalid, request.Amount);
        }

        /// <summary>Legacy adapter retained until all enemy damage producers migrate.</summary>
        public virtual bool TakeDamage(DamageInfo info)
        {
            return ReceiveDamage(DamageRequest.FromLegacy(info)).WasApplied;
        }

        public void Heal(float amount)
        {
            attributes?.Heal(amount);
        }

        protected void InstantDeath()
        {
            if (!IsAlive || attributes?.Health == null) return;

            ReceiveDamage(DamageRequest.Forced(attributes.Health.Current));
            Debug.Log($"{gameObject.name} died instantly!");
        }

        public Attribute GetAttribute(AttributeType type) => attributes ? attributes.Get(type) : null;

        protected virtual void HandleDeath()
        {
            Debug.Log($"{gameObject.name} has died!");
        }

        public virtual void Activate() { }

        public virtual void Deactivate() { }
    }
}
