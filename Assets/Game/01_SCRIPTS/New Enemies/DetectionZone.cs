using UnityEngine;

namespace junklite
{
    /// <summary>
    /// Trigger-based detection zone. Fires events on enter/exit, no per-frame scanning.
    /// </summary>
    [RequireComponent(typeof(Collider))]
    public class DetectionZone : MonoBehaviour
    {
        [Header("Detection")]
        [SerializeField] private LayerMask targetLayers = ~0;
        [SerializeField] private bool requireLineOfSight = false;
        [SerializeField] private LayerMask losBlockingLayers;

        [Header("Debug")]
        [SerializeField] private bool showGizmos = true;

        private Transform detectedTarget;
        private PlayerCharacter detectedPlayer;
        private EnemyCharacter owner;
        private SphereCollider sphereCollider;
        private float originalRadius;

        public event System.Action<PlayerCharacter> OnTargetEnter;
        public event System.Action<PlayerCharacter> OnTargetExit;

        public bool HasTarget => detectedPlayer != null && detectedPlayer.IsAlive;
        public Transform Target => detectedTarget;
        public float OriginalRadius => originalRadius;

        private void Awake()
        {
            var col = GetComponent<Collider>();
            col.isTrigger = true;

            sphereCollider = col as SphereCollider;
            if (sphereCollider != null)
                originalRadius = sphereCollider.radius;

            owner = GetComponentInParent<EnemyCharacter>();
        }

        public void SetRadius(float radius)
        {
            if (sphereCollider != null)
                sphereCollider.radius = radius;
        }

        public void ResetRadius()
        {
            if (sphereCollider != null)
                sphereCollider.radius = originalRadius;
        }

        private void OnTriggerEnter(Collider other)
        {
            if ((targetLayers & (1 << other.gameObject.layer)) == 0) return;
            if (HasTarget) return;

            var player = other.GetComponent<PlayerCharacter>()
                      ?? other.GetComponentInParent<PlayerCharacter>();

            if (player != null && player.IsAlive)
            {
                if (requireLineOfSight && !HasLineOfSight(player.transform))
                    return;

                SetTarget(player);
            }
        }

        private void OnTriggerExit(Collider other)
        {
            if (detectedTarget == null) return;

            var player = other.GetComponent<PlayerCharacter>()
                      ?? other.GetComponentInParent<PlayerCharacter>();

            if (player == detectedPlayer)
                ClearTarget();
        }

        private void OnTriggerStay(Collider other)
        {
            // Only check LOS if required and we have a target
            if (!requireLineOfSight || !HasTarget) return;

            // Only check periodically to save performance (every ~10 frames)
            if (Time.frameCount % 10 != 0) return;

            if (!HasLineOfSight(detectedTarget))
                ClearTarget();
        }

        private bool HasLineOfSight(Transform target)
        {
            Vector3 origin = transform.position;
            Vector3 toTarget = target.position - origin;

            if (Physics.Raycast(origin, toTarget.normalized, out RaycastHit hit, toTarget.magnitude, losBlockingLayers))
                return hit.transform == target || hit.transform.IsChildOf(target);

            return true;
        }

        private void SetTarget(PlayerCharacter player)
        {
            detectedPlayer = player;
            detectedTarget = player.transform;

            owner?.SetTarget(player);

            if (owner == null || !owner.IsInCombat)
                OnTargetEnter?.Invoke(player);
        }

        private void ClearTarget()
        {
            var prev = detectedPlayer;
            detectedPlayer = null;
            detectedTarget = null;

            owner?.ClearTarget();

            if (prev != null)
                OnTargetExit?.Invoke(prev);
        }

#if UNITY_EDITOR
        private void OnDrawGizmos()
        {
            if (!showGizmos) return;

            var col = GetComponent<Collider>();
            if (col == null) return;

            Gizmos.color = HasTarget ? new Color(1f, 0.5f, 0f, 0.3f) : new Color(1f, 1f, 0f, 0.2f);

            if (col is SphereCollider sphere)
            {
                Gizmos.DrawWireSphere(transform.position + sphere.center, sphere.radius);
            }
            else if (col is BoxCollider box)
            {
                Gizmos.matrix = transform.localToWorldMatrix;
                Gizmos.DrawWireCube(box.center, box.size);
            }
        }
#endif
    }
}