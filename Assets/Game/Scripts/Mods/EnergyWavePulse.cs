using UnityEngine;
using System.Collections.Generic;

namespace junklite
{
    /// <summary>
    /// Energy wave projectile that travels in a straight line.
    /// Uses OverlapSphere each frame for reliable enemy detection.
    /// Each enemy is damaged only once per wave via HashSet tracking.
    /// Self-destructs after reaching max distance.
    /// </summary>
    public class EnergyWavePulse : MonoBehaviour
    {
        #region Fields

        private Vector3 direction;
        private float speed;
        private float maxDistance;
        private float damage;
        private float radius;
        private LayerMask enemyMask;
        private GameObject owner;
        private float shakeIntensity;

        private Vector3 startPosition;
        private bool initialized;

        // Track which enemies this wave already hit
        private readonly HashSet<int> hitEnemyIDs = new();
        private static readonly Collider[] scanBuffer = new Collider[16];

        #endregion

        #region Init

        public void Initialize(
            Vector3 moveDirection,
            float moveSpeed,
            float maxTravelDistance,
            float waveDamage,
            float scanRadius,
            LayerMask enemyLayerMask,
            GameObject waveOwner,
            float hitShakeIntensity)
        {
            direction = moveDirection.normalized;
            speed = moveSpeed;
            maxDistance = maxTravelDistance;
            damage = waveDamage;
            radius = scanRadius;
            enemyMask = enemyLayerMask;
            owner = waveOwner;
            shakeIntensity = hitShakeIntensity;

            startPosition = transform.position;
            initialized = true;

            // Face the wave in its travel direction
            if (direction.x < 0f)
            {
                Vector3 s = transform.localScale;
                s.x = -Mathf.Abs(s.x);
                transform.localScale = s;
            }
        }

        #endregion

        #region Update

        private void Update()
        {
            if (!initialized) return;

            // Move forward
            transform.position += direction * (speed * Time.deltaTime);

            // Scan for enemies at current position
            ScanAndDamage();

            // Check max distance
            float traveled = Vector3.Distance(startPosition, transform.position);
            if (traveled >= maxDistance)
                Destroy(gameObject);
        }

        #endregion

        #region Scan

        private void ScanAndDamage()
        {
            int count = Physics.OverlapSphereNonAlloc(
                transform.position, radius, scanBuffer, enemyMask);

            for (int i = 0; i < count; i++)
            {
                var col = scanBuffer[i];
                if (col.gameObject == owner) continue;

                // Use instance ID to track unique enemies (handles parent/child colliders)
                var enemy = col.GetComponentInParent<EnemyCharacter>();
                if (enemy == null || !enemy.IsAlive) continue;

                int id = enemy.gameObject.GetInstanceID();
                if (hitEnemyIDs.Contains(id)) continue;

                // Mark as hit BEFORE dealing damage
                hitEnemyIDs.Add(id);

                var damageable = enemy.GetComponentInParent<IDamageable>();
                if (damageable != null && damageable.IsAlive)
                {
                    bool dealt = damageable.TakeDamage(new DamageInfo(
                        damage,
                        owner,
                        DamageType.Physical,
                        Vector2.zero
                    ));

                    if (dealt)
                        SpawnHitEffects(enemy);
                }
            }
        }

        private void SpawnHitEffects(EnemyCharacter enemy)
        {
            if (CombatEffectsManager.Instance == null) return;

            var enemyCollider = enemy.GetComponent<Collider>();
            if (enemyCollider == null) return;

            Vector3 hitPoint = enemyCollider.bounds.center;
            Vector3 hitDir = direction;

            CombatEffectsManager.Instance.SpawnEnemyHitVFX(hitPoint, hitDir);
            CombatEffectsManager.Instance.SpawnEnemyHurtParticle(hitPoint, hitDir);

            if (shakeIntensity > 0f && FeedbackManager.Instance != null && owner != null)
            {
                var impulse = owner.GetComponent<Unity.Cinemachine.CinemachineImpulseSource>();
                if (impulse != null)
                    FeedbackManager.Instance.DoCameraShake(impulse, shakeIntensity);
            }
        }

        #endregion

        #region Debug

        private void OnDrawGizmosSelected()
        {
            Gizmos.color = Color.cyan;
            Gizmos.DrawWireSphere(transform.position, radius > 0f ? radius : 1f);
        }

        #endregion
    }
}