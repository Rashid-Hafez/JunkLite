using UnityEngine;
using Unity.Cinemachine;
using System.Collections;
using System.Threading;

namespace junklite
{
    public class CameraSwitchTrigger : MonoBehaviour
    {
        [Header("Rotation Settings")]
        public bool rotateOnTrigger;
        public float rotationA; // first rotation (Y-axis)
        public float rotationB; // second rotation (Y-axis)

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
        private bool usingFirstState = false;

        [Header("Lock Settings")]
        private bool locked;
        private Collider triggerCollider;

        private void Awake()
        {
            locked = false;
            pointA = transform.Find("A");
            pointB = transform.Find("B");
            cinemachineBrain = FindAnyObjectByType<CinemachineBrain>();
            triggerCollider = GetComponent<Collider>();
        }



        private void OnTriggerEnter(Collider other)
        {
            if (!other.CompareTag("Player"))
                return;

            Character2D5Controller controller = other.GetComponent<Character2D5Controller>();
            Transform playerSpine = other.transform.Find("BODY SPINE");
            PlayerCombatTracker.Instance.OnCombatStarted += () => { locked = true; triggerCollider.isTrigger = false; };
            PlayerCombatTracker.Instance.OnCombatEnded += () => { locked = false; triggerCollider.isTrigger =true; }; // Register with combat tracker if it exists

            if (controller != null && playerSpine != null)
            {
                Debug.Log("Entered trigger");



                if (switchCameras)
                {
                    Debug.Log("Switching cameras");
                    // Toggle cameras
                    if (cameraA != null && cameraB != null)
                    {
                        if (usingFirstState)
                        {
                            Debug.Log("Switching to Camera A");
                            cameraA.Prioritize();
                            cameraA.transform.Find("Particles").gameObject.SetActive(true);
                            cameraB.transform.Find("Particles").gameObject.SetActive(false);
                        }
                        else
                        {
                            Debug.Log("Switching to Camera B");
                            cameraB.Prioritize();
                            cameraB.transform.Find("Particles").gameObject.SetActive(true);
                            cameraA.transform.Find("Particles").gameObject.SetActive(false);
                        }
                        cinemachineBrain.DefaultBlend.Time = cameraBlendDuration;
                    }


                }

                if (rotateOnTrigger)
                {

                    // Fix the player's position to prevent sliding
                    controller.transform.position = usingFirstState ?
                        new Vector3(pointA.position.x, controller.transform.position.y, pointA.position.z)
                        : new Vector3(pointB.position.x, controller.transform.position.y, pointB.position.z);


                    // Set the correct rotation
                    controller.RotatePLayer(usingFirstState ? rotationA : rotationB);

                    controller.FreezePerpendicularAxis();

                    // Start billboard coroutine (GETS APPLIED TO THE SPINE OBJECT)
                    StartCoroutine(BillboardRotate(playerSpine));
                }
              

                if (!oneWaySwitch)
                {
                    usingFirstState = !usingFirstState; // Toggle state for next trigger
                }
            }
        }

        public IEnumerator BillboardRotate(Transform playerSpine)
        {
            playerSpine.localRotation = Quaternion.Euler(0f, 90f, 0f);
            yield return null; // Wait for the next frame to ensure the camera switch has taken effect
            while (cinemachineBrain.ActiveBlend.BlendWeight < 0.9f && cinemachineBrain.ActiveBlend != null)
            {
                Debug.Log("Blending cameras, progress: " + cinemachineBrain.ActiveBlend.BlendWeight);
                float progress = cinemachineBrain.ActiveBlend.BlendWeight;

                playerSpine.localRotation = Quaternion.Euler(0f, Mathf.Lerp(90f, 0f, progress), 0f);
                yield return null;
            }

            playerSpine.localRotation = Quaternion.Euler(0f, 0f, 0f); // Ensure final rotation is correct   
        
        
        
        }
        private void OnDrawGizmos()
        {
            Gizmos.color = locked? Color.red : Color.green;
            Gizmos.DrawCube(transform.position, triggerCollider.bounds.size);
        }
    }

   
}


