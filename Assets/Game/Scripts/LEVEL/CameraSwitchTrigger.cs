using UnityEngine;
using Unity.Cinemachine;
using System.Collections;

namespace junklite
{
    public enum CameraTriggerLockPolicy
    {
        GlobalCombat = 0,
        Encounter = 2,
        None = 3
    }

    /// <summary>
    /// 90° corner. Each crossing toggles between side A and side B: the player teleports to child "A" or "B",
    /// turns to rotationA/rotationB, and the lane camera switches to cameraA/cameraB.
    /// </summary>
    public class CameraSwitchTrigger : MonoBehaviour
    {
        [Header("Rotation (player teleports to child A / B)")]
        public bool rotateOnTrigger;
        [Tooltip("Player Y rotation on side A.")]
        public float rotationA;
        [Tooltip("Player Y rotation on side B.")]
        public float rotationB;

        [Header("Cameras")]
        public bool switchCameras;
        [Tooltip("Lane camera for side A.")]
        public CinemachineCamera cameraA;
        [Tooltip("Lane camera for side B.")]
        public CinemachineCamera cameraB;
        [Tooltip("Optional. Found automatically if empty.")]
        public CinemachineBrain cinemachineBrain;
        public float cameraBlendDuration = 0.25f;

        [Header("Locks")]
        [Tooltip("After the first crossing the trigger locks, so the player can't go back.")]
        [SerializeField] private bool oneWaySwitch = false;
        [SerializeField] private CameraTriggerLockPolicy lockPolicy = CameraTriggerLockPolicy.GlobalCombat;
        [SerializeField] private EncounterController encounterLock;

        private Transform pointA;
        private Transform pointB;
        private bool hasSwitched = false;
        private bool usingFirstState = false;
        private bool locked;
        private BoxCollider triggerCollider;
        private bool policyLocked;
        private bool oneWayLocked;
        private PlayerCombatTracker subscribedCombatTracker;
        private EncounterController subscribedEncounter;
        private PlayerLifecycle subscribedPlayerLifecycle;
        private Coroutine billboardRoutine;
        private Transform billboardTarget;
        private Quaternion billboardRestRotation;

        [Header("Hide On Cross")]
        [Tooltip("Turned off on the first crossing, back on the next. Restored on death.")]
        [SerializeField] private GameObject[] objectsToHide;
        private bool hidden = false;

        [Header("Arrows (child renderers, tinted by lock state)")]
        [SerializeField] private Color lockColor, unlockColor;
        private Renderer[] arrowRenderers;
        private MaterialPropertyBlock arrowPropertyBlock;
        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        private static readonly int ZWriteId = Shader.PropertyToID("_ZWrite");

        public bool IsLocked => locked;
        public CameraTriggerLockPolicy EffectiveLockPolicy => lockPolicy;

        private void OnEnable()
        {
            RefreshLockBindings();
            RebindPlayerLifecycle();
        }

        private void OnDisable()
        {
            CancelBillboardRotation();
            UnsubscribeFromCombatTracker();
            UnsubscribeFromEncounter();
            UnsubscribeFromPlayerLifecycle();
        }

        private void Awake()
        {
            triggerCollider = GetComponent<BoxCollider>();
            arrowPropertyBlock = new MaterialPropertyBlock();
            if (cinemachineBrain == null)
                cinemachineBrain = FindAnyObjectByType<CinemachineBrain>();
            pointA = transform.Find("A");
            pointB = transform.Find("B");

            locked = false;
            policyLocked = false;
            oneWayLocked = false;
        }

        private void Start()
        {
            // Persistent services may finish Awake after this scene object enables.
            RefreshLockBindings();
            RebindPlayerLifecycle();

            arrowRenderers = GetComponentsInChildren<Renderer>();
            foreach (Renderer renderer in arrowRenderers)
            {
                if (renderer == null) continue;
                arrowPropertyBlock.Clear();
                renderer.GetPropertyBlock(arrowPropertyBlock);
                arrowPropertyBlock.SetFloat(ZWriteId, 1f);
                renderer.SetPropertyBlock(arrowPropertyBlock);
            }

            ApplyLockState();
        }

        private void OnDestroy()
        {
            UnsubscribeFromCombatTracker();
            UnsubscribeFromEncounter();
            UnsubscribeFromPlayerLifecycle();
        }

        private void RefreshLockBindings()
        {
            CameraTriggerLockPolicy policy = EffectiveLockPolicy;

            if (policy == CameraTriggerLockPolicy.GlobalCombat)
                RebindCombatTracker();
            else
                UnsubscribeFromCombatTracker();

            if (policy == CameraTriggerLockPolicy.Encounter)
                RebindEncounter();
            else
                UnsubscribeFromEncounter();

            switch (policy)
            {
                case CameraTriggerLockPolicy.None:
                    SetPolicyLocked(false);
                    break;
            }
        }

        private void RebindCombatTracker()
        {
            PlayerCombatTracker tracker = PlayerCombatTracker.Instance;
            if (subscribedCombatTracker == tracker)
            {
                SetPolicyLocked(tracker != null && tracker.IsPlayerInCombat);
                return;
            }

            UnsubscribeFromCombatTracker();
            subscribedCombatTracker = tracker;

            if (subscribedCombatTracker == null)
            {
                SetPolicyLocked(false);
                return;
            }

            subscribedCombatTracker.OnCombatStarted += Lock;
            subscribedCombatTracker.OnCombatEnded += Unlock;
            SetPolicyLocked(subscribedCombatTracker.IsPlayerInCombat);
        }

        private void UnsubscribeFromCombatTracker()
        {
            if (subscribedCombatTracker == null)
                return;

            subscribedCombatTracker.OnCombatStarted -= Lock;
            subscribedCombatTracker.OnCombatEnded -= Unlock;
            subscribedCombatTracker = null;
        }

        private void RebindEncounter()
        {
            if (subscribedEncounter == encounterLock)
            {
                SynchronizeEncounterLock();
                return;
            }

            UnsubscribeFromEncounter();
            subscribedEncounter = encounterLock;

            if (subscribedEncounter == null)
            {
                SetPolicyLocked(false);
                return;
            }

            subscribedEncounter.EncounterStarted += HandleEncounterStarted;
            subscribedEncounter.EncounterCompleted += HandleEncounterCompleted;
            SynchronizeEncounterLock();
        }

        private void UnsubscribeFromEncounter()
        {
            if (subscribedEncounter == null)
                return;

            subscribedEncounter.EncounterStarted -= HandleEncounterStarted;
            subscribedEncounter.EncounterCompleted -= HandleEncounterCompleted;
            subscribedEncounter = null;
        }

        private void SynchronizeEncounterLock()
        {
            if (subscribedEncounter == null)
                return;

            switch (subscribedEncounter.State)
            {
                case EncounterState.Idle:
                case EncounterState.Completed:
                    SetPolicyLocked(false);
                    break;

                case EncounterState.Running:
                    SetPolicyLocked(true);
                    break;

                // Cancellation deliberately does not imply success or unlock.
                case EncounterState.Cancelled:
                    break;
            }
        }

        private void HandleEncounterStarted(EncounterController encounter)
        {
            if (encounter == subscribedEncounter)
                SetPolicyLocked(true);
        }

        private void HandleEncounterCompleted(EncounterController encounter)
        {
            if (encounter == subscribedEncounter)
                SetPolicyLocked(false);
        }

        private void RebindPlayerLifecycle()
        {
            PlayerLifecycle lifecycle = PlayerLifecycle.Instance;
            if (subscribedPlayerLifecycle == lifecycle)
                return;

            UnsubscribeFromPlayerLifecycle();
            subscribedPlayerLifecycle = lifecycle;

            if (subscribedPlayerLifecycle != null)
                subscribedPlayerLifecycle.PlayerDied += HandlePlayerDied;
        }

        private void UnsubscribeFromPlayerLifecycle()
        {
            if (subscribedPlayerLifecycle == null)
                return;

            subscribedPlayerLifecycle.PlayerDied -= HandlePlayerDied;
            subscribedPlayerLifecycle = null;
        }

        private void HandlePlayerDied(PlayerCharacter player)
        {
            ResetToDefaultState();
        }

        /// <summary>
        /// Resets this trigger back to its initial state.
        /// Called on player death so every trigger is clean for the next run.
        /// </summary>
        private void ResetToDefaultState()
        {
            CancelBillboardRotation();
            usingFirstState = false;
            hasSwitched = false;
            oneWayLocked = false;
            ApplyLockState();

            // Restore any objects that were hidden mid-run
            if (objectsToHide != null && objectsToHide.Length > 0 && hidden)
            {
                foreach (var obj in objectsToHide)
                {
                    if (obj != null)
                        obj.SetActive(true);
                }

                hidden = false;
            }
        }

        /// <summary>
        /// For a teleport (dev tools): sets this corner's state as if the player crossed it to the side
        /// whose yaw is <paramref name="laneYRotation"/>, so walking back turns the right way. Returns that
        /// side's camera, or null if the yaw matches neither side.
        /// </summary>
        public CinemachineCamera SyncToLaneSide(float laneYRotation)
        {
            bool onB = Mathf.Abs(Mathf.DeltaAngle(laneYRotation, rotationB)) < 1f;
            if (!onB && Mathf.Abs(Mathf.DeltaAngle(laneYRotation, rotationA)) >= 1f)
                return null;

            CancelBillboardRotation();
            // Crossing to B leaves usingFirstState true (the next crossing returns to A).
            if (usingFirstState != onB)
            {
                usingFirstState = onB;
                HideObjects();
                if (oneWaySwitch && onB && !hasSwitched)
                {
                    SetOneWayLocked(true);
                    hasSwitched = true;
                }
            }

            return switchCameras ? (onB ? cameraB : cameraA) : null;
        }

        private void Lock()
        {
            SetPolicyLocked(true);
        }

        private void Unlock()
        {
            SetPolicyLocked(false);
        }

        private void SetPolicyLocked(bool value)
        {
            policyLocked = value;
            ApplyLockState();
        }

        private void SetOneWayLocked(bool value)
        {
            oneWayLocked = value;
            ApplyLockState();
        }

        private void ApplyLockState()
        {
            bool shouldLock = policyLocked || oneWayLocked;
            locked = shouldLock;

            if (triggerCollider != null)
            {
                triggerCollider.isTrigger = !locked;

                if (arrowRenderers != null)
                {
                    Color color = locked ? lockColor : unlockColor;
                    foreach (Renderer renderer in arrowRenderers)
                    {
                        if (renderer != null)
                        {
                            arrowPropertyBlock.Clear();
                            renderer.GetPropertyBlock(arrowPropertyBlock);
                            arrowPropertyBlock.SetColor(BaseColorId, color);
                            renderer.SetPropertyBlock(arrowPropertyBlock);
                        }
                    }
                }
            }

        }

        private void OnTriggerEnter(Collider other)
        {
            if (locked || !other.CompareTag("Player"))
                return;

            Character2D5Controller controller = other.GetComponent<Character2D5Controller>();
            Transform playerSpine = other.transform.Find("BODY SPINE");

            if (controller != null && playerSpine != null)
            {
                if (rotateOnTrigger && (pointA == null || pointB == null))
                    return;

                SwitchCamera();
                RotateCharacter(controller, playerSpine);
                usingFirstState = !usingFirstState;
                HideObjects();

                if (oneWaySwitch && !hasSwitched)
                {
                    SetOneWayLocked(true);
                    hasSwitched = true;
                }
            }
        }

        private void RotateCharacter(Character2D5Controller controller, Transform playerSpine)
        {
            if (rotateOnTrigger)
            {
                controller.transform.position = usingFirstState ?
                    new Vector3(pointA.position.x, controller.transform.position.y, pointA.position.z)
                    : new Vector3(pointB.position.x, controller.transform.position.y, pointB.position.z);

                float fromYaw = controller.transform.eulerAngles.y;
                controller.RotatePLayer(usingFirstState ? rotationA : rotationB);
                // Signed from the actual turn, so walking the corner back spins the sprite the other way.
                float turn = Mathf.DeltaAngle(controller.transform.eulerAngles.y, fromYaw);
                controller.FreezePerpendicularAxis();

                CancelBillboardRotation();
                if (cinemachineBrain != null && switchCameras && cameraBlendDuration > 0f &&
                    cameraA != null && cameraB != null && cameraA != cameraB)
                    billboardRoutine = StartCoroutine(BillboardRotate(playerSpine, turn));
            }
        }

        private void SwitchCamera()
        {
            if (switchCameras)
            {
                if (cameraA != null && cameraB != null)
                {
                    CinemachineCamera nextCam = usingFirstState ? cameraA : cameraB;

                    // The manager owns priority and the blend; a zone/cinematic above the lane stays live.
                    if (CameraManager.Instance != null)
                        CameraManager.Instance.SetLaneCamera(nextCam, cameraBlendDuration);
                    else
                        nextCam.Prioritize();
                }
            }
        }

        public int ValidateLockConfiguration(bool logWarnings = true)
        {
            int issueCount = 0;

            void Report(string message)
            {
                issueCount++;
                if (logWarnings)
                    Debug.LogWarning($"[CameraSwitchTrigger] '{name}' {message}", this);
            }

            CameraTriggerLockPolicy policy = EffectiveLockPolicy;
            if (policy == CameraTriggerLockPolicy.Encounter && encounterLock == null)
                Report("uses the Encounter lock policy but has no encounter assigned.");

            if (policy != CameraTriggerLockPolicy.Encounter && encounterLock != null)
                Report("has an encounter assigned but is not using the Encounter lock policy.");

            return issueCount;
        }

        private void HideObjects()
        {
            if (objectsToHide != null && objectsToHide.Length > 0)
            {
                if (!hidden)
                {
                    foreach (var obj in objectsToHide)
                    {
                        if (obj != null)
                            obj.SetActive(false);
                    }
                }
                else
                {
                    foreach (var obj in objectsToHide)
                    {
                        if (obj != null)
                            obj.SetActive(true);
                    }
                }

                hidden = !hidden;
            }
        }

        /// <summary>Starts the sprite at its old world facing (turnDegrees back) and eases it to rest with the camera blend.</summary>
        public IEnumerator BillboardRotate(Transform playerSpine, float turnDegrees)
        {
            if (playerSpine == null)
                yield break;

            billboardTarget = playerSpine;
            billboardRestRotation = playerSpine.localRotation;
            Quaternion offset = Quaternion.Euler(0f, turnDegrees, 0f);

            try
            {
                playerSpine.localRotation = billboardRestRotation * offset;
                yield return null;

                while (playerSpine != null && cinemachineBrain != null)
                {
                    var blend = cinemachineBrain.ActiveBlend;
                    if (blend == null || blend.BlendWeight >= 0.9f)
                        break;

                    playerSpine.localRotation = billboardRestRotation * Quaternion.Slerp(
                        offset, Quaternion.identity, blend.BlendWeight);
                    yield return null;
                }
            }
            finally
            {
                RestoreBillboardRotation();
                billboardRoutine = null;
            }
        }

        private void CancelBillboardRotation()
        {
            if (billboardRoutine != null)
            {
                StopCoroutine(billboardRoutine);
                billboardRoutine = null;
            }

            RestoreBillboardRotation();
        }

        private void RestoreBillboardRotation()
        {
            if (billboardTarget != null)
                billboardTarget.localRotation = billboardRestRotation;

            billboardTarget = null;
        }

        private void OnDrawGizmos()
        {
            Gizmos.color = locked ? Color.red : Color.green;
            if (triggerCollider != null)
                Gizmos.DrawCube(transform.position, triggerCollider.bounds.size);
        }

#if UNITY_EDITOR
        private void OnValidate()
        {
            ValidateLockConfiguration();
        }
#endif
    }
}
