using UnityEngine;
using UnityEngine.Events;
using Unity.Cinemachine;

namespace junklite
{
    public class CameraSwitchTrigger : MonoBehaviour
    {
        [Header("Rotation Settings")]
        public float rotationA; // first rotation (Y-axis)
        public float rotationB; // second rotation (Y-axis)

        [Header("Camera Settings")]
        public CinemachineCamera cameraA;
        public CinemachineCamera cameraB;

        [Header("Teleport Points")]
        private Transform pointA;
        private Transform pointB;

        private bool usingFirstState = false;

        private void Awake()
        {
                pointA = transform.Find("A");
                pointB = transform.Find("B");
        }

        private void OnTriggerEnter(Collider other)
        {
            if (!other.CompareTag("Player"))
                return;

            var controller = other.GetComponent<Character2D5Controller>();
            if (controller != null)
            {
                controller.GetComponent<Rigidbody>().linearVelocity = Vector3.zero;
                // Set the correct rotation
                controller.RotatePLayer(usingFirstState ? rotationA : rotationB);

                // Fix the player's position to prevent sliding
                controller.transform.position = usingFirstState? 
                    new Vector3(pointA.position.x, controller.transform.position.y, pointA.position.z) 
                    : new Vector3(pointB.position.x, controller.transform.position.y, pointB.position.z);

                // Toggle cameras
                if (cameraA != null && cameraB != null)
                {
                    if (usingFirstState)
                    {
                        cameraA.Prioritize();
                        cameraA.transform.Find("Particles").gameObject.SetActive(true);
                        cameraB.transform.Find("Particles").gameObject.SetActive(false);
                    }
                    else
                    {
                        cameraB.Prioritize();
                        cameraB.transform.Find("Particles").gameObject.SetActive(true);
                        cameraA.transform.Find("Particles").gameObject.SetActive(false);
                    }
                }

                // Flip the state for next time
                usingFirstState = !usingFirstState;
            }
        }
    }
}
