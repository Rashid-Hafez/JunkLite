using UnityEngine;

namespace junklite
{
    [RequireComponent(typeof(CharacterState))]
    [RequireComponent(typeof(AttributeManager))]
    [RequireComponent(typeof(Damageable))]
    [RequireComponent(typeof(Character2D5Controller))]
    public abstract class CharacterBase : MonoBehaviour, IDamageable
    {
        [Header("Config")]
        [SerializeField] protected CharacterStats baseStats; 

        // Shared components
        protected CharacterState state;
        protected AttributeManager attributes;
        protected Damageable damageable;
        protected Character2D5Controller controller;
        protected AnimationController animationController;

        // Public accessors
        public bool IsAlive => attributes ? attributes.IsAlive : true;
        public CharacterStats Stats => baseStats;
        public CharacterState State => state;
        public Character2D5Controller Controller => controller;

        protected virtual void Awake()
        {
            // Cache components
            state = GetComponent<CharacterState>();
            attributes = GetComponent<AttributeManager>();
            damageable = GetComponent<Damageable>();
            controller = GetComponent<Character2D5Controller>();
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
            // Pipe controller events into CharacterState flags
            ConnectController();

            // Apply movement stats to controller
            UpdateControllerStats();
        }


        protected virtual void OnDestroy()
        {
            if (attributes != null)
                attributes.OnDeath -= HandleDeath;

            if (controller != null && state != null)
            {
                controller.OnGroundedStateChanged -= state.SetGrounded;
                controller.OnDashStarted -= OnDashStarted;
                controller.OnDashEnded -= OnDashEnded;
                controller.OnMovementChanged -= OnMovementChanged; // unsub ok
            }
        }

        // --- Controller -> State wiring
        private void ConnectController()
        {
            if (controller == null || state == null) return;

            controller.OnGroundedStateChanged += state.SetGrounded;
            controller.OnDashStarted += OnDashStarted;
            controller.OnDashEnded += OnDashEnded;
            controller.OnMovementChanged += OnMovementChanged; // now matches Vector3
        }

       
        private void OnDashStarted() => state.SetDashing(true);
        private void OnDashEnded() => state.SetDashing(false);
        private void OnMovementChanged(Vector3 move)
        {
            // Use X/Z magnitude for 2.5D movement
            // 0.1f threshold => compare squared to avoid sqrt
            bool isMoving = (move.x * move.x + move.z * move.z) > 0.01f;
            state.SetMoving(isMoving);
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

        // Apply baseStats movement into controller
        protected virtual void UpdateControllerStats()
        {
            if (controller == null || baseStats == null) return;

            controller.MoveSpeed = baseStats.moveSpeed;

            // set optional fields if they exist
            SetControllerProperty("JumpForce", baseStats.jumpForce);
            SetControllerProperty("DashForce", baseStats.dashForce);
            SetControllerProperty("DashDuration", baseStats.dashDuration);
        }

        private void SetControllerProperty(string prop, object value)
        {
            var p = controller.GetType().GetProperty(prop);
            if (p != null && p.CanWrite) p.SetValue(controller, value);
        }

        // Called when attributes say we're dead
        protected virtual void HandleDeath()
        {
            if (controller != null) controller.CanMove = false;
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
