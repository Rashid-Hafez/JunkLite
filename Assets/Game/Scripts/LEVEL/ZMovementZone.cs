using UnityEngine;

namespace junklite
{
    /// <summary>
    /// Free-roam box: while the player is inside, vertical input walks in depth (Z).
    /// Leaving the box re-locks the player to the lane (or the depth they entered at), unless something
    /// (usually a LaneEntryTrigger E-press) already snapped them back onto a lane.
    /// Re-enables free roam when the player (re)spawns inside the box.
    /// Several BoxColliders on this object form one zone (for L-shaped rooms).
    /// </summary>
    [RequireComponent(typeof(BoxCollider))]
    public class ZMovementZone : MonoBehaviour
    {
        [Tooltip("Optional. Leaving the box without an E-press snaps the player to the closest point of this lane.")]
        [SerializeField] private LanePath lanePath;

        private BoxCollider[] boxes;
        private int playerContacts;
        private Character2D5Controller playerInside;
        private PlayerLifecycle subscribedLifecycle;
        private float entryDepth;
        private bool depthIsWorldX;

        private void Awake()
        {
            boxes = GetComponents<BoxCollider>();
            foreach (BoxCollider box in boxes) box.isTrigger = true;
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
            playerInside = null;
            playerContacts = 0;
        }

        private void OnTriggerEnter(Collider other)
        {
            if (!other.CompareTag("Player")) return;
            var controller = other.GetComponent<Character2D5Controller>();
            if (controller == null) return;
            if (controller != playerInside) playerContacts = 0;
            playerContacts++;
            if (controller != playerInside) Enter(controller);
        }

        private void OnTriggerExit(Collider other)
        {
            if (playerInside == null || other.gameObject != playerInside.gameObject) return;
            if (--playerContacts > 0) return;

            Character2D5Controller controller = playerInside;
            playerInside = null;
            // Already snapped onto a lane (E-press at a door): leave that placement alone.
            if (!controller.IsFreeRoaming) return;

            Vector3 pos = controller.transform.position;
            if (lanePath != null && lanePath.TryGetClosestPoint(pos, out Vector3 point, out float yRotation))
            {
                controller.SnapToLane(point, yRotation);
                return;
            }

            // No lane assigned: restore the depth coordinate the player entered at.
            if (depthIsWorldX) pos.x = entryDepth;
            else pos.z = entryDepth;
            controller.SnapToLane(pos, controller.transform.eulerAngles.y);
        }

        private void Enter(Character2D5Controller controller)
        {
            playerInside = controller;
            // Depth = the world axis transform.forward runs along (Z on X lanes, X on Z lanes).
            Vector3 forward = controller.transform.forward;
            depthIsWorldX = Mathf.Abs(forward.x) > Mathf.Abs(forward.z);
            Vector3 pos = controller.transform.position;
            entryDepth = depthIsWorldX ? pos.x : pos.z;
            controller.SetFreeRoam(true);
        }

        private void HandlePlayerSpawned(PlayerCharacter player)
        {
            playerInside = null;
            playerContacts = 0;
            if (player == null || player.Controller == null || boxes == null) return;
            Vector3 position = player.Controller.transform.position;
            foreach (BoxCollider box in boxes)
            {
                if (box == null || !box.bounds.Contains(position)) continue;
                playerContacts = 1;
                Enter(player.Controller);
                return;
            }
        }

        #region Lifecycle binding

        private void Rebind()
        {
            PlayerLifecycle lifecycle = PlayerLifecycle.Instance;
            if (lifecycle == subscribedLifecycle) return;
            if (subscribedLifecycle != null) subscribedLifecycle.PlayerSpawned -= HandlePlayerSpawned;
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
            Gizmos.color = new Color(0.3f, 0.5f, 1f, 0.2f);
            Gizmos.matrix = transform.localToWorldMatrix;
            foreach (BoxCollider col in GetComponents<BoxCollider>())
                Gizmos.DrawCube(col.center, col.size);
        }
    }
}
