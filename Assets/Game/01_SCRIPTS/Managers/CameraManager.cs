using System;
using System.Collections;
using System.Collections.Generic;
using Unity.Cinemachine;
using UnityEngine;

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

        [Header("Zoom (e.g. Phantom Strike)")]
        [Tooltip("When using Physical/Perspective: zoom-out Field of View in degrees (wider = more zoomed out). When Orthographic: zoom-out ortho size.")]
        [SerializeField] private float zoomOutValue = 55f;
        [SerializeField] private float zoomOutDuration = 0.25f;
        [SerializeField] private float zoomInDuration = 0.35f;

        // Camera dictionary for easy access
        private Dictionary<string, CinemachineCamera> cameras;

        // Current player reference for event subscription
        private PlayerCharacter currentPlayer;

        // Cached tracking target
        private Transform cachedTrackingTarget;

        // Captured on first RequestZoomOut - we restore to these when zooming back in
        private float defaultZoomValue;
        private bool defaultIsOrthographic;
        private Coroutine zoomCoroutine;


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

        /// <summary>Zoom out smoothly while still following the player. Uses FOV for Physical/Perspective, OrthographicSize for Orthographic. Call RequestZoomBackIn when done.</summary>
        public void RequestZoomOut()
        {
            RequestZoomOut(zoomOutValue);
        }

        /// <summary>Zoom out to a custom value: Field of View in degrees when using Physical/Perspective (wider FOV = zoom out), or ortho size when Orthographic. Pass 0 to use default zoom-out value.</summary>
        public void RequestZoomOut(float customZoomOutValue)
        {
            if (mainCamera == null)
                return;
            var lens = mainCamera.Lens;
            if (customZoomOutValue <= 0f)
                customZoomOutValue = zoomOutValue;
            // Capture current zoom so we can restore it when zooming back in
            defaultIsOrthographic = lens.Orthographic;
            defaultZoomValue = defaultIsOrthographic ? lens.OrthographicSize : lens.FieldOfView;
            if (zoomCoroutine != null)
                StopCoroutine(zoomCoroutine);
            zoomCoroutine = StartCoroutine(CoZoom(customZoomOutValue, zoomOutDuration));
        }

        /// <summary>Zoom back to default (captured at last RequestZoomOut) smoothly.</summary>
        public void RequestZoomBackIn()
        {
            if (mainCamera == null)
                return;
            if (defaultZoomValue <= 0f)
            {
                var lens = mainCamera.Lens;
                defaultZoomValue = lens.Orthographic ? lens.OrthographicSize : lens.FieldOfView;
                defaultIsOrthographic = lens.Orthographic;
                return;
            }
            if (zoomCoroutine != null)
                StopCoroutine(zoomCoroutine);
            zoomCoroutine = StartCoroutine(CoZoom(defaultZoomValue, zoomInDuration));
        }

        private IEnumerator CoZoom(float targetValue, float duration)
        {
            if (mainCamera == null || duration <= 0f)
            {
                zoomCoroutine = null;
                yield break;
            }

            var lens = mainCamera.Lens;
            bool ortho = lens.Orthographic;
            float start = ortho ? lens.OrthographicSize : lens.FieldOfView;
            float elapsed = 0f;

            while (elapsed < duration)
            {
                elapsed += Time.deltaTime;
                float t = Mathf.Clamp01(elapsed / duration);
                t = t * t * (3f - 2f * t); // smoothstep
                float value = Mathf.Lerp(start, targetValue, t);
                if (ortho)
                    lens.OrthographicSize = value;
                else
                    lens.FieldOfView = Mathf.Clamp(value, 0.01f, 179f);
                mainCamera.Lens = lens;
                yield return null;
            }

            if (ortho)
                lens.OrthographicSize = targetValue;
            else
                lens.FieldOfView = Mathf.Clamp(targetValue, 0.01f, 179f);
            mainCamera.Lens = lens;
            zoomCoroutine = null;
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