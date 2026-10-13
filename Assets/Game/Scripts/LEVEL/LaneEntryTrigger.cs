using Unity.Cinemachine;
using UnityEngine;

namespace junklite
{
    /// <summary>
    /// Entry point from a free-move room (safe room / explore room) onto a LanePath.
    /// While the player stands in this trigger box the shared interact prompt ("[E] Proceed") shows;
    /// pressing Interact snaps the player onto the closest point of the lane with the lane's rotation.
    /// The optional gate door stays locked until the player has snapped, and locks again on every (re)spawn.
    /// Until the snap the player free-roams the room in depth (Character2D5Controller.SetFreeRoam).
    /// </summary>
    [RequireComponent(typeof(BoxCollider))]
    public class LaneEntryTrigger : MonoBehaviour
    {
        [SerializeField] private LanePath lanePath;
        [Tooltip("Door out of the free-move room. Locked until the player presses Interact here.")]
        [SerializeField] private SlidingDoor gateDoor;
        [SerializeField] private string promptLabel = "Proceed";
        [Tooltip("Optional. Made the active camera on snap, for lanes that run on a different axis than the room view.")]
        [SerializeField] private CinemachineCamera laneCamera;

        private DoorAccessRule gateRule;
        private Character2D5Controller playerInside;
        private GameInputManager subscribedInput;
        private PlayerLifecycle subscribedLifecycle;
        private bool playerSnapped;

        private void Awake()
        {
            GetComponent<BoxCollider>().isTrigger = true;
            if (gateDoor != null) gateRule = gateDoor.GetComponent<DoorAccessRule>();
            SetGateLocked(true);
        }

        private void OnEnable()
        {
            Rebind();
            if (gateRule != null) gateRule.SatisfiedChanged += HandleGateRuleChanged;
            HidePrompt(instant: true);
        }

        private void Start()
        {
            Rebind();
            if (subscribedLifecycle != null && subscribedLifecycle.Player != null && !playerSnapped)
                HandlePlayerSpawned(subscribedLifecycle.Player);
        }

        private void OnDisable()
        {
            Unbind();
            if (gateRule != null) gateRule.SatisfiedChanged -= HandleGateRuleChanged;
            playerInside = null;
            HidePrompt(instant: true);
        }

        private void OnTriggerEnter(Collider other)
        {
            if (!other.CompareTag("Player")) return;
            var controller = other.GetComponent<Character2D5Controller>();
            // Re-arms if the player came back into a free-roam zone after snapping here once.
            if (controller == null || (playerSnapped && !controller.IsFreeRoaming)) return;

            playerInside = controller;
            Rebind();
            if (GateOpenable) TutorialPromptHUD.ShowInteract(this, promptLabel);
        }

        private void OnTriggerExit(Collider other)
        {
            if (playerInside == null || other.gameObject != playerInside.gameObject) return;
            playerInside = null;
            HidePrompt(instant: false);
        }

        private void HandleInteract()
        {
            // A locked gate (DoorAccessRule) owns the prompt and Interact until it is satisfied.
            if (playerInside == null || lanePath == null || !GateOpenable) return;
            if (!lanePath.TryGetClosestPoint(playerInside.transform.position, out Vector3 point, out float yRotation)) return;

            playerInside.SnapToLane(point, yRotation);
            if (laneCamera != null)
            {
                laneCamera.Prioritize();
                if (CameraManager.Instance != null) CameraManager.Instance.SetActiveCamera(laneCamera);
            }
            playerInside = null;
            playerSnapped = true;
            SetGateLocked(false);
            HidePrompt(instant: false);
        }

        private bool GateOpenable => gateRule == null || !gateRule.isActiveAndEnabled || gateRule.IsSatisfied();

        private void HandleGateRuleChanged()
        {
            if (playerInside == null) return;
            if (GateOpenable) TutorialPromptHUD.ShowInteract(this, promptLabel);
            else HidePrompt(instant: true);
        }

        private void HandlePlayerDied(PlayerCharacter _)
        {
            playerInside = null;
            HidePrompt(instant: true);
        }

        private void HandlePlayerSpawned(PlayerCharacter player)
        {
            playerSnapped = false;
            SetGateLocked(true);
            // The player (re)spawns inside the free-move room; SnapToLane ends free roam.
            if (player != null && player.Controller != null)
                player.Controller.SetFreeRoam(true);
        }

        private void SetGateLocked(bool locked)
        {
            if (gateDoor == null) return;
            if (locked) gateDoor.AddLock(this);
            else gateDoor.RemoveLock(this);
        }

        #region Input / lifecycle binding

        private void Rebind()
        {
            GameInputManager input = GameInputManager.Instance;
            if (input != subscribedInput)
            {
                if (subscribedInput != null)
                {
                    subscribedInput.OnInteract -= HandleInteract;
                }
                subscribedInput = input;
                if (subscribedInput != null)
                {
                    subscribedInput.OnInteract += HandleInteract;
                }
            }

            PlayerLifecycle lifecycle = PlayerLifecycle.Instance;
            if (lifecycle != subscribedLifecycle)
            {
                if (subscribedLifecycle != null)
                {
                    subscribedLifecycle.PlayerDied -= HandlePlayerDied;
                    subscribedLifecycle.PlayerSpawned -= HandlePlayerSpawned;
                }
                subscribedLifecycle = lifecycle;
                if (subscribedLifecycle != null)
                {
                    subscribedLifecycle.PlayerDied += HandlePlayerDied;
                    subscribedLifecycle.PlayerSpawned += HandlePlayerSpawned;
                }
            }
        }

        private void Unbind()
        {
            if (subscribedInput != null)
            {
                subscribedInput.OnInteract -= HandleInteract;
                subscribedInput = null;
            }

            if (subscribedLifecycle != null)
            {
                subscribedLifecycle.PlayerDied -= HandlePlayerDied;
                subscribedLifecycle.PlayerSpawned -= HandlePlayerSpawned;
                subscribedLifecycle = null;
            }
        }

        #endregion

        private void HidePrompt(bool instant) => TutorialPromptHUD.HideInteract(this, instant: instant);

        private void OnDrawGizmos()
        {
            var box = GetComponent<BoxCollider>();
            if (box == null) return;
            Gizmos.color = new Color(0.2f, 1f, 0.6f, 0.25f);
            Gizmos.matrix = transform.localToWorldMatrix;
            Gizmos.DrawCube(box.center, box.size);
        }
    }
}
