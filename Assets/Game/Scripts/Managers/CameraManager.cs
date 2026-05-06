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
        [SerializeField, Tooltip("The camera to snap back to on respawn. If unassigned, falls back to mainCamera.")]
        private CinemachineCamera spawnCamera;

        [Header("Settings")]
        [SerializeField] private Transform playerTransform;

        [Header("Zoom (e.g. Phantom Strike)")]
        [Tooltip("When using Physical/Perspective: zoom-out Field of View in degrees (wider = more zoomed out). When Orthographic: zoom-out ortho size.")]
        [SerializeField] private float zoomOutValue = 55f;
        [SerializeField] private float parryZoomOut = 40f;

        [Header("Parry Zoom / SlowMo")]
        [Tooltip("Target zoom value to use when parry zooms IN (smaller = closer for FOV).")]
        [SerializeField] private float parryZoomInTarget = 30f;
        [Tooltip("Duration (s) to tween into parry zoom (uses normal time scale).")]
        [SerializeField] private float parryZoomInDuration = 0.12f;
        [Tooltip("How long to hold the parry zoom/slow-mo in real seconds (unscaled).")]
        [SerializeField] private float parryZoomHoldRealtime = 0.06f;
        [Tooltip("Duration (s) to tween back to normal zoom after parry (uses normal time scale).")]
        [SerializeField] private float parryZoomReturnDuration = 0.2f;
        [Tooltip("Time.timeScale to use during parry slow-motion (0..1).")]
        [SerializeField] private float parrySlowMoScale = 0.12f;
        [Tooltip("How long (real seconds) to stay in slow-motion during parry (unscaled).")]
        [SerializeField] private float parrySlowMoRealtimeDuration = 0.14f;
        [SerializeField] private float zoomOutDuration = 0.25f;
        [SerializeField] private float zoomInDuration = 0.35f;

        // Camera dictionary for easy access
        [SerializeField] private List<CinemachineCamera> cameraList = new List<CinemachineCamera>();
        private Dictionary<string, CinemachineCamera> cameras;

        // Current player reference for event subscription
        private PlayerCharacter currentPlayer;

        // Cached tracking target
        private Transform cachedTrackingTarget;

        // The camera that zoom/follow operations target (updated when CameraSwitchTrigger fires)
        private CinemachineCamera activeCamera;

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
            activeCamera = mainCamera;

            InitializeCameras();

            if (GameManager.Instance != null)
                GameManager.Instance.OnPlayerSpawned += ConnectToPlayer;
        }

        private void Start()
        {
            if (GameManager.Instance?.Player != null)
            {
                ConnectToPlayer(GameManager.Instance.Player);
                return;
            }

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

        public void ConnectToPlayer(PlayerCharacter character)
        {
            UnsubscribeFromPlayer();

            currentPlayer = character;
            playerTransform = character.gameObject.transform;

            // Set all cameras to low priority and point at the new player transform
            foreach (CinemachineCamera camera in cameraList)
            {
                if (camera != null)
                {
                    camera.Priority = 0;
                    SetPlayerTarget(camera, playerTransform);
                }
            }

            // Subscribe to camera follow requests
            currentPlayer.OnCameraFollowRequested += HandleCameraFollowRequested;

            // Snap back to the spawn camera instantly on every (re)spawn
            ResetToSpawnCamera();
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
        /// Snaps instantly to the designated spawn camera with no blend.
        /// Blend time is restored on the next frame so future transitions still animate.
        /// </summary>
        public void ResetToSpawnCamera()
        {
            CinemachineCamera target = spawnCamera != null ? spawnCamera : mainCamera;
            if (target == null) return;

            CinemachineBrain brain = FindAnyObjectByType<CinemachineBrain>();
            if (brain == null) return;

            float previousBlend = brain.DefaultBlend.Time;
            brain.DefaultBlend.Time = 0f;

            target.Prioritize();
            SetActiveCamera(target);

            StartCoroutine(RestoreBlendNextFrame(brain, previousBlend));
        }

        private IEnumerator RestoreBlendNextFrame(CinemachineBrain brain, float blendTime)
        {
            yield return null;
            if (brain != null)
                brain.DefaultBlend.Time = blendTime;
        }

        /// <summary>
        /// Handles camera follow requests from the player.
        /// When disabled, camera freezes at current position by setting TrackingTarget to null.
        /// </summary>
        private void HandleCameraFollowRequested(bool follow)
        {
            if (activeCamera == null)
                return;

            if (follow)
            {
                activeCamera.Target.TrackingTarget = cachedTrackingTarget;
                Debug.Log("[CameraManager] Camera follow enabled");
            }
            else
            {
                cachedTrackingTarget = activeCamera.Target.TrackingTarget;
                activeCamera.Target.TrackingTarget = null;
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
            if (activeCamera == null)
                return;
            var lens = activeCamera.Lens;
            if (customZoomOutValue <= 0f)
                customZoomOutValue = zoomOutValue;
            defaultIsOrthographic = lens.Orthographic;
            defaultZoomValue = defaultIsOrthographic ? lens.OrthographicSize : lens.FieldOfView;
            if (zoomCoroutine != null)
                StopCoroutine(zoomCoroutine);
            zoomCoroutine = StartCoroutine(CoZoom(customZoomOutValue, zoomOutDuration));
        }

        /// <summary>Zoom back to default (captured at last RequestZoomOut) smoothly.</summary>
        public void RequestZoomBackIn()
        {
            if (activeCamera == null)
                return;
            if (defaultZoomValue <= 0f)
            {
                var lens = activeCamera.Lens;
                defaultZoomValue = lens.Orthographic ? lens.OrthographicSize : lens.FieldOfView;
                defaultIsOrthographic = lens.Orthographic;
                return;
            }
            if (zoomCoroutine != null)
                StopCoroutine(zoomCoroutine);
            zoomCoroutine = StartCoroutine(CoZoom(defaultZoomValue, zoomInDuration));
        }

        // ------------------------------------------------------------------
        // Parry camera effect helpers
        // ------------------------------------------------------------------

        /// <summary>
        /// Simple camera effect triggered when the player successfully parries.
        /// Performs a parry effect: quick slow-motion + zoom-in, hold, then restore.
        /// </summary>
        public void DoParryCameraEffect()
        {
            if (zoomCoroutine != null)
                StopCoroutine(zoomCoroutine);
            StartCoroutine(CoParryEffect());
        }

        private IEnumerator CoParryEffect()
        {
            if (activeCamera == null)
                yield break;

            var lens = activeCamera.Lens;
            defaultIsOrthographic = lens.Orthographic;
            defaultZoomValue = defaultIsOrthographic ? lens.OrthographicSize : lens.FieldOfView;

            if (zoomCoroutine != null) StopCoroutine(zoomCoroutine);
            zoomCoroutine = StartCoroutine(CoZoom(parryZoomInTarget, parryZoomInDuration));

            yield return new WaitForSecondsRealtime(parrySlowMoRealtimeDuration);

            float origTime = Time.timeScale;
            float origFixed = Time.fixedDeltaTime;
            Time.timeScale = Mathf.Clamp01(parrySlowMoScale);
            Time.fixedDeltaTime = origFixed * Time.timeScale;

            if (parryZoomHoldRealtime > 0f)
                yield return new WaitForSecondsRealtime(parryZoomHoldRealtime);

            Time.timeScale = origTime;
            Time.fixedDeltaTime = origFixed;

            if (zoomCoroutine != null) StopCoroutine(zoomCoroutine);
            zoomCoroutine = StartCoroutine(CoZoom(defaultZoomValue, parryZoomReturnDuration));
        }

        private IEnumerator CoZoomBackAfterDelay(float delay)
        {
            yield return new WaitForSeconds(delay);
            RequestZoomBackIn();
        }

        private IEnumerator CoZoom(float targetValue, float duration)
        {
            if (activeCamera == null || duration <= 0f)
            {
                zoomCoroutine = null;
                yield break;
            }

            var lens = activeCamera.Lens;
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
                activeCamera.Lens = lens;
                yield return null;
            }

            if (ortho)
                lens.OrthographicSize = targetValue;
            else
                lens.FieldOfView = Mathf.Clamp(targetValue, 0.01f, 179f);
            activeCamera.Lens = lens;
            zoomCoroutine = null;
        }

        private void InitializeCameras()
        {
            cameras = new Dictionary<string, CinemachineCamera>
            {
                { "Main", mainCamera },
                { "Death", deathCamera }
            };
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
                foreach (var camera in cameras.Values)
                {
                    if (camera != null)
                        camera.gameObject.SetActive(false);
                }

                cameras[cameraName].gameObject.SetActive(true);
            }
        }

        /// <summary>
        /// Called by CameraSwitchTrigger (or anything else that changes the live Cinemachine camera)
        /// so zoom, follow-freeze, and parry effects target the correct camera.
        /// </summary>
        public void SetActiveCamera(CinemachineCamera cam)
        {
            if (cam == null) return;
            activeCamera = cam;
            defaultZoomValue = 0f;
        }

        public void SetPlayerTarget(CinemachineCamera camera, Transform player)
        {
            playerTransform = player;
            cachedTrackingTarget = player;

            if (camera != null)
                camera.Target.TrackingTarget = playerTransform;
        }
    }
}