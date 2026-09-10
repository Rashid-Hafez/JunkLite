using System;
using System.Collections.Generic;
using UnityEngine;

namespace junklite
{
    /// <summary>
    /// Single-player enemy sensor. It reports target facts and never chooses AI states.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Collider))]
    public class EnemyPerception : MonoBehaviour
    {
        [Header("Detection")]
        [SerializeField] private LayerMask targetLayers = ~0;
        [SerializeField] private bool requireLineOfSight;
        [SerializeField] private LayerMask losBlockingLayers;

        [Header("Reachability")]
        [Tooltip("If true, the target is rejected when a floor separates it vertically from this enemy.")]
        [SerializeField] private bool requireReachablePath = true;
        [SerializeField] private LayerMask platformLayers;
        [SerializeField] private float minVerticalDifferenceToCheck = 0.5f;

        [Header("Performance")]
        [SerializeField, Min(0.05f), Tooltip("How often tracked targets are revalidated while inside the sensor.")]
        private float validationInterval = 0.2f;

        [Header("Debug")]
        [SerializeField] private bool showGizmos = true;

        private readonly HashSet<Collider> targetColliders = new();
        private PlayerCharacter currentTarget;
        private SphereCollider sphereCollider;
        private Transform distanceOrigin;
        private float originalRadius;
        private Coroutine validationRoutine;
        private WaitForSeconds validationWait;

        /// <summary>Fires after the current target changes. Arguments are previous and current.</summary>
        public event Action<PlayerCharacter, PlayerCharacter> TargetChanged;

        // Compatibility events for legacy enemy controllers. New brains use TargetChanged.
        public event Action<PlayerCharacter> OnTargetEnter;
        public event Action<PlayerCharacter> OnTargetExit;

        public PlayerCharacter CurrentTarget => HasTarget ? currentTarget : null;
        public Transform TargetTransform => CurrentTarget != null ? CurrentTarget.transform : null;
        public Transform Target => TargetTransform;
        public bool HasTarget => currentTarget != null && currentTarget.IsAlive;
        public float TargetDistance => HasTarget && distanceOrigin != null
            ? Vector3.Distance(distanceOrigin.position, currentTarget.transform.position)
            : float.MaxValue;
        public float OriginalRadius => originalRadius;

        protected virtual void Awake()
        {
            Collider sensorCollider = GetComponent<Collider>();
            sensorCollider.isTrigger = true;

            sphereCollider = sensorCollider as SphereCollider;
            if (sphereCollider != null)
                originalRadius = sphereCollider.radius;

            EnemyCharacter owner = GetComponentInParent<EnemyCharacter>();
            distanceOrigin = owner != null ? owner.transform : transform;

            int playerLayer = LayerMask.NameToLayer("Player");
            if (playerLayer >= 0)
                targetLayers |= 1 << playerLayer;

            validationWait = new WaitForSeconds(validationInterval);
        }

        protected virtual void OnDisable()
        {
            ClearTarget();
            targetColliders.Clear();
            ResetRadius();
            if (validationRoutine != null)
            {
                StopCoroutine(validationRoutine);
                validationRoutine = null;
            }
        }

        public void SetRadius(float radius)
        {
            if (sphereCollider != null)
                sphereCollider.radius = Mathf.Max(0f, radius);
        }

        public void ResetRadius()
        {
            if (sphereCollider != null)
                sphereCollider.radius = originalRadius;
        }

        /// <summary>
        /// Explicitly forgets the target. The brain may use this when its pursuit policy expires.
        /// </summary>
        public void ClearTarget()
        {
            if (ReferenceEquals(currentTarget, null))
                return;

            PlayerCharacter previous = currentTarget;
            currentTarget = null;

            TargetChanged?.Invoke(previous, null);
            OnTargetExit?.Invoke(previous);
        }

        private void OnTriggerEnter(Collider other)
        {
            TryTrackCollider(other);
            EnsureValidationRunning();
        }

        private void OnTriggerExit(Collider other)
        {
            targetColliders.Remove(other);

            PlayerCharacter player = ResolvePlayer(other);
            if (player == null || player != currentTarget)
            {
                StopValidationIfEmpty();
                return;
            }

            CleanupMissingColliders();
            if (!ContainsColliderFor(player))
                ClearTarget();

            StopValidationIfEmpty();
        }

        private void EnsureValidationRunning()
        {
            if (targetColliders.Count > 0 && validationRoutine == null)
                validationRoutine = StartCoroutine(ValidateTrackedTargets());
        }

        private void StopValidationIfEmpty()
        {
            if (targetColliders.Count > 0 || validationRoutine == null)
                return;

            StopCoroutine(validationRoutine);
            validationRoutine = null;
        }

        private System.Collections.IEnumerator ValidateTrackedTargets()
        {
            while (targetColliders.Count > 0)
            {
                yield return validationWait;
                CleanupMissingColliders();

                if (!ReferenceEquals(currentTarget, null) &&
                    (currentTarget == null || !currentTarget.IsAlive || !IsValidTarget(currentTarget)))
                {
                    ClearTarget();
                }

                if (!HasTarget)
                {
                    foreach (Collider collider in targetColliders)
                    {
                        TryTrackCollider(collider);
                        if (HasTarget)
                            break;
                    }
                }
            }

            validationRoutine = null;
        }

        private void TryTrackCollider(Collider other)
        {
            if ((targetLayers & (1 << other.gameObject.layer)) == 0)
                return;

            PlayerCharacter player = ResolvePlayer(other);
            if (player == null || !player.IsAlive)
                return;

            targetColliders.Add(other);

            if (currentTarget == player || currentTarget != null)
                return;

            if (!IsValidTarget(player))
                return;

            currentTarget = player;
            TargetChanged?.Invoke(null, player);
            OnTargetEnter?.Invoke(player);
        }

        private bool IsValidTarget(PlayerCharacter player)
        {
            if (player == null || !player.IsAlive)
                return false;
            if (requireLineOfSight && !HasLineOfSight(player.transform))
                return false;
            if (requireReachablePath && !IsTargetReachable(player.transform))
                return false;
            return true;
        }

        private static PlayerCharacter ResolvePlayer(Collider collider)
        {
            if (collider == null)
                return null;

            return collider.GetComponent<PlayerCharacter>()
                ?? collider.GetComponentInParent<PlayerCharacter>();
        }

        private bool ContainsColliderFor(PlayerCharacter player)
        {
            foreach (Collider collider in targetColliders)
            {
                if (collider != null && ResolvePlayer(collider) == player)
                    return true;
            }

            return false;
        }

        private void CleanupMissingColliders()
        {
            targetColliders.RemoveWhere(collider => collider == null);
        }

        private bool IsTargetReachable(Transform target)
        {
            float verticalDifference = transform.position.y - target.position.y;
            if (verticalDifference < minVerticalDifferenceToCheck)
                return true;

            return !Physics.Raycast(
                transform.position,
                Vector3.down,
                verticalDifference,
                platformLayers,
                QueryTriggerInteraction.Ignore);
        }

        private bool HasLineOfSight(Transform target)
        {
            Vector3 origin = transform.position;
            Vector3 toTarget = target.position - origin;

            if (Physics.Raycast(
                    origin,
                    toTarget.normalized,
                    out RaycastHit hit,
                    toTarget.magnitude,
                    losBlockingLayers,
                    QueryTriggerInteraction.Ignore))
            {
                return hit.transform == target || hit.transform.IsChildOf(target);
            }

            return true;
        }

#if UNITY_EDITOR
        private void OnDrawGizmos()
        {
            if (!showGizmos)
                return;

            Collider sensorCollider = GetComponent<Collider>();
            if (sensorCollider == null)
                return;

            Gizmos.color = HasTarget
                ? new Color(1f, 0.5f, 0f, 0.3f)
                : new Color(1f, 1f, 0f, 0.2f);

            if (sensorCollider is SphereCollider sphere)
            {
                Gizmos.matrix = transform.localToWorldMatrix;
                Gizmos.DrawWireSphere(sphere.center, sphere.radius);
            }
            else if (sensorCollider is BoxCollider box)
            {
                Gizmos.matrix = transform.localToWorldMatrix;
                Gizmos.DrawWireCube(box.center, box.size);
            }
        }
#endif
    }
}
