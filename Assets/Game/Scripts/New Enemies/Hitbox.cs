using UnityEngine;
using System.Collections.Generic;

namespace junklite
{
    /// <summary>
    /// Simple reusable hitbox component for dealing damage.
    /// Attach to a child GameObject with a Box, Sphere or Capsule collider; the
    /// collider only defines the shape.
    /// Enable/disable via SetActive() or Activate()/Deactivate().
    ///
    /// While active, the hitbox queries its own volume on activation and every
    /// physics step instead of waiting for OnTriggerEnter. Trigger callbacks miss
    /// targets that are already inside the volume when it turns on and both bodies
    /// are asleep (point-blank swings), so detection must not depend on them.
    ///
    /// The hitbox just detects hits and fires events.
    /// The OWNER decides what damage/effects to apply via OnHit event.
    /// </summary>
    [RequireComponent(typeof(Collider))]
    public class Hitbox : MonoBehaviour
    {
        [Header("Targeting")]
        [SerializeField] private LayerMask targetLayers = ~0;
        [Tooltip("Hit each target (damage receiver) at most once per activation.")]
        [SerializeField] private bool hitOnce = true;

        // Targets already hit this activation, keyed by damage receiver so a target
        // with several colliders is only hit once.
        private readonly HashSet<Object> hitTargets = new HashSet<Object>();
        // Targets overlapping on the previous query (used when hitOnce is off, so a
        // target is hit once per entry rather than every physics step).
        private readonly HashSet<Object> overlapping = new HashSet<Object>();
        private readonly HashSet<Object> overlappingNow = new HashSet<Object>();
        private readonly Collider[] queryBuffer = new Collider[16];
        private Collider shape;

        // Events - owner subscribes to handle damage/effects
        public event System.Action<Collider, Hitbox> OnHit;

        public LayerMask TargetLayers { get => targetLayers; set => targetLayers = value; }
        public bool HitOnce { get => hitOnce; set => hitOnce = value; }

        private void Awake()
        {
            // Ensure collider is a trigger
            shape = GetComponent<Collider>();
            if (shape != null)
                shape.isTrigger = true;

            // Enemy-owned hitboxes are sensors (see EnemySensorLayer). Player and mod
            // hitboxes keep their authored layer.
            if (GetComponentInParent<EnemyCharacter>() != null)
                EnemySensorLayer.Apply(gameObject);
        }

        private void OnEnable()
        {
            // Clear hit list when activated
            ResetHits();
        }

        private void FixedUpdate()
        {
            QueryHits();
        }

        /// <summary>
        /// Activate the hitbox for a duration, then deactivate.
        /// </summary>
        public void ActivateForDuration(float duration)
        {
            Activate();
            Invoke(nameof(Deactivate), duration);
        }

        /// <summary>
        /// Manually activate the hitbox. Targets already inside the volume are hit
        /// immediately, without waiting for the next physics step.
        /// </summary>
        public void Activate()
        {
            ResetHits();
            gameObject.SetActive(true);
            QueryHits();
        }

        /// <summary>
        /// Manually deactivate the hitbox.
        /// </summary>
        public void Deactivate()
        {
            gameObject.SetActive(false);
        }

        /// <summary>
        /// Reset hit tracking (allows hitting same targets again).
        /// </summary>
        public void ResetHits()
        {
            hitTargets.Clear();
            overlapping.Clear();
        }

        private void QueryHits()
        {
            if (OnHit == null || shape == null || !isActiveAndEnabled)
                return;

            int count = OverlapShape();
            overlappingNow.Clear();

            for (int i = 0; i < count; i++)
            {
                Collider other = queryBuffer[i];
                queryBuffer[i] = null;
                if (other == null)
                    continue;

                Object key = GetTargetKey(other);
                if (!overlappingNow.Add(key))
                    continue;

                bool isNew = hitOnce ? hitTargets.Add(key) : !overlapping.Contains(key);
                if (!isNew)
                    continue;

                OnHit?.Invoke(other, this);

                // The owner may deactivate on the first hit (dash, grab).
                if (!isActiveAndEnabled)
                {
                    for (int j = i + 1; j < count; j++)
                        queryBuffer[j] = null;
                    break;
                }
            }

            overlapping.Clear();
            overlapping.UnionWith(overlappingNow);
        }

        private int OverlapShape()
        {
            Transform t = transform;
            Vector3 scale = t.lossyScale;
            scale = new Vector3(Mathf.Abs(scale.x), Mathf.Abs(scale.y), Mathf.Abs(scale.z));
            const QueryTriggerInteraction triggers = QueryTriggerInteraction.Collide;

            switch (shape)
            {
                case BoxCollider box:
                    return Physics.OverlapBoxNonAlloc(
                        t.TransformPoint(box.center),
                        Vector3.Scale(box.size, scale) * 0.5f,
                        queryBuffer, t.rotation, targetLayers, triggers);

                case SphereCollider sphere:
                    return Physics.OverlapSphereNonAlloc(
                        t.TransformPoint(sphere.center),
                        sphere.radius * Mathf.Max(scale.x, Mathf.Max(scale.y, scale.z)),
                        queryBuffer, targetLayers, triggers);

                case CapsuleCollider capsule:
                {
                    int axis = capsule.direction;
                    float axisScale = scale[axis];
                    float radiusScale = Mathf.Max(scale[(axis + 1) % 3], scale[(axis + 2) % 3]);
                    float radius = capsule.radius * radiusScale;
                    float halfSegment = Mathf.Max(0f, capsule.height * axisScale * 0.5f - radius);
                    Vector3 localAxis = axis == 0 ? Vector3.right : axis == 1 ? Vector3.up : Vector3.forward;
                    Vector3 center = t.TransformPoint(capsule.center);
                    Vector3 offset = t.rotation * localAxis * halfSegment;
                    return Physics.OverlapCapsuleNonAlloc(
                        center - offset, center + offset, radius,
                        queryBuffer, targetLayers, triggers);
                }

                default:
                    return 0;
            }
        }

        private static Object GetTargetKey(Collider other)
        {
            if (DamageReceiverUtility.TryGetReceiver(other, out IDamageReceiver receiver)
                && receiver is Object receiverObject)
                return receiverObject;

            if (other.attachedRigidbody != null)
                return other.attachedRigidbody;

            return other;
        }

        private void OnDrawGizmos()
        {
            var col = GetComponent<Collider>();
            if (col == null) return;

            Gizmos.color = gameObject.activeSelf ? new Color(1f, 0f, 0f, 0.5f) : new Color(1f, 0f, 0f, 0.2f);

            if (col is BoxCollider box)
            {
                Gizmos.matrix = transform.localToWorldMatrix;
                Gizmos.DrawCube(box.center, box.size);
                Gizmos.DrawWireCube(box.center, box.size);
            }
            else if (col is SphereCollider sphere)
            {
                Gizmos.DrawSphere(transform.position + sphere.center, sphere.radius);
                Gizmos.DrawWireSphere(transform.position + sphere.center, sphere.radius);
            }
            else if (col is CapsuleCollider capsule)
            {
                Gizmos.matrix = transform.localToWorldMatrix;
                Gizmos.DrawWireSphere(capsule.center, capsule.radius);
            }
        }
    }
}
