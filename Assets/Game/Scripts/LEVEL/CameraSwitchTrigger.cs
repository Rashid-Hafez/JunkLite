using UnityEngine;
using Unity.Cinemachine;
using System.Collections;
using System.Collections.Generic;
using System;
using System.Linq;
using NUnit.Framework;

namespace junklite
{
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
        [SerializeField] private bool locked = false;
        [SerializeField] private BoxCollider triggerCollider;
        private Action combatStartHandler;
        private Action combatEndHandler;

        [Header("Culling Objects")]
        private bool hidden = false;
        [SerializeField] private GameObject[] objectsToHide;

        [Header("Arrows")]
        [SerializeField] private Renderer[] arrowRenderers;
        [SerializeField] private Color lockColor, unlockColor;

        [Header("Enemy Alive Lock")]
        [SerializeField] private bool enableEnemyLock;
        [SerializeField] private List<EnemyCharacter> enemies = new List<EnemyCharacter>();

        private void OnEnable()
        {
            if (PlayerCombatTracker.Instance != null)
            {
                PlayerCombatTracker.Instance.OnCombatStarted += combatStartHandler;
                PlayerCombatTracker.Instance.OnCombatEnded += combatEndHandler;
            }
        }

        private void OnDisable()
        {
            if (PlayerCombatTracker.Instance != null)
            {
                PlayerCombatTracker.Instance.OnCombatStarted -= combatStartHandler;
                PlayerCombatTracker.Instance.OnCombatEnded -= combatEndHandler;
            }
        }

        private void Awake()
        {
            triggerCollider = GetComponent<BoxCollider>();
            cinemachineBrain = FindAnyObjectByType<CinemachineBrain>();
            pointA = transform.Find("A");
            pointB = transform.Find("B");

            locked = false;

            combatStartHandler = Lock;
            combatEndHandler = Unlock;
        }

        private void Start()
        {
            if (PlayerCombatTracker.Instance != null)
            {
                PlayerCombatTracker.Instance.OnCombatStarted += Lock;
                PlayerCombatTracker.Instance.OnCombatEnded += Unlock;
            }

            arrowRenderers = GetComponentsInChildren<Renderer>();
            foreach (Renderer renderer in arrowRenderers)
            {
                renderer.material.SetFloat("_ZWrite", 1f);
            }

            // Reset this trigger's state whenever the player dies so it's
            // fresh for the next run. Camera snap is handled by CameraManager.
            if (GameManager.Instance != null)
                GameManager.Instance.OnPlayerDied += ResetToDefaultState;
        }

        private void OnDestroy()
        {
            if (PlayerCombatTracker.Instance != null)
            {
                PlayerCombatTracker.Instance.OnCombatStarted -= Lock;
                PlayerCombatTracker.Instance.OnCombatEnded -= Unlock;
            }

            if (GameManager.Instance != null)
                GameManager.Instance.OnPlayerDied -= ResetToDefaultState;
        }

        /// <summary>
        /// Resets this trigger back to its initial state.
        /// Called on player death so every trigger is clean for the next run.
        /// </summary>
        private void ResetToDefaultState()
        {
            usingFirstState = false;

            // Restore any objects that were hidden mid-run
            if (objectsToHide.Length > 0 && hidden)
            {
                foreach (var obj in objectsToHide)
                    obj.SetActive(true);

                hidden = false;
            }
        }

        private void Lock()
        {
            locked = true;
            Debug.Log("Locked");
            if (triggerCollider != null)
            {
                triggerCollider.isTrigger = false;
                foreach (Renderer renderer in arrowRenderers)
                    renderer.material.SetColor("_BaseColor", lockColor);
            }
        }

        private void Unlock()
        {
            locked = false;
            Debug.Log("Unlocked");
            if (triggerCollider != null)
            {
                triggerCollider.isTrigger = true;
                foreach (Renderer renderer in arrowRenderers)
                    renderer.material.SetColor("_BaseColor", unlockColor);
            }
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
                    Lock();
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
            // Only check while enemy-lock is enabled to avoid repeated calls when unused
            if (enableEnemyLock)
                CheckEnemiesAlive();
        }

        private void CheckEnemiesAlive()
        {
            // Defensive: do nothing if not enabled
            if (!enableEnemyLock)
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
                if (locked)
                    Unlock();
            }
            else
            {
                if (!locked)
                    Lock();
            }
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
    }
}