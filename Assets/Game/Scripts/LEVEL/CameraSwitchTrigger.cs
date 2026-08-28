using UnityEngine;
using Unity.Cinemachine;
using System.Collections;
using System.Collections.Generic;

namespace junklite
{
    public enum CameraTriggerLockPolicy
    {
        // Preserves existing scenes: enemy-list locking when enabled, otherwise
        // the original global PlayerCombatTracker behavior.
        LegacyAutomatic,
        GlobalCombat,
        Encounter,
        None,
        LegacyEnemyList
    }

    public class CameraSwitchTrigger : MonoBehaviour
    {
        [Header("Rotation Settings")]
        public bool rotateOnTrigger;
        public float rotationA; // first rotation (Y-axis)
        public float rotationB; // second rotation (Y-axis)
        [SerializeField, Tooltip("Set to true if the rotation from A to B is Counter-clockwise.")] private bool ccwAB = true;

        [Header("Camera Settings")]
        public bool switchCameras;
        public CinemachineCamera cameraA;
        public CinemachineCamera cameraB;
        public CinemachineBrain cinemachineBrain;
        public float cameraBlendDuration = 0.25f;

        [Header("Teleport Points")]
        private Transform pointA;
        private Transform pointB;

        [SerializeField] private bool oneWaySwitch = false;
        private bool hasSwitched = false;
        private bool usingFirstState = false;

        [Header("Lock Settings")]
        [SerializeField] private CameraTriggerLockPolicy lockPolicy = CameraTriggerLockPolicy.LegacyAutomatic;
        [SerializeField] private EncounterController encounterLock;
        [SerializeField] private bool locked = false;
        [SerializeField] private BoxCollider triggerCollider;
        private bool policyLocked;
        private bool oneWayLocked;
        private PlayerCombatTracker subscribedCombatTracker;
        private EncounterController subscribedEncounter;
        private PlayerLifecycle subscribedPlayerLifecycle;

        [Header("Culling Objects")]
        private bool hidden = false;
        [SerializeField] private GameObject[] objectsToHide;

        [Header("Arrows")]
        [SerializeField] private Renderer[] arrowRenderers;
        [SerializeField] private Color lockColor, unlockColor;

        [Header("Legacy Enemy Alive Lock")]
        [SerializeField] private bool enableEnemyLock;
        [SerializeField] private List<EnemyCharacter> enemies = new List<EnemyCharacter>();

        public bool IsLocked => locked;
        public CameraTriggerLockPolicy EffectiveLockPolicy => lockPolicy switch
        {
            CameraTriggerLockPolicy.LegacyAutomatic when enableEnemyLock =>
                CameraTriggerLockPolicy.LegacyEnemyList,
            CameraTriggerLockPolicy.LegacyAutomatic =>
                CameraTriggerLockPolicy.GlobalCombat,
            _ => lockPolicy
        };

        private void OnEnable()
        {
            RefreshLockBindings();
            RebindPlayerLifecycle();
        }

        private void OnDisable()
        {
            UnsubscribeFromCombatTracker();
            UnsubscribeFromEncounter();
            UnsubscribeFromPlayerLifecycle();
        }

        private void Awake()
        {
            triggerCollider = GetComponent<BoxCollider>();
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
                renderer.material.SetFloat("_ZWrite", 1f);
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

                case CameraTriggerLockPolicy.LegacyEnemyList:
                    CheckEnemiesAlive();
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
            usingFirstState = false;
            hasSwitched = false;
            oneWayLocked = false;
            ApplyLockState();

            // Restore any objects that were hidden mid-run
            if (objectsToHide != null && objectsToHide.Length > 0 && hidden)
            {
                foreach (var obj in objectsToHide)
                    obj.SetActive(true);

                hidden = false;
            }
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
            bool changed = locked != shouldLock;
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
                            renderer.material.SetColor("_BaseColor", color);
                    }
                }
            }

            if (changed)
                Debug.Log(locked ? "Locked" : "Unlocked", this);
        }

        private void OnTriggerEnter(Collider other)
        {
            if (!other.CompareTag("Player"))
                return;

            Character2D5Controller controller = other.GetComponent<Character2D5Controller>();
            Transform playerSpine = other.transform.Find("BODY SPINE");

            if (controller != null && playerSpine != null)
            {
                Debug.Log("Entered trigger");

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

                controller.RotatePLayer(usingFirstState ? rotationA : rotationB);
                controller.FreezePerpendicularAxis();

                StartCoroutine(BillboardRotate(playerSpine));
            }
        }

        private void SwitchCamera()
        {
            if (switchCameras)
            {
                Debug.Log("Switching cameras");
                if (cameraA != null && cameraB != null)
                {
                    CinemachineCamera nextCam;
                    if (usingFirstState)
                    {
                        cameraA.Prioritize();
                        nextCam = cameraA;
                    }
                    else
                    {
                        cameraB.Prioritize();
                        nextCam = cameraB;
                    }
                    cinemachineBrain.DefaultBlend.Time = cameraBlendDuration;

                    CameraManager.Instance?.SetActiveCamera(nextCam);
                }
            }
        }

        private void Update()
        {
            // Compatibility only. New encounter locks are event-driven.
            if (EffectiveLockPolicy == CameraTriggerLockPolicy.LegacyEnemyList)
                CheckEnemiesAlive();
        }

        private void CheckEnemiesAlive()
        {
            if (EffectiveLockPolicy != CameraTriggerLockPolicy.LegacyEnemyList)
                return;

            if (enemies == null)
                enemies = new List<EnemyCharacter>();

            // Remove null/destroyed or dead enemies from the tracked list.
            // Use RemoveAll to avoid modifying the list while iterating.
            enemies.RemoveAll(e =>
                e == null ||
                e.attributes?.Health == null ||
                e.attributes.Health.Current <= 0);

            // If there are no enemies left, ensure unlocked; otherwise ensure locked.
            if (enemies.Count == 0)
            {
                if (policyLocked)
                    SetPolicyLocked(false);
            }
            else
            {
                if (!policyLocked)
                    SetPolicyLocked(true);
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

            if (policy == CameraTriggerLockPolicy.LegacyEnemyList &&
                (enemies == null || enemies.Count == 0))
            {
                Report("uses the legacy enemy-list lock but has no enemies assigned.");
            }

            return issueCount;
        }

        private void HideObjects()
        {
            if (objectsToHide.Length > 0)
            {
                if (!hidden)
                {
                    foreach (var obj in objectsToHide)
                        obj.SetActive(false);
                }
                else
                {
                    foreach (var obj in objectsToHide)
                        obj.SetActive(true);
                }

                hidden = !hidden;
            }
        }

        public IEnumerator BillboardRotate(Transform playerSpine)
        {
            playerSpine.localRotation = Quaternion.Euler(0f, ccwAB ? 90f : -90f, 0f);
            yield return null;
            while (cinemachineBrain.ActiveBlend.BlendWeight < 0.9f && cinemachineBrain.ActiveBlend != null)
            {
                float progress = cinemachineBrain.ActiveBlend.BlendWeight;
                playerSpine.localRotation = Quaternion.Euler(0f, Mathf.Lerp(ccwAB ? 90f : -90f, 0f, progress), 0f);
                yield return null;
            }

            playerSpine.localRotation = Quaternion.Euler(0f, 0f, 0f);
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
