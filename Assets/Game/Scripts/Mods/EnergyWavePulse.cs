using UnityEngine;
using System.Collections.Generic;

namespace junklite
{

    public class EnergyWavePulse : MonoBehaviour
    {
        #region Fields

        private Vector3 direction;
        private float speed;
        private float lifetime;
        private float tickDamage;
        private float tickInterval;
        private float radius;
        private LayerMask enemyMask;
        private GameObject owner;
        private float shakeIntensity;
        private float dragRatio;

        private float elapsed;
        private bool initialized;
        private bool released;

        // Track which enemies were already captured (one capture per enemy)
        private readonly HashSet<int> hitEnemyIDs = new();

        // Enemies currently being dragged
        private readonly List<DraggedEnemy> draggedEnemies = new();

        private static readonly Collider[] scanBuffer = new Collider[16];

        #endregion

        #region Drag Data

        private struct DraggedEnemy
        {
            public EnemyCharacter Enemy;
            public Vector3 Offset; // offset from pulse center when captured
            public float LastDamageTick; // Time.time of last DOT tick
        }

        #endregion

        #region Init

        public void Initialize(
            Vector3 moveDirection,
            float moveSpeed,
            float pulseLifetime,
            float damagePerTick,
            float damageInterval,
            float scanRadius,
            LayerMask enemyLayerMask,
            GameObject pulseOwner,
            float hitShakeIntensity,
            float dragDurationRatio)
        {
            direction = moveDirection.normalized;
            speed = moveSpeed;
            lifetime = pulseLifetime;
            tickDamage = damagePerTick;
            tickInterval = damageInterval;
            radius = scanRadius;
            enemyMask = enemyLayerMask;
            owner = pulseOwner;
            shakeIntensity = hitShakeIntensity;
            dragRatio = Mathf.Clamp01(dragDurationRatio);

            elapsed = 0f;
            initialized = true;
            released = false;

            // Face the pulse in its travel direction
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

            elapsed += Time.deltaTime;

            // Lifetime expired
            if (elapsed >= lifetime)
            {
                ReleaseAll();
                Destroy(gameObject);
                return;
            }

            // Move forward
            transform.position += direction * (speed * Time.deltaTime);

            float progress = elapsed / lifetime;
            bool inActivePhase = progress < dragRatio;

            if (inActivePhase)
            {
                ScanAndCapture();
                DragCapturedEnemies();
            }
            else if (!released)
            {
                // Transition: release all dragged enemies once
                ReleaseAll();
            }
        }

        #endregion

        #region Scan & Capture

        private void ScanAndCapture()
        {
            int count = Physics.OverlapSphereNonAlloc(
                transform.position, radius, scanBuffer, enemyMask,
                QueryTriggerInteraction.Ignore);

            for (int i = 0; i < count; i++)
            {
                var col = scanBuffer[i];
                if (col.gameObject == owner) continue;

                var enemy = col.GetComponentInParent<EnemyCharacter>();
                if (enemy == null || !enemy.IsAlive) continue;

                int id = enemy.gameObject.GetInstanceID();
                if (hitEnemyIDs.Contains(id)) continue;

                // First contact: damage + capture
                hitEnemyIDs.Add(id);

                // Deal damage
                var damageable = enemy.GetComponentInParent<IDamageable>();
                if (damageable != null && damageable.IsAlive)
                {
                    bool dealt = damageable.TakeDamage(new DamageInfo(
                        tickDamage,
                        owner,
                        DamageType.Physical,
                        Vector2.zero
                    ));

                    if (dealt)
                        SpawnHitEffects(enemy);
                }

                // Capture for dragging
                CaptureEnemy(enemy);
            }
        }

        private void CaptureEnemy(EnemyCharacter enemy)
        {
            // Store offset so enemies don't all stack on the same point
            Vector3 offset = enemy.transform.position - transform.position;

            draggedEnemies.Add(new DraggedEnemy
            {
                Enemy = enemy,
                Offset = offset,
                LastDamageTick = Time.time
            });

            // Disable enemy movement/AI so we can control their position
            var rb = enemy.GetComponent<Rigidbody>();
            if (rb != null)
            {
                rb.isKinematic = true;
                rb.linearVelocity = Vector3.zero;
            }

            var nav = enemy.GetComponent<UnityEngine.AI.NavMeshAgent>();
            if (nav != null && nav.enabled)
                nav.enabled = false;
        }

        #endregion

        #region Drag

        private void DragCapturedEnemies()
        {
            for (int i = draggedEnemies.Count - 1; i >= 0; i--)
            {
                var entry = draggedEnemies[i];

                // Enemy died or got destroyed while dragged
                if (entry.Enemy == null || !entry.Enemy.IsAlive)
                {
                    draggedEnemies.RemoveAt(i);
                    continue;
                }

                // Move enemy with the pulse, preserving their relative offset
                entry.Enemy.transform.position = transform.position + entry.Offset;

                // DOT tick
                if (tickInterval > 0f && Time.time >= entry.LastDamageTick + tickInterval)
                {
                    entry.LastDamageTick = Time.time;
                    draggedEnemies[i] = entry; // write back updated tick time

                    var damageable = entry.Enemy.GetComponentInParent<IDamageable>();
                    if (damageable != null && damageable.IsAlive)
                    {
                        bool dealt = damageable.TakeDamage(new DamageInfo(
                            tickDamage,
                            owner,
                            DamageType.Physical,
                            Vector2.zero
                        ));

                        if (dealt)
                            SpawnHitEffects(entry.Enemy);
                    }
                }
            }
        }

        #endregion

        #region Release

        private void ReleaseAll()
        {
            if (released) return;
            released = true;

            for (int i = 0; i < draggedEnemies.Count; i++)
            {
                var entry = draggedEnemies[i];
                if (entry.Enemy == null) continue;

                ReleaseEnemy(entry.Enemy);
            }

            draggedEnemies.Clear();
        }

        private void ReleaseEnemy(EnemyCharacter enemy)
        {
            var rb = enemy.GetComponent<Rigidbody>();
            if (rb != null)
                rb.isKinematic = false;

            var nav = enemy.GetComponent<UnityEngine.AI.NavMeshAgent>();
            if (nav != null)
                nav.enabled = true;
        }

        #endregion

        #region Hit Effects

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

        #region Cleanup

        private void OnDestroy()
        {
            // Safety net: release anyone still dragged if pulse is destroyed early
            if (!released)
                ReleaseAll();
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