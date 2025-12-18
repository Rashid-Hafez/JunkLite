using UnityEngine;

namespace junklite
{
    /// <summary>
    /// Base class for all enemies. AI-driven character.
    /// Uses EnemyBrain for decisions, EnemyController for movement.
    /// </summary>
    [RequireComponent(typeof(EnemyController))]
    public class EnemyCharacter : CharacterBase
    {
        [Header("Enemy Config")]
        [SerializeField] private float detectionRange = 10f;
        [SerializeField] private LayerMask playerLayer;

        // Components
        protected EnemyController controller;
        protected EnemyBrain brain;
        protected EnemyAttackHandler attackHandler;

        // Target
        protected Transform target;

        // Public accessors
        public float DetectionRange => detectionRange;
        public Transform Target => target;
        public bool HasTarget => target != null;
        public EnemyController Controller => controller;
        public EnemyBrain Brain => brain;
        public EnemyAttackHandler AttackHandler => attackHandler;

        protected override void Awake()
        {
            base.Awake();

            // Cache enemy-specific components
            controller = GetComponent<EnemyController>();
            brain = GetComponent<EnemyBrain>();
            attackHandler = GetComponent<EnemyAttackHandler>();
        }

        protected override void Start()
        {
            base.Start();

            // Initialize components
            if (brain != null)
                brain.Initialize(this);

            if (attackHandler != null)
                attackHandler.Initialize(this);

            // Enable brain
            if (brain != null)
                brain.EnableBrain(true);
        }

        protected override void OnDestroy()
        {
            base.OnDestroy();
        }

        /// <summary>
        /// Set the target for this enemy (usually the player).
        /// </summary>
        public void SetTarget(Transform newTarget)
        {
            target = newTarget;
        }

        /// <summary>
        /// Clear the current target.
        /// </summary>
        public void ClearTarget()
        {
            target = null;
        }

        /// <summary>
        /// Check if target is within detection range.
        /// </summary>
        public bool IsTargetInRange()
        {
            if (target == null) return false;
            float distance = Vector3.Distance(transform.position, target.position);
            return distance <= detectionRange;
        }

        /// <summary>
        /// Get distance to current target.
        /// </summary>
        public float GetDistanceToTarget()
        {
            if (target == null) return float.MaxValue;
            return Vector3.Distance(transform.position, target.position);
        }

        /// <summary>
        /// Get direction to target (normalized).
        /// </summary>
        public Vector3 GetDirectionToTarget()
        {
            if (target == null) return Vector3.zero;
            return (target.position - transform.position).normalized;
        }

        /// <summary>
        /// Get direction sign to target. 1 = right, -1 = left, 0 = no target
        /// </summary>
        public int GetDirectionSignToTarget()
        {
            if (target == null) return 0;
            return target.position.x > transform.position.x ? 1 : -1;
        }

        public override void TakeDamage(DamageInfo info)
        {
            if (state != null && !state.CanTakeDamage) return;

            base.TakeDamage(info);

            // Notify brain that we got hit
            if (brain != null)
                brain.OnDamaged(info);
        }

        protected override void HandleDeath()
        {
            base.HandleDeath();

            // Disable brain
            if (brain != null)
                brain.EnableBrain(false);

            // Stop movement
            if (controller != null)
                controller.CanMove = false;

            // Future: drop loot, play death animation, return to pool
            // For now, just disable
            gameObject.SetActive(false);
        }

        public override void Activate()
        {
            gameObject.SetActive(true);

            if (controller != null)
                controller.CanMove = true;

            if (brain != null)
                brain.EnableBrain(true);
        }

        public override void Deactivate()
        {
            ClearTarget();

            if (brain != null)
                brain.EnableBrain(false);

            if (controller != null)
                controller.CanMove = false;

            gameObject.SetActive(false);
        }

        private void OnDrawGizmosSelected()
        {
            // Detection range
            Gizmos.color = Color.yellow;
            Gizmos.DrawWireSphere(transform.position, detectionRange);
        }
    }
}