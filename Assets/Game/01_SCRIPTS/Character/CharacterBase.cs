using UnityEngine;

namespace junklite
{
    [RequireComponent(typeof(CharacterSystem))]
    public abstract class CharacterBase : MonoBehaviour, IDamageable
    {
        [SerializeField] protected CharacterStats baseStats;

        protected CharacterSystem characterSystem;
        protected Character2D5Controller controller;

        // Properties - Simple delegation to CharacterSystem only
        public bool IsAlive => characterSystem.IsAlive;
        public CharacterStats Stats => characterSystem.Stats;
        public CharacterSystem System => characterSystem;
        public Character2D5Controller Controller => controller;

        protected virtual void Awake()
        {
            // Get components
            characterSystem = GetComponent<CharacterSystem>();
            controller = GetComponent<Character2D5Controller>();

            // Initialize character system with stats
            characterSystem.Initialize(baseStats);

            // Connect events - ONE WAY FLOW
            characterSystem.OnDeath += HandleDeath;

            // Connect controller to character system
            ConnectController();

            // Apply initial stats
            UpdateControllerStats();
        }

        protected virtual void OnDestroy()
        {
            if (characterSystem != null)
            {
                characterSystem.OnDeath -= HandleDeath;
            }
        }

        /// <summary>Connect controller events to character system</summary>
        private void ConnectController()
        {
            if (controller == null || characterSystem == null) return;

            // Connect controller events to character system
            controller.OnGroundedStateChanged += characterSystem.SetGrounded;
            controller.OnDashStarted += () => characterSystem.SetDashing(true);
            controller.OnDashEnded += () => characterSystem.SetDashing(false);
            controller.OnMovementChanged += (movement) =>
                characterSystem.SetMoving(movement.magnitude > 0.1f);
        }

        /// <summary>SINGLE damage entry point - handles all damage logic and effects</summary>
        public virtual void TakeDamage(DamageInfo info)
        {
            // Check if character can take damage
            if (!characterSystem.CanTakeDamage) return;

            // Apply knockback BEFORE damage (in case damage kills the character)
            ApplyKnockback(info);

            // Apply damage to health through CharacterSystem
            characterSystem.ApplyDamageToHealth(info);

            // Apply any damage effects (hit stun, particles, etc.)
            ApplyDamageEffects(info);
        }

        /// <summary>Override this for custom knockback behavior</summary>
        protected virtual void ApplyKnockback(DamageInfo info)
        {
            if (info.Source != null && controller != null)
            {
                Vector3 knockbackDirection = (transform.position - info.Source.transform.position).normalized;
                controller.AddForce(knockbackDirection * 15f, ForceMode.Impulse);
            }
        }

        /// <summary>Override this for custom damage effects (hit stun, particles, etc.)</summary>
        protected virtual void ApplyDamageEffects(DamageInfo info)
        {
            // Apply hit stun using the character system
            characterSystem.ApplyStun(0.1f);
        }

        /// <summary>Simple healing delegation</summary>
        public virtual void Heal(float amount)
        {
            characterSystem.Heal(amount);
        }

        /// <summary>Instantly kill this character, bypassing all defenses</summary>
        public virtual void InstantDeath()
        {
            if (!IsAlive) return;

            var health = characterSystem.Health;
            if (health != null)
            {
                // Remove all remaining health to trigger death
                health.Remove(health.Current);
                Debug.Log($"{gameObject.name} died instantly!");
            }
        }

        /// <summary>Apply stats to controller</summary>
        protected virtual void UpdateControllerStats()
        {
            if (controller == null || baseStats == null) return;

            controller.MoveSpeed = baseStats.moveSpeed;

            // Use reflection safely for optional properties
            SetControllerProperty("JumpForce", baseStats.jumpForce);
            SetControllerProperty("DashForce", baseStats.dashForce);
            SetControllerProperty("DashDuration", baseStats.dashDuration);
        }

        /// <summary>Helper to set controller properties safely</summary>
        private void SetControllerProperty(string propertyName, object value)
        {
            var property = controller.GetType().GetProperty(propertyName);
            if (property != null && property.CanWrite)
            {
                property.SetValue(controller, value);
            }
        }

        /// <summary>Override for custom death behavior</summary>
        protected virtual void HandleDeath()
        {
            // Disable controller movement
            if (controller != null)
            {
                controller.CanMove = false;
            }

            Debug.Log($"{gameObject.name} has died!");
        }

        // Convenience methods for easy attribute access - all delegate to CharacterSystem
        public Attribute GetAttribute(string name) => characterSystem.GetAttribute(name);
        public Attribute Health => characterSystem.Health;
        public Attribute Mana => characterSystem.Mana;
        public Attribute Stamina => characterSystem.Stamina;
    }

    // Keep existing interfaces
    public interface IDamageable
    {
        void TakeDamage(DamageInfo info);
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