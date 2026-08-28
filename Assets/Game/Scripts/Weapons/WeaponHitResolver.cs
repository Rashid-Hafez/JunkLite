using System.Collections.Generic;
using UnityEngine;

namespace junklite
{
    internal readonly struct WeaponHitDetectionResult
    {
        public AttackHitResult Type { get; }
        public Collider Target { get; }
        public Collider[] AllTargets { get; }
        public Vector3 Point { get; }

        public WeaponHitDetectionResult(
            AttackHitResult type,
            Collider target = null,
            Collider[] allTargets = null,
            Vector3 point = default)
        {
            Type = type;
            Target = target;
            AllTargets = allTargets;
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

        public WeaponHitResolver(LayerMask enemyLayer, LayerMask environmentLayer)
        {
            this.enemyLayer = enemyLayer;
            this.environmentLayer = environmentLayer;
        }

        public WeaponHitDetectionResult Detect(Vector3 origin, float radius, bool piercing)
        {
            Collider[] hits = Physics.OverlapSphere(
                origin,
                radius,
                enemyLayer | environmentLayer,
                QueryTriggerInteraction.Ignore);

            Collider closestEnemy = null;
            float closestDistance = float.MaxValue;
            bool hitEnvironment = false;
            List<Collider> enemyHits = piercing ? new List<Collider>() : null;

            for (int i = 0; i < hits.Length; i++)
            {
                Collider hit = hits[i];
                int layerMask = 1 << hit.gameObject.layer;

                if ((layerMask & enemyLayer) != 0)
                {
                    if (piercing)
                    {
                        enemyHits.Add(hit);
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

            if (piercing && enemyHits != null && enemyHits.Count > 0)
            {
                return new WeaponHitDetectionResult(
                    AttackHitResult.Enemy,
                    allTargets: enemyHits.ToArray(),
                    point: enemyHits[0].ClosestPoint(origin));
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
    }
}
