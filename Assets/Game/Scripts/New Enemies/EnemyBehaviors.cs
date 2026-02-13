using UnityEngine;

namespace junklite
{
    // ============================================================
    // SERIALIZABLE BEHAVIOR CLASSES
    // 
    // Reusable implementations for capability interfaces.
    // Enemies embed these as [SerializeField] fields and delegate.
    // 
    // Add this file to: Scripts/Enemies/Core/EnemyBehaviors.cs
    // ============================================================

    /// <summary>
    /// Reusable patrol implementation.
    /// </summary>
    [System.Serializable]
    public class PatrolBehavior
    {
        [SerializeField] private float patrolDistance = 5f;
        [SerializeField] private float patrolSpeed = 2f;
        [SerializeField] private float wallCheckDistance = 0.5f;
        [SerializeField] private LayerMask wallLayer;
        [SerializeField] private Transform wallCheckPoint;

        private Transform owner;
        private Vector3 spawnPosition;
        private int direction = 1;
        private Vector3 horizontalAxis = Vector3.right;

        public float PatrolDistance => patrolDistance;
        public float PatrolSpeed => patrolSpeed;
        public Vector3 SpawnPosition => spawnPosition;
        public int PatrolDirection { get => direction; set => direction = value; }
        public bool HasPatrol => patrolDistance > 0f;

        public void Initialize(Transform owner)
        {
            this.owner = owner;
            this.spawnPosition = owner.position;
        }

        public bool IsWallAhead()
        {
            if (owner == null) return false;

            Vector3 origin = wallCheckPoint != null
                ? wallCheckPoint.position
                : owner.position + Vector3.up * 0.5f;
            Vector3 dir = horizontalAxis * direction;
            return Physics.Raycast(origin, dir, wallCheckDistance, wallLayer);
        }

        public bool IsAtPatrolBoundary()
        {
            if (owner == null) return false;

            float dist = Vector3.Dot(owner.position - spawnPosition,owner.right);
            return (direction > 0 && dist >= patrolDistance) ||
                   (direction < 0 && dist <= -patrolDistance);
        }

        public void ReverseDirection() => direction *= -1;

        private Vector3 SnapToNearestAxis(Vector3 dir)
        {
            float absX = Mathf.Abs(dir.x);
            float absZ = Mathf.Abs(dir.z);
            if (absX >= absZ)
                return new Vector3(Mathf.Sign(dir.x), 0f, 0f);
            else
                return new Vector3(0f, 0f, Mathf.Sign(dir.z));
        }

        public void DrawGizmos(Transform enemyTransform)
        {
            if (patrolDistance <= 0f) return;
            if (enemyTransform == null) return;

            // In play mode use stored spawn, in edit mode use current position
            Vector3 origin = Application.isPlaying ? spawnPosition : enemyTransform.position;

            // Patrol range
            Gizmos.color = Color.cyan;
            Vector3 left = origin + enemyTransform.right * -1f * patrolDistance;
            Vector3 right = origin + enemyTransform.right * patrolDistance;
            Gizmos.DrawLine(left, right);
            Gizmos.DrawWireSphere(left, 0.2f);
            Gizmos.DrawWireSphere(right, 0.2f);

            // Spawn point
            Gizmos.color = Color.green;
            Gizmos.DrawWireCube(origin, Vector3.one * 0.2f);

            // Wall check ray
            Gizmos.color = Color.yellow;
            Vector3 checkOrigin = wallCheckPoint != null
                ? wallCheckPoint.position
                : enemyTransform.position + Vector3.up * 0.5f;
            Vector3 checkDir = (Application.isPlaying ? horizontalAxis : SnapToNearestAxis(enemyTransform.right)) * direction;
            Gizmos.DrawRay(checkOrigin, checkDir * wallCheckDistance);
        }
    }

    /// <summary>
    /// Reusable charge-up implementation.
    /// </summary>
    [System.Serializable]
    public class ChargeBehavior
    {
        [SerializeField] private float chargeTime = 1f;
        [SerializeField] private GameObject chargeVFXPrefab;

        public float ChargeTime => chargeTime;
        public GameObject ChargeVFXPrefab => chargeVFXPrefab;
    }

    /// <summary>
    /// Reusable dash attack implementation.
    /// </summary>
    [System.Serializable]
    public class DashBehavior
    {
        [SerializeField] private float dashSpeed = 15f;
        [SerializeField] private float dashDamage = 10f;
        [SerializeField] private float dashStopDistance = 0.5f;
        [SerializeField] private Vector2 dashKnockback = new Vector2(15f, 5f);
        [SerializeField] private Hitbox dashHitbox;
        [SerializeField] private GameObject dashVFXPrefab;

        public float DashSpeed => dashSpeed;
        public float DashDamage => dashDamage;
        public float DashStopDistance => dashStopDistance;
        public Vector2 DashKnockback => dashKnockback;
        public Hitbox DashHitbox => dashHitbox;
        public GameObject DashVFXPrefab => dashVFXPrefab;
    }

    /// <summary>
    /// Reusable grab implementation.
    /// </summary>
    [System.Serializable]
    public class GrabBehavior
    {
        [SerializeField] private bool canGrab = true;
        [SerializeField][Range(0f, 1f)] private float grabChance = 0.3f;
        [SerializeField] private float grabDuration = 0.5f;
        [SerializeField] private Vector3 grabOffset = new Vector3(0f, 1.5f, 0f);
        [SerializeField] private Vector2 throwForce = new Vector2(25f, 10f);
        [SerializeField] private float throwDamage = 5f;
        [SerializeField] private GameObject grabVFXPrefab;

        public bool CanGrab => canGrab;
        public float GrabChance => grabChance;
        public float GrabDuration => grabDuration;
        public Vector3 GrabOffset => grabOffset;
        public Vector2 ThrowForce => throwForce;
        public float ThrowDamage => throwDamage;
        public GameObject GrabVFXPrefab => grabVFXPrefab;

        public bool RollForGrab() => canGrab && Random.value <= grabChance;
    }

    /// <summary>
    /// Reusable recovery implementation.
    /// </summary>
    [System.Serializable]
    public class RecoveryBehavior
    {
        [SerializeField] private float recoveryTime = 0.3f;
        [SerializeField] private GameObject recoveryVFXPrefab;

        public float RecoveryTime => recoveryTime;
        public GameObject RecoveryVFXPrefab => recoveryVFXPrefab;
    }

    /// <summary>
    /// Reusable melee attack implementation.
    /// </summary>
    [System.Serializable]
    public class MeleeAttackBehavior
    {
        [Tooltip("Cooldown between attacks")]
        [SerializeField] private float attackSpeed = 0.5f;
        [SerializeField] private float damage = 10f;
        [SerializeField] private Vector2 knockback = new Vector2(5f, 2f);
        [SerializeField] private Hitbox hitbox;
        [SerializeField] private GameObject vfxPrefab;

        public float MeleeAttackSpeed => attackSpeed;
        public float MeleeDamage => damage;
        public Vector2 MeleeKnockback => knockback;
        public Hitbox MeleeHitbox => hitbox;
        public GameObject MeleeVFXPrefab => vfxPrefab;
    }

    /// <summary>
    /// Reusable dodge implementation.
    /// </summary>
    [System.Serializable]
    public class DodgeBehavior
    {
        [SerializeField] private float dodgeDistance = 3f;
        [SerializeField] private float dodgeSpeed = 10f;
        [SerializeField] private float dodgeHeight = 0.5f;
        [SerializeField] private bool hasIFrames = true;
        [SerializeField] private GameObject dodgeVFXPrefab;

        public float DodgeDistance => dodgeDistance;
        public float DodgeSpeed => dodgeSpeed;
        public float DodgeDuration => dodgeSpeed > 0f ? dodgeDistance / dodgeSpeed : 0.3f;
        public float DodgeHeight => dodgeHeight;
        public bool DodgeHasIFrames => hasIFrames;
        public GameObject DodgeVFXPrefab => dodgeVFXPrefab;
    }

    /// <summary>
    /// Reusable chase implementation.
    /// </summary>
    [System.Serializable]
    public class ChaseBehavior
    {
        [SerializeField] private float chaseSpeed = 5f;
        [Tooltip("Distance to stop from target (0 = use attack range instead)")]
        [SerializeField] private float chaseStopDistance = 0f;

        private Vector3 lastKnownPosition;
        private bool hasLastKnownPosition;

        public float ChaseSpeed => chaseSpeed;
        public float ChaseStopDistance => chaseStopDistance;
        public Vector3 LastKnownTargetPosition => lastKnownPosition;
        public bool HasLastKnownPosition => hasLastKnownPosition;

        public void UpdateLastKnownPosition(Vector3 position)
        {
            lastKnownPosition = position;
            hasLastKnownPosition = true;
        }

        public void ClearLastKnownPosition() => hasLastKnownPosition = false;
    }

    /// <summary>
    /// Reusable ranged attack implementation.
    /// </summary>
    [System.Serializable]
    public class RangedAttackBehavior
    {
        [SerializeField] private float attackDuration = 0.5f;
        [SerializeField] private float damage = 10f;
        [SerializeField] private float projectileSpeed = 10f;
        [SerializeField] private float attackRange = 10f;
        [SerializeField] private float attackCooldown = 2f;
        [SerializeField] private GameObject projectilePrefab;
        [SerializeField] private Transform spawnPoint;
        [SerializeField] private GameObject vfxPrefab;

        private float lastAttackTime = float.NegativeInfinity;

        public float RangedAttackDuration => attackDuration;
        public float RangedDamage => damage;
        public float ProjectileSpeed => projectileSpeed;
        public float RangedAttackRange => attackRange;
        public GameObject ProjectilePrefab => projectilePrefab;
        public Transform ProjectileSpawnPoint => spawnPoint;
        public GameObject RangedVFXPrefab => vfxPrefab;

        public bool CanAttack => Time.time >= lastAttackTime + attackCooldown;
        public void RecordAttack() => lastAttackTime = Time.time;
        public void SetSpawnPoint(Transform point) => spawnPoint = point;
    }
}