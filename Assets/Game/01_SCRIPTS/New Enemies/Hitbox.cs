using UnityEngine;
using System.Collections.Generic;
using System.Diagnostics;

namespace junklite
{
    /// <summary>
    /// Simple reusable hitbox component for dealing damage.
    /// Attach to a child GameObject with a trigger collider.
    /// Enable/disable via SetActive() or Activate()/Deactivate().
    /// 
    /// The hitbox just detects hits and fires events.
    /// The OWNER decides what damage/effects to apply via OnHit event.
    /// </summary>
    [RequireComponent(typeof(Collider))]
    public class Hitbox : MonoBehaviour
    {
        [Header("Targeting")]
        [SerializeField] private LayerMask targetLayers = ~0;
        [SerializeField] private bool hitOnce = true;

        // Track what we've already hit this activation
        private HashSet<Collider> hitTargets = new HashSet<Collider>();

        // Events - owner subscribes to handle damage/effects
        public event System.Action<Collider, Hitbox> OnHit;

        public LayerMask TargetLayers { get => targetLayers; set => targetLayers = value; }
        public bool HitOnce { get => hitOnce; set => hitOnce = value; }

        private void Awake()
        {
            // Ensure collider is a trigger
            var col = GetComponent<Collider>();
            if (col != null)
                col.isTrigger = true;
        }

        private void OnEnable()
        {
            // Clear hit list when activated
            hitTargets.Clear();
        }

        private void OnTriggerEnter(Collider other)
        {
            // Check layer mask
            if ((targetLayers & (1 << other.gameObject.layer)) == 0)
                return;

            // Skip if already hit and hitOnce is true
            if (hitOnce && hitTargets.Contains(other))
                return;

            // Track hit
            hitTargets.Add(other);
            
            // Fire event - let owner handle damage/effects
            OnHit?.Invoke(other, this);
        }

        /// <summary>
        /// Activate the hitbox for a duration, then deactivate.
        /// </summary>
        public void ActivateForDuration(float duration)
        {
            hitTargets.Clear();
            gameObject.SetActive(true);
            Invoke(nameof(Deactivate), duration);
        }

        /// <summary>
        /// Manually activate the hitbox.
        /// </summary>
        public void Activate()
        {
            hitTargets.Clear();
            gameObject.SetActive(true);
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