using System.Collections.Generic;
using Unity.Cinemachine;
using UnityEngine;

namespace junklite
{
    /// <summary>
    /// A progress marker the player has to reach. Trigger box across the lane: when the
    /// player first walks into it, <see cref="PlayerLifecycle"/> respawns her here (position
    /// and yaw of Spawn Point, this transform by default) until a later checkpoint is
    /// reached. Progress only moves forward: each checkpoint fires once per run, and one
    /// with a lower Order than the active checkpoint is ignored, so backtracking through an
    /// earlier checkpoint never moves the respawn back. Only reached checkpoints count: in a
    /// scene with any Checkpoint, dying before the first one respawns at the primary spawn,
    /// never at the nearest marker. Restart Level resets every checkpoint. Lives under
    /// [Gameplay]; the yaw must match the lane (0 = lane along +X, 270 = lane along +Z, as
    /// LanePath.YRotationFor).
    /// Reaching it also snapshots the route: the live lane camera and every Camera Switch
    /// Trigger's state. A respawn here restores them, because death resets the corners and
    /// every spawn snaps the camera back to the spawn camera.
    /// </summary>
    [RequireComponent(typeof(Collider))]
    public sealed class Checkpoint : MonoBehaviour
    {
        [Tooltip("Position in the level's checkpoint sequence (1 = first). A checkpoint with a lower order than the active one is ignored, so number them along the intended route. Equal orders (or all 0) fall back to first-come.")]
        [SerializeField, Min(0)] private int order;
        [Tooltip("Where to respawn. Empty = this transform. Its yaw must match the lane.")]
        [SerializeField] private Transform spawnPoint;
        [Tooltip("Optional object switched on once this checkpoint has been reached (a light, a glow).")]
        [SerializeField] private GameObject reachedVisual;

        private bool reached;
        private CinemachineCamera laneCamera;
        private readonly List<(CameraSwitchTrigger corner, CameraSwitchTrigger.RouteState state)> cornerStates = new();

        public int Order => order;
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

        /// <summary>
        /// Reaches this checkpoint. Fires once per run, and only if it is progress
        /// (not behind the active checkpoint); otherwise nothing happens.
        /// </summary>
        public void Activate()
        {
            if (reached)
                return;

            PlayerLifecycle lifecycle = PlayerLifecycle.Instance;
            if (lifecycle != null && !lifecycle.TryReachCheckpoint(this))
                return;

            reached = true;
            CaptureRoute();
            if (reachedVisual != null)
                reachedVisual.SetActive(true);
        }

        /// <summary>Forgets that this checkpoint was reached (Restart Level).</summary>
        public void ResetProgress()
        {
            reached = false;
            laneCamera = null;
            cornerStates.Clear();
            if (reachedVisual != null)
                reachedVisual.SetActive(false);
        }

        /// <summary>
        /// Called by PlayerLifecycle right after the player spawned here: every corner goes back to
        /// the state it had when this checkpoint was reached, and the camera cuts to the lane camera
        /// that framed it, so the view faces the lane and walking back through a corner turns the
        /// right way.
        /// </summary>
        public void RestoreRoute()
        {
            foreach ((CameraSwitchTrigger corner, CameraSwitchTrigger.RouteState state) in cornerStates)
            {
                if (corner != null)
                    corner.RestoreRouteState(state);
            }

            if (laneCamera != null && CameraManager.Instance != null)
                CameraManager.Instance.CutToLaneCamera(laneCamera);
        }

        private void CaptureRoute()
        {
            laneCamera = CameraManager.Instance != null ? CameraManager.Instance.LaneCamera : null;
            cornerStates.Clear();
            foreach (CameraSwitchTrigger corner in FindObjectsByType<CameraSwitchTrigger>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
                cornerStates.Add((corner, corner.CaptureRouteState()));
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
