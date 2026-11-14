using UnityEngine;

namespace junklite
{
    /// <summary>
    /// Starts a prop fully dissolved and dissolves it into existence once, when the player
    /// comes within <see cref="radius"/>. The dissolve cutoff range comes from the mesh's local
    /// bounds, so any prop size works with the object-space Dissolve shader.
    /// </summary>
    [RequireComponent(typeof(DissolveController))]
    public class ProximityDissolveIn : MonoBehaviour
    {
        [SerializeField] private float radius = 40f;
        [SerializeField] private float duration = 1.2f;
        [SerializeField, Tooltip("Seconds between distance checks.")]
        private float checkInterval = 0.15f;
        [SerializeField, Tooltip("Object-space units past the mesh bounds; covers the shader's noise, checker and edge thickness.")]
        private float boundsMargin = 2f;

        private DissolveController dissolver;
        private PlayerLifecycle lifecycle;
        private Transform target;
        private float hiddenCutoff, visibleCutoff;
        private float nextCheck;
        private bool revealed;

        private void Awake()
        {
            dissolver = GetComponent<DissolveController>();

            var filter = GetComponent<MeshFilter>();
            Bounds b = filter != null && filter.sharedMesh != null
                ? filter.sharedMesh.bounds
                : new Bounds(Vector3.zero, Vector3.one);
            hiddenCutoff = b.max.y + boundsMargin;
            visibleCutoff = b.min.y - boundsMargin;
        }

        private void OnEnable()
        {
            if (!revealed)
                dissolver.SetImmediate(hiddenCutoff);

            lifecycle = PlayerLifecycle.Instance;
            if (lifecycle != null)
            {
                lifecycle.PlayerSpawned += HandlePlayerSpawned;
                if (lifecycle.Player != null) target = lifecycle.Player.transform;
            }
        }

        private void OnDisable()
        {
            if (lifecycle != null)
                lifecycle.PlayerSpawned -= HandlePlayerSpawned;
            lifecycle = null;
        }

        private void HandlePlayerSpawned(PlayerCharacter player) =>
            target = player != null ? player.transform : null;

        private void Update()
        {
            if (revealed || Time.time < nextCheck) return;
            nextCheck = Time.time + checkInterval;

            if (target == null)
            {
                if (lifecycle == null) OnEnable();
                return;
            }

            if ((target.position - transform.position).sqrMagnitude > radius * radius) return;

            revealed = true;
            dissolver.AnimateDissolve(visibleCutoff, duration);
            enabled = false;
        }
    }
}
