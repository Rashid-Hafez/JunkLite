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
        [SerializeField, Tooltip("Optional explicit brain. If unassigned, it is resolved once when this scene-local rig initializes.")]
        private CinemachineBrain cameraBrain;

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

        [Header("Additional Level Cameras")]
        [SerializeField, Tooltip("Optional cameras used by level triggers. Core cameras are registered automatically.")]
        private List<CinemachineCamera> cameraList = new List<CinemachineCamera>();

        // Configured once and reused when a player spawns or respawns. This keeps
        // player binding explicit without performing scene-wide camera searches.
        private readonly List<CinemachineCamera> managedCameras = new List<CinemachineCamera>();
        private readonly HashSet<CinemachineCamera> managedCameraSet = new HashSet<CinemachineCamera>();
        private Dictionary<string, CinemachineCamera> cameras;

        // Current player reference for event subscription
        private PlayerCharacter currentPlayer;
        private PlayerLifecycle subscribedPlayerLifecycle;

        // Cached tracking target
        private Transform cachedTrackingTarget;
        private bool followEnabled = true;

        // The camera that zoom/follow operations target (updated when CameraSwitchTrigger fires)
        private CinemachineCamera activeCamera;

        // Captured on first RequestZoomOut - we restore to these when zooming back in
        private float defaultZoomValue;
        private bool defaultIsOrthographic;
        private Coroutine zoomCoroutine;

        public CinemachineCamera MainCamera => mainCamera;


        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                enabled = false;
                Destroy(this);
                return;
            }

            Instance = this;
            RebuildManagedCameraCache();
            InitializeCameras();
            activeCamera = mainCamera != null ? mainCamera : GetFirstManagedCamera();

            if (cameraBrain == null)
                cameraBrain = FindAnyObjectByType<CinemachineBrain>();
        }

        private void OnEnable()
        {
            SubscribeToPlayerLifecycle();

            PlayerCharacter player = PlayerLifecycle.Instance?.Player;
            if (player != null)
                ConnectToPlayer(player);
        }

        private void Start()
        {
            SubscribeToPlayerLifecycle();

            if (currentPlayer == null && PlayerLifecycle.Instance?.Player != null)
            {
                ConnectToPlayer(PlayerLifecycle.Instance.Player);
                return;
            }

            if (playerTransform != null)
            {
                cachedTrackingTarget = playerTransform;
                BindManagedCameras(playerTransform);
                ResetToSpawnCamera();
            }
        }

        private void OnDisable()
        {
            UnsubscribeFromPlayerLifecycle();
            UnsubscribeFromPlayer();
        }

        private void OnDestroy()
        {
            if (Instance == this)
                Instance = null;
        }

        private void SubscribeToPlayerLifecycle()
        {
            PlayerLifecycle lifecycle = PlayerLifecycle.Instance;
            if (subscribedPlayerLifecycle == lifecycle)
                return;

            UnsubscribeFromPlayerLifecycle();
            subscribedPlayerLifecycle = lifecycle;

            if (subscribedPlayerLifecycle != null)
                subscribedPlayerLifecycle.PlayerSpawned += ConnectToPlayer;
        }

        private void UnsubscribeFromPlayerLifecycle()
        {
            if (subscribedPlayerLifecycle == null)
                return;

            subscribedPlayerLifecycle.PlayerSpawned -= ConnectToPlayer;
            subscribedPlayerLifecycle = null;
        }

        public void ConnectToPlayer(PlayerCharacter character)
        {
            if (character == null)
            {
                Debug.LogWarning("[CameraManager] Cannot connect cameras to a null player.");
                return;
            }

            if (currentPlayer != character)
            {
                UnsubscribeFromPlayer();
                currentPlayer = character;
                currentPlayer.OnCameraFollowRequested += HandleCameraFollowRequested;
            }

            playerTransform = character.transform;
            cachedTrackingTarget = playerTransform;
            followEnabled = true;
            EnsureConfiguredCamerasRegistered();

            // Reset priorities and bind every configured/dynamically registered camera.
            // CameraSwitchTrigger cameras join this same registry through SetActiveCamera.
            foreach (CinemachineCamera camera in managedCameras)
            {
                if (camera != null)
                {
                    camera.Priority = 0;
                    BindCameraTarget(camera, playerTransform);
                }
            }

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
            CinemachineCamera target = spawnCamera != null
                ? spawnCamera
                : mainCamera != null
                    ? mainCamera
                    : GetFirstManagedCamera();

            if (target == null) return;

            RegisterCamera(target);
            BindCameraTarget(target, followEnabled ? playerTransform : null);
            target.gameObject.SetActive(true);

            CinemachineBrain brain = cameraBrain;
            if (brain == null)
            {
                brain = FindAnyObjectByType<CinemachineBrain>();
                cameraBrain = brain;
            }

            if (brain == null)
            {
                target.Prioritize();
                SetActiveCamera(target);
                return;
            }

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
            followEnabled = follow;

            if (activeCamera == null)
                return;

            if (follow)
            {
                BindCameraTarget(activeCamera, cachedTrackingTarget);
            }
            else
            {
                if (activeCamera.Target.TrackingTarget != null)
                    cachedTrackingTarget = activeCamera.Target.TrackingTarget;

                BindCameraTarget(activeCamera, null);
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
                CinemachineCamera target = cameras[cameraName];
                target.gameObject.SetActive(true);
                target.Prioritize();
                SetActiveCamera(target);
            }
        }

        /// <summary>
        /// Called by CameraSwitchTrigger (or anything else that changes the live Cinemachine camera)
        /// so zoom, follow-freeze, and parry effects target the correct camera.
        /// </summary>
        public void SetActiveCamera(CinemachineCamera cam)
        {
            if (cam == null) return;

            RegisterCamera(cam);
            BindCameraTarget(cam, followEnabled ? playerTransform : null);
            activeCamera = cam;
            defaultZoomValue = 0f;
        }

        public void SetPlayerTarget(CinemachineCamera camera, Transform player)
        {
            playerTransform = player;
            cachedTrackingTarget = player;

            if (camera != null)
            {
                RegisterCamera(camera);
                BindCameraTarget(camera, playerTransform);
            }
        }

        private void RebuildManagedCameraCache()
        {
            managedCameras.Clear();
            managedCameraSet.Clear();
            EnsureConfiguredCamerasRegistered();
        }

        private void EnsureConfiguredCamerasRegistered()
        {
            RegisterCamera(mainCamera);
            RegisterCamera(spawnCamera);
            RegisterCamera(deathCamera);

            foreach (CinemachineCamera camera in cameraList)
                RegisterCamera(camera);
        }

        private void RegisterCamera(CinemachineCamera camera)
        {
            if (camera == null || !managedCameraSet.Add(camera))
                return;

            managedCameras.Add(camera);
        }

        private CinemachineCamera GetFirstManagedCamera()
        {
            foreach (CinemachineCamera camera in managedCameras)
            {
                if (camera != null)
                    return camera;
            }

            return null;
        }

        private void BindManagedCameras(Transform target)
        {
            EnsureConfiguredCamerasRegistered();
            foreach (CinemachineCamera camera in managedCameras)
                BindCameraTarget(camera, target);
        }

        private static void BindCameraTarget(CinemachineCamera camera, Transform target)
        {
            if (camera != null)
                camera.Target.TrackingTarget = target;
        }
    }
}
