using System;
using Unity.Cinemachine;
using UnityEngine;

namespace junklite
{
    /// <summary>
    /// Cinematic hand-off between rooms. It starts when the player enters this object's trigger box (Start),
    /// or, in Script mode, when a caller such as a LaneEntryTrigger calls Play. The camera blends slowly to
    /// the authored Shot that frames the next room and holds there. The shot releases when the player reaches
    /// Arrival, retreats out of Start (box mode only), dies, or Max Hold runs out, then the camera blends back
    /// to the zone or lane camera underneath.
    /// </summary>
    [RequireComponent(typeof(BoxCollider))]
    public class CameraTransition : MonoBehaviour
    {
        public enum StartMode
        {
            EnterStartBox,
            Script
        }

        [Tooltip("Enter Start Box: plays when the player walks into this box. Script: a caller (e.g. a LaneEntryTrigger on Interact) plays it, and the box is unused.")]
        [SerializeField] private StartMode startOn = StartMode.EnterStartBox;

        [Header("Shot")]
        [Tooltip("Authored shot of the next room. Keep it inactive and at the same priority as the lane cameras.")]
        [SerializeField] private CinemachineCamera shot;
        [Tooltip("Sets the shot's Look At to the player, so a Rotation Composer pans with them.")]
        [SerializeField] private bool lookAtPlayer = true;

        [Header("Arrival")]
        [Tooltip("Box inside the next room. Only its shape is used; the collider is disabled at runtime.")]
        [SerializeField] private BoxCollider arrival;

        [Header("Timing")]
        [SerializeField] private CinemachineBlendDefinition blendIn =
            new CinemachineBlendDefinition(CinemachineBlendDefinition.Styles.EaseInOut, 2.5f);
        [SerializeField] private CinemachineBlendDefinition blendOut =
            new CinemachineBlendDefinition(CinemachineBlendDefinition.Styles.EaseInOut, 1.2f);
        [Tooltip("Seconds to hold before giving up. 0 waits for the player indefinitely.")]
        [SerializeField, Min(0f)] private float maxHold;
        [Tooltip("Release if the player leaves the Start box on the side away from Arrival.")]
        [SerializeField] private bool releaseOnRetreat = true;
        [Tooltip("Play once per life. Death re-arms it.")]
        [SerializeField] private bool oneShot = true;

        private IDisposable lease;
        private Transform player;
        private bool played;
        private float startedAt;
        private PlayerLifecycle subscribedLifecycle;

        public StartMode StartOn => startOn;
        public CinemachineCamera Shot => shot;
        public BoxCollider Arrival => arrival;
        public bool IsActive => lease != null;
        /// <summary>The shot is live and its blend-in has finished.</summary>
        public bool IsSettled => lease != null && CameraManager.Instance != null && CameraManager.Instance.IsSettledOn(shot);

        private void Awake()
        {
            BoxCollider start = GetComponent<BoxCollider>();
            start.isTrigger = true;
            start.enabled = startOn == StartMode.EnterStartBox;
            if (arrival != null) arrival.enabled = false;
            // An enabled camera jumps to the top of its priority peers and would steal the view at load.
            if (shot != null) shot.gameObject.SetActive(false);
        }

        private void OnEnable() => Rebind();
        private void Start() => Rebind();

        private void OnDisable()
        {
            Unbind();
            Release();
        }

        private void OnTriggerEnter(Collider other)
        {
            if (startOn != StartMode.EnterStartBox) return;
            if (!other.CompareTag("Player") || other.GetComponent<Character2D5Controller>() == null) return;
            Play(other.transform);
        }

        /// <summary>Starts the shot on <paramref name="playerTransform"/>. False if it is already playing or spent.</summary>
        public bool Play(Transform playerTransform)
        {
            if (lease != null || shot == null || playerTransform == null || CameraManager.Instance == null) return false;
            if (oneShot && played) return false;

            player = playerTransform;
            if (lookAtPlayer)
            {
                shot.Target.CustomLookAtTarget = true;
                shot.Target.LookAtTarget = player;
            }

            played = true;
            startedAt = Time.time;
            lease = CameraManager.Instance.PushCinematic(shot, blendIn, blendOut);
            return true;
        }

        private void OnTriggerExit(Collider other)
        {
            if (lease == null || !releaseOnRetreat || other.transform != player) return;
            if (IsRetreating(player.position))
                Release();
        }

        private void Update()
        {
            if (lease == null) return;

            if (player == null || !player.gameObject.activeInHierarchy || IsInArrival(player.position) ||
                (maxHold > 0f && Time.time - startedAt >= maxHold))
                Release();
        }

        private bool IsInArrival(Vector3 worldPosition)
        {
            if (arrival == null) return false;
            Vector3 local = arrival.transform.InverseTransformPoint(worldPosition) - arrival.center;
            Vector3 half = arrival.size * 0.5f;
            return Mathf.Abs(local.x) <= half.x && Mathf.Abs(local.y) <= half.y && Mathf.Abs(local.z) <= half.z;
        }

        // Leaving Start toward Arrival keeps the hold; only leaving on the far side counts as a retreat.
        private bool IsRetreating(Vector3 worldPosition)
        {
            if (arrival == null) return true;
            BoxCollider start = GetComponent<BoxCollider>();
            Vector3 startCenter = transform.TransformPoint(start.center);
            Vector3 toArrival = arrival.transform.TransformPoint(arrival.center) - startCenter;
            return Vector3.Dot(worldPosition - startCenter, toArrival) < 0f;
        }

        /// <summary>Drops the shot (blends out to the zone/lane camera). Safe when not active.</summary>
        public void Release()
        {
            IDisposable current = lease;
            lease = null;
            player = null;
            current?.Dispose();
        }

        // The CameraManager has already dropped the shot; this only re-arms the trigger.
        private void HandlePlayerDied(PlayerCharacter _)
        {
            Release();
            played = false;
        }

        #region Lifecycle binding

        private void Rebind()
        {
            PlayerLifecycle lifecycle = PlayerLifecycle.Instance;
            if (lifecycle == subscribedLifecycle) return;
            Unbind();
            subscribedLifecycle = lifecycle;
            if (subscribedLifecycle != null) subscribedLifecycle.PlayerDied += HandlePlayerDied;
        }

        private void Unbind()
        {
            if (subscribedLifecycle == null) return;
            subscribedLifecycle.PlayerDied -= HandlePlayerDied;
            subscribedLifecycle = null;
        }

        #endregion

        private void OnDrawGizmos()
        {
            BoxCollider start = GetComponent<BoxCollider>();
            if (start != null && startOn == StartMode.EnterStartBox)
            {
                Gizmos.color = new Color(0f, 0.9f, 1f, 0.15f);
                Gizmos.matrix = transform.localToWorldMatrix;
                Gizmos.DrawCube(start.center, start.size);
            }

            if (arrival != null)
            {
                Gizmos.color = new Color(0.2f, 1f, 0.3f, 0.15f);
                Gizmos.matrix = arrival.transform.localToWorldMatrix;
                Gizmos.DrawCube(arrival.center, arrival.size);
            }

            Gizmos.matrix = Matrix4x4.identity;
            if (shot != null)
            {
                Gizmos.color = new Color(0f, 0.9f, 1f, 0.9f);
                Gizmos.DrawLine(transform.position, shot.transform.position);
                if (arrival != null)
                    Gizmos.DrawLine(shot.transform.position, arrival.transform.TransformPoint(arrival.center));
            }
        }
    }
}
