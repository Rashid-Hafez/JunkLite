using UnityEngine;

namespace junklite
{
    /// <summary>
    /// Owns Flying Dummy's gravity setup, passive hover bob, return height, and
    /// death fall without reading the FSM or making AI decisions.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(EnemyCharacter))]
    [RequireComponent(typeof(EnemyMovement))]
    [RequireComponent(typeof(Rigidbody))]
    public sealed class FlyingHoverController : MonoBehaviour
    {
        [SerializeField, HideInInspector] private bool ownsSerializedConfiguration;
        [SerializeField] private float hoverBobAmount = 0.2f;
        [SerializeField] private float hoverBobSpeed = 2f;
        [SerializeField] private float returnHeightSpeed = 5f;

        private EnemyCharacter actor;
        private EnemyMovement movement;
        private Rigidbody body;
        private float spawnHeight;
        private float baseHeight;
        private float bobTimer;
        private bool followingTarget;

        public bool OwnsSerializedConfiguration => ownsSerializedConfiguration;

        private void Awake()
        {
            actor = GetComponent<EnemyCharacter>();
            movement = GetComponent<EnemyMovement>();
            body = GetComponent<Rigidbody>();
            spawnHeight = transform.position.y;
            baseHeight = spawnHeight;

            ConfigureFlightPhysics();
        }

        private void OnEnable()
        {
            if (actor != null)
                actor.Died += HandleDied;
        }

        private void OnDisable()
        {
            if (actor != null)
                actor.Died -= HandleDied;
        }

        private void Update()
        {
            if (actor == null || !actor.IsAlive || followingTarget
                || movement == null || movement.IsInKnockback)
            {
                return;
            }

            baseHeight = Mathf.MoveTowards(
                baseHeight,
                spawnHeight,
                Mathf.Max(0f, returnHeightSpeed) * Time.deltaTime);

            bobTimer += Time.deltaTime * hoverBobSpeed;
            float bobOffset = hoverBobAmount > 0f
                ? Mathf.Sin(bobTimer) * hoverBobAmount
                : 0f;

            Vector3 position = transform.position;
            position.y = baseHeight + bobOffset;
            transform.position = position;
        }

        public void SetFollowingTarget(bool following)
        {
            if (followingTarget == following)
                return;

            followingTarget = following;
            if (!followingTarget)
                baseHeight = transform.position.y;
        }

        public void ApplyLegacyConfiguration(float bobAmount, float bobSpeed, float heightReturnSpeed)
        {
            if (ownsSerializedConfiguration)
                return;

            hoverBobAmount = bobAmount;
            hoverBobSpeed = bobSpeed;
            returnHeightSpeed = heightReturnSpeed;
            ownsSerializedConfiguration = true;
        }

        private void ConfigureFlightPhysics()
        {
            if (body == null)
                return;

            body.useGravity = false;
            body.constraints = RigidbodyConstraints.FreezeRotation
                | RigidbodyConstraints.FreezePositionZ;
        }

        private void HandleDied(EnemyCharacter enemy)
        {
            followingTarget = false;
            if (body == null)
                return;

            body.isKinematic = false;
            body.useGravity = true;
            body.linearVelocity = Vector3.zero;
            body.constraints = RigidbodyConstraints.FreezeRotation;
        }

#if UNITY_EDITOR
        private void OnDrawGizmosSelected()
        {
            if (hoverBobAmount <= 0f)
                return;

            Gizmos.color = new Color(0.5f, 0.5f, 1f, 0.5f);
            float height = Application.isPlaying ? spawnHeight : transform.position.y;
            Vector3 center = new(transform.position.x, height, transform.position.z);
            Gizmos.DrawLine(
                center + Vector3.up * hoverBobAmount,
                center + Vector3.down * hoverBobAmount);
        }
#endif
    }
}
