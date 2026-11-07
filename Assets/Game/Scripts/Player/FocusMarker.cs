using UnityEngine;

namespace junklite
{
    /// <summary>
    /// World marker over the player's combat focus. Look and size are authored on the prefab
    /// (Prefabs/PLAYER/Focus Marker.prefab); code only places it above the target and toggles it.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class FocusMarker : MonoBehaviour
    {
        [Tooltip("Gap between the top of the target's collider and the marker.")]
        [SerializeField] private float heightAboveTarget = 0.35f;
        [Tooltip("Optional gentle bob, world units. 0 = still.")]
        [SerializeField] private float bobHeight = 0.08f;
        [SerializeField] private float bobSpeed = 4f;
        [Tooltip("Turn to face the camera each frame (the authored rotation is kept on top), so it reads after lane corners.")]
        [SerializeField] private bool faceCamera = true;

        private Collider target;
        private Quaternion authoredRotation;

        private void Awake() => authoredRotation = transform.rotation;

        public void Show(Collider focusCollider)
        {
            target = focusCollider;
            if (target == null) { Hide(); return; }
            if (!gameObject.activeSelf) gameObject.SetActive(true);
            Follow();
        }

        public void Hide()
        {
            target = null;
            if (gameObject.activeSelf) gameObject.SetActive(false);
        }

        private void LateUpdate()
        {
            if (target == null || !target.gameObject.activeInHierarchy) { Hide(); return; }
            Follow();
        }

        private void Follow()
        {
            Bounds bounds = target.bounds;
            float bob = bobHeight > 0f ? Mathf.Sin(Time.time * bobSpeed) * bobHeight : 0f;
            transform.position = new Vector3(bounds.center.x, bounds.max.y + heightAboveTarget + bob, bounds.center.z);
            if (!faceCamera) return;
            Camera view = Camera.main;
            if (view != null) transform.rotation = view.transform.rotation * authoredRotation;
        }
    }
}
