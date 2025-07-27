using UnityEngine;
using System.Collections.Generic;
using Unity.Cinemachine;

namespace junklite
{
    public class CameraManager : MonoBehaviour
    {
        public static CameraManager Instance { get; private set; }

        [Header("Camera References")]
        [SerializeField] private CinemachineCamera mainCamera;
        [SerializeField] private CinemachineCamera deathCamera;

        [Header("Settings")]
        [SerializeField] private Transform playerTransform;

        // Camera dictionary for easy access
        private Dictionary<string, CinemachineCamera> cameras;

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;

            InitializeCameras();
        }

        private void Start()
        {
            // Find player if not assigned
            if (playerTransform == null)
            {
                GameObject player = GameObject.FindGameObjectWithTag("Player");
                if (player != null)
                    playerTransform = player.transform;
            }

            // Set player as follow target and activate main camera
            if (mainCamera != null && playerTransform != null)
            {
                mainCamera.Follow = playerTransform;
                SwitchToMainCamera();
            }
        }

        private void InitializeCameras()
        {
            cameras = new Dictionary<string, CinemachineCamera>();

            if (mainCamera != null)
                cameras["Main"] = mainCamera;
            if (deathCamera != null)
                cameras["Death"] = deathCamera;

            // Set all cameras to low priority
            foreach (var camera in cameras.Values)
            {
                if (camera != null)
                    camera.Priority = 0;
            }
        }

        
        public void SwitchToMainCamera()
        {
            SwitchToCamera("Main");
        }

        /// <summary>Switch to death camera</summary>
        public void SwitchToDeathCamera()
        {
            SwitchToCamera("Death");
        }

      
        public void SwitchToCamera(string cameraName)
        {
            if (cameras.ContainsKey(cameraName) && cameras[cameraName] != null)
            {
                // Disable all cameras
                foreach (var camera in cameras.Values)
                {
                    if (camera != null)
                        camera.gameObject.SetActive(false);
                }

                // Enable target camera
                cameras[cameraName].gameObject.SetActive(true);
            }
        }

       
        public void SetPlayerTarget(Transform player)
        {
            playerTransform = player;
            if (mainCamera != null)
                mainCamera.Follow = playerTransform;
        }
    }
}