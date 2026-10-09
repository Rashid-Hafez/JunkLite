using UnityEngine;

namespace junklite
{
    /// <summary>
    /// Two-leaf sliding door that opens while the player is near.
    /// The proximity trigger is the BoxCollider on this object. The blocker is a solid
    /// BoxCollider across the doorway; it is turned off once the door is fully open and
    /// turned back on as soon as the door starts closing.
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
        [Tooltip("A locked door stays shut (and closes if open) even with the player nearby.")]
        [SerializeField] private bool locked;

        [Header("Audio (optional)")]
        [SerializeField] private AudioSource audioSource;
        [SerializeField] private AudioClip openClip;
        [SerializeField] private AudioClip closeClip;

        private Vector3 leftClosed;
        private Vector3 rightClosed;
        private float openAmount;   // 0 = closed, 1 = open
        private int playerContacts;

        public bool IsOpen => openAmount >= 1f;
        public bool Locked { get => locked; set => locked = value; }

        private void Awake()
        {
            GetComponent<BoxCollider>().isTrigger = true;
            if (leftLeaf != null) leftClosed = leftLeaf.localPosition;
            if (rightLeaf != null) rightClosed = rightLeaf.localPosition;
            Apply();
        }

        private void OnDisable() => playerContacts = 0;

        private void Update()
        {
            float target = playerContacts > 0 && !locked ? 1f : 0f;
            if (Mathf.Approximately(openAmount, target)) return;

            if (target < openAmount && blocker != null && !blocker.enabled)
                blocker.enabled = true;

            openAmount = Mathf.MoveTowards(openAmount, target, Time.deltaTime / Mathf.Max(0.01f, slideDuration));
            Apply();
        }

        private void OnTriggerEnter(Collider other)
        {
            if (!other.CompareTag("Player")) return;
            if (playerContacts++ == 0 && !locked) PlayClip(openClip);
        }

        private void OnTriggerExit(Collider other)
        {
            if (!other.CompareTag("Player") || playerContacts == 0) return;
            if (--playerContacts == 0 && openAmount > 0f) PlayClip(closeClip);
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
