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

        [Header("Cameras")]
        [Tooltip("The first lane camera. Zoom and parry start from it until another camera goes live.")]
        [SerializeField] private CinemachineCamera mainCamera;
        [SerializeField, Tooltip("The camera to snap back to on respawn. If unassigned, falls back to mainCamera.")]
        private CinemachineCamera spawnCamera;
        [SerializeField, Tooltip("Optional. Found automatically if empty.")]
        private CinemachineBrain cameraBrain;
        [SerializeField, Tooltip("Other lane cameras (e.g. the ones Camera Switch Triggers use). They get the player as their target on spawn.")]
        private List<CinemachineCamera> cameraList = new List<CinemachineCamera>();
        [SerializeField, Tooltip("Only for scenes without a PlayerLifecycle. Normally set on spawn.")]
        private Transform playerTransform;

        [Header("Zoom (e.g. Phantom Strike)")]
        [Tooltip("When using Physical/Perspective: zoom-out Field of View in degrees (wider = more zoomed out). When Orthographic: zoom-out ortho size.")]
        [SerializeField] private float zoomOutValue = 55f;
        [SerializeField] private float zoomOutDuration = 0.25f;
        [SerializeField] private float zoomInDuration = 0.35f;

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

        // Configured once and reused when a player spawns or respawns. This keeps
        // player binding explicit without performing scene-wide camera searches.
        private readonly List<CinemachineCamera> managedCameras = new List<CinemachineCamera>();
        private readonly HashSet<CinemachineCamera> managedCameraSet = new HashSet<CinemachineCamera>();

        // Current player reference for event subscription
        private PlayerCharacter currentPlayer;
        private PlayerLifecycle subscribedPlayerLifecycle;

        // Cached tracking target
        private Transform cachedTrackingTarget;
        private bool followEnabled = true;

        // The live camera (top of the layer stack). Zoom and parry target it.
        private CinemachineCamera activeCamera;

        // Lane (CameraSwitchTrigger, LaneEntryTrigger, spawn) < Zone (CameraZone) < Cinematic (CameraTransition).
        private readonly CameraLayerStack<CinemachineCamera> layers = new CameraLayerStack<CinemachineCamera>();

        // Blend for the next brain transition into pendingBlendTarget, applied through
        // CinemachineCore.GetBlendOverride so the brain's authored DefaultBlend is never mutated.
        private CinemachineCamera pendingBlendTarget;
        private CinemachineBlendDefinition pendingBlend;
        private static readonly CinemachineBlendDefinition CutBlend =
            new CinemachineBlendDefinition(CinemachineBlendDefinition.Styles.Cut, 0f);

        // The camera that follows the player; cinematic shots are authored and never rebound.
        private CinemachineCamera FollowCamera => layers.Zone != null ? layers.Zone : layers.Lane;

        // Captured on first RequestZoomOut - we restore to these when zooming back in
        private float defaultZoomValue;
        private bool defaultIsOrthographic;
        private Coroutine zoomCoroutine;

        public CinemachineCamera MainCamera => mainCamera;
        /// <summary>The camera the manager made live (top layer). Shown in the dev console.</summary>
        public CinemachineCamera LiveCamera => activeCamera;
        public string LiveLayer => layers.CinematicCount > 0 ? "Cinematic" : layers.Zone != null ? "Zone" : "Lane";
        public bool IsBlending => cameraBrain != null && cameraBrain.IsBlending;


        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                enabled = false;
                Destroy(this);
                return;
            }

            Instance = this;
            EnsureConfiguredCamerasRegistered();
            activeCamera = mainCamera != null ? mainCamera : GetFirstManagedCamera();
            layers.Lane = activeCamera;

            if (cameraBrain == null)
                cameraBrain = FindAnyObjectByType<CinemachineBrain>();
        }

        private void OnEnable()
        {
            CinemachineCore.GetBlendOverride -= OverrideBlend;
            CinemachineCore.GetBlendOverride += OverrideBlend;
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
            CinemachineCore.GetBlendOverride -= OverrideBlend;
            UnsubscribeFromPlayerLifecycle();
            UnsubscribeFromPlayer();
        }

        private void OnDestroy()
        {
            CinemachineCore.GetBlendOverride -= OverrideBlend; // Static event: never outlive this manager.
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
            {
                subscribedPlayerLifecycle.PlayerSpawned += ConnectToPlayer;
                subscribedPlayerLifecycle.PlayerDied += HandlePlayerDied;
            }
        }

        private void UnsubscribeFromPlayerLifecycle()
        {
            if (subscribedPlayerLifecycle == null)
                return;

            subscribedPlayerLifecycle.PlayerSpawned -= ConnectToPlayer;
            subscribedPlayerLifecycle.PlayerDied -= HandlePlayerDied;
            subscribedPlayerLifecycle = null;
        }

        // Zones re-apply themselves on PlayerSpawned when the player respawns inside one,
        // so overlays are dropped on death rather than on spawn (event order is not guaranteed).
        private void HandlePlayerDied(PlayerCharacter player)
        {
            if (layers.Zone == null && layers.CinematicCount == 0)
                return;

            layers.ClearOverlays();
            RefreshLiveCamera(null);
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
            // CameraSwitchTrigger cameras join this same registry through SetLaneCamera.
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
        /// Snaps instantly to the designated spawn camera with a cut (no blend, no damping catch-up).
        /// A zone the player respawned inside stays on top of it.
        /// </summary>
        private void ResetToSpawnCamera()
        {
            CinemachineCamera target = spawnCamera != null
                ? spawnCamera
                : mainCamera != null
                    ? mainCamera
                    : GetFirstManagedCamera();

            if (target == null) return;

            RegisterCamera(target);
            BindCameraTarget(target, followEnabled ? playerTransform : null);
            layers.Lane = target;
            RefreshLiveCamera(CutBlend, forcePrioritize: true);

            if (activeCamera != null)
                activeCamera.PreviousStateIsValid = false;
        }

        /// <summary>
        /// Handles camera follow requests from the player.
        /// When disabled, camera freezes at current position by setting TrackingTarget to null.
        /// </summary>
        private void HandleCameraFollowRequested(bool follow)
        {
            followEnabled = follow;

            CinemachineCamera followCamera = FollowCamera;
            if (followCamera == null)
                return;

            if (follow)
            {
                BindCameraTarget(followCamera, cachedTrackingTarget);
            }
            else
            {
                if (followCamera.Target.TrackingTarget != null)
                    cachedTrackingTarget = followCamera.Target.TrackingTarget;

                BindCameraTarget(followCamera, null);
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

        /// <summary>Sets the base (lane) camera. A zone or cinematic above it stays live until released.</summary>
        public void SetLaneCamera(CinemachineCamera cam, CinemachineBlendDefinition? blend = null)
        {
            if (cam == null) return;

            RegisterCamera(cam);
            BindCameraTarget(cam, followEnabled ? playerTransform : null);
            layers.Lane = cam;
            RefreshLiveCamera(blend, forcePrioritize: true);
        }

        /// <summary>True once <paramref name="cam"/> is the live camera and no blend is running.</summary>
        public bool IsSettledOn(CinemachineCamera cam)
        {
            return cam != null && cameraBrain != null &&
                   ReferenceEquals(cameraBrain.ActiveVirtualCamera, cam) && !cameraBrain.IsBlending;
        }

        /// <summary>Lane switch with a cut and no damping catch-up, for a teleport.</summary>
        public void CutToLaneCamera(CinemachineCamera cam)
        {
            SetLaneCamera(cam, CutBlend);
            if (activeCamera != null)
                activeCamera.PreviousStateIsValid = false;
        }

        /// <summary>Lane switch with the brain's default blend style over <paramref name="blendTime"/> seconds.</summary>
        public void SetLaneCamera(CinemachineCamera cam, float blendTime)
        {
            CinemachineBlendDefinition.Styles style = cameraBrain != null
                ? cameraBrain.DefaultBlend.Style
                : CinemachineBlendDefinition.Styles.EaseInOut;
            SetLaneCamera(cam, new CinemachineBlendDefinition(style, blendTime));
        }

        /// <summary>Room framing camera (CameraZone). It follows the player like a lane camera.</summary>
        public void SetZoneCamera(CinemachineCamera cam, CinemachineBlendDefinition blend)
        {
            if (cam == null) return;

            RegisterCamera(cam);
            BindCameraTarget(cam, followEnabled ? playerTransform : null);
            layers.SetZone(cam);
            RefreshLiveCamera(blend);
        }

        /// <summary>Releases the zone only if <paramref name="cam"/> still owns it.</summary>
        public void ClearZoneCamera(CinemachineCamera cam, CinemachineBlendDefinition blend)
        {
            if (layers.ClearZone(cam))
                RefreshLiveCamera(blend);
        }

        /// <summary>
        /// Puts an authored shot on top of every other layer until the returned lease is disposed.
        /// The shot's targets are not rebound. Death clears it; a later Dispose is then a no-op.
        /// </summary>
        public IDisposable PushCinematic(CinemachineCamera shot, CinemachineBlendDefinition blendIn,
            CinemachineBlendDefinition blendOut)
        {
            if (shot == null)
                return new CameraLayerStack<CinemachineCamera>.Lease(0, null);

            int id = layers.PushCinematic(shot);
            RefreshLiveCamera(blendIn);
            return new CameraLayerStack<CinemachineCamera>.Lease(id, released =>
            {
                if (this != null && layers.ReleaseCinematic(released))
                    RefreshLiveCamera(blendOut);
            });
        }

        // Makes the top layer live. The blend (null = brain default) applies only to the transition it starts.
        private void RefreshLiveCamera(CinemachineBlendDefinition? blend, bool forcePrioritize = false)
        {
            CinemachineCamera top = layers.Top;
            if (top == null) return;

            bool changed = top != activeCamera;
            if (!changed && !forcePrioritize) return;

            if (changed)
            {
                pendingBlendTarget = blend.HasValue ? top : null;
                if (blend.HasValue) pendingBlend = blend.Value;
            }

            top.gameObject.SetActive(true);
            top.Prioritize();
            activeCamera = top;
            if (changed) defaultZoomValue = 0f;
        }

        private CinemachineBlendDefinition OverrideBlend(ICinemachineCamera from, ICinemachineCamera to,
            CinemachineBlendDefinition defaultBlend, UnityEngine.Object owner)
        {
            if (pendingBlendTarget == null || !ReferenceEquals(to, pendingBlendTarget) || owner != cameraBrain)
                return defaultBlend;

            pendingBlendTarget = null;
            return pendingBlend;
        }

        private void EnsureConfiguredCamerasRegistered()
        {
            RegisterCamera(mainCamera);
            RegisterCamera(spawnCamera);

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
