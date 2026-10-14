using System;
using System.Collections.Generic;
using UnityEngine;

namespace junklite
{
    /// <summary>
    /// Two-leaf sliding door that opens while the player or a living enemy is near
    /// (enemies chasing the player follow through).
    /// The proximity trigger is the BoxCollider on this object. The blocker is a solid
    /// BoxCollider across the doorway; it is turned off once the door is fully open and
    /// turned back on as soon as the door starts closing.
    /// Locking is shared: any number of owners (lane gates, access rules) can hold a lock,
    /// and the door stays shut until every owner has released theirs.
    /// </summary>
    [RequireComponent(typeof(BoxCollider))]
    public class SlidingDoor : MonoBehaviour
    {
        [Header("Leaves (slide along this object's local X)")]
        [SerializeField] private Transform leftLeaf;
        [SerializeField] private Transform rightLeaf;
        [Tooltip("Slide distance per leaf, in this object's local units.")]
        [SerializeField] private float slideDistance = 1.4f;
        [Tooltip("Seconds to fully open or close.")]
        [SerializeField] private float slideDuration = 0.45f;
        [SerializeField] private AnimationCurve slideCurve = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);

        [Header("Blocker")]
        [SerializeField] private Collider blocker;
        [Tooltip("Starts locked. A locked door stays shut (and closes if open) even with the player nearby.")]
        [SerializeField] private bool locked;

        [Header("Audio (optional)")]
        [SerializeField] private AudioSource audioSource;
        [SerializeField] private AudioClip openClip;
        [SerializeField] private AudioClip closeClip;

        private Vector3 leftClosed;
        private Vector3 rightClosed;
        private float openAmount;   // 0 = closed, 1 = open
        private int playerContacts;
        private readonly HashSet<object> lockOwners = new();
        // Solid enemy bodies inside the trigger. Detection/hitbox triggers are ignored.
        private readonly Dictionary<Collider, EnemyCharacter> enemyContacts = new();
        private readonly List<Collider> staleEnemies = new();

        public bool IsOpen => openAmount >= 1f;
        public bool Locked => lockOwners.Count > 0;

        /// <summary>Raised each time the door finishes opening for the player (not for enemies).</summary>
        public event Action<SlidingDoor> PlayerOpened;

        /// <summary>Adds a lock held by <paramref name="owner"/>. Adding the same owner twice does nothing.</summary>
        public void AddLock(object owner)
        {
            if (owner != null) lockOwners.Add(owner);
        }

        /// <summary>Releases the lock held by <paramref name="owner"/>. Safe to call when it holds none.</summary>
        public void RemoveLock(object owner)
        {
            if (owner != null) lockOwners.Remove(owner);
        }

        private void Awake()
        {
            if (locked) AddLock(this);
            GetComponent<BoxCollider>().isTrigger = true;
            if (leftLeaf != null) leftClosed = leftLeaf.localPosition;
            if (rightLeaf != null) rightClosed = rightLeaf.localPosition;
            Apply();
        }

        private void OnDisable()
        {
            playerContacts = 0;
            enemyContacts.Clear();
        }

        private void Update()
        {
            PruneEnemies();
            bool someoneNear = playerContacts > 0 || enemyContacts.Count > 0;
            float target = someoneNear && !Locked ? 1f : 0f;
            if (Mathf.Approximately(openAmount, target)) return;

            if (target < openAmount && blocker != null && !blocker.enabled)
                blocker.enabled = true;

            openAmount = Mathf.MoveTowards(openAmount, target, Time.deltaTime / Mathf.Max(0.01f, slideDuration));
            Apply();
            if (openAmount >= 1f && playerContacts > 0) PlayerOpened?.Invoke(this);
        }

        private void OnTriggerEnter(Collider other)
        {
            if (!other.CompareTag("Player"))
            {
                TrackEnemy(other);
                return;
            }
            if (playerContacts++ == 0 && !Locked) PlayClip(openClip);
        }

        private void OnTriggerExit(Collider other)
        {
            if (!other.CompareTag("Player"))
            {
                enemyContacts.Remove(other);
                return;
            }
            if (playerContacts == 0) return;
            if (--playerContacts == 0 && openAmount > 0f) PlayClip(closeClip);
        }

        private void TrackEnemy(Collider other)
        {
            if (other.isTrigger) return;
            EnemyCharacter enemy = other.GetComponentInParent<EnemyCharacter>();
            if (enemy != null && enemy.IsAlive) enemyContacts[other] = enemy;
        }

        // Dead or disabled enemies never send OnTriggerExit, so drop them here.
        private void PruneEnemies()
        {
            if (enemyContacts.Count == 0) return;
            foreach (KeyValuePair<Collider, EnemyCharacter> pair in enemyContacts)
            {
                if (pair.Key == null || !pair.Key.enabled || !pair.Key.gameObject.activeInHierarchy ||
                    pair.Value == null || !pair.Value.IsAlive)
                    staleEnemies.Add(pair.Key);
            }
            foreach (Collider stale in staleEnemies) enemyContacts.Remove(stale);
            staleEnemies.Clear();
        }

        private void Apply()
        {
            float k = slideCurve.Evaluate(openAmount) * slideDistance;
            if (leftLeaf != null) leftLeaf.localPosition = leftClosed + Vector3.left * k;
            if (rightLeaf != null) rightLeaf.localPosition = rightClosed + Vector3.right * k;
            if (blocker != null && openAmount >= 1f) blocker.enabled = false;
        }

        private void PlayClip(AudioClip clip)
        {
            if (audioSource != null && clip != null) audioSource.PlayOneShot(clip);
        }

        private void OnDrawGizmosSelected()
        {
            var box = GetComponent<BoxCollider>();
            if (box == null) return;
            Gizmos.color = new Color(1f, 0.8f, 0.2f, 0.2f);
            Gizmos.matrix = transform.localToWorldMatrix;
            Gizmos.DrawCube(box.center, box.size);
        }
    }
}
