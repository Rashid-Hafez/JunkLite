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
            transform.position += Vector3.up * verticalVelocity * Time.deltaTime;

            Vector3 origin = transform.position + Vector3.up * originOffset;

            if (Physics.SphereCast(
                    origin,
                    groundCheckRadius,
                    Vector3.down,
                    out _,
                    originOffset + groundCheckDistance,
                    groundLayerMask,
                    QueryTriggerInteraction.Ignore))
            {
                grounded = true;
                verticalVelocity = 0f;
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