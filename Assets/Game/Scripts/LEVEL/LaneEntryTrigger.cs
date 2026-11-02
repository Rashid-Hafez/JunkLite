using System;
using System.Collections;
using Unity.Cinemachine;
using UnityEngine;

namespace junklite
{
    /// <summary>
    /// Entry point from a free-move room (safe room / explore room) onto a LanePath.
    /// While the player stands in this trigger box the shared interact prompt ("[E] Proceed") shows;
    /// pressing Interact walks the player (eased) onto the closest point of the lane with the lane's rotation.
    /// The optional gate door stays locked until the player has snapped, and locks again on every (re)spawn.
    /// Until the snap the player free-roams the room in depth (Character2D5Controller.SetFreeRoam).
    /// With Walk Through set, Interact always carries the player out of the room: input locks, the player steps onto
    /// the lane and walks through the door to Walk Through, then input returns. A Transition plays its shot first.
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
        [Tooltip("Speed (m/s) of the eased step from the room onto the lane. 0 snaps at once.")]
        [SerializeField, Min(0f)] private float stepSpeed = 4f;
        [Tooltip("Point past the door, outside the room's zones. After Interact the player walks here (snapped onto the lane), so she always leaves the room.")]
        [SerializeField] private Transform walkThrough;
        [SerializeField, Min(0.1f)] private float walkSpeed = 3f;

        [Header("Cinematic (optional)")]
        [Tooltip("Shot to play on Interact. Set its Start On to Script. The player walks only after the shot settles.")]
        [SerializeField] private CameraTransition transition;
        [Tooltip("Longest wait (s) for the shot to settle before the player moves anyway.")]
        [SerializeField, Min(0f)] private float maxWait = 4f;

        public CameraTransition Transition => transition;
        public Transform WalkThrough => walkThrough;

        private DoorAccessRule gateRule;
        private Character2D5Controller playerInside;
        private GameInputManager subscribedInput;
        private PlayerLifecycle subscribedLifecycle;
        private bool playerSnapped;
        private Coroutine entryRoutine;
        private IDisposable entryInputLock;

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
            CancelEntry();
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
            // Came back into the room before reaching Arrival; a Script-mode shot never sees that retreat.
            if (transition != null && transition.IsActive) transition.Release();
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
            if (playerInside == null || lanePath == null || !GateOpenable || entryRoutine != null) return;
            if (!lanePath.TryGetClosestPoint(playerInside.transform.position, out Vector3 point, out float yRotation)) return;

            Character2D5Controller controller = playerInside;
            playerInside = null;
            playerSnapped = true;
            HidePrompt(instant: false);
            entryRoutine = StartCoroutine(CoEnter(controller, point, yRotation));
        }

        private IEnumerator CoEnter(Character2D5Controller controller, Vector3 point, float yRotation)
        {
            PlayerState state = controller.GetComponent<PlayerState>();
            entryInputLock = state != null ? state.AcquireInputLock() : null;
            controller.StopAllVelocity();

            if (transition != null && transition.Play(controller.transform))
                yield return WaitUntilOrTimeout(() => transition.IsSettled || !transition.IsActive);

            SetGateLocked(false);
            controller.StepToLane(point, yRotation, stepSpeed);
            if (laneCamera != null)
            {
                if (CameraManager.Instance != null) CameraManager.Instance.SetLaneCamera(laneCamera);
                else laneCamera.Prioritize();
            }
            while (controller.IsSteppingToLane) yield return null;

            // The unlocked door slides open (0.45 s) as the player walks into its trigger, so walk at once.
            // Only another lock (e.g. an encounter) keeps her on this side instead of snapping through it.
            if (walkThrough != null && (gateDoor == null || !gateDoor.Locked) &&
                lanePath.TryGetClosestPoint(walkThrough.position, out Vector3 exit, out float exitYRotation))
            {
                controller.StepToLane(exit, exitYRotation, walkSpeed);
                while (controller.IsSteppingToLane) yield return null;
            }

            entryRoutine = null;
            ReleaseEntryInput();
        }

        private IEnumerator WaitUntilOrTimeout(Func<bool> done)
        {
            float until = Time.time + maxWait;
            while (!done() && Time.time < until) yield return null;
        }

        // The shot itself is released by the transition (arrival, death, max hold); this only stops the walk.
        private void CancelEntry()
        {
            if (entryRoutine != null) StopCoroutine(entryRoutine);
            entryRoutine = null;
            ReleaseEntryInput();
        }

        private void ReleaseEntryInput()
        {
            entryInputLock?.Dispose();
            entryInputLock = null;
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
            CancelEntry();
            playerInside = null;
            HidePrompt(instant: true);
        }

        private void HandlePlayerSpawned(PlayerCharacter player)
        {
            CancelEntry();
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
