using UnityEngine;

namespace junklite
{
    /// <summary>
    /// A respawn point the player has to reach. Trigger box across the lane: when the
    /// player walks into it, <see cref="PlayerLifecycle"/> respawns her here (position
    /// and yaw of Spawn Point, this transform by default) until a later checkpoint is
    /// reached. Only reached checkpoints count: in a scene with any Checkpoint, dying
    /// before the first one respawns at the primary spawn, never at the nearest marker.
    /// Restart Level forgets it. Lives under [Gameplay]; the yaw must match the lane
    /// (0 = lane along +X, 270 = lane along +Z, as LanePath.YRotationFor).
    /// </summary>
    [RequireComponent(typeof(Collider))]
    public sealed class Checkpoint : MonoBehaviour
    {
        [Tooltip("Where to respawn. Empty = this transform. Its yaw must match the lane.")]
        [SerializeField] private Transform spawnPoint;
        [Tooltip("Optional object switched on once this checkpoint has been reached (a light, a glow).")]
        [SerializeField] private GameObject reachedVisual;

        private bool reached;

        public Transform SpawnTransform => spawnPoint != null ? spawnPoint : transform;
        public bool Reached => reached;

        private void Awake()
        {
            foreach (Collider volume in GetComponents<Collider>())
                volume.isTrigger = true;

            if (reachedVisual != null)
                reachedVisual.SetActive(false);
        }

        private void OnEnable()
        {
            PlayerLifecycle.Instance?.RegisterCheckpoint(this);
        }

        private void OnDisable()
        {
            PlayerLifecycle.Instance?.UnregisterCheckpoint(this);
        }

        private void OnTriggerEnter(Collider other)
        {
            if (!other.CompareTag("Player"))
                return;

            PlayerCharacter player = other.GetComponentInParent<PlayerCharacter>();
            if (player == null || !player.IsAlive)
                return;

            Activate();
        }

        /// <summary>Makes this the respawn point. Reaching it again is harmless.</summary>
        public void Activate()
        {
            if (!reached)
            {
                reached = true;
                if (reachedVisual != null)
                    reachedVisual.SetActive(true);
            }

            PlayerLifecycle.Instance?.SetCheckpoint(this);
        }

#if UNITY_EDITOR
        private void OnDrawGizmos()
        {
            Gizmos.color = reached ? new Color(0.2f, 1f, 0.4f, 0.9f) : new Color(0.2f, 0.7f, 1f, 0.9f);
            Transform spawn = SpawnTransform;
            Gizmos.DrawWireSphere(spawn.position, 0.5f);
            Gizmos.DrawLine(spawn.position, spawn.position + spawn.right * 1.5f);

            if (TryGetComponent(out BoxCollider box))
            {
                Gizmos.matrix = transform.localToWorldMatrix;
                Gizmos.color = new Color(0.2f, 0.7f, 1f, 0.25f);
                Gizmos.DrawCube(box.center, box.size);
                Gizmos.matrix = Matrix4x4.identity;
            }
        }
#endif
    }
}
