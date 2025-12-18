using UnityEngine;

namespace junklite
{
    [RequireComponent(typeof(AttributeManager))]
    [RequireComponent(typeof(Damageable))]
    public abstract class CharacterBase : MonoBehaviour, IDamageable
    {
        [Header("Config")]
        [SerializeField] protected CharacterStats baseStats;

        // Shared components
        protected CharacterState state;
        protected AttributeManager attributes;
        protected Damageable damageable;
        protected AnimationController animationController;

        // Public accessors
        public bool IsAlive => attributes ? attributes.IsAlive : true;
        public CharacterStats Stats => baseStats;
        public CharacterState State => state;

        protected virtual void Awake()
        {
            // Cache components
            state = GetComponent<CharacterState>();
            attributes = GetComponent<AttributeManager>();
            damageable = GetComponent<Damageable>();
            animationController = GetComponent<AnimationController>();

            // Build runtime attributes from the ScriptableObject ASAP
            if (attributes != null && baseStats != null)
                attributes.Initialize(baseStats);  // <-- MOVED HERE

            // Wire up damageable with its providers
            if (damageable != null)
                damageable.Bind(baseStats, attributes, state);

            // Listen for death
            if (attributes != null)
                attributes.OnDeath += HandleDeath;
        }

        protected virtual void Start()
        {
        }


        protected virtual void OnDestroy()
        {
            if (attributes != null)
                attributes.OnDeath -= HandleDeath;
        }

        // --- IDamageable implementation (single entry)
        public virtual void TakeDamage(DamageInfo info)
        {
            if (state != null && !state.CanTakeDamage) return;
            if (damageable != null)
                damageable.TakeDamage(info);
        }

        // Convenience healing (health math is in AttributeManager)
        protected void Heal(float amount)
        {
            attributes?.Heal(amount);
        }

        protected void InstantDeath()
        {
            if (!IsAlive || attributes?.Health == null) return;
            attributes.Health.Remove(attributes.Health.Current); // triggers OnDeath
            Debug.Log($"{gameObject.name} died instantly!");
        }

        // Called when attributes say we're dead
        protected virtual void HandleDeath()
        {
            Debug.Log($"{gameObject.name} has died!");
        }

        // Optional typed attribute helpers
        public Attribute GetAttribute(AttributeType type) => attributes ? attributes.Get(type) : null;
        public Attribute Health => attributes ? attributes.Health : null;


        //override methods
        public virtual void Activate()
        {

        }

        public virtual void Deactivate()
        {

        }

    }

}