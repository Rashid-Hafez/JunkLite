using UnityEngine;

namespace junklite
{
    /// <summary>
    /// Marker component to identify spawn points in a scene.
    /// Attach this to any GameObject where the player can spawn.
    /// The GameManager will automatically find all SpawnPoint components on scene load.
    /// </summary>
    public class SpawnPoint : MonoBehaviour
    {
        [Header("Spawn Point Settings")]
        [Tooltip("Optional: Give this spawn point a name for easier identification.")]
        [SerializeField] private string spawnPointName = "";

        [Tooltip("Optional: Priority for spawn selection (lower = higher priority).")]
        [SerializeField] private int priority = 0;

        public string SpawnPointName => string.IsNullOrEmpty(spawnPointName) ? gameObject.name : spawnPointName;
        public int Priority => priority;

#if UNITY_EDITOR
        [Header("Editor Visualization")]
        [SerializeField] private Color gizmoColor = Color.green;
        [SerializeField] private float gizmoRadius = 0.5f;

        void OnDrawGizmos()
        {
            Gizmos.color = gizmoColor;
            Gizmos.DrawWireSphere(transform.position, gizmoRadius);
            Gizmos.DrawLine(transform.position, transform.position + transform.forward * gizmoRadius * 2f);
        }

        void OnDrawGizmosSelected()
        {
            Gizmos.color = Color.yellow;
            Gizmos.DrawSphere(transform.position, gizmoRadius * 0.3f);
        }
#endif
    }
}