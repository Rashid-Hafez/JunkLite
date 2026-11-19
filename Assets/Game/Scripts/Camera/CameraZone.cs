using Unity.Cinemachine;
using UnityEngine;

namespace junklite
{
    /// <summary>
    /// Room framing: while the player is inside these trigger boxes, the room camera replaces the lane
    /// camera. Author the room camera's feel on the camera itself (Follow / Position Composer, Rotation
    /// Composer, Confiner 3D). Can share an object with ZMovementZone. Several BoxColliders form one zone.
    /// Room occluders listed here only work while the zone is live.
    /// </summary>
    [RequireComponent(typeof(BoxCollider))]
    public class CameraZone : MonoBehaviour
    {
        [Tooltip("Room camera. Keep its GameObject inactive in the scene; the CameraManager enables it.")]
        [SerializeField] private CinemachineCamera roomCamera;
        [SerializeField] private CinemachineBlendDefinition blendIn =
            new CinemachineBlendDefinition(CinemachineBlendDefinition.Styles.EaseInOut, 1f);
        [SerializeField] private CinemachineBlendDefinition blendOut =
            new CinemachineBlendDefinition(CinemachineBlendDefinition.Styles.EaseInOut, 1f);
        [Tooltip("Live only while the player free-roams. Snapping to the lane (Interact at a LaneEntryTrigger) hands back to the lane camera.")]
        [SerializeField] private bool freeRoamOnly;
        [Tooltip("Occluders that only work while this zone is live, e.g. a wall that must stay solid from the lane.")]
        [SerializeField] private CameraOccluder[] roomOccluders = new CameraOccluder[0];

        private static readonly CinemachineBlendDefinition Cut =
            new CinemachineBlendDefinition(CinemachineBlendDefinition.Styles.Cut, 0f);

        private BoxCollider[] boxes;
        private int playerContacts;
        private GameObject playerInside;
        private Character2D5Controller playerController;
        private PlayerLifecycle subscribedLifecycle;
        private bool live;
        private bool cutNext;

        public CinemachineCamera RoomCamera => roomCamera;
        public bool IsLive => live;

        private void Awake()
        {
            boxes = GetComponents<BoxCollider>();
            foreach (BoxCollider box in boxes) box.isTrigger = true;
            // An enabled camera jumps to the top of its priority peers and would steal the view at load.
            if (roomCamera != null) roomCamera.gameObject.SetActive(false);
            SetOccludersEnabled(false);
        }

        private void OnEnable() => Rebind();

        private void Start()
        {
            Rebind();
            if (subscribedLifecycle != null && subscribedLifecycle.Player != null)
                HandlePlayerSpawned(subscribedLifecycle.Player);
        }

        private void OnDisable()
        {
            Unbind();
            ClearPlayer();
            Refresh();
        }

        private void Update()
        {
            // Free roam can end without leaving the box (lane snap), so watch it while the player is inside.
            if (freeRoamOnly && playerInside != null) Refresh();
        }

        private void OnTriggerEnter(Collider other)
        {
            if (!other.CompareTag("Player")) return;
            var controller = other.GetComponent<Character2D5Controller>();
            if (controller == null) return;
            if (other.gameObject != playerInside) playerContacts = 0;
            playerContacts++;
            playerInside = other.gameObject;
            playerController = controller;
            Refresh();
        }

        private void OnTriggerExit(Collider other)
        {
            if (playerInside == null || other.gameObject != playerInside) return;
            if (--playerContacts > 0) return;
            ClearPlayer();
            Refresh();
        }

        private void ClearPlayer()
        {
            playerInside = null;
            playerController = null;
            playerContacts = 0;
        }

        private void Refresh()
        {
            bool want = playerInside != null &&
                        (!freeRoamOnly || playerController == null || playerController.IsFreeRoaming);
            if (want == live) return;

            live = want;
            CinemachineBlendDefinition blend = cutNext ? Cut : want ? blendIn : blendOut;
            cutNext = false;
            SetOccludersEnabled(want);

            if (roomCamera == null || CameraManager.Instance == null) return;
            if (want) CameraManager.Instance.SetZoneCamera(roomCamera, blend);
            else CameraManager.Instance.ClearZoneCamera(roomCamera, blend);
        }

        private void SetOccludersEnabled(bool value)
        {
            foreach (CameraOccluder occluder in roomOccluders)
                if (occluder != null) occluder.enabled = value;
        }

        // The CameraManager drops zones on death; re-apply with a cut if the player (re)spawns inside this one.
        private void HandlePlayerSpawned(PlayerCharacter player)
        {
            ClearPlayer();
            cutNext = true;
            Refresh(); // Drops a zone still held from before the spawn (a no-op after death).
            cutNext = false;
            if (player == null || boxes == null) return;

            // Test the body, not the pivot: the pivot sits at the feet, below a box that starts above the floor.
            Bounds playerBounds = GetBodyBounds(player);
            foreach (BoxCollider box in boxes)
            {
                if (box == null || !box.bounds.Intersects(playerBounds)) continue;
                // Physics still sends OnTriggerEnter for this overlap and counts it; starting at 1 would double it.
                playerContacts = 0;
                playerInside = player.gameObject;
                playerController = player.Controller;
                cutNext = true;
                Refresh();
                return;
            }
        }

        // Built from the transform, since collider bounds lag a teleport until the next physics step.
        private static Bounds GetBodyBounds(PlayerCharacter player)
        {
            Transform t = player.transform;
            if (player.TryGetComponent(out CapsuleCollider capsule))
            {
                float width = capsule.radius * 2f;
                return new Bounds(t.TransformPoint(capsule.center), new Vector3(width, capsule.height, width));
            }

            return new Bounds(t.position, Vector3.zero);
        }

        #region Lifecycle binding

        private void Rebind()
        {
            PlayerLifecycle lifecycle = PlayerLifecycle.Instance;
            if (lifecycle == subscribedLifecycle) return;
            Unbind();
            subscribedLifecycle = lifecycle;
            if (subscribedLifecycle != null) subscribedLifecycle.PlayerSpawned += HandlePlayerSpawned;
        }

        private void Unbind()
        {
            if (subscribedLifecycle == null) return;
            subscribedLifecycle.PlayerSpawned -= HandlePlayerSpawned;
            subscribedLifecycle = null;
        }

        #endregion

        private void OnDrawGizmos()
        {
            Gizmos.color = new Color(1f, 0.6f, 0.1f, 0.15f);
            Gizmos.matrix = transform.localToWorldMatrix;
            foreach (BoxCollider col in GetComponents<BoxCollider>())
                Gizmos.DrawCube(col.center, col.size);
            Gizmos.matrix = Matrix4x4.identity;
            Gizmos.color = new Color(1f, 0.6f, 0.1f, 0.8f);
            if (roomCamera != null)
                Gizmos.DrawLine(transform.position, roomCamera.transform.position);
            foreach (CameraOccluder occluder in roomOccluders)
                if (occluder != null) Gizmos.DrawLine(transform.position, occluder.transform.position);
        }
    }
}
