using UnityEngine;

namespace junklite
{
    public class CustomGravity : MonoBehaviour
    {
        [SerializeField] private float gravity = 12f;
        [SerializeField] private float groundCheckRadius = 0.1f;
        [SerializeField] private float groundCheckDistance = 0.05f;
        [SerializeField] private float originOffset = 0.5f;
        [SerializeField] private LayerMask groundLayerMask = 1;

        private float verticalVelocity;
        private bool grounded;

        private void Update()
        {
            if (grounded) return;

            verticalVelocity -= gravity * Time.deltaTime;

            float moveThisFrame = Mathf.Abs(verticalVelocity * Time.deltaTime);
            Vector3 origin = transform.position + Vector3.up * originOffset;

            if (Physics.SphereCast(
                    origin,
                    groundCheckRadius,
                    Vector3.down,
                    out RaycastHit hit,
                    originOffset + groundCheckDistance + moveThisFrame, // ← cast the full travel distance
                    groundLayerMask,
                    QueryTriggerInteraction.Ignore))
            {
                // Snap flush to the surface instead of tunnelling through it
                float snapDistance = hit.distance - originOffset;
                if (snapDistance > 0f)
                    transform.position += Vector3.down * snapDistance;

                grounded = true;
                verticalVelocity = 0f;
            }
            else
            {
                transform.position += Vector3.up * verticalVelocity * Time.deltaTime;
            }
        }

        public void ResetGravity()
        {
            grounded = false;
            verticalVelocity = 0f;
        }

        private void OnDrawGizmosSelected()
        {
            Gizmos.color = grounded ? Color.green : Color.yellow;
            Vector3 origin = transform.position + Vector3.up * originOffset;
            Gizmos.DrawWireSphere(origin, groundCheckRadius);
            Gizmos.DrawLine(origin, origin + Vector3.down * (originOffset + groundCheckDistance));
        }
    }
}