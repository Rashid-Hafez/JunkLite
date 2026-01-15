using UnityEngine;

namespace junklite
{
    /// <summary>
    /// Trigger-based detection zone for enemies.
    /// Attach to a child GameObject with a trigger collider (sphere recommended).
    /// Fires events when targets enter/exit, no per-frame scanning needed.
    /// 
    /// Note: Events are suppressed while owner.IsInCombat to prevent
    /// interrupting combat sequences (charge → dash → grab → recovery).
    /// </summary>
    [RequireComponent(typeof(Collider))]
    public class DetectionZone : MonoBehaviour
    {
        [Header("Detection Settings")]
        [SerializeField] private LayerMask targetLayers = ~0;
        [SerializeField] private bool requireLineOfSight = false;
        [SerializeField] private LayerMask losBlockingLayers;

        [Header("Debug")]
        [SerializeField] private bool showGizmos = true;

        // Current detected target
        private Transform detectedTarget;
        private PlayerCharacter detectedPlayer;

        // Owner enemy
        private EnemyCharacter owner;

        // Events
        public event System.Action<PlayerCharacter> OnTargetEnter;
        public event System.Action<PlayerCharacter> OnTargetExit;

        // Public accessors
        public bool HasTarget => detectedPlayer != null && detectedPlayer.IsAlive;
        public Transform Target => detectedTarget;
        public PlayerCharacter TargetPlayer => detectedPlayer;

        // Radius management
        private SphereCollider sphereCollider;
        private float originalRadius;

        private void Awake()
        {
            // Ensure collider is a trigger
            var col = GetComponent<Collider>();
            if (col != null)
                col.isTrigger = true;

            // Cache sphere collider for radius resizing
            sphereCollider = col as SphereCollider;
            if (sphereCollider != null)
                originalRadius = sphereCollider.radius;

            // Find owner
            owner = GetComponentInParent<EnemyCharacter>();
        }

        #region Radius Management

        /// <summary>
        /// Expand detection radius (e.g., for pursuit mode).
        /// </summary>
        public void SetRadius(float radius)
        {
            if (sphereCollider != null)
                sphereCollider.radius = radius;
        }

        /// <summary>
        /// Reset to original radius (e.g., when exiting combat).
        /// </summary>
        public void ResetRadius()
        {
            if (sphereCollider != null && originalRadius > 0f)
                sphereCollider.radius = originalRadius;
        }

        /// <summary>
        /// Get the original radius.
        /// </summary>
        public float OriginalRadius => originalRadius;

        #endregion

        private void OnTriggerEnter(Collider other)
        {
            // Check layer mask
            if ((targetLayers & (1 << other.gameObject.layer)) == 0)
                return;

            // Already have a target
            if (HasTarget)
                return;

            // Try to find player
            var player = other.GetComponent<PlayerCharacter>();
            if (player == null)
                player = other.GetComponentInParent<PlayerCharacter>();

            if (player != null && player.IsAlive)
            {
                // Optional LOS check
                if (requireLineOfSight && !HasLineOfSight(player.transform))
                    return;

                SetTarget(player);
            }
        }

        private void OnTriggerExit(Collider other)
        {
            if (detectedTarget == null)
                return;

            // Check if the exiting object is our target
            var player = other.GetComponent<PlayerCharacter>();
            if (player == null)
                player = other.GetComponentInParent<PlayerCharacter>();

            if (player != null && player == detectedPlayer)
            {
                ClearTarget();
            }
        }

        private void OnTriggerStay(Collider other)
        {
            // If we require LOS, check it periodically
            if (!requireLineOfSight || !HasTarget)
                return;

            if (!HasLineOfSight(detectedTarget))
            {
                ClearTarget();
            }
        }

        private bool HasLineOfSight(Transform target)
        {
            Vector3 origin = transform.position;
            Vector3 direction = (target.position - origin).normalized;
            float distance = Vector3.Distance(origin, target.position);

            if (Physics.Raycast(origin, direction, out RaycastHit hit, distance, losBlockingLayers))
            {
                // Something is blocking LOS
                return hit.transform == target || hit.transform.IsChildOf(target);
            }

            return true;
        }

        private void SetTarget(PlayerCharacter player)
        {
            detectedPlayer = player;
            detectedTarget = player.transform;

            // Notify owner (always update target reference)
            if (owner != null)
                owner.SetTarget(player);

            // Only fire event if owner is NOT in combat
            // This prevents interrupting combat sequences
            if (owner == null || !owner.IsInCombat)
            {
                OnTargetEnter?.Invoke(player);
                // Debug.Log($"[DetectionZone] {owner?.name} detected {player.name}");
            }
        }

        private void ClearTarget()
        {
            var previousTarget = detectedPlayer;

            detectedPlayer = null;
            detectedTarget = null;

            // Notify owner (always update target reference)
            if (owner != null)
                owner.ClearTarget();

            // Only fire event if owner is NOT in combat
            // This prevents interrupting combat sequences
            if (owner == null || !owner.IsInCombat)
            {
                if (previousTarget != null)
                    OnTargetExit?.Invoke(previousTarget);
                Debug.Log($"[DetectionZone] {owner?.name} lost target");
            }
        }

        /// <summary>
        /// Manually clear the current target.
        /// </summary>
        public void ForceTargetClear()
        {
            ClearTarget();
        }

        private void OnDrawGizmos()
        {
            if (!showGizmos) return;

            var col = GetComponent<Collider>();
            if (col == null) return;

            Gizmos.color = HasTarget ? new Color(1f, 0.5f, 0f, 0.3f) : new Color(1f, 1f, 0f, 0.2f);

            if (col is SphereCollider sphere)
            {
                Gizmos.DrawSphere(transform.position + sphere.center, sphere.radius);
                Gizmos.DrawWireSphere(transform.position + sphere.center, sphere.radius);
            }
            else if (col is BoxCollider box)
            {
                Gizmos.matrix = transform.localToWorldMatrix;
                Gizmos.DrawCube(box.center, box.size);
                Gizmos.DrawWireCube(box.center, box.size);
            }

            // Draw LOS line to target
            if (HasTarget)
            {
                Gizmos.color = Color.red;
                Gizmos.DrawLine(transform.position, detectedTarget.position);
            }
        }
    }
}