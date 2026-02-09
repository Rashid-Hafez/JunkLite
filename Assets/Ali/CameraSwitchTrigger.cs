using UnityEngine;
using Unity.Cinemachine;

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
        

        private void Awake()
        {
                pointA = transform.Find("A");
                pointB = transform.Find("B");
                cinemachineBrain = FindAnyObjectByType<CinemachineBrain>();
        }

        private void OnTriggerEnter(Collider other)
        {
            if (!other.CompareTag("Player"))
                return;

            var controller = other.GetComponent<Character2D5Controller>();
            if (controller != null)
            {
                Debug.Log("Entered trigger");
                if (rotateOnTrigger)
                {
                    controller.GetComponent<Rigidbody>().linearVelocity = Vector3.zero;
                    // Set the correct rotation
                    controller.RotatePLayer(usingFirstState ? rotationA : rotationB);

                    // Fix the player's position to prevent sliding
                    controller.transform.position = usingFirstState ?
                        new Vector3(pointA.position.x, controller.transform.position.y, pointA.position.z)
                        : new Vector3(pointB.position.x, controller.transform.position.y, pointB.position.z);

                    controller.FreezePerpendicularAxis();
                }


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

                if (!oneWaySwitch)
                { 
                    usingFirstState = !usingFirstState; // Toggle state for next trigger
                }
            }
        }
    }
}
