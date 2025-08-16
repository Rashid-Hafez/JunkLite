using Gentleland.StemapunkUI.DemoAndExample;
using UnityEngine;
using UnityEngine.Events;

namespace junklite
{
    [RequireComponent(typeof(HealthComponent))]
    public abstract class CharacterBase : MonoBehaviour, IDamageable
    {
        [SerializeField] CharacterStats stats;       
        protected CharacterStats runtimeStats;        

        protected HealthComponent healthComponent;
        protected Character2D5Controller controller;

        public bool IsAlive => healthComponent.IsAlive;
        public CharacterStats Stats => runtimeStats;
        public Character2D5Controller Controller => controller;
        public HealthComponent Health => healthComponent;

        protected virtual void Awake()
        {
            // fetch components
            controller = GetComponent<Character2D5Controller>();
            healthComponent = GetComponent<HealthComponent>();

            
            runtimeStats = Instantiate(stats);
            healthComponent.Initialize(runtimeStats);

            
            healthComponent.OnDeath += HandleDeath;
        }

        /// <summary>Forward to HealthComponent.</summary>
        public virtual void TakeDamage(DamageInfo info)
            => healthComponent.TakeDamage(info);

        /// <summary>Override in subclasses for death behavior (VFX, Destroy, loot…)</summary>
        protected virtual void HandleDeath()
        {
            // e.g. Play animation, disable AI, Destroy(gameObject)…
        }
    }



    public interface IDamageable
    {
        /// <summary>Apply damage described by DamageInfo.</summary>
        void TakeDamage(DamageInfo info);

        /// <summary>True if current health > 0.</summary>
        bool IsAlive { get; }
    }



    public enum DamageType
    {
        Physical,
        Fire,
        Magic
    }

    public struct DamageInfo
    {
        public float Amount;
        public GameObject Source;
        public DamageType Type;

        public DamageInfo(float amount, GameObject source = null, DamageType type = DamageType.Physical)
        {
            Amount = amount;
            Source = source;
            Type = type;
        }
    }
}



