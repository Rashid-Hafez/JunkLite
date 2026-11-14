using System.Collections.Generic;
using UnityEngine;

namespace junklite
{
    /// <summary>
    /// Lives on the rendering camera (the CinemachineBrain object). A few times per second it sweeps a
    /// sphere from the camera to the player against CameraOcclusion volumes and tells each CameraOccluder
    /// hit to get out of the way. The sweep includes volumes the camera starts inside, so a camera that
    /// clips into a wall clears it too. Allocation-free after warm-up.
    /// </summary>
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(1000)] // After CinemachineBrain has placed the camera this frame.
    public class CameraOcclusionFader : MonoBehaviour
    {
        [SerializeField] private LayerMask occlusionMask;
        [Tooltip("Seconds between sweeps (unscaled).")]
        [SerializeField, Min(0f)] private float tickInterval = 0.05f;
        [Tooltip("Sweep radius: wider keeps the player's silhouette clear, not just their center.")]
        [SerializeField, Min(0.01f)] private float castRadius = 0.4f;
        [Tooltip("An occluder stays out of the way this long after the last hit, so edges don't flicker.")]
        [SerializeField, Min(0f)] private float linger = 0.25f;
        [Tooltip("Fallback target offset when the player has no collider.")]
        [SerializeField] private Vector3 targetOffset = new Vector3(0f, 1f, 0f);
        [SerializeField, Range(4, 64)] private int maxHits = 16;

        private RaycastHit[] hits;
        private readonly Dictionary<Collider, CameraOccluder> occluderByCollider = new Dictionary<Collider, CameraOccluder>();
        private readonly Dictionary<CameraOccluder, float> lastSeen = new Dictionary<CameraOccluder, float>();
        private readonly List<CameraOccluder> occluding = new List<CameraOccluder>();
        private readonly List<CameraOccluder> animating = new List<CameraOccluder>();

        private Camera viewCamera;
        private bool occlusionCullingSuspended;
        private bool occlusionCullingDefault;
        private Transform target;
        private Collider targetCollider;
        private PlayerLifecycle subscribedLifecycle;
        private float nextTick;

        private void Reset()
        {
            occlusionMask = LayerMask.GetMask(CameraOccluder.LayerName);
        }

        private void Awake()
        {
            hits = new RaycastHit[maxHits];
            viewCamera = GetComponent<Camera>();
            if (viewCamera != null) occlusionCullingDefault = viewCamera.useOcclusionCulling;
        }

        private void OnEnable() => Rebind();

        private void Start()
        {
            Rebind();
            if (target == null && subscribedLifecycle != null && subscribedLifecycle.Player != null)
                SetTarget(subscribedLifecycle.Player);
        }

        private void OnDisable()
        {
            Unbind();
            ReleaseAll();
        }

        private void LateUpdate()
        {
            float now = Time.unscaledTime;
            if (target != null && now >= nextTick)
            {
                nextTick = now + tickInterval;
                Sweep(now);
            }

            float dt = Time.unscaledDeltaTime;
            for (int i = animating.Count - 1; i >= 0; i--)
            {
                CameraOccluder occluder = animating[i];
                if (occluder == null || !occluder.isActiveAndEnabled || !occluder.Step(dt))
                    animating.RemoveAt(i);
            }

            SuspendOcclusionCulling(occluding.Count > 0 || animating.Count > 0);
        }

        // Baked occlusion culling still treats hidden walls as solid and would cull the player and the
        // room behind them, so it is off only while something is hidden or fading.
        private void SuspendOcclusionCulling(bool suspend)
        {
            if (viewCamera == null || suspend == occlusionCullingSuspended) return;
            occlusionCullingSuspended = suspend;
            viewCamera.useOcclusionCulling = !suspend && occlusionCullingDefault;
        }

        private void Sweep(float now)
        {
            Vector3 origin = transform.position;
            Vector3 end = targetCollider != null && targetCollider.enabled
                ? targetCollider.bounds.center
                : target.position + targetOffset;
            Vector3 toTarget = end - origin;
            float distance = toTarget.magnitude;

            if (distance > 0.001f)
            {
                // Hits the camera starts inside report distance 0, which covers the clipped-into-wall case.
                int count = Physics.SphereCastNonAlloc(origin, castRadius, toTarget / distance, hits,
                    distance, occlusionMask, QueryTriggerInteraction.Collide);

                for (int i = 0; i < count; i++)
                {
                    CameraOccluder occluder = Resolve(hits[i].collider);
                    if (occluder == null) continue;

                    lastSeen[occluder] = now;
                    // A zone can disable and re-enable an occluder while it is still listed here.
                    if (!occluding.Contains(occluder)) occluding.Add(occluder);
                    if (!occluder.IsOccluded) SetOccluded(occluder, true);
                }
            }

            for (int i = occluding.Count - 1; i >= 0; i--)
            {
                CameraOccluder occluder = occluding[i];
                if (occluder == null)
                {
                    occluding.RemoveAt(i);
                    continue;
                }

                if (now - lastSeen[occluder] < linger) continue;
                occluding.RemoveAt(i);
                SetOccluded(occluder, false);
            }
        }

        private CameraOccluder Resolve(Collider hit)
        {
            if (!occluderByCollider.TryGetValue(hit, out CameraOccluder occluder))
            {
                occluder = hit.GetComponentInParent<CameraOccluder>();
                occluderByCollider[hit] = occluder;
            }

            return occluder != null && occluder.isActiveAndEnabled ? occluder : null;
        }

        private void SetOccluded(CameraOccluder occluder, bool value)
        {
            if (occluder.SetOccluded(value) && !animating.Contains(occluder))
                animating.Add(occluder);
        }

        private void ReleaseAll()
        {
            foreach (CameraOccluder occluder in occluding)
            {
                if (occluder != null && occluder.SetOccluded(false))
                    occluder.Step(float.MaxValue); // Snap back; nothing will step it later.
            }

            occluding.Clear();
            animating.Clear();
            lastSeen.Clear();
            SuspendOcclusionCulling(false);
        }

        private void SetTarget(PlayerCharacter player)
        {
            ReleaseAll();
            target = player != null ? player.transform : null;
            targetCollider = player != null ? player.GetComponent<Collider>() : null;
            nextTick = 0f;
        }

        #region Lifecycle binding

        private void Rebind()
        {
            PlayerLifecycle lifecycle = PlayerLifecycle.Instance;
            if (lifecycle == subscribedLifecycle) return;
            Unbind();
            subscribedLifecycle = lifecycle;
            if (subscribedLifecycle != null) subscribedLifecycle.PlayerSpawned += SetTarget;
        }

        private void Unbind()
        {
            if (subscribedLifecycle == null) return;
            subscribedLifecycle.PlayerSpawned -= SetTarget;
            subscribedLifecycle = null;
        }

        #endregion

        private void OnDrawGizmosSelected()
        {
            if (target == null) return;
            Gizmos.color = Color.yellow;
            Vector3 end = targetCollider != null ? targetCollider.bounds.center : target.position + targetOffset;
            Gizmos.DrawLine(transform.position, end);
            Gizmos.DrawWireSphere(end, castRadius);
        }
    }
}
