using UnityEngine;

namespace junklite
{
    internal readonly struct WeaponHitDetectionResult
    {
        public AttackHitResult Type { get; }
        public Collider Target { get; }
        public Collider[] AllTargets { get; }
        public int AllTargetCount { get; }
        public Vector3 Point { get; }

        public WeaponHitDetectionResult(
            AttackHitResult type,
            Collider target = null,
            Collider[] allTargets = null,
            int allTargetCount = 0,
            Vector3 point = default)
        {
            Type = type;
            Target = target;
            AllTargets = allTargets;
            AllTargetCount = allTargetCount;
            Point = point;
        }
    }

    /// <summary>
    /// Performs weapon overlap queries and classifies the result. It does not apply
    /// damage, consume durability or trigger presentation.
    /// </summary>
    internal sealed class WeaponHitResolver
    {
        private readonly LayerMask enemyLayer;
        private readonly LayerMask environmentLayer;
        private Collider[] queryBuffer = new Collider[64];
        private Collider[] enemyBuffer = new Collider[64];
        private const int MaxQueryCapacity = 512;

        public WeaponHitResolver(LayerMask enemyLayer, LayerMask environmentLayer)
        {
            this.enemyLayer = enemyLayer;
            this.environmentLayer = environmentLayer;
        }

        public WeaponHitDetectionResult Detect(Vector3 origin, float radius, bool piercing)
        {
            int hitCount = QueryOverlaps(origin, radius);

            Collider closestEnemy = null;
            float closestDistance = float.MaxValue;
            bool hitEnvironment = false;
            int enemyCount = 0;

            for (int i = 0; i < hitCount; i++)
            {
                Collider hit = queryBuffer[i];
                if (hit == null)
                    continue;
                int layerMask = 1 << hit.gameObject.layer;

                if ((layerMask & enemyLayer) != 0)
                {
                    if (piercing)
                    {
                        enemyBuffer[enemyCount++] = hit;
                        continue;
                    }

                    float distance = Vector3.Distance(origin, hit.transform.position);
                    if (distance < closestDistance)
                    {
                        closestDistance = distance;
                        closestEnemy = hit;
                    }
                }
                else if ((layerMask & environmentLayer) != 0)
                {
                    hitEnvironment = true;
                }
            }

            if (piercing && enemyCount > 0)
            {
                return new WeaponHitDetectionResult(
                    AttackHitResult.Enemy,
                    allTargets: enemyBuffer,
                    allTargetCount: enemyCount,
                    point: enemyBuffer[0].ClosestPoint(origin));
            }

            if (!piercing && closestEnemy != null)
            {
                return new WeaponHitDetectionResult(
                    AttackHitResult.Enemy,
                    target: closestEnemy,
                    point: closestEnemy.ClosestPoint(origin));
            }

            return hitEnvironment
                ? new WeaponHitDetectionResult(AttackHitResult.Environment)
                : new WeaponHitDetectionResult(AttackHitResult.None);
        }

        private int QueryOverlaps(Vector3 origin, float radius)
        {
            while (true)
            {
                int count = Physics.OverlapSphereNonAlloc(
                    origin,
                    radius,
                    queryBuffer,
                    enemyLayer | environmentLayer,
                    QueryTriggerInteraction.Ignore);

                if (count < queryBuffer.Length || queryBuffer.Length >= MaxQueryCapacity)
                {
                    EnsureEnemyCapacity(queryBuffer.Length);
                    return count;
                }

                int nextCapacity = Mathf.Min(queryBuffer.Length * 2, MaxQueryCapacity);
                queryBuffer = new Collider[nextCapacity];
            }
        }

        private void EnsureEnemyCapacity(int capacity)
        {
            if (enemyBuffer.Length < capacity)
                enemyBuffer = new Collider[capacity];
        }
    }
}
