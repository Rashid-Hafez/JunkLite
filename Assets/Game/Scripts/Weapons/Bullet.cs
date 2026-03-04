using UnityEngine;
using System;

namespace junklite
{
    public struct BulletConfig
    {
        public Vector3 spawnPosition;
        public Vector3 direction;
        public float speed;
        public float radius;
        public float damage;
        public Vector2 knockback;
        public GameObject owner;
    }


    [RequireComponent(typeof(SpriteRenderer))]
    public class Bullet : MonoBehaviour
    {
        private BulletConfig config;
        private LayerMask enemyLayer;
        private LayerMask environmentLayer;

        // Called by WeaponManager when an enemy is hit.
        // Passes the hit collider so WeaponManager can resolve IDamageable,
        // spawn VFX, fire events, etc.
        private Action<Collider> onEnemyHit;

        // Called when the bullet is done (hit something or left the screen).
        // Wired up by ProjectileManager to re-enqueue and disable.
        private Action onReturn;

        private Vector3 lastPosition;
        private bool isActive;

        // =====================================================================
        // INITIALIZATION
        // Called by ProjectileManager immediately after retrieving from pool.
        // =====================================================================

        public void Initialize(
            BulletConfig cfg,
            LayerMask enemy,
            LayerMask environment,
            Action<Collider> enemyHitCallback,
            Action returnCallback)
        {
            config = cfg;
            enemyLayer = enemy;
            environmentLayer = environment;
            onEnemyHit = enemyHitCallback;
            onReturn = returnCallback;

            transform.position = cfg.spawnPosition;
            lastPosition = cfg.spawnPosition;
            isActive = true;

            // Rotate sprite to face movement direction
            if (cfg.direction != Vector3.zero)
            {
                float angle = Mathf.Atan2(cfg.direction.y, cfg.direction.x) * Mathf.Rad2Deg;
                transform.rotation = Quaternion.Euler(0f, 0f, angle);
            }
        }

        // =====================================================================
        // UPDATE
        // =====================================================================

        private void Update()
        {
            if (!isActive) return;

            float distanceThisFrame = config.speed * Time.deltaTime;

            // Continuous SphereCast: cast from last position along movement direction
            // for exactly the distance we're about to move. Catches any collider in
            // the swept volume regardless of bullet speed.
            if (Physics.SphereCast(
                lastPosition,
                config.radius,
                config.direction,
                out RaycastHit hit,
                distanceThisFrame,
                enemyLayer | environmentLayer,
                QueryTriggerInteraction.Ignore))
            {
                int hitMask = 1 << hit.collider.gameObject.layer;

                if ((hitMask & enemyLayer) != 0)
                {
                    // Enemy hit — let WeaponManager handle damage/VFX/events via callback
                    onEnemyHit?.Invoke(hit.collider);

                    // Spawn hit VFX at contact point
                    if (CombatEffectsManager.Instance != null)
                    {
                        Vector3 hitDir = -config.direction;
                        CombatEffectsManager.Instance.SpawnEnemyHitVFX(hit.point, hitDir);
                        CombatEffectsManager.Instance.SpawnEnemyHurtParticle(hit.point, hitDir);
                    }
                }
                else if ((hitMask & environmentLayer) != 0)
                {
                    // Environment hit — spawn impact VFX directly, no game logic needed
                    if (CombatEffectsManager.Instance != null)
                    {
                        CombatEffectsManager.Instance.SpawnEnvHitParticle(hit.point, hit.normal);
                        CombatEffectsManager.Instance.SpawnHitCross(hit.point);
                    }
                }

                ReturnToPool();
                return;
            }

            // No hit this frame — advance position
            transform.position += config.direction * distanceThisFrame;
            lastPosition = transform.position;

            // Screen boundary check — return to pool when bullet leaves the camera view
            if (Camera.main != null)
            {
                Vector3 viewport = Camera.main.WorldToViewportPoint(transform.position);
                if (viewport.x < 0f || viewport.x > 1f || viewport.y < 0f || viewport.y > 1f || viewport.z < 0f)
                    ReturnToPool();
            }
        }

        // =====================================================================
        // POOL RETURN
        // =====================================================================

        private void ReturnToPool()
        {
            isActive = false;
            onReturn?.Invoke();
        }
    }
}