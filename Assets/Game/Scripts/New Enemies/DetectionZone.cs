using UnityEngine;

namespace junklite
{
    [RequireComponent(typeof(Collider))]
    public class DetectionZone : MonoBehaviour
    {
        [Header("Detection")]
        [SerializeField] private LayerMask targetLayers = ~0;
        [SerializeField] private bool requireLineOfSight = false;
        [SerializeField] private LayerMask losBlockingLayers;

        [Header("Reachability")]
        [Tooltip("If true, enemy won't engage if there is a floor between it and the player")]
        [SerializeField] private bool requireReachablePath = true;
        [SerializeField] private LayerMask platformLayers;
        [Tooltip("Only applies reachability check if enemy is this many units above the player")]
        [SerializeField] private float minVerticalDifferenceToCheck = 0.5f;

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

            int playerLayer = LayerMask.NameToLayer("Player");
            if (playerLayer >= 0)
                targetLayers |= 1 << playerLayer;
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

            if (player == null || !player.IsAlive) return;
            if (requireLineOfSight && !HasLineOfSight(player.transform)) return;
            if (requireReachablePath && !IsTargetReachable(player.transform)) return;

            SetTarget(player);
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
            if (!HasTarget)
            {
                detectedPlayer = null;
                detectedTarget = null;

                if ((targetLayers & (1 << other.gameObject.layer)) == 0) return;

                var player = other.GetComponent<PlayerCharacter>()
                          ?? other.GetComponentInParent<PlayerCharacter>();

                if (player == null || !player.IsAlive) return;
                if (requireLineOfSight && !HasLineOfSight(player.transform)) return;
                if (requireReachablePath && !IsTargetReachable(player.transform)) return;

                SetTarget(player);
                return;
            }

            if (Time.frameCount % 10 != 0) return;

            // Drop target if they're no longer reachable (e.g. player dropped off a ledge)
            if (requireReachablePath && !IsTargetReachable(detectedTarget))
            {
                ClearTarget();
                return;
            }

            if (requireLineOfSight && !HasLineOfSight(detectedTarget))
                ClearTarget();
        }

        // Casts downward from the enemy — if a floor is hit between the enemy's Y
        // and the player's Y, there's a platform between them and the enemy can't reach them.
        private bool IsTargetReachable(Transform target)
        {
            float enemyY = transform.position.y;
            float targetY = target.position.y;
            float verticalDiff = enemyY - targetY;

            // Player is at same level or above — always reachable
            if (verticalDiff < minVerticalDifferenceToCheck)
                return true;

            // Cast downward from enemy; check if anything solid sits between the two Y positions
            if (Physics.Raycast(
                    transform.position,
                    Vector3.down,
                    out RaycastHit hit,
                    verticalDiff,
                    platformLayers,
                    QueryTriggerInteraction.Ignore))
            {
                // Hit something between enemy and player — floor is blocking the path
                return false;
            }

            return true;
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
                Gizmos.DrawWireSphere(transform.position + sphere.center, sphere.radius);
            else if (col is BoxCollider box)
            {
                Gizmos.matrix = transform.localToWorldMatrix;
                Gizmos.DrawWireCube(box.center, box.size);
            }

            // Visualize the downward reachability ray when selected
            if (requireReachablePath && detectedTarget != null)
            {
                float diff = transform.position.y - detectedTarget.position.y;
                if (diff > minVerticalDifferenceToCheck)
                {
                    Gizmos.color = Color.cyan;
                    Gizmos.DrawLine(transform.position, transform.position + Vector3.down * diff);
                }
            }
        }
#endif
    }
}