using UnityEngine;
using System.Collections.Generic;
using Unity.Cinemachine;
using System;

namespace junklite
{
    [DefaultExecutionOrder(3)]
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

        // Current player reference for event subscription
        private PlayerCharacter currentPlayer;

        // Cached tracking target
        private Transform cachedTrackingTarget;


        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;

            InitializeCameras();
            
            GameManager.Instance.OnPlayerSpawned += ConnectToPlayer;
        }

        private void Start()
        {

            // Set player as follow target and activate main camera
            if (mainCamera != null && playerTransform != null)
            {
                mainCamera.Target.TrackingTarget = playerTransform;
                SwitchToMainCamera();
            }
        }

        private void OnDestroy()
        {
            if (GameManager.Instance != null)
                GameManager.Instance.OnPlayerSpawned -= ConnectToPlayer;

            UnsubscribeFromPlayer();
        }

        private void ConnectToPlayer(PlayerCharacter character)
        {
            UnsubscribeFromPlayer();

            currentPlayer = character;
            playerTransform = character.gameObject.transform;

            // Subscribe to camera follow requests
            currentPlayer.OnCameraFollowRequested += HandleCameraFollowRequested;

            SetPlayerTarget(playerTransform);
        }

        private void UnsubscribeFromPlayer()
        {
            if (currentPlayer != null)
            {
                currentPlayer.OnCameraFollowRequested -= HandleCameraFollowRequested;
                currentPlayer = null;
            }
        }

        /// <summary>
        /// Handles camera follow requests from the player.
        /// When disabled, camera freezes at current position by setting TrackingTarget to null.
        /// </summary>
        private void HandleCameraFollowRequested(bool follow)
        {
            if (mainCamera == null)
                return;

            if (follow)
            {
                // Resume following player
                mainCamera.Target.TrackingTarget = cachedTrackingTarget;
                Debug.Log("[CameraManager] Camera follow enabled");
            }
            else
            {
                // Cache current target and set to null to freeze
                cachedTrackingTarget = mainCamera.Target.TrackingTarget;
                mainCamera.Target.TrackingTarget = null;
                Debug.Log("[CameraManager] Camera follow disabled (frozen)");
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
            cachedTrackingTarget = player;

            if (mainCamera != null)
                mainCamera.Target.TrackingTarget = playerTransform;
        }
    }
}