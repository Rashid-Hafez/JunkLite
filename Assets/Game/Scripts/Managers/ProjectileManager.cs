using UnityEngine;
using System;
using System.Collections.Generic;

namespace junklite
{
    public class ProjectileManager : MonoBehaviour
    {
        public static ProjectileManager Instance { get; private set; }

        [Header("Pool Settings")]
        [Tooltip("How many objects to pre-warm per prefab. Set to 0 to disable.")]
        [SerializeField] private int prewarmCount = 10;

        [Tooltip("Parent transform for pooled objects. Keeps the hierarchy clean.")]
        [SerializeField] private Transform poolParent;

        // Generic per-prefab pool. Stores inactive GameObjects keyed by their prefab.
        private readonly Dictionary<GameObject, Queue<GameObject>> pools = new();

        // =====================================================================
        // LIFECYCLE
        // =====================================================================

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }

            Instance = this;

            if (poolParent == null)
            {
                var poolGo = new GameObject("ProjectilePool");
                poolGo.transform.SetParent(transform);
                poolParent = poolGo.transform;
            }
        }

        private void OnDestroy()
        {
            if (Instance == this)
                Instance = null;
        }

        // =====================================================================
        // PREWARM
        // =====================================================================

        public void PrewarmPool(GameObject prefab, int count)
        {
            if (prefab == null) return;

            if (!pools.ContainsKey(prefab))
                pools[prefab] = new Queue<GameObject>();

            for (int i = 0; i < count; i++)
            {
                var go = Instantiate(prefab, poolParent);
                go.SetActive(false);
                pools[prefab].Enqueue(go);
            }
        }

        // =====================================================================
        // BULLET (legacy — kept for future projectile weapons)
        // =====================================================================

        public Bullet FireBullet(
            GameObject prefab,
            BulletConfig config,
            LayerMask enemyLayer,
            LayerMask environmentLayer,
            Action<Collider> onEnemyHit)
        {
            if (prefab == null)
            {
                Debug.LogWarning("[ProjectileManager] FireBullet called with null prefab.");
                return null;
            }

            var go = GetFromPool(prefab);
            var bullet = go.GetComponent<Bullet>();

            if (bullet == null)
            {
                Debug.LogError($"[ProjectileManager] Prefab '{prefab.name}' missing Bullet component!");
                bullet = go.AddComponent<Bullet>();
            }

            go.SetActive(true);
            bullet.Initialize(
                config,
                enemyLayer,
                environmentLayer,
                onEnemyHit,
                () => ReturnToPool(prefab, go));

            return bullet;
        }

        // =====================================================================
        // HITSCAN TRACER
        // =====================================================================

        public HitscanTracer FireTracer(
            GameObject prefab,
            Vector3 from,
            Vector3 to,
            float fadeDuration)
        {
            if (prefab == null) return null;

            var go = GetFromPool(prefab);
            var tracer = go.GetComponent<HitscanTracer>();

            if (tracer == null)
            {
                Debug.LogError($"[ProjectileManager] Prefab '{prefab.name}' missing HitscanTracer component!");
                tracer = go.AddComponent<HitscanTracer>();
            }

            go.SetActive(true);
            tracer.Initialize(from, to, fadeDuration, () => ReturnToPool(prefab, go));

            return tracer;
        }

        // =====================================================================
        // GENERIC POOL
        // =====================================================================

        private GameObject GetFromPool(GameObject prefab)
        {
            if (!pools.ContainsKey(prefab))
                pools[prefab] = new Queue<GameObject>();

            var pool = pools[prefab];

            while (pool.Count > 0)
            {
                var candidate = pool.Dequeue();
                if (candidate != null && !candidate.activeSelf)
                    return candidate;
            }

            // Pool exhausted — instantiate new
            var go = Instantiate(prefab, poolParent);
            go.SetActive(false);
            return go;
        }

        private void ReturnToPool(GameObject prefab, GameObject instance)
        {
            if (instance == null) return;

            instance.SetActive(false);

            if (!pools.ContainsKey(prefab))
                pools[prefab] = new Queue<GameObject>();

            pools[prefab].Enqueue(instance);
        }
    }
}